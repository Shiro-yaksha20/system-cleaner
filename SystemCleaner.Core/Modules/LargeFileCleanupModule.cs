using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SystemCleaner.Core.Abstractions;
using SystemCleaner.Core.Models;
using SystemCleaner.Core.Utilities;

namespace SystemCleaner.Core.Modules;

public sealed class LargeFileCleanupModule : ICleanupModule
{
    private readonly IReadOnlyList<LargeFileScanRule> _rules;

    public LargeFileCleanupModule(CleanupModuleInfo moduleInfo, IEnumerable<LargeFileScanRule> rules)
    {
        ModuleInfo = moduleInfo ?? throw new ArgumentNullException(nameof(moduleInfo));
        _rules = rules?.ToArray() ?? throw new ArgumentNullException(nameof(rules));
    }

    public CleanupModuleInfo ModuleInfo { get; }

    public Task<CleanupScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var items = new List<CleanupItem>();

            foreach (var rule in _rules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var root = SafeResolve(rule.RootPathResolver);
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    continue;
                }

                var candidates = FindLargeFiles(root!, rule.MinimumSizeBytes, rule.MaxResults, cancellationToken);
                foreach (var file in candidates)
                {
                    var displayName = $"{rule.DisplayName}: {file.Name}";
                    items.Add(new CleanupItem(ModuleInfo, displayName, file.FullName, file.Length, CleanupItemType.File));
                }
            }

            var ordered = items
                .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(item => item.SizeBytes)
                .Take(100)
                .ToArray();

            return new CleanupScanResult(ModuleInfo, ordered);
        }, cancellationToken);
    }

    public Task<CleanupExecutionResult> CleanAsync(IEnumerable<CleanupItem> items, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var results = new List<CleanupItemResult>();
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.Equals(item.Module.Id, ModuleInfo.Id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var result = FileSystemHelper.CleanFileItem(item);
                results.Add(result);
            }

            return new CleanupExecutionResult(ModuleInfo, results);
        }, cancellationToken);
    }

    private static IEnumerable<FileInfo> FindLargeFiles(string root, long minimumSizeBytes, int maxResults, CancellationToken cancellationToken)
    {
        var results = new List<FileInfo>();
        var stack = new Stack<DirectoryInfo>();
        stack.Push(new DirectoryInfo(root));

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = stack.Pop();
            FileInfo[] files;
            try
            {
                files = directory.GetFiles();
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                if (file.Length < minimumSizeBytes)
                {
                    continue;
                }

                results.Add(file);
            }

            DirectoryInfo[] subDirectories;
            try
            {
                subDirectories = directory.GetDirectories();
            }
            catch
            {
                continue;
            }

            foreach (var subDirectory in subDirectories)
            {
                stack.Push(subDirectory);
            }
        }

        return results
            .OrderByDescending(file => file.Length)
            .ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToArray();
    }

    private static string? SafeResolve(Func<string?> resolver)
    {
        try
        {
            return resolver.Invoke();
        }
        catch
        {
            return null;
        }
    }
}

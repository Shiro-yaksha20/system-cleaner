using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using SystemCleaner.Core.Abstractions;
using SystemCleaner.Core.Models;
using SystemCleaner.Core.Utilities;

namespace SystemCleaner.Core.Modules;

public sealed class DuplicateCleanupModule : ICleanupModule
{
    private readonly IReadOnlyList<DuplicateScanRule> _rules;

    public DuplicateCleanupModule(CleanupModuleInfo moduleInfo, IEnumerable<DuplicateScanRule> rules)
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

                var duplicates = FindDuplicates(root!, rule.MinimumSizeBytes, cancellationToken);
                foreach (var duplicate in duplicates)
                {
                    var displayName = $"{rule.DisplayName}: {duplicate.Name}";
                    items.Add(new CleanupItem(ModuleInfo, displayName, duplicate.FullName, duplicate.Length, CleanupItemType.File));
                }
            }

            var ordered = items
                .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(item => item.SizeBytes)
                .Take(200)
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

                results.Add(FileSystemHelper.CleanFileItem(item));
            }

            return new CleanupExecutionResult(ModuleInfo, results);
        }, cancellationToken);
    }

    private static IEnumerable<FileInfo> FindDuplicates(string root, long minimumSizeBytes, CancellationToken cancellationToken)
    {
        var filesBySize = new Dictionary<long, List<FileInfo>>();
        foreach (var file in EnumerateFiles(root, cancellationToken))
        {
            if (file.Length < minimumSizeBytes)
            {
                continue;
            }

            if (!filesBySize.TryGetValue(file.Length, out var list))
            {
                list = new List<FileInfo>();
                filesBySize[file.Length] = list;
            }

            list.Add(file);
        }

        var duplicates = new List<FileInfo>();
        using var sha256 = SHA256.Create();

        foreach (var group in filesBySize.Values.Where(g => g.Count > 1))
        {
            var hashes = new Dictionary<string, List<FileInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in group)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string hash;
                try
                {
                    using var stream = File.OpenRead(file.FullName);
                    hash = Convert.ToHexString(sha256.ComputeHash(stream));
                }
                catch
                {
                    continue;
                }

                if (!hashes.TryGetValue(hash, out var list))
                {
                    list = new List<FileInfo>();
                    hashes[hash] = list;
                }

                list.Add(file);
            }

            foreach (var hashGroup in hashes.Values.Where(list => list.Count > 1))
            {
                foreach (var duplicate in hashGroup.Skip(1))
                {
                    duplicates.Add(duplicate);
                }
            }
        }

        return duplicates;
    }

    private static IEnumerable<FileInfo> EnumerateFiles(string root, CancellationToken cancellationToken)
    {
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
                yield return file;
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

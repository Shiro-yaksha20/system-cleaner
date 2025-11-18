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

public sealed class DirectoryCleanupModule : ICleanupModule
{
    private readonly IReadOnlyList<DirectoryCleanupRule> _rules;

    public DirectoryCleanupModule(CleanupModuleInfo moduleInfo, IEnumerable<DirectoryCleanupRule> rules)
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
                var path = SafeResolvePath(rule);
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                if (!Directory.Exists(path))
                {
                    continue;
                }

                var issues = new List<string>();
                var size = FileSystemHelper.CalculateDirectorySize(path, cancellationToken, issues);

                if (size <= 0)
                {
                    continue;
                }

                var item = new CleanupItem(ModuleInfo, rule.DisplayName, path, size, CleanupItemType.Directory, rule.RequiresElevation);
                items.Add(item);
            }

            return new CleanupScanResult(ModuleInfo, items);
        }, cancellationToken);
    }

    public Task<CleanupExecutionResult> CleanAsync(IEnumerable<CleanupItem> items, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var results = new List<CleanupItemResult>();

            foreach (var item in items.Where(static item => item.ItemType == CleanupItemType.Directory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.Equals(item.Module.Id, ModuleInfo.Id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var result = FileSystemHelper.CleanDirectoryItem(item, cancellationToken);
                results.Add(result);
            }

            return new CleanupExecutionResult(ModuleInfo, results);
        }, cancellationToken);
    }

    private static string? SafeResolvePath(DirectoryCleanupRule rule)
    {
        try
        {
            return rule.PathResolver.Invoke();
        }
        catch
        {
            return null;
        }
    }
}

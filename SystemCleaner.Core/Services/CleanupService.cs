using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SystemCleaner.Core.Abstractions;
using SystemCleaner.Core.Models;

namespace SystemCleaner.Core.Services;

public sealed class CleanupService : ICleanupService
{
    private readonly IReadOnlyList<ICleanupModule> _modules;

    public CleanupService(IEnumerable<ICleanupModule> modules)
    {
        _modules = modules?.ToArray() ?? throw new ArgumentNullException(nameof(modules));
    }

    public IReadOnlyList<ICleanupModule> Modules => _modules;

    public async Task<IReadOnlyList<CleanupScanResult>> ScanAsync(IProgress<CleanupProgressUpdate>? progress = null, CancellationToken cancellationToken = default)
    {
        var results = new List<CleanupScanResult>();

        for (var index = 0; index < _modules.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var module = _modules[index];
            progress?.Report(new CleanupProgressUpdate($"Scanning {module.ModuleInfo.Name}...", index, _modules.Count));
            var result = await module.ScanAsync(cancellationToken).ConfigureAwait(false);
            if (result.Items.Count > 0)
            {
                results.Add(result);
            }
        }

        progress?.Report(new CleanupProgressUpdate("Scan completed", _modules.Count, _modules.Count));
        return results;
    }

    public async Task<CleanupBatchResult> CleanAsync(IEnumerable<CleanupItem> items, IProgress<CleanupProgressUpdate>? progress = null, CancellationToken cancellationToken = default)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        var itemsByModule = items.GroupBy(static item => item.Module.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        var moduleResults = new List<CleanupExecutionResult>();

        for (var index = 0; index < itemsByModule.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var group = itemsByModule[index];
            var module = _modules.FirstOrDefault(m => string.Equals(m.ModuleInfo.Id, group.Key, StringComparison.OrdinalIgnoreCase));
            if (module is null)
            {
                continue;
            }

            progress?.Report(new CleanupProgressUpdate($"Cleaning {module.ModuleInfo.Name}...", index, itemsByModule.Length));
            var result = await module.CleanAsync(group, cancellationToken).ConfigureAwait(false);
            moduleResults.Add(result);
        }

        progress?.Report(new CleanupProgressUpdate("Cleanup completed", itemsByModule.Length, itemsByModule.Length));
        return new CleanupBatchResult(moduleResults);
    }
}

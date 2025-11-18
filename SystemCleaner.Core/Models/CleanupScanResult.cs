using System;
using System.Linq;

namespace SystemCleaner.Core.Models;

public sealed class CleanupScanResult
{
    public CleanupScanResult(CleanupModuleInfo module, IReadOnlyList<CleanupItem> items)
    {
        Module = module ?? throw new ArgumentNullException(nameof(module));
        Items = items ?? throw new ArgumentNullException(nameof(items));
    }

    public CleanupModuleInfo Module { get; }

    public IReadOnlyList<CleanupItem> Items { get; }

    public long TotalSizeBytes => Items.Sum(static item => item.SizeBytes);
}

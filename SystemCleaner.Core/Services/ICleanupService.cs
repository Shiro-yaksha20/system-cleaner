using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SystemCleaner.Core.Abstractions;
using SystemCleaner.Core.Models;

namespace SystemCleaner.Core.Services;

public interface ICleanupService
{
    IReadOnlyList<ICleanupModule> Modules { get; }

    Task<IReadOnlyList<CleanupScanResult>> ScanAsync(IProgress<CleanupProgressUpdate>? progress = null, CancellationToken cancellationToken = default);

    Task<CleanupBatchResult> CleanAsync(IEnumerable<CleanupItem> items, IProgress<CleanupProgressUpdate>? progress = null, CancellationToken cancellationToken = default);
}

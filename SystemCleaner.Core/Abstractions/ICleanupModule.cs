using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SystemCleaner.Core.Models;

namespace SystemCleaner.Core.Abstractions;

public interface ICleanupModule
{
    CleanupModuleInfo ModuleInfo { get; }

    Task<CleanupScanResult> ScanAsync(CancellationToken cancellationToken = default);

    Task<CleanupExecutionResult> CleanAsync(IEnumerable<CleanupItem> items, CancellationToken cancellationToken = default);
}

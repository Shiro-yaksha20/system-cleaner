using System;
using System.Linq;

namespace SystemCleaner.Core.Models;

public sealed class CleanupExecutionResult
{
    public CleanupExecutionResult(CleanupModuleInfo module, IReadOnlyList<CleanupItemResult> itemResults)
    {
        Module = module ?? throw new ArgumentNullException(nameof(module));
        ItemResults = itemResults ?? throw new ArgumentNullException(nameof(itemResults));
    }

    public CleanupModuleInfo Module { get; }

    public IReadOnlyList<CleanupItemResult> ItemResults { get; }

    public long BytesFreed => ItemResults.Where(static result => result.Succeeded)
        .Sum(static result => result.BytesFreed);
}

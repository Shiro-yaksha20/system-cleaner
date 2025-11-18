using System;
using System.Collections.Generic;
using System.Linq;

namespace SystemCleaner.Core.Models;

public sealed class CleanupBatchResult
{
    public CleanupBatchResult(IReadOnlyList<CleanupExecutionResult> moduleResults)
    {
        ModuleResults = moduleResults ?? throw new ArgumentNullException(nameof(moduleResults));
        TotalBytesFreed = ModuleResults.Sum(static result => result.BytesFreed);
        Failures = ModuleResults
            .SelectMany(static result => result.ItemResults.Where(static item => !item.Succeeded))
            .ToArray();
    }

    public IReadOnlyList<CleanupExecutionResult> ModuleResults { get; }

    public long TotalBytesFreed { get; }

    public IReadOnlyList<CleanupItemResult> Failures { get; }
}

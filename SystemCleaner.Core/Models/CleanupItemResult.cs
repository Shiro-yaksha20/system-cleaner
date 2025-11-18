using System;

namespace SystemCleaner.Core.Models;

public sealed class CleanupItemResult
{
    public CleanupItemResult(CleanupItem item, bool succeeded, long bytesFreed, IReadOnlyList<string> issues)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        Succeeded = succeeded;
        BytesFreed = bytesFreed;
        Issues = issues ?? throw new ArgumentNullException(nameof(issues));
    }

    public CleanupItem Item { get; }

    public bool Succeeded { get; }

    public long BytesFreed { get; }

    public IReadOnlyList<string> Issues { get; }
}

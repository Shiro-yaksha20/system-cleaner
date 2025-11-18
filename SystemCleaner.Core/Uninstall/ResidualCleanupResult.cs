using System;
using System.Collections.Generic;

namespace SystemCleaner.Core.Uninstall;

public sealed class ResidualCleanupResult
{
    public ResidualCleanupResult(int removed, int failed, IReadOnlyList<string> messages)
    {
        Removed = removed;
        Failed = failed;
        Messages = messages ?? Array.Empty<string>();
    }

    public int Removed { get; }

    public int Failed { get; }

    public IReadOnlyList<string> Messages { get; }
}

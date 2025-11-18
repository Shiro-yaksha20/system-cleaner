using System.Collections.Generic;
using SystemCleaner.Core.Models;

namespace SystemCleaner.Core.Startup;

public sealed class StartupDiscoveryResult
{
    public StartupDiscoveryResult(IReadOnlyList<StartupEntry> entries, IReadOnlyList<string> issues)
    {
        Entries = entries;
        Issues = issues;
    }

    public IReadOnlyList<StartupEntry> Entries { get; }

    public IReadOnlyList<string> Issues { get; }
}

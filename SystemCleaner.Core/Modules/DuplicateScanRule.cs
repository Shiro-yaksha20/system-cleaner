using System;

namespace SystemCleaner.Core.Modules;

public sealed class DuplicateScanRule
{
    public DuplicateScanRule(string displayName, Func<string?> rootPathResolver, long minimumSizeBytes = 10 * 1024 * 1024)
    {
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        RootPathResolver = rootPathResolver ?? throw new ArgumentNullException(nameof(rootPathResolver));
        MinimumSizeBytes = minimumSizeBytes;
    }

    public string DisplayName { get; }

    public Func<string?> RootPathResolver { get; }

    public long MinimumSizeBytes { get; }
}

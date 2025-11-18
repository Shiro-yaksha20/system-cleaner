using System;

namespace SystemCleaner.Core.Modules;

public sealed class LargeFileScanRule
{
    public LargeFileScanRule(string displayName, Func<string?> rootPathResolver, long minimumSizeBytes = 100 * 1024 * 1024, int maxResults = 20)
    {
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        RootPathResolver = rootPathResolver ?? throw new ArgumentNullException(nameof(rootPathResolver));
        MinimumSizeBytes = minimumSizeBytes;
        MaxResults = maxResults;
    }

    public string DisplayName { get; }

    public Func<string?> RootPathResolver { get; }

    public long MinimumSizeBytes { get; }

    public int MaxResults { get; }
}

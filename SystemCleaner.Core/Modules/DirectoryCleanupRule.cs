using System;

namespace SystemCleaner.Core.Modules;

public sealed class DirectoryCleanupRule
{
    public DirectoryCleanupRule(string displayName, Func<string?> pathResolver, bool requiresElevation = false)
    {
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        PathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        RequiresElevation = requiresElevation;
    }

    public string DisplayName { get; }

    public Func<string?> PathResolver { get; }

    public bool RequiresElevation { get; }
}

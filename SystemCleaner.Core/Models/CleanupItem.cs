using System;

namespace SystemCleaner.Core.Models;

public sealed class CleanupItem
{
    public CleanupItem(
        CleanupModuleInfo module,
        string displayName,
        string path,
        long sizeBytes,
        CleanupItemType itemType,
        bool requiresElevation = false)
    {
        Module = module ?? throw new ArgumentNullException(nameof(module));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        Path = path ?? throw new ArgumentNullException(nameof(path));
        SizeBytes = sizeBytes;
        ItemType = itemType;
        RequiresElevation = requiresElevation;
    }

    public CleanupModuleInfo Module { get; }

    public string DisplayName { get; }

    public string Path { get; }

    public long SizeBytes { get; }

    public CleanupItemType ItemType { get; }

    public bool RequiresElevation { get; }
}

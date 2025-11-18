namespace SystemCleaner.Core.Uninstall;

public readonly record struct ResidualCleanupOptions(bool ShredFiles)
{
    public static ResidualCleanupOptions Default { get; } = new(ShredFiles: false);
}

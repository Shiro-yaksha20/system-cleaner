namespace SystemCleaner.Core.Uninstall;

public readonly record struct UninstallOptions(
	bool Force,
	bool CreateRestorePoint = false,
	ResidualCleanupMode ResidualCleanupMode = ResidualCleanupMode.None,
	bool ShredResidualFiles = false)
{
	public ResidualCleanupOptions CreateCleanupOptions() => new(ShredResidualFiles);
}

namespace SystemCleaner.Core.Models;

public sealed record CleanupModuleInfo(
	string Id,
	string Name,
	string Description,
	bool IsQuickCleanSafe = true,
	string? Warning = null);

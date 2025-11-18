namespace SystemCleaner.Core.Models;

public sealed record CleanupProgressUpdate(string Message, int CompletedSteps, int TotalSteps);

using System;

namespace SystemCleaner.App.Services;

public sealed record NotificationMessage(
    string Id,
    NotificationSeverity Severity,
    string Message,
    string? Detail,
    string? Context,
    DateTimeOffset Timestamp,
    string? CorrelationId);

public enum NotificationSeverity
{
    Info,
    Warning,
    Error
}

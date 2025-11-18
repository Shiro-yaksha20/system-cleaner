using System;

namespace SystemCleaner.App.ViewModels;

public sealed class LogEntryViewModel
{
    public LogEntryViewModel(string message, string severity)
    {
        Timestamp = DateTime.Now;
        Message = message;
        Severity = severity;
    }

    public DateTime Timestamp { get; }

    public string Message { get; }

    public string Severity { get; }

    public override string ToString() => $"[{Timestamp:HH:mm:ss}] ({Severity}) {Message}";
}

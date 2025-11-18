namespace SystemCleaner.Core.Uninstall;

public sealed class SoftwareHealthInsight
{
    public SoftwareHealthInsight(string title, string message, SoftwareHealthSeverity severity)
    {
        Title = title;
        Message = message;
        Severity = severity;
    }

    public string Title { get; }

    public string Message { get; }

    public SoftwareHealthSeverity Severity { get; }
}

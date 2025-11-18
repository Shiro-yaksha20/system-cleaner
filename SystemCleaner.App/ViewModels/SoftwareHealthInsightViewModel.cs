using SystemCleaner.Core.Uninstall;

namespace SystemCleaner.App.ViewModels;

public sealed class SoftwareHealthInsightViewModel
{
    public SoftwareHealthInsightViewModel(SoftwareHealthInsight insight)
    {
        Insight = insight;
    }

    public SoftwareHealthInsight Insight { get; }

    public string Title => Insight.Title;

    public string Message => Insight.Message;

    public SoftwareHealthSeverity Severity => Insight.Severity;
}

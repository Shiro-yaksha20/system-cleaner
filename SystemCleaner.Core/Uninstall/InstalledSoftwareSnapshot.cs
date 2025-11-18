using System.Collections.Generic;

namespace SystemCleaner.Core.Uninstall;

public sealed class InstalledSoftwareSnapshot
{
    public InstalledSoftwareSnapshot(
        IReadOnlyList<InstalledApplication> applications,
        IReadOnlyList<BrowserExtensionInfo> browserExtensions,
        IReadOnlyList<SoftwareHealthInsight> healthInsights,
        IReadOnlyList<string> issues)
    {
        Applications = applications;
        BrowserExtensions = browserExtensions;
        HealthInsights = healthInsights;
        Issues = issues;
    }

    public IReadOnlyList<InstalledApplication> Applications { get; }

    public IReadOnlyList<BrowserExtensionInfo> BrowserExtensions { get; }

    public IReadOnlyList<SoftwareHealthInsight> HealthInsights { get; }

    public IReadOnlyList<string> Issues { get; }
}

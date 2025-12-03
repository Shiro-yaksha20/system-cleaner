using SystemCleaner.App.Theming;

namespace SystemCleaner.App.Settings;

public sealed class AppSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.System;

    public bool RequireConfirmation { get; set; } = true;

    public string? VirusTotalApiKey { get; set; }

    public bool HasVirusTotalApiKey { get; set; }

    public AppSettings Clone()
    {
        return new AppSettings
        {
            Theme = Theme,
            RequireConfirmation = RequireConfirmation,
            VirusTotalApiKey = VirusTotalApiKey,
            HasVirusTotalApiKey = HasVirusTotalApiKey
        };
    }
}

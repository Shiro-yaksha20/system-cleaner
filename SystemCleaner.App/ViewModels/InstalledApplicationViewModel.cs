using System;
using SystemCleaner.App.Utilities;
using SystemCleaner.Core.Uninstall;

namespace SystemCleaner.App.ViewModels;

public sealed class InstalledApplicationViewModel : ObservableObject
{
    private bool _isSelected;

    public InstalledApplicationViewModel(InstalledApplication application)
    {
        Application = application ?? throw new ArgumentNullException(nameof(application));
    }

    public InstalledApplication Application { get; }

    public string Name => Application.Name;

    public string Publisher => string.IsNullOrWhiteSpace(Application.Publisher) ? "Unknown" : Application.Publisher;

    public string Version => string.IsNullOrWhiteSpace(Application.Version) ? "-" : Application.Version;

    public string InstallLocation => Application.InstallLocation ?? string.Empty;

    public string RegistryKeyPath => Application.RegistryKeyPath;

    public string InstallDateDisplay => Application.InstallDate?.ToString("MMM dd, yyyy") ?? "-";

    public string SizeDisplay => Application.EstimatedSizeBytes.HasValue
        ? SizeFormatter.FormatSize(Application.EstimatedSizeBytes.Value)
        : "-";

    public bool IsWindowsApp => Application.IsWindowsApp;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

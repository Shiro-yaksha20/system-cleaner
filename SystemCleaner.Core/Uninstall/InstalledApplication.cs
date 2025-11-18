using System;

namespace SystemCleaner.Core.Uninstall;

public sealed class InstalledApplication
{
    public InstalledApplication(
        string name,
        string publisher,
        string version,
        string? installLocation,
        string? uninstallCommand,
        string? quietUninstallCommand,
        string? productCode,
        string registryKeyPath,
        bool isSystemComponent,
        DateTime? installDate,
        long? estimatedSizeBytes,
        bool isWindowsApp)
    {
        Name = name;
        Publisher = publisher;
        Version = version;
        InstallLocation = installLocation;
        UninstallCommand = uninstallCommand;
        QuietUninstallCommand = quietUninstallCommand;
        ProductCode = productCode;
        RegistryKeyPath = registryKeyPath;
        IsSystemComponent = isSystemComponent;
        InstallDate = installDate;
        EstimatedSizeBytes = estimatedSizeBytes;
        IsWindowsApp = isWindowsApp;
    }

    public string Name { get; }

    public string Publisher { get; }

    public string Version { get; }

    public string? InstallLocation { get; }

    public string? UninstallCommand { get; }

    public string? QuietUninstallCommand { get; }

    public string? ProductCode { get; }

    public string RegistryKeyPath { get; }

    public bool IsSystemComponent { get; }

    public DateTime? InstallDate { get; }

    public long? EstimatedSizeBytes { get; }

    public bool IsWindowsApp { get; }
}

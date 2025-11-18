using Microsoft.Win32;

namespace SystemCleaner.Core.Models;

public sealed class StartupEntry
{
    public StartupEntry(
        string name,
        string location,
        string command,
        bool isEnabled,
        StartupEntryKind kind = StartupEntryKind.Unknown,
        RegistryHive? registryHive = null,
        RegistryView? registryView = null,
        RegistryView? registryApprovalView = null,
        string? registrySubKey = null,
        string? registryValueName = null,
        string? approvalSubKey = null)
    {
        Name = name;
        Location = location;
        Command = command;
        IsEnabled = isEnabled;
        Kind = kind;
        RegistryHive = registryHive;
        RegistryView = registryView;
        RegistryApprovalView = registryApprovalView;
        RegistrySubKey = registrySubKey;
        RegistryValueName = registryValueName;
        ApprovalSubKey = approvalSubKey;
    }

    public string Name { get; }

    public string Location { get; }

    public string Command { get; }

    public bool IsEnabled { get; }

    public StartupEntryKind Kind { get; }

    public RegistryHive? RegistryHive { get; }

    public RegistryView? RegistryView { get; }

    public RegistryView? RegistryApprovalView { get; }

    public string? RegistrySubKey { get; }

    public string? RegistryValueName { get; }

    public string? ApprovalSubKey { get; }
}

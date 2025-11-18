using System;
using SystemCleaner.Core.Models;

namespace SystemCleaner.App.ViewModels;

public enum StartupVerificationStatus
{
    Unknown,
    Match,
    Mismatch
}

public sealed class StartupEntryViewModel : ObservableObject
{
    private bool _isEnabled;
    private bool _isTogglePending;
    private bool _isNotificationActive;
    private StartupVerificationStatus _verificationStatus = StartupVerificationStatus.Unknown;

    public StartupEntryViewModel(StartupEntry entry)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        _isEnabled = entry.IsEnabled;
    }

    public StartupEntry Entry { get; private set; }

    public string Name => Entry.Name;

    public string Location => Entry.Location;

    public string Command => Entry.Command;

    public bool SupportsToggle =>
        Entry.RegistryHive is not null &&
        Entry.RegistryView is not null &&
        !string.IsNullOrWhiteSpace(Entry.ApprovalSubKey) &&
        !string.IsNullOrWhiteSpace(Entry.RegistryValueName);

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value) && _isNotificationActive)
            {
                ToggleRequested?.Invoke(this, value);
            }
        }
    }

    public bool IsTogglePending
    {
        get => _isTogglePending;
        internal set => SetProperty(ref _isTogglePending, value);
    }

    public StartupVerificationStatus VerificationStatus
    {
        get => _verificationStatus;
        internal set => SetProperty(ref _verificationStatus, value);
    }

    internal event EventHandler<bool>? ToggleRequested;

    internal void EnableUserNotifications() => _isNotificationActive = true;

    internal void UpdateFromSource(StartupEntry entry)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        SetIsEnabledSilently(entry.IsEnabled);
    }

    internal void MarkVerification(bool? matches)
    {
        VerificationStatus = matches switch
        {
            true => StartupVerificationStatus.Match,
            false => StartupVerificationStatus.Mismatch,
            _ => StartupVerificationStatus.Unknown
        };
    }

    internal void SetIsEnabledSilently(bool value)
    {
        var previous = _isNotificationActive;
        _isNotificationActive = false;
        SetProperty(ref _isEnabled, value);
        _isNotificationActive = previous;
    }
}

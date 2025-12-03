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
    private bool _hasApprovalState = true;

    public StartupEntryViewModel(StartupEntry entry)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        _isEnabled = entry.IsEnabled;
    }

    public StartupEntry Entry { get; private set; }

    public string Name => Entry.Name;

    public string Location => Entry.Location;

    public string Scope => Entry.Location;

    public bool RequiresElevation => string.Equals(Entry.Location, "All Users", StringComparison.OrdinalIgnoreCase);

    public string Command => Entry.Command;

    /// <summary>
    /// Returns true if this entry can be enabled/disabled.
    /// For registry entries, we need RegistryView. For startup folder entries, 
    /// we use RegistryApprovalView instead (RegistryView is null for folder entries).
    /// </summary>
    internal bool HasToggleMetadata =>
        Entry.RegistryHive is not null &&
        (Entry.RegistryView is not null || Entry.RegistryApprovalView is not null) &&
        !string.IsNullOrWhiteSpace(Entry.ApprovalSubKey) &&
        !string.IsNullOrWhiteSpace(Entry.RegistryValueName);

    public bool SupportsToggle => HasToggleMetadata && _hasApprovalState;

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

    internal void UpdateToggleAvailability(bool hasApprovalState)
    {
        if (_hasApprovalState == hasApprovalState)
        {
            return;
        }

        _hasApprovalState = hasApprovalState;
        RaisePropertyChanged(nameof(SupportsToggle));
    }
}

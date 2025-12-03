using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using SystemCleaner.App.Services;
using SystemCleaner.Core.Models;
using SystemCleaner.Core.Startup;

namespace SystemCleaner.App.ViewModels;

public sealed class StartupManagerViewModel : ObservableObject
{
    private readonly IStartupDiscoveryService _discoveryService;
    private readonly IUserConfirmationService _confirmationService;
    private readonly INotificationService _notificationService;
    private readonly RelayCommand _refreshCommand;
    private readonly RelayCommand _openEntryLocationCommand;
    private bool _isBusy;
    private string _statusMessage = "";
    private StartupEntryViewModel? _selectedEntry;
    private bool _hasEntries;
    private bool _hasIssues;

    public StartupManagerViewModel(IStartupDiscoveryService discoveryService, IUserConfirmationService confirmationService, INotificationService notificationService)
    {
        _discoveryService = discoveryService ?? throw new ArgumentNullException(nameof(discoveryService));
        _confirmationService = confirmationService ?? throw new ArgumentNullException(nameof(confirmationService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        Entries = new ObservableCollection<StartupEntryViewModel>();
        Issues = new ObservableCollection<string>();
        _refreshCommand = new RelayCommand(async () => await RefreshAsync(), () => !IsBusy);
        _openEntryLocationCommand = new RelayCommand(OpenSelectedEntryLocation, () => SelectedEntry is not null);
    }

    public ObservableCollection<StartupEntryViewModel> Entries { get; }

    public ObservableCollection<string> Issues { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                _refreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool HasEntries
    {
        get => _hasEntries;
        private set => SetProperty(ref _hasEntries, value);
    }

    public bool HasIssues
    {
        get => _hasIssues;
        private set => SetProperty(ref _hasIssues, value);
    }

    public ICommand RefreshCommand => _refreshCommand;

    public ICommand OpenEntryLocationCommand => _openEntryLocationCommand;

    public StartupEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
            {
                _openEntryLocationCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Loading startup entries...";
        ClearIssues();
        DiagnosticLogger.LogInfo("Startup refresh requested", nameof(StartupManagerViewModel));

        try
        {
            var result = await _discoveryService.GetStartupEntriesAsync(cancellationToken);
            var aggregatedIssues = new List<string>(result.Issues);
            ResetEntrySubscriptions();
            Entries.Clear();
            foreach (var startupEntry in result.Entries)
            {
                var viewModel = await CreateEntryViewModelAsync(startupEntry, aggregatedIssues, cancellationToken);
                Entries.Add(viewModel);
            }
            HasEntries = Entries.Count > 0;

            SelectedEntry = Entries.FirstOrDefault();
            if (result.Entries.Count == 0)
            {
                StatusMessage = "No startup items detected.";
            }
            else
            {
                var warningSuffix = result.Issues.Count > 0 ? " (with warnings)" : string.Empty;
                StatusMessage = $"Loaded {result.Entries.Count} entries{warningSuffix}.";
            }

            ReplaceIssues(aggregatedIssues);

            DiagnosticLogger.LogInfo($"Startup refresh completed. Entries={result.Entries.Count}; Warnings={result.Issues.Count}", nameof(StartupManagerViewModel));

            if (result.Issues.Count > 0)
            {
                var joined = string.Join("; ", result.Issues);
                DiagnosticLogger.LogInfo($"Startup discovery warnings: {joined}", nameof(StartupManagerViewModel));
                PublishNotification(NotificationSeverity.Warning, "Startup scan completed with warnings.", joined);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Startup scan canceled.";
            DiagnosticLogger.LogInfo("Startup refresh canceled", nameof(StartupManagerViewModel));
            HasEntries = Entries.Count > 0;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            DiagnosticLogger.Log(ex, nameof(StartupManagerViewModel));
            PublishNotification(NotificationSeverity.Error, "Startup scan failed.", ex.Message);
            ReplaceIssues(new[] { ex.Message });
        }
        finally
        {
            IsBusy = false;
            HasEntries = Entries.Count > 0;
        }
    }

    private async Task<StartupEntryViewModel> CreateEntryViewModelAsync(StartupEntry entry, IList<string> issueSink, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var viewModel = new StartupEntryViewModel(entry);
        viewModel.ToggleRequested += OnEntryToggleRequested;
        viewModel.EnableUserNotifications();
        await InitializeVerificationAsync(viewModel, issueSink, cancellationToken);
        return viewModel;
    }

    private async Task InitializeVerificationAsync(StartupEntryViewModel viewModel, IList<string> issueSink, CancellationToken cancellationToken)
    {
        viewModel.MarkVerification(null);

        if (!viewModel.HasToggleMetadata)
        {
            viewModel.UpdateToggleAvailability(false);
            issueSink.Add($"{viewModel.Name}: Windows did not expose the metadata required to toggle this entry.");
            return;
        }

        try
        {
            var actualState = await _discoveryService.GetStartupEntryApprovalStateAsync(viewModel.Entry, cancellationToken);
            if (actualState is bool state)
            {
                viewModel.UpdateToggleAvailability(true);
                viewModel.MarkVerification(state == viewModel.Entry.IsEnabled);
            }
            else
            {
                viewModel.UpdateToggleAvailability(false);
                issueSink.Add($"{viewModel.Name}: Windows did not return an approval state (toggle disabled).");
            }
        }
        catch (Exception ex)
        {
            viewModel.UpdateToggleAvailability(false);
            issueSink.Add($"{viewModel.Name}: Unable to verify approval state ({ex.Message}).");
            DiagnosticLogger.Log(ex, nameof(StartupManagerViewModel));
        }
    }

    private void ResetEntrySubscriptions()
    {
        foreach (var entry in Entries)
        {
            entry.ToggleRequested -= OnEntryToggleRequested;
        }
    }

    private void OnEntryToggleRequested(object? sender, bool desiredState)
    {
        if (sender is not StartupEntryViewModel entry)
        {
            return;
        }

        if (!entry.SupportsToggle)
        {
            entry.SetIsEnabledSilently(!desiredState);
            StatusMessage = "This startup entry cannot be toggled.";
            PublishNotification(NotificationSeverity.Warning, "Startup entry cannot be toggled.");
            return;
        }

        _ = ToggleEntryAsync(entry, desiredState);
    }

    private async Task ToggleEntryAsync(StartupEntryViewModel entry, bool desiredState)
    {
        if (IsBusy)
        {
            entry.SetIsEnabledSilently(!desiredState);
            StatusMessage = "Startup scan in progress. Try again shortly.";
            PublishNotification(NotificationSeverity.Info, "Startup scan already running. Try again shortly.");
            return;
        }

        if (entry.IsTogglePending)
        {
            entry.SetIsEnabledSilently(!desiredState);
            return;
        }

        if (!await ConfirmStartupChangeAsync(entry, desiredState))
        {
            entry.SetIsEnabledSilently(!desiredState);
            StatusMessage = "Startup change canceled.";
            PublishNotification(NotificationSeverity.Info, "Startup change canceled.");
            return;
        }

        entry.IsTogglePending = true;
        entry.MarkVerification(null);
        var previousState = !desiredState;

        try
        {
            StatusMessage = $"{(desiredState ? "Enabling" : "Disabling")} {entry.Name}...";
            await _discoveryService.SetStartupEntryEnabledAsync(entry.Entry, desiredState);

            var current = entry.Entry;
            var candidate = new StartupEntry(
                current.Name,
                current.Location,
                current.Command,
                desiredState,
                current.Kind,
                current.RegistryHive,
                current.RegistryView,
                current.RegistryApprovalView,
                current.RegistrySubKey,
                current.RegistryValueName,
                current.ApprovalSubKey);

            var verification = await _discoveryService.GetStartupEntryApprovalStateAsync(candidate);
            if (verification is bool actual)
            {
                var finalized = new StartupEntry(
                    candidate.Name,
                    candidate.Location,
                    candidate.Command,
                    actual,
                    candidate.Kind,
                    candidate.RegistryHive,
                    candidate.RegistryView,
                    candidate.RegistryApprovalView,
                    candidate.RegistrySubKey,
                    candidate.RegistryValueName,
                    candidate.ApprovalSubKey);

                entry.UpdateFromSource(finalized);
                entry.MarkVerification(actual == desiredState);
                StatusMessage = actual == desiredState
                    ? $"{entry.Name} {(actual ? "enabled" : "disabled")} (confirmed)."
                    : $"{entry.Name} may still be {(actual ? "enabled" : "disabled")} (confirmation mismatch).";
            }
            else
            {
                entry.UpdateFromSource(candidate);
                entry.MarkVerification(null);
                StatusMessage = $"{entry.Name} {(desiredState ? "enabled" : "disabled")} (confirmation unavailable).";
            }
        }
        catch (StartupDiscoveryException ex)
        {
            entry.SetIsEnabledSilently(previousState);
            entry.MarkVerification(null);
            var detail = ex.InnerException?.Message;
            StatusMessage = string.IsNullOrWhiteSpace(detail) ? ex.Message : $"{ex.Message} {detail}";
            DiagnosticLogger.Log(ex, nameof(StartupManagerViewModel));
            PublishNotification(NotificationSeverity.Error, $"Unable to update {entry.Name}.", detail ?? ex.Message);
        }
        catch (Exception ex)
        {
            entry.SetIsEnabledSilently(previousState);
            entry.MarkVerification(null);
            StatusMessage = $"Unable to update {entry.Name}: {ex.Message}";
            DiagnosticLogger.Log(ex, nameof(StartupManagerViewModel));
            PublishNotification(NotificationSeverity.Error, $"Unable to update {entry.Name}.", ex.Message);
        }
        finally
        {
            entry.IsTogglePending = false;
        }
    }

    private void ClearIssues() => ReplaceIssues(Array.Empty<string>());

    private void ReplaceIssues(IEnumerable<string> issues)
    {
        Issues.Clear();
        foreach (var issue in issues ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(issue))
            {
                continue;
            }

            Issues.Add(issue.Trim());
        }

        HasIssues = Issues.Count > 0;
    }

    private void OpenSelectedEntryLocation()
    {
        var entry = SelectedEntry;
        if (entry is null)
        {
            return;
        }

        var resolved = ResolveCommandPath(entry.Command);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            StatusMessage = "Unable to resolve startup entry path.";
            PublishNotification(NotificationSeverity.Warning, "Unable to resolve startup entry path.");
            return;
        }

        try
        {
            if (File.Exists(resolved))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{resolved}\"",
                    UseShellExecute = true
                });
                StatusMessage = $"Opened {resolved}.";
                return;
            }

            if (Directory.Exists(resolved))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{resolved}\"",
                    UseShellExecute = true
                });
                StatusMessage = $"Opened {resolved}.";
                return;
            }

            StatusMessage = "Startup path not found.";
            PublishNotification(NotificationSeverity.Warning, "Startup entry path not found.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unable to open location: {ex.Message}";
            DiagnosticLogger.Log(ex, nameof(StartupManagerViewModel));
            PublishNotification(NotificationSeverity.Error, "Unable to open startup entry location.", ex.Message);
        }
    }

    private static string? ResolveCommandPath(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        string expanded;
        try
        {
            expanded = Environment.ExpandEnvironmentVariables(command).Trim();
        }
        catch (ArgumentException)
        {
            expanded = command.Trim();
        }
        if (expanded.Length == 0)
        {
            return null;
        }

        if (expanded.StartsWith("\"", StringComparison.Ordinal))
        {
            var endQuote = expanded.IndexOf('"', 1);
            if (endQuote > 1)
            {
                return expanded.Substring(1, endQuote - 1);
            }
        }

        if (File.Exists(expanded) || Directory.Exists(expanded))
        {
            return expanded;
        }

        var terminators = new[] { ' ', '\t' };
        var splitIndex = expanded.IndexOfAny(terminators);
        if (splitIndex > 0)
        {
            var candidate = expanded[..splitIndex];
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return expanded;
    }

    private Task<bool> ConfirmStartupChangeAsync(StartupEntryViewModel entry, bool desiredState)
    {
        var action = desiredState ? "enable" : "disable";
        var message = $"Do you want to {action} startup entry '{entry.Name}'?";
        return _confirmationService.ConfirmAsync("Confirm Startup Change", message);
    }

    private void PublishNotification(NotificationSeverity severity, string message, string? detail = null)
    {
        _notificationService.Publish(message, severity, detail, nameof(StartupManagerViewModel));
    }
}

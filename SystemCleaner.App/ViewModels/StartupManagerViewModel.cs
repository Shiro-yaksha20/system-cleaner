using System;
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
    private readonly RelayCommand _refreshCommand;
    private readonly RelayCommand _openEntryLocationCommand;
    private bool _isBusy;
    private string _statusMessage = "";
    private StartupEntryViewModel? _selectedEntry;

    public StartupManagerViewModel(IStartupDiscoveryService discoveryService, IUserConfirmationService confirmationService)
    {
        _discoveryService = discoveryService ?? throw new ArgumentNullException(nameof(discoveryService));
        _confirmationService = confirmationService ?? throw new ArgumentNullException(nameof(confirmationService));
        Entries = new ObservableCollection<StartupEntryViewModel>();
        _refreshCommand = new RelayCommand(async () => await RefreshAsync(), () => !IsBusy);
        _openEntryLocationCommand = new RelayCommand(OpenSelectedEntryLocation, () => SelectedEntry is not null);
    }

    public ObservableCollection<StartupEntryViewModel> Entries { get; }

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
        DiagnosticLogger.LogInfo("Startup refresh requested", nameof(StartupManagerViewModel));

        try
        {
            var result = await _discoveryService.GetStartupEntriesAsync(cancellationToken);
            ResetEntrySubscriptions();
            Entries.Clear();
            foreach (var startupEntry in result.Entries)
            {
                Entries.Add(CreateEntryViewModel(startupEntry));
            }

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

            DiagnosticLogger.LogInfo($"Startup refresh completed. Entries={result.Entries.Count}; Warnings={result.Issues.Count}", nameof(StartupManagerViewModel));

            if (result.Issues.Count > 0)
            {
                var joined = string.Join("; ", result.Issues);
                DiagnosticLogger.LogInfo($"Startup discovery warnings: {joined}", nameof(StartupManagerViewModel));
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Startup scan canceled.";
            DiagnosticLogger.LogInfo("Startup refresh canceled", nameof(StartupManagerViewModel));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            DiagnosticLogger.Log(ex, nameof(StartupManagerViewModel));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private StartupEntryViewModel CreateEntryViewModel(StartupEntry entry)
    {
        var viewModel = new StartupEntryViewModel(entry);
        viewModel.ToggleRequested += OnEntryToggleRequested;
        viewModel.EnableUserNotifications();
        viewModel.MarkVerification(null);
        return viewModel;
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
        }
        catch (Exception ex)
        {
            entry.SetIsEnabledSilently(previousState);
            entry.MarkVerification(null);
            StatusMessage = $"Unable to update {entry.Name}: {ex.Message}";
            DiagnosticLogger.Log(ex, nameof(StartupManagerViewModel));
        }
        finally
        {
            entry.IsTogglePending = false;
        }
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
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unable to open location: {ex.Message}";
            DiagnosticLogger.Log(ex, nameof(StartupManagerViewModel));
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
}

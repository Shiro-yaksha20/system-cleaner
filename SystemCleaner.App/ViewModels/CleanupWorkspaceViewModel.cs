using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SystemCleaner.App.Services;
using SystemCleaner.App.Utilities;
using SystemCleaner.Core.Abstractions;
using SystemCleaner.Core.Models;
using SystemCleaner.Core.Services;

namespace SystemCleaner.App.ViewModels;

public sealed class CleanupWorkspaceViewModel : ObservableObject, IDisposable
{
    private const int MaxLogEntries = 200;

    private readonly ICleanupService _cleanupService;
    private readonly IUserConfirmationService _confirmationService;
    private readonly INotificationService _notificationService;
    private readonly ObservableCollection<CleanupModuleViewModel> _modules;
    private readonly ObservableCollection<ModuleSummaryViewModel> _moduleSummaries;
    private readonly ObservableCollection<LogEntryViewModel> _logEntries;
    private readonly RelayCommand _scanCommand;
    private readonly RelayCommand _cleanCommand;
    private readonly RelayCommand _selectAllCommand;
    private readonly RelayCommand _deselectAllCommand;
    private readonly RelayCommand _quickCleanCommand;
    private readonly RelayCommand _openItemLocationCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly Dispatcher _dispatcher;
    private CleanupModuleViewModel? _selectedModule;
    private CancellationTokenSource? _currentOperationCts;
    private bool _isBusy;
    private string _statusMessage = "Ready";
    private DateTime? _lastScanTimestamp;
    private long _lastScanTotalBytes;
    private int _lastScanTotalItems;
    private string _lastQuickCleanSummary = "Quick Clean not run yet.";
    private bool _hasCreatedRestorePoint;
    private DateTime _nextRestorePointAttemptUtc;
    private int _pendingCleanupItemCount;

    public CleanupWorkspaceViewModel(
        ICleanupService cleanupService,
        IUserConfirmationService confirmationService,
        INotificationService notificationService)
    {
        _cleanupService = cleanupService ?? throw new ArgumentNullException(nameof(cleanupService));
        _confirmationService = confirmationService ?? throw new ArgumentNullException(nameof(confirmationService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        _logEntries = new ObservableCollection<LogEntryViewModel>();
        _modules = new ObservableCollection<CleanupModuleViewModel>(_cleanupService.Modules.Select(CreateModuleViewModel));
        _moduleSummaries = new ObservableCollection<ModuleSummaryViewModel>(_cleanupService.Modules.Select(CreateModuleSummaryViewModel));
        if (_modules.Count > 0)
        {
            _selectedModule = _modules[0];
        }

        _scanCommand = new RelayCommand(async () => await ExecuteScanAsync(), () => !IsBusy);
        _cleanCommand = new RelayCommand(async () => await ExecuteCleanAsync(), CanExecuteClean);
        _selectAllCommand = new RelayCommand(SelectAllItems, () => SelectedModule?.Items.Count > 0);
        _deselectAllCommand = new RelayCommand(DeselectAllItems, () => SelectedModule?.Items.Count > 0);
        _quickCleanCommand = new RelayCommand(async () => await ExecuteQuickCleanAsync(), () => !IsBusy);
        _openItemLocationCommand = new RelayCommand(OpenSelectedItemLocation, () => SelectedModule?.SelectedItem is not null);
        _cancelCommand = new RelayCommand(() => CancelCurrentOperation(), () => IsBusy);
    }

    public ObservableCollection<CleanupModuleViewModel> Modules => _modules;

    public ObservableCollection<ModuleSummaryViewModel> ModuleSummaries => _moduleSummaries;

    public ObservableCollection<LogEntryViewModel> LogEntries => _logEntries;

    public CleanupModuleViewModel? SelectedModule
    {
        get => _selectedModule;
        set
        {
            if (SetProperty(ref _selectedModule, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public long TotalSelectedBytes => _modules.Sum(static module => module.SelectedSizeBytes);

    public string TotalSelectedDisplay => SizeFormatter.FormatSize(TotalSelectedBytes);

    public string LastQuickCleanSummary
    {
        get => _lastQuickCleanSummary;
        private set => SetProperty(ref _lastQuickCleanSummary, value);
    }

    public string LastScanSummary => _lastScanTimestamp is null
        ? "Scan not run yet."
        : $"{_lastScanTimestamp:MMM dd, yyyy h:mm tt} • {_lastScanTotalItems} item{(_lastScanTotalItems == 1 ? string.Empty : "s")} • {SizeFormatter.FormatSize(_lastScanTotalBytes)}";

    public int PendingCleanupItemCount
    {
        get => _pendingCleanupItemCount;
        private set => SetProperty(ref _pendingCleanupItemCount, value);
    }

    public ICommand ScanCommand => _scanCommand;

    public ICommand CleanCommand => _cleanCommand;

    public ICommand SelectAllCommand => _selectAllCommand;

    public ICommand DeselectAllCommand => _deselectAllCommand;

    public ICommand QuickCleanCommand => _quickCleanCommand;

    public ICommand OpenItemLocationCommand => _openItemLocationCommand;

    public ICommand CancelCommand => _cancelCommand;

    private Task InvokeOnUiAsync(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(action).Task;
    }

    public async Task ExecuteScanAsync()
    {
        await RunOperationAsync(async token =>
        {
            var progress = new Progress<CleanupProgressUpdate>(update =>
            {
                StatusMessage = update.Message;
                AddLog(update.Message, "Info");
            });
            await UpdateModulesFromScanAsync(token, progress);
        }, "Scanning...", notificationContext: "scan");
    }

    public void AddLog(string message, string severity)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var entry = new LogEntryViewModel(message, severity);

        if (_dispatcher.CheckAccess())
        {
            AddLogEntry(entry);
        }
        else
        {
            _dispatcher.Invoke(() => AddLogEntry(entry));
        }
    }

    public void Dispose()
    {
        CancelCurrentOperation();
        foreach (var module in _modules)
        {
            module.PropertyChanged -= OnModulePropertyChanged;
        }

        GC.SuppressFinalize(this);
    }

    private CleanupModuleViewModel CreateModuleViewModel(ICleanupModule module)
    {
        var viewModel = new CleanupModuleViewModel(module.ModuleInfo);
        viewModel.PropertyChanged += OnModulePropertyChanged;
        return viewModel;
    }

    private ModuleSummaryViewModel CreateModuleSummaryViewModel(ICleanupModule module)
    {
        return new ModuleSummaryViewModel(module.ModuleInfo);
    }

    private async Task ExecuteCleanAsync()
    {
        var itemsToClean = _modules
            .SelectMany(module => module.Items.Where(item => item.IsSelected).Select(item => item.Item))
            .ToArray();

        if (itemsToClean.Length == 0)
        {
            StatusMessage = "No items selected for cleanup.";
            AddLog("Cleanup skipped: no items selected.", "Info");
            return;
        }

        if (!await ConfirmCleanupAsync(itemsToClean, "Manual Cleanup"))
        {
            StatusMessage = "Cleanup canceled.";
            AddLog("Cleanup canceled by user.", "Info");
            return;
        }

        await RunOperationAsync(async token =>
        {
            var progress = new Progress<CleanupProgressUpdate>(update => StatusMessage = update.Message);
            await EnsureRestorePointAsync(token);
            var result = await _cleanupService.CleanAsync(itemsToClean, progress, token);
            var freedDisplay = SizeFormatter.FormatSize(result.TotalBytesFreed);
            StatusMessage = $"Cleanup completed. Freed {freedDisplay}.";
            LogCleanupResult(result);
            AddLog($"Manual cleanup freed {freedDisplay}.", "Success");

            await UpdateModulesFromScanAsync(token, progress: null);
        }, "Cleaning...", suppressCompletionMessage: true, notificationContext: "cleanup");
    }

    private async Task ExecuteQuickCleanAsync()
    {
        await RunOperationAsync(async token =>
        {
            AddLog("Quick clean started", "Info");
            var progress = new Progress<CleanupProgressUpdate>(update =>
            {
                StatusMessage = update.Message;
                AddLog(update.Message, "Info");
            });

            await UpdateModulesFromScanAsync(token, progress);

            var itemsToClean = _modules
                .Where(module => module.IsQuickCleanSafe)
                .SelectMany(module => module.Items.Where(item => item.IsSelected).Select(item => item.Item))
                .ToArray();

            if (itemsToClean.Length == 0)
            {
                AddLog("Quick clean skipped: no safe items selected.", "Info");
                StatusMessage = "Nothing to quick clean.";
                return;
            }

            if (!await ConfirmCleanupAsync(itemsToClean, "Quick Clean"))
            {
                AddLog("Quick clean canceled by user.", "Info");
                StatusMessage = "Quick clean canceled.";
                return;
            }

            await EnsureRestorePointAsync(token);

            var result = await _cleanupService.CleanAsync(itemsToClean, progress, token);
            LogCleanupResult(result);
            var freed = SizeFormatter.FormatSize(result.TotalBytesFreed);
            AddLog($"Quick clean freed {freed}.", "Success");
            LastQuickCleanSummary = $"{DateTime.Now:MMM dd, h:mm tt} • Freed {freed}";

            await UpdateModulesFromScanAsync(token, progress: null);
        }, "Quick Clean...", suppressCompletionMessage: true, notificationContext: "quick clean");
    }

    private bool CanExecuteClean() => !IsBusy && TotalSelectedBytes > 0;

    private void SelectAllItems()
    {
        SelectedModule?.SelectAll(true);
        RefreshTotals();
    }

    private void DeselectAllItems()
    {
        SelectedModule?.SelectAll(false);
        RefreshTotals();
    }

    private void OnModulePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CleanupModuleViewModel.SelectedSizeBytes) or nameof(CleanupModuleViewModel.TotalSizeBytes))
        {
            RefreshTotals();
        }

        if (e.PropertyName is nameof(CleanupModuleViewModel.SelectedItem))
        {
            _openItemLocationCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task RunOperationAsync(
        Func<CancellationToken, Task> operation,
        string startMessage,
        bool suppressCompletionMessage = false,
        string? notificationContext = null)
    {
        if (IsBusy)
        {
            return;
        }

        CancelCurrentOperation();
        _currentOperationCts = new CancellationTokenSource();
        StatusMessage = startMessage;
        IsBusy = true;

        try
        {
            await operation(_currentOperationCts.Token);
            if (!suppressCompletionMessage)
            {
                StatusMessage = "Ready";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operation canceled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            var context = string.IsNullOrWhiteSpace(notificationContext) ? "operation" : notificationContext;
            _notificationService.PublishError($"System Cleaner {context} failed.", ex.Message, nameof(CleanupWorkspaceViewModel));
        }
        finally
        {
            CancelCurrentOperation(requestCancellation: false);
            IsBusy = false;
            RefreshTotals();
        }
    }

    private async Task UpdateModulesFromScanAsync(CancellationToken token, IProgress<CleanupProgressUpdate>? progress)
    {
        var results = await _cleanupService.ScanAsync(progress, token).ConfigureAwait(false);
        await InvokeOnUiAsync(() =>
        {
            ApplyScanResults(results);
            UpdateModuleSummaries(results);
            RefreshTotals();
        });
        var totalItems = results.Sum(static result => result.Items.Count);
        var totalSize = SizeFormatter.FormatSize(results.Sum(static result => result.TotalSizeBytes));
        AddLog($"Scan complete: {totalItems} items ({totalSize}).", "Info");
    }

    private void ApplyScanResults(IReadOnlyList<CleanupScanResult> results)
    {
        var lookup = results.ToDictionary(result => result.Module.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var module in _modules)
        {
            if (lookup.TryGetValue(module.Id, out var result))
            {
                module.SetItems(result.Items);
                if (result.Items.Count > 0)
                {
                    AddLog($"{module.Name}: {SizeFormatter.FormatSize(result.TotalSizeBytes)} detected.", module.IsQuickCleanSafe ? "Info" : "Warning");
                    if (!string.IsNullOrWhiteSpace(module.Warning))
                    {
                        AddLog(module.Warning!, "Warning");
                    }
                }
            }
            else
            {
                module.SetItems(Array.Empty<CleanupItem>());
            }
        }
    }

    private void UpdateModuleSummaries(IReadOnlyList<CleanupScanResult> results)
    {
        _lastScanTimestamp = DateTime.UtcNow;
        _lastScanTotalItems = results.Sum(static result => result.Items.Count);
        _lastScanTotalBytes = results.Sum(static result => result.TotalSizeBytes);
        PendingCleanupItemCount = _lastScanTotalItems;

        var lookup = results.ToDictionary(result => result.Module.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var summary in _moduleSummaries)
        {
            if (lookup.TryGetValue(summary.Id, out var result))
            {
                summary.Update(result.Items.Count, result.TotalSizeBytes, _lastScanTimestamp);
            }
            else
            {
                summary.Update(0, 0, _lastScanTimestamp);
            }
        }

        RaisePropertyChanged(nameof(LastScanSummary));
    }

    private void CancelCurrentOperation(bool requestCancellation = true)
    {
        if (_currentOperationCts is null)
        {
            return;
        }

        if (requestCancellation && !_currentOperationCts.IsCancellationRequested)
        {
            StatusMessage = "Canceling current operation...";
            _currentOperationCts.Cancel();
        }

        _currentOperationCts.Dispose();
        _currentOperationCts = null;
        _cancelCommand.RaiseCanExecuteChanged();
    }

    private void RefreshTotals()
    {
        RaisePropertyChanged(nameof(TotalSelectedBytes));
        RaisePropertyChanged(nameof(TotalSelectedDisplay));
        _cleanCommand.RaiseCanExecuteChanged();
        _selectAllCommand.RaiseCanExecuteChanged();
        _deselectAllCommand.RaiseCanExecuteChanged();
        _quickCleanCommand.RaiseCanExecuteChanged();
        _openItemLocationCommand.RaiseCanExecuteChanged();
    }

    private void LogCleanupResult(CleanupBatchResult result)
    {
        foreach (var failure in result.Failures)
        {
            var issues = string.Join("; ", failure.Issues);
            AddLog($"Cleanup issue for {failure.Item.DisplayName}: {issues}", "Warning");
        }
    }

    private async Task EnsureRestorePointAsync(CancellationToken token)
    {
        if (_hasCreatedRestorePoint)
        {
            return;
        }

        if (DateTime.UtcNow < _nextRestorePointAttemptUtc)
        {
            return;
        }

        var success = await TryCreateRestorePointAsync(token).ConfigureAwait(false);
        if (success)
        {
            _hasCreatedRestorePoint = true;
            AddLog("System restore point created.", "Info");
        }
        else
        {
            _nextRestorePointAttemptUtc = DateTime.UtcNow.AddMinutes(30);
            AddLog("Could not create system restore point. We will retry later.", "Warning");
        }
    }

    private static async Task<bool> TryCreateRestorePointAsync(CancellationToken token)
    {
        return await Task.Run(() =>
        {
            try
            {
                var scope = new ManagementScope("\\\\localhost\\root\\default");
                scope.Connect();

                using var managementClass = new ManagementClass(scope, new ManagementPath("SystemRestore"), null);
                using var inParams = managementClass.GetMethodParameters("CreateRestorePoint");
                inParams["Description"] = "SystemCleaner Restore Point";
                inParams["RestorePointType"] = 16; // APPLICATION_INSTALL
                inParams["EventType"] = 100;

                var outParams = managementClass.InvokeMethod("CreateRestorePoint", inParams, null);
                if (outParams is null)
                {
                    return false;
                }

                return Convert.ToInt32(outParams.Properties["ReturnValue"].Value) == 0;
            }
            catch
            {
                return false;
            }
        }, token).ConfigureAwait(false);
    }

    private void OpenSelectedItemLocation()
    {
        var item = SelectedModule?.SelectedItem;
        if (item is null)
        {
            return;
        }

        var path = item.Path;

        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
                AddLog($"Opened location for {item.DisplayName}.", "Info");
                return;
            }

            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true
                });
                AddLog($"Opened folder {path}.", "Info");
                return;
            }

            AddLog($"Path not found: {path}", "Warning");
        }
        catch (Exception ex)
        {
            AddLog($"Unable to open location: {ex.Message}", "Error");
            _notificationService.PublishError("Unable to open selected item location.", ex.Message, nameof(CleanupWorkspaceViewModel));
        }
    }

    private void RaiseCommandStates()
    {
        _scanCommand.RaiseCanExecuteChanged();
        _cleanCommand.RaiseCanExecuteChanged();
        _selectAllCommand.RaiseCanExecuteChanged();
        _deselectAllCommand.RaiseCanExecuteChanged();
        _quickCleanCommand.RaiseCanExecuteChanged();
        _openItemLocationCommand.RaiseCanExecuteChanged();
        _cancelCommand.RaiseCanExecuteChanged();
    }

    private Task<bool> ConfirmCleanupAsync(IReadOnlyCollection<CleanupItem> items, string operationName)
    {
        var count = items?.Count ?? 0;
        var totalBytes = items?.Sum(static item => item.SizeBytes) ?? 0;
        var sizeDisplay = SizeFormatter.FormatSize(totalBytes);
        var message = $"{operationName} will remove {count} item{(count == 1 ? string.Empty : "s")} totaling {sizeDisplay}.\nDo you want to continue?";
        return _confirmationService.ConfirmAsync($"{operationName} Confirmation", message);
    }

    private void AddLogEntry(LogEntryViewModel entry)
    {
        _logEntries.Insert(0, entry);
        while (_logEntries.Count > MaxLogEntries)
        {
            _logEntries.RemoveAt(_logEntries.Count - 1);
        }
    }
}

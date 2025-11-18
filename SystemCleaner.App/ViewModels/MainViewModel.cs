using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using SystemCleaner.App.Settings;
using SystemCleaner.App.Theming;
using SystemCleaner.App.Utilities;
using SystemCleaner.Core.Abstractions;
using SystemCleaner.Core.Models;
using SystemCleaner.Core.Services;
using SystemCleaner.Core.Startup;
using SystemCleaner.Core.Uninstall;
using AppThemeMode = SystemCleaner.App.Theming.ThemeMode;

namespace SystemCleaner.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private const int MaxLogEntries = 200;
    private readonly CleanupService _cleanupService;
    private readonly ObservableCollection<CleanupModuleViewModel> _modules;
    private readonly RelayCommand _scanCommand;
    private readonly RelayCommand _cleanCommand;
    private readonly RelayCommand _selectAllCommand;
    private readonly RelayCommand _deselectAllCommand;
    private readonly RelayCommand _quickCleanCommand;
    private readonly RelayCommand _openItemLocationCommand;
    private readonly ThemeService _themeService;
    private readonly StartupManagerViewModel _startupManager;
    private readonly UninstallerViewModel _uninstaller;
    private readonly ObservableCollection<LogEntryViewModel> _logEntries;
    private readonly ObservableCollection<ModuleSummaryViewModel> _moduleSummaries;
    private readonly AppThemeMode[] _themeOptions = Enum.GetValues<AppThemeMode>();
    private readonly RelayCommand _changeTabCommand;
    private readonly SystemUsageViewModel _systemUsage;
    private readonly SystemInfoViewModel _systemInfo;
    private readonly HardwareMonitorService _hardwareMonitorService;
    private readonly IUserConfirmationService _confirmationService;
    private readonly IAppSettingsService _settingsService;
    private readonly RelayCommand _cancelCommand;
    private readonly VirusTotalService _virusTotalService;
    private readonly VirusTotalViewModel _virusTotal;
    private string? _pendingVirusTotalApiKey;
    private bool _isRestoringSettings;
    private CancellationTokenSource? _currentOperationCts;
    private AppThemeMode _selectedTheme = AppThemeMode.System;
    private bool _hasCreatedRestorePoint;
    private DateTime? _lastScanTimestamp;
    private long _lastScanTotalBytes;
    private int _lastScanTotalItems;
    private string _lastQuickCleanSummary = "Quick Clean not run yet.";
    private string? _virusTotalApiKey;
    private DateTime _nextRestorePointAttemptUtc;

    private CleanupModuleViewModel? _selectedModule;
    private bool _isBusy;
    private string _statusMessage = "Ready";
    private MainTab _selectedTab = MainTab.Overview;

    public MainViewModel(CleanupService cleanupService,
        ThemeService themeService,
        StartupDiscoveryService startupDiscoveryService,
        UninstallerService uninstallerService,
        HardwareMonitorService hardwareMonitorService,
        IUserConfirmationService confirmationService,
        IAppSettingsService settingsService,
        VirusTotalService virusTotalService)
    {
        _cleanupService = cleanupService ?? throw new ArgumentNullException(nameof(cleanupService));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _hardwareMonitorService = hardwareMonitorService ?? throw new ArgumentNullException(nameof(hardwareMonitorService));
        _confirmationService = confirmationService ?? throw new ArgumentNullException(nameof(confirmationService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _virusTotalService = virusTotalService ?? throw new ArgumentNullException(nameof(virusTotalService));
        if (startupDiscoveryService is null)
        {
            throw new ArgumentNullException(nameof(startupDiscoveryService));
        }
        if (uninstallerService is null)
        {
            throw new ArgumentNullException(nameof(uninstallerService));
        }

        _logEntries = new ObservableCollection<LogEntryViewModel>();
        _startupManager = new StartupManagerViewModel(startupDiscoveryService, confirmationService);
        _uninstaller = new UninstallerViewModel(uninstallerService, confirmationService);
        _modules = new ObservableCollection<CleanupModuleViewModel>(cleanupService.Modules.Select(CreateModuleViewModel));
        _moduleSummaries = new ObservableCollection<ModuleSummaryViewModel>(cleanupService.Modules.Select(CreateModuleSummaryViewModel));
        if (_modules.Count > 0)
        {
            _selectedModule = _modules[0];
        }

        _systemUsage = SystemUsageViewModel.CreateSample();
        _systemInfo = new SystemInfoViewModel();

        _scanCommand = new RelayCommand(async () => await ExecuteScanAsync(), () => !IsBusy);
        _cleanCommand = new RelayCommand(async () => await ExecuteCleanAsync(), CanExecuteClean);
        _selectAllCommand = new RelayCommand(SelectAllItems, () => SelectedModule?.Items.Count > 0);
        _deselectAllCommand = new RelayCommand(DeselectAllItems, () => SelectedModule?.Items.Count > 0);
        _quickCleanCommand = new RelayCommand(async () => await ExecuteQuickCleanAsync(), () => !IsBusy);
        _openItemLocationCommand = new RelayCommand(OpenSelectedItemLocation, () => SelectedModule?.SelectedItem is not null);
        _changeTabCommand = new RelayCommand(parameter =>
        {
            if (parameter is MainTab tab)
            {
                SelectedTab = tab;
            }
        });
    _cancelCommand = new RelayCommand(() => CancelCurrentOperation(), () => IsBusy);

        _virusTotal = new VirusTotalViewModel(_virusTotalService);

        _themeService.ApplyTheme(_selectedTheme);

        _hardwareMonitorService.SnapshotAvailable += OnHardwareSnapshotAvailable;
        UpdateHardwareMonitorState();
    }

    public ObservableCollection<CleanupModuleViewModel> Modules => _modules;

    public ObservableCollection<ModuleSummaryViewModel> ModuleSummaries => _moduleSummaries;

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
        private set => SetProperty(ref _statusMessage, value);
    }

    public MainTab SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (SetProperty(ref _selectedTab, value))
            {
                UpdateHardwareMonitorState();
            }
        }
    }

    public long TotalSelectedBytes => _modules.Sum(static module => module.SelectedSizeBytes);

    public string TotalSelectedDisplay => SizeFormatter.FormatSize(TotalSelectedBytes);

    public ICommand ScanCommand => _scanCommand;

    public ICommand CleanCommand => _cleanCommand;

    public ICommand SelectAllCommand => _selectAllCommand;

    public ICommand DeselectAllCommand => _deselectAllCommand;

    public ICommand QuickCleanCommand => _quickCleanCommand;

    public ICommand OpenItemLocationCommand => _openItemLocationCommand;

    public ICommand ChangeTabCommand => _changeTabCommand;

    public ICommand CancelCommand => _cancelCommand;

    public StartupManagerViewModel StartupManager => _startupManager;

    public UninstallerViewModel Uninstaller => _uninstaller;

    public VirusTotalViewModel VirusTotal => _virusTotal;

    public bool RequireConfirmation
    {
        get => _confirmationService.RequireConfirmation;
        set
        {
            if (_confirmationService.RequireConfirmation == value)
            {
                return;
            }

            _confirmationService.RequireConfirmation = value;
            RaisePropertyChanged();
            AddLog(value ? "Confirmation prompts enabled." : "Confirmation prompts disabled.", "Info");
            ScheduleSettingsSave();
        }
    }

    public ObservableCollection<LogEntryViewModel> LogEntries => _logEntries;

    public IReadOnlyList<AppThemeMode> ThemeOptions => _themeOptions;

    public SystemUsageViewModel SystemUsage => _systemUsage;

    public SystemInfoViewModel SystemInfo => _systemInfo;

    public string LastScanSummary => _lastScanTimestamp is null
        ? "Scan not run yet."
        : $"{_lastScanTimestamp:MMM dd, yyyy h:mm tt} • {_lastScanTotalItems} item{(_lastScanTotalItems == 1 ? string.Empty : "s")} • {SizeFormatter.FormatSize(_lastScanTotalBytes)}";

    public string LastQuickCleanSummary
    {
        get => _lastQuickCleanSummary;
        private set => SetProperty(ref _lastQuickCleanSummary, value);
    }

    public AppThemeMode SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetProperty(ref _selectedTheme, value))
            {
                _themeService.ApplyTheme(value);
                AddLog($"Theme changed to {value}.", "Info");
                RaisePropertyChanged(nameof(IsDarkTheme));
                ScheduleSettingsSave();
            }
        }
    }

    public bool IsDarkTheme
    {
        get => SelectedTheme == AppThemeMode.Dark;
        set => SelectedTheme = value ? AppThemeMode.Dark : AppThemeMode.Light;
    }

    public string? VirusTotalApiKey
    {
        get => _virusTotalApiKey;
        set
        {
            var sanitized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (SetProperty(ref _virusTotalApiKey, sanitized))
            {
                _virusTotalService.SetApiKey(_virusTotalApiKey);
                RaisePropertyChanged(nameof(HasVirusTotalApiKey));
                RaisePropertyChanged(nameof(VirusTotalApiKeyStatus));
                ScheduleSettingsSave();
            }
        }
    }

    public bool HasVirusTotalApiKey => !string.IsNullOrWhiteSpace(_virusTotalApiKey);

    public string VirusTotalApiKeyStatus
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_pendingVirusTotalApiKey) &&
                !string.Equals(_pendingVirusTotalApiKey, _virusTotalApiKey, StringComparison.Ordinal))
            {
                return "Pending save";
            }

            return HasVirusTotalApiKey ? "API key stored" : "Not configured";
        }
    }

    public async Task InitializeAsync()
    {
        var settingsTask = LoadSettingsAsync();
        var startupTask = _startupManager.RefreshAsync();
        var uninstallTask = _uninstaller.InitializeAsync();
        await Task.WhenAll(settingsTask, startupTask, uninstallTask);
        AddLog("Startup entries loaded.", "Info");
        AddLog("Installed software catalog loaded.", "Info");
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

    private async Task ExecuteScanAsync()
    {
        await RunOperationAsync(async token =>
        {
            var progress = new Progress<CleanupProgressUpdate>(update =>
            {
                StatusMessage = update.Message;
                AddLog(update.Message, "Info");
            });
            await UpdateModulesFromScanAsync(token, progress);
        }, "Scanning...");
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
        }, "Cleaning...", suppressCompletionMessage: true);
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

    private async Task RunOperationAsync(Func<CancellationToken, Task> operation, string startMessage, bool suppressCompletionMessage = false)
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
        var results = await _cleanupService.ScanAsync(progress, token);
        ApplyScanResults(results);
        UpdateModuleSummaries(results);
        RefreshTotals();
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
            AddLog($"Quick clean freed {SizeFormatter.FormatSize(result.TotalBytesFreed)}.", "Success");

            await UpdateModulesFromScanAsync(token, progress: null);
        }, "Quick Clean...", suppressCompletionMessage: true);
    }

    private void LogCleanupResult(CleanupBatchResult result)
    {
        foreach (var failure in result.Failures)
        {
            var issues = string.Join("; ", failure.Issues);
            AddLog($"Cleanup issue for {failure.Item.DisplayName}: {issues}", "Warning");
        }
    }

    private void OnHardwareSnapshotAvailable(object? sender, HardwareSnapshotEventArgs e)
    {
        if (e?.Snapshot is null)
        {
            return;
        }

        _systemUsage.Update(e.Snapshot);
        _systemInfo.Update(e.Snapshot);
    }

    private void UpdateHardwareMonitorState()
    {
        var shouldRun = SelectedTab is MainTab.Overview or MainTab.SystemInfo;
        if (shouldRun)
        {
            _hardwareMonitorService.Start();
        }
        else
        {
            _hardwareMonitorService.Stop();
        }
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            var settings = await _settingsService.GetAsync().ConfigureAwait(false);
            _isRestoringSettings = true;
            try
            {
                SelectedTheme = settings.Theme;
                if (_confirmationService.RequireConfirmation != settings.RequireConfirmation)
                {
                    _confirmationService.RequireConfirmation = settings.RequireConfirmation;
                    RaisePropertyChanged(nameof(RequireConfirmation));
                }

                _virusTotalApiKey = settings.VirusTotalApiKey;
                RaisePropertyChanged(nameof(VirusTotalApiKey));
                RaisePropertyChanged(nameof(HasVirusTotalApiKey));
                _virusTotalService.SetApiKey(_virusTotalApiKey);
                _pendingVirusTotalApiKey = _virusTotalApiKey;
                RaisePropertyChanged(nameof(VirusTotalApiKeyStatus));
            }
            finally
            {
                _isRestoringSettings = false;
            }

            AddLog("Settings loaded.", "Info");
        }
        catch (Exception ex)
        {
            AddLog($"Unable to load settings: {ex.Message}", "Warning");
            DiagnosticLogger.Log(ex, nameof(MainViewModel));
        }
    }

    private void ScheduleSettingsSave()
    {
        if (_isRestoringSettings)
        {
            return;
        }

        var snapshot = new AppSettings
        {
            Theme = _selectedTheme,
            RequireConfirmation = _confirmationService.RequireConfirmation,
            VirusTotalApiKey = _virusTotalApiKey
        };

        _ = Task.Run(async () =>
        {
            try
            {
                await _settingsService.SaveAsync(snapshot).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Log(ex, nameof(MainViewModel));
            }
        });
    }

    public void UpdatePendingVirusTotalApiKey(string? value)
    {
        var sanitized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (string.Equals(_pendingVirusTotalApiKey, sanitized, StringComparison.Ordinal))
        {
            return;
        }

        _pendingVirusTotalApiKey = sanitized;
        RaisePropertyChanged(nameof(VirusTotalApiKeyStatus));
    }

    public bool SavePendingVirusTotalApiKey()
    {
        if (string.IsNullOrWhiteSpace(_pendingVirusTotalApiKey))
        {
            StatusMessage = "Enter a VirusTotal API key before saving.";
            return false;
        }

        var sanitized = _pendingVirusTotalApiKey.Trim();
        if (string.Equals(sanitized, _virusTotalApiKey, StringComparison.Ordinal))
        {
            StatusMessage = "VirusTotal API key unchanged.";
            return false;
        }

        VirusTotalApiKey = sanitized;
        _pendingVirusTotalApiKey = null;
        RaisePropertyChanged(nameof(VirusTotalApiKeyStatus));
        AddLog("VirusTotal API key saved.", "Success");
        StatusMessage = "VirusTotal API key saved.";
        return true;
    }

    public void ClearVirusTotalApiKey()
    {
        var hadKey = HasVirusTotalApiKey;
        _pendingVirusTotalApiKey = null;

        if (hadKey)
        {
            VirusTotalApiKey = null;
            AddLog("VirusTotal API key cleared.", "Info");
            StatusMessage = "VirusTotal API key cleared.";
        }

        RaisePropertyChanged(nameof(VirusTotalApiKeyStatus));
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

        var success = await TryCreateRestorePointAsync(token);
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
        }, token);
    }

    private void AddLog(string message, string severity)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var entry = new LogEntryViewModel(message, severity);

        if (Application.Current?.Dispatcher is Dispatcher dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => AddLogEntry(entry));
        }
        else
        {
            AddLogEntry(entry);
        }
    }

    private void AddLogEntry(LogEntryViewModel entry)
    {
        _logEntries.Insert(0, entry);
        while (_logEntries.Count > MaxLogEntries)
        {
            _logEntries.RemoveAt(_logEntries.Count - 1);
        }
    }

    public void Dispose()
    {
    CancelCurrentOperation();
        _hardwareMonitorService.SnapshotAvailable -= OnHardwareSnapshotAvailable;
        _hardwareMonitorService.Dispose();
        _virusTotal.Dispose();
        _virusTotalService.Dispose();
        GC.SuppressFinalize(this);
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
}

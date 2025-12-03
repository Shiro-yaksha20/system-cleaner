using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using SystemCleaner.App.Services;
using SystemCleaner.App.Utilities;
using SystemCleaner.Core.Uninstall;

namespace SystemCleaner.App.ViewModels;

public sealed class UninstallerViewModel : ObservableObject
{
    private const int OperationTimeoutMilliseconds = 600_000;

    private readonly IUninstallerService _service;
    private readonly IUserConfirmationService _confirmationService;
    private readonly INotificationService _notificationService;
    private readonly RelayCommand _refreshCommand;
    private readonly RelayCommand _uninstallCommand;
    private readonly RelayCommand _forceUninstallCommand;
    private readonly RelayCommand _openInstallLocationCommand;
    private readonly RelayCommand _openRegistryKeyCommand;
    private readonly RelayCommand _removeExtensionCommand;
    private readonly RelayCommand _powerfulScanCommand;
    private readonly RelayCommand _cleanupResidualsCommand;
    private readonly RelayCommand _toggleMonitorCommand;
    private readonly RelayCommand _toggleWindowsAppsCommand;
    private readonly Dispatcher _dispatcher;
    private readonly object _applicationsSyncRoot = new();
    private readonly object _extensionsSyncRoot = new();
    private readonly object _residualsSyncRoot = new();
    private CancellationTokenSource? _currentOperation;
    private readonly ICollectionView _applicationsView;
    private CancellationTokenSource? _monitoringCts;
    private HashSet<string> _knownApplications = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<InstalledApplication> _lastResidualTargets = Array.Empty<InstalledApplication>();
    private bool _isBusy;
    private string _statusMessage = "Ready";
    private string _searchText = string.Empty;
    private InstalledApplicationViewModel? _selectedApplication;
    private BrowserExtensionViewModel? _selectedExtension;
    private string _installMonitorStatus = "Install monitor idle.";
    private string _residualStatus = "Powerful scan not run.";
    private bool _showWindowsAppsOnly;
    private bool _hasScannedResiduals;
    private bool _isSelectAllChecked;
    private bool _isApplyingSelectAll;

    public UninstallerViewModel(IUninstallerService service, IUserConfirmationService? confirmationService = null, INotificationService? notificationService = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _confirmationService = confirmationService ?? new UserConfirmationService { RequireConfirmation = false };
        _notificationService = notificationService ?? new NotificationService();
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        Applications = new ObservableCollection<InstalledApplicationViewModel>();
        BrowserExtensions = new ObservableCollection<BrowserExtensionViewModel>();
        HealthInsights = new ObservableCollection<SoftwareHealthInsightViewModel>();
        ResidualItems = new ObservableCollection<ResidualItemViewModel>();
        _applicationsView = CollectionViewSource.GetDefaultView(Applications);
        _applicationsView.Filter = FilterApplications;
        _applicationsView.SortDescriptions.Clear();
        _applicationsView.SortDescriptions.Add(new SortDescription(nameof(InstalledApplicationViewModel.Name), ListSortDirection.Ascending));

        if (_applicationsView is ICollectionViewLiveShaping liveView)
        {
            liveView.IsLiveFiltering = true;
            liveView.IsLiveSorting = true;
        }

        BindingOperations.EnableCollectionSynchronization(Applications, _applicationsSyncRoot);
        BindingOperations.EnableCollectionSynchronization(BrowserExtensions, _extensionsSyncRoot);
        BindingOperations.EnableCollectionSynchronization(ResidualItems, _residualsSyncRoot);

        _refreshCommand = new RelayCommand(async () => await RefreshAsync(), () => !IsBusy);
        _uninstallCommand = new RelayCommand(async () => await UninstallSelectedAsync(force: false), () => !IsBusy && SelectedCount > 0);
        _forceUninstallCommand = new RelayCommand(async () => await UninstallSelectedAsync(force: true), () => !IsBusy && SelectedCount > 0);
        _openInstallLocationCommand = new RelayCommand(OpenInstallLocation, () => SelectedApplication is not null && !string.IsNullOrWhiteSpace(SelectedApplication.InstallLocation));
        _openRegistryKeyCommand = new RelayCommand(OpenRegistryKey, () => SelectedApplication is not null);
        _removeExtensionCommand = new RelayCommand(async () => await RemoveSelectedExtensionAsync(), () => SelectedExtension?.CanRemove == true);
        _powerfulScanCommand = new RelayCommand(async () => await RunPowerfulScanAsync(), () => !IsBusy && SelectedCount > 0);
        _cleanupResidualsCommand = new RelayCommand(async () => await CleanupResidualsAsync(), () => !IsBusy && ResidualItems.Any(item => item.IsSelected));
        _toggleMonitorCommand = new RelayCommand(async () => await ToggleInstallMonitorAsync());
        _toggleWindowsAppsCommand = new RelayCommand(ToggleWindowsAppsFilter);

        ResidualItems.CollectionChanged += OnResidualItemsCollectionChanged;
    }

    public ObservableCollection<InstalledApplicationViewModel> Applications { get; }

    public ObservableCollection<BrowserExtensionViewModel> BrowserExtensions { get; }

    public ObservableCollection<SoftwareHealthInsightViewModel> HealthInsights { get; }

    public ObservableCollection<ResidualItemViewModel> ResidualItems { get; }

    public ICollectionView ApplicationsView => _applicationsView;

    public ICommand RefreshCommand => _refreshCommand;

    public ICommand UninstallCommand => _uninstallCommand;

    public ICommand ForceUninstallCommand => _forceUninstallCommand;

    public ICommand OpenInstallLocationCommand => _openInstallLocationCommand;

    public ICommand OpenRegistryKeyCommand => _openRegistryKeyCommand;

    public ICommand RemoveExtensionCommand => _removeExtensionCommand;

    public ICommand PowerfulScanCommand => _powerfulScanCommand;

    public ICommand CleanupResidualsCommand => _cleanupResidualsCommand;

    public ICommand ToggleMonitorCommand => _toggleMonitorCommand;

    public ICommand ToggleWindowsAppsCommand => _toggleWindowsAppsCommand;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetIsBusyFlag(value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => UpdateStatus(value);
    }

    public string SearchText
    {
        get => _searchText;
        set => SetSearchText(value);
    }

    public InstalledApplicationViewModel? SelectedApplication
    {
        get => _selectedApplication;
        set => SetSelectedApplication(value);
    }

    public BrowserExtensionViewModel? SelectedExtension
    {
        get => _selectedExtension;
        set => SetSelectedExtension(value);
    }

    public int SelectedCount => Applications.Count(app => app.IsSelected);

    public bool HasSelection => SelectedCount > 0;

    public bool HasApplications => Applications.Count > 0;

    public string InstallMonitorStatus
    {
        get => _installMonitorStatus;
        private set => SetInstallMonitorMessage(value);
    }

    public bool IsMonitoring => _monitoringCts is not null;

    public string MonitorToggleLabel => IsMonitoring ? "Stop Monitor" : "Start Monitor";

    public string ResidualStatus
    {
        get => _residualStatus;
        private set => UpdateResidualStatus(value);
    }

    public bool HasResiduals => ResidualItems.Count > 0;

    public bool HasScannedResiduals => _hasScannedResiduals;

    public bool IsSelectAllChecked
    {
        get => _isSelectAllChecked;
        set => ApplySelectAll(value);
    }

    public long SelectedSizeBytes => Applications
        .Where(app => app.IsSelected && app.Application.EstimatedSizeBytes.HasValue)
        .Sum(app => app.Application.EstimatedSizeBytes!.Value);

    public string SelectedSizeDisplay => SizeFormatter.FormatSize(SelectedSizeBytes);

    public bool ShowWindowsAppsOnly
    {
        get => _showWindowsAppsOnly;
        set => SetShowWindowsAppsOnly(value);
    }

    public string WindowsAppsToggleLabel => ShowWindowsAppsOnly ? "Show All Apps" : "Windows Apps Only";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RefreshAsync(cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        await RunOperationAsync(async token =>
        {
            UpdateStatus("Scanning installed software...");
            var snapshot = await _service.GetInstalledSoftwareAsync(token);
            UpdateApplications(snapshot.Applications);
            UpdateExtensions(snapshot.BrowserExtensions);
            UpdateHealth(snapshot.HealthInsights);
            _lastResidualTargets = Array.Empty<InstalledApplication>();
            UpdateResiduals(Array.Empty<ResidualItem>());
            UpdateResidualStatus("Powerful scan not run.");
            SetResidualScanState(false);

            if (snapshot.Issues.Count > 0)
            {
                foreach (var issue in snapshot.Issues)
                {
                    DiagnosticLogger.LogInfo(issue, nameof(UninstallerViewModel));
                }
            }

            DiagnosticLogger.LogInfo($"Refresh loaded {snapshot.Applications.Count} application(s).", nameof(UninstallerViewModel));

            if (snapshot.Applications.Count == 0)
            {
                UpdateStatus("No applications detected. Check log for registry access issues.", NotificationSeverity.Warning);
            }

            UpdateStatus($"Loaded {snapshot.Applications.Count} applications.");
        }, cancellationToken);
    }

    private async Task UninstallSelectedAsync(bool force)
    {
        if (SelectedCount == 0)
        {
            UpdateStatus("No applications selected.");
            return;
        }

        var selected = Applications.Where(app => app.IsSelected).ToArray();

        if (!await ConfirmUninstallAsync(selected, force))
        {
            UpdateStatus("Uninstall canceled.");
            return;
        }

        await RunOperationAsync(async token =>
        {
            UpdateStatus(force ? "Running forced uninstall..." : "Uninstalling selected applications...");
            var uninstallOptions = force
                ? new UninstallOptions(true, CreateRestorePoint: true, ResidualCleanupMode: ResidualCleanupMode.Cleanup, ShredResidualFiles: true)
                : new UninstallOptions(false, ResidualCleanupMode: ResidualCleanupMode.ScanOnly);
            var result = await _service.UninstallAsync(selected.Select(app => app.Application), uninstallOptions, token);
            foreach (var message in result.Messages)
            {
                DiagnosticLogger.LogInfo(message, nameof(UninstallerViewModel));
            }

            if (result.Failed == 0)
            {
                UpdateStatus(force ? "Forced uninstall completed." : "Uninstall completed.");
            }
            else
            {
                UpdateStatus($"Completed with {result.Failed} issue(s).", NotificationSeverity.Warning);
            }

            var snapshot = await _service.GetInstalledSoftwareAsync(token);
            UpdateApplications(snapshot.Applications);
            UpdateExtensions(snapshot.BrowserExtensions);
            UpdateHealth(snapshot.HealthInsights);

            var residuals = await CollectResidualsAsync(selected, token);
            UpdateResiduals(residuals);
            SetResidualScanState(true);
            UpdateResidualStatus(residuals.Count == 0 ? "No residuals detected." : $"Powerful scan found {residuals.Count} residual item(s).");
        }, CancellationToken.None);
    }

    private async Task RunPowerfulScanAsync()
    {
        var selectedApps = Applications.Where(app => app.IsSelected).ToArray();
        if (selectedApps.Length == 0)
        {
            UpdateResidualStatus("Select at least one application to scan.");
            return;
        }

        await RunOperationAsync(async token =>
        {
            UpdateResidualStatus("Running powerful scan...");
            var residuals = await CollectResidualsAsync(selectedApps, token).ConfigureAwait(false);
            UpdateResiduals(residuals);
            SetResidualScanState(true);
            UpdateResidualStatus(residuals.Count == 0
                ? "No residuals detected."
                : $"Powerful scan found {residuals.Count} residual item(s).");
        }, CancellationToken.None);
    }

    private Task<IReadOnlyList<ResidualItem>> CollectResidualsAsync(IEnumerable<InstalledApplicationViewModel> selection, CancellationToken token)
    {
        var targets = selection
            .Select(app => app.Application)
            .DistinctBy(app => app.RegistryKeyPath)
            .ToArray();
        _lastResidualTargets = targets;
        return CollectResidualsAsync(targets, token);
    }

    private Task<IReadOnlyList<ResidualItem>> CollectResidualsAsync(IReadOnlyList<InstalledApplication> targets, CancellationToken token)
    {
        if (targets.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<ResidualItem>>(Array.Empty<ResidualItem>());
        }

        return _service.FindResidualItemsAsync(targets, token);
    }

    private async Task CleanupResidualsAsync()
    {
        if (ResidualItems.Count == 0)
        {
            UpdateResidualStatus("No residual items to clean.");
            return;
        }

        var selectedResidualsView = ResidualItems.Where(item => item.IsSelected).ToArray();
        if (selectedResidualsView.Length == 0)
        {
            UpdateResidualStatus("Select at least one residual item to remove.");
            return;
        }

        if (!await ConfirmResidualCleanupAsync(selectedResidualsView))
        {
            UpdateResidualStatus("Residual cleanup canceled.");
            return;
        }

        var selectedResiduals = selectedResidualsView.Select(item => item.Item).ToArray();

        await RunOperationAsync(async token =>
        {
            UpdateResidualStatus("Removing residual items...");
            var result = await _service.CleanupResidualItemsAsync(selectedResiduals, token).ConfigureAwait(false);
            foreach (var message in result.Messages)
            {
                DiagnosticLogger.LogInfo(message, nameof(UninstallerViewModel));
            }

            var residuals = await CollectResidualsAsync(_lastResidualTargets, token).ConfigureAwait(false);
            UpdateResiduals(residuals);
            SetResidualScanState(true);

            var summary = result.Failed == 0
                ? $"Removed {result.Removed} residual item(s)."
                : $"Cleanup completed with {result.Failed} failure(s).";

            if (residuals.Count == 0)
            {
                UpdateResidualStatus($"{summary} Residual cleanup complete.");
            }
            else
            {
                UpdateResidualStatus($"{summary} {residuals.Count} residual item(s) remain.");
            }
        }, CancellationToken.None);
    }

    private async Task RemoveSelectedExtensionAsync()
    {
        var extension = SelectedExtension;
        if (extension is null || !extension.CanRemove)
        {
            UpdateStatus("No removable extension selected.");
            return;
        }

        if (!await ConfirmExtensionRemovalAsync(extension))
        {
            UpdateStatus("Extension removal canceled.");
            return;
        }

        await RunOperationAsync(async token =>
        {
            UpdateStatus($"Removing {extension.Name}...");
            var removed = await _service.RemoveBrowserExtensionAsync(extension.Extension, token);
            if (removed)
            {
                BrowserExtensions.Remove(extension);
                UpdateStatus("Extension removed.");
            }
            else
            {
                UpdateStatus("Unable to remove extension.", NotificationSeverity.Warning);
            }
        }, CancellationToken.None);
    }

    private void OpenInstallLocation()
    {
        var app = SelectedApplication;
        if (app is null)
        {
            return;
        }

        var path = app.InstallLocation;
        if (string.IsNullOrWhiteSpace(path))
        {
            UpdateStatus("Install location not available.", NotificationSeverity.Warning);
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                ProcessStart("explorer.exe", path);
                UpdateStatus($"Opened {path}.");
                return;
            }

            UpdateStatus("Install directory not found.", NotificationSeverity.Warning);
        }
        catch (Exception ex)
        {
            UpdateStatus(ex.Message, NotificationSeverity.Error, ex.Message);
            DiagnosticLogger.Log(ex, nameof(UninstallerViewModel));
        }
    }

    private void OpenRegistryKey()
    {
        var app = SelectedApplication;
        if (app is null)
        {
            return;
        }

        var keyPath = app.RegistryKeyPath;
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            UpdateStatus("Registry key unavailable.", NotificationSeverity.Warning);
            return;
        }

        try
        {
            if (Application.Current?.Dispatcher is Dispatcher dispatcher)
            {
                dispatcher.BeginInvoke(new Action(() => Clipboard.SetText(keyPath)));
            }

            ProcessStart("reg.exe", $"query \"{keyPath}\"");
            UpdateStatus("Registry path copied to clipboard.");
        }
        catch (Exception ex)
        {
            UpdateStatus(ex.Message, NotificationSeverity.Error, ex.Message);
            DiagnosticLogger.Log(ex, nameof(UninstallerViewModel));
        }
    }

    private Task<bool> ConfirmUninstallAsync(IReadOnlyCollection<InstalledApplicationViewModel> apps, bool force)
    {
        if (apps.Count == 0)
        {
            return Task.FromResult(true);
        }

        var preview = string.Join(", ", apps.Take(3).Select(app => app.Name));
        if (apps.Count > 3)
        {
            preview += ", ...";
        }

        var action = force ? "force uninstall" : "uninstall";
        var message = $"You are about to {action} {apps.Count} application{(apps.Count == 1 ? string.Empty : "s")}: {preview}.\nDo you want to continue?";
        return _confirmationService.ConfirmAsync("Confirm Uninstall", message);
    }

    private Task<bool> ConfirmResidualCleanupAsync(IReadOnlyCollection<ResidualItemViewModel> residuals)
    {
        if (residuals.Count == 0)
        {
            return Task.FromResult(true);
        }

        var sample = string.Join(Environment.NewLine, residuals.Take(3).Select(item => $"• {item.ApplicationName}: {item.Path}"));
        if (residuals.Count > 3)
        {
            sample += Environment.NewLine + "• ...";
        }

        var message = $"Remove {residuals.Count} residual item{(residuals.Count == 1 ? string.Empty : "s")} from disk?\n{sample}\nProceed with cleanup?";
        return _confirmationService.ConfirmAsync("Confirm Residual Cleanup", message);
    }

    private Task<bool> ConfirmExtensionRemovalAsync(BrowserExtensionViewModel extension)
    {
        var message = $"Remove browser extension '{extension.Name}' for {extension.Browser}?\nLocation: {extension.Location}";
        return _confirmationService.ConfirmAsync("Confirm Extension Removal", message);
    }

    private bool FilterApplications(object obj)
    {
        if (obj is not InstalledApplicationViewModel app)
        {
            return false;
        }

        try
        {
            if (ShowWindowsAppsOnly && !app.IsWindowsApp)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                return true;
            }

            return app.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || app.Publisher.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log(ex, nameof(UninstallerViewModel));
            return true;
        }
    }

    private async Task RunOperationAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        CancelCurrentOperation();
        _currentOperation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _currentOperation.CancelAfter(OperationTimeoutMilliseconds);
        SetIsBusyFlag(true);

        try
        {
            await operation(_currentOperation.Token);
        }
        catch (OperationCanceledException)
        {
            UpdateStatus("Operation canceled.");
        }
        catch (Exception ex)
        {
            UpdateStatus(ex.Message, NotificationSeverity.Error, ex.Message);
            DiagnosticLogger.Log(ex, nameof(UninstallerViewModel));
        }
        finally
        {
            CancelCurrentOperation();
            SetIsBusyFlag(false);
            InvokeOnUi(() =>
            {
                RaiseCommandStates();
                RaisePropertyChanged(nameof(SelectedCount));
                RaisePropertyChanged(nameof(HasSelection));
                RaisePropertyChanged(nameof(SelectedSizeBytes));
                RaisePropertyChanged(nameof(SelectedSizeDisplay));
                RefreshSelectAllState();
            });
        }
    }

    private void UpdateApplications(IReadOnlyList<InstalledApplication> applications)
    {
        InvokeOnUi(() =>
        {
            var snapshot = Applications.ToArray();
            foreach (var existing in snapshot)
            {
                existing.PropertyChanged -= OnApplicationPropertyChanged;
            }

            Applications.Clear();
            foreach (var app in applications)
            {
                var viewModel = new InstalledApplicationViewModel(app);
                viewModel.PropertyChanged += OnApplicationPropertyChanged;
                Applications.Add(viewModel);
            }

            RaisePropertyChanged(nameof(SelectedCount));
            RaisePropertyChanged(nameof(HasSelection));
            RaisePropertyChanged(nameof(HasApplications));
            RaiseCommandStates();
            SelectedApplication = Applications.FirstOrDefault();
            RefreshSelectAllState();
            RaisePropertyChanged(nameof(SelectedSizeBytes));
            RaisePropertyChanged(nameof(SelectedSizeDisplay));
        });

        RefreshKnownApplications(applications);

        var viewCount = 0;
        InvokeOnUi(() => viewCount = _applicationsView.Cast<object>().Count());
        DiagnosticLogger.LogInfo(
            $"Applications collection size: {applications.Count}; view count: {viewCount}; showWindowsOnly={ShowWindowsAppsOnly}; search='{SearchText}'",
            nameof(UninstallerViewModel));
    }

    private void UpdateExtensions(IReadOnlyList<BrowserExtensionInfo> extensions)
    {
        InvokeOnUi(() =>
        {
            BrowserExtensions.Clear();
            foreach (var extension in extensions)
            {
                BrowserExtensions.Add(new BrowserExtensionViewModel(extension));
            }

            SelectedExtension = BrowserExtensions.FirstOrDefault();
        });
    }

    private void UpdateHealth(IReadOnlyList<SoftwareHealthInsight> insights)
    {
        InvokeOnUi(() =>
        {
            HealthInsights.Clear();
            foreach (var insight in insights)
            {
                HealthInsights.Add(new SoftwareHealthInsightViewModel(insight));
            }
        });
    }

    private void UpdateResiduals(IEnumerable<ResidualItem> residuals)
    {
        InvokeOnUi(() =>
        {
            foreach (var item in ResidualItems)
            {
                item.PropertyChanged -= OnResidualItemPropertyChanged;
            }

            ResidualItems.Clear();

            foreach (var residual in residuals)
            {
                var viewModel = new ResidualItemViewModel(residual);
                viewModel.PropertyChanged += OnResidualItemPropertyChanged;
                ResidualItems.Add(viewModel);
            }

            RaisePropertyChanged(nameof(HasResiduals));
            _cleanupResidualsCommand.RaiseCanExecuteChanged();
        });
    }

    private void RaiseCommandStates()
    {
        InvokeOnUi(() =>
        {
            _refreshCommand.RaiseCanExecuteChanged();
            _uninstallCommand.RaiseCanExecuteChanged();
            _forceUninstallCommand.RaiseCanExecuteChanged();
            _openInstallLocationCommand.RaiseCanExecuteChanged();
            _openRegistryKeyCommand.RaiseCanExecuteChanged();
            _removeExtensionCommand.RaiseCanExecuteChanged();
            _powerfulScanCommand.RaiseCanExecuteChanged();
            _cleanupResidualsCommand.RaiseCanExecuteChanged();
            _toggleMonitorCommand.RaiseCanExecuteChanged();
        });
    }

    private void OnApplicationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstalledApplicationViewModel.IsSelected))
        {
            RaisePropertyChanged(nameof(SelectedCount));
            RaisePropertyChanged(nameof(HasSelection));
            RaisePropertyChanged(nameof(SelectedSizeBytes));
            RaisePropertyChanged(nameof(SelectedSizeDisplay));
            RefreshSelectAllState();
            RaiseCommandStates();
        }
    }

    private Task ToggleInstallMonitorAsync()
    {
        if (IsMonitoring)
        {
            StopInstallMonitor();
            SetInstallMonitorMessage("Install monitor stopped.");
            return Task.CompletedTask;
        }

        var cts = StartInstallMonitor();
        if (cts is null)
        {
            SetInstallMonitorMessage("Unable to start monitor.");
            return Task.CompletedTask;
        }

        SetInstallMonitorMessage("Install monitor running...");
        _ = MonitorInstallationsAsync(cts, cts.Token);
        return Task.CompletedTask;
    }

    private CancellationTokenSource? StartInstallMonitor()
    {
        StopInstallMonitor();
        _monitoringCts = new CancellationTokenSource();
        InvokeOnUi(() =>
        {
            RaiseCommandStates();
            RaisePropertyChanged(nameof(MonitorToggleLabel));
        });
        return _monitoringCts;
    }

    private void StopInstallMonitor()
    {
        if (_monitoringCts is null)
        {
            return;
        }
        _monitoringCts.Cancel();
        _monitoringCts.Dispose();
        _monitoringCts = null;
        InvokeOnUi(() =>
        {
            RaiseCommandStates();
            RaisePropertyChanged(nameof(MonitorToggleLabel));
        });
    }

    private async Task MonitorInstallationsAsync(CancellationTokenSource tokenSource, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(20), token).ConfigureAwait(false);
                var snapshot = await _service.GetInstalledSoftwareAsync(token).ConfigureAwait(false);
                var current = snapshot.Applications.Select(app => app.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var newApps = current.Where(name => !_knownApplications.Contains(name)).ToArray();

                if (newApps.Length > 0)
                {
                    foreach (var app in newApps)
                    {
                        DiagnosticLogger.LogInfo($"Install monitor detected new installation: {app}", nameof(UninstallerViewModel));
                    }

                    SetInstallMonitorMessage($"Detected {newApps.Length} new installation(s).");

                    InvokeOnUi(() =>
                    {
                        UpdateApplications(snapshot.Applications);
                        UpdateExtensions(snapshot.BrowserExtensions);
                        UpdateHealth(snapshot.HealthInsights);
                    });
                }

                RefreshKnownApplications(snapshot.Applications);
            }
        }
        catch (OperationCanceledException)
        {
            SetInstallMonitorMessage("Install monitor stopped.");
        }
        catch (Exception ex)
        {
            SetInstallMonitorMessage("Install monitor error.");
            DiagnosticLogger.Log(ex, nameof(UninstallerViewModel));
        }
        finally
        {
            if (ReferenceEquals(_monitoringCts, tokenSource))
            {
                StopInstallMonitor();
            }
        }
    }

    private void RefreshKnownApplications(IEnumerable<InstalledApplication> applications)
    {
        _knownApplications = applications
            .Select(app => app.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void InvokeOnUi(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _dispatcher.Invoke(action);
        }
    }

    private void SetIsBusyFlag(bool value)
    {
        InvokeOnUi(() =>
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        });
    }

    private void PublishNotification(NotificationSeverity severity, string message, string? detail = null)
    {
        _notificationService.Publish(message, severity, detail, nameof(UninstallerViewModel));
    }

    private void UpdateStatus(string message, NotificationSeverity? severity = null, string? detail = null)
    {
        InvokeOnUi(() => SetProperty(ref _statusMessage, message));
        if (severity.HasValue)
        {
            PublishNotification(severity.Value, message, detail);
        }
    }

    private void SetSearchText(string value)
    {
        InvokeOnUi(() =>
        {
            if (SetProperty(ref _searchText, value))
            {
                _applicationsView.Refresh();
            }
        });
    }

    private void SetSelectedApplication(InstalledApplicationViewModel? value)
    {
        InvokeOnUi(() =>
        {
            if (SetProperty(ref _selectedApplication, value))
            {
                RaiseCommandStates();
            }
        });
    }

    private void SetSelectedExtension(BrowserExtensionViewModel? value)
    {
        InvokeOnUi(() =>
        {
            if (SetProperty(ref _selectedExtension, value))
            {
                RaiseCommandStates();
            }
        });
    }

    private void SetInstallMonitorMessage(string message)
    {
        InvokeOnUi(() => SetProperty(ref _installMonitorStatus, message));
    }

    private void UpdateResidualStatus(string message)
    {
        InvokeOnUi(() => SetProperty(ref _residualStatus, message));
    }

    private void SetResidualScanState(bool hasScanned)
    {
        InvokeOnUi(() => SetProperty(ref _hasScannedResiduals, hasScanned, nameof(HasScannedResiduals)));
    }

    private void ApplySelectAll(bool isChecked)
    {
        InvokeOnUi(() =>
        {
            if (_isApplyingSelectAll)
            {
                return;
            }

            SetProperty(ref _isSelectAllChecked, isChecked, nameof(IsSelectAllChecked));

            _isApplyingSelectAll = true;
            try
            {
                foreach (var app in Applications)
                {
                    app.IsSelected = isChecked;
                }
            }
            finally
            {
                _isApplyingSelectAll = false;
            }

            RaisePropertyChanged(nameof(SelectedCount));
            RaisePropertyChanged(nameof(HasSelection));
            RaisePropertyChanged(nameof(SelectedSizeBytes));
            RaisePropertyChanged(nameof(SelectedSizeDisplay));
        });
    }

    private void RefreshSelectAllState()
    {
        if (_isApplyingSelectAll)
        {
            return;
        }

        var next = Applications.Count > 0 && Applications.All(app => app.IsSelected);
        InvokeOnUi(() => SetProperty(ref _isSelectAllChecked, next, nameof(IsSelectAllChecked)));
    }

    private void OnResidualItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaisePropertyChanged(nameof(HasResiduals));
        _cleanupResidualsCommand.RaiseCanExecuteChanged();
    }

    private void OnResidualItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResidualItemViewModel.IsSelected))
        {
            _cleanupResidualsCommand.RaiseCanExecuteChanged();
        }
    }

    private void SetShowWindowsAppsOnly(bool value)
    {
        InvokeOnUi(() =>
        {
            if (SetProperty(ref _showWindowsAppsOnly, value))
            {
                _applicationsView.Refresh();
                RaisePropertyChanged(nameof(WindowsAppsToggleLabel));
            }
        });
    }

    private void ToggleWindowsAppsFilter()
    {
        var next = !ShowWindowsAppsOnly;
        SetShowWindowsAppsOnly(next);
        UpdateStatus(next ? "Filtering to Windows Store apps." : "Showing all applications.");
    }

    private void CancelCurrentOperation()
    {
        if (_currentOperation is null)
        {
            return;
        }

        if (!_currentOperation.IsCancellationRequested)
        {
            _currentOperation.Cancel();
        }

        _currentOperation.Dispose();
        _currentOperation = null;
    }

    private static void ProcessStart(string file, string arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = file,
            Arguments = arguments,
            UseShellExecute = true
        };

        System.Diagnostics.Process.Start(startInfo);
    }
}

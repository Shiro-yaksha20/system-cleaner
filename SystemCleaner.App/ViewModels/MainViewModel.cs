using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using SystemCleaner.App.Services;
using SystemCleaner.App.Settings;
using SystemCleaner.App.Theming;
using SystemCleaner.Core.Abstractions;
using SystemCleaner.Core.Services;
using SystemCleaner.Core.Startup;
using SystemCleaner.Core.Uninstall;
using AppThemeMode = SystemCleaner.App.Theming.ThemeMode;

namespace SystemCleaner.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly CleanupWorkspaceViewModel _cleanup;
    private readonly IThemeService _themeService;
    private readonly StartupManagerViewModel _startupManager;
    private readonly UninstallerViewModel _uninstaller;
    private readonly AppThemeMode[] _themeOptions = Enum.GetValues<AppThemeMode>();
    private readonly RelayCommand _changeTabCommand;
    private readonly RelayCommand _dismissNotificationCommand;
    private readonly SystemUsageViewModel _systemUsage;
    private readonly SystemInfoViewModel _systemInfo;
    private readonly IHardwareMonitorService _hardwareMonitorService;
    private readonly IUserConfirmationService _confirmationService;
    private readonly IAppSettingsService _settingsService;
    private readonly IVirusTotalService _virusTotalService;
    private readonly VirusTotalViewModel _virusTotal;
    private readonly INotificationService _notificationService;
    private readonly INotifyCollectionChanged _notificationFeed;
    private string? _pendingVirusTotalApiKey;
    private bool _isRestoringSettings;
    private AppThemeMode _selectedTheme = AppThemeMode.System;
    private string? _virusTotalApiKey;
    private MainTab _selectedTab = MainTab.Overview;
    private NotificationMessage? _activeNotification;

    public MainViewModel(ICleanupService cleanupService,
        IThemeService themeService,
        IStartupDiscoveryService startupDiscoveryService,
        IUninstallerService uninstallerService,
        IHardwareMonitorService hardwareMonitorService,
        IUserConfirmationService confirmationService,
        IAppSettingsService settingsService,
        IVirusTotalService virusTotalService,
        INotificationService notificationService)
    {
        _ = cleanupService ?? throw new ArgumentNullException(nameof(cleanupService));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _hardwareMonitorService = hardwareMonitorService ?? throw new ArgumentNullException(nameof(hardwareMonitorService));
        _confirmationService = confirmationService ?? throw new ArgumentNullException(nameof(confirmationService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _virusTotalService = virusTotalService ?? throw new ArgumentNullException(nameof(virusTotalService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _notificationFeed = _notificationService.Notifications ?? throw new InvalidOperationException("Notification feed unavailable.");
        _dismissNotificationCommand = new RelayCommand(DismissActiveNotification, () => ActiveNotification is not null);

        var startupSvc = startupDiscoveryService ?? throw new ArgumentNullException(nameof(startupDiscoveryService));
        var uninstallSvc = uninstallerService ?? throw new ArgumentNullException(nameof(uninstallerService));

        _cleanup = new CleanupWorkspaceViewModel(cleanupService, confirmationService, _notificationService);
        _startupManager = new StartupManagerViewModel(startupSvc, confirmationService, _notificationService);
        _uninstaller = new UninstallerViewModel(uninstallSvc, confirmationService, _notificationService);

        _systemUsage = SystemUsageViewModel.CreateSample();
        _systemInfo = new SystemInfoViewModel();

        _changeTabCommand = new RelayCommand(parameter =>
        {
            if (parameter is MainTab tab)
            {
                SelectedTab = tab;
            }
        });

        _virusTotal = new VirusTotalViewModel(_virusTotalService, _notificationService);

        _themeService.ApplyTheme(_selectedTheme);

        _hardwareMonitorService.SnapshotAvailable += OnHardwareSnapshotAvailable;
        UpdateHardwareMonitorState();

        _notificationService.NotificationPublished += OnNotificationPublished;
        _notificationFeed.CollectionChanged += OnNotificationsChanged;
        ActiveNotification = _notificationService.Notifications.FirstOrDefault();
    }

    public CleanupWorkspaceViewModel Cleanup => _cleanup;

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

    public ICommand ChangeTabCommand => _changeTabCommand;

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

    public IReadOnlyList<AppThemeMode> ThemeOptions => _themeOptions;

    public SystemUsageViewModel SystemUsage => _systemUsage;

    public SystemInfoViewModel SystemInfo => _systemInfo;

    public ICommand DismissNotificationCommand => _dismissNotificationCommand;

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

    public NotificationMessage? ActiveNotification
    {
        get => _activeNotification;
        private set
        {
            if (SetProperty(ref _activeNotification, value))
            {
                _dismissNotificationCommand.RaiseCanExecuteChanged();
            }
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
            Cleanup.StatusMessage = "Enter a VirusTotal API key before saving.";
            return false;
        }

        var sanitized = _pendingVirusTotalApiKey.Trim();
        if (string.Equals(sanitized, _virusTotalApiKey, StringComparison.Ordinal))
        {
            Cleanup.StatusMessage = "VirusTotal API key unchanged.";
            return false;
        }

        VirusTotalApiKey = sanitized;
        _pendingVirusTotalApiKey = null;
        RaisePropertyChanged(nameof(VirusTotalApiKeyStatus));
        AddLog("VirusTotal API key saved.", "Success");
        Cleanup.StatusMessage = "VirusTotal API key saved.";
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
            Cleanup.StatusMessage = "VirusTotal API key cleared.";
        }

        RaisePropertyChanged(nameof(VirusTotalApiKeyStatus));
    }

    public void Dispose()
    {
        _cleanup.Dispose();
        _hardwareMonitorService.SnapshotAvailable -= OnHardwareSnapshotAvailable;
        _notificationService.NotificationPublished -= OnNotificationPublished;
        _notificationFeed.CollectionChanged -= OnNotificationsChanged;
        _hardwareMonitorService.Dispose();
        _virusTotal.Dispose();
        _virusTotalService.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnNotificationPublished(object? sender, NotificationMessage e)
    {
        ActiveNotification = e;
    }

    private void OnNotificationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_notificationService.Notifications.Count == 0)
        {
            ActiveNotification = null;
            return;
        }

        if (ActiveNotification is not null && _notificationService.Notifications.Contains(ActiveNotification))
        {
            return;
        }

        ActiveNotification = _notificationService.Notifications.FirstOrDefault();
    }

    private void DismissActiveNotification()
    {
        if (ActiveNotification is null)
        {
            return;
        }

        _notificationService.Dismiss(ActiveNotification);
    }

    private void AddLog(string message, string severity)
    {
        _cleanup.AddLog(message, severity);
    }
}

using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SystemCleaner.App.Services;
using SystemCleaner.App.Settings;
using SystemCleaner.App.ViewModels;
using SystemCleaner.Core.Modules;
using SystemCleaner.Core.Services;
using SystemCleaner.Core.Startup;
using SystemCleaner.Core.Uninstall;

namespace SystemCleaner.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var modules = CleanupModuleCatalog.CreateDefaultModules();
        var cleanupService = new CleanupService(modules);
        var themeService = new ThemeService();
        var startupDiscoveryService = new StartupDiscoveryService();
        var uninstallerService = new UninstallerService();
        var hardwareMonitorService = new HardwareMonitorService();
        var confirmationService = new UserConfirmationService();
        var settingsService = new AppSettingsService();
        var virusTotalService = new VirusTotalService();
        var mainViewModel = new MainViewModel(cleanupService, themeService, startupDiscoveryService, uninstallerService, hardwareMonitorService, confirmationService, settingsService, virusTotalService);
        var mainWindow = new MainWindow
        {
            DataContext = mainViewModel
        };

        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (MainWindow?.DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logPath = DiagnosticLogger.Log(e.Exception, "Dispatcher");
        ShowCrashMessage(e.Exception.Message, logPath);
        e.Handled = true;
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            var logPath = DiagnosticLogger.Log(exception, "AppDomain");
            ShowCrashMessage(exception.Message, logPath);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        var logPath = DiagnosticLogger.Log(e.Exception, "TaskScheduler");
        ShowCrashMessage(e.Exception.Message, logPath);
        e.SetObserved();
    }

    private static void ShowCrashMessage(string message, string logPath)
    {
        var fullMessage = $"System Cleaner hit an unexpected error. Details: {message}\nLog file: {logPath}";

        void ShowMessage()
        {
            var owner = Current?.MainWindow;
            if (owner != null)
            {
                MessageBox.Show(owner, fullMessage, "System Cleaner", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                MessageBox.Show(fullMessage, "System Cleaner", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        if (Current?.Dispatcher?.CheckAccess() == true)
        {
            ShowMessage();
        }
        else
        {
            Current?.Dispatcher?.Invoke(ShowMessage);
        }
    }
}




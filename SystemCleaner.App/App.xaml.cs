using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
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
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
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

        _serviceProvider?.Dispose();

        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ICleanupService>(_ => new CleanupService(CleanupModuleCatalog.CreateDefaultModules()));
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IStartupDiscoveryService, StartupDiscoveryService>();
        services.AddSingleton<IUninstallerService, UninstallerService>();
        services.AddSingleton<IHardwareMonitorService, HardwareMonitorService>();
        services.AddSingleton<IUserConfirmationService, UserConfirmationService>();
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<IVirusTotalService, VirusTotalService>();
        services.AddSingleton<MainViewModel>();
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




using System;
using System.Threading.Tasks;
using System.Windows;

namespace SystemCleaner.App.Services;

public interface IUserConfirmationService
{
    bool RequireConfirmation { get; set; }

    Task<bool> ConfirmAsync(string title, string message);
}

public sealed class UserConfirmationService : IUserConfirmationService
{
    private bool _requireConfirmation = true;

    public bool RequireConfirmation
    {
        get => _requireConfirmation;
        set => _requireConfirmation = value;
    }

    public Task<bool> ConfirmAsync(string title, string message)
    {
        if (!RequireConfirmation)
        {
            return Task.FromResult(true);
        }

        bool result;
        var app = Application.Current;
        if (app?.Dispatcher?.CheckAccess() == true)
        {
            result = ShowDialog(title, message, app.MainWindow);
        }
        else if (app?.Dispatcher is not null)
        {
            result = app.Dispatcher.Invoke(() => ShowDialog(title, message, app.MainWindow));
        }
        else
        {
            result = ShowDialog(title, message, owner: null);
        }

        return Task.FromResult(result);
    }

    private static bool ShowDialog(string title, string message, Window? owner)
    {
        var options = MessageBoxButton.YesNo;
        var icon = MessageBoxImage.Question;
        var defaultResult = MessageBoxResult.No;
        if (owner is not null)
        {
            return MessageBox.Show(owner, message, title, options, icon, defaultResult) == MessageBoxResult.Yes;
        }

        return MessageBox.Show(message, title, options, icon, defaultResult) == MessageBoxResult.Yes;
    }
}

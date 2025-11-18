using System.Windows;
using System.Windows.Controls;
using SystemCleaner.App.ViewModels;

namespace SystemCleaner.App.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.UpdatePendingVirusTotalApiKey(ApiKeyBox.Password);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        viewModel.UpdatePendingVirusTotalApiKey(ApiKeyBox.Password);
        var saved = viewModel.SavePendingVirusTotalApiKey();
        if (saved)
        {
            ApiKeyBox.Password = string.Empty;
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ClearVirusTotalApiKey();
        }

        ApiKeyBox.Password = string.Empty;
    }
}

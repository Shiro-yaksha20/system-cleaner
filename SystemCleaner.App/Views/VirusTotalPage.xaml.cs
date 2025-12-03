using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using SystemCleaner.App.ViewModels;

namespace SystemCleaner.App.Views;

public partial class VirusTotalPage : UserControl
{
    public VirusTotalPage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
        IsVisibleChanged += OnIsVisibleChanged;
        DataContextChanged += OnDataContextChanged;
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Uri?.AbsoluteUri))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
        e.Handled = true;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        RequestQuotaRefreshIfNeeded();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            RequestQuotaRefreshIfNeeded();
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            RequestQuotaRefreshIfNeeded();
        }
    }

    private void RequestQuotaRefreshIfNeeded()
    {
        if (DataContext is VirusTotalViewModel viewModel)
        {
            viewModel.EnsureQuotaRefreshOnNavigate();
        }
    }
}

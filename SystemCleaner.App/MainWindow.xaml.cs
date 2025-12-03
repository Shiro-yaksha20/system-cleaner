using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SystemCleaner.App.ViewModels;
using SystemCleaner.App.Services;

namespace SystemCleaner.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        StateChanged += OnWindowStateChanged;
        ApplyWindowStateLayout();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        try
        {
            await viewModel.InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log(ex, nameof(MainWindow));
            MessageBox.Show(this,
                "System Cleaner failed to initialize. Please check the logs for more details.",
                "Initialization Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        ApplyWindowStateLayout();
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source)
        {
            if (FindAncestor<TabPanel>(source) is not null ||
                FindAncestor<TabItem>(source) is not null ||
                FindAncestor<Button>(source) is not null)
            {
                return;
            }
        }

        if (e.ClickCount == 2)
        {
            MaximizeButton_Click(sender, e);
            return;
        }

        DragMove();
    }

    private static T? FindAncestor<T>(DependencyObject current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        ApplyWindowStateLayout();
    }

    private void ApplyWindowStateLayout()
    {
        if (ShellBorder is null)
        {
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            ShellBorder.Margin = new Thickness(0);
            ShellBorder.Padding = new Thickness(6);
            ShellBorder.CornerRadius = new CornerRadius(0);
        }
        else
        {
            ShellBorder.Margin = new Thickness(12);
            ShellBorder.Padding = new Thickness(12);
            ShellBorder.CornerRadius = new CornerRadius(12);
        }
    }
}
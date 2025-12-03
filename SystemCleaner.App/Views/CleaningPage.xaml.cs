using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SystemCleaner.App.Views;

public partial class CleaningPage : UserControl
{
    public CleaningPage()
    {
        InitializeComponent();
    }

    private void MoreActionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is null)
        {
            return;
        }

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
    }
}

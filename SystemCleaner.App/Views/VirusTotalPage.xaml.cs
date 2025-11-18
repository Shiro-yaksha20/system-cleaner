using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace SystemCleaner.App.Views;

public partial class VirusTotalPage : UserControl
{
    public VirusTotalPage()
    {
        InitializeComponent();
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
}

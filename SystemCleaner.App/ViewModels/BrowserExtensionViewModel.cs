using SystemCleaner.Core.Uninstall;

namespace SystemCleaner.App.ViewModels;

public sealed class BrowserExtensionViewModel : ObservableObject
{
    public BrowserExtensionViewModel(BrowserExtensionInfo extension)
    {
        Extension = extension;
    }

    public BrowserExtensionInfo Extension { get; }

    public string Browser => Extension.Browser;

    public string Name => Extension.Name;

    public string Location => Extension.Location;

    public bool CanRemove => Extension.CanRemove;

    public bool Flagged => Extension.Flagged;
}

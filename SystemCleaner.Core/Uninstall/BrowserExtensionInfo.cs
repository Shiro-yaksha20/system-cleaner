namespace SystemCleaner.Core.Uninstall;

public sealed class BrowserExtensionInfo
{
    public BrowserExtensionInfo(string browser, string name, string location, bool canRemove, bool flagged)
    {
        Browser = browser;
        Name = name;
        Location = location;
        CanRemove = canRemove;
        Flagged = flagged;
    }

    public string Browser { get; }

    public string Name { get; }

    public string Location { get; }

    public bool CanRemove { get; }

    public bool Flagged { get; }
}

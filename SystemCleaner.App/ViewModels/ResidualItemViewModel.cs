using System;
using SystemCleaner.App.Utilities;
using SystemCleaner.Core.Uninstall;

namespace SystemCleaner.App.ViewModels;

public sealed class ResidualItemViewModel : ObservableObject
{
    private bool _isSelected = true;

    public ResidualItemViewModel(ResidualItem item)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
    }

    public ResidualItem Item { get; }

    public string ApplicationName => Item.ApplicationName;

    public string Path => Item.Path;

    public string Source => Item.Source;

    public string KindDisplay => Item.Kind.ToString();

    public string SizeDisplay => Item.SizeBytes.HasValue
        ? SizeFormatter.FormatSize(Item.SizeBytes.Value)
        : "-";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

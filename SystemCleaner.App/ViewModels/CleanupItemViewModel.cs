using System;
using SystemCleaner.App.Utilities;
using SystemCleaner.Core.Models;

namespace SystemCleaner.App.ViewModels;

public sealed class CleanupItemViewModel : ObservableObject
{
    private bool _isSelected = true;

    public CleanupItemViewModel(CleanupItem item)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
    }

    public CleanupItem Item { get; }

    public string DisplayName => Item.DisplayName;

    public string Path => Item.Path;

    public string SizeDisplay => SizeFormatter.FormatSize(Item.SizeBytes);

    public bool RequiresElevation => Item.RequiresElevation;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

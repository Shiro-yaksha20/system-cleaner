using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using SystemCleaner.App.Utilities;
using SystemCleaner.Core.Models;

namespace SystemCleaner.App.ViewModels;

public sealed class CleanupModuleViewModel : ObservableObject
{
    private readonly CleanupModuleInfo _moduleInfo;
    private ObservableCollection<CleanupItemViewModel> _items;
    private CleanupItemViewModel? _selectedItem;

    public CleanupModuleViewModel(CleanupModuleInfo moduleInfo)
    {
        _moduleInfo = moduleInfo ?? throw new ArgumentNullException(nameof(moduleInfo));
        _items = new ObservableCollection<CleanupItemViewModel>();
        _items.CollectionChanged += OnItemsCollectionChanged;
    }

    public string Id => _moduleInfo.Id;

    public string Name => _moduleInfo.Name;

    public string Description => _moduleInfo.Description;

    public bool IsQuickCleanSafe => _moduleInfo.IsQuickCleanSafe;

    public string? Warning => _moduleInfo.Warning;

    public ObservableCollection<CleanupItemViewModel> Items => _items;

    public CleanupItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    public long SelectedSizeBytes => _items.Where(static item => item.IsSelected).Sum(static item => item.Item.SizeBytes);

    public string SelectedSizeDisplay => SizeFormatter.FormatSize(SelectedSizeBytes);

    public long TotalSizeBytes => _items.Sum(static item => item.Item.SizeBytes);

    public string TotalSizeDisplay => SizeFormatter.FormatSize(TotalSizeBytes);

    public void SetItems(IEnumerable<CleanupItem> items)
    {
        foreach (var existing in _items)
        {
            existing.PropertyChanged -= OnItemPropertyChanged;
        }

        _items.CollectionChanged -= OnItemsCollectionChanged;
        _items = new ObservableCollection<CleanupItemViewModel>(items.Select(item => CreateItemViewModel(item)));
        _items.CollectionChanged += OnItemsCollectionChanged;
        RaisePropertyChanged(nameof(Items));
        SelectedItem = _items.FirstOrDefault();
        RaiseTotalsChanged();
    }

    public void SelectAll(bool isSelected)
    {
        foreach (var item in _items)
        {
            item.IsSelected = isSelected;
        }
    }

    private CleanupItemViewModel CreateItemViewModel(CleanupItem item)
    {
        var viewModel = new CleanupItemViewModel(item);
        viewModel.PropertyChanged += OnItemPropertyChanged;
        return viewModel;
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (CleanupItemViewModel item in e.OldItems)
            {
                item.PropertyChanged -= OnItemPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (CleanupItemViewModel item in e.NewItems)
            {
                item.PropertyChanged += OnItemPropertyChanged;
            }
        }

        if (_selectedItem is not null && !_items.Contains(_selectedItem))
        {
            SelectedItem = _items.FirstOrDefault();
        }

        RaiseTotalsChanged();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CleanupItemViewModel.IsSelected))
        {
            RaiseTotalsChanged();
        }
    }

    private void RaiseTotalsChanged()
    {
        RaisePropertyChanged(nameof(SelectedSizeBytes));
        RaisePropertyChanged(nameof(SelectedSizeDisplay));
        RaisePropertyChanged(nameof(TotalSizeBytes));
        RaisePropertyChanged(nameof(TotalSizeDisplay));
    }
}

using System;
using SystemCleaner.App.Utilities;
using SystemCleaner.Core.Models;

namespace SystemCleaner.App.ViewModels;

public sealed class ModuleSummaryViewModel : ObservableObject
{
    private int _lastItemCount;
    private long _lastTotalBytes;
    private DateTime? _lastScannedAt;

    public ModuleSummaryViewModel(CleanupModuleInfo moduleInfo)
    {
        if (moduleInfo is null)
        {
            throw new ArgumentNullException(nameof(moduleInfo));
        }

        Id = moduleInfo.Id;
        Name = moduleInfo.Name;
        Description = moduleInfo.Description;
        IsQuickCleanSafe = moduleInfo.IsQuickCleanSafe;
        Warning = moduleInfo.Warning;
    }

    public string Id { get; }

    public string Name { get; }

    public string Description { get; }

    public bool IsQuickCleanSafe { get; }

    public string? Warning { get; }

    public int LastItemCount
    {
        get => _lastItemCount;
        private set
        {
            if (SetProperty(ref _lastItemCount, value))
            {
                RaiseDerivedChanges();
            }
        }
    }

    public long LastTotalBytes
    {
        get => _lastTotalBytes;
        private set
        {
            if (SetProperty(ref _lastTotalBytes, value))
            {
                RaiseDerivedChanges();
            }
        }
    }

    public DateTime? LastScannedAt
    {
        get => _lastScannedAt;
        private set
        {
            if (SetProperty(ref _lastScannedAt, value))
            {
                RaiseDerivedChanges();
            }
        }
    }

    public string LastScanSummary => LastItemCount == 0
        ? "Not scanned yet"
        : $"{LastItemCount} item{(LastItemCount == 1 ? string.Empty : "s")} • {SizeFormatter.FormatSize(LastTotalBytes)}";

    public string LastScannedDisplay => LastScannedAt is null
        ? "Scan pending"
        : LastScannedAt.Value.ToLocalTime().ToString("g");

    public bool HasWarning => !string.IsNullOrWhiteSpace(Warning);

    public string QuickCleanGuidance => IsQuickCleanSafe ? "Safe for Quick Clean" : "Manual review recommended";

    public void Update(int itemCount, long totalBytes, DateTime? scannedAt)
    {
        LastItemCount = itemCount;
        LastTotalBytes = totalBytes;
        LastScannedAt = scannedAt;
    }

    private void RaiseDerivedChanges()
    {
        RaisePropertyChanged(nameof(LastScanSummary));
        RaisePropertyChanged(nameof(LastScannedDisplay));
    }
}

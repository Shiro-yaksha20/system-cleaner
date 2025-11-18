using System;
using System.Collections.ObjectModel;
using System.Globalization;
using SystemCleaner.App.Services;
using SystemCleaner.App.Utilities;

namespace SystemCleaner.App.ViewModels;

public sealed class VirusTotalSubmissionViewModel : ObservableObject
{
    private string _displayName = string.Empty;
    private string _submissionType = string.Empty;
    private DateTimeOffset _submittedAt;
    private string _status = string.Empty;
    private int _malicious;
    private int _suspicious;
    private int _harmless;
    private int _undetected;
    private int _timeout;
    private string? _sha256;
    private string? _permalink;
    private string? _pendingMessage;
    private long? _sizeBytes;
    private string? _fileType;
    private int? _reputation;
    private readonly ObservableCollection<string> _tags = new();

    public VirusTotalSubmissionViewModel(string analysisId)
    {
        AnalysisId = analysisId;
    }

    public string AnalysisId { get; }

    public string DisplayName
    {
        get => _displayName;
        private set => SetProperty(ref _displayName, value);
    }

    public string SubmissionType
    {
        get => _submissionType;
        private set => SetProperty(ref _submissionType, value);
    }

    public DateTimeOffset SubmittedAt
    {
        get => _submittedAt;
        private set
        {
            if (SetProperty(ref _submittedAt, value))
            {
                RaisePropertyChanged(nameof(SubmittedAtDisplay));
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public int MaliciousCount
    {
        get => _malicious;
        private set
        {
            if (SetProperty(ref _malicious, value))
            {
                NotifyDetectionPropertyChanges();
            }
        }
    }

    public int SuspiciousCount
    {
        get => _suspicious;
        private set
        {
            if (SetProperty(ref _suspicious, value))
            {
                NotifyDetectionPropertyChanges();
            }
        }
    }

    public int HarmlessCount
    {
        get => _harmless;
        private set
        {
            if (SetProperty(ref _harmless, value))
            {
                NotifyDetectionPropertyChanges();
            }
        }
    }

    public int UndetectedCount
    {
        get => _undetected;
        private set
        {
            if (SetProperty(ref _undetected, value))
            {
                NotifyDetectionPropertyChanges();
            }
        }
    }

    public int TimeoutCount
    {
        get => _timeout;
        private set => SetProperty(ref _timeout, value);
    }

    public string? Sha256
    {
        get => _sha256;
        private set => SetProperty(ref _sha256, value);
    }

    public string? Permalink
    {
        get => _permalink;
        private set => SetProperty(ref _permalink, value);
    }

    public string Summary
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_pendingMessage))
            {
                return _pendingMessage!;
            }

            return DetectionHeadline;
        }
    }

    public ObservableCollection<VirusTotalEngineResultViewModel> Engines { get; } = new();

    public int VendorCount => Engines.Count;

    public string DetectionRatio => VendorCount > 0 ? $"{MaliciousCount}/{VendorCount}" : "0/0";

    public bool HasDetections => MaliciousCount + SuspiciousCount > 0;

    public string DetectionHeadline => HasDetections
        ? $"{MaliciousCount + SuspiciousCount} security vendor{(MaliciousCount + SuspiciousCount == 1 ? string.Empty : "s")} flagged this resource"
        : "No security vendors flagged this resource as malicious.";

    public string DetectionDetails => VendorCount > 0
        ? $"{MaliciousCount} malicious • {SuspiciousCount} suspicious • {HarmlessCount} harmless • {UndetectedCount} undetected"
        : "Awaiting vendor verdicts.";

    public string SubmittedAtDisplay => SubmittedAt == default ? "Not available" : SubmittedAt.ToString("g", CultureInfo.CurrentCulture);

    public string SizeDisplay => _sizeBytes.HasValue ? SizeFormatter.FormatSize(_sizeBytes.Value) : "Unknown";

    public string FileTypeDisplay => string.IsNullOrWhiteSpace(_fileType) ? "Unknown" : _fileType!;

    public string VendorSummary => VendorCount > 0 ? $"{VendorCount} engines" : "No engines yet";

    public string ReputationDisplay => _reputation.HasValue ? _reputation.Value.ToString(CultureInfo.CurrentCulture) : "—";

    public ObservableCollection<string> Tags => _tags;

    public bool HasTags => _tags.Count > 0;

    public void Update(VirusTotalAnalysis analysis, string? fallbackName = null)
    {
        _pendingMessage = null;
        var name = string.IsNullOrWhiteSpace(analysis.DisplayName) ? fallbackName : analysis.DisplayName;
        DisplayName = string.IsNullOrWhiteSpace(name) ? "(unknown)" : name;
        SubmissionType = analysis.SubmissionType;
        SubmittedAt = analysis.SubmittedAt;
        Status = analysis.Status;
        MaliciousCount = analysis.Malicious;
        SuspiciousCount = analysis.Suspicious;
        HarmlessCount = analysis.Harmless;
        UndetectedCount = analysis.Undetected;
        TimeoutCount = analysis.Timeout;
        Sha256 = analysis.Sha256;
        Permalink = analysis.Permalink;
        _sizeBytes = analysis.SizeBytes;
        _fileType = analysis.FileType;
        _reputation = analysis.Reputation;

        _tags.Clear();
        foreach (var tag in analysis.Tags)
        {
            if (!string.IsNullOrWhiteSpace(tag))
            {
                _tags.Add(tag);
            }
        }

        Engines.Clear();
        foreach (var result in analysis.Engines)
        {
            Engines.Add(new VirusTotalEngineResultViewModel
            {
                Engine = result.EngineName,
                Category = result.Category,
                Result = string.IsNullOrWhiteSpace(result.Result) ? result.Category : result.Result
            });
        }

        RaisePropertyChanged(nameof(Engines));
        RaisePropertyChanged(nameof(VendorCount));
        RaisePropertyChanged(nameof(DetectionRatio));
        RaisePropertyChanged(nameof(HasDetections));
        RaisePropertyChanged(nameof(DetectionHeadline));
        RaisePropertyChanged(nameof(DetectionDetails));
        RaisePropertyChanged(nameof(SubmittedAtDisplay));
        RaisePropertyChanged(nameof(SizeDisplay));
        RaisePropertyChanged(nameof(FileTypeDisplay));
        RaisePropertyChanged(nameof(VendorSummary));
        RaisePropertyChanged(nameof(ReputationDisplay));
        RaisePropertyChanged(nameof(Tags));
        RaisePropertyChanged(nameof(HasTags));
        RaisePropertyChanged(nameof(Summary));
    }

    public void MarkPending(string statusMessage, string submissionType, string? fallbackName = null)
    {
        _pendingMessage = statusMessage;
        DisplayName = string.IsNullOrWhiteSpace(fallbackName) ? "(pending)" : fallbackName;
        SubmissionType = submissionType;
        SubmittedAt = DateTimeOffset.Now;
        Status = statusMessage;
        MaliciousCount = 0;
        SuspiciousCount = 0;
        HarmlessCount = 0;
        UndetectedCount = 0;
        TimeoutCount = 0;
        Sha256 = null;
        Permalink = null;
        _sizeBytes = null;
        _fileType = null;
        _reputation = null;

        Engines.Clear();
        _tags.Clear();
        RaisePropertyChanged(nameof(Engines));
        RaisePropertyChanged(nameof(VendorCount));
        RaisePropertyChanged(nameof(DetectionRatio));
        RaisePropertyChanged(nameof(HasDetections));
        RaisePropertyChanged(nameof(DetectionHeadline));
        RaisePropertyChanged(nameof(DetectionDetails));
        RaisePropertyChanged(nameof(SubmittedAtDisplay));
        RaisePropertyChanged(nameof(SizeDisplay));
        RaisePropertyChanged(nameof(FileTypeDisplay));
        RaisePropertyChanged(nameof(VendorSummary));
        RaisePropertyChanged(nameof(ReputationDisplay));
        RaisePropertyChanged(nameof(Tags));
        RaisePropertyChanged(nameof(HasTags));
        RaisePropertyChanged(nameof(Summary));
    }

    public void SetPendingMessage(string statusMessage)
    {
        _pendingMessage = statusMessage;
        RaisePropertyChanged(nameof(DetectionHeadline));
        RaisePropertyChanged(nameof(DetectionDetails));
        RaisePropertyChanged(nameof(Summary));
    }

    private void NotifyDetectionPropertyChanges()
    {
        RaisePropertyChanged(nameof(Summary));
        RaisePropertyChanged(nameof(DetectionRatio));
        RaisePropertyChanged(nameof(HasDetections));
        RaisePropertyChanged(nameof(DetectionHeadline));
        RaisePropertyChanged(nameof(DetectionDetails));
    }
}

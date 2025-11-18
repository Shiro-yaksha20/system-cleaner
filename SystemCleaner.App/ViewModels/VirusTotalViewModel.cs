using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SystemCleaner.App.Services;

namespace SystemCleaner.App.ViewModels;

public sealed class VirusTotalViewModel : ObservableObject, IDisposable
{
    private readonly VirusTotalService _service;
    private readonly RelayCommand _browseFileCommand;
    private readonly RelayCommand _scanFileCommand;
    private readonly RelayCommand _scanUrlCommand;
    private readonly RelayCommand _refreshCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly ObservableCollection<VirusTotalSubmissionViewModel> _history = new();
    private CancellationTokenSource? _operationCts;
    private CancellationTokenSource? _quotaRefreshCts;
    private bool _disposed;
    private string? _selectedFilePath;
    private string _urlInput = string.Empty;
    private VirusTotalSubmissionViewModel? _selectedSubmission;
    private bool _isBusy;
    private string _statusMessage = "Ready.";
    private bool _hasQuotaInfo;
    private string _quotaSummary = "Quota unavailable.";
    private string _quotaDetails = "Add your VirusTotal API key in Settings to track rate limits.";

    public VirusTotalViewModel(VirusTotalService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _service.ApiKeyChanged += OnApiKeyChanged;
        _service.QuotaUpdated += OnQuotaUpdated;

        ResetQuotaInfo();
        ApplyQuota(_service.LatestQuota);
        if (HasApiKey)
        {
            RequestQuotaRefresh();
        }

        _browseFileCommand = new RelayCommand(BrowseFile);
        _scanFileCommand = new RelayCommand(async () => await ScanFileAsync(), CanExecuteScanFile);
        _scanUrlCommand = new RelayCommand(async () => await ScanUrlAsync(), CanExecuteScanUrl);
        _refreshCommand = new RelayCommand(async () => await RefreshSelectedAsync(), CanExecuteRefresh);
        _cancelCommand = new RelayCommand(() => CancelCurrentOperation(), () => IsBusy);

        if (!HasApiKey)
        {
            StatusMessage = "Add your VirusTotal API key in Settings to submit scans.";
        }
    }

    public ObservableCollection<VirusTotalSubmissionViewModel> History => _history;

    public ICommand BrowseFileCommand => _browseFileCommand;

    public ICommand ScanFileCommand => _scanFileCommand;

    public ICommand ScanUrlCommand => _scanUrlCommand;

    public ICommand RefreshCommand => _refreshCommand;

    public ICommand CancelCommand => _cancelCommand;

    public string? SelectedFilePath
    {
        get => _selectedFilePath;
        set
        {
            if (SetProperty(ref _selectedFilePath, value))
            {
                _scanFileCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string UrlInput
    {
        get => _urlInput;
        set
        {
            if (SetProperty(ref _urlInput, value))
            {
                _scanUrlCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public VirusTotalSubmissionViewModel? SelectedSubmission
    {
        get => _selectedSubmission;
        set
        {
            if (SetProperty(ref _selectedSubmission, value))
            {
                _refreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool HasQuotaInfo
    {
        get => _hasQuotaInfo;
        private set => SetProperty(ref _hasQuotaInfo, value);
    }

    public string QuotaSummary
    {
        get => _quotaSummary;
        private set => SetProperty(ref _quotaSummary, value);
    }

    public string QuotaDetails
    {
        get => _quotaDetails;
        private set => SetProperty(ref _quotaDetails, value);
    }

    public bool HasApiKey => _service.HasApiKey;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _service.ApiKeyChanged -= OnApiKeyChanged;
        _service.QuotaUpdated -= OnQuotaUpdated;
        CancelCurrentOperation();
        CancelQuotaRefresh();
    }

    private bool CanExecuteScanFile()
    {
        return HasApiKey && !IsBusy && !string.IsNullOrWhiteSpace(SelectedFilePath) && File.Exists(SelectedFilePath);
    }

    private bool CanExecuteScanUrl()
    {
        return HasApiKey && !IsBusy && !string.IsNullOrWhiteSpace(UrlInput);
    }

    private bool CanExecuteRefresh()
    {
        return HasApiKey && !IsBusy && SelectedSubmission is not null;
    }

    private void BrowseFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a file to analyze",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            SelectedFilePath = dialog.FileName;
            StatusMessage = $"Ready to scan {Path.GetFileName(dialog.FileName)}.";
        }
    }

    private async Task ScanFileAsync()
    {
        if (!CanExecuteScanFile())
        {
            if (!HasApiKey)
            {
                StatusMessage = "Enter your VirusTotal API key in Settings.";
            }
            return;
        }

        var path = SelectedFilePath!;
        SelectedSubmission = null;
        await RunOperationAsync(async token =>
        {
            StatusMessage = "Uploading file to VirusTotal...";
            var analysis = await _service.AnalyzeFileAsync(path, token);
            HandleAnalysisResult(analysis, Path.GetFileName(path));
        }, ex => HandleSubmissionException(ex, Path.GetFileName(path), "File"));
    }

    private async Task ScanUrlAsync()
    {
        if (!CanExecuteScanUrl())
        {
            if (!HasApiKey)
            {
                StatusMessage = "Enter your VirusTotal API key in Settings.";
            }
            return;
        }

        var trimmed = UrlInput.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out _))
        {
            StatusMessage = "Enter a valid URL.";
            return;
        }

        SelectedSubmission = null;
        await RunOperationAsync(async token =>
        {
            StatusMessage = "Submitting URL to VirusTotal...";
            var analysis = await _service.AnalyzeUrlAsync(trimmed, token);
            HandleAnalysisResult(analysis, trimmed);
        }, ex => HandleSubmissionException(ex, trimmed, "URL"));
    }

    private async Task RefreshSelectedAsync()
    {
        if (!CanExecuteRefresh())
        {
            return;
        }

        var selected = SelectedSubmission;
        if (selected is null)
        {
            return;
        }

        await RunOperationAsync(async token =>
        {
            StatusMessage = "Refreshing analysis...";
            var analysis = await _service.RefreshAnalysisAsync(selected.AnalysisId, token);
            HandleAnalysisResult(analysis, selected.DisplayName);
        }, ex => HandleSubmissionException(ex, selected.DisplayName, selected.SubmissionType));
    }

    private async Task RunOperationAsync(Func<CancellationToken, Task> operation, Func<VirusTotalException, bool>? handleVirusTotalException = null)
    {
        if (IsBusy)
        {
            return;
        }

        CancelCurrentOperation();
        _operationCts = new CancellationTokenSource();
        IsBusy = true;

        try
        {
            await operation(_operationCts.Token);
            if (!IsBusy)
            {
                return;
            }
            StatusMessage = string.IsNullOrWhiteSpace(StatusMessage) ? "Completed." : StatusMessage;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operation canceled.";
        }
        catch (VirusTotalException ex)
        {
            var handled = handleVirusTotalException is not null && handleVirusTotalException(ex);
            if (!handled)
            {
                StatusMessage = ex.Message;
                DiagnosticLogger.Log(ex, nameof(VirusTotalViewModel));
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "VirusTotal request failed.";
            DiagnosticLogger.Log(ex, nameof(VirusTotalViewModel));
        }
        finally
        {
            CancelCurrentOperation(false);
            IsBusy = false;
        }
    }

    private void HandleAnalysisResult(VirusTotalAnalysis? analysis, string resourceName)
    {
        if (analysis is null)
        {
            StatusMessage = "VirusTotal did not return analysis data.";
            return;
        }

        var existing = _history.FirstOrDefault(item => item.AnalysisId == analysis.Id);
        if (existing is null)
        {
            existing = new VirusTotalSubmissionViewModel(analysis.Id);
            _history.Insert(0, existing);
        }

        existing.Update(analysis, resourceName);
        SelectedSubmission = existing;

        var status = analysis.Status?.Trim().ToLowerInvariant() ?? string.Empty;
        if (status is "queued" or "in-progress" or "running")
        {
            const string pendingMessage = "VirusTotal is still processing this submission. Select it later and click Refresh.";
            existing.SetPendingMessage(pendingMessage);
            StatusMessage = pendingMessage;
            return;
        }

        if (status is "error" or "failed")
        {
            const string failureMessage = "VirusTotal reported a problem with this submission. Try refreshing in a moment.";
            existing.SetPendingMessage(failureMessage);
            StatusMessage = failureMessage;
            return;
        }

        StatusMessage = existing.HasDetections
            ? existing.DetectionHeadline
            : $"Analysis complete. {existing.DetectionHeadline}";

        RequestQuotaRefresh();
    }

    private void OnQuotaUpdated(object? sender, VirusTotalQuotaInfo quota)
    {
        if (Application.Current is { Dispatcher: { } dispatcher } && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(new Action(() => ApplyQuota(quota)));
        }
        else
        {
            ApplyQuota(quota);
        }
    }

    private void ResetQuotaInfo()
    {
        HasQuotaInfo = true;

        if (!HasApiKey)
        {
            QuotaSummary = "Add your API key to see VirusTotal quota.";
            QuotaDetails = "Open Settings, paste your VirusTotal API key, and run a scan to retrieve current usage.";
            return;
        }

        QuotaSummary = "Hover to see VirusTotal quota usage.";
        QuotaDetails = "Run a scan to populate your remaining per-minute and per-day limits.";
    }

    private void ApplyQuota(VirusTotalQuotaInfo quota)
    {
        if (!HasApiKey)
        {
            QuotaSummary = "Add your API key to see VirusTotal quota.";
            QuotaDetails = "Open Settings, paste your VirusTotal API key, and run a scan to retrieve current usage.";
            return;
        }

        if (quota is null || !quota.HasData)
        {
            QuotaSummary = "Quota data pending...";
            QuotaDetails = "VirusTotal will provide rate-limit headers after the next successful response.";
            return;
        }

        QuotaSummary = BuildQuotaSummary(quota);
        QuotaDetails = BuildQuotaDetails(quota);
    }

    private void RequestQuotaRefresh()
    {
        if (!HasApiKey)
        {
            return;
        }

        CancelQuotaRefresh();
        _quotaRefreshCts = new CancellationTokenSource();
        var token = _quotaRefreshCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await _service.RefreshQuotaAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation.
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Log(ex, nameof(VirusTotalViewModel));
            }
        });
    }

    private void CancelQuotaRefresh()
    {
        if (_quotaRefreshCts is null)
        {
            return;
        }

        if (!_quotaRefreshCts.IsCancellationRequested)
        {
            _quotaRefreshCts.Cancel();
        }

        _quotaRefreshCts.Dispose();
        _quotaRefreshCts = null;
    }

    private static string BuildQuotaSummary(VirusTotalQuotaInfo quota)
    {
        var parts = new List<string>();

        var userBucket = SelectPreferredBucket(quota.User);
        if (userBucket is not null)
        {
            parts.Add($"User {FormatSummaryBucket(userBucket)}");
        }

        var appBucket = SelectPreferredBucket(quota.App);
        if (appBucket is not null)
        {
            parts.Add($"App {FormatSummaryBucket(appBucket)}");
        }

        return parts.Count > 0 ? string.Join(" | ", parts) : "Quota data pending...";
    }

    private static string BuildQuotaDetails(VirusTotalQuotaInfo quota)
    {
        var builder = new StringBuilder();
        AppendQuotaSection(builder, "User limits", quota.User);
        if (quota.User.Count > 0 && quota.App.Count > 0)
        {
            builder.AppendLine();
        }

        AppendQuotaSection(builder, "Application limits", quota.App);

        var result = builder.ToString().Trim();
        return result.Length == 0
            ? "VirusTotal returned rate-limit headers but no structured data was parsed."
            : result;
    }

    private static void AppendQuotaSection(StringBuilder builder, string title, IReadOnlyList<VirusTotalQuotaBucket> buckets)
    {
        if (buckets.Count == 0)
        {
            return;
        }

        builder.AppendLine(title + ":");
        foreach (var bucket in SortBuckets(buckets))
        {
            builder.Append("- ");
            builder.AppendLine(FormatDetailBucket(bucket));
        }
    }

    private static IEnumerable<VirusTotalQuotaBucket> SortBuckets(IReadOnlyList<VirusTotalQuotaBucket> buckets)
    {
        return buckets
            .OrderBy(bucket => GetWindowOrder(bucket.Window))
            .ThenBy(bucket => bucket.Window, StringComparer.OrdinalIgnoreCase);
    }

    private static VirusTotalQuotaBucket? SelectPreferredBucket(IReadOnlyList<VirusTotalQuotaBucket> buckets)
    {
        if (buckets.Count == 0)
        {
            return null;
        }

        foreach (var window in new[] { "minute", "hour", "day" })
        {
            var match = buckets.FirstOrDefault(bucket => string.Equals(bucket.Window, window, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return buckets[0];
    }

    private static string FormatSummaryBucket(VirusTotalQuotaBucket bucket)
    {
        var window = NormalizeWindowLabel(bucket.Window);
        var remaining = FormatQuotaValue(bucket.Remaining);
        var limit = FormatQuotaValue(bucket.Limit);
        return $"{remaining}/{limit} {window}";
    }

    private static string FormatDetailBucket(VirusTotalQuotaBucket bucket)
    {
        var window = NormalizeWindowLabel(bucket.Window);
        var remaining = FormatQuotaValue(bucket.Remaining);
        var limit = FormatQuotaValue(bucket.Limit);
        return $"{window}: {remaining} remaining of {limit}";
    }

    private static string NormalizeWindowLabel(string? window)
    {
        return string.IsNullOrWhiteSpace(window) ? "unknown" : window;
    }

    private static string FormatQuotaValue(int value)
    {
        if (value < 0)
        {
            return "?";
        }

        return value.ToString("N0", CultureInfo.CurrentCulture);
    }

    private static int GetWindowOrder(string? window)
    {
        return window?.ToLowerInvariant() switch
        {
            "minute" => 0,
            "hour" => 1,
            "day" => 2,
            "month" => 3,
            _ => 4
        };
    }

    private bool HandleSubmissionException(VirusTotalException exception, string resourceName, string submissionType)
    {
        var handled = false;

        if (TryExtractAnalysisId(exception.Message, out var analysisId))
        {
            var message = "VirusTotal is still processing this submission. Select it later and choose Refresh.";
            var entry = _history.FirstOrDefault(item => item.AnalysisId == analysisId);
            if (entry is null)
            {
                entry = new VirusTotalSubmissionViewModel(analysisId);
                _history.Insert(0, entry);
            }

            entry.MarkPending(message, submissionType, resourceName);
            SelectedSubmission = entry;
            StatusMessage = message;
            handled = true;
            DiagnosticLogger.Log(exception, nameof(VirusTotalViewModel));
        }
        else if (exception.StatusCode == HttpStatusCode.TooManyRequests)
        {
            StatusMessage = "VirusTotal rate limit reached. Wait a moment and try again.";
            handled = true;
            DiagnosticLogger.Log(exception, nameof(VirusTotalViewModel));
        }
        else if (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            StatusMessage = "VirusTotal rejected the API key. Double-check the key in Settings.";
            handled = true;
            DiagnosticLogger.Log(exception, nameof(VirusTotalViewModel));
        }
        else if (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            StatusMessage = "VirusTotal denied the request (HTTP 403). The key might lack file upload permissions or the quota is exhausted.";
            handled = true;
            DiagnosticLogger.Log(exception, nameof(VirusTotalViewModel));
        }
        else if (exception.StatusCode == HttpStatusCode.RequestEntityTooLarge)
        {
            StatusMessage = "VirusTotal refused the file because it exceeds the 32 MB limit for standard API keys.";
            handled = true;
            DiagnosticLogger.Log(exception, nameof(VirusTotalViewModel));
        }
        else if (exception.StatusCode == HttpStatusCode.RequestTimeout)
        {
            StatusMessage = "VirusTotal timed out while processing. Select the submission and click Refresh in a bit.";
            handled = true;
            DiagnosticLogger.Log(exception, nameof(VirusTotalViewModel));
        }

        return handled;
    }

    private static bool TryExtractAnalysisId(string? message, out string analysisId)
    {
        analysisId = string.Empty;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        const string marker = " for ";
        var index = message.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return false;
        }

        var extracted = message[(index + marker.Length)..].Trim();
        if (string.IsNullOrWhiteSpace(extracted))
        {
            return false;
        }

        analysisId = extracted;
        return true;
    }

    private void CancelCurrentOperation(bool requestCancellation = true)
    {
        if (_operationCts is null)
        {
            return;
        }

        if (requestCancellation && !_operationCts.IsCancellationRequested)
        {
            _operationCts.Cancel();
        }

        _operationCts.Dispose();
        _operationCts = null;
    }

    private void RaiseCommandStates()
    {
        _scanFileCommand.RaiseCanExecuteChanged();
        _scanUrlCommand.RaiseCanExecuteChanged();
        _refreshCommand.RaiseCanExecuteChanged();
        _cancelCommand.RaiseCanExecuteChanged();
    }

    private void OnApiKeyChanged(object? sender, EventArgs e)
    {
        RaisePropertyChanged(nameof(HasApiKey));
        RaiseCommandStates();
        if (!HasApiKey)
        {
            StatusMessage = "Add your VirusTotal API key in Settings to submit scans.";
        }

        ResetQuotaInfo();
        if (HasApiKey)
        {
            ApplyQuota(_service.LatestQuota);
            RequestQuotaRefresh();
        }
    }
}

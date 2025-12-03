using System;
using System.Threading;
using System.Threading.Tasks;

namespace SystemCleaner.App.Services;

public interface IVirusTotalService : IDisposable
{
    event EventHandler? ApiKeyChanged;

    event EventHandler<VirusTotalQuotaInfo>? QuotaUpdated;

    event EventHandler<bool>? RateLimitStateChanged;

    bool HasApiKey { get; }

    bool IsWaitingForQuota { get; }

    VirusTotalQuotaInfo LatestQuota { get; }

    void SetApiKey(string? apiKey);

    Task RefreshQuotaAsync(CancellationToken token);

    Task<VirusTotalAnalysis?> AnalyzeFileAsync(string filePath, CancellationToken token);

    Task<VirusTotalAnalysis?> AnalyzeUrlAsync(string url, CancellationToken token);

    Task<VirusTotalAnalysis?> RefreshAnalysisAsync(string analysisId, CancellationToken token);
}

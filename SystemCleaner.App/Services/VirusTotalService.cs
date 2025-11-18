using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SystemCleaner.App.Services;

public sealed class VirusTotalService : IDisposable
{
    private static readonly Uri BaseUri = new("https://www.virustotal.com/api/v3/");
    private readonly HttpClient _httpClient;
    private string? _apiKey;
    private bool _disposed;
    private VirusTotalQuotaInfo _latestQuota = VirusTotalQuotaInfo.Empty;

    public VirusTotalService()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = BaseUri,
            Timeout = TimeSpan.FromSeconds(120)
        };
    }

    public event EventHandler? ApiKeyChanged;

    public event EventHandler<VirusTotalQuotaInfo>? QuotaUpdated;

    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    public VirusTotalQuotaInfo LatestQuota => _latestQuota;

    public void SetApiKey(string? apiKey)
    {
        var normalized = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        if (string.Equals(_apiKey, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _apiKey = normalized;
        _httpClient.DefaultRequestHeaders.Remove("x-apikey");
        if (_apiKey is not null)
        {
            _httpClient.DefaultRequestHeaders.Add("x-apikey", _apiKey);
        }

        ApiKeyChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RefreshQuotaAsync(CancellationToken token)
    {
        EnsureNotDisposed();
        EnsureApiKey();

        VirusTotalQuotaInfo? quota = null;

        quota = await TryFetchQuotaAsync("groups/self", token).ConfigureAwait(false);

        if (quota is null && !string.IsNullOrWhiteSpace(_apiKey))
        {
            quota = await TryFetchQuotaAsync($"groups/{_apiKey}", token).ConfigureAwait(false);
        }

        if (quota is null)
        {
            return;
        }

        _latestQuota = quota;
        QuotaUpdated?.Invoke(this, quota);
    }

    public async Task<VirusTotalAnalysis?> AnalyzeFileAsync(string filePath, CancellationToken token)
    {
        EnsureNotDisposed();
        EnsureApiKey();

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("File not found for VirusTotal analysis.", filePath);
        }

        await using var fileStream = File.OpenRead(filePath);
        using var multipart = new MultipartFormDataContent
        {
            { new StreamContent(fileStream), "file", Path.GetFileName(filePath) }
        };

    using var response = await _httpClient.PostAsync("files", multipart, token).ConfigureAwait(false);
    UpdateQuota(response);
    await EnsureSuccessAsync(response, token).ConfigureAwait(false);
        var analysisId = await ExtractAnalysisIdAsync(response, token).ConfigureAwait(false);
        if (analysisId is null)
        {
            return null;
        }

        var analysis = await WaitForAnalysisAsync(analysisId, allowFileShortcut: true, token).ConfigureAwait(false);
        if (analysis is null)
        {
            return null;
        }

        if (IsFileSubmission(analysis) && HasMeaningfulResults(analysis))
        {
            return analysis;
        }

        return await EnrichAnalysisAsync(analysis, token).ConfigureAwait(false);
    }

    public async Task<VirusTotalAnalysis?> AnalyzeUrlAsync(string url, CancellationToken token)
    {
        EnsureNotDisposed();
        EnsureApiKey();

        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL is required.", nameof(url));
        }

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["url"] = url
        });

    using var response = await _httpClient.PostAsync("urls", content, token).ConfigureAwait(false);
    UpdateQuota(response);
    await EnsureSuccessAsync(response, token).ConfigureAwait(false);
        var analysisId = await ExtractAnalysisIdAsync(response, token).ConfigureAwait(false);
        if (analysisId is null)
        {
            return null;
        }

        var analysis = await WaitForAnalysisAsync(analysisId, allowFileShortcut: false, token).ConfigureAwait(false);
        return await EnrichAnalysisAsync(analysis, token).ConfigureAwait(false);
    }

    public async Task<VirusTotalAnalysis?> RefreshAnalysisAsync(string analysisId, CancellationToken token)
    {
        EnsureNotDisposed();
        EnsureApiKey();

        var analysis = await GetAnalysisAsync(analysisId, token).ConfigureAwait(false);
        return await EnrichAnalysisAsync(analysis, token).ConfigureAwait(false);
    }

    private async Task<VirusTotalAnalysis?> WaitForAnalysisAsync(string analysisId, bool allowFileShortcut, CancellationToken token)
    {
        const int maxAttempts = 15;
        const int delaySeconds = 2;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var analysis = await GetAnalysisAsync(analysisId, token).ConfigureAwait(false);
            if (analysis is null)
            {
                return null;
            }

            if (allowFileShortcut && IsFileSubmission(analysis))
            {
                var resolved = await TryResolveWithFileDetailsAsync(analysis, token).ConfigureAwait(false);
                if (resolved is not null)
                {
                    return resolved;
                }
            }

            if (!string.Equals(analysis.Status, "queued", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(analysis.Status, "in-progress", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(analysis.Status, "running", StringComparison.OrdinalIgnoreCase))
            {
                return analysis;
            }

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token).ConfigureAwait(false);
        }

        var finalAnalysis = await GetAnalysisAsync(analysisId, token).ConfigureAwait(false);
        if (finalAnalysis is null)
        {
            return null;
        }

        if (allowFileShortcut && IsFileSubmission(finalAnalysis))
        {
            var resolved = await TryResolveWithFileDetailsAsync(finalAnalysis, token).ConfigureAwait(false);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return finalAnalysis;
    }

    private static bool IsFileSubmission(VirusTotalAnalysis analysis)
    {
        return string.Equals(analysis.SubmissionType, "File", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateQuota(HttpResponseMessage response)
    {
        if (response is null)
        {
            return;
        }

        try
        {
            var quota = VirusTotalQuotaInfo.FromResponse(response);
            if (!quota.HasData)
            {
                return;
            }

            _latestQuota = quota;
            QuotaUpdated?.Invoke(this, quota);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log(ex, nameof(VirusTotalService));
        }
    }

    private async Task<VirusTotalAnalysis?> TryResolveWithFileDetailsAsync(VirusTotalAnalysis analysis, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(analysis.Sha256))
        {
            return null;
        }

        var fileDetails = await GetFileDetailsAsync(analysis.Sha256, token).ConfigureAwait(false);
        if (fileDetails is null)
        {
            return null;
        }

        if (!HasMeaningfulResults(fileDetails))
        {
            return null;
        }

        return MergeFileAnalysis(analysis, fileDetails);
    }

    private async Task<VirusTotalQuotaInfo?> TryFetchQuotaAsync(string endpoint, CancellationToken token)
    {
        try
        {
            using var response = await _httpClient.GetAsync(endpoint, token).ConfigureAwait(false);
            UpdateQuota(response);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            await EnsureSuccessAsync(response, token).ConfigureAwait(false);
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            return await ParseQuotaAsync(stream, token).ConfigureAwait(false);
        }
        catch (VirusTotalException ex)
        {
            DiagnosticLogger.Log(ex, nameof(VirusTotalService));
            return null;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log(ex, nameof(VirusTotalService));
            return null;
        }
    }

    private static async Task<VirusTotalQuotaInfo?> ParseQuotaAsync(Stream stream, CancellationToken token)
    {
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("data", out var dataElement))
        {
            return null;
        }

        if (!dataElement.TryGetProperty("attributes", out var attributesElement))
        {
            return null;
        }

        if (!attributesElement.TryGetProperty("quotas", out var quotasElement))
        {
            return null;
        }

        return VirusTotalQuotaInfo.FromQuotaPayload(quotasElement);
    }

    private static bool HasMeaningfulResults(VirusTotalAnalysis details)
    {
        if (details.Engines.Count > 0)
        {
            return true;
        }

        var totalVotes = details.Malicious + details.Suspicious + details.Harmless + details.Undetected;
        return totalVotes > 0;
    }

    private async Task<VirusTotalAnalysis?> GetAnalysisAsync(string analysisId, CancellationToken token)
    {
        using var response = await _httpClient.GetAsync($"analyses/{analysisId}", token).ConfigureAwait(false);
        UpdateQuota(response);
        await EnsureSuccessAsync(response, token).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        return await ParseAnalysisAsync(stream, token).ConfigureAwait(false);
    }

    private async Task<VirusTotalAnalysis?> GetFileDetailsAsync(string resourceId, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return null;
        }

        using var response = await _httpClient.GetAsync($"files/{resourceId}", token).ConfigureAwait(false);
        UpdateQuota(response);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        await EnsureSuccessAsync(response, token).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        return await ParseFileAsync(stream, token).ConfigureAwait(false);
    }

    private async Task<VirusTotalAnalysis?> EnrichAnalysisAsync(VirusTotalAnalysis? analysis, CancellationToken token)
    {
        if (analysis is null)
        {
            return null;
        }

        if (!string.Equals(analysis.SubmissionType, "File", StringComparison.OrdinalIgnoreCase))
        {
            return analysis;
        }

        if (string.IsNullOrWhiteSpace(analysis.Sha256))
        {
            return analysis;
        }

        var fileDetails = await GetFileDetailsAsync(analysis.Sha256, token).ConfigureAwait(false);
        if (fileDetails is null)
        {
            return analysis;
        }

        return MergeFileAnalysis(analysis, fileDetails);
    }

    private static async Task<string?> ExtractAnalysisIdAsync(HttpResponseMessage response, CancellationToken token)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
        if (document.RootElement.TryGetProperty("data", out var data) &&
            data.TryGetProperty("id", out var idProperty))
        {
            return idProperty.GetString();
        }

        return null;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var statusCode = response.StatusCode;
        string? message = null;
        string? rawContent = null;
        try
        {
            if (response.Content is not null)
            {
                rawContent = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(rawContent))
            {
                throw new JsonException("Empty response body.");
            }

            using var document = JsonDocument.Parse(rawContent);
            if (document.RootElement.TryGetProperty("error", out var errorElement))
            {
                if (errorElement.TryGetProperty("message", out var messageElement))
                {
                    message = messageElement.GetString();
                }
            }
            else if (document.RootElement.TryGetProperty("errors", out var errorsElement) &&
                     errorsElement.ValueKind == JsonValueKind.Array &&
                     errorsElement.GetArrayLength() > 0)
            {
                var first = errorsElement[0];
                if (first.TryGetProperty("message", out var messageElement))
                {
                    message = messageElement.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // Fall through to generic handling with content snippet.
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            var statusText = $"VirusTotal request failed with status {(int)statusCode} ({statusCode}).";
            if (!string.IsNullOrWhiteSpace(rawContent))
            {
                var snippet = rawContent.Length > 240 ? rawContent[..240] + "..." : rawContent;
                message = $"{statusText} Response: {snippet}";
            }
            else
            {
                message = statusText;
            }
        }

        throw new VirusTotalException(message, statusCode);
    }

    private static async Task<VirusTotalAnalysis?> ParseAnalysisAsync(Stream stream, CancellationToken token)
    {
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("data", out var dataElement))
        {
            return null;
        }

        var attributes = dataElement.TryGetProperty("attributes", out var attributesElement)
            ? attributesElement
            : default;

        var status = attributesElement.ValueKind == JsonValueKind.Object &&
                     attributesElement.TryGetProperty("status", out var statusElement)
            ? statusElement.GetString() ?? "unknown"
            : "unknown";

        var statsElement = attributesElement.ValueKind == JsonValueKind.Object &&
                           attributesElement.TryGetProperty("stats", out var s)
            ? s
            : default;

        int GetStat(string name)
        {
            if (statsElement.ValueKind == JsonValueKind.Object && statsElement.TryGetProperty(name, out var value) && value.TryGetInt32(out var number))
            {
                return number;
            }

            return 0;
        }

        var engineResults = new List<VirusTotalEngineResult>();
        if (attributesElement.ValueKind == JsonValueKind.Object &&
            attributesElement.TryGetProperty("results", out var resultsElement) &&
            resultsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in resultsElement.EnumerateObject())
            {
                var category = property.Value.TryGetProperty("category", out var categoryElement)
                    ? categoryElement.GetString()
                    : null;
                var result = property.Value.TryGetProperty("result", out var resultElement)
                    ? resultElement.GetString()
                    : null;
                engineResults.Add(new VirusTotalEngineResult(property.Name, category, result));
            }
        }

        var submittedAt = DateTimeOffset.UtcNow;
        if (attributesElement.ValueKind == JsonValueKind.Object && attributesElement.TryGetProperty("date", out var dateElement) &&
            dateElement.ValueKind == JsonValueKind.Number && dateElement.TryGetInt64(out var seconds))
        {
            submittedAt = DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime();
        }

        string submissionType = "Analysis";
        string? displayName = null;
        string? sha256 = null;
        if (document.RootElement.TryGetProperty("meta", out var metaElement))
        {
            if (metaElement.TryGetProperty("file_info", out var fileInfo) && fileInfo.ValueKind == JsonValueKind.Object)
            {
                submissionType = "File";
                if (fileInfo.TryGetProperty("file_name", out var fileNameElement))
                {
                    displayName = fileNameElement.GetString();
                }

                if (fileInfo.TryGetProperty("sha256", out var sha256Element))
                {
                    sha256 = sha256Element.GetString();
                }
            }
            else if (metaElement.TryGetProperty("url_info", out var urlInfo) && urlInfo.ValueKind == JsonValueKind.Object)
            {
                submissionType = "URL";
                if (urlInfo.TryGetProperty("url", out var urlElement))
                {
                    displayName = urlElement.GetString();
                }
            }
        }

        if (string.IsNullOrWhiteSpace(displayName) && attributesElement.ValueKind == JsonValueKind.Object)
        {
            if (attributesElement.TryGetProperty("meaningful_name", out var meaningfulName) && meaningfulName.ValueKind == JsonValueKind.String)
            {
                displayName = meaningfulName.GetString();
            }
        }

        displayName ??= dataElement.TryGetProperty("id", out var idElem) ? idElem.GetString() : null;

        string? permalink = null;
        if (dataElement.TryGetProperty("links", out var linksElement) && linksElement.ValueKind == JsonValueKind.Object &&
            linksElement.TryGetProperty("self", out var selfElement))
        {
            permalink = selfElement.GetString();
        }

        var analysisId = dataElement.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;

        return new VirusTotalAnalysis
        {
            Id = analysisId,
            Status = status,
            SubmissionType = submissionType,
            DisplayName = displayName,
            SubmittedAt = submittedAt,
            Malicious = GetStat("malicious"),
            Suspicious = GetStat("suspicious"),
            Harmless = GetStat("harmless"),
            Undetected = GetStat("undetected"),
            Timeout = GetStat("timeout"),
            Sha256 = sha256,
            Permalink = permalink,
            Engines = engineResults
        };
    }

    private static async Task<VirusTotalAnalysis?> ParseFileAsync(Stream stream, CancellationToken token)
    {
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("data", out var dataElement))
        {
            return null;
        }

        var attributes = dataElement.TryGetProperty("attributes", out var attributesElement)
            ? attributesElement
            : default;

        var statsElement = attributes.ValueKind == JsonValueKind.Object &&
                           attributes.TryGetProperty("last_analysis_stats", out var stats)
            ? stats
            : default;

        int GetStat(string name)
        {
            if (statsElement.ValueKind == JsonValueKind.Object && statsElement.TryGetProperty(name, out var value) && value.TryGetInt32(out var number))
            {
                return number;
            }

            return 0;
        }

        var engines = new List<VirusTotalEngineResult>();
        if (attributes.ValueKind == JsonValueKind.Object &&
            attributes.TryGetProperty("last_analysis_results", out var resultsElement) &&
            resultsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in resultsElement.EnumerateObject())
            {
                var node = property.Value;
                var engineName = property.Name;
                if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty("engine_name", out var engineNameElement))
                {
                    engineName = engineNameElement.GetString() ?? engineName;
                }

                string? category = null;
                string? result = null;
                if (node.ValueKind == JsonValueKind.Object)
                {
                    if (node.TryGetProperty("category", out var categoryElement))
                    {
                        category = categoryElement.GetString();
                    }

                    if (node.TryGetProperty("result", out var resultElement))
                    {
                        result = resultElement.GetString();
                    }
                }

                engines.Add(new VirusTotalEngineResult(engineName, category, result));
            }
        }

        var submittedAt = DateTimeOffset.UtcNow;
        if (attributes.ValueKind == JsonValueKind.Object &&
            attributes.TryGetProperty("last_analysis_date", out var dateElement) &&
            dateElement.ValueKind == JsonValueKind.Number &&
            dateElement.TryGetInt64(out var seconds))
        {
            submittedAt = DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime();
        }

        string? displayName = null;
        if (attributes.ValueKind == JsonValueKind.Object &&
            attributes.TryGetProperty("meaningful_name", out var nameElement) &&
            nameElement.ValueKind == JsonValueKind.String)
        {
            displayName = nameElement.GetString();
        }

        if (string.IsNullOrWhiteSpace(displayName) &&
            attributes.ValueKind == JsonValueKind.Object &&
            attributes.TryGetProperty("names", out var namesElement) &&
            namesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var nameCandidate in namesElement.EnumerateArray())
            {
                if (nameCandidate.ValueKind == JsonValueKind.String)
                {
                    displayName = nameCandidate.GetString();
                    if (!string.IsNullOrWhiteSpace(displayName))
                    {
                        break;
                    }
                }
            }
        }

        string? sha256 = null;
        if (attributes.ValueKind == JsonValueKind.Object &&
            attributes.TryGetProperty("sha256", out var shaElement) &&
            shaElement.ValueKind == JsonValueKind.String)
        {
            sha256 = shaElement.GetString();
        }

        long? sizeBytes = null;
        if (attributes.ValueKind == JsonValueKind.Object &&
            attributes.TryGetProperty("size", out var sizeElement) &&
            sizeElement.ValueKind == JsonValueKind.Number &&
            sizeElement.TryGetInt64(out var sizeValue))
        {
            sizeBytes = sizeValue;
        }

        string? fileType = null;
        if (attributes.ValueKind == JsonValueKind.Object &&
            attributes.TryGetProperty("type_description", out var typeElement) &&
            typeElement.ValueKind == JsonValueKind.String)
        {
            fileType = typeElement.GetString();
        }

        var tags = new List<string>();
        if (attributes.ValueKind == JsonValueKind.Object &&
            attributes.TryGetProperty("tags", out var tagsElement) &&
            tagsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var tagElement in tagsElement.EnumerateArray())
            {
                if (tagElement.ValueKind == JsonValueKind.String)
                {
                    var tag = tagElement.GetString();
                    if (!string.IsNullOrWhiteSpace(tag))
                    {
                        tags.Add(tag);
                    }
                }
            }
        }

        var names = new List<string>();
        if (attributes.ValueKind == JsonValueKind.Object &&
            attributes.TryGetProperty("names", out var altNamesElement) &&
            altNamesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in altNamesElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value) && !names.Contains(value))
                    {
                        names.Add(value);
                    }
                }
            }
        }

        int? reputation = null;
        if (attributes.ValueKind == JsonValueKind.Object &&
            attributes.TryGetProperty("reputation", out var reputationElement) &&
            reputationElement.ValueKind == JsonValueKind.Number &&
            reputationElement.TryGetInt32(out var reputationValue))
        {
            reputation = reputationValue;
        }

        string? permalink = null;
        if (dataElement.TryGetProperty("links", out var linksElement) &&
            linksElement.ValueKind == JsonValueKind.Object &&
            linksElement.TryGetProperty("self", out var selfElement) &&
            selfElement.ValueKind == JsonValueKind.String)
        {
            permalink = selfElement.GetString();
        }

        var id = dataElement.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
            ? idElement.GetString() ?? string.Empty
            : string.Empty;

        return new VirusTotalAnalysis
        {
            Id = id,
            Status = "completed",
            SubmissionType = "File",
            DisplayName = displayName,
            SubmittedAt = submittedAt,
            Malicious = GetStat("malicious"),
            Suspicious = GetStat("suspicious"),
            Harmless = GetStat("harmless"),
            Undetected = GetStat("undetected"),
            Timeout = GetStat("timeout"),
            Sha256 = sha256,
            Permalink = permalink,
            SizeBytes = sizeBytes,
            FileType = fileType,
            Tags = tags,
            Names = names,
            Reputation = reputation,
            Engines = engines
        };
    }

    private static VirusTotalAnalysis MergeFileAnalysis(VirusTotalAnalysis original, VirusTotalAnalysis fileDetails)
    {
        return new VirusTotalAnalysis
        {
            Id = original.Id,
            Status = string.IsNullOrWhiteSpace(fileDetails.Status) ? original.Status : fileDetails.Status,
            SubmissionType = original.SubmissionType,
            DisplayName = fileDetails.DisplayName ?? original.DisplayName,
            SubmittedAt = fileDetails.SubmittedAt != default ? fileDetails.SubmittedAt : original.SubmittedAt,
            Malicious = fileDetails.Malicious,
            Suspicious = fileDetails.Suspicious,
            Harmless = fileDetails.Harmless,
            Undetected = fileDetails.Undetected,
            Timeout = fileDetails.Timeout,
            Sha256 = fileDetails.Sha256 ?? original.Sha256,
            Permalink = fileDetails.Permalink ?? original.Permalink,
            SizeBytes = fileDetails.SizeBytes ?? original.SizeBytes,
            FileType = fileDetails.FileType ?? original.FileType,
            Tags = fileDetails.Tags.Count > 0 ? fileDetails.Tags : original.Tags,
            Names = fileDetails.Names.Count > 0 ? fileDetails.Names : original.Names,
            Reputation = fileDetails.Reputation ?? original.Reputation,
            Engines = fileDetails.Engines.Count > 0 ? fileDetails.Engines : original.Engines
        };
    }

    private void EnsureApiKey()
    {
        if (!HasApiKey)
        {
            throw new InvalidOperationException("VirusTotal API key is not configured.");
        }
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(VirusTotalService));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
    }
}

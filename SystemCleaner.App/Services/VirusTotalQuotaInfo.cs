using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Linq;
using System.Text.Json;

namespace SystemCleaner.App.Services;

public sealed record VirusTotalQuotaBucket(string Window, int Limit, int Remaining);

public sealed class VirusTotalQuotaInfo
{
    public static readonly VirusTotalQuotaInfo Empty = new(Array.Empty<VirusTotalQuotaBucket>(), Array.Empty<VirusTotalQuotaBucket>());

    public VirusTotalQuotaInfo(IReadOnlyList<VirusTotalQuotaBucket> appBuckets, IReadOnlyList<VirusTotalQuotaBucket> userBuckets)
    {
        App = appBuckets ?? throw new ArgumentNullException(nameof(appBuckets));
        User = userBuckets ?? throw new ArgumentNullException(nameof(userBuckets));
    }

    public IReadOnlyList<VirusTotalQuotaBucket> App { get; }

    public IReadOnlyList<VirusTotalQuotaBucket> User { get; }

    public bool HasData => App.Count > 0 || User.Count > 0;

    public static VirusTotalQuotaInfo FromResponse(HttpResponseMessage response)
    {
        if (response is null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        var app = BuildBuckets(response, "x-app-rate-limit", "x-app-rate-limit-remaining");
        var user = BuildBuckets(response, "x-user-rate-limit", "x-user-rate-limit-remaining");
        return new VirusTotalQuotaInfo(app, user);
    }

    public static VirusTotalQuotaInfo FromQuotaPayload(JsonElement quotasElement)
    {
        if (quotasElement.ValueKind != JsonValueKind.Object)
        {
            return Empty;
        }

        var app = new List<VirusTotalQuotaBucket>();
        var user = new List<VirusTotalQuotaBucket>();

        foreach (var quotaProperty in quotasElement.EnumerateObject())
        {
            if (quotaProperty.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var limit = ExtractInt(quotaProperty.Value, new[] { "allowed", "limit", "max" });
            var used = ExtractInt(quotaProperty.Value, new[] { "used", "consumed" });
            var remaining = ExtractInt(quotaProperty.Value, new[] { "remaining" });

            if (remaining < 0 && limit >= 0 && used >= 0)
            {
                remaining = Math.Max(0, limit - used);
            }

            var window = DeriveWindowName(quotaProperty.Name);
            var bucket = new VirusTotalQuotaBucket(window, limit, remaining);

            if (quotaProperty.Name.Contains("user", StringComparison.OrdinalIgnoreCase))
            {
                user.Add(bucket);
            }
            else
            {
                app.Add(bucket);
            }
        }

        return new VirusTotalQuotaInfo(app, user);
    }

    private static IReadOnlyList<VirusTotalQuotaBucket> BuildBuckets(HttpResponseMessage response, string limitHeader, string remainingHeader)
    {
        var limits = ParseRatePairs(GetHeaderValues(response, limitHeader));
        var remaining = ParseRatePairs(GetHeaderValues(response, remainingHeader));

        if (limits.Count == 0 && remaining.Count == 0)
        {
            return Array.Empty<VirusTotalQuotaBucket>();
        }

        var windows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var window in limits.Keys)
        {
            windows.Add(window);
        }

        foreach (var window in remaining.Keys)
        {
            windows.Add(window);
        }

        var result = new List<VirusTotalQuotaBucket>();
        foreach (var window in windows)
        {
            var limitValue = limits.TryGetValue(window, out var limit) ? limit : -1;
            var remainingValue = remaining.TryGetValue(window, out var remain) ? remain : -1;
            result.Add(new VirusTotalQuotaBucket(window, limitValue, remainingValue));
        }

        result.Sort((left, right) => string.Compare(left.Window, right.Window, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    private static IEnumerable<string>? GetHeaderValues(HttpResponseMessage response, string headerName)
    {
        if (response.Headers.TryGetValues(headerName, out var values))
        {
            return values;
        }

        if (response.Content is not null && response.Content.Headers.TryGetValues(headerName, out values))
        {
            return values;
        }

        return null;
    }

    private static Dictionary<string, int> ParseRatePairs(IEnumerable<string>? values)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (values is null)
        {
            return result;
        }

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var segments = value.Split(',');
            foreach (var segment in segments)
            {
                var pair = segment.Trim();
                if (pair.Length == 0)
                {
                    continue;
                }

                var separatorIndex = pair.IndexOf('=');
                if (separatorIndex <= 0 || separatorIndex >= pair.Length - 1)
                {
                    continue;
                }

                var key = pair[..separatorIndex].Trim();
                var valuePortion = pair[(separatorIndex + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (int.TryParse(valuePortion, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    result[key] = parsed;
                }
            }
        }

        return result;
    }

    private static int ExtractInt(JsonElement element, IEnumerable<string> propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value))
            {
                return value;
            }
        }

        return -1;
    }

    private static string DeriveWindowName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return "unknown";
        }

        var normalized = rawName.Replace('-', '_');
        var segments = normalized.Split('_');
        if (segments.Length == 0)
        {
            return rawName;
        }

        var windowCandidate = segments.Last();
        if (windowCandidate.Equals("requests", StringComparison.OrdinalIgnoreCase) && segments.Length >= 2)
        {
            windowCandidate = segments[^2];
        }

        return windowCandidate;
    }
}

using System;
using System.Collections.Generic;

namespace SystemCleaner.App.Services;

public sealed class VirusTotalAnalysis
{
    public string Id { get; init; } = string.Empty;

    public string Status { get; init; } = "unknown";

    public string SubmissionType { get; init; } = "Analysis";

    public string? DisplayName { get; init; }

    public DateTimeOffset SubmittedAt { get; init; }

    public int Malicious { get; init; }

    public int Suspicious { get; init; }

    public int Harmless { get; init; }

    public int Undetected { get; init; }

    public int Timeout { get; init; }

    public string? Sha256 { get; init; }

    public string? Permalink { get; init; }

    public long? SizeBytes { get; init; }

    public string? FileType { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Names { get; init; } = Array.Empty<string>();

    public int? Reputation { get; init; }

    public IReadOnlyList<VirusTotalEngineResult> Engines { get; init; } = Array.Empty<VirusTotalEngineResult>();
}

public sealed record VirusTotalEngineResult(string EngineName, string? Category, string? Result);

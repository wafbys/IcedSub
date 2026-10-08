namespace IcedSub.Core.Models;

/// <summary>
/// A single subtitle hit from a provider (before download).
/// </summary>
public sealed class SubtitleCandidate
{
    public required string ProviderName { get; init; }
    public required string ProviderId { get; init; }
    public required string DisplayName { get; init; }
    public required string Language { get; init; }
    public required string Format { get; init; }
    public string? ReleaseName { get; init; }
    public string? HearingImpairedHint { get; init; }
    public double? Score { get; init; }
    public int? DownloadCount { get; init; }
    public string? DetailUrl { get; init; }

    /// <summary>Opaque state the provider needs for DownloadAsync (e.g. file_id, sub id).</summary>
    public required string DownloadKey { get; init; }

    public Dictionary<string, string> Extra { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

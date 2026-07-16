namespace IcedSubDL.Core.Models;

/// <summary>
/// Local video file identity used for subtitle search and naming.
/// </summary>
public sealed class MediaInfo
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required string FileNameWithoutExtension { get; init; }
    public long FileSizeBytes { get; init; }

    /// <summary>OpenSubtitles-style 64-bit movie hash as 16 lowercase hex digits.</summary>
    public string? OpenSubtitlesMovieHash { get; init; }

    /// <summary>Best-effort title parsed from the file name (not authoritative metadata).</summary>
    public string? ParsedTitle { get; init; }

    public int? Year { get; init; }
    public int? Season { get; init; }
    public int? Episode { get; init; }

    public string? ImdbId { get; init; }
    public string? TmdbId { get; init; }

    /// <summary>
    /// True when this is a free-text keyword search (no local video file to read).
    /// </summary>
    public bool IsKeywordQuery { get; init; }

    /// <summary>
    /// Directory used when saving downloads for keyword-only searches.
    /// </summary>
    public string? OutputDirectory { get; init; }
}

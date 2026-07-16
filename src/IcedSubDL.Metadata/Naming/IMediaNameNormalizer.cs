using IcedSubDL.Core.Models;

namespace IcedSubDL.Metadata.Naming;

/// <summary>
/// Placeholder for future authoritative metadata-based file renaming (TMDB/IMDb + rules).
/// </summary>
public interface IMediaNameNormalizer
{
    /// <summary>
    /// Proposes a new file name (without path) for the given media. Does not rename on disk.
    /// </summary>
    Task<NameProposal> ProposeAsync(MediaInfo media, CancellationToken cancellationToken = default);
}

public sealed record NameProposal(
    string OriginalFileName,
    string ProposedFileName,
    string? Source,
    string? Rationale,
    bool IsSupported);

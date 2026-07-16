using IcedSubDL.Core.Models;

namespace IcedSubDL.Metadata.Naming;

/// <summary>
/// Stub until naming rules and metadata providers are designed.
/// </summary>
public sealed class NotImplementedMediaNameNormalizer : IMediaNameNormalizer
{
    public Task<NameProposal> ProposeAsync(MediaInfo media, CancellationToken cancellationToken = default)
    {
        var proposal = new NameProposal(
            OriginalFileName: media.FileName,
            ProposedFileName: media.FileName,
            Source: null,
            Rationale: "Filename normalization is not implemented yet. Rules will use authoritative metadata (e.g. TMDB/IMDb) plus custom patterns.",
            IsSupported: false);

        return Task.FromResult(proposal);
    }
}

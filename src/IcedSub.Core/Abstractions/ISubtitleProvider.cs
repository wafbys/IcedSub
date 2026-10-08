using IcedSub.Core.Models;

namespace IcedSub.Core.Abstractions;

public interface ISubtitleProvider
{
    string Name { get; }
    bool IsConfigured { get; }

    Task<IReadOnlyList<SubtitleCandidate>> SearchAsync(
        MediaInfo media,
        SearchOptions options,
        CancellationToken cancellationToken = default);

    Task<SubtitleFile> DownloadAsync(
        SubtitleCandidate candidate,
        CancellationToken cancellationToken = default);
}

namespace IcedSub.Core.Models;

/// <summary>
/// Downloaded subtitle payload ready to write next to a video.
/// </summary>
public sealed class SubtitleFile
{
    public required SubtitleCandidate Candidate { get; init; }
    public required string SuggestedFileName { get; init; }
    public required byte[] Content { get; init; }
    public required string Format { get; init; }
}

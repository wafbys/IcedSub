using IcedSub.Core.Media;
using IcedSub.Core.Models;

namespace IcedSub.Core.Scoring;

public static class SubtitleScorer
{
    public static double Score(MediaInfo media, SubtitleCandidate candidate, SearchOptions options)
    {
        double score = 0;

        // Language priority
        string lang = SubtitleFileNamer.NormalizeLanguageTag(candidate.Language);
        int langIndex = -1;
        for (int i = 0; i < options.Languages.Count; i++)
        {
            if (string.Equals(
                    SubtitleFileNamer.NormalizeLanguageTag(options.Languages[i]),
                    lang,
                    StringComparison.OrdinalIgnoreCase))
            {
                langIndex = i;
                break;
            }
        }

        if (langIndex >= 0)
            score += 100 - langIndex * 10;
        else
            score += 10;

        // Optional format preference (empty list = no penalty; soft boost only)
        if (options.PreferredFormats is { Count: > 0 })
        {
            if (SrtArchiveExtractor.IsPreferredFormat(candidate.Format, options.PreferredFormats) ||
                SrtArchiveExtractor.IsPreferredFormat(candidate.DisplayName, options.PreferredFormats) ||
                SrtArchiveExtractor.IsPreferredFormat(candidate.ReleaseName, options.PreferredFormats))
                score += 25;
        }
        else
        {
            // Mild preference for common text subs when ranking, but never exclude others
            string fmt = (candidate.Format ?? string.Empty).ToLowerInvariant();
            if (fmt is "srt" or "ass" or "ssa" or "vtt")
                score += 10;
        }

        // Release name similarity to video file name
        if (!string.IsNullOrWhiteSpace(candidate.ReleaseName))
        {
            string a = Normalize(media.FileNameWithoutExtension);
            string b = Normalize(candidate.ReleaseName);
            if (a.Length > 0 && b.Length > 0)
            {
                if (a.Contains(b, StringComparison.OrdinalIgnoreCase) ||
                    b.Contains(a, StringComparison.OrdinalIgnoreCase))
                    score += 25;
                else if (TokenOverlap(a, b) >= 0.5)
                    score += 15;
            }
        }

        if (candidate.DownloadCount is > 0)
            score += Math.Min(20, Math.Log10(candidate.DownloadCount.Value + 1) * 8);

        if (candidate.Score is > 0)
            score += Math.Min(15, candidate.Score.Value / 10.0);

        // Prefer Chinese dual / 双语 slightly when zh is requested
        if (candidate.Language.Contains("双语", StringComparison.Ordinal) ||
            candidate.DisplayName.Contains("双语", StringComparison.Ordinal))
            score += 5;

        return score;
    }

    public static IReadOnlyList<SubtitleCandidate> Rank(
        MediaInfo media,
        IEnumerable<SubtitleCandidate> candidates,
        SearchOptions options)
    {
        return candidates
            .Select(c =>
            {
                double s = Score(media, c, options);
                c.Extra["computed_score"] = s.ToString("0.##");
                return (Candidate: c, Score: s);
            })
            .OrderByDescending(x => x.Score)
            .Select(x => x.Candidate)
            .ToList();
    }

    private static string Normalize(string value) =>
        value.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ').Trim().ToLowerInvariant();

    private static double TokenOverlap(string a, string b)
    {
        var ta = a.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
        var tb = b.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
        if (ta.Count == 0 || tb.Count == 0)
            return 0;
        int inter = ta.Count(t => tb.Contains(t));
        return (double)inter / Math.Max(ta.Count, tb.Count);
    }
}

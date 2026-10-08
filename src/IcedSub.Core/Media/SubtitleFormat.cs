namespace IcedSub.Core.Media;

public static class SubtitleFormat
{
    /// <summary>Guess extension from file name (e.g. .srt / .rar / .ass).</summary>
    public static string FromFileName(string? fileName, string fallback = "bin")
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return fallback;

        string ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        return string.IsNullOrEmpty(ext) ? fallback : ext;
    }

    /// <summary>Map ASSRT/OpenSubtitles subtype labels to a short extension.</summary>
    public static string FromSubtypeLabel(string? subtype, string fallback = "bin")
    {
        if (string.IsNullOrWhiteSpace(subtype))
            return fallback;

        string s = subtype.Trim().ToLowerInvariant();
        if (s.Contains("subrip") || s.Contains("srt"))
            return "srt";
        if (s.Contains("ass") || s.Contains("ssa"))
            return s.Contains("ssa") && !s.Contains("ass") ? "ssa" : "ass";
        if (s.Contains("vobsub") || s.Contains("idx") || s.Contains("sub"))
            return "rar"; // often packaged
        if (s.Contains("sup") || s.Contains("pgs") || s.Contains("blu"))
            return "sup";
        if (s.Contains("smi") || s.Contains("sami"))
            return "smi";
        if (s.Contains("vtt"))
            return "vtt";
        if (s.Contains("rar"))
            return "rar";
        if (s.Contains("zip"))
            return "zip";
        if (s.Contains("7z"))
            return "7z";

        // If it already looks like an extension token
        if (s is { Length: >= 2 and <= 5 } && s.All(char.IsLetterOrDigit))
            return s;

        return fallback;
    }

    public static string CombineHints(string? fileName, string? subtype, string? declaredFormat = null)
    {
        string fromName = FromFileName(fileName, fallback: string.Empty);
        if (!string.IsNullOrEmpty(fromName))
            return fromName;

        if (!string.IsNullOrWhiteSpace(declaredFormat))
        {
            string d = declaredFormat.Trim().TrimStart('.').ToLowerInvariant();
            if (d.Length > 0 && !d.Contains(' ') && d.Length <= 8)
                return d;
        }

        return FromSubtypeLabel(subtype);
    }
}

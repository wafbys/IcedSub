using IcedSub.Core.Models;

namespace IcedSub.Core.Media;

public static class SubtitleFileNamer
{
    /// <summary>
    /// Resolve output directory for a media item (video folder, --out, or cwd).
    /// </summary>
    public static string GetOutputDirectory(MediaInfo media) =>
        media.OutputDirectory
        ?? Path.GetDirectoryName(media.FilePath)
        ?? Environment.CurrentDirectory;

    /// <summary>
    /// Prefer the provider's original file name. Falls back to a generated name only when missing.
    /// If the target already exists and overwrite is false, appends " (2)", " (3)", ... before the extension.
    /// </summary>
    public static string GetOutputPath(
        MediaInfo media,
        SubtitleCandidate candidate,
        SubtitleFile? downloaded = null,
        bool overwriteExisting = false)
    {
        string dir = GetOutputDirectory(media);
        string original = downloaded?.SuggestedFileName
                          ?? candidate.Extra.GetValueOrDefault("original_filename")
                          ?? candidate.ReleaseName
                          ?? string.Empty;

        string fileName = SanitizeFileName(Path.GetFileName(original));
        if (string.IsNullOrWhiteSpace(fileName))
        {
            // Fallback only when the source gives no usable name
            string ext = SubtitleFormat.CombineHints(
                downloaded?.SuggestedFileName,
                candidate.Extra.GetValueOrDefault("subtype"),
                downloaded?.Format ?? candidate.Format);
            fileName = $"{media.FileNameWithoutExtension}.{NormalizeLanguageTag(candidate.Language)}.{candidate.ProviderName}-{candidate.ProviderId}.{ext}";
            fileName = SanitizeFileName(fileName);
        }

        string path = Path.Combine(dir, fileName);
        if (overwriteExisting || !File.Exists(path))
            return path;

        return MakeUniquePath(dir, fileName);
    }

    /// <summary>Legacy helper used by tests / callers that only have format info.</summary>
    public static string GetOutputPath(
        MediaInfo media,
        SubtitleCandidate candidate,
        string format,
        int? index = null,
        bool includeProviderId = false)
    {
        // Prefer original name on candidate if present
        string? original = candidate.Extra.GetValueOrDefault("original_filename");
        if (!string.IsNullOrWhiteSpace(original))
        {
            return GetOutputPath(
                media,
                candidate,
                downloaded: new SubtitleFile
                {
                    Candidate = candidate,
                    SuggestedFileName = original,
                    Content = [],
                    Format = format
                });
        }

        // Generated pattern (fallback)
        string lang = NormalizeLanguageTag(candidate.Language);
        string ext = string.IsNullOrWhiteSpace(format) ? "bin" : format.Trim().TrimStart('.').ToLowerInvariant();
        if (ext.Contains('(') || ext.Contains(' ') || ext.Length > 8)
            ext = SubtitleFormat.FromSubtypeLabel(ext);

        var parts = new List<string> { media.FileNameWithoutExtension, lang };
        if (index is > 0)
            parts.Add(index.Value.ToString("00"));
        if (includeProviderId)
        {
            string id = SanitizeFileNamePart(candidate.ProviderId);
            string provider = SanitizeFileNamePart(candidate.ProviderName);
            if (!string.IsNullOrEmpty(provider) || !string.IsNullOrEmpty(id))
                parts.Add($"{provider}-{id}".Trim('-'));
        }

        string fileName = string.Join('.', parts.Where(p => !string.IsNullOrWhiteSpace(p))) + "." + ext;
        return Path.Combine(GetOutputDirectory(media), fileName);
    }

    public static string SanitizeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        char[] invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        string s = new string(chars).Trim(' ', '.');
        // Windows reserved device names
        string stem = Path.GetFileNameWithoutExtension(s);
        if (stem is "CON" or "PRN" or "AUX" or "NUL"
            or "COM1" or "COM2" or "COM3" or "COM4" or "COM5" or "COM6" or "COM7" or "COM8" or "COM9"
            or "LPT1" or "LPT2" or "LPT3" or "LPT4" or "LPT5" or "LPT6" or "LPT7" or "LPT8" or "LPT9")
        {
            s = "_" + s;
        }

        return s;
    }

    public static string SanitizeFileNamePart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        char[] invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(c => invalid.Contains(c) || c is '.' ? '_' : c).ToArray();
        string s = new string(chars);
        while (s.Contains("__", StringComparison.Ordinal))
            s = s.Replace("__", "_", StringComparison.Ordinal);
        return s.Trim('_');
    }

    public static bool ExternalSrtExists(MediaInfo media, IEnumerable<string>? languages)
    {
        string dir = GetOutputDirectory(media);
        if (!Directory.Exists(dir))
            return false;

        // Any existing external subtitle next to the target is enough for skip-existing
        try
        {
            foreach (string file in Directory.EnumerateFiles(dir, media.FileNameWithoutExtension + ".*"))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is ".srt" or ".ass" or ".ssa" or ".vtt" or ".sup" or ".smi")
                    return true;
            }
        }
        catch
        {
            // ignore
        }

        if (languages is not null)
        {
            foreach (string lang in languages)
            {
                string tag = NormalizeLanguageTag(lang);
                string path = Path.Combine(dir, $"{media.FileNameWithoutExtension}.{tag}.srt");
                if (File.Exists(path))
                    return true;
            }
        }

        string bare = Path.Combine(dir, $"{media.FileNameWithoutExtension}.srt");
        return File.Exists(bare);
    }

    public static string NormalizeLanguageTag(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return "und";

        string l = language.Trim().Replace('_', '-');
        return l.ToLowerInvariant() switch
        {
            "zh" or "chi" or "zho" or "chs" or "zh-hans" or "zh-cn" or "cn" => "zh-CN",
            "cht" or "zh-hant" or "zh-tw" or "zh-hk" or "tc" => "zh-TW",
            "en" or "eng" => "en",
            "ja" or "jpn" => "ja",
            "ko" or "kor" => "ko",
            _ => l
        };
    }

    private static string MakeUniquePath(string dir, string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        for (int i = 2; i < 10_000; i++)
        {
            string candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(dir, $"{name}-{Guid.NewGuid():N}{ext}");
    }
}

using System.Text.RegularExpressions;
using IcedSub.Core.Hashing;
using IcedSub.Core.Models;
using Microsoft.Extensions.Logging;

namespace IcedSub.Core.Media;

public sealed partial class VideoScanner
{
    private readonly AppSettings _settings;
    private readonly ILogger<VideoScanner>? _logger;

    public VideoScanner(AppSettings settings, ILogger<VideoScanner>? logger = null)
    {
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Resolve inputs into media items. Each input may be:
    /// - an existing video file
    /// - an existing directory (recursive video scan)
    /// - otherwise a free-text keyword (no file IO / no hash)
    /// </summary>
    public async Task<IReadOnlyList<MediaInfo>> ScanAsync(
        IEnumerable<string> paths,
        bool computeHash = true,
        string? keywordOutputDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<MediaInfo>();

        foreach (string raw in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            string trimmed = raw.Trim();

            // Existing file?
            string? filePath = ResolveExistingFile(trimmed);
            if (filePath is not null)
            {
                if (!_settings.VideoExtensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase))
                {
                    _logger?.LogWarning("Not a known video extension, skip file: {File}", filePath);
                    continue;
                }

                try
                {
                    results.Add(await CreateMediaInfoAsync(filePath, computeHash, cancellationToken)
                        .ConfigureAwait(false));
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Skip unreadable video: {File}", filePath);
                }

                continue;
            }

            // Existing directory?
            string? dirPath = ResolveExistingDirectory(trimmed);
            if (dirPath is not null)
            {
                var ext = new HashSet<string>(_settings.VideoExtensions, StringComparer.OrdinalIgnoreCase);
                foreach (string file in Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!ext.Contains(Path.GetExtension(file)))
                        continue;

                    try
                    {
                        results.Add(await CreateMediaInfoAsync(file, computeHash, cancellationToken)
                            .ConfigureAwait(false));
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Skip unreadable video: {File}", file);
                    }
                }

                continue;
            }

            // Free-text keyword (no video read)
            results.Add(CreateFromKeyword(trimmed, keywordOutputDirectory));
            _logger?.LogInformation("Keyword search (no video file): {Query}", trimmed);
        }

        return results;
    }

    public static MediaInfo CreateFromKeyword(string keyword, string? outputDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        string q = keyword.Trim();
        var (title, year, season, episode) = ParseFileName(q);

        string outDir = string.IsNullOrWhiteSpace(outputDirectory)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(outputDirectory);

        // Synthetic path used only for naming output files
        string safeName = SanitizeFileName(q);
        return new MediaInfo
        {
            FilePath = Path.Combine(outDir, safeName),
            FileName = safeName,
            FileNameWithoutExtension = safeName,
            FileSizeBytes = 0,
            OpenSubtitlesMovieHash = null,
            ParsedTitle = title ?? q,
            Year = year,
            Season = season,
            Episode = episode,
            IsKeywordQuery = true,
            OutputDirectory = outDir
        };
    }

    public async Task<MediaInfo> CreateMediaInfoAsync(
        string filePath,
        bool computeHash = true,
        CancellationToken cancellationToken = default)
    {
        var fi = new FileInfo(filePath);
        if (!fi.Exists)
            throw new FileNotFoundException("Video file not found.", filePath);

        string nameNoExt = Path.GetFileNameWithoutExtension(fi.Name);
        var (title, year, season, episode) = ParseFileName(nameNoExt);

        string? hash = null;
        if (computeHash)
            hash = await OpenSubtitlesMovieHash.ComputeAsync(fi.FullName, cancellationToken).ConfigureAwait(false);

        return new MediaInfo
        {
            FilePath = fi.FullName,
            FileName = fi.Name,
            FileNameWithoutExtension = nameNoExt,
            FileSizeBytes = fi.Length,
            OpenSubtitlesMovieHash = hash,
            ParsedTitle = title,
            Year = year,
            Season = season,
            Episode = episode,
            IsKeywordQuery = false,
            OutputDirectory = fi.DirectoryName
        };
    }

    public static (string? Title, int? Year, int? Season, int? Episode) ParseFileName(string nameNoExt)
    {
        var seasonEpisode = SeasonEpisodeRegex().Match(nameNoExt);
        int? season = null, episode = null;
        if (seasonEpisode.Success)
        {
            season = int.Parse(seasonEpisode.Groups[1].Value);
            episode = int.Parse(seasonEpisode.Groups[2].Value);
        }

        int? year = null;
        var yearMatch = YearRegex().Match(nameNoExt);
        if (yearMatch.Success && int.TryParse(yearMatch.Groups[1].Value, out int y) && y is >= 1900 and <= 2100)
            year = y;

        string cleaned = TagCleanupRegex().Replace(nameNoExt, " ");
        cleaned = SeasonEpisodeRegex().Replace(cleaned, " ");
        cleaned = YearRegex().Replace(cleaned, " ");
        cleaned = ReleaseGroupRegex().Replace(cleaned, " ");
        cleaned = MultiSpaceRegex().Replace(cleaned, " ").Trim(' ', '.', '-', '_');

        return (string.IsNullOrWhiteSpace(cleaned) ? nameNoExt : cleaned, year, season, episode);
    }

    private static string? ResolveExistingFile(string raw)
    {
        if (File.Exists(raw))
            return Path.GetFullPath(raw);

        try
        {
            string full = Path.GetFullPath(raw);
            if (File.Exists(full))
                return full;
        }
        catch (Exception)
        {
            // invalid path chars for full path — treat as keyword
        }

        return null;
    }

    private static string? ResolveExistingDirectory(string raw)
    {
        if (Directory.Exists(raw))
            return Path.GetFullPath(raw);

        try
        {
            string full = Path.GetFullPath(raw);
            if (Directory.Exists(full))
                return full;
        }
        catch (Exception)
        {
            // ignore
        }

        return null;
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        string s = new string(chars).Trim(' ', '.');
        return string.IsNullOrWhiteSpace(s) ? "query" : s;
    }

    [GeneratedRegex(@"[Ss](\d{1,2})[Ee](\d{1,3})", RegexOptions.CultureInvariant)]
    private static partial Regex SeasonEpisodeRegex();

    [GeneratedRegex(@"(?:^|[.\s_\-(])((?:19|20)\d{2})(?:[.\s_\-)]|$)", RegexOptions.CultureInvariant)]
    private static partial Regex YearRegex();

    [GeneratedRegex(
        @"\b(?:\d{3,4}p|UHD|HDR10|HDR|DV|DoVi|BluRay|Blu-Ray|WEB-?DL|WEBRip|HDTV|x264|x265|h264|h265|HEVC|AAC|DTS|TrueHD|Atmos|REMUX|Proper|REPACK|Internal)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TagCleanupRegex();

    [GeneratedRegex(@"[.\s_\-]+")]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(@"[-.][A-Za-z0-9]+$")]
    private static partial Regex ReleaseGroupRegex();
}

using IcedSubDL.Core.Abstractions;
using IcedSubDL.Core.Media;
using IcedSubDL.Core.Models;
using IcedSubDL.Core.Scoring;
using Microsoft.Extensions.Logging;

namespace IcedSubDL.Core.Services;

public sealed class SubtitleOrchestrator
{
    private readonly IReadOnlyList<ISubtitleProvider> _providers;
    private readonly ILogger<SubtitleOrchestrator>? _logger;

    public SubtitleOrchestrator(
        IEnumerable<ISubtitleProvider> providers,
        ILogger<SubtitleOrchestrator>? logger = null)
    {
        _providers = providers.ToList();
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubtitleCandidate>> SearchAllAsync(
        MediaInfo media,
        SearchOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options.SkipIfExternalSrtExists &&
            SubtitleFileNamer.ExternalSrtExists(media, options.Languages))
        {
            _logger?.LogInformation("Skip {File}: external SRT already present.", media.FileName);
            return [];
        }

        var configured = _providers.Where(p => p.IsConfigured).ToList();
        if (configured.Count == 0)
        {
            _logger?.LogWarning("No subtitle providers are configured (API keys/tokens missing).");
            return [];
        }

        var tasks = configured.Select(async p =>
        {
            try
            {
                var hits = await p.SearchAsync(media, options, cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation("{Provider}: {Count} hit(s) for {File}", p.Name, hits.Count, media.FileName);
                return hits;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "{Provider} search failed for {File}", p.Name, media.FileName);
                return (IReadOnlyList<SubtitleCandidate>)[];
            }
        });

        var batches = await Task.WhenAll(tasks).ConfigureAwait(false);
        var merged = batches.SelectMany(x => x).ToList();

        // Only hard-filter when user explicitly set PreferredFormats
        IEnumerable<SubtitleCandidate> pool = merged;
        if (options.PreferredFormats is { Count: > 0 })
        {
            var filtered = merged.Where(c =>
                SrtArchiveExtractor.IsPreferredFormat(c.Format, options.PreferredFormats) ||
                SrtArchiveExtractor.IsPreferredFormat(c.DisplayName, options.PreferredFormats) ||
                SrtArchiveExtractor.IsPreferredFormat(c.ReleaseName, options.PreferredFormats) ||
                string.IsNullOrWhiteSpace(c.Format)).ToList();
            if (filtered.Count > 0)
                pool = filtered;
        }

        return SubtitleScorer.Rank(media, pool, options);
    }

    public async Task<string?> DownloadAndSaveAsync(
        MediaInfo media,
        SubtitleCandidate candidate,
        SearchOptions options,
        int? index = null,
        bool multiDownload = false,
        CancellationToken cancellationToken = default)
    {
        var provider = _providers.FirstOrDefault(p =>
            string.Equals(p.Name, candidate.ProviderName, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
            throw new InvalidOperationException($"Provider not found: {candidate.ProviderName}");

        // Providers return raw payload — no archive extraction. Keep original file name.
        SubtitleFile file = await provider.DownloadAsync(candidate, cancellationToken).ConfigureAwait(false);

        string outputPath = SubtitleFileNamer.GetOutputPath(
            media,
            candidate,
            downloaded: file,
            overwriteExisting: options.OverwriteExisting);

        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // GetOutputPath already de-dupes with " (2)" unless overwrite; if same path still exists and no overwrite, skip.
        if (File.Exists(outputPath) && !options.OverwriteExisting)
        {
            _logger?.LogWarning("File exists, not overwriting: {Path}", outputPath);
            return null;
        }

        await File.WriteAllBytesAsync(outputPath, file.Content, cancellationToken).ConfigureAwait(false);
        _logger?.LogInformation("Saved {Path} ({Bytes} bytes, original name)", outputPath, file.Content.Length);
        return outputPath;
    }

    public async Task<BatchResult> ProcessAsync(
        MediaInfo media,
        SearchOptions options,
        CancellationToken cancellationToken = default)
    {
        var candidates = await SearchAllAsync(media, options, cancellationToken).ConfigureAwait(false);
        if (candidates.Count == 0)
            return new BatchResult(media, [], [], "No candidates");

        if (options.DownloadMode == DownloadMode.None)
            return new BatchResult(media, candidates, [], "Search only");

        IReadOnlyList<SubtitleCandidate> toDownload = options.DownloadMode switch
        {
            DownloadMode.Top => [candidates[0]],
            DownloadMode.All => candidates,
            _ => []
        };

        bool multi = toDownload.Count > 1;
        var saved = new List<string>();
        var failures = new List<string>();

        for (int i = 0; i < toDownload.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = toDownload[i];
            int index = i + 1;
            try
            {
                string? path = await DownloadAndSaveAsync(
                        media,
                        candidate,
                        options,
                        index: index,
                        multiDownload: multi,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (path is not null)
                    saved.Add(path);
                else
                    failures.Add($"[{index}] skipped existing ({candidate.ProviderName} {candidate.ProviderId})");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex,
                    "Download failed [{Index}] {Provider}/{Id}: {Name}",
                    index,
                    candidate.ProviderName,
                    candidate.ProviderId,
                    candidate.DisplayName);
                failures.Add($"[{index}] {candidate.DisplayName}: {ex.Message}");
            }
        }

        string status = options.DownloadMode == DownloadMode.Top
            ? (saved.Count > 0 ? "Downloaded top" : $"Top download failed ({failures.Count})")
            : $"Downloaded {saved.Count}/{toDownload.Count}" +
              (failures.Count > 0 ? $", failed {failures.Count}" : string.Empty);

        return new BatchResult(media, candidates, saved, status, failures);
    }
}

public sealed record BatchResult(
    MediaInfo Media,
    IReadOnlyList<SubtitleCandidate> Candidates,
    IReadOnlyList<string> SavedPaths,
    string Status,
    IReadOnlyList<string>? Failures = null);

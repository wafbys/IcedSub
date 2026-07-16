using System.Net.Sockets;
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

    public async Task<(IReadOnlyList<SubtitleCandidate> Candidates, IReadOnlyList<string> Notes)> SearchAllAsync(
        MediaInfo media,
        SearchOptions options,
        CancellationToken cancellationToken = default)
    {
        var notes = new List<string>();

        if (options.SkipIfExternalSrtExists &&
            SubtitleFileNamer.ExternalSrtExists(media, options.Languages))
        {
            notes.Add("已存在外挂字幕，已跳过");
            return ([], notes);
        }

        var configured = _providers.Where(p => p.IsConfigured).ToList();
        if (configured.Count == 0)
        {
            notes.Add("未配置任何字幕源（请设置 API Key / Token）");
            return ([], notes);
        }

        var tasks = configured.Select(async p =>
        {
            try
            {
                var hits = await p.SearchAsync(media, options, cancellationToken).ConfigureAwait(false);
                return (Hits: hits, Error: (string?)null);
            }
            catch (Exception ex)
            {
                return (Hits: (IReadOnlyList<SubtitleCandidate>)[], Error: $"{p.Name}: {FormatShortError(ex)}");
            }
        });

        var batches = await Task.WhenAll(tasks).ConfigureAwait(false);
        foreach (var batch in batches)
        {
            if (batch.Error is not null)
            {
                notes.Add(batch.Error);
                _logger?.LogWarning("{Error}", batch.Error);
            }
            else if (batch.Hits.Count > 0)
            {
                _logger?.LogInformation("找到 {Count} 条候选", batch.Hits.Count);
            }
        }

        var merged = batches.SelectMany(x => x.Hits).ToList();

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

        return (SubtitleScorer.Rank(media, pool, options), notes);
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

        SubtitleFile file = await provider.DownloadAsync(candidate, cancellationToken).ConfigureAwait(false);

        string outputPath = SubtitleFileNamer.GetOutputPath(
            media,
            candidate,
            downloaded: file,
            overwriteExisting: options.OverwriteExisting);

        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        if (File.Exists(outputPath) && !options.OverwriteExisting)
        {
            _logger?.LogWarning("已存在，跳过: {Path}", outputPath);
            return null;
        }

        await File.WriteAllBytesAsync(outputPath, file.Content, cancellationToken).ConfigureAwait(false);
        _logger?.LogInformation("已保存 {Path}", outputPath);
        return outputPath;
    }

    public async Task<BatchResult> ProcessAsync(
        MediaInfo media,
        SearchOptions options,
        CancellationToken cancellationToken = default)
    {
        var (candidates, searchNotes) = await SearchAllAsync(media, options, cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
            return new BatchResult(media, [], [], "无结果", searchNotes);

        if (options.DownloadMode == DownloadMode.None)
            return new BatchResult(media, candidates, [], "仅搜索", searchNotes);

        IReadOnlyList<SubtitleCandidate> toDownload = options.DownloadMode switch
        {
            DownloadMode.Top => [candidates[0]],
            DownloadMode.All => candidates,
            _ => []
        };

        bool multi = toDownload.Count > 1;
        var saved = new List<string>();
        var failures = new List<string>(searchNotes);

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
                    failures.Add($"[{index}] 已存在，跳过");
            }
            catch (Exception ex)
            {
                string shortMsg = FormatShortError(ex);
                _logger?.LogWarning("下载失败 [{Index}]: {Message}", index, shortMsg);
                failures.Add($"[{index}] {shortMsg}");
            }
        }

        string status = options.DownloadMode == DownloadMode.Top
            ? (saved.Count > 0 ? "已下载（最优）" : "下载失败")
            : $"已下载 {saved.Count}/{toDownload.Count}" +
              (failures.Count > searchNotes.Count ? $"，失败 {failures.Count - searchNotes.Count}" : string.Empty);

        return new BatchResult(media, candidates, saved, status, failures);
    }

    public static string FormatShortError(Exception ex)
    {
        // Unwrap common wrappers
        Exception cur = ex;
        while (cur.InnerException is not null &&
               (cur is HttpRequestException or AggregateException or TaskCanceledException))
        {
            cur = cur.InnerException!;
        }

        if (cur is SocketException se)
        {
            return se.SocketErrorCode switch
            {
                SocketError.AccessDenied => "网络被系统拦截（权限/防火墙/代理），请检查网络或稍后重试",
                SocketError.HostNotFound => "无法解析域名",
                SocketError.TimedOut => "连接超时",
                SocketError.ConnectionRefused => "连接被拒绝",
                SocketError.NetworkUnreachable or SocketError.HostUnreachable => "网络不可达",
                _ => $"网络错误: {se.Message}"
            };
        }

        if (cur is HttpRequestException http)
        {
            string m = http.Message;
            // Strip long host noise: keep first sentence-ish
            int cut = m.IndexOf(" (");
            if (cut > 0)
                m = m[..cut];
            if (m.Contains("forbidden by its access permissions", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase))
                return "网络被系统拦截（权限/防火墙/代理），请检查网络或稍后重试";
            return m.Length > 120 ? m[..120] + "…" : m;
        }

        if (cur is TaskCanceledException)
            return "请求超时";

        string msg = cur.Message;
        return msg.Length > 120 ? msg[..120] + "…" : msg;
    }
}

public sealed record BatchResult(
    MediaInfo Media,
    IReadOnlyList<SubtitleCandidate> Candidates,
    IReadOnlyList<string> SavedPaths,
    string Status,
    IReadOnlyList<string>? Failures = null);

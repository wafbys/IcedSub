using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using IcedSub.Core.Abstractions;
using IcedSub.Core.Media;
using IcedSub.Core.Models;
using Microsoft.Extensions.Logging;

namespace IcedSub.Providers.OpenSubtitles;

public sealed class OpenSubtitlesProvider : ISubtitleProvider
{
    private readonly HttpClient _http;
    private readonly OpenSubtitlesSettings _settings;
    private readonly ILogger<OpenSubtitlesProvider>? _logger;
    private readonly SemaphoreSlim _authLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OpenSubtitlesProvider(
        HttpClient http,
        OpenSubtitlesSettings settings,
        ILogger<OpenSubtitlesProvider>? logger = null)
    {
        _http = http;
        _settings = settings;
        _logger = logger;

        if (_http.BaseAddress is null && !string.IsNullOrWhiteSpace(_settings.BaseUrl))
            _http.BaseAddress = new Uri(_settings.BaseUrl.TrimEnd('/') + "/");

        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(_settings.UserAgent);

        if (!string.IsNullOrWhiteSpace(_settings.ApiKey) &&
            !_http.DefaultRequestHeaders.Contains("Api-Key"))
            _http.DefaultRequestHeaders.Add("Api-Key", _settings.ApiKey);
    }

    public string Name => "OpenSubtitles";

    public bool IsConfigured =>
        _settings.Enabled && !string.IsNullOrWhiteSpace(_settings.ApiKey);

    public async Task<IReadOnlyList<SubtitleCandidate>> SearchAsync(
        MediaInfo media,
        SearchOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return [];

        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(media.OpenSubtitlesMovieHash))
            query.Add($"moviehash={Uri.EscapeDataString(media.OpenSubtitlesMovieHash)}");

        string languages = string.Join(',', options.Languages.Select(MapLanguage));
        if (!string.IsNullOrWhiteSpace(languages))
            query.Add($"languages={Uri.EscapeDataString(languages)}");

        // Optional formats filter only when user configured PreferredFormats
        if (options.PreferredFormats.Count > 0)
            query.Add($"formats={Uri.EscapeDataString(string.Join(',', options.PreferredFormats))}");

        if (media.Season is int s && media.Episode is int e)
        {
            query.Add($"season_number={s}");
            query.Add($"episode_number={e}");
        }

        // Keyword / title query when no hash, or always for keyword-only searches
        string? textQuery = media.IsKeywordQuery
            ? (media.ParsedTitle ?? media.FileNameWithoutExtension)
            : string.IsNullOrWhiteSpace(media.OpenSubtitlesMovieHash)
                ? media.ParsedTitle ?? media.FileNameWithoutExtension
                : null;

        if (!string.IsNullOrWhiteSpace(textQuery))
            query.Add($"query={Uri.EscapeDataString(textQuery)}");

        string path = "subtitles?" + string.Join('&', query);
        SearchResponse? response = await GetJsonAsync<SearchResponse>(path, cancellationToken).ConfigureAwait(false);

        var list = new List<SubtitleCandidate>();
        if (response?.Data is { Count: > 0 })
        {
            foreach (var item in response.Data.Take(options.MaxResultsPerProvider))
            {
                var attrs = item.Attributes;
                if (attrs is null)
                    continue;

                var file = attrs.Files?.FirstOrDefault();
                if (file?.FileId is null)
                    continue;

                string format = attrs.Format ?? GuessFormat(attrs.Files);
                if (options.PreferredFormats.Count > 0 &&
                    !SrtArchiveExtractor.IsPreferredFormat(format, options.PreferredFormats))
                    continue;

                string lang = attrs.Language ?? "und";
                string display = attrs.FeatureDetails?.Title
                    ?? attrs.Release
                    ?? file.FileName
                    ?? $"file_id={file.FileId}";

                list.Add(new SubtitleCandidate
                {
                    ProviderName = Name,
                    ProviderId = item.Id ?? file.FileId.Value.ToString(),
                    DisplayName = display,
                    Language = lang,
                    Format = format,
                    ReleaseName = attrs.Release ?? file.FileName,
                    HearingImpairedHint = attrs.HearingImpaired == true ? "HI" : null,
                    DownloadCount = attrs.DownloadCount,
                    Score = attrs.Ratings,
                    DownloadKey = file.FileId.Value.ToString(),
                    Extra =
                    {
                        ["file_name"] = file.FileName ?? string.Empty
                    }
                });
            }
        }

        // Fallback query by file name if hash search empty
        if (list.Count == 0 && !string.IsNullOrWhiteSpace(media.FileNameWithoutExtension))
        {
            string q = Uri.EscapeDataString(media.FileNameWithoutExtension);
            string fb = $"subtitles?query={q}&languages={Uri.EscapeDataString(languages)}";
            if (options.PreferredFormats.Count > 0)
                fb += $"&formats={Uri.EscapeDataString(string.Join(',', options.PreferredFormats))}";
            response = await GetJsonAsync<SearchResponse>(fb, cancellationToken).ConfigureAwait(false);
            if (response?.Data is { Count: > 0 })
            {
                foreach (var item in response.Data.Take(options.MaxResultsPerProvider))
                {
                    var attrs = item.Attributes;
                    var file = attrs?.Files?.FirstOrDefault();
                    if (file?.FileId is null || attrs is null)
                        continue;

                    string format = attrs.Format ?? "srt";
                    list.Add(new SubtitleCandidate
                    {
                        ProviderName = Name,
                        ProviderId = item.Id ?? file.FileId.Value.ToString(),
                        DisplayName = attrs.Release ?? file.FileName ?? displayFallback(file.FileId.Value),
                        Language = attrs.Language ?? "und",
                        Format = format,
                        ReleaseName = attrs.Release ?? file.FileName,
                        DownloadCount = attrs.DownloadCount,
                        Score = attrs.Ratings,
                        DownloadKey = file.FileId.Value.ToString()
                    });
                }
            }
        }

        return list;

        static string displayFallback(long id) => $"OpenSubtitles #{id}";
    }

    public async Task<SubtitleFile> DownloadAsync(
        SubtitleCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("OpenSubtitles is not configured.");

        await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);

        if (!long.TryParse(candidate.DownloadKey, out long fileId))
            throw new InvalidOperationException($"Invalid OpenSubtitles file id: {candidate.DownloadKey}");

        using var req = new HttpRequestMessage(HttpMethod.Post, "download");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        req.Content = JsonContent.Create(new { file_id = fileId }, options: JsonOptions);

        using HttpResponseMessage resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenSubtitles download failed ({(int)resp.StatusCode}): {body}");

        var download = JsonSerializer.Deserialize<DownloadResponse>(body, JsonOptions)
                       ?? throw new InvalidOperationException("Empty download response.");

        if (string.IsNullOrWhiteSpace(download.Link))
            throw new InvalidOperationException("OpenSubtitles did not return a download link.");

        byte[] bytes = await _http.GetByteArrayAsync(download.Link, cancellationToken).ConfigureAwait(false);
        string hint = download.FileName
                      ?? candidate.Extra.GetValueOrDefault("file_name")
                      ?? candidate.ReleaseName
                      ?? "subtitle.bin";

        // Save raw payload (no archive extraction).
        string safeName = Path.GetFileName(hint);
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "subtitle.bin";

        string format = SubtitleFormat.FromFileName(safeName, candidate.Format);
        if (string.IsNullOrEmpty(Path.GetExtension(safeName)) && !string.IsNullOrWhiteSpace(format))
            safeName = $"{safeName}.{format.TrimStart('.')}";

        return new SubtitleFile
        {
            Candidate = candidate,
            Content = bytes,
            SuggestedFileName = safeName,
            Format = format
        };
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_token) && DateTimeOffset.UtcNow < _tokenExpiry)
            return;

        // Anonymous download is possible with API key only (low quota). Login optional.
        if (string.IsNullOrWhiteSpace(_settings.Username) || string.IsNullOrWhiteSpace(_settings.Password))
        {
            _token = null;
            return;
        }

        await _authLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrEmpty(_token) && DateTimeOffset.UtcNow < _tokenExpiry)
                return;

            using var req = new HttpRequestMessage(HttpMethod.Post, "login");
            req.Content = JsonContent.Create(new
            {
                username = _settings.Username,
                password = _settings.Password
            }, options: JsonOptions);

            using HttpResponseMessage resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                _logger?.LogWarning("OpenSubtitles login failed: {Body}", body);
                _token = null;
                return;
            }

            var login = JsonSerializer.Deserialize<LoginResponse>(body, JsonOptions);
            _token = login?.Token;
            _tokenExpiry = DateTimeOffset.UtcNow.AddHours(23);
        }
        finally
        {
            _authLock.Release();
        }
    }

    private async Task<T?> GetJsonAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        if (!string.IsNullOrEmpty(_token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

        using HttpResponseMessage resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            _logger?.LogWarning("OpenSubtitles GET {Url} failed ({Code}): {Body}",
                relativeUrl, (int)resp.StatusCode, Truncate(body));
            return default;
        }

        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    private static string MapLanguage(string lang)
    {
        // OpenSubtitles uses ISO 639-1 / zh-cn style codes
        return SubtitleFileNamer.NormalizeLanguageTag(lang) switch
        {
            "zh-CN" => "zh-cn",
            "zh-TW" => "zh-tw",
            "en" => "en",
            var x => x.ToLowerInvariant()
        };
    }

    private static string GuessFormat(List<OsFile>? files)
    {
        string? name = files?.FirstOrDefault()?.FileName;
        if (string.IsNullOrEmpty(name))
            return "srt";
        string ext = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        return string.IsNullOrEmpty(ext) ? "srt" : ext;
    }

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300] + "...";

    private sealed class SearchResponse
    {
        public List<OsItem>? Data { get; set; }
    }

    private sealed class OsItem
    {
        public string? Id { get; set; }
        public OsAttributes? Attributes { get; set; }
    }

    private sealed class OsAttributes
    {
        public string? Language { get; set; }
        public string? Release { get; set; }
        public string? Format { get; set; }
        public int? DownloadCount { get; set; }
        public double? Ratings { get; set; }
        public bool? HearingImpaired { get; set; }
        public List<OsFile>? Files { get; set; }
        public OsFeatureDetails? FeatureDetails { get; set; }
    }

    private sealed class OsFile
    {
        [JsonPropertyName("file_id")]
        public long? FileId { get; set; }

        [JsonPropertyName("file_name")]
        public string? FileName { get; set; }
    }

    private sealed class OsFeatureDetails
    {
        public string? Title { get; set; }
    }

    private sealed class LoginResponse
    {
        public string? Token { get; set; }
    }

    private sealed class DownloadResponse
    {
        public string? Link { get; set; }

        [JsonPropertyName("file_name")]
        public string? FileName { get; set; }
    }
}

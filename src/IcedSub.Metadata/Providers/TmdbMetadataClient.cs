using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using IcedSub.Metadata.Models;
using Microsoft.Extensions.Logging;

namespace IcedSub.Metadata.Providers;

/// <summary>
/// TMDB API v3 — 用 IMDb id 反查电影或剧集，再取中英标题与年份（导演仅电影）。
/// 电影字段 title/release_date，剧集字段 name/first_air_date。
/// 文档：https://developer.themoviedb.org/docs
/// </summary>
public sealed partial class TmdbMetadataClient
{
    private readonly HttpClient _http;
    private readonly string? _apiKey;
    private readonly string? _readToken;
    private readonly ILogger<TmdbMetadataClient>? _logger;
    private readonly WikidataDoubanResolver? _doubanResolver;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TmdbMetadataClient(
        HttpClient http,
        string? apiKey,
        string? readAccessToken = null,
        ILogger<TmdbMetadataClient>? logger = null,
        WikidataDoubanResolver? doubanResolver = null)
    {
        _http = http;
        _apiKey = apiKey;
        _readToken = readAccessToken;
        _logger = logger;
        _doubanResolver = doubanResolver;

        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri("https://api.themoviedb.org/3/");

        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "IcedSub/0.1");

        if (!string.IsNullOrWhiteSpace(_readToken) &&
            !_http.DefaultRequestHeaders.Contains("Authorization"))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _readToken.Trim());
        }
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_apiKey) || !string.IsNullOrWhiteSpace(_readToken);

    public async Task<BilingualTitle?> GetByImdbIdAsync(
        string imdbId,
        CancellationToken ct = default,
        string? doubanOverride = null)
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                "未配置 TMDB。请设置 Naming:TmdbApiKey 或 Naming:TmdbReadAccessToken（或环境变量 ICEDSUB_TMDB_APIKEY / ICEDSUB_TMDB_TOKEN）。");

        imdbId = NormalizeImdbId(imdbId);
        if (imdbId.Length == 0)
            return null;

        // 1) find
        using var findDoc = await GetJsonAsync(
            $"find/{Uri.EscapeDataString(imdbId)}?external_source=imdb_id",
            ct).ConfigureAwait(false);

        if (findDoc is null)
            return null;

        int? tmdbId = null;
        bool isTv = false;

        if (findDoc.RootElement.TryGetProperty("movie_results", out var movies) &&
            movies.ValueKind == JsonValueKind.Array &&
            movies.GetArrayLength() > 0)
        {
            tmdbId = movies[0].TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number
                ? idEl.GetInt32()
                : null;
        }

        if (tmdbId is null &&
            findDoc.RootElement.TryGetProperty("tv_results", out var tvs) &&
            tvs.ValueKind == JsonValueKind.Array &&
            tvs.GetArrayLength() > 0)
        {
            tmdbId = tvs[0].TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number
                ? idEl.GetInt32()
                : null;
            isTv = tmdbId is not null;
        }

        if (tmdbId is null)
            return null;

        if (isTv)
            _logger?.LogInformation("tt 对应剧集 TMDB={Id}，按 tv 接口取剧名 / 首播年", tmdbId);

        // 电影与剧集字段名不同：title/release_date vs name/first_air_date
        string detailsPath = isTv ? "tv" : "movie";

        // 2) details en / zh
        using var enDoc = await GetJsonAsync($"{detailsPath}/{tmdbId}?language=en-US", ct).ConfigureAwait(false);
        using var zhDoc = await GetJsonAsync($"{detailsPath}/{tmdbId}?language=zh-CN", ct).ConfigureAwait(false);
        if (enDoc is null)
            return null;

        string titleEn = (isTv
                             ? GetString(enDoc.RootElement, "name")
                             : GetString(enDoc.RootElement, "title"))
                         ?? (isTv
                             ? GetString(enDoc.RootElement, "original_name")
                             : GetString(enDoc.RootElement, "original_title"))
                         ?? string.Empty;

        string? titleZh = zhDoc is null
            ? null
            : (isTv ? GetString(zhDoc.RootElement, "name") : GetString(zhDoc.RootElement, "title"));
        string? zhOriginal = zhDoc is null
            ? null
            : (isTv ? GetString(zhDoc.RootElement, "original_name") : GetString(zhDoc.RootElement, "original_title"));
        // 若中文名仍是英文，可尝试 original_* 是否中文
        if (titleZh is not null && LooksMostlyLatin(titleZh) && !LooksMostlyLatin(zhOriginal))
            titleZh = zhOriginal;
        if (titleZh is not null && string.Equals(titleZh, titleEn, StringComparison.OrdinalIgnoreCase))
            titleZh = null;

        string? year = null;
        string? release = isTv
            ? GetString(enDoc.RootElement, "first_air_date")
            : GetString(enDoc.RootElement, "release_date");
        if (!string.IsNullOrWhiteSpace(release) && release.Length >= 4)
            year = release[..4];

        // 3) 导演（命名不含导演；剧集走 tv 接口，不取 credits）
        string? directorEn = null;
        string? directorZh = null;
        if (!isTv)
        {
            using var credits = await GetJsonAsync($"movie/{tmdbId}/credits", ct).ConfigureAwait(false);
            int? directorPersonId = null;
            if (credits is not null &&
                credits.RootElement.TryGetProperty("crew", out var crew) &&
                crew.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in crew.EnumerateArray())
                {
                    if (!string.Equals(GetString(c, "job"), "Director", StringComparison.OrdinalIgnoreCase))
                        continue;
                    directorEn = GetString(c, "name");
                    if (c.TryGetProperty("id", out var pid) && pid.ValueKind == JsonValueKind.Number)
                        directorPersonId = pid.GetInt32();
                    break;
                }
            }

            if (directorPersonId is int personId)
                directorZh = await GetPersonChineseNameAsync(personId, directorEn, ct).ConfigureAwait(false);
        }

        // external id 校验
        using var ext = await GetJsonAsync($"{detailsPath}/{tmdbId}/external_ids", ct).ConfigureAwait(false);
        string? imdbFromTmdb = ext is null ? null : GetString(ext.RootElement, "imdb_id");
        if (!string.IsNullOrWhiteSpace(imdbFromTmdb))
            imdbId = NormalizeImdbId(imdbFromTmdb);

        if (string.IsNullOrWhiteSpace(titleEn))
            return null;

        string? doubanId = await ResolveDoubanIdAsync(imdbId, doubanOverride, ct)
            .ConfigureAwait(false);

        return new BilingualTitle
        {
            ImdbId = imdbId,
            TmdbId = tmdbId,
            TitleEn = titleEn,
            TitleZh = titleZh,
            Year = year,
            DirectorEn = directorEn,
            DirectorZh = directorZh,
            DoubanId = doubanId
        };
    }

    private async Task<string?> ResolveDoubanIdAsync(
        string imdbId,
        string? doubanOverride,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(doubanOverride))
            return doubanOverride.Trim();

        if (_doubanResolver is null)
            return null;

        return await _doubanResolver.GetDoubanIdAsync(imdbId, ct).ConfigureAwait(false);
    }

    private async Task<string?> GetPersonChineseNameAsync(int personId, string? enName, CancellationToken ct)
    {
        using var person = await GetJsonAsync($"person/{personId}?language=zh-CN", ct).ConfigureAwait(false);
        if (person is null)
            return null;

        // also_known_as 里找中日韩字符名
        if (person.RootElement.TryGetProperty("also_known_as", out var aka) &&
            aka.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in aka.EnumerateArray())
            {
                string? s = a.GetString();
                if (!string.IsNullOrWhiteSpace(s) && HasCjk(s) &&
                    !string.Equals(s, enName, StringComparison.OrdinalIgnoreCase))
                    return s;
            }
        }

        string? name = GetString(person.RootElement, "name");
        if (!string.IsNullOrWhiteSpace(name) && HasCjk(name) &&
            !string.Equals(name, enName, StringComparison.OrdinalIgnoreCase))
            return name;

        return null;
    }

    private async Task<JsonDocument?> GetJsonAsync(string relative, CancellationToken ct)
    {
        string url = relative;
        if (!string.IsNullOrWhiteSpace(_apiKey) && string.IsNullOrWhiteSpace(_readToken))
        {
            url += relative.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            url += "api_key=" + Uri.EscapeDataString(_apiKey!);
        }

        try
        {
            using HttpResponseMessage resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                _logger?.LogWarning("TMDB {Url} -> {Code}", relative, (int)resp.StatusCode);
                return null;
            }

            return JsonDocument.Parse(body);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("TMDB 请求失败 {Url}: {Message}", relative, ex.Message);
            return null;
        }
    }

    public static string NormalizeImdbId(string raw)
    {
        raw = raw.Trim();
        var m = ImdbUrl().Match(raw);
        if (m.Success)
            raw = m.Groups[1].Value;

        if (raw.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
            return "tt" + Digits().Replace(raw[2..], string.Empty);

        string d = Digits().Replace(raw, string.Empty);
        return d.Length == 0 ? string.Empty : "tt" + d;
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static bool HasCjk(string s) => s.Any(c => c is >= '\u4e00' and <= '\u9fff');

    private static bool LooksMostlyLatin(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return true;
        int letters = s.Count(char.IsLetter);
        if (letters == 0)
            return false;
        int latin = s.Count(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z'));
        return latin * 2 >= letters;
    }

    [GeneratedRegex(@"imdb\.com/title/(tt\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ImdbUrl();

    [GeneratedRegex(@"\D")]
    private static partial Regex Digits();
}

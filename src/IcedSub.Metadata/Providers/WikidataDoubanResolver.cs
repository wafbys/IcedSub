using System.Text.Json;
using IcedSub.Metadata.Models;
using Microsoft.Extensions.Logging;

namespace IcedSub.Metadata.Providers;

/// <summary>
/// 通过 Wikidata 在 IMDb id 与豆瓣 id 之间互查：
/// P345 = IMDb id，P4529 = 豆瓣影视 id。
/// 豆瓣没有公开 API；Wikidata 是开放数据，无需密钥。
/// 查询失败/无数据时返回 null（调用方应优雅降级）。
/// </summary>
public sealed class WikidataDoubanResolver
{
    private const string SparqlEndpoint = "https://query.wikidata.org/sparql";
    private const int MaxAttempts = 2;

    private readonly HttpClient _http;
    private readonly ILogger<WikidataDoubanResolver>? _logger;

    public WikidataDoubanResolver(
        HttpClient http,
        ILogger<WikidataDoubanResolver>? logger = null)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>IMDb tt → 豆瓣 id（P345 → P4529）。</summary>
    public async Task<string?> GetDoubanIdAsync(
        string imdbId,
        CancellationToken cancellationToken = default)
    {
        string tt = TmdbMetadataClient.NormalizeImdbId(imdbId);
        if (string.IsNullOrWhiteSpace(tt) || tt == "tt")
            return null;

        string query =
            $"SELECT ?douban WHERE {{ ?film wdt:P345 \"{tt}\" ; wdt:P4529 ?douban . }} LIMIT 1";
        return await QueryFirstValueAsync(query, "douban", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>豆瓣 id → IMDb tt（P4529 → P345）。</summary>
    public async Task<string?> GetImdbIdAsync(
        string doubanId,
        CancellationToken cancellationToken = default)
    {
        // 豆瓣 id 是纯数字；过滤掉任何非数字字符以防注入。
        string id = new((doubanId ?? string.Empty).Where(char.IsDigit).ToArray());
        if (id.Length == 0)
            return null;

        string query =
            $"SELECT ?imdb WHERE {{ ?film wdt:P4529 \"{id}\" ; wdt:P345 ?imdb . }} LIMIT 1";
        string? tt = await QueryFirstValueAsync(query, "imdb", cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(tt) ? null : tt;
    }

    /// <summary>
    /// IMDb id 或豆瓣 id → 标题/年份（免密钥，供 TMDB 未配置或未收录时兜底）。
    /// 中文取 zh 标签（含 zh-hans/zh-cn/zh-hant/zh-tw/zh-hk），英文取 en 标签；
    /// 年份取 P577（出版日期）中最早的一年；IMDb/豆瓣 id 分别取 P345/P4529。
    /// 覆盖不全时返回 null 或缺失字段（调用方应优雅降级）。
    /// </summary>
    public async Task<BilingualTitle?> GetTitleAsync(
        string? imdbId,
        string? doubanId,
        CancellationToken cancellationToken = default)
    {
        string? tt = string.IsNullOrWhiteSpace(imdbId) ? null : TmdbMetadataClient.NormalizeImdbId(imdbId);
        if (string.IsNullOrWhiteSpace(tt) || tt == "tt")
            tt = null;

        string db = new((doubanId ?? string.Empty).Where(char.IsDigit).ToArray());
        string? douban = db.Length == 0 ? null : db;

        if (tt is null && douban is null)
            return null;

        // 给了 IMDb id 就按 P345 匹配（更精确）；否则按豆瓣 P4529。
        string match = tt is not null
            ? $"{{ ?item wdt:P345 \"{tt}\" . }}"
            : $"{{ ?item wdt:P4529 \"{douban}\" . }}";

        string query =
            "SELECT ?item ?date ?imdb ?douban ?zhLabel ?enLabel WHERE { " +
            match +
            " OPTIONAL { ?item wdt:P577 ?date . }" +
            " OPTIONAL { ?item wdt:P345 ?imdb . }" +
            " OPTIONAL { ?item wdt:P4529 ?douban . }" +
            " OPTIONAL { ?item rdfs:label ?zhLabel . FILTER(lang(?zhLabel) IN (\"zh\",\"zh-hans\",\"zh-cn\",\"zh-hant\",\"zh-tw\",\"zh-hk\")) }" +
            " OPTIONAL { ?item rdfs:label ?enLabel . FILTER(lang(?enLabel) = \"en\") }" +
            " }";

        using JsonDocument? doc = await QueryJsonAsync(query, cancellationToken).ConfigureAwait(false);
        if (doc is null)
            return null;

        return ParseTitleBindings(doc, tt, douban);
    }

    /// <summary>解析 <see cref="GetTitleAsync"/> 的 SPARQL JSON 结果；无可用标题时返回 null。</summary>
    public static BilingualTitle? ParseTitleBindings(
        JsonDocument doc,
        string? fallbackImdbId = null,
        string? fallbackDoubanId = null)
    {
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("results", out JsonElement results) ||
            !results.TryGetProperty("bindings", out JsonElement bindings) ||
            bindings.ValueKind != JsonValueKind.Array ||
            bindings.GetArrayLength() == 0)
        {
            return null;
        }

        string? zh = null;
        int zhRank = int.MaxValue;
        string? en = null;
        string? bindingTt = null;
        string? bindingDouban = null;
        string? year = null;

        foreach (JsonElement row in bindings.EnumerateArray())
        {
            if (TryGetBinding(row, "zhLabel", out string z, out string? zl))
            {
                int rank = ZhLangRank(zl);
                if (rank < zhRank)
                {
                    zhRank = rank;
                    zh = z;
                }
            }

            if (en is null && TryGetBinding(row, "enLabel", out string e, out _))
                en = e;
            if (bindingTt is null && TryGetBinding(row, "imdb", out string t, out _))
                bindingTt = t;
            if (bindingDouban is null && TryGetBinding(row, "douban", out string d, out _))
                bindingDouban = d;

            if (TryGetBinding(row, "date", out string dt, out _))
            {
                string y = ExtractYear(dt);
                if (y.Length == 4 && (year is null || string.CompareOrdinal(y, year) < 0))
                    year = y;
            }
        }

        if (!string.IsNullOrWhiteSpace(bindingTt))
            bindingTt = TmdbMetadataClient.NormalizeImdbId(bindingTt);

        string? finalImdb = !string.IsNullOrWhiteSpace(bindingTt) ? bindingTt : fallbackImdbId;
        string? finalDouban = !string.IsNullOrWhiteSpace(bindingDouban) ? bindingDouban : fallbackDoubanId;

        string? titleZh = string.IsNullOrWhiteSpace(zh) ? null : zh;
        string? titleEn = !string.IsNullOrWhiteSpace(en) ? en : titleZh;
        if (string.IsNullOrWhiteSpace(titleEn))
            return null;

        return new BilingualTitle
        {
            ImdbId = finalImdb ?? string.Empty,
            TmdbId = null,
            TitleEn = titleEn,
            TitleZh = titleZh,
            Year = year,
            DoubanId = string.IsNullOrWhiteSpace(finalDouban) ? null : finalDouban
        };
    }

    private static int ZhLangRank(string? lang) => lang switch
    {
        "zh" => 0,
        "zh-hans" => 1,
        "zh-cn" => 2,
        "zh-hant" => 3,
        "zh-tw" => 4,
        "zh-hk" => 5,
        _ => 99
    };

    private static string ExtractYear(string value) =>
        value.Length >= 4 && value[..4].All(char.IsDigit) ? value[..4] : string.Empty;

    private static bool TryGetBinding(JsonElement row, string name, out string value, out string? lang)
    {
        value = string.Empty;
        lang = null;
        if (!row.TryGetProperty(name, out JsonElement node) ||
            !node.TryGetProperty("value", out JsonElement v) ||
            v.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = v.GetString() ?? string.Empty;
        if (node.TryGetProperty("xml:lang", out JsonElement l) && l.ValueKind == JsonValueKind.String)
            lang = l.GetString();
        return true;
    }

    private async Task<JsonDocument?> QueryJsonAsync(string query, CancellationToken cancellationToken)
    {
        string url = SparqlEndpoint + "?format=json&query=" + Uri.EscapeDataString(query);

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using HttpResponseMessage resp = await _http.GetAsync(url, cancellationToken)
                    .ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                {
                    _logger?.LogWarning(
                        "Wikidata SPARQL 返回 {Code}（第 {Attempt}/{Max} 次）",
                        (int)resp.StatusCode, attempt, MaxAttempts);
                    continue;
                }

                return JsonDocument.Parse(
                    await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex)
                when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger?.LogWarning(
                    "Wikidata 查询失败（第 {Attempt}/{Max} 次）: {Message}",
                    attempt, MaxAttempts, ex.Message);
            }
        }

        return null;
    }

    private async Task<string?> QueryFirstValueAsync(
        string query,
        string variableName,
        CancellationToken cancellationToken)
    {
        using JsonDocument? doc = await QueryJsonAsync(query, cancellationToken).ConfigureAwait(false);
        if (doc is null)
            return null;

        string? value = ExtractFirstBindingValue(doc, variableName);
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        // 请求成功但没有绑定：条目未收录该 id，无需重试。
        _logger?.LogDebug("Wikidata 未找到 {Var} 绑定", variableName);
        return null;
    }

    private static string? ExtractFirstBindingValue(JsonDocument doc, string variableName)
    {
        if (!doc.RootElement.TryGetProperty("results", out JsonElement results) ||
            !results.TryGetProperty("bindings", out JsonElement bindings) ||
            bindings.ValueKind != JsonValueKind.Array ||
            bindings.GetArrayLength() == 0)
        {
            return null;
        }

        JsonElement first = bindings[0];
        if (first.TryGetProperty(variableName, out JsonElement node) &&
            node.TryGetProperty("value", out JsonElement value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }
}

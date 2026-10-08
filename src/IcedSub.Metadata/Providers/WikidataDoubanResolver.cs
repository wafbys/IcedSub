using System.Text.Json;
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

    private async Task<string?> QueryFirstValueAsync(
        string query,
        string variableName,
        CancellationToken cancellationToken)
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

                using JsonDocument doc = JsonDocument.Parse(
                    await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

                string? value = ExtractFirstBindingValue(doc, variableName);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;

                // 请求成功但没有绑定：条目未收录该 id，无需重试。
                _logger?.LogDebug("Wikidata 未找到 {Var} 绑定", variableName);
                return null;
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

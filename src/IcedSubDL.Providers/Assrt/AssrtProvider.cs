using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using IcedSubDL.Core.Abstractions;
using IcedSubDL.Core.Media;
using IcedSubDL.Core.Models;
using Microsoft.Extensions.Logging;

namespace IcedSubDL.Providers.Assrt;

/// <summary>
/// 射手网(伪) https://assrt.net — Chinese-friendly subtitle API.
/// Please attribute: 字幕服务由 assrt.net 提供
/// </summary>
public sealed partial class AssrtProvider : ISubtitleProvider
{
    private readonly HttpClient _http;
    private readonly AssrtSettings _settings;
    private readonly ILogger<AssrtProvider>? _logger;

    /// <summary>
    /// Dedicated client for file CDN downloads. file*.assrt.net currently CNAMEs to glb.assrt.net
    /// which has no A record; traffic is reachable via api.assrt.net's address with original Host/SNI.
    /// </summary>
    private static readonly HttpClient FileDownloadClient = CreateFileDownloadClient();

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public AssrtProvider(HttpClient http, AssrtSettings settings, ILogger<AssrtProvider>? logger = null)
    {
        _http = http;
        _settings = settings;
        _logger = logger;

        if (_http.BaseAddress is null && !string.IsNullOrWhiteSpace(_settings.BaseUrl))
            _http.BaseAddress = new Uri(_settings.BaseUrl.TrimEnd('/') + "/");

        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("IcedSubDL/0.1 (https://github.com/local/IcedSubDL)");
    }

    public string Name => "ASSRT";

    public bool IsConfigured =>
        _settings.Enabled && !string.IsNullOrWhiteSpace(_settings.Token);

    public async Task<IReadOnlyList<SubtitleCandidate>> SearchAsync(
        MediaInfo media,
        SearchOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return [];

        var seenIds = new HashSet<int>();
        var list = new List<SubtitleCandidate>();

        foreach (var (query, isFile) in BuildSearchQueries(media))
        {
            if (query.Length < 3)
                continue;

            cancellationToken.ThrowIfCancellationRequested();

            string url =
                $"v1/sub/search?token={Uri.EscapeDataString(_settings.Token!)}" +
                $"&q={Uri.EscapeDataString(query)}" +
                $"&cnt={Math.Clamp(options.MaxResultsPerProvider, 1, 15)}" +
                "&filelist=1" +
                (isFile ? "&is_file=1" : string.Empty);

            AssrtSearchResponse? response = await GetAsync<AssrtSearchResponse>(url, cancellationToken)
                .ConfigureAwait(false);

            if (response is null)
                continue;

            if (response.Status != 0)
            {
                _logger?.LogDebug("ASSRT search status={Status} for q={Query}", response.Status, query);
                continue;
            }

            // ASSRT returns "subs": {} (empty object) when there are zero hits — converter yields empty list.
            IReadOnlyList<AssrtSubItem> subs = response.Sub?.Subs ?? [];
            if (subs.Count == 0)
            {
                _logger?.LogDebug("ASSRT 0 hits for q={Query} (is_file={IsFile})", query, isFile);
                continue;
            }

            _logger?.LogInformation(
                "ASSRT {Count} raw hit(s) for q={Query} (is_file={IsFile})",
                subs.Count,
                query,
                isFile);

            foreach (var sub in subs)
            {
                if (!seenIds.Add(sub.Id))
                    continue;

                var candidate = ToCandidate(sub, options);
                if (candidate is not null)
                    list.Add(candidate);
            }

            // Enough results — stop widening the query
            if (list.Count >= options.MaxResultsPerProvider)
                break;
        }

        return list.Take(options.MaxResultsPerProvider).ToList();
    }

    public async Task<SubtitleFile> DownloadAsync(
        SubtitleCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("ASSRT is not configured.");

        if (!int.TryParse(candidate.DownloadKey, out int id))
            throw new InvalidOperationException($"Invalid ASSRT id: {candidate.DownloadKey}");

        string url =
            $"v1/sub/detail?token={Uri.EscapeDataString(_settings.Token!)}&id={id}";

        AssrtDetailResponse? response = await GetAsync<AssrtDetailResponse>(url, cancellationToken)
            .ConfigureAwait(false);

        if (response is null || response.Status != 0 || response.Sub?.Subs is not { Count: > 0 })
            throw new InvalidOperationException($"ASSRT detail failed for id={id}");

        AssrtSubDetail detail = response.Sub.Subs[0];

        // Save original payload as-is (no archive extraction).
        // Prefer package URL; fall back to first filelist entry.
        string? downloadUrl = detail.Url;
        string hintName = detail.Filename
                          ?? detail.Filelist?.FirstOrDefault()?.F
                          ?? "subtitle.bin";

        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            AssrtFileEntry? any = detail.Filelist?.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.Url));
            downloadUrl = any?.Url;
            if (!string.IsNullOrWhiteSpace(any?.F))
                hintName = any!.F!;
        }

        if (string.IsNullOrWhiteSpace(downloadUrl))
            throw new InvalidOperationException($"ASSRT id={id} has no download URL.");

        byte[] bytes = await DownloadBytesWithFallbackAsync(downloadUrl, cancellationToken)
            .ConfigureAwait(false);

        if (bytes.Length == 0)
            throw new InvalidOperationException($"ASSRT id={id} returned empty file.");

        // Keep provider original name as-is (only strip directory; invalid chars cleaned at save).
        string safeName = Path.GetFileName(hintName.Trim());
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = $"assrt-{id}.bin";

        // If server name has no extension, infer from magic / subtype (still keep base name)
        if (string.IsNullOrEmpty(Path.GetExtension(safeName)))
        {
            string ext = SubtitleFormat.CombineHints(null, detail.Subtype ?? candidate.Extra.GetValueOrDefault("subtype"));
            if (IsRar(bytes))
                ext = "rar";
            else if (IsZip(bytes))
                ext = "zip";
            safeName = $"{safeName}.{ext}";
        }

        string format = SubtitleFormat.FromFileName(safeName, SubtitleFormat.FromSubtypeLabel(detail.Subtype));

        return new SubtitleFile
        {
            Candidate = candidate,
            Content = bytes,
            SuggestedFileName = safeName,
            Format = format
        };
    }

    /// <summary>
    /// Ordered search attempts: specific file name first, then broader keywords.
    /// </summary>
    public static IEnumerable<(string Query, bool IsFile)> BuildSearchQueries(MediaInfo media)
    {
        var ordered = new List<(string Query, bool IsFile)>();

        void Add(string? q, bool isFile)
        {
            if (string.IsNullOrWhiteSpace(q))
                return;
            q = MultiSpaceRegex().Replace(q.Trim(), " ");
            if (q.Length < 3)
                return;
            if (ordered.Any(x =>
                    string.Equals(x.Query, q, StringComparison.OrdinalIgnoreCase) && x.IsFile == isFile))
                return;
            ordered.Add((q, isFile));
        }

        string fileStem = media.FileNameWithoutExtension;
        string? parsed = media.ParsedTitle;

        // Pure keyword: free-text only (no is_file, no hash, no local path)
        if (media.IsKeywordQuery)
        {
            Add(parsed ?? fileStem, isFile: false);
            Add(fileStem, isFile: false);
            return ordered;
        }

        // 1) Exact-ish file name (release matching)
        Add(fileStem, isFile: true);
        // 2) Same stem as free keyword (Chinese titles often fail is_file)
        Add(fileStem, isFile: false);
        // 3) Parsed title
        Add(parsed, isFile: false);

        // 4) Strip common Chinese "edition" noise
        string stripped = StripChineseNoise(fileStem);
        Add(stripped, isFile: false);
        if (!string.IsNullOrWhiteSpace(parsed))
            Add(StripChineseNoise(parsed), isFile: false);

        // 5) Latin tokens only (e.g. "Michael Jackson")
        string latin = ExtractLatinPhrase(fileStem);
        Add(latin, isFile: false);

        // 6) Chinese phrase only — only if reasonably long
        string chinese = ExtractChinesePhrase(fileStem);
        if (chinese.Length >= 4)
            Add(chinese, isFile: false);

        // 7) Combine latin + Chinese
        if (!string.IsNullOrWhiteSpace(latin) && chinese.Length >= 2)
            Add($"{latin} {chinese}", isFile: false);

        return ordered;
    }

    public static string StripChineseNoise(string input)
    {
        string s = NoiseRegex().Replace(input, " ");
        s = MultiSpaceRegex().Replace(s, " ").Trim(' ', '-', '_', '.');
        return s;
    }

    public static string ExtractLatinPhrase(string input)
    {
        var matches = LatinWordRegex().Matches(input);
        if (matches.Count == 0)
            return string.Empty;
        return string.Join(' ', matches.Select(m => m.Value));
    }

    public static string ExtractChinesePhrase(string input)
    {
        var matches = ChineseRunRegex().Matches(input);
        if (matches.Count == 0)
            return string.Empty;
        return string.Join(' ', matches.Select(m => m.Value).OrderByDescending(m => m.Length).Take(2));
    }

    private SubtitleCandidate? ToCandidate(AssrtSubItem sub, SearchOptions options)
    {
        string subtype = sub.Subtype ?? string.Empty;
        string lang = MapAssrtLanguage(sub.Lang?.Desc, sub.Lang?.Langlist);

        if (options.Languages.Count > 0 && !LanguageMatches(lang, options.Languages) &&
            !LanguageMatches(sub.Lang?.Desc ?? string.Empty, options.Languages))
        {
            bool wantZh = options.Languages.Any(l =>
                SubtitleFileNamer.NormalizeLanguageTag(l).StartsWith("zh", StringComparison.OrdinalIgnoreCase));
            bool isZh = (sub.Lang?.Desc ?? string.Empty).Contains('简') ||
                        (sub.Lang?.Desc ?? string.Empty).Contains('繁') ||
                        (sub.Lang?.Desc ?? string.Empty).Contains("双语") ||
                        (sub.Lang?.Desc ?? string.Empty).Contains('中');
            if (!(wantZh && isZh))
                return null;
        }

        // Optional soft filter only when user set PreferredFormats
        string format = SubtitleFormat.FromSubtypeLabel(subtype, fallback: string.IsNullOrWhiteSpace(subtype) ? "bin" : subtype);
        if (options.PreferredFormats is { Count: > 0 } &&
            !SrtArchiveExtractor.IsPreferredFormat(format, options.PreferredFormats) &&
            !SrtArchiveExtractor.IsPreferredFormat(subtype, options.PreferredFormats))
        {
            // keep unknown/empty; only drop clear mismatches
            if (!string.IsNullOrWhiteSpace(format) && format is not "bin")
                return null;
        }

        return new SubtitleCandidate
        {
            ProviderName = Name,
            ProviderId = sub.Id.ToString(),
            DisplayName = sub.NativeName ?? sub.Videoname ?? $"ASSRT #{sub.Id}",
            Language = lang,
            Format = format,
            ReleaseName = sub.Videoname,
            Score = sub.VoteScore,
            DownloadKey = sub.Id.ToString(),
            Extra =
            {
                ["release_site"] = sub.ReleaseSite ?? string.Empty,
                ["subtype"] = subtype
            }
        };
    }

    private async Task<T?> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        Exception? last = null;
        foreach (string baseUrl in EnumerateApiBases())
        {
            string absolute = CombineApiUrl(baseUrl, relativeUrl);
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, absolute);
                if (!string.IsNullOrWhiteSpace(_settings.Token))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.Token);

                using HttpResponseMessage resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
                string body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    last = new HttpRequestException($"HTTP {(int)resp.StatusCode}");
                    continue;
                }

                try
                {
                    return JsonSerializer.Deserialize<T>(body, JsonOptions);
                }
                catch (JsonException)
                {
                    _logger?.LogWarning("ASSRT 返回无法解析的数据");
                    return default;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SocketException or IOException)
            {
                last = ex;
            }
        }

        if (last is not null)
            throw last;
        return default;
    }

    private IEnumerable<string> EnumerateApiBases()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (string? u in new[] { _settings.BaseUrl }.Concat(_settings.AlternateBaseUrls ?? []))
        {
            if (string.IsNullOrWhiteSpace(u))
                continue;
            string t = u.Trim().TrimEnd('/');
            if (seen.Add(t))
                list.Add(t);
        }

        return list;
    }

    private static string CombineApiUrl(string baseUrl, string relativeUrl)
    {
        if (relativeUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            relativeUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return relativeUrl;

        return baseUrl.TrimEnd('/') + "/" + relativeUrl.TrimStart('/');
    }

    private async Task<byte[]> DownloadBytesWithFallbackAsync(string url, CancellationToken cancellationToken)
    {
        Exception? last = null;
        foreach (string candidate in BuildDownloadUrlCandidates(url))
        {
            try
            {
                _logger?.LogDebug("ASSRT downloading: {Url}", SanitizeUrlForLog(candidate));
                byte[] bytes = await DownloadBytesAsync(candidate, cancellationToken).ConfigureAwait(false);
                if (bytes.Length > 0)
                    return bytes;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or SocketException)
            {
                last = ex;
                _logger?.LogDebug(ex, "ASSRT download attempt failed: {Url}", SanitizeUrlForLog(candidate));
            }
        }

        throw last ?? new HttpRequestException("ASSRT download failed for all URL candidates.");
    }

    private async Task<byte[]> DownloadBytesAsync(string url, CancellationToken cancellationToken)
    {
        // Use file client (DNS remap) for broken file CDN hosts; otherwise normal API client is fine.
        HttpClient client = NeedsFileHostRemap(url) ? FileDownloadClient : _http;
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using HttpResponseMessage resp = await client.SendAsync(req, cancellationToken).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Prefer HTTPS and alternate fileN hosts when DNS/CDN is flaky.
    /// </summary>
    public static IEnumerable<string> BuildDownloadUrlCandidates(string originalUrl)
    {
        if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out Uri? uri))
        {
            yield return originalUrl;
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string host in EnumerateHostFallbacks(uri.Host))
        {
            // HTTPS first (file CDN works on 443 via api edge)
            var https = new UriBuilder(uri) { Host = host, Scheme = "https", Port = -1 };
            if (seen.Add(https.Uri.AbsoluteUri))
                yield return https.Uri.AbsoluteUri;

            // Original scheme/port as secondary
            var same = new UriBuilder(uri) { Host = host };
            if (seen.Add(same.Uri.AbsoluteUri))
                yield return same.Uri.AbsoluteUri;
        }
    }

    private static IEnumerable<string> EnumerateHostFallbacks(string host)
    {
        yield return host;

        if (!IsAssrtFileHost(host))
            yield break;

        // Swap file0 <-> file1 (historical tip from the community)
        if (host.StartsWith("file0.", StringComparison.OrdinalIgnoreCase))
            yield return "file1." + host["file0.".Length..];
        else if (host.StartsWith("file1.", StringComparison.OrdinalIgnoreCase))
            yield return "file0." + host["file1.".Length..];
        else if (host.StartsWith("file", StringComparison.OrdinalIgnoreCase))
        {
            yield return "file0.assrt.net";
            yield return "file1.assrt.net";
        }
    }

    public static bool IsAssrtFileHost(string host) =>
        host.Equals("glb.assrt.net", StringComparison.OrdinalIgnoreCase) ||
        (host.EndsWith(".assrt.net", StringComparison.OrdinalIgnoreCase) &&
         host.StartsWith("file", StringComparison.OrdinalIgnoreCase));

    private static bool NeedsFileHostRemap(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && IsAssrtFileHost(uri.Host);

    private static string SanitizeUrlForLog(string url)
    {
        // Drop query string (contains signed tokens)
        int q = url.IndexOf('?', StringComparison.Ordinal);
        return q >= 0 ? url[..q] + "?*" : url;
    }

    private static HttpClient CreateFileDownloadClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectCallback = async (context, cancellationToken) =>
            {
                string dnsHost = context.DnsEndPoint.Host;
                int port = context.DnsEndPoint.Port;

                // file*.assrt.net -> glb.assrt.net has no A/AAAA; api.assrt.net serves the files.
                string resolveHost = IsAssrtFileHost(dnsHost) ? "api.assrt.net" : dnsHost;

                IPAddress[] addresses = await Dns
                    .GetHostAddressesAsync(resolveHost, cancellationToken)
                    .ConfigureAwait(false);

                // Prefer IPv4
                IEnumerable<IPAddress> ordered = addresses
                    .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1);

                Exception? last = null;
                foreach (IPAddress ip in ordered)
                {
                    var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(ip, port, cancellationToken).ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception ex)
                    {
                        socket.Dispose();
                        last = ex;
                    }
                }

                throw last ?? new SocketException((int)SocketError.HostNotFound);
            }
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(90)
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "IcedSubDL/0.1 (https://assrt.net)");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
        return client;
    }

    private static string MapAssrtLanguage(string? desc, AssrtLangList? list)
    {
        desc ??= string.Empty;
        if (desc.Contains("双语", StringComparison.Ordinal))
            return "zh-CN";
        if (desc.Contains('繁') || desc.Contains("繁体", StringComparison.Ordinal))
            return "zh-TW";
        if (desc.Contains('简') || desc.Contains("简体", StringComparison.Ordinal) || desc.Contains('中'))
            return "zh-CN";
        if (desc.Contains('英') || list?.Langeng == true)
            return "en";
        if (list?.Langchs == true)
            return "zh-CN";
        if (list?.Langcht == true)
            return "zh-TW";
        return string.IsNullOrWhiteSpace(desc) ? "zh-CN" : desc;
    }

    private static bool LanguageMatches(string lang, IReadOnlyList<string> wanted)
    {
        string n = SubtitleFileNamer.NormalizeLanguageTag(lang);
        return wanted.Any(w =>
        {
            string wn = SubtitleFileNamer.NormalizeLanguageTag(w);
            return string.Equals(n, wn, StringComparison.OrdinalIgnoreCase) ||
                   n.StartsWith(wn, StringComparison.OrdinalIgnoreCase) ||
                   wn.StartsWith(n, StringComparison.OrdinalIgnoreCase) ||
                   (n.StartsWith("zh", StringComparison.OrdinalIgnoreCase) &&
                    wn.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
        });
    }

    private static bool IsRar(byte[] data) =>
        data.Length >= 4 && data[0] == 0x52 && data[1] == 0x61 && data[2] == 0x72 && data[3] == 0x21;

    private static bool IsZip(byte[] data) =>
        data.Length >= 4 && data[0] == (byte)'P' && data[1] == (byte)'K';

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300] + "...";

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new FlexibleListConverterFactory());
        return options;
    }

    [GeneratedRegex(@"[.\s_\-]+")]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(
        @"(?:4K|8K|修复版|修复|高清|蓝光|完整版|官方|中字|特效|内嵌|外挂|历史演唱会|演唱会|演唱会版|珍藏版|收藏版|Remaster(?:ed)?|Restored|Live)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NoiseRegex();

    [GeneratedRegex(@"[A-Za-z]{2,}")]
    private static partial Regex LatinWordRegex();

    [GeneratedRegex(@"[\u4e00-\u9fff]{2,}")]
    private static partial Regex ChineseRunRegex();

    /// <summary>
    /// ASSRT returns "subs": [] when hits exist, but "subs": {} when empty.
    /// Also sometimes a single object instead of an array.
    /// </summary>
    private sealed class FlexibleListConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeToConvert.IsGenericType &&
            typeToConvert.GetGenericTypeDefinition() == typeof(List<>);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            Type elementType = typeToConvert.GetGenericArguments()[0];
            Type converterType = typeof(FlexibleListConverter<>).MakeGenericType(elementType);
            return (JsonConverter)Activator.CreateInstance(converterType)!;
        }
    }

    private sealed class FlexibleListConverter<T> : JsonConverter<List<T>>
    {
        public override List<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return null;

                case JsonTokenType.StartArray:
                {
                    var list = new List<T>();
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        var item = JsonSerializer.Deserialize<T>(ref reader, options);
                        if (item is not null)
                            list.Add(item);
                    }

                    return list;
                }

                case JsonTokenType.StartObject:
                {
                    // Empty object {} => empty list. Non-empty object => single-item list if deserializable.
                    using var doc = JsonDocument.ParseValue(ref reader);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object && !doc.RootElement.EnumerateObject().Any())
                        return [];

                    // Numeric keys? (unlikely) — try values
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        var list = new List<T>();
                        bool allNumericKeys = doc.RootElement.EnumerateObject().All(p => int.TryParse(p.Name, out _));
                        if (allNumericKeys && doc.RootElement.EnumerateObject().Any())
                        {
                            foreach (var prop in doc.RootElement.EnumerateObject())
                            {
                                var item = prop.Value.Deserialize<T>(options);
                                if (item is not null)
                                    list.Add(item);
                            }

                            return list;
                        }

                        var single = doc.RootElement.Deserialize<T>(options);
                        return single is null ? [] : [single];
                    }

                    return [];
                }

                default:
                    throw new JsonException($"Unexpected token {reader.TokenType} when reading List<{typeof(T).Name}>.");
            }
        }

        public override void Write(Utf8JsonWriter writer, List<T> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, options);
    }

    private sealed class AssrtSearchResponse
    {
        public int Status { get; set; }
        public AssrtSubBlock? Sub { get; set; }
    }

    private sealed class AssrtDetailResponse
    {
        public int Status { get; set; }
        public AssrtDetailBlock? Sub { get; set; }
    }

    private sealed class AssrtSubBlock
    {
        public List<AssrtSubItem>? Subs { get; set; }
    }

    private sealed class AssrtDetailBlock
    {
        public List<AssrtSubDetail>? Subs { get; set; }
    }

    private sealed class AssrtSubItem
    {
        public int Id { get; set; }

        [JsonPropertyName("native_name")]
        public string? NativeName { get; set; }

        public string? Videoname { get; set; }
        public string? Subtype { get; set; }

        [JsonPropertyName("vote_score")]
        public int VoteScore { get; set; }

        [JsonPropertyName("release_site")]
        public string? ReleaseSite { get; set; }

        public AssrtLang? Lang { get; set; }
    }

    private sealed class AssrtSubDetail
    {
        public int Id { get; set; }

        [JsonPropertyName("native_name")]
        public string? NativeName { get; set; }

        public string? Videoname { get; set; }
        public string? Subtype { get; set; }

        [JsonPropertyName("vote_score")]
        public int VoteScore { get; set; }

        [JsonPropertyName("release_site")]
        public string? ReleaseSite { get; set; }

        public AssrtLang? Lang { get; set; }
        public string? Filename { get; set; }
        public string? Url { get; set; }
        public List<AssrtFileEntry>? Filelist { get; set; }
    }

    private sealed class AssrtFileEntry
    {
        public string? F { get; set; }
        public string? Url { get; set; }
        public string? S { get; set; }
    }

    private sealed class AssrtLang
    {
        public string? Desc { get; set; }
        public AssrtLangList? Langlist { get; set; }
    }

    private sealed class AssrtLangList
    {
        [JsonPropertyName("langdou")]
        public bool Langdou { get; set; }

        [JsonPropertyName("langchs")]
        public bool Langchs { get; set; }

        [JsonPropertyName("langcht")]
        public bool Langcht { get; set; }

        [JsonPropertyName("langeng")]
        public bool Langeng { get; set; }
    }
}

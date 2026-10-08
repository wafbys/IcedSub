using System.CommandLine;
using IcedSub.Cli.Configuration;
using IcedSub.Core.Media;
using IcedSub.Core.Models;
using IcedSub.Core.Services;
using IcedSub.Metadata.Models;
using IcedSub.Metadata.Naming;
using IcedSub.Metadata.Providers;
using IcedSub.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Windows 控制台默认代码页（如 437/1252）无法表示中文，会把中文写成 "?"。
// 启动时显式切换为 UTF-8；输出/输入被重定向（无控制台）时忽略失败。
try
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.InputEncoding = System.Text.Encoding.UTF8;
}
catch (IOException)
{
    // No attached console; keep the default encoding.
}

// Default command is search when none given:
//   icedsub 火遮眼          -> search 火遮眼
//   icedsub search 火遮眼
//   icedsub download 火遮眼
//   icedsub name tt33311069
string[] normalized = CliApp.NormalizeArgs(args);

var queryArg = new Argument<string[]>("query")
{
    Description = "Keyword, video file, or directory",
    Arity = ArgumentArity.OneOrMore
};

var outOption = new Option<string?>("--out", "-o")
{
    Description = "Output directory (keyword downloads; default: current directory)"
};

var langOption = new Option<string[]>("--lang", "-l")
{
    Description = "Language priority, e.g. zh-CN en",
    AllowMultipleArgumentsPerToken = true
};

var topOnlyOption = new Option<bool>("--top-only")
{
    Description = "Download only the best match (default: all)",
    DefaultValueFactory = _ => false
};

var overwriteOption = new Option<bool>("--overwrite")
{
    Description = "Overwrite existing files with the same name",
    DefaultValueFactory = _ => false
};

var imdbArg = new Argument<string?>("imdb")
{
    Description = "IMDb id，例如 tt33311069（可省略，进入填空交互）",
    Arity = ArgumentArity.ZeroOrOne
};

var searchCommand = new Command("search", "Search subtitles (default command)");
searchCommand.Arguments.Add(queryArg);
searchCommand.Options.Add(outOption);
searchCommand.Options.Add(langOption);
searchCommand.SetAction(async (parseResult, ct) =>
{
    await CliApp.RunAsync(
        queries: parseResult.GetValue(queryArg) ?? [],
        download: false,
        outDir: parseResult.GetValue(outOption),
        langs: parseResult.GetValue(langOption),
        topOnly: false,
        overwrite: false,
        ct).ConfigureAwait(false);
});

var downloadCommand = new Command("download", "Search and download subtitles (all matches; original file names)");
downloadCommand.Arguments.Add(queryArg);
downloadCommand.Options.Add(outOption);
downloadCommand.Options.Add(langOption);
downloadCommand.Options.Add(topOnlyOption);
downloadCommand.Options.Add(overwriteOption);
downloadCommand.SetAction(async (parseResult, ct) =>
{
    await CliApp.RunAsync(
        queries: parseResult.GetValue(queryArg) ?? [],
        download: true,
        outDir: parseResult.GetValue(outOption),
        langs: parseResult.GetValue(langOption),
        topOnly: parseResult.GetValue(topOnlyOption),
        overwrite: parseResult.GetValue(overwriteOption),
        ct).ConfigureAwait(false);
});

var nameCommand = new Command(
    "name",
    "按 TMDB/Wikidata 生成规范目录名：中文_English_(年份)_imdb-id_douban-id");
nameCommand.Arguments.Add(imdbArg);

var doubanOption = new Option<string?>("--douban")
{
    Description = "豆瓣 id（可省略，进入填空交互）"
};
nameCommand.Options.Add(doubanOption);

var yesOption = new Option<bool>("--yes", "-y")
{
    Description = "不进入填空/确认，直接输出（适合脚本）",
    DefaultValueFactory = _ => false
};
nameCommand.Options.Add(yesOption);

nameCommand.SetAction(async (parseResult, ct) =>
{
    string? imdb = parseResult.GetValue(imdbArg);
    string? douban = parseResult.GetValue(doubanOption);
    bool yes = parseResult.GetValue(yesOption);
    await CliApp.RunNameAsync(imdb, douban, yes, ct).ConfigureAwait(false);
});

var root = new RootCommand("IcedSub — multi-source subtitle tool");
root.Subcommands.Add(searchCommand);
root.Subcommands.Add(downloadCommand);
root.Subcommands.Add(nameCommand);

root.SetAction(_ =>
{
    Console.WriteLine("IcedSub");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  icedsub <query>                 # same as search");
    Console.WriteLine("  icedsub search <query>");
    Console.WriteLine("  icedsub download <query> [-o dir]");
    Console.WriteLine("  icedsub name [tt...]            # 填空交互 → 确认 → 输出目录名");
    Console.WriteLine("  icedsub name tt... --douban <id> -y   # 直接输出，不走交互");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  icedsub 火遮眼");
    Console.WriteLine("  icedsub download 火遮眼 -o D:\\Subs");
    Console.WriteLine("  icedsub name");
    Console.WriteLine("  icedsub name tt0111161");
    Console.WriteLine();
    Console.WriteLine("Name: 肖申克的救赎_The Shawshank Redemption_(1994)_imdb-tt0111161_douban-1292052");
    Console.WriteLine("Sources: OpenSubtitles.com, ASSRT; naming via TMDB + Wikidata");
    Console.WriteLine("Config : %AppData%\\IcedSub\\appsettings.json");
    Console.WriteLine("        ICEDSUB_ASSRT_TOKEN / ICEDSUB_TMDB_APIKEY");
});

return await root.Parse(normalized).InvokeAsync();

internal static class CliApp
{
    private static readonly HashSet<string> Commands =
        new(StringComparer.OrdinalIgnoreCase) { "search", "download", "name", "-h", "--help", "-?", "help" };

    public static string[] NormalizeArgs(string[] args)
    {
        if (args.Length == 0)
            return args;

        string first = args[0];
        if (Commands.Contains(first) || first.StartsWith('-'))
            return args;

        // name-like imdb id as first token without subcommand → still search by default
        // (user must type: name tt...)
        var list = new List<string>(args.Length + 1) { "search" };
        list.AddRange(args);
        return list.ToArray();
    }

    public static async Task RunAsync(
        string[] queries,
        bool download,
        string? outDir,
        string[]? langs,
        bool topOnly,
        bool overwrite,
        CancellationToken ct)
    {
        if (queries.Length == 0)
        {
            Console.WriteLine("Missing query. Example: icedsub search 火遮眼");
            return;
        }

        AppSettings settings = SettingsLoader.Load();
        await using ServiceProvider sp = BuildServices(settings);
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("IcedSub");
        var scanner = sp.GetRequiredService<VideoScanner>();
        var orchestrator = sp.GetRequiredService<SubtitleOrchestrator>();

        PrintProviders(settings);

        var options = new SearchOptions
        {
            Languages = langs is { Length: > 0 } ? langs.ToList() : settings.Defaults.Languages,
            PreferredFormats = settings.Defaults.PreferredFormats,
            MaxResultsPerProvider = settings.Defaults.MaxResultsPerProvider,
            SkipIfExternalSrtExists = false,
            DownloadMode = download
                ? (topOnly ? DownloadMode.Top : DownloadMode.All)
                : DownloadMode.None,
            OverwriteExisting = overwrite
        };

        var mediaList = await scanner.ScanAsync(
                queries,
                computeHash: true,
                keywordOutputDirectory: outDir,
                cancellationToken: ct)
            .ConfigureAwait(false);

        if (mediaList.Count == 0)
        {
            logger.LogWarning("Nothing to process.");
            return;
        }

        foreach (var media in mediaList)
        {
            Console.WriteLine();
            if (media.IsKeywordQuery)
            {
                Console.WriteLine($"==== {media.ParsedTitle} ====");
                Console.WriteLine($"out: {media.OutputDirectory}");
            }
            else
            {
                Console.WriteLine($"==== {media.FileName} ====");
                Console.WriteLine($"path: {media.FilePath}");
            }

            var result = await orchestrator.ProcessAsync(media, options, ct).ConfigureAwait(false);

            int i = 0;
            foreach (var c in result.Candidates.Take(15))
            {
                i++;
                string score = c.Extra.GetValueOrDefault("computed_score") ?? "-";
                Console.WriteLine($"  [{i}] ({score}) {c.ProviderName} | {c.Language} | {c.Format} | {c.DisplayName}");
                if (!string.IsNullOrWhiteSpace(c.ReleaseName))
                    Console.WriteLine($"       {c.ReleaseName}");
            }

            if (result.Candidates.Count == 0)
                Console.WriteLine("  (未找到字幕)");

            Console.WriteLine($"status: {result.Status}");
            foreach (string saved in result.SavedPaths)
                Console.WriteLine($"saved:  {saved}");
            if (result.Failures is { Count: > 0 })
            {
                foreach (string fail in result.Failures)
                    Console.WriteLine($"note:   {fail}");
            }
        }
    }

    public static async Task RunNameAsync(
        string? imdbId,
        string? doubanOverride,
        bool assumeYes,
        CancellationToken ct)
    {
        bool interactive = !assumeYes && !Console.IsInputRedirected;

        string? tt = string.IsNullOrWhiteSpace(imdbId) ? null : imdbId.Trim();
        string? douban = string.IsNullOrWhiteSpace(doubanOverride) ? null : doubanOverride.Trim();

        if (interactive)
        {
            Console.WriteLine("填空：IMDb id / 豆瓣 id 任填其一或都填（回车跳过）。");
            if (tt is null)
            {
                Console.Write("  IMDb id (tt...): ");
                tt = ReadNotEmpty();
            }
            if (douban is null)
            {
                Console.Write("  豆瓣 id: ");
                douban = ReadNotEmpty();
            }
        }

        if (string.IsNullOrWhiteSpace(tt) && string.IsNullOrWhiteSpace(douban))
        {
            Console.WriteLine("至少提供一个：IMDb id 或 豆瓣 id。");
            Console.WriteLine("用法: icedsub name [tt...] [--douban <id>] [-y]");
            return;
        }

        AppSettings settings = SettingsLoader.Load();
        await using ServiceProvider sp = BuildServices(settings);
        var tmdb = sp.GetRequiredService<TmdbMetadataClient>();
        var wikidata = sp.GetRequiredService<WikidataDoubanResolver>();

        try
        {
            BilingualTitle? meta = null;

            if (tmdb.IsConfigured)
            {
                // 只给豆瓣 id 时，先反查 IMDb id（TMDB 需要它取标题）。
                if (string.IsNullOrWhiteSpace(tt) && !string.IsNullOrWhiteSpace(douban))
                {
                    Console.WriteLine($"正在由豆瓣 id {douban} 反查 IMDb id ...");
                    string? resolved = await wikidata.GetImdbIdAsync(douban, ct).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(resolved))
                        Console.WriteLine("无法由该豆瓣 id 反查到 IMDb id（Wikidata 可能未收录）。");
                    else
                    {
                        tt = resolved;
                        Console.WriteLine($"  → {tt}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(tt))
                {
                    Console.Write("正在查询元数据 (TMDB) ...");
                    meta = await tmdb.GetByImdbIdAsync(tt!, ct, douban).ConfigureAwait(false);
                    Console.WriteLine(" 完成");
                }
            }

            if (meta is null)
            {
                // 兜底：未配置 TMDB，或 TMDB 未收录时，用免密钥的 Wikidata。
                if (!tmdb.IsConfigured)
                    Console.WriteLine("未配置 TMDB，改用 Wikidata 兜底（覆盖可能不全；配置 ICEDSUB_TMDB_APIKEY 可提高准确性）。");
                else
                    Console.WriteLine("TMDB 未收录，改用 Wikidata 兜底。");

                Console.Write("正在查询元数据 (Wikidata) ...");
                meta = await wikidata.GetTitleAsync(tt, douban, ct).ConfigureAwait(false);
                Console.WriteLine(" 完成");
            }

            Console.WriteLine();

            if (meta is null)
            {
                Console.WriteLine("未找到元数据（TMDB / Wikidata 均未收录，或 id 有误）。");
                return;
            }

            if (interactive)
                meta = EditFields(meta);
            else
                PrintFields(meta);

            string name = BilingualNamePattern.Build(meta);

            if (interactive)
            {
                Console.WriteLine();
                Console.Write("确认生成？[Y/n]: ");
                string? ans = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (ans is "n" or "no")
                {
                    Console.WriteLine("已取消。");
                    return;
                }
                Console.WriteLine();
            }

            Console.WriteLine($"目录名: {name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("失败: " + SubtitleOrchestrator.FormatShortError(ex));
        }
    }

    private static void PrintFields(BilingualTitle meta)
    {
        Console.WriteLine($"TMDB:  {(meta.TmdbId?.ToString() ?? "-")}");
        Console.WriteLine($"IMDb:  {meta.ImdbId}");
        Console.WriteLine($"Douban:{meta.DoubanId ?? "-"}");
        Console.WriteLine($"ZH:    {meta.TitleZh ?? "-"}");
        Console.WriteLine($"EN:    {meta.TitleEn}");
        Console.WriteLine($"Year:  {meta.Year ?? "-"}");
    }

    /// <summary>
    /// 逐项回车确认当前反查值，或输入新值覆盖；输入 "-" 清空该段。
    /// </summary>
    private static BilingualTitle EditFields(BilingualTitle meta)
    {
        Console.WriteLine("逐项确认（回车接受反查值，输入新值覆盖，输入 - 清空）：");

        string zh = PromptField("中文名", meta.TitleZh);
        string en = PromptField("English", meta.TitleEn);
        string year = PromptField("年份", meta.Year);
        string imdb = PromptField("imdb-id", meta.ImdbId);
        string douban = PromptField("douban-id", meta.DoubanId);

        return new BilingualTitle
        {
            ImdbId = imdb,
            TmdbId = meta.TmdbId,
            TitleEn = en,
            TitleZh = string.IsNullOrEmpty(zh) ? null : zh,
            Year = string.IsNullOrEmpty(year) ? null : year,
            DoubanId = string.IsNullOrEmpty(douban) ? null : douban
        };
    }

    private static string PromptField(string label, string? current)
    {
        Console.Write($"  {label} [{current ?? ""}]: ");
        string input = (Console.ReadLine() ?? string.Empty).Trim();
        if (input.Length == 0)
            return current ?? string.Empty;
        if (input == "-")
            return string.Empty;
        return input;
    }

    private static string? ReadNotEmpty()
    {
        string? line = Console.ReadLine();
        return string.IsNullOrWhiteSpace(line) ? null : line.Trim();
    }

    public static ServiceProvider BuildServices(AppSettings settings)
    {
        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.TimestampFormat = "HH:mm:ss ";
            });
            b.SetMinimumLevel(LogLevel.Information);
            b.AddFilter("System.Net.Http", LogLevel.Warning);
            b.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
            b.AddFilter("Microsoft.Extensions.Http", LogLevel.Warning);
        });
        services.AddIcedSubProviders(settings);
        services.AddSingleton(settings);
        services.AddSingleton<VideoScanner>();
        services.AddSingleton<SubtitleOrchestrator>();

        services.AddHttpClient("tmdb", c =>
        {
            c.BaseAddress = new Uri("https://api.themoviedb.org/3/");
            c.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient("wikidata", c =>
        {
            c.BaseAddress = new Uri("https://query.wikidata.org/");
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "IcedSub/0.1 (subtitle tool; +https://github.com/local/IcedSub)");
            c.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/sparql-results+json");
        });
        services.AddTransient(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("wikidata");
            return new WikidataDoubanResolver(http, sp.GetService<ILogger<WikidataDoubanResolver>>());
        });
        services.AddTransient(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("tmdb");
            return new TmdbMetadataClient(
                http,
                settings.Naming.TmdbApiKey,
                settings.Naming.TmdbReadAccessToken,
                sp.GetService<ILogger<TmdbMetadataClient>>(),
                sp.GetService<WikidataDoubanResolver>());
        });

        return services.BuildServiceProvider();
    }

    private static void PrintProviders(AppSettings settings)
    {
        string os = settings.OpenSubtitles.Enabled && !string.IsNullOrWhiteSpace(settings.OpenSubtitles.ApiKey)
            ? "on"
            : "off";
        string assrt = settings.Assrt.Enabled && !string.IsNullOrWhiteSpace(settings.Assrt.Token)
            ? "on"
            : "off";
        Console.WriteLine($"providers: OpenSubtitles={os}  ASSRT={assrt}");
    }
}

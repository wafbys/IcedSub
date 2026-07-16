using System.CommandLine;
using IcedSubDL.Cli.Configuration;
using IcedSubDL.Core.Media;
using IcedSubDL.Core.Models;
using IcedSubDL.Core.Services;
using IcedSubDL.Metadata.Naming;
using IcedSubDL.Metadata.Providers;
using IcedSubDL.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Default command is search when none given:
//   icedsubdl 火遮眼          -> search 火遮眼
//   icedsubdl search 火遮眼
//   icedsubdl download 火遮眼
//   icedsubdl name tt33311069
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

var imdbArg = new Argument<string>("imdb")
{
    Description = "IMDb id，例如 tt33311069"
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
    "按 TMDB 生成文件名：English (中文)_year_Director (导演中文)_ttid");
nameCommand.Arguments.Add(imdbArg);
nameCommand.SetAction(async (parseResult, ct) =>
{
    string imdb = parseResult.GetValue(imdbArg) ?? string.Empty;
    await CliApp.RunNameAsync(imdb, ct).ConfigureAwait(false);
});

var root = new RootCommand("IcedSubDL — multi-source subtitle tool");
root.Subcommands.Add(searchCommand);
root.Subcommands.Add(downloadCommand);
root.Subcommands.Add(nameCommand);

root.SetAction(_ =>
{
    Console.WriteLine("IcedSubDL");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  icedsubdl <query>                 # same as search");
    Console.WriteLine("  icedsubdl search <query>");
    Console.WriteLine("  icedsubdl download <query> [-o dir]");
    Console.WriteLine("  icedsubdl name <tt...>            # English (中文)_year_Director (中文)_ttid");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  icedsubdl 火遮眼");
    Console.WriteLine("  icedsubdl download 火遮眼 -o D:\\Subs");
    Console.WriteLine("  icedsubdl name tt33311069");
    Console.WriteLine();
    Console.WriteLine("Name: The Furious (火遮眼)_2025_Kenji Tanigaki (谷垣健治)_tt33311069");
    Console.WriteLine("Sources: OpenSubtitles.com, ASSRT; naming via TMDB");
    Console.WriteLine("Config : %AppData%\\IcedSubDL\\appsettings.json");
    Console.WriteLine("        ICEDSUBDL_ASSRT_TOKEN / ICEDSUBDL_TMDB_APIKEY");
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
            Console.WriteLine("Missing query. Example: icedsubdl search 火遮眼");
            return;
        }

        AppSettings settings = SettingsLoader.Load();
        await using ServiceProvider sp = BuildServices(settings);
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("IcedSubDL");
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

    public static async Task RunNameAsync(string imdbId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(imdbId))
        {
            Console.WriteLine("请提供 IMDb id，例如：");
            Console.WriteLine("  icedsubdl name tt33311069");
            return;
        }

        AppSettings settings = SettingsLoader.Load();
        await using ServiceProvider sp = BuildServices(settings);
        var tmdb = sp.GetRequiredService<TmdbMetadataClient>();

        if (!tmdb.IsConfigured)
        {
            Console.WriteLine("未配置 TMDB。");
            Console.WriteLine("  在 appsettings.json 设置 Naming:TmdbApiKey");
            Console.WriteLine("  或环境变量 ICEDSUBDL_TMDB_APIKEY");
            Console.WriteLine("  申请：https://www.themoviedb.org/settings/api");
            return;
        }

        Console.WriteLine("模式: English (中文)_year_Director (导演中文)_ttid");
        Console.WriteLine($"查询: {TmdbMetadataClient.NormalizeImdbId(imdbId)}");
        Console.WriteLine();

        try
        {
            var meta = await tmdb.GetByImdbIdAsync(imdbId, ct).ConfigureAwait(false);
            if (meta is null)
            {
                Console.WriteLine("未找到元数据（TMDB 可能尚未收录，或 id 有误）。");
                return;
            }

            string fileName = BilingualNamePattern.Build(meta);
            Console.WriteLine($"TMDB:  {meta.TmdbId}");
            Console.WriteLine($"IMDb:  {meta.ImdbId}");
            Console.WriteLine($"EN:    {meta.TitleEn}");
            Console.WriteLine($"ZH:    {meta.TitleZh ?? "-"}");
            Console.WriteLine($"Year:  {meta.Year ?? "-"}");
            Console.WriteLine($"DirEN: {meta.DirectorEn ?? "-"}");
            Console.WriteLine($"DirZH: {meta.DirectorZh ?? "-"}");
            Console.WriteLine();
            Console.WriteLine($"文件名: {fileName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("失败: " + SubtitleOrchestrator.FormatShortError(ex));
        }
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
        services.AddTransient(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("tmdb");
            return new TmdbMetadataClient(
                http,
                settings.Naming.TmdbApiKey,
                settings.Naming.TmdbReadAccessToken,
                sp.GetService<ILogger<TmdbMetadataClient>>());
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

using System.CommandLine;
using IcedSubDL.Cli.Configuration;
using IcedSubDL.Core.Media;
using IcedSubDL.Core.Models;
using IcedSubDL.Core.Services;
using IcedSubDL.Metadata.Naming;
using IcedSubDL.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Default command is search when none given:
//   icedsubdl 火遮眼          -> search 火遮眼
//   icedsubdl search 火遮眼
//   icedsubdl download 火遮眼
//   icedsubdl name ...        (reserved)
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

var nameCommand = new Command("name", "Normalize media file names (reserved — not implemented yet)");
nameCommand.Arguments.Add(queryArg);
nameCommand.SetAction((parseResult, _) =>
{
    string[] queries = parseResult.GetValue(queryArg) ?? [];
    Console.WriteLine("name: not implemented yet.");
    Console.WriteLine("Reserved for future file renaming with authoritative metadata + rules.");
    if (queries.Length > 0)
        Console.WriteLine("query: " + string.Join(" | ", queries));
    return Task.CompletedTask;
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
    Console.WriteLine("  icedsubdl name <query>            # reserved");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  icedsubdl 火遮眼");
    Console.WriteLine("  icedsubdl search 火遮眼");
    Console.WriteLine("  icedsubdl download 火遮眼 -o D:\\Subs");
    Console.WriteLine("  icedsubdl download D:\\Videos\\movie.mkv");
    Console.WriteLine();
    Console.WriteLine("Sources: OpenSubtitles.com, ASSRT (assrt.net)");
    Console.WriteLine("Config : %AppData%\\IcedSubDL\\appsettings.json");
    Console.WriteLine("        ICEDSUBDL_ASSRT_TOKEN / ICEDSUBDL_OPENSUBTITLES_APIKEY");
    Console.WriteLine();
    Console.WriteLine("字幕服务由 assrt.net 提供（若启用 ASSRT）");
});

return await root.Parse(normalized).InvokeAsync();

internal static class CliApp
{
    private static readonly HashSet<string> Commands =
        new(StringComparer.OrdinalIgnoreCase) { "search", "download", "name", "-h", "--help", "-?", "help" };

    /// <summary>
    /// If the first token is not a known command, treat the whole line as search.
    /// </summary>
    public static string[] NormalizeArgs(string[] args)
    {
        if (args.Length == 0)
            return args;

        string first = args[0];
        if (Commands.Contains(first) || first.StartsWith('-'))
            return args;

        // icedsubdl 火遮眼  ->  icedsubdl search 火遮眼
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
                Console.WriteLine("  (no candidates)");

            Console.WriteLine($"status: {result.Status}");
            foreach (string saved in result.SavedPaths)
                Console.WriteLine($"saved:  {saved}");
            if (result.Failures is { Count: > 0 })
            {
                foreach (string fail in result.Failures)
                    Console.WriteLine($"fail:   {fail}");
            }
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
        });
        services.AddIcedSubProviders(settings);
        services.AddSingleton(settings);
        services.AddSingleton<VideoScanner>();
        services.AddSingleton<SubtitleOrchestrator>();
        services.AddSingleton<IMediaNameNormalizer, NotImplementedMediaNameNormalizer>();
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

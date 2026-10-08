using IcedSub.Core.Models;
using Microsoft.Extensions.Configuration;

namespace IcedSub.Cli.Configuration;

public static class SettingsLoader
{
    public static AppSettings Load(string? configPath = null)
    {
        string baseDir = AppContext.BaseDirectory;
        string userDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "IcedSub");

        var builder = new ConfigurationBuilder()
            .SetBasePath(baseDir)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile(Path.Combine(userDir, "appsettings.json"), optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "ICEDSUB_");

        if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
            builder.AddJsonFile(Path.GetFullPath(configPath), optional: false, reloadOnChange: false);

        IConfigurationRoot config = builder.Build();
        var settings = new AppSettings();
        config.Bind(settings);

        // Env overrides for secrets
        settings.OpenSubtitles.ApiKey =
            FirstNonEmpty(Environment.GetEnvironmentVariable("ICEDSUB_OPENSUBTITLES_APIKEY"),
                settings.OpenSubtitles.ApiKey);
        settings.OpenSubtitles.Username =
            FirstNonEmpty(Environment.GetEnvironmentVariable("ICEDSUB_OPENSUBTITLES_USERNAME"),
                settings.OpenSubtitles.Username);
        settings.OpenSubtitles.Password =
            FirstNonEmpty(Environment.GetEnvironmentVariable("ICEDSUB_OPENSUBTITLES_PASSWORD"),
                settings.OpenSubtitles.Password);
        settings.Assrt.Token =
            FirstNonEmpty(Environment.GetEnvironmentVariable("ICEDSUB_ASSRT_TOKEN"),
                settings.Assrt.Token);
        settings.Naming.TmdbApiKey =
            FirstNonEmpty(Environment.GetEnvironmentVariable("ICEDSUB_TMDB_APIKEY"),
                settings.Naming.TmdbApiKey);
        settings.Naming.TmdbReadAccessToken =
            FirstNonEmpty(Environment.GetEnvironmentVariable("ICEDSUB_TMDB_TOKEN"),
                settings.Naming.TmdbReadAccessToken);

        return settings;
    }

    private static string? FirstNonEmpty(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) ? a : b;
}

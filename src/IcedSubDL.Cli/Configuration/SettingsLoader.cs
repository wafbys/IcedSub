using IcedSubDL.Core.Models;
using Microsoft.Extensions.Configuration;

namespace IcedSubDL.Cli.Configuration;

public static class SettingsLoader
{
    public static AppSettings Load(string? configPath = null)
    {
        string baseDir = AppContext.BaseDirectory;
        string userDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "IcedSubDL");

        var builder = new ConfigurationBuilder()
            .SetBasePath(baseDir)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile(Path.Combine(userDir, "appsettings.json"), optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "ICEDSUBDL_");

        if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
            builder.AddJsonFile(Path.GetFullPath(configPath), optional: false, reloadOnChange: false);

        IConfigurationRoot config = builder.Build();
        var settings = new AppSettings();
        config.Bind(settings);

        // Env overrides for secrets
        settings.OpenSubtitles.ApiKey =
            FirstNonEmpty(Environment.GetEnvironmentVariable("ICEDSUBDL_OPENSUBTITLES_APIKEY"),
                settings.OpenSubtitles.ApiKey);
        settings.OpenSubtitles.Username =
            FirstNonEmpty(Environment.GetEnvironmentVariable("ICEDSUBDL_OPENSUBTITLES_USERNAME"),
                settings.OpenSubtitles.Username);
        settings.OpenSubtitles.Password =
            FirstNonEmpty(Environment.GetEnvironmentVariable("ICEDSUBDL_OPENSUBTITLES_PASSWORD"),
                settings.OpenSubtitles.Password);
        settings.Assrt.Token =
            FirstNonEmpty(Environment.GetEnvironmentVariable("ICEDSUBDL_ASSRT_TOKEN"),
                settings.Assrt.Token);

        return settings;
    }

    private static string? FirstNonEmpty(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) ? a : b;
}

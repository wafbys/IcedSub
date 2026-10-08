namespace IcedSub.Core.Models;

public sealed class AppSettings
{
    public OpenSubtitlesSettings OpenSubtitles { get; set; } = new();
    public AssrtSettings Assrt { get; set; } = new();
    public NamingSettings Naming { get; set; } = new();
    public SearchOptions Defaults { get; set; } = new();
    public string[] VideoExtensions { get; set; } =
    [
        ".mkv", ".mp4", ".avi", ".mov", ".m4v", ".wmv", ".ts", ".m2ts",
        ".webm", ".mpeg", ".mpg", ".ogm", ".flv"
    ];
}

/// <summary>
/// 文件命名元数据（TMDB）。模式：English (中文)_year_Director (导演中文)_ttid
/// </summary>
public sealed class NamingSettings
{
    /// <summary>TMDB API Key（v3）。https://www.themoviedb.org/settings/api</summary>
    public string? TmdbApiKey { get; set; }

    /// <summary>TMDB Read Access Token（Bearer，可选，优先于 ApiKey）。</summary>
    public string? TmdbReadAccessToken { get; set; }
}

public sealed class OpenSubtitlesSettings
{
    /// <summary>API key from https://www.opensubtitles.com/en/consumers</summary>
    public string? ApiKey { get; set; }

    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>Required by OpenSubtitles; identify your app.</summary>
    public string UserAgent { get; set; } = "IcedSub v0.1";

    public string BaseUrl { get; set; } = "https://api.opensubtitles.com/api/v1";
    public bool Enabled { get; set; } = true;
}

public sealed class AssrtSettings
{
    /// <summary>Token from https://assrt.net user panel.</summary>
    public string? Token { get; set; }

    public string BaseUrl { get; set; } = "https://api.assrt.net";

    /// <summary>Fallback API hosts when primary is unreachable (official docs list api.makedie.me).</summary>
    public string[] AlternateBaseUrls { get; set; } =
    [
        "https://api.makedie.me",
        "http://api.assrt.net"
    ];

    public bool Enabled { get; set; } = true;
}

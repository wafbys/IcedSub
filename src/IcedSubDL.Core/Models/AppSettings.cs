namespace IcedSubDL.Core.Models;

public sealed class AppSettings
{
    public OpenSubtitlesSettings OpenSubtitles { get; set; } = new();
    public AssrtSettings Assrt { get; set; } = new();
    public SearchOptions Defaults { get; set; } = new();
    public string[] VideoExtensions { get; set; } =
    [
        ".mkv", ".mp4", ".avi", ".mov", ".m4v", ".wmv", ".ts", ".m2ts",
        ".webm", ".mpeg", ".mpg", ".ogm", ".flv"
    ];
}

public sealed class OpenSubtitlesSettings
{
    /// <summary>API key from https://www.opensubtitles.com/en/consumers</summary>
    public string? ApiKey { get; set; }

    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>Required by OpenSubtitles; identify your app.</summary>
    public string UserAgent { get; set; } = "IcedSubDL v0.1";

    public string BaseUrl { get; set; } = "https://api.opensubtitles.com/api/v1";
    public bool Enabled { get; set; } = true;
}

public sealed class AssrtSettings
{
    /// <summary>Token from https://assrt.net user panel.</summary>
    public string? Token { get; set; }

    public string BaseUrl { get; set; } = "https://api.assrt.net";
    public bool Enabled { get; set; } = true;
}

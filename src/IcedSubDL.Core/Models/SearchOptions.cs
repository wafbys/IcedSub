namespace IcedSubDL.Core.Models;

public enum DownloadMode
{
    /// <summary>Search only; do not download.</summary>
    None = 0,

    /// <summary>Download only the highest-ranked candidate.</summary>
    Top = 1,

    /// <summary>Download every ranked candidate (unique file names).</summary>
    All = 2
}

public sealed class SearchOptions
{
    /// <summary>BCP-47 / provider language codes, highest priority first. Default: zh-CN, zh-TW, en.</summary>
    public List<string> Languages { get; set; } = ["zh-CN", "zh-TW", "en"];

    /// <summary>
    /// Optional format preference for ranking only (empty = accept all formats as-is).
    /// Does not extract archives; downloads are saved raw.
    /// </summary>
    public List<string> PreferredFormats { get; set; } = [];

    public int MaxResultsPerProvider { get; set; } = 15;

    /// <summary>If true and a matching external .srt already exists beside the video, skip search.</summary>
    public bool SkipIfExternalSrtExists { get; set; }

    /// <summary>What to download after search. Default for download command: All.</summary>
    public DownloadMode DownloadMode { get; set; } = DownloadMode.All;

    /// <summary>Overwrite existing subtitle files when saving.</summary>
    public bool OverwriteExisting { get; set; }
}

namespace IcedSubDL.Metadata.Models;

/// <summary>
/// Metadata used for: 中文名_English_(year)_imdb-ttid_douban-id
/// </summary>
public sealed class BilingualTitle
{
    public required string ImdbId { get; init; }
    public int? TmdbId { get; init; }
    public required string TitleEn { get; init; }
    public string? TitleZh { get; init; }
    public string? Year { get; init; }
    public string? DirectorEn { get; init; }
    public string? DirectorZh { get; init; }
    public string? CompanyEn { get; init; }
    public string? CompanyZh { get; init; }

    /// <summary>豆瓣影视 ID（来自 Wikidata P4529；可能为空）。</summary>
    public string? DoubanId { get; init; }
}

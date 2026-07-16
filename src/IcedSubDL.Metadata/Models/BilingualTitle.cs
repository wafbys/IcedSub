namespace IcedSubDL.Metadata.Models;

/// <summary>
/// Metadata used for: English (中文)_year_DirectorEn (导演中文)_ttid
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
}

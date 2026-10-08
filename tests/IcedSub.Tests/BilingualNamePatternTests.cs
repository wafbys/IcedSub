using IcedSub.Metadata.Models;
using IcedSub.Metadata.Naming;
using IcedSub.Metadata.Providers;

namespace IcedSub.Tests;

public class BilingualNamePatternTests
{
    [Fact]
    public void Build_ExactUserExample()
    {
        var meta = new BilingualTitle
        {
            ImdbId = "tt0111161",
            TitleEn = "The Shawshank Redemption",
            TitleZh = "肖申克的救赎",
            Year = "1994",
            DoubanId = "1292052"
        };

        string name = BilingualNamePattern.Build(meta);
        Assert.Equal("肖申克的救赎_The Shawshank Redemption_(1994)_imdb-tt0111161_douban-1292052", name);
    }

    [Fact]
    public void Build_FarewellMyConcubine()
    {
        var meta = new BilingualTitle
        {
            ImdbId = "tt0106332",
            TitleEn = "Farewell My Concubine",
            TitleZh = "霸王别姬",
            Year = "1993",
            DoubanId = "1291546"
        };

        Assert.Equal(
            "霸王别姬_Farewell My Concubine_(1993)_imdb-tt0106332_douban-1291546",
            BilingualNamePattern.Build(meta));
    }

    [Fact]
    public void FormatTitle_ChineseFirst_JoinedByUnderscore()
    {
        Assert.Equal("乙_A", BilingualNamePattern.FormatTitle("乙", "A"));
        Assert.Equal("A", BilingualNamePattern.FormatTitle(null, "A"));
        Assert.Equal("乙", BilingualNamePattern.FormatTitle("乙", null));
        Assert.Equal("Same", BilingualNamePattern.FormatTitle("Same", "Same"));
    }

    [Fact]
    public void Sanitize_ConvertsFullWidthParens()
    {
        Assert.Equal("A (B)", BilingualNamePattern.SanitizeSegment("A（B）"));
    }

    [Fact]
    public void NormalizeImdbId()
    {
        Assert.Equal("tt33311069", TmdbMetadataClient.NormalizeImdbId("tt33311069"));
        Assert.Equal("tt33311069", TmdbMetadataClient.NormalizeImdbId("33311069"));
        Assert.Equal("tt33311069", TmdbMetadataClient.NormalizeImdbId("https://www.imdb.com/title/tt33311069/"));
    }

    [Fact]
    public void Build_OmitsMissingDoubanAndDirector()
    {
        var meta = new BilingualTitle
        {
            ImdbId = "tt1",
            TitleEn = "Foo",
            TitleZh = "佛",
            Year = "2020"
        };
        Assert.Equal("佛_Foo_(2020)_imdb-tt1", BilingualNamePattern.Build(meta));
    }

    [Fact]
    public void Build_NoChinese_FallsBackToEnglishOnly()
    {
        var meta = new BilingualTitle
        {
            ImdbId = "tt9",
            TitleEn = "Foo",
            Year = "2020",
            DoubanId = "123"
        };
        Assert.Equal("Foo_(2020)_imdb-tt9_douban-123", BilingualNamePattern.Build(meta));
    }
}

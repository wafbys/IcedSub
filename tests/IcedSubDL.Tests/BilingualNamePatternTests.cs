using IcedSubDL.Metadata.Models;
using IcedSubDL.Metadata.Naming;
using IcedSubDL.Metadata.Providers;

namespace IcedSubDL.Tests;

public class BilingualNamePatternTests
{
    [Fact]
    public void Build_ExactUserExample()
    {
        var meta = new BilingualTitle
        {
            ImdbId = "tt33311069",
            TitleEn = "The Furious",
            TitleZh = "火遮眼",
            Year = "2025",
            DirectorEn = "Kenji Tanigaki",
            DirectorZh = "谷垣健治"
        };

        string name = BilingualNamePattern.Build(meta);
        Assert.Equal("The Furious (火遮眼)_2025_Kenji Tanigaki (谷垣健治)_tt33311069", name);
    }

    [Fact]
    public void FormatBilingual_UsesHalfWidthParens()
    {
        Assert.Equal("A (乙)", BilingualNamePattern.FormatBilingual("A", "乙"));
        Assert.Equal("A", BilingualNamePattern.FormatBilingual("A", null));
        Assert.Equal("乙", BilingualNamePattern.FormatBilingual(null, "乙"));
        Assert.Equal("Same", BilingualNamePattern.FormatBilingual("Same", "Same"));
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
    public void Build_OmitsMissingDirector()
    {
        var meta = new BilingualTitle
        {
            ImdbId = "tt1",
            TitleEn = "Foo",
            TitleZh = "佛",
            Year = "2020"
        };
        Assert.Equal("Foo (佛)_2020_tt1", BilingualNamePattern.Build(meta));
    }
}

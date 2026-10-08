using System.Text.Json;
using IcedSub.Metadata.Naming;
using IcedSub.Metadata.Providers;

namespace IcedSub.Tests;

public class WikidataTitleParseTests
{
    [Fact]
    public void Parse_FullFields_BuildsExpectedName()
    {
        using var doc = JsonDocument.Parse("""
        {
          "results": {
            "bindings": [
              {
                "item": { "type": "uri", "value": "http://www.wikidata.org/entity/Q172241" },
                "date": { "type": "literal", "value": "1994-09-10T00:00:00Z" },
                "imdb": { "type": "literal", "value": "tt0111161" },
                "douban": { "type": "literal", "value": "1292052" },
                "zhLabel": { "type": "literal", "value": "肖申克的救赎", "xml:lang": "zh" },
                "enLabel": { "type": "literal", "value": "The Shawshank Redemption", "xml:lang": "en" }
              }
            ]
          }
        }
        """);

        var meta = WikidataDoubanResolver.ParseTitleBindings(doc);

        Assert.NotNull(meta);
        Assert.Equal("tt0111161", meta!.ImdbId);
        Assert.Equal("肖申克的救赎", meta.TitleZh);
        Assert.Equal("The Shawshank Redemption", meta.TitleEn);
        Assert.Equal("1994", meta.Year);
        Assert.Equal("1292052", meta.DoubanId);
        Assert.Null(meta.TmdbId);
        Assert.Equal(
            "肖申克的救赎_The Shawshank Redemption_(1994)_imdb-tt0111161_douban-1292052",
            BilingualNamePattern.Build(meta));
    }

    [Fact]
    public void Parse_PrefersZhOverRegionalVariants()
    {
        using var doc = JsonDocument.Parse("""
        {
          "results": {
            "bindings": [
              {
                "zhLabel": { "type": "literal", "value": "繁體名", "xml:lang": "zh-hant" },
                "enLabel": { "type": "literal", "value": "Title", "xml:lang": "en" }
              },
              {
                "zhLabel": { "type": "literal", "value": "简体名", "xml:lang": "zh" },
                "enLabel": { "type": "literal", "value": "Title", "xml:lang": "en" }
              }
            ]
          }
        }
        """);

        var meta = WikidataDoubanResolver.ParseTitleBindings(doc);

        Assert.NotNull(meta);
        Assert.Equal("简体名", meta!.TitleZh);
    }

    [Fact]
    public void Parse_PicksEarliestYear()
    {
        using var doc = JsonDocument.Parse("""
        {
          "results": {
            "bindings": [
              { "date": { "type": "literal", "value": "1995-03-09T00:00:00Z" }, "enLabel": { "type": "literal", "value": "Film", "xml:lang": "en" } },
              { "date": { "type": "literal", "value": "1994-09-10T00:00:00Z" }, "enLabel": { "type": "literal", "value": "Film", "xml:lang": "en" } }
            ]
          }
        }
        """);

        var meta = WikidataDoubanResolver.ParseTitleBindings(doc);

        Assert.NotNull(meta);
        Assert.Equal("1994", meta!.Year);
    }

    [Fact]
    public void Parse_EnglishOnly_DropsChinese()
    {
        using var doc = JsonDocument.Parse("""
        {
          "results": {
            "bindings": [
              { "imdb": { "type": "literal", "value": "tt9" }, "enLabel": { "type": "literal", "value": "Foo", "xml:lang": "en" } }
            ]
          }
        }
        """);

        var meta = WikidataDoubanResolver.ParseTitleBindings(doc);

        Assert.NotNull(meta);
        Assert.Null(meta!.TitleZh);
        Assert.Equal("Foo", meta.TitleEn);
        Assert.Equal("Foo_imdb-tt9", BilingualNamePattern.Build(meta));
    }

    [Fact]
    public void Parse_NoLabels_ReturnsNull()
    {
        using var doc = JsonDocument.Parse("""
        {
          "results": {
            "bindings": [
              { "item": { "type": "uri", "value": "http://www.wikidata.org/entity/Q1" } }
            ]
          }
        }
        """);

        Assert.Null(WikidataDoubanResolver.ParseTitleBindings(doc));
    }

    [Fact]
    public void Parse_EmptyBindings_ReturnsNull()
    {
        using var doc = JsonDocument.Parse("""{ "results": { "bindings": [] } }""");
        Assert.Null(WikidataDoubanResolver.ParseTitleBindings(doc));
    }

    [Fact]
    public void Parse_UsesFallbackIdsWhenBindingsLackThem()
    {
        using var doc = JsonDocument.Parse("""
        {
          "results": {
            "bindings": [
              { "enLabel": { "type": "literal", "value": "Foo", "xml:lang": "en" } }
            ]
          }
        }
        """);

        var meta = WikidataDoubanResolver.ParseTitleBindings(doc, "tt123", "456");

        Assert.NotNull(meta);
        Assert.Equal("tt123", meta!.ImdbId);
        Assert.Equal("456", meta.DoubanId);
        Assert.Equal("Foo_imdb-tt123_douban-456", BilingualNamePattern.Build(meta));
    }
}

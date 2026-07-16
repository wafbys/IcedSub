using IcedSubDL.Core.Media;

namespace IcedSubDL.Tests;

public class VideoScannerParseTests
{
    [Theory]
    [InlineData("The.Matrix.1999.1080p.BluRay.x264-GROUP", "The Matrix", 1999, null, null)]
    [InlineData("Show.Name.S02E05.720p.WEB-DL", "Show Name", null, 2, 5)]
    [InlineData("Movie.2020", "Movie", 2020, null, null)]
    public void ParseFileName_ExtractsFields(
        string name,
        string expectedTitle,
        int? year,
        int? season,
        int? episode)
    {
        var (title, y, s, e) = VideoScanner.ParseFileName(name);
        Assert.Equal(expectedTitle, title);
        Assert.Equal(year, y);
        Assert.Equal(season, s);
        Assert.Equal(episode, e);
    }
}

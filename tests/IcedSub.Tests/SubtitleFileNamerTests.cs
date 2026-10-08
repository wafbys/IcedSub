using IcedSub.Core.Media;
using IcedSub.Core.Models;

namespace IcedSub.Tests;

public class SubtitleFileNamerTests
{
    [Theory]
    [InlineData("zh", "zh-CN")]
    [InlineData("chs", "zh-CN")]
    [InlineData("zh-tw", "zh-TW")]
    [InlineData("eng", "en")]
    public void NormalizeLanguageTag(string input, string expected)
    {
        Assert.Equal(expected, SubtitleFileNamer.NormalizeLanguageTag(input));
    }

    [Fact]
    public void GetOutputPath_KeepsOriginalFileName()
    {
        var media = new MediaInfo
        {
            FilePath = @"D:\Videos\Movie.2020.mkv",
            FileName = "Movie.2020.mkv",
            FileNameWithoutExtension = "Movie.2020",
            FileSizeBytes = 1,
            OutputDirectory = @"D:\Videos"
        };
        var candidate = new SubtitleCandidate
        {
            ProviderName = "ASSRT",
            ProviderId = "1",
            DisplayName = "x",
            Language = "zh-CN",
            Format = "srt",
            DownloadKey = "1"
        };
        var file = new SubtitleFile
        {
            Candidate = candidate,
            SuggestedFileName = "火遮眼 中文完美版.srt",
            Content = [1, 2, 3],
            Format = "srt"
        };

        string path = SubtitleFileNamer.GetOutputPath(media, candidate, downloaded: file, overwriteExisting: true);
        Assert.Equal(Path.Combine(@"D:\Videos", "火遮眼 中文完美版.srt"), path);
    }

    [Fact]
    public void GetOutputPath_LegacyGenerated_StillWorks()
    {
        var media = new MediaInfo
        {
            FilePath = @"D:\Videos\Movie.2020.mkv",
            FileName = "Movie.2020.mkv",
            FileNameWithoutExtension = "Movie.2020",
            FileSizeBytes = 1
        };
        var candidate = new SubtitleCandidate
        {
            ProviderName = "ASSRT",
            ProviderId = "1",
            DisplayName = "x",
            Language = "zh-CN",
            Format = "srt",
            DownloadKey = "1"
        };

        string path = SubtitleFileNamer.GetOutputPath(media, candidate, format: "srt");
        Assert.Equal(@"D:\Videos\Movie.2020.zh-CN.srt", path);
    }

    [Fact]
    public void SanitizeFileName_ReplacesInvalidCharsOnly()
    {
        string name = SubtitleFileNamer.SanitizeFileName("a:b/c?.srt");
        Assert.DoesNotContain(":", name);
        Assert.DoesNotContain("/", name);
        Assert.DoesNotContain("?", name);
        Assert.EndsWith(".srt", name);
    }
}

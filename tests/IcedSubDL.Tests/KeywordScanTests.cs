using IcedSubDL.Core.Media;
using IcedSubDL.Core.Models;
using IcedSubDL.Providers.Assrt;

namespace IcedSubDL.Tests;

public class KeywordScanTests
{
    [Fact]
    public async Task ScanAsync_NonexistentPath_IsKeywordQuery()
    {
        var settings = new AppSettings();
        var scanner = new VideoScanner(settings);

        var list = await scanner.ScanAsync(["火遮眼"], computeHash: true);
        Assert.Single(list);
        Assert.True(list[0].IsKeywordQuery);
        Assert.Equal("火遮眼", list[0].ParsedTitle);
        Assert.Null(list[0].OpenSubtitlesMovieHash);
    }

    [Fact]
    public void CreateFromKeyword_SetsOutputDirectory()
    {
        var media = VideoScanner.CreateFromKeyword("火遮眼", @"D:\Subs");
        Assert.True(media.IsKeywordQuery);
        Assert.Equal(@"D:\Subs", media.OutputDirectory);
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
            SuggestedFileName = "火遮眼.The.Furious.2025.srt",
            Content = [1],
            Format = "srt"
        };
        Assert.Equal(
            Path.Combine(@"D:\Subs", "火遮眼.The.Furious.2025.srt"),
            SubtitleFileNamer.GetOutputPath(media, candidate, downloaded: file, overwriteExisting: true));
    }

    [Fact]
    public void BuildSearchQueries_Keyword_OnlyFreeText()
    {
        var media = VideoScanner.CreateFromKeyword("火遮眼");
        var queries = AssrtProvider.BuildSearchQueries(media).ToList();
        Assert.All(queries, q => Assert.False(q.IsFile));
        Assert.Contains(queries, q => q.Query == "火遮眼");
    }
}

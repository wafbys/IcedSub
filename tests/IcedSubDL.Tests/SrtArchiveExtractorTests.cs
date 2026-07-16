using System.IO.Compression;
using System.Text;
using IcedSubDL.Core.Media;

namespace IcedSubDL.Tests;

public class SrtArchiveExtractorTests
{
    [Fact]
    public void TryExtractSrt_PlainSrt_Works()
    {
        string srt = "1\n00:00:01,000 --> 00:00:02,000\nHello\n";
        byte[] bytes = Encoding.UTF8.GetBytes(srt);
        var result = SrtArchiveExtractor.TryExtractSrt(bytes, "a.srt");
        Assert.NotNull(result);
        Assert.Equal("a.srt", result.Value.FileName);
        Assert.Contains("Hello", Encoding.UTF8.GetString(result.Value.Content));
    }

    [Fact]
    public void TryExtractSrt_FromZip_PicksSrt()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("folder/sub.srt");
            using var w = new StreamWriter(entry.Open());
            w.Write("1\n00:00:01,000 --> 00:00:02,000\nZip\n");
        }

        byte[] payload = ms.ToArray();
        var result = SrtArchiveExtractor.TryExtractSrt(payload);
        Assert.NotNull(result);
        Assert.Equal("sub.srt", result.Value.FileName);
    }

    [Fact]
    public void IsPreferredFormat_Srt()
    {
        Assert.True(SrtArchiveExtractor.IsPreferredFormat("srt", ["srt"]));
        Assert.True(SrtArchiveExtractor.IsPreferredFormat("Subrip(srt)", ["srt"]));
        Assert.False(SrtArchiveExtractor.IsPreferredFormat("ass", ["srt"]));
    }
}

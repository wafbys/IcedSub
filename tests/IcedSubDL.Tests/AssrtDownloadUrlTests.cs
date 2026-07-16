using IcedSubDL.Providers.Assrt;

namespace IcedSubDL.Tests;

public class AssrtDownloadUrlTests
{
    [Theory]
    [InlineData("file0.assrt.net", true)]
    [InlineData("file1.assrt.net", true)]
    [InlineData("glb.assrt.net", true)]
    [InlineData("api.assrt.net", false)]
    [InlineData("assrt.net", false)]
    public void IsAssrtFileHost(string host, bool expected)
    {
        Assert.Equal(expected, AssrtProvider.IsAssrtFileHost(host));
    }

    [Fact]
    public void BuildDownloadUrlCandidates_UpgradesHttpsAndSwapsFileHosts()
    {
        const string original =
            "http://file1.assrt.net/download/123/a.srt?_=1&-=abc&api=1";

        var list = AssrtProvider.BuildDownloadUrlCandidates(original).ToList();

        Assert.Contains(list, u => u.StartsWith("https://file1.assrt.net/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(list, u => u.Contains("file0.assrt.net", StringComparison.OrdinalIgnoreCase));
        Assert.All(list, u => Assert.Contains("/download/123/a.srt", u, StringComparison.OrdinalIgnoreCase));
    }
}

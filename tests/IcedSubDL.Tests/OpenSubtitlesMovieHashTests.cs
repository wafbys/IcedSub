using System.Text;
using IcedSubDL.Core.Hashing;

namespace IcedSubDL.Tests;

public class OpenSubtitlesMovieHashTests
{
    [Fact]
    public void Compute_EmptyFile_IsZero()
    {
        string hash = OpenSubtitlesMovieHash.Compute([]);
        Assert.Equal("0000000000000000", hash);
    }

    [Fact]
    public void Compute_KnownPattern_IsStable()
    {
        // 16 bytes of 0x01 — deterministic hash
        byte[] data = Enumerable.Repeat((byte)1, 16).ToArray();
        string hash = OpenSubtitlesMovieHash.Compute(data);
        Assert.Equal(16, hash.Length);
        Assert.Matches("^[0-9a-f]{16}$", hash);

        // Same input => same hash
        Assert.Equal(hash, OpenSubtitlesMovieHash.Compute(data));
    }

    [Fact]
    public async Task ComputeAsync_File_MatchesCompute()
    {
        byte[] data = Encoding.UTF8.GetBytes("IcedSubDL-hash-test-" + new string('x', 2000));
        string path = Path.Combine(Path.GetTempPath(), "icedsubdl-hash-" + Guid.NewGuid().ToString("n") + ".bin");
        try
        {
            await File.WriteAllBytesAsync(path, data);
            string fromFile = await OpenSubtitlesMovieHash.ComputeAsync(path);
            string fromBytes = OpenSubtitlesMovieHash.Compute(data);
            Assert.Equal(fromBytes, fromFile);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}

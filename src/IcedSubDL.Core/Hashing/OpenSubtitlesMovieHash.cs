namespace IcedSubDL.Core.Hashing;

/// <summary>
/// OpenSubtitles movie hash (64-bit): file size + sum of first/last 64 KiB as little-endian uint64 words.
/// </summary>
public static class OpenSubtitlesMovieHash
{
    private const int ChunkSize = 64 * 1024;

    public static async Task<string> ComputeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: ChunkSize,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        return await ComputeAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<string> ComputeAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek)
            throw new ArgumentException("Stream must be seekable.", nameof(stream));

        long length = stream.Length;
        ulong hash = (ulong)length;
        byte[] buffer = new byte[ChunkSize];

        stream.Seek(0, SeekOrigin.Begin);
        int read = await ReadExactOrLessAsync(stream, buffer, cancellationToken).ConfigureAwait(false);
        hash += SumUInt64Words(buffer.AsSpan(0, read));

        if (length > ChunkSize)
        {
            stream.Seek(Math.Max(0, length - ChunkSize), SeekOrigin.Begin);
            read = await ReadExactOrLessAsync(stream, buffer, cancellationToken).ConfigureAwait(false);
            hash += SumUInt64Words(buffer.AsSpan(0, read));
        }

        return hash.ToString("x16");
    }

    public static string Compute(byte[] fileBytes)
    {
        using var ms = new MemoryStream(fileBytes, writable: false);
        return ComputeAsync(ms).GetAwaiter().GetResult();
    }

    private static ulong SumUInt64Words(ReadOnlySpan<byte> data)
    {
        ulong sum = 0;
        int full = data.Length / 8 * 8;
        for (int i = 0; i < full; i += 8)
            sum += BitConverter.ToUInt64(data.Slice(i, 8));

        if (full < data.Length)
        {
            Span<byte> tail = stackalloc byte[8];
            tail.Clear();
            data[full..].CopyTo(tail);
            sum += BitConverter.ToUInt64(tail);
        }

        return sum;
    }

    private static async Task<int> ReadExactOrLessAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct).ConfigureAwait(false);
            if (n == 0)
                break;
            total += n;
        }

        return total;
    }
}

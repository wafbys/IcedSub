using System.IO.Compression;
using System.Text;

namespace IcedSubDL.Core.Media;

/// <summary>
/// Picks an .srt entry from raw bytes, zip archives, or multi-file packages.
/// </summary>
public static class SrtArchiveExtractor
{
    public static (byte[] Content, string FileName)? TryExtractSrt(byte[] payload, string? hintFileName = null)
    {
        if (payload.Length == 0)
            return null;

        // Direct SRT (UTF-8/BOM text)
        if (LooksLikeSrt(payload) ||
            (hintFileName is not null && hintFileName.EndsWith(".srt", StringComparison.OrdinalIgnoreCase)))
        {
            string name = hintFileName is not null && hintFileName.EndsWith(".srt", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileName(hintFileName)
                : "subtitle.srt";
            return (payload, name);
        }

        // ZIP
        if (IsZip(payload))
        {
            using var ms = new MemoryStream(payload);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            var srtEntries = zip.Entries
                .Where(e => e.FullName.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) && e.Length > 0)
                .OrderByDescending(e => e.Length)
                .ToList();

            if (srtEntries.Count == 0)
                return null;

            ZipArchiveEntry best = srtEntries[0];
            using var entryStream = best.Open();
            using var outMs = new MemoryStream();
            entryStream.CopyTo(outMs);
            return (outMs.ToArray(), Path.GetFileName(best.FullName));
        }

        return null;
    }

    public static bool IsPreferredFormat(string? formatOrFileName, IEnumerable<string> preferredFormats)
    {
        var preferred = preferredFormats as ICollection<string> ?? preferredFormats.ToList();
        if (preferred.Count == 0)
            return true;

        string token = (formatOrFileName ?? string.Empty).Trim().ToLowerInvariant();
        foreach (string pref in preferred)
        {
            string p = pref.Trim().TrimStart('.').ToLowerInvariant();
            if (token == p || token.EndsWith("." + p, StringComparison.Ordinal) ||
                token.Contains(p, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool IsZip(byte[] payload) =>
        payload.Length >= 4 && payload[0] == (byte)'P' && payload[1] == (byte)'K';

    private static bool LooksLikeSrt(byte[] payload)
    {
        // Skip BOM
        int offset = 0;
        if (payload.Length >= 3 && payload[0] == 0xEF && payload[1] == 0xBB && payload[2] == 0xBF)
            offset = 3;

        int len = Math.Min(payload.Length - offset, 512);
        if (len <= 0)
            return false;

        string head = Encoding.UTF8.GetString(payload, offset, len);
        // Very loose: contains a timestamp line
        return head.Contains("-->", StringComparison.Ordinal);
    }
}

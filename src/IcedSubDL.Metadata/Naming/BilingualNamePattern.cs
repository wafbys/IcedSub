using System.Text;
using System.Text.RegularExpressions;
using IcedSubDL.Metadata.Models;

namespace IcedSubDL.Metadata.Naming;

/// <summary>
/// 命名模式（半角，下划线分段）：
/// 肖申克的救赎_The Shawshank Redemption_(1994)_imdb-tt0111161_douban-1292052
/// 即：中文名_English_(年份)_imdb-id_douban-id
/// </summary>
public static partial class BilingualNamePattern
{
    public static string Build(BilingualTitle meta)
    {
        ArgumentNullException.ThrowIfNull(meta);

        var parts = new List<string>();

        string title = FormatTitle(meta.TitleZh, meta.TitleEn);
        if (!string.IsNullOrEmpty(title))
            parts.Add(title);

        string year = SanitizeSegment(meta.Year);
        if (!string.IsNullOrEmpty(year))
            parts.Add($"({year})");

        string imdb = SanitizeSegment(meta.ImdbId);
        if (!string.IsNullOrEmpty(imdb))
            parts.Add($"imdb-{imdb}");

        string douban = SanitizeSegment(meta.DoubanId);
        if (!string.IsNullOrEmpty(douban))
            parts.Add($"douban-{douban}");

        return string.Join('_', parts);
    }

    /// <summary>
    /// 中文_English — 无中文时仅英文；无英文时仅中文；两者相同则不重复。
    /// 分段内不允许下划线（下划线留给段分隔），全角括号转半角。
    /// </summary>
    public static string FormatTitle(string? chinese, string? english)
    {
        string zh = SanitizeSegment(chinese);
        string en = SanitizeSegment(english);

        if (string.IsNullOrEmpty(zh) && string.IsNullOrEmpty(en))
            return string.Empty;
        if (string.IsNullOrEmpty(zh))
            return en;
        if (string.IsNullOrEmpty(en))
            return zh;
        if (string.Equals(zh, en, StringComparison.OrdinalIgnoreCase))
            return zh;

        return $"{zh}_{en}";
    }

    public static string SanitizeSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        foreach (char c in value.Trim())
        {
            // 全角括号 -> 半角；左括号前补空格以贴近 "En (中)" 风格
            if (c is '（')
            {
                if (sb.Length > 0 && sb[^1] is not ' ' and not '(')
                    sb.Append(' ');
                sb.Append('(');
                continue;
            }

            if (c is '）')
            {
                sb.Append(')');
                continue;
            }

            // 路径非法字符
            if (c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' or '\0')
            {
                sb.Append(' ');
                continue;
            }

            if (c is '\r' or '\n' or '\t')
            {
                sb.Append(' ');
                continue;
            }

            // 字段内不要下划线（下划线留给段分隔）
            if (c == '_')
            {
                sb.Append(' ');
                continue;
            }

            sb.Append(c);
        }

        string s = MultiSpace().Replace(sb.ToString(), " ").Trim(' ', '.');
        return s;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpace();
}

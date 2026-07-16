using System.Text;
using System.Text.RegularExpressions;
using IcedSubDL.Metadata.Models;

namespace IcedSubDL.Metadata.Naming;

/// <summary>
/// 命名模式（半角括号）：
/// The Furious (火遮眼)_2025_Kenji Tanigaki (谷垣健治)_tt33311069
/// 即：英文 (中文)_年份_导演英文 (导演中文)_imdbId
/// </summary>
public static partial class BilingualNamePattern
{
    public static string Build(BilingualTitle meta)
    {
        ArgumentNullException.ThrowIfNull(meta);

        var parts = new List<string>();

        string title = FormatBilingual(meta.TitleEn, meta.TitleZh);
        if (!string.IsNullOrEmpty(title))
            parts.Add(title);

        string year = SanitizeSegment(meta.Year);
        if (!string.IsNullOrEmpty(year))
            parts.Add(year);

        string director = FormatBilingual(meta.DirectorEn, meta.DirectorZh);
        if (!string.IsNullOrEmpty(director))
            parts.Add(director);

        string id = SanitizeSegment(meta.ImdbId);
        if (!string.IsNullOrEmpty(id))
            parts.Add(id);

        return string.Join('_', parts);
    }

    /// <summary>
    /// English (中文) — 无中文时仅英文；中英文相同则不重复括号。
    /// 括号一律半角 ()。
    /// </summary>
    public static string FormatBilingual(string? english, string? chinese)
    {
        string en = SanitizeSegment(english);
        string zh = SanitizeSegment(chinese);

        if (string.IsNullOrEmpty(en) && string.IsNullOrEmpty(zh))
            return string.Empty;
        if (string.IsNullOrEmpty(en))
            return zh;
        if (string.IsNullOrEmpty(zh))
            return en;
        if (string.Equals(en, zh, StringComparison.OrdinalIgnoreCase))
            return en;

        return $"{en} ({zh})";
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

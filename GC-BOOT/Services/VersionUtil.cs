using System;
using System.Globalization;

namespace GC.Services;

/// <summary>
/// 版本号工具：Release tag 有 v1.0.6 / GC-1.0.6 / 1.0.6 等写法，
/// 统一成可比较的 Version 再排序，挑**版本号最高**的那个。
/// </summary>
public static class VersionUtil
{
    /// <summary>把 tag 规整成 "1.0.6" 这样的纯数字版本。</summary>
    public static string Normalize(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return "";
        var s = tag!.Trim();

        // 去掉前缀：v / V / GC- / release-
        if (s.StartsWith("GC-", StringComparison.OrdinalIgnoreCase)) s = s[3..];
        else if (s.StartsWith("release-", StringComparison.OrdinalIgnoreCase)) s = s[8..];
        s = s.TrimStart('v', 'V');

        // 只保留开头的 数字与点
        var end = 0;
        while (end < s.Length && (char.IsDigit(s[end]) || s[end] == '.')) end++;
        s = s[..end].Trim('.');

        return s;
    }

    /// <summary>解析成 Version；无法解析时返回 0.0。</summary>
    public static Version Parse(string? tag)
    {
        var n = Normalize(tag);
        if (string.IsNullOrWhiteSpace(n)) return new Version(0, 0);
        return Version.TryParse(n, out var v) ? v : new Version(0, 0);
    }

    /// <summary>比较两个版本号，a>b 返回正、a&lt;b 返回负、相等返回 0。</summary>
    public static int Compare(string? a, string? b) => Parse(a).CompareTo(Parse(b));
}

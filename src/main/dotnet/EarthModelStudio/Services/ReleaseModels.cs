using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EarthModelStudio.Services;

/// <summary>
/// GitCode/Gitee v5 releases API 的一条 Release（字段做宽松解析，兼容多种返回格式）。
/// </summary>
public sealed class ReleaseInfo
{
    public string TagName { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Draft { get; set; }
    public bool Prerelease { get; set; }
    public string? PublishedAt { get; set; }
    public List<ReleaseAsset> Assets { get; set; } = new();

    /// <summary>从 tag_name / name 里抽出的版本号（去掉前缀 v、GC- 等）。</summary>
    public string Version => NormalizeVersion(TagName, Name);

    public static string NormalizeVersion(string tag, string name)
    {
        var raw = !string.IsNullOrWhiteSpace(tag) ? tag : name;
        if (string.IsNullOrWhiteSpace(raw)) return "";
        raw = raw!.Trim();
        // "v1.0.1" / "GC-1.0.1" / "release-1.0.1" -> 取第一段像版本号的部分
        foreach (var prefix in new[] { "v", "V", "GC-", "gc-", "release-", "Release-" })
            if (raw.StartsWith(prefix, StringComparison.Ordinal)) { raw = raw[prefix.Length..]; break; }
        // 形如 GC-1.0.1-Win-amd64 时只取版本段
        var dash = raw.IndexOf('-');
        if (dash > 0 && System.Version.TryParse(raw[..dash], out _)) raw = raw[..dash];
        return raw;
    }
}

public sealed class ReleaseAsset
{
    public string Name { get; set; } = "";
    public string? BrowserDownloadUrl { get; set; }
    public string? DownloadUrl { get; set; }
    public long Size { get; set; }
    public string? Sha256 { get; set; }

    [JsonIgnore]
    public string Url => BrowserDownloadUrl ?? DownloadUrl ?? "";

    /// <summary>解析附件名里的系统/架构，映射到 .NET RID。</summary>
    /// <returns>如 win-x64 / win-arm64 / osx-arm64；无法识别返回 null。</returns>
    public string? RidFromName(string fullName)
    {
        var n = fullName ?? "";
        // 统一大小写与分隔符后再判断，兼容 "GC-1.0.1-Win-amd64.zip"
        var lower = n.ToLowerInvariant().Replace('_', '-');

        bool win = lower.Contains("win") || lower.Contains("windows");
        bool mac = lower.Contains("osx") || lower.Contains("mac") || lower.Contains("darwin");
        bool linux = lower.Contains("linux");
        if (!win && !mac && !linux) return null;

        bool arm64 = lower.Contains("arm64") || lower.Contains("aarch64") || lower.Contains("win-arm");
        bool x86 = lower.Contains("x86") && !lower.Contains("x64") && !lower.Contains("amd64") && !arm64;
        // amd64 / x64 / x86_64 都视为 x64
        var arch = arm64 ? "arm64" : x86 ? "x86" : "x64";

        if (win) return "win-" + arch;
        if (mac) return "osx-" + arch;
        return "linux-" + arch;
    }
}

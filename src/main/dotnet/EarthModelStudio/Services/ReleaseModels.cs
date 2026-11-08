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

    private static readonly string[] VersionPrefixes =
        { "v", "V", "GC-", "gc-", "release-", "Release-" };

    private static readonly string[] PrereleaseTags =
        { "alpha", "beta", "rc", "pre", "dev", "snapshot" };

    public static string NormalizeVersion(string tag, string name)
    {
        var raw = !string.IsNullOrWhiteSpace(tag) ? tag : name;
        if (string.IsNullOrWhiteSpace(raw)) return "";
        raw = raw!.Trim();
        // "v1.0.1" / "GC-1.0.1" / "release-1.0.1" -> 取第一段像版本号的部分
        foreach (var prefix in VersionPrefixes)
            if (raw.StartsWith(prefix, StringComparison.Ordinal)) { raw = raw[prefix.Length..]; break; }
        // 形如 GC-1.0.1-Win-amd64 时只取版本段；但 2.3.0-beta 这类预发布串要保留完整。
        // 用「后缀是否常见预发布标签」区分，避免把 "-Win" 误当成预发布。
        var dash = raw.IndexOf('-');
        if (dash > 0 && System.Version.TryParse(raw[..dash], out _))
        {
            var tail = raw[(dash + 1)..].ToLowerInvariant();
            var isPrerelease = Array.Exists(PrereleaseTags, p => tail.StartsWith(p, StringComparison.Ordinal));
            if (!isPrerelease) raw = raw[..dash];
        }
        return raw;
    }
}

public sealed class ReleaseAsset
{
    public string Name { get; set; } = "";
    public string? BrowserDownloadUrl { get; set; }
    public string? DownloadUrl { get; set; }
    public long Size { get; set; }
    /// <summary>附件类型：GitCode 用 "source" 表示自动生成的源码压缩包，需跳过。</summary>
    public string? Type { get; set; }

    private string? _sha256;
    /// <summary>
    /// 校验值。真实 Release 的 digest 形如 "sha256:899fc1..."，
    /// 这里统一剥掉算法前缀，只留 hex，便于与 SHA256.HashData 结果比较。
    /// </summary>
    public string? Sha256
    {
        get => _sha256;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) { _sha256 = null; return; }
            var v = value!.Trim();
            var colon = v.IndexOf(':');
            if (colon > 0 && v[..colon].Equals("sha256", StringComparison.OrdinalIgnoreCase))
                v = v[(colon + 1)..];
            _sha256 = v.Trim();
        }
    }

    [JsonIgnore]
    public string Url => BrowserDownloadUrl ?? DownloadUrl ?? "";

    /// <summary>单文件发布包（.exe 等）不能走解压，需要直接作为 payload 内容。</summary>
    public bool IsSingleFile
    {
        get
        {
            var n = Name.ToLowerInvariant();
            // macOS 的 .app 发布包其实是 zip（内含 .app 目录），需要解压，不算单文件
            if (n.EndsWith(".app", StringComparison.Ordinal)) return false;
            return !n.EndsWith(".zip", StringComparison.Ordinal) &&
                   !n.EndsWith(".tar.gz", StringComparison.Ordinal) &&
                   !n.EndsWith(".tgz", StringComparison.Ordinal);
        }
    }

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
        // x86_64 / x86-64 / amd64 / x64 都是 64 位；只有裸 "x86"/"win-x86" 才是 32 位
        bool is64 = lower.Contains("x64") || lower.Contains("amd64") ||
                    lower.Contains("x86-64") || lower.Contains("x86_64");
        bool x86 = !is64 && !arm64 &&
                   (lower.Contains("x86") || lower.Contains("win32") || lower.Contains("i386"));
        // 其余（未显式标注 32 位）一律按 64 位处理，覆盖 "GC-1.0.1-Windows.zip" 这类简写
        var arch = arm64 ? "arm64" : x86 ? "x86" : "x64";

        if (win) return "win-" + arch;
        if (mac) return "osx-" + arch;
        return "linux-" + arch;
    }
}

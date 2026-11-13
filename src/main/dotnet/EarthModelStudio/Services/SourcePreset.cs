using System;
using System.Collections.Generic;
using System.Linq;

namespace EarthModelStudio.Services;

/// <summary>
/// 内置下载源。只允许这两个，不再支持用户自定义源。
///
/// 两个源的内容是同一份仓库（同一个 version.json、同一批模型），
/// 区别只是**托管在哪台服务器**上；启动时探测两者延迟，取快的那个下载。
/// </summary>
public sealed class SourcePreset
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>仓库主页（用于展示与打开）。</summary>
    public string HomeUrl { get; init; } = "";
    /// <summary>API 根地址（Gitea: /api/v1，GitCode: /api/v5）。</summary>
    public string ApiBase { get; init; } = "";
    /// <summary>原始文件下载前缀（用于拼 raw 地址）。</summary>
    public string RawBase { get; init; } = "";
    /// <summary>后端类型：gitea / gitcode。</summary>
    public string Kind { get; init; } = "";
    public string Owner { get; init; } = "";
    public string Repo { get; init; } = "";
    public string Branch { get; init; } = "main";
    /// <summary>raw 地址风格：gitea 用 raw/branch/{b}，gitcode 用 raw/{b}。</summary>
    public string RawStyle { get; init; } = "branch";

    public string Repository => HomeUrl;

    /// <summary>拼 raw 文件下载地址（两个平台的 URL 结构不同）。</summary>
    public string RawUrl(string path) => RawStyle == "plain"
        ? $"{RawBase}/{Owner}/{Repo}/raw/{Branch}/{path.TrimStart('/')}"
        : $"{RawBase}/{Owner}/{Repo}/raw/branch/{Branch}/{path.TrimStart('/')}";

    public static readonly SourcePreset Xhmcl = new()
    {
        Id = "xhmcl",
        Name = "Xhmcl",
        HomeUrl = "https://git.xhmcl.com/Morris/GC",
        ApiBase = "https://git.xhmcl.com/api/v1",
        RawBase = "https://git.xhmcl.com",
        Kind = "gitea",
        RawStyle = "branch",
        Owner = "Morris",
        Repo = "GC",
        Branch = "main"
    };

    public static readonly SourcePreset GitCode = new()
    {
        Id = "gitcode",
        Name = "GitCode",
        HomeUrl = "https://gitcode.com/2509_92447106/GC",
        ApiBase = "https://api.gitcode.com/api/v5",
        RawBase = "https://gitcode.com",
        Kind = "gitcode",
        RawStyle = "plain",
        Owner = "2509_92447106",
        Repo = "GC",
        Branch = "main"
    };

    public static readonly SourcePreset[] All = { Xhmcl, GitCode };

    public static SourcePreset? Find(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>把任意仓库地址映射到这两个内置源之一（用于兼容旧配置）。</summary>
    public static SourcePreset Detect(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return GitCode;
        var u = url!.ToLowerInvariant();
        if (u.Contains("xhmcl.com")) return Xhmcl;
        return GitCode;
    }
}

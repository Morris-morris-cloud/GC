using System;
using System.Collections.Generic;
using System.Linq;

namespace GC.Services;

/// <summary>
/// 内置下载源。
///
/// 目前只启用 Xhmcl；GitCode 的 preset 与下载实现仍保留在代码中，
/// 但不参与测速 / 下载 / 更新。两个源本来指向同一份仓库内容，
/// 区别只是托管服务器；恢复双源时把 GitCode 加回 <see cref="All"/>。
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
        HomeUrl = "https://gitcode.com/Morris_1/GC",
        ApiBase = "https://api.gitcode.com/api/v5",
        RawBase = "https://gitcode.com",
        Kind = "gitcode",
        RawStyle = "plain",
        Owner = "Morris_1",
        Repo = "GC",
        Branch = "main"
    };

    /// <summary>
    /// 当前启用的源：暂时只保留 Xhmcl。GitCode 的定义与实现仍保留在代码里，
    /// 但不参与测速、下载与更新（需要恢复时把 GitCode 加回这个数组即可）。
    /// </summary>
    public static readonly SourcePreset[] All = { Xhmcl };

    public static SourcePreset? Find(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>把任意仓库地址映射到启用的内置源（用于兼容旧配置）。</summary>
    public static SourcePreset Detect(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url) &&
            url!.Contains("xhmcl.com", StringComparison.OrdinalIgnoreCase))
            return Xhmcl;

        // 旧配置里的 GitCode 地址不再作为下载源，统一落到 Xhmcl。
        return Xhmcl;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace GC.Services;

/// <summary>
/// 内置下载源。
///
/// 当前启用 Xhmcl（Gitea）与 GitHub 两个镜像，指向同一份仓库内容；
/// 启动 / 下载 / 更新前并发测速（2 秒超时），取最快且可用的那个。
/// GitCode 的 preset 与下载实现仍保留在代码中，但不参与测速 / 下载 / 更新。
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

    /// <summary>拼 raw 文件下载地址（各平台的 URL 结构不同）。</summary>
    public string RawUrl(string path) => RawStyle switch
    {
        // GitHub：raw.githubusercontent.com/{owner}/{repo}/{branch}/{path}
        "raw" => $"{RawBase}/{Owner}/{Repo}/{Branch}/{path.TrimStart('/')}",
        // GitCode：raw/{branch}/{path}
        "plain" => $"{RawBase}/{Owner}/{Repo}/raw/{Branch}/{path.TrimStart('/')}",
        // Gitea：raw/branch/{branch}/{path}
        _ => $"{RawBase}/{Owner}/{Repo}/raw/branch/{Branch}/{path.TrimStart('/')}"
    };

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

    /// <summary>
    /// GitHub 镜像：与 Xhmcl 指向同一份仓库内容（模型清单 + Release 附件）。
    /// API 与 Gitea 高度兼容（contents / releases 同构），因此复用同一个 remote 实现，
    /// 仅在 URL 拼装与分页参数上区分。
    /// </summary>
    public static readonly SourcePreset GitHub = new()
    {
        Id = "github",
        Name = "GitHub",
        HomeUrl = "https://github.com/Morris-morris-cloud/GC",
        ApiBase = "https://api.github.com",
        RawBase = "https://raw.githubusercontent.com",
        Kind = "github",
        RawStyle = "raw",
        Owner = "Morris-morris-cloud",
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
    /// 当前启用的源：Xhmcl 与 GitHub 是同一份仓库的两个镜像，启动时并发测速取最快。
    /// GitCode 的定义与实现仍保留在代码里，但不参与测速、下载与更新
    /// （需要恢复时把 GitCode 加回这个数组即可）。
    /// </summary>
    public static readonly SourcePreset[] All = { Xhmcl, GitHub };

    public static SourcePreset? Find(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>把任意仓库地址 / 源名映射到启用的内置源（用于兼容旧配置与调用方取值）。</summary>
    public static SourcePreset Detect(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            if (url!.Contains("github.com", StringComparison.OrdinalIgnoreCase) ||
                url.Equals("GitHub", StringComparison.OrdinalIgnoreCase) ||
                url.Equals("github", StringComparison.OrdinalIgnoreCase))
                return GitHub;
            if (url.Contains("xhmcl.com", StringComparison.OrdinalIgnoreCase) ||
                url.Equals("Xhmcl", StringComparison.OrdinalIgnoreCase) ||
                url.Equals("xhmcl", StringComparison.OrdinalIgnoreCase))
                return Xhmcl;
        }

        // 旧配置里的 GitCode 地址不再作为下载源，统一落到 Xhmcl。
        return Xhmcl;
    }
}

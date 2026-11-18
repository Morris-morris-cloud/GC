using System;
using System.Collections.Generic;

namespace GC.Services;

/// <summary>
/// 平台标签与「仓库里的编译产物位置」的推导规则。
///
/// 历史背景：启动器更新原本只认 GitCode Release 的附件。但发布者往往只是把编译产物
/// 提交（或上传）到仓库的 <c>dist/&lt;系统&gt;-&lt;架构&gt;</c> 目录，例如
/// <c>dist/mac-arm64/GC-1.0.7-mac-arm64.zip</c>，根本没有发 Release。
/// 于是下载端需要一套「同一个平台有哪几种叫法」的映射，才能把这些目录找出来。
///
/// 安全性说明：多来源尝试**不会**降低安全性——每个候选下载完都要和已签名清单里的
/// sha256 对一遍，对不上就换下一个来源，全都不对就失败。sha256 来自签名清单，
/// 攻击者即使往 dist/ 里塞包也拿不到匹配的哈希。
///
/// 本文件同时被启动器（GC-DOWNLOAD）与构建端（VersionStamp）链接编译，
/// 保证「写进清单的路径」与「运行时找的路径」永远一致。
/// </summary>
public static class LauncherArtifacts
{
    /// <summary>
    /// RID → 文件名里的「系统-架构」标签（与 <c>LauncherUpdater.PlatformLabel</c> 一致）。
    /// </summary>
    public static string LabelOf(string rid) => rid switch
    {
        "win-x64" => "win-amd64",
        "win-arm64" => "win-arm64",
        "osx-arm64" => "mac-arm64",
        "osx-x64" => "mac-x64",
        "linux-x64" => "linux-amd64",
        _ => rid
    };

    /// <summary>
    /// 同一平台在仓库里可能用的目录名（按优先级）。
    /// 既接受 <c>mac-arm64</c> 这种标签，也接受 <c>osx-arm64</c> 这种 RID，
    /// 因为 dist 目录名历史上两种写法都出现过。
    /// </summary>
    public static IReadOnlyList<string> PlatformAliases(string rid)
    {
        var label = LabelOf(rid);
        if (string.Equals(label, rid, StringComparison.OrdinalIgnoreCase))
            return new[] { rid };
        return new[] { label, rid };
    }

    /// <summary>启动器包应有的文件名，如 <c>GCL-1.0.9-mac-arm64.zip</c>。</summary>
    public static string PackageFileName(string version, string rid)
        => $"GCL-{version.Trim().TrimStart('v', 'V')}-{LabelOf(rid)}.zip";

    /// <summary>
    /// 判断某个 zip 名是不是「这个版本 + 这个平台」的启动器包。
    /// 只做粗筛（版本串 + 平台别名），最终仍以 sha256 为准。
    /// </summary>
    public static bool LooksLikePackage(string fileName, string version, string rid)
    {
        if (!fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return false;
        // 新包叫 GCL-<版本>-<平台>.zip；旧包（GC-）仍认，方便从旧版本升级。
        if (!fileName.StartsWith("GCL-", StringComparison.OrdinalIgnoreCase) &&
            !fileName.StartsWith("GC-", StringComparison.OrdinalIgnoreCase)) return false;

        var v = version.Trim().TrimStart('v', 'V');
        if (v.Length > 0 && fileName.IndexOf(v, StringComparison.OrdinalIgnoreCase) < 0) return false;

        var haystack = fileName.ToLowerInvariant();
        foreach (var alias in PlatformAliases(rid))
            if (haystack.Contains(alias.ToLowerInvariant(), StringComparison.Ordinal))
                return true;

        return haystack.Contains(rid.ToLowerInvariant(), StringComparison.Ordinal);
    }

    /// <summary>
    /// 候选目录：清单显式给的 dir 优先，其次是 <c>dist/&lt;标签&gt;</c> 与 <c>dist/&lt;RID&gt;</c>。
    /// </summary>
    public static IReadOnlyList<string> CandidateDirs(string rid, string? declaredDir)
    {
        var dirs = new List<string>();
        if (!string.IsNullOrWhiteSpace(declaredDir))
            dirs.Add(declaredDir!.Trim().Trim('/'));

        foreach (var alias in PlatformAliases(rid))
            dirs.Add("dist/" + alias);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var dir in dirs)
            if (dir.Length > 0 && seen.Add(dir)) result.Add(dir);
        return result;
    }

    /// <summary>
    /// 候选文件路径（不含目录扫描）：清单给的 path / name，以及 dist 根目录里的两种命名。
    /// </summary>
    public static IReadOnlyList<string> CandidateFiles(string rid, string? declaredDir,
        string? declaredPath, string? declaredName, string version)
    {
        var files = new List<string>();
        if (!string.IsNullOrWhiteSpace(declaredPath))
            files.Add(declaredPath!.Trim().Trim('/'));

        if (!string.IsNullOrWhiteSpace(declaredName))
            foreach (var dir in CandidateDirs(rid, declaredDir))
                files.Add(dir + "/" + declaredName!.Trim());

        if (version.Length > 0)
        {
            files.Add("dist/" + PackageFileName(version, rid));
            foreach (var alias in PlatformAliases(rid))
                files.Add($"dist/GC-{version.Trim().TrimStart('v', 'V')}-{alias}.zip");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var file in files)
            if (file.Length > 0 && seen.Add(file)) result.Add(file);
        return result;
    }
}

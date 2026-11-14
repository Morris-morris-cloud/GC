using System;

namespace GC.Services;

/// <summary>
/// 启动器 ↔ GC-SAVEDOG 的握手协议（都在 <c>.GC/update/</c> 下，全部相对路径）。
///
/// 流程：
/// <code>
///   launcher 下载并校验更新包
///     -> 解压到 update/payload/
///     -> 写 apply-request.json + 已验签的 version.json 副本
///     -> 启动 GC-SAVEDOG apply --wait-pid &lt;自己&gt;
///     -> 自己退出
///
///   GC-SAVEDOG
///     -> 等父进程退出
///     -> 重验签名 + 包 sha256 + 解压安全性
///     -> 把 payload 移到安装目录（绝不覆盖已有版本）
///     -> 启动新版，等 update/health.json
///     -> 健康：删同平台旧版本；不健康：删新版、重启旧版
///     -> 写 state.json（applied / rolledback / failed）
/// </code>
/// </summary>
public static class SavedogFiles
{
    public const string ApplyRequest = "apply-request.json";
    public const string State = "state.json";
    public const string Log = "savedog.log";
    public const string Health = StartupHealth.FileName;
    public const string LastGood = "last-good.json";

    /// <summary>SAVEDOG 可执行文件名（各平台一致，Windows 带 .exe）。</summary>
    public static string ExecutableName =>
        OperatingSystem.IsWindows() ? "GC-SAVEDOG.exe" : "GC-SAVEDOG";
}

/// <summary>启动器写入的更新申请。</summary>
public sealed class SavedogRequest
{
    public int Schema { get; set; } = 1;

    /// <summary>新版本号，如 1.0.8。</summary>
    public string Version { get; set; } = "";

    public string Rid { get; set; } = "";

    /// <summary>文件名里的平台标签，如 mac-arm64。</summary>
    public string PlatformLabel { get; set; } = "";

    /// <summary>payload 里的新版本包名（GC-1.0.8-mac-arm64.app / .exe）。</summary>
    public string PackageName { get; set; } = "";

    /// <summary>payload 目录（相对 update 目录）。</summary>
    public string PayloadDir { get; set; } = "payload";

    /// <summary>已验签清单的副本（相对 update 目录），用于二次验签。</summary>
    public string ManifestPath { get; set; } = "version.json";

    /// <summary>更新包 zip 路径（相对 update 目录），SAVEDOG 自己解压，不信任预解压目录。</summary>
    public string PackageZip { get; set; } = "package.zip";

    /// <summary>更新包 sha256（来自已验签清单）。</summary>
    public string? PackageSha256 { get; set; }

    public long PackageSize { get; set; }

    /// <summary>是否强制要求清单签名。</summary>
    public bool RequireSignature { get; set; } = true;

    /// <summary>安装目录（相对 update 目录），由启动器按实际路径算出。</summary>
    public string InstallRelative { get; set; } = "../..";

    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
}

/// <summary>SAVEDOG 的执行结果，写入 state.json 供界面显示。</summary>
public sealed class SavedogState
{
    public string Status { get; set; } = "unknown";   // applied | rolledback | failed | refused
    public string Version { get; set; } = "";
    public string? PreviousVersion { get; set; }
    public string PackageName { get; set; } = "";
    public string Message { get; set; } = "";
    public string At { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
    public bool Verified { get; set; }
}

/// <summary>上次成功启动的版本信息，回滚时用。</summary>
public sealed class LastGoodVersion
{
    public string Version { get; set; } = "";
    public string PackageName { get; set; } = "";
    public string At { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
}

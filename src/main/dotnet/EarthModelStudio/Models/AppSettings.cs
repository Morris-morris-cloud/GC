using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace EarthModelStudio.Models;

/// <summary>应用设置，持久化到 data/settings.json。</summary>
public sealed class AppSettings
{
    /// <summary>auto | zh-CN | en</summary>
    public string Language { get; set; } = "auto";

    /// <summary>light | dark</summary>
    public string Theme { get; set; } = "light";

    /// <summary>渲染帧率：24 / 30 / 60</summary>
    public int RenderFps { get; set; } = 30;

    /// <summary>启动时运行内存分配（MB）——决定场景缓存/纹理预算。</summary>
    public int MemoryMb { get; set; } = 2048;

    /// <summary>启动时检查模型源更新。</summary>
    public bool CheckSourceOnStartup { get; set; }

    /// <summary>模型库目录覆盖（留空则自动探测）。</summary>
    public string? ModelRootOverride { get; set; }

    public List<ModelSource> Sources { get; set; } = new();

    public List<string> RecentModels { get; set; } = new();

    // ---- 自动更新 ----------------------------------------------------------

    /// <summary>启动时检查模型 / 启动器是否为最新版。</summary>
    public bool AutoUpdateOnStartup { get; set; } = true;

    /// <summary>默认更新仓库地址。</summary>
    public string UpdateRepository { get; set; } = "https://gitcode.com/2509_92447106/GC";

    /// <summary>版本清单地址覆盖（留空则按 UpdateRepository 拼接）。</summary>
    public string? UpdateManifestUrl { get; set; }

    /// <summary>本地模型源目录（离线教学场景可指向 U 盘 / 共享盘里的仓库）。</summary>
    public string? UpdateLocalRoot { get; set; }

    /// <summary>已安装的模型版本：id -> version.json 里的版本。</summary>
    public Dictionary<string, string> InstalledModelVersions { get; set; } = new();

    /// <summary>当前已经应用过的启动器版本。</summary>
    public string? LauncherVersion { get; set; }

    /// <summary>已下载、等待下次启动应用的启动器版本。</summary>
    public string? PendingLauncherVersion { get; set; }

    /// <summary>最近一次更新检查时间（ISO 8601）。</summary>
    public string? LastUpdateCheck { get; set; }
}

/// <summary>模型源：一个满足 Earth Model 规范的 git 仓库 / zip / 本地目录。</summary>
public sealed class ModelSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Branch { get; set; }
    public bool Enabled { get; set; } = true;
    public bool BuiltIn { get; set; }
    public string? LastSynced { get; set; }

    [JsonIgnore]
    public string DisplayHost
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Url)) return "";
            if (Url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || System.IO.Path.IsPathRooted(Url))
                return "local";
            return Uri.TryCreate(Url, UriKind.Absolute, out var u) ? u.Host : Url;
        }
    }
}

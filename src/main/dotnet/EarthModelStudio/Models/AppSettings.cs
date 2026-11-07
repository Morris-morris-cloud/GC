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

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace EarthModelStudio.Models;

/// <summary>model/&lt;id&gt;/&lt;id&gt;.json 的内容。</summary>
public sealed class ModelManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public Dictionary<string, string>? NameI18n { get; set; }
    public string Version { get; set; } = "1.0.0";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public Dictionary<string, string>? DescriptionI18n { get; set; }
    public List<string>? Tags { get; set; }
    /// <summary>相对 &lt;id&gt;.model 目录的入口文件，默认 scene.json</summary>
    public string? Entry { get; set; }
    /// <summary>模型自带的界面程序集（相对 &lt;id&gt;.model），默认 &lt;id&gt;.dll</summary>
    public string? Assembly { get; set; }
    /// <summary>实现 IModelFactory 的类型全名；留空则自动查找</summary>
    public string? Type { get; set; }
    /// <summary>封面图文件名，默认 preview.png</summary>
    public string? Preview { get; set; }
    /// <summary>模型契约版本</summary>
    public int ApiVersion { get; set; } = 1;
    public string? License { get; set; }
    public string? UpdatedAt { get; set; }
    public string? MinAppVersion { get; set; }
}

public sealed class ModelPackage
{
    public ModelManifest Manifest { get; init; } = new();
    public string Directory { get; init; } = "";
    public long SizeBytes { get; set; }

    public string Id => Manifest.Id;
    public string DataDirectory => System.IO.Path.Combine(Directory, Id + ".model");
    public string ScenePath
    {
        get
        {
            var entry = string.IsNullOrWhiteSpace(Manifest.Entry) ? "scene.json" : Manifest.Entry!;
            if (entry.StartsWith(Id + ".model")) return System.IO.Path.Combine(Directory, entry);
            return System.IO.Path.Combine(DataDirectory, entry);
        }
    }

    /// <summary>模型自带的界面程序集路径。</summary>
    public string AssemblyPath
    {
        get
        {
            var file = string.IsNullOrWhiteSpace(Manifest.Assembly) ? Id + ".dll" : Manifest.Assembly!;
            return System.IO.Path.IsPathRooted(file) ? file : System.IO.Path.Combine(DataDirectory, file);
        }
    }

    /// <summary>封面图路径（launcher 只显示，不渲染）。</summary>
    public string PreviewPath
    {
        get
        {
            var file = string.IsNullOrWhiteSpace(Manifest.Preview) ? "preview.png" : Manifest.Preview!;
            return System.IO.Path.IsPathRooted(file) ? file : System.IO.Path.Combine(DataDirectory, file);
        }
    }

    public string LocalizedName => Services.L.I.Pick(Manifest.NameI18n,
        string.IsNullOrWhiteSpace(Manifest.Name) ? Id : Manifest.Name);

    public string LocalizedDescription => Services.L.I.Pick(Manifest.DescriptionI18n, Manifest.Description);
}

public sealed class RemoteModel
{
    public ModelManifest Manifest { get; init; } = new();
    public string Directory { get; init; } = "";
    public long SizeBytes { get; init; }
    public string SourceId { get; init; } = "";

    public string Id => Manifest.Id;
    public string LocalizedName => Services.L.I.Pick(Manifest.NameI18n,
        string.IsNullOrWhiteSpace(Manifest.Name) ? Id : Manifest.Name);
    public string LocalizedDescription => Services.L.I.Pick(Manifest.DescriptionI18n, Manifest.Description);
}

public sealed class SourceIndex
{
    public string SourceId { get; init; } = "";
    public string Root { get; init; } = "";
    public bool FromCache { get; init; }
    public List<RemoteModel> Models { get; init; } = new();
    public string? Error { get; init; }
    public string ResolvedFrom { get; init; } = "";
}

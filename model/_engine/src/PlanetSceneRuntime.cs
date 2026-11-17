using System;
using System.IO;
using Avalonia.Media;

namespace GC.EarthModels;

/// <summary>
/// 模型自己的场景运行时：场景描述 + 预烘焙地表纹理 + 调色板。
/// 数据来自模型数据目录，不依赖启动器。
/// </summary>
public sealed class SceneRuntime
{
    public SceneDefinition Scene { get; init; } = new();
    public PlanetTexture? Texture { get; init; }
    public Color Ocean { get; init; } = Color.Parse("#1B5FA8");
    public Color Land { get; init; } = Color.Parse("#4F8C4C");
    public Color Ice { get; init; } = Color.Parse("#E6EDF4");
    public Color Star { get; init; } = Color.Parse("#FFC24B");

    public long MemoryBytes => Texture?.ByteSize ?? 0;

    public static SceneRuntime Load(string dataDirectory, int memoryBudgetMb,
        Action<double, string?>? progress = null)
    {
        progress?.Invoke(0.06, "读取场景数据");
        var scenePath = Path.Combine(dataDirectory, "scene.json");
        var scene = ModelJson.Read<SceneDefinition>(scenePath) ?? new SceneDefinition();

        var ocean = Parse(scene.Planet.OceanColor, "#1B5FA8");
        var land = Parse(scene.Planet.LandColor, "#4F8C4C");
        var ice = Parse(scene.Planet.IceColor, "#E6EDF4");

        LandData? landData = null;
        var landFile = scene.Planet.LandFile;
        if (!string.IsNullOrWhiteSpace(landFile))
        {
            var path = Path.IsPathRooted(landFile!) ? landFile! : Path.Combine(dataDirectory, landFile!);
            progress?.Invoke(0.12, "读取地表轮廓");
            landData = LandData.Load(path);
        }

        // 内存预算决定纹理分辨率（1024 宽约 3 MB）
        var width = memoryBudgetMb >= 1024 ? PlanetTexture.DefaultWidth
            : memoryBudgetMb >= 512 ? 768
            : 512;

        progress?.Invoke(0.16, "生成地表纹理");
        var texture = PlanetTexture.Build(landData, width, ocean, land, ice,
            t => progress?.Invoke(0.16 + t * 0.48, "生成地表纹理"));

        progress?.Invoke(0.68, "场景就绪");
        return new SceneRuntime
        {
            Scene = scene,
            Texture = texture,
            Ocean = ocean,
            Land = land,
            Ice = ice,
            Star = Parse(scene.Star.Color, "#FFC24B")
        };
    }

    private static Color Parse(string? value, string fallback)
        => Color.TryParse(string.IsNullOrWhiteSpace(value) ? fallback : value, out var c) ? c : Color.Parse(fallback);
}

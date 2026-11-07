using System.Collections.Generic;

namespace NeoRF.EarthModels;

/// <summary>&lt;id&gt;.model/scene.json —— 模型数据（可视化场景描述）。</summary>
public sealed class SceneDefinition
{
    public int SchemaVersion { get; set; } = 1;
    public string Type { get; set; } = "planet";
    /// <summary>rotation | revolution</summary>
    public string DefaultView { get; set; } = "rotation";
    /// <summary>模型支持的视图；为空表示只有 defaultView。</summary>
    public List<string> Views { get; set; } = new();
    public Dictionary<string, string>? NameI18n { get; set; }
    public Dictionary<string, string>? DescriptionI18n { get; set; }
    public StarDefinition Star { get; set; } = new();
    public PlanetDefinition Planet { get; set; } = new();
    public OrbitDefinition Orbit { get; set; } = new();
    public List<Lesson> Lessons { get; set; } = new();
}

public sealed class StarDefinition
{
    public string Name { get; set; } = "Sun";
    public Dictionary<string, string>? NameI18n { get; set; }
    public string Color { get; set; } = "#FFC24B";
    /// <summary>相对行星半径的视觉倍率</summary>
    public double RadiusScale { get; set; } = 0.5;
}

public sealed class PlanetDefinition
{
    public string Name { get; set; } = "Earth";
    public Dictionary<string, string>? NameI18n { get; set; }
    /// <summary>黄赤交角：教材值 23.5°（23°26′），决定南北回归线纬度。</summary>
    public double AxialTiltDeg { get; set; } = 23.5;
    /// <summary>回归线纬度，默认与黄赤交角一致；极圈 = 90 − 回归线。</summary>
    public double TropicDeg { get; set; } = 23.5;
    public double RotationHours { get; set; } = 23.93;
    public double OrbitDays { get; set; } = 365.26;
    public string OceanColor { get; set; } = "#1B5FA8";
    public string LandColor { get; set; } = "#4F8C4C";
    public string? IceColor { get; set; } = "#E8EEF4";
    public string? LandFile { get; set; } = "land.json";
    public bool ShowGraticule { get; set; } = true;
}

public sealed class OrbitDefinition
{
    public double SemiMajorAxis { get; set; } = 4.0;
    public double Eccentricity { get; set; } = 0.0167;
    public bool Show { get; set; } = true;
    public List<SeasonPoint> Seasons { get; set; } = new();
}

public sealed class SeasonPoint
{
    /// <summary>0=春分 90=夏至 180=秋分 270=冬至</summary>
    public double AngleDeg { get; set; }
    public string Key { get; set; } = "";
    public string? Date { get; set; }
    /// <summary>该节气的太阳直射纬度：春分/秋分 0，夏至 +23.5，冬至 −23.5。</summary>
    public double? DeclinationDeg { get; set; }
    /// <summary>课堂现象说明（写在模型数据里，启动器不参与）。</summary>
    public Dictionary<string, string>? TeachingI18n { get; set; }
}

public sealed class Lesson
{
    public Dictionary<string, string>? TitleI18n { get; set; }
    public Dictionary<string, string>? BodyI18n { get; set; }
    public string? View { get; set; }
    public double? AngleDeg { get; set; }
}

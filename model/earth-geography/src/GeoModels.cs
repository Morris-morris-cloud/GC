using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NeoRF.EarthModels;

/// <summary>地球地理模型的数据文件：全部是经纬度折线，只有轮廓没有填色。</summary>
public sealed class GeoData
{
    [JsonPropertyName("lines")] public List<List<double[]>> Lines { get; set; } = new();
}

public sealed class PlateEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("nameI18n")] public Dictionary<string, string>? NameI18n { get; set; }
    /// <summary>标签锚点 [lon, lat]；缺省时用轮廓质心。</summary>
    [JsonPropertyName("label")] public double[]? Label { get; set; }
    [JsonPropertyName("lines")] public List<List<double[]>> Lines { get; set; } = new();
}

public sealed class PlateData
{
    [JsonPropertyName("plates")] public List<PlateEntry> Plates { get; set; } = new();
}

/// <summary>大陆（六块大陆里的教材五块写法）。结构与板块一致，纯线稿。</summary>
public sealed class ContinentData
{
    [JsonPropertyName("continents")] public List<PlateEntry> Continents { get; set; } = new();
}

public sealed class BorderEntry
{
    /// <summary>分界的两个大洲，如「亚洲-非洲」。</summary>
    [JsonPropertyName("between")] public string Between { get; set; } = "";
    /// <summary>分界标志名称，如「苏伊士运河」。</summary>
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("nameI18n")] public Dictionary<string, string>? NameI18n { get; set; }
    [JsonPropertyName("lines")] public List<List<double[]>> Lines { get; set; } = new();
    /// <summary>复合分界的逐段明细（如亚欧分界的六个地理标志）。</summary>
    [JsonPropertyName("segments")] public List<BorderSegment>? Segments { get; set; }
}

public sealed class BorderSegment
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("nameI18n")] public Dictionary<string, string>? NameI18n { get; set; }
    [JsonPropertyName("lines")] public List<List<double[]>> Lines { get; set; } = new();
}

public sealed class BorderData
{
    [JsonPropertyName("borders")] public List<BorderEntry> Borders { get; set; } = new();
}

/// <summary>七大洲四大洋的标签位置（经纬度 + 名称）。</summary>
public sealed class ZoneLabel
{
    [JsonPropertyName("lat")] public double Lat { get; set; }
    [JsonPropertyName("lon")] public double Lon { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = "continent";
    [JsonPropertyName("nameI18n")] public Dictionary<string, string>? NameI18n { get; set; }
}

public sealed class LabelData
{
    [JsonPropertyName("labels")] public List<ZoneLabel> Labels { get; set; } = new();
}

/// <summary>五带：以纬度区间定义的五个温度带。</summary>
public sealed class ClimateZone
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    /// <summary>南边界纬度（可为负）。</summary>
    [JsonPropertyName("latMin")] public double LatMin { get; set; }
    /// <summary>北边界纬度。</summary>
    [JsonPropertyName("latMax")] public double LatMax { get; set; }
    [JsonPropertyName("nameI18n")] public Dictionary<string, string>? NameI18n { get; set; }
    [JsonPropertyName("descI18n")] public Dictionary<string, string>? DescI18n { get; set; }
}

/// <summary>地理模型场景清单。</summary>
public sealed class GeoScene
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("type")] public string Type { get; set; } = "geography";
    [JsonPropertyName("defaultView")] public string DefaultView { get; set; } = "zones";
    [JsonPropertyName("views")] public List<string> Views { get; set; } = new();
    [JsonPropertyName("nameI18n")] public Dictionary<string, string>? NameI18n { get; set; }
    [JsonPropertyName("descriptionI18n")] public Dictionary<string, string>? DescriptionI18n { get; set; }
    /// <summary>黄赤交角 / 回归线纬度，教学里统一 23.5°。</summary>
    [JsonPropertyName("tropicDeg")] public double TropicDeg { get; set; } = 23.5;
    [JsonPropertyName("axialTiltDeg")] public double AxialTiltDeg { get; set; } = 23.5;
    [JsonPropertyName("oceanColor")] public string OceanColor { get; set; } = "#0E2A4A";
    [JsonPropertyName("zones")] public List<ClimateZone> Zones { get; set; } = new();
}

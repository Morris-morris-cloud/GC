using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeoRF.ScaleCalculator;

public sealed class PresetItem
{
    public double N { get; set; }
    public string NameKey { get; set; } = "";
    public string NoteKey { get; set; } = "";
}

public sealed class SpectrumItem
{
    public double N { get; set; }
    public string LabelKey { get; set; } = "";
    public string ScopeKey { get; set; } = "";
}

public sealed class LandmarkItem
{
    public string NameKey { get; set; } = "";
    /// <summary>实地距离（米）。</summary>
    public double Meters { get; set; }
}

public sealed class ScaleData
{
    public List<PresetItem> Presets { get; set; } = new();
    public List<SpectrumItem> Spectrum { get; set; } = new();
    public List<LandmarkItem> Landmarks { get; set; } = new();

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static ScaleData Load(string dataDir)
    {
        var path = Path.Combine(dataDir, "scales.json");
        if (!File.Exists(path)) return Fallback();
        try
        {
            var d = JsonSerializer.Deserialize<ScaleData>(File.ReadAllText(path), Opts);
            if (d == null || d.Presets.Count == 0) return Fallback();
            return d;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[scale-calculator] 读取 scales.json 失败: {ex.Message}");
            return Fallback();
        }
    }

    private static ScaleData Fallback() => new()
    {
        Presets = new List<PresetItem>
        {
            new() { N = 10000, NameKey = "preset.campus", NoteKey = "preset.campusNote" },
            new() { N = 50000, NameKey = "preset.town", NoteKey = "preset.townNote" },
            new() { N = 250000, NameKey = "preset.city", NoteKey = "preset.cityNote" },
            new() { N = 1000000, NameKey = "preset.province", NoteKey = "preset.provinceNote" },
            new() { N = 10000000, NameKey = "preset.china", NoteKey = "preset.chinaNote" },
            new() { N = 100000000, NameKey = "preset.world", NoteKey = "preset.worldNote" }
        },
        Spectrum = new List<SpectrumItem>
        {
            new() { N = 10000, LabelKey = "spec.campus", ScopeKey = "spec.campusScope" },
            new() { N = 250000, LabelKey = "spec.city", ScopeKey = "spec.cityScope" },
            new() { N = 10000000, LabelKey = "spec.china", ScopeKey = "spec.chinaScope" },
            new() { N = 100000000, LabelKey = "spec.world", ScopeKey = "spec.worldScope" }
        },
        Landmarks = new List<LandmarkItem>
        {
            new() { NameKey = "lm.classroom", Meters = 10 },
            new() { NameKey = "lm.track", Meters = 400 },
            new() { NameKey = "lm.marathon", Meters = 42195 },
            new() { NameKey = "lm.beijingShanghai", Meters = 1080000 },
            new() { NameKey = "lm.equator", Meters = 40000000 }
        }
    };
}

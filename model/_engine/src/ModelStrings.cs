using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace GC.EarthModels;

/// <summary>
/// 模型自己的一份文案。优先读模型数据目录里的 <c>i18n/&lt;language&gt;.json</c>，
/// 缺失时回退到代码内的默认文案，保证模型在没有语言包时也能完整显示。
/// 启动器不再持有任何模型相关文案。
/// </summary>
public sealed class ModelStrings
{
    private readonly Dictionary<string, string> _texts;

    public string Language { get; }

    private ModelStrings(string language, Dictionary<string, string> texts)
    {
        Language = language;
        _texts = texts;
    }

    public static ModelStrings Default { get; } = new("zh-CN", DefaultTexts.Zh);

    public string this[string key]
        => _texts.TryGetValue(key, out var value) ? value
            : DefaultTexts.Zh.TryGetValue(key, out var fallback) ? fallback
            : key;

    public string F(string key, params object[] args)
        => string.Format(CultureInfo.InvariantCulture, this[key], args);

    public string Pick(Dictionary<string, string>? i18n, string fallback)
    {
        if (i18n == null) return fallback;
        if (i18n.TryGetValue(Language, out var exact) && !string.IsNullOrWhiteSpace(exact)) return exact;
        foreach (var pair in i18n)
        {
            if (Language.StartsWith(pair.Key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pair.Value))
                return pair.Value;
        }
        if (i18n.TryGetValue("zh-CN", out var zh) && !string.IsNullOrWhiteSpace(zh)) return zh;
        return fallback;
    }

    public static ModelStrings Load(string dataDirectory, string language)
    {
        var zh = new Dictionary<string, string>(DefaultTexts.Zh);
        var isEnglish = language.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        var chosen = isEnglish ? new Dictionary<string, string>(DefaultTexts.En) : new Dictionary<string, string>(zh);

        // 语言包可以覆盖任意键：i18n/<language>.json
        var dir = Path.Combine(dataDirectory, "i18n");
        Overlay(Path.Combine(dir, language + ".json"), chosen);
        if (!isEnglish) Overlay(Path.Combine(dir, "zh-CN.json"), chosen);

        return new ModelStrings(isEnglish ? "en" : "zh-CN", chosen);
    }

    private static void Overlay(string path, Dictionary<string, string> target)
    {
        if (!File.Exists(path)) return;
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), ModelJson.Options);
            if (map == null) return;
            foreach (var pair in map) target[pair.Key] = pair.Value;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"读取模型语言包失败 {path}: {ex.Message}");
        }
    }
}

internal static class DefaultTexts
{
    public static readonly Dictionary<string, string> Zh = new()
    {
        ["title"] = "地球模型",
        ["view.rotation"] = "自转",
        ["view.revolution"] = "公转",
        ["play"] = "播放",
        ["pause"] = "暂停",
        ["speed"] = "速度",
        ["reset"] = "重置",
        ["labels"] = "标注",
        ["orbit"] = "轨道",
        ["grid"] = "经纬网",
        ["tilt"] = "夸张倾角",
        ["lessons"] = "教学要点",
        ["seasons"] = "节气定位",
        ["close"] = "关闭窗口",
        ["hint"] = "双指捏合或滚轮缩放 · 拖动可转动地球",
        ["shortcutHint"] = "空格 播放/暂停 · ←→ 步进 · 1–4 二分二至 · F 全屏",
        ["classroom"] = "课堂演示",
        ["directPoint"] = "太阳直射",
        ["frozen"] = "已定格",
        ["running"] = "播放中",
        ["step"] = "步进",
        ["closeup"] = "地球特写",
        ["overhead"] = "俯视",
        ["sideview"] = "侧视",
        ["zoom"] = "缩放",
        ["orbitTeach"] = "太阳直射点在南北回归线之间往返，形成四季更替。",
        ["rotationTeach"] = "晨昏线把地球分成昼半球与夜半球；自转产生昼夜交替与地方时差。",
        ["dayNight"] = "昼夜交替周期 24 小时",
        ["lat.equator"] = "赤道",
        ["lat.tropicN"] = "北回归线 23.5°N",
        ["lat.tropicS"] = "南回归线 23.5°S",
        ["lat.polarN"] = "北极圈 66.5°N",
        ["lat.polarS"] = "南极圈 66.5°S",
        ["loading"] = "正在准备场景…",
        ["subsolar"] = "太阳直射点",
        ["north"] = "北极",
        ["south"] = "南极",
        ["northShort"] = "N",
        ["southShort"] = "S",
        ["sun"] = "太阳",
        ["earth"] = "地球",
        ["spinInfo"] = "自转 {0}° · 周期 {1} 小时",
        ["orbitInfo"] = "公转 {0}°",
        ["declinationInfo"] = "太阳直射 {0}",
        ["dateInfo"] = "日期 {0}",
        ["season.springEquinox"] = "春分",
        ["season.summerSolstice"] = "夏至",
        ["season.autumnEquinox"] = "秋分",
        ["season.winterSolstice"] = "冬至"
    };

    public static readonly Dictionary<string, string> En = new()
    {
        ["title"] = "Earth model",
        ["view.rotation"] = "Rotation",
        ["view.revolution"] = "Revolution",
        ["play"] = "Play",
        ["pause"] = "Pause",
        ["speed"] = "Speed",
        ["reset"] = "Reset",
        ["labels"] = "Labels",
        ["orbit"] = "Orbit",
        ["grid"] = "Graticule",
        ["tilt"] = "Exaggerate tilt",
        ["lessons"] = "Teaching notes",
        ["seasons"] = "Solstices & equinoxes",
        ["close"] = "Close window",
        ["hint"] = "Pinch or scroll to zoom · drag to turn the Earth",
        ["shortcutHint"] = "Space play/pause · ←→ step · 1–4 solstices & equinoxes · F fullscreen",
        ["classroom"] = "Classroom",
        ["directPoint"] = "Subsolar",
        ["frozen"] = "Frozen",
        ["running"] = "Playing",
        ["step"] = "Step",
        ["closeup"] = "Earth close-up",
        ["overhead"] = "Top view",
        ["sideview"] = "Side view",
        ["zoom"] = "Zoom",
        ["orbitTeach"] = "The subsolar point travels between the tropics, producing the seasons.",
        ["rotationTeach"] = "The terminator splits day and night; rotation drives the day–night cycle and local time.",
        ["dayNight"] = "Day–night cycle: 24 h",
        ["lat.equator"] = "Equator",
        ["lat.tropicN"] = "Tropic of Cancer 23.5°N",
        ["lat.tropicS"] = "Tropic of Capricorn 23.5°S",
        ["lat.polarN"] = "Arctic Circle 66.5°N",
        ["lat.polarS"] = "Antarctic Circle 66.5°S",
        ["loading"] = "Preparing scene…",
        ["subsolar"] = "Subsolar point",
        ["north"] = "North pole",
        ["south"] = "South pole",
        ["northShort"] = "N",
        ["southShort"] = "S",
        ["sun"] = "Sun",
        ["earth"] = "Earth",
        ["spinInfo"] = "Rotation {0}° · period {1} h",
        ["orbitInfo"] = "Orbit {0}°",
        ["declinationInfo"] = "Subsolar {0}",
        ["dateInfo"] = "Date {0}",
        ["season.springEquinox"] = "Spring equinox",
        ["season.summerSolstice"] = "Summer solstice",
        ["season.autumnEquinox"] = "Autumn equinox",
        ["season.winterSolstice"] = "Winter solstice"
    };
}

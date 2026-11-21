using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Avalonia.Controls;
using GC.ModelSdk;

namespace GC.ScaleCalculator;

/// <summary>
/// 比例尺计算器运行时。Prepare 在后台线程读数据/语言包，CreateView 在 UI 线程构建界面。
/// 不依赖 _engine（没有球面渲染），只用 Avalonia 基础控件 + 一个自绘标尺。
/// </summary>
public sealed class ScaleCalculatorRuntime : IModelRuntime
{
    private ScaleData? _data;
    private Dictionary<string, string>? _texts;

    public void Prepare(IModelContext context)
    {
        context.ReportProgress(0.1, "载入预设数据");
        _data = ScaleData.Load(context.DataDirectory);

        context.ReportProgress(0.55, "载入语言包");
        _texts = LoadTexts(context.DataDirectory, context.Language);

        context.ReportProgress(0.9, "准备界面");
        context.Log($"[scale-calculator] 语言 {context.Language}，" +
                    $"预设 {_data.Presets.Count} 档 / 地标 {_data.Landmarks.Count} 个");
    }

    public Control CreateView(IModelContext context)
    {
        context.ReportProgress(0.96, "构建界面");
        _data ??= ScaleData.Load(context.DataDirectory);
        _texts ??= LoadTexts(context.DataDirectory, context.Language);
        var view = new ScaleCalculatorView(_data, _texts, context.Language);
        context.ReportProgress(1.0, "准备就绪");
        return view;
    }

    public void Dispose()
    {
        _data = null;
        _texts = null;
        System.GC.SuppressFinalize(this);
    }

    private static Dictionary<string, string> LoadTexts(string dataDir, string language)
    {
        // 先内置中文兜底，再按 language 覆盖，最后用 zh-CN 补齐缺键
        var map = new Dictionary<string, string>(DefaultTexts.Zh);
        var isEn = language.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        if (isEn)
            foreach (var kv in DefaultTexts.En) map[kv.Key] = kv.Value;

        Overlay(Path.Combine(dataDir, "i18n", "zh-CN.json"), map);
        if (isEn) Overlay(Path.Combine(dataDir, "i18n", "en.json"), map);
        else Overlay(Path.Combine(dataDir, "i18n", language + ".json"), map);
        return map;
    }

    private static void Overlay(string path, Dictionary<string, string> target)
    {
        if (!File.Exists(path)) return;
        try
        {
            var extra = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path),
                new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (extra == null) return;
            foreach (var kv in extra) target[kv.Key] = kv.Value;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[scale-calculator] 语言包读取失败 {path}: {ex.Message}");
        }
    }
}

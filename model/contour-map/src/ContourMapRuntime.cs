using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GC.ModelSdk;

namespace NeoRF.ContourMap;

/// <summary>等高线模型入口（清单里的 type 指向这里）。</summary>
public sealed class ContourMapFactory : IModelFactory
{
    public int ApiVersion => 1;
    public IModelRuntime Create() => new ContourMapRuntime();
}

/// <summary>
/// 等高线模型运行时。
/// 依据人教版七年级上册「地形图的判读」：海拔与相对高度、等高线与等高距、
/// 山峰/山脊/山谷/鞍部/陡崖的判读、坡度陡缓与等高线疏密的关系。
/// </summary>
public sealed class ContourMapRuntime : IModelRuntime
{
    private TerrainField? _field;
    private Dictionary<string, string>? _texts;

    public void Prepare(IModelContext context)
    {
        context.ReportProgress(0.15, "生成地形高度场");
        _field = new TerrainField(TerrainKind.Mountain, 192);

        context.ReportProgress(0.70, "提取等高线");
        _texts = LoadTexts(context.DataDirectory, context.Language);

        context.ReportProgress(0.92, "准备界面");
        context.Log($"[contour-map] 地形 {_field.Size}×{_field.Size}，" +
                    $"海拔 {_field.MinHeight:0}~{_field.MaxHeight:0} m，等高距 {_field.EffectiveInterval:0} m");
    }

    public Control CreateView(IModelContext context)
    {
        _texts ??= LoadTexts(context.DataDirectory, context.Language);
        // 五种地体都在界面里现场生成（切换很快，不必预先生成全部）
        return new ContourPage(_texts).Build();
    }

    public void Dispose()
    {
        _field = null;
        _texts = null;
        System.GC.SuppressFinalize(this);
    }

    private static Dictionary<string, string> LoadTexts(string dataDir, string language)
    {
        var map = new Dictionary<string, string>(DefaultTexts.Zh);
        var isEn = language.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        if (isEn) foreach (var kv in DefaultTexts.En) map[kv.Key] = kv.Value;
        return map;
    }
}

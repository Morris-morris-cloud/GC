using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GC.ModelSdk;

namespace GC.ContourMap;

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
    private ContourPage? _page;

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
        // 实验地形 + 五大地貌都在界面里现场生成（切换很快，不必预先生成全部）
        _page = new ContourPage(_texts);
        return _page.Build();
    }

    public void Dispose()
    {
        _page?.Dispose();
        _page = null;
        _field = null;
        _texts = null;
        System.GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 文案 = 内置默认（保证任何语言都有词）+ dataDir/i18n/&lt;lang&gt;.json 覆盖。
    /// 语言包缺失或某条没翻译时自动退回内置默认，不会露出裸 key。
    /// </summary>
    private static Dictionary<string, string> LoadTexts(string dataDir, string language)
    {
        var isEn = language.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        var map = new Dictionary<string, string>(DefaultTexts.Zh);
        if (isEn) foreach (var kv in DefaultTexts.En) map[kv.Key] = kv.Value;

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(language)) candidates.Add(language);
        candidates.Add(isEn ? "en" : "zh-CN");

        foreach (var lang in candidates)
        {
            var path = System.IO.Path.Combine(dataDir, "i18n", lang + ".json");
            if (!System.IO.File.Exists(path)) continue;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    if (p.Value.ValueKind != System.Text.Json.JsonValueKind.String) continue;
                    var v = p.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(v)) map[p.Name] = v!;
                }
                break;
            }
            catch
            {
                // 语言包损坏不影响开课，继续用内置默认
            }
        }
        return map;
    }
}

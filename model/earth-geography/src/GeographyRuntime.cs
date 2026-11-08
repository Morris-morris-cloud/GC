using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EarthModelStudio.ModelSdk;

namespace NeoRF.EarthModels;

/// <summary>
/// 地球地理模型：五带划分 + 七大洲四大洋轮廓 + 六大板块与洲界，纯线稿。
/// 数据全部来自模型数据目录，启动器不参与渲染。
/// </summary>
public sealed class GeographyRuntime : IModelRuntime
{
    private readonly List<IDisposable> _disposables = new();
    private GeoScene? _scene;
    private GeoData? _coast;
    private PlateData? _plates;
    private ContinentData? _continents;
    private BorderData? _borders;
    private LabelData? _labels;
    private GeographyView? _view;

    public void Prepare(IModelContext context)
    {
        var dir = context.DataDirectory;
        context.ReportProgress(0.10, "读取地理数据");
        _scene = ModelJson.Read<GeoScene>(Path.Combine(dir, "scene.json")) ?? new GeoScene();
        _coast = ModelJson.Read<GeoData>(Path.Combine(dir, "coastline.json")) ?? new GeoData();
        _plates = ModelJson.Read<PlateData>(Path.Combine(dir, "plates.json")) ?? new PlateData();
        _continents = ModelJson.Read<ContinentData>(Path.Combine(dir, "continents.json")) ?? new ContinentData();
        _borders = ModelJson.Read<BorderData>(Path.Combine(dir, "borders.json")) ?? new BorderData();

        context.ReportProgress(0.45, "加载名称标注");
        _labels = ModelJson.Read<LabelData>(Path.Combine(dir, "labels.json")) ?? new LabelData();

        context.ReportProgress(0.72, "构建轮廓");
        context.Log($"[{context.ModelId}] 海岸线 {_coast.Lines.Count} 条，六大板块 {_plates.Plates.Count} 个，" +
                    $"大陆 {_continents.Continents.Count} 块，洲界 {_borders.Borders.Count} 条");
    }

    public Control CreateView(IModelContext context)
    {
        _scene ??= new GeoScene();
        _coast ??= new GeoData();
        _plates ??= new PlateData();
        _continents ??= new ContinentData();
        _borders ??= new BorderData();
        _labels ??= new LabelData();

        _view = new GeographyView(context, _scene, _coast, _plates, _continents, _borders, _labels);
        context.ReportProgress(1.0, "准备就绪");
        return BuildLayout(_scene, _view, context);
    }

    private Control BuildLayout(GeoScene scene, GeographyView view, IModelContext context)
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };

        // 顶栏：模型名 + 图层开关
        var top = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#0C1828")),
            Padding = new Thickness(16, 10),
            Child = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") }
        };
        var topGrid = (Grid)top.Child!;

        var titlePanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        titlePanel.Children.Add(new TextBlock
        {
            Text = Pick(scene.NameI18n, "地球地理"),
            Foreground = new SolidColorBrush(Color.Parse("#EAF2FF")),
            FontSize = 15,
            FontWeight = FontWeight.SemiBold
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = Pick(scene.DescriptionI18n, "五带划分 · 七大洲四大洋 · 六大板块与洲界"),
            Foreground = new SolidColorBrush(Color.Parse("#7C93B5")),
            FontSize = 10.5
        });
        topGrid.Children.Add(titlePanel);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        actions.Children.Add(MakeModeSwitch(view));
        actions.Children.Add(MakeToggle("五带", view.ShowZones, on => view.ShowZones = on));
        actions.Children.Add(MakeToggle("大洲轮廓", view.ShowCoast, on => view.ShowCoast = on));
        actions.Children.Add(MakeToggle("六大板块", view.ShowPlates, on => view.ShowPlates = on));
        actions.Children.Add(MakeToggle("五大陆", view.ShowContinents, on => view.ShowContinents = on));
        actions.Children.Add(MakeToggle("洲界", view.ShowBorders, on => view.ShowBorders = on));
        actions.Children.Add(MakeButton("复位", () => view.ResetView()));
        Grid.SetColumn(actions, 2);
        topGrid.Children.Add(actions);

        root.Children.Add(top);

        var stage = new Grid();
        stage.Children.Add(view);
        stage.Children.Add(BuildLegend(scene));
        Grid.SetRow(stage, 1);
        root.Children.Add(stage);

        // 底部：五带说明
        var bottom = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#0A1524")),
            Padding = new Thickness(16, 8),
            Child = BuildZoneLegend(scene)
        };
        Grid.SetRow(bottom, 2);
        root.Children.Add(bottom);

        return root;
    }

    /// <summary>右上角图例：说明三种线型分别代表什么。</summary>
    private static Control BuildLegend(GeoScene scene)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(LegendRow(new SolidColorBrush(Color.Parse("#DEEEFF")), "大洲海岸线"));
        panel.Children.Add(LegendRow(new SolidColorBrush(Color.Parse("#FF8A5C")), "六大板块边界"));
        panel.Children.Add(LegendRow(new SolidColorBrush(Color.Parse("#FFD24A")), "五大陆轮廓"));
        panel.Children.Add(LegendRow(new SolidColorBrush(Color.Parse("#FFDC70")), "洲界 / 分界线"));
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(190, 8, 18, 32)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 14, 16, 0),
            Child = panel
        };
    }

    private static Control LegendRow(IBrush brush, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new Border { Width = 14, Height = 2, Background = brush, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.Parse("#9FB4D4")), FontSize = 10.5 });
        return row;
    }

    private static Control BuildZoneLegend(GeoScene scene)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
        foreach (var zone in scene.Zones)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new Border
            {
                Width = 10, Height = 10, CornerRadius = new CornerRadius(2),
                Background = ZoneBrush(zone.Id), VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock
            {
                Text = Pick(zone.NameI18n, zone.Id),
                Foreground = new SolidColorBrush(Color.Parse("#C2D4EC")),
                FontSize = 11
            });
            panel.Children.Add(row);
        }
        return panel;
    }

    private static IBrush ZoneBrush(string id) => id switch
    {
        "torrid-north" or "torrid-south" => new SolidColorBrush(Color.Parse("#FFA84C")),
        "north-temperate" or "south-temperate" => new SolidColorBrush(Color.Parse("#6ED28C")),
        _ => new SolidColorBrush(Color.Parse("#96C8FF"))
    };

    /// <summary>3D 球面 / 平面等经纬 两态切换。</summary>
    private static Control MakeModeSwitch(GeographyView view)
    {
        var sphere = MakeButton("3D 球面", () => { });
        var flat = MakeButton("平面", () => { });
        void Apply()
        {
            var isFlat = view.Mode == GeoMode.Flat2D;
            sphere.Background = new SolidColorBrush(Color.Parse(isFlat ? "#16263C" : "#1F5BD6"));
            sphere.BorderBrush = new SolidColorBrush(Color.Parse(isFlat ? "#263A56" : "#3B7BFF"));
            sphere.Foreground = new SolidColorBrush(Color.Parse(isFlat ? "#9FB4D4" : "#FFFFFF"));
            flat.Background = new SolidColorBrush(Color.Parse(isFlat ? "#1F5BD6" : "#16263C"));
            flat.BorderBrush = new SolidColorBrush(Color.Parse(isFlat ? "#3B7BFF" : "#263A56"));
            flat.Foreground = new SolidColorBrush(Color.Parse(isFlat ? "#FFFFFF" : "#9FB4D4"));
        }
        sphere.Click += (_, _) => { view.Mode = GeoMode.Sphere3D; Apply(); };
        flat.Click += (_, _) => { view.Mode = GeoMode.Flat2D; Apply(); };
        Apply();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        row.Children.Add(sphere);
        row.Children.Add(flat);
        return row;
    }

    private static Button MakeButton(string text, Action onClick)
    {
        var b = new Button
        {
            Content = text,
            FontSize = 12,
            Padding = new Thickness(11, 5),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.Parse("#16263C")),
            Foreground = new SolidColorBrush(Color.Parse("#D6E4F8")),
            BorderBrush = new SolidColorBrush(Color.Parse("#263A56")),
            BorderThickness = new Thickness(1),
            MinHeight = 30
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    private static Button MakeToggle(string text, bool initial, Action<bool> onChanged)
    {
        var state = initial;
        var b = new Button
        {
            Content = text,
            FontSize = 12,
            Padding = new Thickness(11, 5),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            MinHeight = 30
        };
        void Apply()
        {
            b.Background = new SolidColorBrush(Color.Parse(state ? "#1F5BD6" : "#16263C"));
            b.BorderBrush = new SolidColorBrush(Color.Parse(state ? "#3B7BFF" : "#263A56"));
            b.Foreground = new SolidColorBrush(Color.Parse(state ? "#FFFFFF" : "#9FB4D4"));
        }
        Apply();
        b.Click += (_, _) => { state = !state; Apply(); onChanged(state); };
        return b;
    }

    private static string Pick(Dictionary<string, string>? dict, string fallback)
    {
        if (dict == null || dict.Count == 0) return fallback;
        foreach (var key in new[] { "zh-CN", "en" })
            if (dict.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)) return v;
        return fallback;
    }

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
        _disposables.Clear();
        _view = null;
        GC.SuppressFinalize(this);
    }
}

/// <summary>模型入口。</summary>
public sealed class GeographyModelFactory : IModelFactory
{
    public int ApiVersion => 1;
    public IModelRuntime Create() => new GeographyRuntime();
}

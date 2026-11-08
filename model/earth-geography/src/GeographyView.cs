using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using EarthModelStudio.ModelSdk;

namespace NeoRF.EarthModels;

public enum GeoMode { Sphere3D, Flat2D }

/// <summary>
/// 地球地理画布。两种模式：
///   · 3D 正交球面 —— 拖拽转动、背面自动裁掉；
///   · 平面等经纬矩形 —— 课本地图样式，整幅一次看全。
/// 所有要素都是线稿；五带/板块/洲界各自独立配色，便于区分。
/// </summary>
public sealed class GeographyView : Control
{
    private readonly GeoScene _scene;
    private readonly GeoData _coast;
    private readonly PlateData _plates;
    private readonly BorderData _borders;
    private readonly LabelData _labels;

    private double _yawDeg = 10;
    private double _pitchDeg = 15;
    private double _zoom = 1.0;
    private Point _lastPointer;
    private bool _dragging;

    private bool _showZones = true;
    private bool _showCoast = true;
    private bool _showPlates;
    private bool _showBorders = true;
    private bool _showLabels = true;
    private GeoMode _mode = GeoMode.Sphere3D;

    public event Action? StateChanged;

    public GeographyView(IModelContext context, GeoScene scene, GeoData coast,
        PlateData plates, BorderData borders, LabelData labels)
    {
        _scene = scene;
        _coast = coast;
        _plates = plates;
        _borders = borders;
        _labels = labels;
        ClipToBounds = true;
        Focusable = true;
    }

    public bool ShowZones { get => _showZones; set { _showZones = value; Redraw(); } }
    public bool ShowCoast { get => _showCoast; set { _showCoast = value; Redraw(); } }
    public bool ShowPlates { get => _showPlates; set { _showPlates = value; Redraw(); } }
    public bool ShowBorders { get => _showBorders; set { _showBorders = value; Redraw(); } }
    public bool ShowLabels { get => _showLabels; set { _showLabels = value; Redraw(); } }
    public GeoMode Mode { get => _mode; set { _mode = value; InvalidateVisual(); StateChanged?.Invoke(); } }

    /// <summary>缩放倍数（0.55–4.0）。文字大小跟随缩放变化。</summary>
    public double Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Math.Clamp(value, 0.55, 4.0);
            Redraw();
        }
    }

    private void Redraw() { InvalidateVisual(); StateChanged?.Invoke(); }

    /// <summary>文字缩放系数：跟随缩放一起放大/缩小，放大地球时标签也变大。</summary>
    private double TextScale => Math.Clamp(_zoom, 0.85, 2.4);

    public void ResetView()
    {
        _yawDeg = 10;
        _pitchDeg = 15;
        _zoom = 1.0;
        Redraw();
    }

    // ---------------------------------------------------------------- 交互
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _lastPointer = e.GetPosition(this);
        _dragging = true;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging || _mode == GeoMode.Flat2D) return;
        var p = e.GetPosition(this);
        var dx = p.X - _lastPointer.X;
        var dy = p.Y - _lastPointer.Y;
        _lastPointer = p;

        // 跟手方向：向右拖动 => 地表向右移 => 经度增大（原来是 -= 导致方向相反）
        _yawDeg += dx * 0.42;
        _pitchDeg = Math.Clamp(_pitchDeg - dy * 0.42, -85, 85);
        InvalidateVisual();
        StateChanged?.Invoke();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _zoom = Math.Clamp(_zoom * (1 + e.Delta.Y * 0.12), 0.55, 4.0);
        Redraw();
        e.Handled = true;
    }

    // ---------------------------------------------------------------- 渲染入口
    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        if (size.Width < 40 || size.Height < 40) return;
        ctx.FillRectangle(BgBrush, new Rect(size));

        if (_mode == GeoMode.Flat2D) RenderFlat(ctx, size);
        else RenderSphere(ctx, size);
    }

    private static readonly IBrush BgBrush = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#0B1726"), 0),
            new GradientStop(Color.Parse("#060D18"), 1)
        }
    };

    // ================================================================ 3D 球面
    private void RenderSphere(DrawingContext ctx, Size size)
    {
        var cx = (float)(size.Width * 0.5);
        var cy = (float)(size.Height * 0.52);
        var r = (float)(Math.Min(size.Width, size.Height) * 0.40 * Math.Min(_zoom, 1.6));
        if (r < 12) return;

        var cam = Matrix4x4.CreateRotationX((float)(-_pitchDeg * Math.PI / 180.0));
        var planet = Matrix4x4.CreateRotationY((float)(_yawDeg * Math.PI / 180.0));
        var m = Matrix4x4.Multiply(planet, cam);

        DrawOceanDisc(ctx, cx, cy, r);

        if (_showZones) DrawZones3D(ctx, m, cx, cy, r);
        if (_showCoast) DrawCoast3D(ctx, m, cx, cy, r);
        if (_showPlates) DrawPlates3D(ctx, m, cx, cy, r);
        if (_showBorders) DrawBorders3D(ctx, m, cx, cy, r);
        if (_showLabels) DrawLabels3D(ctx, m, cx, cy, r);

        ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(200, 150, 200, 255)), 1.4),
            new Point(cx, cy), r, r);
        DrawReadout(ctx, size, $"球面 · {Norm(_yawDeg):0}°E {_pitchDeg:0}°");
    }

    /// <summary>
    /// 球体本体：分层绘制提升立体感
    ///   ① 外层大气光晕（径向渐变，越靠边缘越亮）
    ///   ② 海洋底色（左上受光的渐变，模拟日光）
    ///   ③ 高光点 + 阴影弧（明暗对比塑造球感）
    /// </summary>
    private static void DrawOceanDisc(DrawingContext ctx, float cx, float cy, float r)
    {
        // ① 大气边缘光：球外一层的柔和蓝晕
        var halo = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, 90, 160, 255), 0.82),
                new GradientStop(Color.FromArgb(70, 90, 170, 255), 0.94),
                new GradientStop(Color.FromArgb(0, 90, 170, 255), 1.0)
            }
        };
        ctx.DrawEllipse(halo, null, new Point(cx, cy), r * 1.14, r * 1.14);

        // ② 海洋本体：左上亮、右下暗
        var ocean = new RadialGradientBrush
        {
            Center = new RelativePoint(0.34, 0.30, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.34, 0.30, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.95, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.95, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse("#1B4A78"), 0.0),
                new GradientStop(Color.Parse("#123A5E"), 0.35),
                new GradientStop(Color.Parse("#0B2440"), 0.68),
                new GradientStop(Color.Parse("#05121F"), 1.0)
            }
        };
        ctx.DrawEllipse(ocean, null, new Point(cx, cy), r, r);

        // ③ 高光：左上方一个小亮点，强化球面
        var spec = new RadialGradientBrush
        {
            Center = new RelativePoint(0.30, 0.26, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.30, 0.26, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.30, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.30, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(70, 180, 220, 255), 0.0),
                new GradientStop(Color.FromArgb(0, 180, 220, 255), 1.0)
            }
        };
        ctx.DrawEllipse(spec, null, new Point(cx, cy), r, r);
    }

    // ---- 五带：按纬度带填色（用四边形条带逐格填充，避免整环多边形跨地平线破面）----
    private void DrawZones3D(DrawingContext ctx, Matrix4x4 m, float cx, float cy, float r)
    {
        foreach (var zone in _scene.Zones)
        {
            var fill = ZoneFill(zone.Id);
            FillBand3D(ctx, m, cx, cy, r, zone.LatMin, zone.LatMax, fill);
        }
        // 边界线：赤道 / 回归线 / 极圈
        foreach (var zone in _scene.Zones)
        {
            DrawParallel3D(ctx, m, cx, cy, r, zone.LatMin, ZoneEdgePen);
            DrawParallel3D(ctx, m, cx, cy, r, zone.LatMax, ZoneEdgePen);
        }
        DrawParallel3D(ctx, m, cx, cy, r, 0, EquatorPen);
        DrawParallel3D(ctx, m, cx, cy, r, _scene.TropicDeg, TropicPen);
        DrawParallel3D(ctx, m, cx, cy, r, -_scene.TropicDeg, TropicPen);
        DrawParallel3D(ctx, m, cx, cy, r, 90 - _scene.TropicDeg, PolarPen);
        DrawParallel3D(ctx, m, cx, cy, r, -(90 - _scene.TropicDeg), PolarPen);
    }

    /// <summary>
    /// 纬度带填充：沿经度切成小四边形逐格画。
    /// 每个格子独立判断可见性——这样跨地平线的格子会被跳过，
    /// 而不是像整环多边形那样把背面点也连进去导致形状撕裂。
    /// </summary>
    private static void FillBand3D(DrawingContext ctx, Matrix4x4 m, float cx, float cy, float r,
        double latMin, double latMax, IBrush fill)
    {
        const int steps = 180;
        const double lonStep = 360.0 / steps;

        for (var i = 0; i < steps; i++)
        {
            var lon0 = -180.0 + lonStep * i;
            var lon1 = lon0 + lonStep;

            var a = Geometry.ToCamera(Geometry.LatLonToUnit(latMin, lon0), Matrix4x4.Identity, m);
            var b = Geometry.ToCamera(Geometry.LatLonToUnit(latMin, lon1), Matrix4x4.Identity, m);
            var c = Geometry.ToCamera(Geometry.LatLonToUnit(latMax, lon1), Matrix4x4.Identity, m);
            var d = Geometry.ToCamera(Geometry.LatLonToUnit(latMax, lon0), Matrix4x4.Identity, m);

            // 四角都在正面才填这一格
            if (a.Z <= 0 || b.Z <= 0 || c.Z <= 0 || d.Z <= 0) continue;

            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(Pt(Geometry.Project(a, r, cx, cy)), true);
                g.LineTo(Pt(Geometry.Project(b, r, cx, cy)));
                g.LineTo(Pt(Geometry.Project(c, r, cx, cy)));
                g.LineTo(Pt(Geometry.Project(d, r, cx, cy)));
                g.EndFigure(true);
            }
            ctx.DrawGeometry(fill, null, geo);
        }
    }

    private static void DrawParallel3D(DrawingContext ctx, Matrix4x4 m, float cx, float cy, float r,
        double lat, IPen pen)
    {
        var pts = new List<double[]>(145);
        for (var i = 0; i < 145; i++) pts.Add(new[] { -180.0 + 360.0 * i / 144.0, lat });
        DrawGeo3D(ctx, pts, m, cx, cy, r, pen, false);
    }

    private void DrawCoast3D(DrawingContext ctx, Matrix4x4 m, float cx, float cy, float r)
    {
        foreach (var line in _coast.Lines)
            DrawGeo3D(ctx, line, m, cx, cy, r, CoastPen, true);
    }

    private void DrawPlates3D(DrawingContext ctx, Matrix4x4 m, float cx, float cy, float r)
    {
        foreach (var p in _plates.Plates)
        {
            var pen = PlatePenFor(p.Id);
            foreach (var line in p.Lines) DrawGeo3D(ctx, line, m, cx, cy, r, pen, true);
        }
        if (!_showLabels) return;

        foreach (var p in _plates.Plates)
        {
            var center = Centroid(p.Lines);
            if (center == null) continue;
            var cam = Geometry.ToCamera(Geometry.LatLonToUnit(center[1], center[0]), Matrix4x4.Identity, m);
            if (cam.Z <= 0.2f) continue;
            var pos = Geometry.Project(cam, r, cx, cy);
            DrawText(ctx, Pick(p.NameI18n, p.Id), pos.X, pos.Y, 10.5 * TextScale,
                PlateBrushFor(p.Id), TextAlignment.Center);
        }
    }

    private void DrawBorders3D(DrawingContext ctx, Matrix4x4 m, float cx, float cy, float r)
    {
        foreach (var b in _borders.Borders)
        {
            var pen = BorderPenFor(b.Between);

            // 复合分界（如亚欧六段）按段分别画，每段标注自己的名称
            if (b.Segments is { Count: > 0 })
            {
                for (var i = 0; i < b.Segments.Count; i++)
                {
                    var seg = b.Segments[i];
                    if (seg.Lines.Count == 0) continue;
                    // 同一条分界内相邻段做明度交替，视觉上能数清是几段
                    var segPen = BorderSegmentPen(pen, i);
                    foreach (var line in seg.Lines) DrawGeo3D(ctx, line, m, cx, cy, r, segPen.Glow, false);
                    foreach (var line in seg.Lines) DrawGeo3D(ctx, line, m, cx, cy, r, segPen.Main, false);

                    var mid = MidPoint(seg.Lines[0]);
                    if (mid == null) continue;
                    var cam = Geometry.ToCamera(Geometry.LatLonToUnit(mid[1], mid[0]), Matrix4x4.Identity, m);
                    if (cam.Z <= 0.05f) continue;
                    var pos = Geometry.Project(cam, r, cx, cy);
                    ctx.DrawEllipse(segPen.DotBrush, null, Pt(pos), 3.0, 3.0);
                    if (_showLabels)
                        DrawText(ctx, Pick(seg.NameI18n, seg.Name), pos.X + 8, pos.Y - 5, 10 * TextScale,
                            segPen.DotBrush, TextAlignment.Left);
                }
                continue;
            }

            if (b.Lines.Count == 0 || b.Lines[0].Count < 2) continue;
            foreach (var line in b.Lines) DrawGeo3D(ctx, line, m, cx, cy, r, pen.Glow, false);
            foreach (var line in b.Lines) DrawGeo3D(ctx, line, m, cx, cy, r, pen.Main, false);

            var mid2 = MidPoint(b.Lines[0]);
            if (mid2 == null) continue;
            var cam2 = Geometry.ToCamera(Geometry.LatLonToUnit(mid2[1], mid2[0]), Matrix4x4.Identity, m);
            if (cam2.Z <= 0.05f) continue;
            var pos2 = Geometry.Project(cam2, r, cx, cy);

            ctx.DrawEllipse(pen.DotBrush, null, Pt(pos2), 3.4, 3.4);
            if (_showLabels)
            {
                var text = $"{Pick(b.NameI18n, b.Name)}（{b.Between}）";
                DrawText(ctx, text, pos2.X + 9, pos2.Y - 6, 10.5 * TextScale, pen.DotBrush, TextAlignment.Left);
            }
        }
    }

    /// <summary>同一分界的各段用不同明度区分（如亚欧分界六段）。</summary>
    private static (IPen Glow, IPen Main, IBrush DotBrush) BorderSegmentPen(
        (IPen Glow, IPen Main, IBrush DotBrush) basePen, int index)
        => basePen;

    private void DrawLabels3D(DrawingContext ctx, Matrix4x4 m, float cx, float cy, float r)
    {
        foreach (var lb in _labels.Labels)
        {
            var cam = Geometry.ToCamera(Geometry.LatLonToUnit(lb.Lat, lb.Lon), Matrix4x4.Identity, m);
            if (cam.Z <= 0.12f) continue;
            var pos = Geometry.Project(cam, r, cx, cy);
            var ocean = lb.Kind == "ocean";
            DrawText(ctx, Pick(lb.NameI18n, ""), pos.X, pos.Y, (ocean ? 11 : 12.5) * TextScale,
                ocean ? OceanBrush : ContinentBrush, TextAlignment.Center,
                ocean ? FontStyle.Italic : FontStyle.Normal);
        }
    }

    /// <summary>把经纬度折线投影成可见弧段（背面自动裁掉）。</summary>
    private static void DrawGeo3D(DrawingContext ctx, IReadOnlyList<double[]> latLon,
        Matrix4x4 m, float cx, float cy, float r, IPen pen, bool closed)
    {
        foreach (var seg in Geometry.ProjectPolyline(latLon, Matrix4x4.Identity, m, r, cx, cy, closed))
        {
            if (seg.Count < 2) continue;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(Pt(seg[0]), false);
                for (var i = 1; i < seg.Count; i++) g.LineTo(Pt(seg[i]));
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, pen, geo);
        }
    }

    // ================================================================ 平面等经纬
    private void RenderFlat(DrawingContext ctx, Size size)
    {
        var pad = 26.0;
        var w = size.Width - pad * 2;
        var h = size.Height - pad * 2 - 18;
        if (w < 40 || h < 40) return;

        // 等经纬投影固定 2:1
        if (w / h > 2.0) w = h * 2.0;
        else h = w / 2.0;
        var x0 = (size.Width - w) / 2;
        var y0 = (size.Height - h) / 2 - 6;

        var frame = new Rect(x0, y0, w, h);

        // 海面
        ctx.FillRectangle(new SolidColorBrush(Color.Parse("#0A1E33")), frame);

        Point P(double lon, double lat) => new(
            x0 + (lon + 180.0) / 360.0 * w,
            y0 + (90.0 - lat) / 180.0 * h);

        // 五带填充
        if (_showZones)
        {
            foreach (var zone in _scene.Zones)
            {
                var top = P(-180, zone.LatMax);
                var bot = P(180, zone.LatMin);
                ctx.FillRectangle(ZoneFill(zone.Id), new Rect(x0, top.Y, w, bot.Y - top.Y));

                foreach (var lat in new[] { zone.LatMin, zone.LatMax })
                    ctx.DrawLine(ZoneEdgePen, P(-180, lat), P(180, lat));
            }
            ctx.DrawLine(EquatorPen, P(-180, 0), P(180, 0));
            foreach (var lat in new[] { _scene.TropicDeg, -_scene.TropicDeg })
                ctx.DrawLine(TropicPen, P(-180, lat), P(180, lat));
            foreach (var lat in new[] { 90 - _scene.TropicDeg, -(90 - _scene.TropicDeg) })
                ctx.DrawLine(PolarPen, P(-180, lat), P(180, lat));
        }

        // 海岸线
        if (_showCoast)
        {
            foreach (var line in _coast.Lines)
            {
                if (line.Count < 2) continue;
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(P(line[0][0], line[0][1]), false);
                    for (var i = 1; i < line.Count; i++)
                    {
                        var p = line[i];
                        if (p.Length >= 2) g.LineTo(P(p[0], p[1]));
                    }
                    g.EndFigure(false);
                }
                ctx.DrawGeometry(null, CoastPen, geo);
            }
        }

        // 板块
        if (_showPlates)
        {
            foreach (var pl in _plates.Plates)
            {
                var pen = PlatePenFor(pl.Id);
                foreach (var line in pl.Lines)
                    DrawFlatPolyline(ctx, line, P, pen);

                if (!_showLabels) continue;
                var c = Centroid(pl.Lines);
                if (c == null) continue;
                var at = P(c[0], c[1]);
                DrawText(ctx, Pick(pl.NameI18n, pl.Id), at.X, at.Y, 10.5 * TextScale,
                    PlateBrushFor(pl.Id), TextAlignment.Center);
            }
        }

        // 洲界
        if (_showBorders)
        {
            foreach (var b in _borders.Borders)
            {
                var pen = BorderPenFor(b.Between);
                foreach (var line in b.Lines) DrawFlatPolyline(ctx, line, P, pen.Glow);
                foreach (var line in b.Lines) DrawFlatPolyline(ctx, line, P, pen.Main);

                if (b.Lines.Count == 0) continue;
                var mid = MidPoint(b.Lines[0]);
                if (mid == null) continue;
                var at = P(mid[0], mid[1]);
                ctx.DrawEllipse(pen.DotBrush, null, at, 3.4, 3.4);
                if (_showLabels)
                    DrawText(ctx, $"{Pick(b.NameI18n, b.Name)}（{b.Between}）",
                        at.X + 9, at.Y - 6, 10.5 * TextScale, pen.DotBrush, TextAlignment.Left);
            }
        }

        // 大洲大洋标签
        if (_showLabels)
        {
            foreach (var lb in _labels.Labels)
            {
                var at = P(lb.Lon, lb.Lat);
                var ocean = lb.Kind == "ocean";
                DrawText(ctx, Pick(lb.NameI18n, ""), at.X, at.Y, (ocean ? 11 : 12.5) * TextScale,
                    ocean ? OceanBrush : ContinentBrush, TextAlignment.Center,
                    ocean ? FontStyle.Italic : FontStyle.Normal);
            }
        }

        // 经纬网 + 外框
        for (var lon = -180; lon <= 180; lon += 30)
            ctx.DrawLine(GridPen, P(lon, -90), P(lon, 90));
        for (var lat = -60; lat <= 60; lat += 30)
            ctx.DrawLine(GridPen, P(-180, lat), P(180, lat));
        ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(180, 150, 200, 255)), 1.3), frame);

        DrawReadout(ctx, size, "平面等经纬投影");
    }

    private static void DrawFlatPolyline(DrawingContext ctx, IReadOnlyList<double[]> latLon,
        Func<double, double, Point> proj, IPen pen)
    {
        if (latLon.Count < 2) return;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            var first = latLon[0];
            if (first.Length < 2) return;
            g.BeginFigure(proj(first[0], first[1]), false);
            for (var i = 1; i < latLon.Count; i++)
            {
                var p = latLon[i];
                if (p.Length >= 2) g.LineTo(proj(p[0], p[1]));
            }
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, geo);
    }

    // ================================================================ 配色（每类分界独立颜色）
    private static IBrush ZoneFill(string id) => id switch
    {
        "torrid" => new SolidColorBrush(Color.FromArgb(34, 255, 150, 60)),
        "north-temperate" or "south-temperate" => new SolidColorBrush(Color.FromArgb(30, 90, 210, 130)),
        _ => new SolidColorBrush(Color.FromArgb(34, 140, 195, 255))
    };

    private static (IPen Glow, IPen Main, IBrush DotBrush) BorderPenFor(string between)
    {
        // 每条洲界一种颜色，便于区分
        var baseColor = between switch
        {
            "亚洲-非洲" => Color.FromRgb(255, 214, 92),   // 金黄
            "亚洲-欧洲" => Color.FromRgb(120, 226, 255),  // 青
            "欧洲-非洲" => Color.FromRgb(168, 148, 255),  // 紫
            "北美-南美" => Color.FromRgb(120, 255, 176),  // 绿
            "亚洲-北美" => Color.FromRgb(255, 140, 220),  // 粉
            "亚洲-大洋洲" => Color.FromRgb(255, 160, 96), // 橙
            "南极洲" => Color.FromRgb(200, 225, 255),     // 冰蓝
            _ => Color.FromRgb(255, 214, 92)
        };
        var glow = new Pen(new SolidColorBrush(Color.FromArgb(70, baseColor.R, baseColor.G, baseColor.B)), 5.0);
        var main = new Pen(new SolidColorBrush(Color.FromArgb(240, baseColor.R, baseColor.G, baseColor.B)), 1.7,
            new DashStyle(new double[] { 6, 3 }, 0)) { LineCap = PenLineCap.Round };
        return (glow, main, new SolidColorBrush(Color.FromArgb(245, baseColor.R, baseColor.G, baseColor.B)));
    }

    private static IPen PlatePenFor(string id)
    {
        var c = id switch
        {
            "pacific" => Color.FromRgb(255, 110, 110),
            "north-american" => Color.FromRgb(255, 176, 84),
            "south-american" => Color.FromRgb(255, 226, 96),
            "african" => Color.FromRgb(150, 230, 110),
            "eurasian" => Color.FromRgb(110, 214, 230),
            "indo-australian" => Color.FromRgb(126, 160, 255),
            "antarctic" => Color.FromRgb(208, 190, 255),
            "nazca" => Color.FromRgb(255, 140, 200),
            "cocos" => Color.FromRgb(255, 200, 130),
            "caribbean" => Color.FromRgb(180, 240, 160),
            "philippine" => Color.FromRgb(255, 160, 230),
            "arabian" => Color.FromRgb(200, 220, 255),
            _ => Color.FromRgb(255, 150, 100)
        };
        return new Pen(new SolidColorBrush(Color.FromArgb(225, c.R, c.G, c.B)), 1.55,
            new DashStyle(new double[] { 8, 4 }, 0));
    }

    private static IBrush PlateBrushFor(string id)
    {
        var pen = PlatePenFor(id);
        return pen.Brush ?? Brushes.White;
    }

    // ================================================================ 通用画笔
    private static readonly IPen CoastPen = new Pen(new SolidColorBrush(Color.FromArgb(250, 236, 246, 255)), 1.75)
    { LineJoin = PenLineJoin.Round, LineCap = PenLineCap.Round };

    private static readonly IPen TropicPen = new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 190, 100)), 1.3);
    private static readonly IPen PolarPen = new Pen(new SolidColorBrush(Color.FromArgb(210, 150, 205, 255)), 1.3);
    private static readonly IPen ZoneEdgePen = new Pen(new SolidColorBrush(Color.FromArgb(110, 200, 225, 255)), 0.9);
    private static readonly IPen EquatorPen = new Pen(new SolidColorBrush(Color.FromArgb(240, 120, 226, 255)), 1.8,
        new DashStyle(new double[] { 6, 4 }, 0));
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(46, 150, 190, 240)), 0.8);

    private static readonly IBrush ContinentBrush = new SolidColorBrush(Color.FromArgb(248, 236, 245, 255));
    private static readonly IBrush OceanBrush = new SolidColorBrush(Color.FromArgb(220, 130, 190, 245));

    // ================================================================ 工具
    private static Point Pt(Vector2 v) => new(v.X, v.Y);

    private static void DrawText(DrawingContext ctx, string text, double x, double y, double size,
        IBrush brush, TextAlignment align, FontStyle style = FontStyle.Normal)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, style, FontWeight.SemiBold), size, brush);
        var px = align switch
        {
            TextAlignment.Center => x - ft.Width / 2,
            TextAlignment.Right => x - ft.Width,
            _ => x
        };
        ctx.DrawText(ft, new Point(px, y));
    }

    private static void DrawReadout(DrawingContext ctx, Size size, string text)
        => DrawText(ctx, text, 18, size.Height - 24, 11,
            new SolidColorBrush(Color.FromArgb(150, 160, 190, 230)), TextAlignment.Left);

    private static double Norm(double deg)
    {
        var v = deg % 360;
        if (v > 180) v -= 360;
        if (v < -180) v += 360;
        return Math.Abs(v);
    }

    private static double[]? MidPoint(List<double[]> line)
    {
        for (var i = line.Count / 2; i < line.Count; i++)
            if (line[i].Length >= 2) return line[i];
        return line.Count > 0 && line[0].Length >= 2 ? line[0] : null;
    }

    private static string Pick(Dictionary<string, string>? dict, string fallback)
    {
        if (dict == null || dict.Count == 0) return fallback;
        foreach (var key in new[] { "zh-CN", "en" })
            if (dict.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)) return v;
        foreach (var kv in dict)
            if (!string.IsNullOrWhiteSpace(kv.Value)) return kv.Value;
        return fallback;
    }

    private static double[]? Centroid(List<List<double[]>> lines)
    {
        double sx = 0, sy = 0, sz = 0;
        var n = 0;
        foreach (var l in lines)
            foreach (var p in l)
            {
                if (p.Length < 2) continue;
                var v = Geometry.LatLonToUnit(p[1], p[0]);
                sx += v.X; sy += v.Y; sz += v.Z; n++;
            }
        if (n == 0) return null;
        var len = Math.Sqrt(sx * sx + sy * sy + sz * sz);
        if (len < 1e-9) return null;
        return new[]
        {
            Math.Atan2(sx / len, sz / len) * 180.0 / Math.PI,
            Math.Asin(Math.Clamp(sy / len, -1, 1)) * 180.0 / Math.PI
        };
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using EarthModelStudio.ModelSdk;

namespace NeoRF.EarthModels;

/// <summary>
/// 地球地理模型画布：**纯线稿**球面，不填色。
/// 三个图层可独立开关：五带 / 大洲大洋轮廓 / 板块与洲界。
/// 拖拽转动视角，滚轮缩放。
/// </summary>
public sealed class GeographyView : Control
{
    private readonly GeoScene _scene;
    private readonly GeoData _coast;
    private readonly PlateData _plates;
    private readonly BorderData _borders;
    private readonly LabelData _labels;

    // 视角
    private double _yawDeg = 10;
    private double _pitchDeg = 18;
    private double _zoom = 1.0;
    private Point _lastPointer;
    private bool _dragging;

    // 图层开关
    private bool _showZones = true;
    private bool _showCoast = true;
    private bool _showPlates;
    private bool _showBorders;
    private bool _showLabels = true;

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
        ClipToBounds = true;
        Focusable = true;
    }

    public string CurrentLayerName => _showZones ? "zones"
        : _showPlates ? "plates"
        : _showBorders ? "borders"
        : "coast";

    public bool ShowZones { get => _showZones; set { _showZones = value; InvalidateVisual(); StateChanged?.Invoke(); } }
    public bool ShowCoast { get => _showCoast; set { _showCoast = value; InvalidateVisual(); StateChanged?.Invoke(); } }
    public bool ShowPlates { get => _showPlates; set { _showPlates = value; InvalidateVisual(); StateChanged?.Invoke(); } }
    public bool ShowBorders { get => _showBorders; set { _showBorders = value; InvalidateVisual(); StateChanged?.Invoke(); } }
    public bool ShowLabels { get => _showLabels; set { _showLabels = value; InvalidateVisual(); StateChanged?.Invoke(); } }

    public void ResetView()
    {
        _yawDeg = 10;
        _pitchDeg = 18;
        _zoom = 1.0;
        InvalidateVisual();
        StateChanged?.Invoke();
    }

    public void SetView(double yaw, double pitch, double zoom = 1.0)
    {
        _yawDeg = yaw;
        _pitchDeg = pitch;
        _zoom = zoom;
        InvalidateVisual();
        StateChanged?.Invoke();
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
        if (!_dragging) return;
        var p = e.GetPosition(this);
        var dx = p.X - _lastPointer.X;
        var dy = p.Y - _lastPointer.Y;
        _lastPointer = p;
        // 跟手拖动：像素位移换算成角度
        _yawDeg -= dx * 0.42;
        _pitchDeg = Math.Clamp(_pitchDeg + dy * 0.42, -85, 85);
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
        InvalidateVisual();
        StateChanged?.Invoke();
        e.Handled = true;
    }

    // ---------------------------------------------------------------- 渲染
    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        if (size.Width < 40 || size.Height < 40) return;

        DrawBackground(ctx, size);

        var cx = (float)(size.Width * 0.5);
        var cy = (float)(size.Height * 0.52);
        var radius = (float)(Math.Min(size.Width, size.Height) * 0.44 * _zoom);
        if (radius < 8) return;

        // 相机：先按俯仰转 X，再按偏航转 Y
        var cam = Matrix4x4.CreateRotationX((float)(-_pitchDeg * Math.PI / 180.0));
        var planet = Matrix4x4.CreateRotationY((float)(_yawDeg * Math.PI / 180.0));
        var combined = Matrix4x4.Multiply(planet, cam);

        DrawSphereDisc(ctx, cx, cy, radius);

        if (_showZones) DrawClimateZones(ctx, combined, cx, cy, radius);
        if (_showCoast) DrawCoastlines(ctx, combined, cx, cy, radius);
        if (_showPlates) DrawPlates(ctx, combined, cx, cy, radius);
        if (_showBorders) DrawBorders(ctx, combined, cx, cy, radius);
        if (_showLabels) DrawLabels(ctx, combined, cx, cy, radius);

        DrawCompoundSphereOutLine(ctx, cx, cy, radius);
        DrawHint(ctx, size, combined, cx, cy, radius);
    }

    private static readonly IBrush BgTop = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#0B1726"), 0),
            new GradientStop(Color.Parse("#060D18"), 1)
        }
    };

    private void DrawBackground(DrawingContext ctx, Size size)
        => ctx.FillRectangle(BgTop, new Rect(size));

    /// <summary>球体本身：极暗的海水底，只为了让白色轮廓看得清。</summary>
    private static void DrawSphereDisc(DrawingContext ctx, float cx, float cy, float r)
    {
        var disc = new RadialGradientBrush
        {
            Center = new RelativePoint(0.36, 0.32, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.36, 0.32, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.85, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.85, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse("#12314F"), 0),
                new GradientStop(Color.Parse("#0A1E33"), 0.62),
                new GradientStop(Color.Parse("#061120"), 1)
            }
        };
        ctx.DrawEllipse(disc, null, new Point(cx, cy), r, r);
    }

    private static void DrawCompoundSphereOutLine(DrawingContext ctx, float cx, float cy, float r)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(190, 150, 200, 255)), 1.4);
        ctx.DrawEllipse(null, pen, new Point(cx, cy), r, r);
    }

    // ---- 五带 ----
    private void DrawClimateZones(DrawingContext ctx, Matrix4x4 combined, float cx, float cy, float r)
    {
        foreach (var zone in _scene.Zones)
        {
            // 每个带用半透明色带填充 + 边界纬线
            var color = ZoneFillColor(zone.Id);
            FillLatBand(ctx, combined, cx, cy, r, zone.LatMin, zone.LatMax, color);

            foreach (var lat in new[] { zone.LatMin, zone.LatMax })
            {
                var pen = Math.Abs(Math.Abs(lat) - _scene.TropicDeg) < 0.5 ? TropicPen
                    : Math.Abs(Math.Abs(lat) - (90 - _scene.TropicDeg)) < 0.5 ? PolarPen
                    : ZoneEdgePen;
                DrawParallel(ctx, combined, cx, cy, r, lat, pen);
            }
        }
        // 赤道独立强调
        var eqPen = new Pen(new SolidColorBrush(Color.FromArgb(235, 120, 220, 255)), 1.8,
            new DashStyle(new double[] { 6, 4 }, 0));
        DrawParallel(ctx, combined, cx, cy, r, 0, eqPen);
    }

    /// <summary>把纬度带的球面片填成半透明色（只填可见半球）。</summary>
    private static void FillLatBand(DrawingContext ctx, Matrix4x4 combined, float cx, float cy, float r,
        double latMin, double latMax, IBrush brush)
    {
        // 用「纬线环」的投影轮廓近似填充：采样一圈点拼成多边形
        const int n = 96;
        for (var band = 0; band < 2; band++)
        {
            var lat = band == 0 ? latMin : latMax;
            var pts = new List<Vector2>(n);
            for (var i = 0; i <= n; i++)
            {
                var lon = -180.0 + 360.0 * i / n;
                var p = Geometry.ToCamera(Geometry.LatLonToUnit(lat, lon),
                    Matrix4x4.Identity, combined);
                if (p.Z <= 0) continue;
                pts.Add(Geometry.Project(p, r, cx, cy));
            }
            if (pts.Count < 3) continue;
            // 只画这段环带的两条边界，中间填充交给 DrawParallel 视觉上已足够
        }

        // 实际填充：沿经线密集采样形成条带多边形
        const int steps = 60;
        var lons = new double[steps + 1];
        for (var i = 0; i <= steps; i++) lons[i] = -180.0 + 360.0 * i / steps;

        var top = new List<Vector2>();
        var bottom = new List<Vector2>();
        for (var i = 0; i <= steps; i++)
        {
            var pTop = Geometry.ToCamera(Geometry.LatLonToUnit(latMax, lons[i]),
                Matrix4x4.Identity, combined);
            var pBot = Geometry.ToCamera(Geometry.LatLonToUnit(latMin, lons[i]),
                Matrix4x4.Identity, combined);
            if (pTop.Z <= 0 || pBot.Z <= 0) continue;
            top.Add(Geometry.Project(pTop, r, cx, cy));
            bottom.Add(Geometry.Project(pBot, r, cx, cy));
        }
        if (top.Count < 3 || bottom.Count < 3) return;

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(Pt(top[0]), true);
            for (var i = 1; i < top.Count; i++) g.LineTo(Pt(top[i]));
            for (var i = bottom.Count - 1; i >= 0; i--) g.LineTo(Pt(bottom[i]));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(brush, null, geo);
    }

    private static IBrush ZoneFillColor(string id) => id switch
    {
        "torrid-north" or "torrid-south" => new SolidColorBrush(Color.FromArgb(30, 255, 168, 76)),
        "north-temperate" or "south-temperate" => new SolidColorBrush(Color.FromArgb(30, 110, 210, 140)),
        _ => new SolidColorBrush(Color.FromArgb(34, 150, 200, 255))
    };

    private static void DrawParallel(DrawingContext ctx, Matrix4x4 combined, float cx, float cy, float r,
        double lat, IPen pen)
    {
        var pts = new List<double[]>(kParallelSamples);
        for (var i = 0; i < kParallelSamples; i++)
            pts.Add(new[] { -180.0 + 360.0 * i / (kParallelSamples - 1), lat });
        DrawGeoPolyline(ctx, pts, combined, cx, cy, r, pen, closed: false);
    }

    private const int kParallelSamples = 145;

    // ---- 海岸线 ----
    private void DrawCoastlines(DrawingContext ctx, Matrix4x4 combined, float cx, float cy, float r)
    {
        foreach (var line in _coast.Lines)
            DrawGeoPolyline(ctx, line, combined, cx, cy, r, CoastPen, closed: true);
    }

    // ---- 板块边界 ----
    private void DrawPlates(DrawingContext ctx, Matrix4x4 combined, float cx, float cy, float r)
    {
        foreach (var plate in _plates.Plates)
            foreach (var line in plate.Lines)
                DrawGeoPolyline(ctx, line, combined, cx, cy, r, PlatePen, closed: true);

        if (!_showLabels) return;
        foreach (var plate in _plates.Plates)
        {
            var center = Centroid(plate.Lines);
            if (center == null) continue;
            var cam = Geometry.ToCamera(Geometry.LatLonToUnit(center[1], center[0]),
                Matrix4x4.Identity, combined);
            if (cam.Z <= 0.18f) continue;
            var pos = Geometry.Project(cam, r, cx, cy);
            var name = Pick(plate.NameI18n, plate.Id);
            DrawText(ctx, name, pos.X, pos.Y, 11, PlateBrush, TextAlignment.Center);
        }
    }

    // ---- 洲界 ----
    private void DrawBorders(DrawingContext ctx, Matrix4x4 combined, float cx, float cy, float r)
    {
        foreach (var b in _borders.Borders)
        {
            foreach (var line in b.Lines)
                DrawGeoPolyline(ctx, line, combined, cx, cy, r, BorderPen, closed: false);

            // 标志点：取线中点
            if (b.Lines.Count == 0 || b.Lines[0].Count == 0) continue;
            var mid = b.Lines[0][b.Lines[0].Count / 2];
            if (mid.Length < 2) continue;
            var cam = Geometry.ToCamera(Geometry.LatLonToUnit(mid[1], mid[0]),
                Matrix4x4.Identity, combined);
            if (cam.Z <= 0.05f) continue;
            var pos = Geometry.Project(cam, r, cx, cy);
            DrawDot(ctx, pos, 3.2, BorderBrush);
            if (_showLabels)
                DrawText(ctx, Pick(b.NameI18n, b.Name), pos.X + 8, pos.Y - 6, 10.5, BorderBrush, TextAlignment.Left);
        }
    }

    // ---- 七大洲四大洋标签 ----
    private void DrawLabels(DrawingContext ctx, Matrix4x4 combined, float cx, float cy, float r)
    {
        foreach (var label in _labels.Labels)
        {
            var cam = Geometry.ToCamera(Geometry.LatLonToUnit(label.Lat, label.Lon),
                Matrix4x4.Identity, combined);
            if (cam.Z <= 0.12f) continue;
            var pos = Geometry.Project(cam, r, cx, cy);
            var isOcean = label.Kind == "ocean";
            var brush = isOcean ? OceanBrush : ContinentBrush;
            DrawText(ctx, Pick(label.NameI18n, ""), pos.X, pos.Y,
                isOcean ? 11.5 : 12.5, brush, TextAlignment.Center,
                isOcean ? FontStyle.Italic : FontStyle.Normal);
        }
    }

    // ---- 通用：画一条经纬度折线（自动裁掉背面）----
    private static void DrawGeoPolyline(DrawingContext ctx, IReadOnlyList<double[]> latLon,
        Matrix4x4 combined, float cx, float cy, float r, IPen pen, bool closed)
    {
        var segments = Geometry.ProjectPolyline(latLon, Matrix4x4.Identity, combined, r, cx, cy, closed);
        foreach (var seg in segments)
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

    // ---------------------------------------------------------------- 画笔
    private static readonly IPen CoastPen = new Pen(new SolidColorBrush(Color.FromArgb(250, 236, 246, 255)), 1.75)
    { LineJoin = PenLineJoin.Round, LineCap = PenLineCap.Round };

    private static readonly IPen PlatePen = new Pen(new SolidColorBrush(Color.FromArgb(210, 255, 138, 92)), 1.5,
        new DashStyle(new double[] { 7, 4 }, 0));

    private static readonly IPen BorderPen = new Pen(new SolidColorBrush(Color.FromArgb(225, 255, 214, 110)), 1.35,
        new DashStyle(new double[] { 3, 3 }, 0));

    private static readonly IPen TropicPen = new Pen(new SolidColorBrush(Color.FromArgb(215, 255, 190, 100)), 1.3);
    private static readonly IPen PolarPen = new Pen(new SolidColorBrush(Color.FromArgb(205, 150, 205, 255)), 1.3);
    private static readonly IPen ZoneEdgePen = new Pen(new SolidColorBrush(Color.FromArgb(120, 190, 220, 255)), 0.9);

    private static readonly IBrush ContinentBrush = new SolidColorBrush(Color.FromArgb(245, 232, 242, 255));
    private static readonly IBrush OceanBrush = new SolidColorBrush(Color.FromArgb(215, 130, 190, 245));
    private static readonly IBrush PlateBrush = new SolidColorBrush(Color.FromArgb(235, 255, 158, 118));
    private static readonly IBrush BorderBrush = new SolidColorBrush(Color.FromArgb(240, 255, 220, 128));

    // ---------------------------------------------------------------- 文字
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

    private static Point Pt(Vector2 v) => new(v.X, v.Y);

    private static void DrawDot(DrawingContext ctx, Vector2 p, double radius, IBrush brush)
        => ctx.DrawEllipse(brush, null, Pt(p), radius, radius);

    private void DrawHint(DrawingContext ctx, Size size, Matrix4x4 combined, float cx, float cy, float r)
    {
        // 左下角经纬读数
        var lat = _pitchDeg.ToString("0", CultureInfo.InvariantCulture);
        var lon = NormalizeLon(_yawDeg).ToString("0", CultureInfo.InvariantCulture);
        var text = $"视角 {lat}° / {lon}°";
        DrawText(ctx, text, 18, size.Height - 26, 11,
            new SolidColorBrush(Color.FromArgb(140, 160, 190, 230)), TextAlignment.Left);
    }

    private static double NormalizeLon(double deg)
    {
        var v = deg % 360;
        if (v > 180) v -= 360;
        if (v < -180) v += 360;
        return v;
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
        var pts = new List<double[]>();
        foreach (var l in lines)
            foreach (var p in l)
                if (p.Length >= 2) pts.Add(p);
        if (pts.Count == 0) return null;

        // 用球面平均避免经度跨越 ±180 出错
        double sx = 0, sy = 0, sz = 0;
        foreach (var p in pts)
        {
            var v = Geometry.LatLonToUnit(p[1], p[0]);
            sx += v.X; sy += v.Y; sz += v.Z;
        }
        var n = Math.Sqrt(sx * sx + sy * sy + sz * sz);
        if (n < 1e-9) return null;
        sy /= n; sx /= n; sz /= n;
        var lat = Math.Asin(Math.Clamp(sy, -1, 1)) * 180.0 / Math.PI;
        var lon = Math.Atan2(sx, sz) * 180.0 / Math.PI;
        return new[] { lon, lat };
    }
}

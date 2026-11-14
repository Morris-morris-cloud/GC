using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using GC.ModelSdk;

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
    private readonly ContinentData _continents;
    private readonly BorderData _borders;
    private readonly LabelData _labels;

    private double _yawDeg = 10;
    private double _pitchDeg = 15;
    private double _zoom = 1.0;
    private double _zoomTarget = 1.0;
    private Avalonia.Threading.DispatcherTimer? _zoomTimer;
    private Point _lastPointer;
    private bool _dragging;

    private bool _showZones = true;
    private bool _showCoast = true;
    private bool _showPlates;
    private bool _showContinents = true;
    private bool _showBorders = true;
    private bool _showLabels = true;
    private GeoMode _mode = GeoMode.Sphere3D;

    // ── 平面视图几何缓存：五大陆有 1.3 万点，每帧重建 StreamGeometry 会明显掉帧。
    //    尺寸/缩放不变时复用同一份几何，只重画笔刷。
    private readonly Dictionary<string, Avalonia.Media.Geometry> _flatCache = new();
    private double _flatCacheW = -1, _flatCacheH = -1;
    private Avalonia.Media.Geometry? _coastGeo;
    private double _coastCacheW = -1, _coastCacheH = -1;
    // 单位球坐标缓存：三角运算只算一次，之后每帧只做矩阵乘 + 投影
    private readonly Dictionary<string, (Vector3[] Pts, bool[] Valid)> _unitCache = new();

    // ── 3D 视图成品几何缓存：旋转/缩放/尺寸都不变时直接复用上一帧的几何，
    //    避免每帧重建 2.6 万点的 StreamGeometry 并重新提交给 Skia。
    //    用户静止观察时这是绝大多数情况；拖动时按角度量化命中，转动依然跟手。
    private Avalonia.Media.Geometry? _s3dCoast, _s3dCont;
    private readonly Dictionary<string, Avalonia.Media.Geometry> _s3dBands = new();
    private readonly Dictionary<string, Avalonia.Media.Geometry> _s3dPlates = new();
    private string _s3dKey = "";

    public event Action? StateChanged;

    public GeographyView(IModelContext context, GeoScene scene, GeoData coast,
        PlateData plates, ContinentData continents, BorderData borders, LabelData labels)
    {
        _scene = scene;
        _coast = coast;
        _plates = plates;
        _continents = continents;
        _borders = borders;
        _labels = labels;
        ClipToBounds = true;
        Focusable = true;
    }

    public bool ShowZones { get => _showZones; set { _showZones = value; Redraw(); } }
    public bool ShowCoast { get => _showCoast; set { _showCoast = value; Redraw(); } }
    public bool ShowPlates { get => _showPlates; set { _showPlates = value; Redraw(); } }
    public bool ShowContinents { get => _showContinents; set { _showContinents = value; Redraw(); } }
    public bool ShowBorders { get => _showBorders; set { _showBorders = value; Redraw(); } }
    public bool ShowLabels { get => _showLabels; set { _showLabels = value; Redraw(); } }
    public GeoMode Mode { get => _mode; set { _mode = value; InvalidateVisual(); StateChanged?.Invoke(); } }

    /// <summary>缩放倍数（0.55–4.0）。文字大小跟随缩放变化。</summary>
    /// <summary>
    /// 缩放。写入的是**目标值**，由动画计时器平滑逼近，
    /// 直接跳变会让滚轮缩放显得生硬。
    /// </summary>
    public double Zoom
    {
        get => _zoomTarget;
        set
        {
            _zoomTarget = Math.Clamp(value, 0.55, 4.0);
            EnsureAnimating();
        }
    }

    /// <summary>立即生效（贴图生成、复位等场景不该有过渡）。</summary>
    public void SetZoomImmediate(double value)
    {
        _zoomTarget = Math.Clamp(value, 0.55, 4.0);
        _zoom = _zoomTarget;
        Redraw();
    }

    private void Redraw() { InvalidateVisual(); StateChanged?.Invoke(); }

    /// <summary>
    /// 缩放动画：60Hz 逼近目标值，差值小于阈值即停，
    /// 避免空转白耗 CPU（一体机上尤其明显）。
    /// </summary>
    private void EnsureAnimating()
    {
        _zoomTimer ??= new Avalonia.Threading.DispatcherTimer(
            TimeSpan.FromMilliseconds(16), Avalonia.Threading.DispatcherPriority.Render, (_, _) => StepZoom());
        if (!_zoomTimer.IsEnabled) _zoomTimer.Start();
    }

    private void StepZoom()
    {
        var diff = _zoomTarget - _zoom;
        if (Math.Abs(diff) < 0.0015)
        {
            _zoom = _zoomTarget;
            _zoomTimer?.Stop();
            Redraw();
            return;
        }
        // 指数逼近：手感接近系统级平滑缩放
        _zoom += diff * 0.28;
        Redraw();
    }

    /// <summary>释放动画计时器，避免窗口关闭后还在跑。</summary>
    public void StopAnimation() => _zoomTimer?.Stop();

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
        // 走 Zoom 属性（带平滑动画），不要在事件里直接改 _zoom
        Zoom = _zoomTarget * (1 + e.Delta.Y * 0.12);
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

        // 成品几何缓存 key：旋转 / 缩放 / 尺寸 / 图层只要有一个变了才重建几何。
        // 静止观察时命中率 100%，拖动时按 0.5° 量化，仍然跟手。
        var key = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{Math.Round(_yawDeg * 2) / 2}|{Math.Round(_pitchDeg * 2) / 2}|{Math.Round(_zoom, 3)}|" +
            $"{Math.Round(size.Width)}|{Math.Round(size.Height)}|" +
            $"{(_showZones ? 1 : 0)}{(_showCoast ? 1 : 0)}{(_showPlates ? 1 : 0)}{(_showContinents ? 1 : 0)}");
        var rebuild = key != _s3dKey;
        if (rebuild)
        {
            _s3dKey = key;
            _s3dCoast = null; _s3dCont = null;
            _s3dBands.Clear();
            _s3dPlates.Clear();
        }

        if (_showZones)
        {
            // 每个温度带一种颜色，所以按带分别缓存一份几何
            foreach (var zone in _scene.Zones)
            {
                var zkey = "band:" + zone.Id;
                if (rebuild || !_s3dBands.TryGetValue(zkey, out var band))
                {
                    var merged = new StreamGeometry();
                    using (var g = merged.Open())
                        AppendBand(g, m, cx, cy, r, zone.LatMin, zone.LatMax);
                    band = merged;
                    _s3dBands[zkey] = band;
                }
                ctx.DrawGeometry(ZoneFill(zone.Id), null, band);
            }

            // 边界线（赤道/回归线/极圈）随旋转变化，直接画
            DrawZoneLines3D(ctx, m, cx, cy, r);
        }
        if (_showCoast)
        {
            if (rebuild || _s3dCoast == null) _s3dCoast = BuildCoast(m, cx, cy, r);
            ctx.DrawGeometry(null, CoastPen, _s3dCoast);
        }
        if (_showPlates) DrawPlates3D(ctx, m, cx, cy, r);
        if (_showContinents)
        {
            if (rebuild || _s3dCont == null) _s3dCont = BuildContinents(m, cx, cy, r);
            ctx.DrawGeometry(null, ContinentPen, _s3dCont);
        }
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

    // ---- 五带边界线：赤道 / 回归线 / 极圈（随旋转变化，每帧直接画）----
    private void DrawZoneLines3D(DrawingContext ctx, Matrix4x4 m, float cx, float cy, float r)
    {
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
    private static void DrawParallel3D(DrawingContext ctx, Matrix4x4 m, float cx, float cy, float r,
        double lat, IPen pen)
    {
        var pts = new List<double[]>(145);
        for (var i = 0; i < 145; i++) pts.Add(new[] { -180.0 + 360.0 * i / 144.0, lat });
        DrawGeo3D(ctx, pts, m, cx, cy, r, pen, false);
    }
    /// <summary>海岸线合并几何（供成品缓存使用）。</summary>
    private Avalonia.Media.Geometry BuildCoast(Matrix4x4 m, float cx, float cy, float r)
    {
        var merged = new StreamGeometry();
        using (var g = merged.Open())
        {
            for (var i = 0; i < _coast.Lines.Count; i++)
                AppendCached(g, "coast:" + i, _coast.Lines[i], m, r, cx, cy, true);
        }
        return merged;
    }

    /// <summary>五大陆合并几何（供成品缓存使用）。</summary>
    private Avalonia.Media.Geometry BuildContinents(Matrix4x4 m, float cx, float cy, float r)
    {
        var merged = new StreamGeometry();
        using (var g = merged.Open())
        {
            foreach (var c in _continents.Continents)
                for (var i = 0; i < c.Lines.Count; i++)
                    AppendCached(g, "cont:" + c.Id + ":" + i, c.Lines[i], m, r, cx, cy, true);
        }
        return merged;
    }

    /// <summary>把一个纬度带的可见格子追加到几何里（每格独立判断可见性）。</summary>
    private static void AppendBand(StreamGeometryContext g, Matrix4x4 m, float cx, float cy, float r,
        double latMin, double latMax)
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
            if (a.Z <= 0 || b.Z <= 0 || c.Z <= 0 || d.Z <= 0) continue;
            g.BeginFigure(Pt(Geometry.Project(a, r, cx, cy)), true);
            g.LineTo(Pt(Geometry.Project(b, r, cx, cy)));
            g.LineTo(Pt(Geometry.Project(c, r, cx, cy)));
            g.LineTo(Pt(Geometry.Project(d, r, cx, cy)));
            g.EndFigure(true);
        }
    }

    /// <summary>把一条缓存折线的所有可见弧段追加到同一个几何里。</summary>
    private void AppendCached(StreamGeometryContext g, string key, IReadOnlyList<double[]> latLon,
        Matrix4x4 m, float r, float cx, float cy, bool closed)
    {
        var entry = GetUnit(key, latLon);
        foreach (var seg in Geometry.ProjectUnitPolyline(entry.Pts, entry.Valid,
                     Matrix4x4.Identity, m, r, cx, cy, closed))
        {
            if (seg.Count < 2) continue;
            g.BeginFigure(Pt(seg[0]), false);
            for (var i = 1; i < seg.Count; i++) g.LineTo(Pt(seg[i]));
            g.EndFigure(false);
        }
    }

    private (Vector3[] Pts, bool[] Valid) GetUnit(string key, IReadOnlyList<double[]> latLon)
    {
        if (_unitCache.TryGetValue(key, out var entry)) return entry;
        var pts = new Vector3[latLon.Count];
        var valid = new bool[latLon.Count];
        for (var i = 0; i < latLon.Count; i++)
        {
            var p = latLon[i];
            if (p.Length >= 2)
            {
                pts[i] = Geometry.LatLonToUnit(p[1], p[0]);
                valid[i] = true;
            }
        }
        entry = (pts, valid);
        _unitCache[key] = entry;
        return entry;
    }

    private void DrawPlates3D(DrawingContext ctx, Matrix4x4 m, float cx, float cy, float r)
    {
        foreach (var p in _plates.Plates)
        {
            var pen = PlatePenFor(p.Id);
            var merged = new StreamGeometry();
            using (var g = merged.Open())
            {
                for (var i = 0; i < p.Lines.Count; i++)
                    AppendCached(g, "plate:" + p.Id + ":" + i, p.Lines[i], m, r, cx, cy, true);
            }
            ctx.DrawGeometry(null, pen, merged);
        }
        if (_showLabels) DrawPlateLabels3D(ctx, m, cx, cy, r);
    }

    private void DrawPlateLabels3D(DrawingContext ctx, Matrix4x4 m, float cx, float cy, float r)
    {
        foreach (var p in _plates.Plates)
        {
            var at = p.Label is { Length: >= 2 } ? p.Label : Centroid(p.Lines);
            if (at == null) continue;
            var cam = Geometry.ToCamera(Geometry.LatLonToUnit(at[1], at[0]), Matrix4x4.Identity, m);
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

    /// <summary>
    /// 缓存版绘制：单位球坐标按 key 缓存一次，之后每帧只做矩阵乘与投影。
    /// 海岸线/大陆共 2.6 万点，这是 3D 视图的主要开销来源。
    /// </summary>
    private void DrawCached3D(DrawingContext ctx, string key, IReadOnlyList<double[]> latLon,
        Matrix4x4 m, float cx, float cy, float r, IPen pen, bool closed)
    {
        var entry = GetUnit(key, latLon);
        foreach (var seg in Geometry.ProjectUnitPolyline(entry.Pts, entry.Valid,
                     Matrix4x4.Identity, m, r, cx, cy, closed))
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
            var cw = Math.Round(size.Width);
            var ch = Math.Round(size.Height);
            if (Math.Abs(cw - _coastCacheW) > 0.5 || Math.Abs(ch - _coastCacheH) > 0.5 || _coastGeo == null)
            {
                var merged = new StreamGeometry();
                using (var g = merged.Open())
                {
                    foreach (var line in _coast.Lines)
                    {
                        if (line.Count < 2) continue;
                        g.BeginFigure(P(line[0][0], line[0][1]), false);
                        for (var i = 1; i < line.Count; i++)
                        {
                            var p = line[i];
                            if (p.Length >= 2) g.LineTo(P(p[0], p[1]));
                        }
                        g.EndFigure(false);
                    }
                }
                _coastGeo = merged;
                _coastCacheW = cw; _coastCacheH = ch;
            }
            ctx.DrawGeometry(null, CoastPen, _coastGeo);
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
                var c = pl.Label is { Length: >= 2 } ? pl.Label : Centroid(pl.Lines);
                if (c == null) continue;
                var at = P(c[0], c[1]);
                DrawText(ctx, Pick(pl.NameI18n, pl.Id), at.X, at.Y, 10.5 * TextScale,
                    PlateBrushFor(pl.Id), TextAlignment.Center);
            }
        }

        // 五大陆
        if (_showContinents)
        {
            // 用缓存几何：尺寸不变时只画一次构建开销
            var fw = Math.Round(size.Width);
            var fh = Math.Round(size.Height);
            if (Math.Abs(fw - _flatCacheW) > 0.5 || Math.Abs(fh - _flatCacheH) > 0.5 || _flatCache.Count == 0)
            {
                _flatCache.Clear();
                _flatCacheW = fw; _flatCacheH = fh;
            }

            foreach (var c in _continents.Continents)
            {
                var key = "cont:" + c.Id;
                if (!_flatCache.TryGetValue(key, out var geo))
                {
                    var merged = new StreamGeometry();
                    using (var g = merged.Open())
                    {
                        foreach (var line in c.Lines)
                        {
                            if (line.Count < 2) continue;
                            g.BeginFigure(P(line[0][0], line[0][1]), false);
                            for (var i = 1; i < line.Count; i++)
                            {
                                var p = line[i];
                                if (p.Length >= 2) g.LineTo(P(p[0], p[1]));
                            }
                            g.EndFigure(false);
                        }
                    }
                    geo = merged;
                    _flatCache[key] = geo;
                }
                ctx.DrawGeometry(null, ContinentPen, geo);

                if (!_showLabels) continue;
                var a = c.Label is { Length: >= 2 } ? c.Label : Centroid(c.Lines);
                if (a == null) continue;
                var at = P(a[0], a[1]);
                DrawText(ctx, Pick(c.NameI18n, c.Id), at.X, at.Y, 12 * TextScale,
                    MainlandBrush, TextAlignment.Center);
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
            "南美-南极" => Color.FromRgb(96, 232, 222),   // 青绿（德雷克海峡）
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
        // 教材六大板块，每块一个颜色
        var c = id switch
        {
            "eurasian" => Color.FromRgb(110, 214, 230),      // 青
            "african" => Color.FromRgb(150, 230, 110),       // 绿
            "american" => Color.FromRgb(255, 176, 84),       // 橙
            "pacific" => Color.FromRgb(255, 110, 110),       // 红
            "indian-ocean" => Color.FromRgb(126, 160, 255),  // 蓝紫
            "antarctic" => Color.FromRgb(208, 190, 255),     // 淡紫
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

    /// <summary>五大陆轮廓：金黄色实线，与板块虚线明显区分。</summary>
    private static readonly IPen ContinentPen = new Pen(new SolidColorBrush(Color.FromArgb(240, 255, 210, 74)), 2.0)
    { LineJoin = PenLineJoin.Round, LineCap = PenLineCap.Round };
    private static readonly IBrush MainlandBrush = new SolidColorBrush(Color.FromRgb(255, 218, 110));

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

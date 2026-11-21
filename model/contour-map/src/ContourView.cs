using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace GC.ContourMap;

/// <summary>观察视角：立体（可旋转）或俯视（等高线图）。</summary>
public enum ViewMode
{
    Perspective,
    Top
}

/// <summary>实验流程的四个步骤，对应 HTML 课件的四条流程线。</summary>
public enum ExperimentStep
{
    Cutting = 0,     // 等高切割：水平面下降与山体相交
    Lines = 1,       // 形成交线：多层交线共同描述起伏
    Projection = 2,  // 投影成图：交线垂直投影到同一平面
    Density = 3      // 疏密判坡：等高线疏密反映坡度陡缓
}

/// <summary>
/// 等高线教学画布（可交互 3D 版）。
///
/// · 立体视角：软件光栅化的三维地形，鼠标拖拽旋转、滚轮缩放；
///   水平切面沿高度下降 → 交线形成 → 垂直投影 → 疏密判坡，四步动画完整复刻 HTML 课件。
/// · 俯视视角：分层设色等高线图 + 山峰/河流/陡崖符号 + 剖面图 + 图例/比例尺/指北针。
/// · 两视角鼠标悬停同步显示海拔与地形部位。
/// </summary>
public sealed class ContourView : Control
{
    // ── 配色
    private static readonly Color CInk = Color.Parse("#111A2E");
    private static readonly Color CLine = Color.Parse("#DCE7F8");
    private static readonly Color CContour = Color.Parse("#3B2A18");
    private static readonly Color CContourIndex = Color.Parse("#1F1508");
    private static readonly Color CAccent = Color.Parse("#2F6BFF");
    private static readonly Color CCutting = Color.Parse("#1FD1E8");
    private static readonly Color CProjection = Color.Parse("#7C5CFF");

    private static readonly Typeface Font = new("Noto Sans CJK SC, Microsoft YaHei, Arial");

    public TerrainField Field { get; private set; }

    private readonly List<(float Level, List<List<(double U, double V)>> Lines)> _contours = new();
    private List<float> _levels = new();

    public Func<string, string> T { get; set; } = k => k;

    // ── 相机
    private double _yawDeg = 34.0;
    private double _pitchDeg = 36.0;
    private double _zoom = 1.0;
    private double _camS = 1, _camCx = 0, _camCy = 0;   // UpdateCamera 算出的屏幕变换
    private bool _dragging;
    private Point _lastPointer;

    // ── 交互 / 显示状态
    private double _hoverU = 0.5, _hoverV = 0.5;
    private bool _hasHover;
    private Point _pointerPosition;
    private bool _showContours = true;
    private bool _showLabels = true;
    private bool _colored = true;

    // ── 实验动画状态
    private ExperimentStep _step = ExperimentStep.Cutting;
    private double _anim = 0.0;              // 当前步骤内进度 0..1
    private bool _playing;
    private DispatcherTimer? _timer;
    private bool _disposed;

    // 供悬停命中测试的投影四边形（屏幕坐标 + 对应 u/v）
    private readonly List<(Point P0, Point P1, Point P2, Point P3, double U, double V)> _hitQuads = new();

    // 面片着色画刷缓存：每帧 5000 个面如果都 new 一个 brush，光分配就吃掉大半帧时间
    private readonly Dictionary<int, SolidColorBrush> _brushCache = new();

    private SolidColorBrush ShadeBrush(int r, int g, int b)
    {
        r = Math.Clamp(r / 4 * 4, 0, 252);
        g = Math.Clamp(g / 4 * 4, 0, 252);
        b = Math.Clamp(b / 4 * 4, 0, 252);
        var key = (r << 16) | (g << 8) | b;
        if (_brushCache.TryGetValue(key, out var brush)) return brush;
        brush = new SolidColorBrush(Color.FromRgb((byte)r, (byte)g, (byte)b));
        _brushCache[key] = brush;
        return brush;
    }

    public bool ShowContours { get => _showContours; set => SetFlag(ref _showContours, value); }
    public bool ShowLabels { get => _showLabels; set => SetFlag(ref _showLabels, value); }
    public bool Colored { get => _colored; set => SetFlag(ref _colored, value); }

    public ViewMode Mode { get; private set; } = ViewMode.Perspective;
    public ExperimentStep Step => _step;
    public bool IsPlaying => _playing;
    public double AnimationProgress => _anim;

    private void SetFlag(ref bool slot, bool value)
    {
        if (slot == value) return;
        slot = value;
        InvalidateVisual();
    }

    /// <summary>当前悬停点信息。</summary>
    public event Action<double, double, float, TerrainFeature>? HoverChanged;
    /// <summary>步骤切换（供界面同步流程条与文案）。</summary>
    public event Action<ExperimentStep>? StepChanged;
    /// <summary>播放进度变化（0..1，供界面显示进度条）。</summary>
    public event Action<ExperimentStep, double>? ProgressChanged;
    /// <summary>整套动画播放完成。</summary>
    public event Action? PlaybackFinished;

    public ContourView(TerrainField field, Func<string, string> text)
    {
        Field = field;
        T = text;
        ClipToBounds = true;
        RebuildContours();

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(28), DispatcherPriority.Render, OnTick);
    }

    /// <summary>释放计时器，避免其持有控件导致模型窗口无法卸载。</summary>
    public void DisposeView()
    {
        if (_disposed) return;
        _disposed = true;
        if (_timer != null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }
    }

    private void RebuildContours()
    {
        _levels = Field.ContourLevels();
        _contours.Clear();
        foreach (var lv in _levels)
            _contours.Add((lv, Field.ExtractContour(lv)));
    }

    /// <summary>切换到另一块地形（重新提取等高线）。</summary>
    public void SetField(TerrainField field)
    {
        Field = field;
        RebuildContours();
        InvalidateVisual();
    }

    public void SetMode(ViewMode mode)
    {
        if (Mode == mode) return;
        Mode = mode;
        InvalidateVisual();
    }

    public void ToggleMode() => SetMode(Mode == ViewMode.Top ? ViewMode.Perspective : ViewMode.Top);

    public void SetStep(ExperimentStep step)
    {
        _step = step;
        _anim = 0;
        StepChanged?.Invoke(_step);
        ProgressChanged?.Invoke(_step, _anim);
        InvalidateVisual();
    }

    /// <summary>播放/暂停切换。默认从当前步骤继续。</summary>
    public void TogglePlay()
    {
        if (_playing) Pause();
        else Play();
    }

    public void Play()
    {
        if (_playing) return;
        // 已经播到最后一帧时再按播放，从头开始，避免“点了没反应”
        if (_step == ExperimentStep.Density && _anim >= 1.0)
        {
            _step = ExperimentStep.Cutting;
            _anim = 0;
            StepChanged?.Invoke(_step);
        }
        _playing = true;
        _timer?.Start();
        InvalidateVisual();
    }

    public void Pause()
    {
        if (!_playing) return;
        _playing = false;
        _timer?.Stop();
        InvalidateVisual();
    }

    /// <summary>从头播放整套流程。</summary>
    public void PlayFromStart()
    {
        _step = ExperimentStep.Cutting;
        _anim = 0;
        StepChanged?.Invoke(_step);
        Play();
    }

    /// <summary>直接展示完整等高线（地貌专题用）：停在“形成交线”步骤的终点，不切平山体。</summary>
    public void ShowContourPattern()
    {
        _playing = false;
        _timer?.Stop();
        _step = ExperimentStep.Lines;
        _anim = 1.0;
        StepChanged?.Invoke(_step);
        ProgressChanged?.Invoke(_step, _anim);
        InvalidateVisual();
    }

    public void Reset()
    {
        Pause();
        _step = ExperimentStep.Cutting;
        _anim = 0;
        StepChanged?.Invoke(_step);
        ProgressChanged?.Invoke(_step, _anim);
        InvalidateVisual();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_playing || _disposed) return;
        _anim += 0.0105;                       // 每步约 2.7 秒
        if (_anim >= 1.0)
        {
            _anim = 0;
            var next = (int)_step + 1;
            if (next > 3)
            {
                _playing = false;
                _timer?.Stop();
                _anim = 1;
                ProgressChanged?.Invoke(_step, _anim);
                PlaybackFinished?.Invoke();
                InvalidateVisual();
                return;
            }
            _step = (ExperimentStep)next;
            StepChanged?.Invoke(_step);
        }
        ProgressChanged?.Invoke(_step, _anim);
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- 交互
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Mode != ViewMode.Perspective) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _lastPointer = e.GetPosition(this);
        _dragging = true;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);

        if (_dragging && Mode == ViewMode.Perspective)
        {
            var dx = p.X - _lastPointer.X;
            var dy = p.Y - _lastPointer.Y;
            _lastPointer = p;
            _yawDeg += dx * 0.45;
            _pitchDeg = Math.Clamp(_pitchDeg + dy * 0.35, 12, 88);
            InvalidateVisual();
            return;
        }

        UpdateHover(p);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _zoom = Math.Clamp(_zoom * (1 + e.Delta.Y * 0.12), 0.55, 2.4);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!_hasHover) return;
        _hasHover = false;
        InvalidateVisual();
    }

    private void UpdateHover(Point p)
    {
        var size = Bounds.Size;
        if (size.Width < 40 || size.Height < 40) return;

        double u = -1, v = -1;
        if (Mode == ViewMode.Top)
        {
            var map = TopMapRect(size);
            if (map.Contains(p))
            {
                u = (p.X - map.X) / map.Width;
                v = (p.Y - map.Y) / map.Height;
            }
        }
        else
        {
            // 在已渲染的投影四边形中命中测试，旋转后依然准确
            for (var i = _hitQuads.Count - 1; i >= 0; i--)
            {
                var q = _hitQuads[i];
                if (InQuad(p, q.P0, q.P1, q.P2, q.P3))
                {
                    u = q.U; v = q.V;
                    break;
                }
            }
        }

        if (u < 0 || u > 1 || v < 0 || v > 1)
        {
            if (_hasHover) { _hasHover = false; InvalidateVisual(); }
            return;
        }

        _hoverU = u; _hoverV = v; _hasHover = true;
        _pointerPosition = p;
        var h = Field.SampleSmooth(u, v);
        var f = TerrainAnalyzer.Classify(Field, u, v);
        HoverChanged?.Invoke(u, v, h, f);
        InvalidateVisual();
    }

    private static bool InQuad(Point p, Point a, Point b, Point c, Point d)
        => InTri(p, a, b, c) || InTri(p, a, c, d);

    private static bool InTri(Point p, Point a, Point b, Point c)
    {
        double Sign(Point p1, Point p2, Point p3)
            => (p1.X - p3.X) * (p2.Y - p3.Y) - (p2.X - p3.X) * (p1.Y - p3.Y);
        var d1 = Sign(p, a, b);
        var d2 = Sign(p, b, c);
        var d3 = Sign(p, c, a);
        var neg = d1 < 0 || d2 < 0 || d3 < 0;
        var pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }

    // ---------------------------------------------------------------- 渲染
    public override void Render(DrawingContext dc)
    {
        var size = Bounds.Size;
        if (size.Width < 80 || size.Height < 80) return;
        dc.FillRectangle(new SolidColorBrush(Color.Parse("#0B1524")), new Rect(size));

        if (Mode == ViewMode.Top) RenderTop(dc, size);
        else RenderPerspective(dc, size);
    }

    // ================================================================ 立体视角

    /// <summary>把归一化地形坐标投影到屏幕，并给出深度（越大越靠近观察者）。</summary>
    private (Point Screen, double Depth) Project(double u, double v, double h, Size size)
    {
        var (x1, sy3, depth) = RotateProject(u, v, h);
        return (new Point(_camCx + x1 * _camS, _camCy + sy3 * _camS), depth);
    }

    /// <summary>绕竖轴旋转 + 俯仰，得到未缩放的屏幕横坐标与纵坐标。</summary>
    private (double X, double Y, double Depth) RotateProject(double u, double v, double h)
    {
        var wx = (u - 0.5) * 2.0;
        var wy = (v - 0.5) * 2.0;
        var span = Math.Max(1f, Field.MaxHeight - Field.MinHeight);
        var wz = (h - Field.MinHeight) / span * 1.15;   // 垂直夸张，凸显起伏

        var yaw = _yawDeg * Math.PI / 180.0;
        var pitch = _pitchDeg * Math.PI / 180.0;
        var x1 = wx * Math.Cos(yaw) - wy * Math.Sin(yaw);
        var y1 = wx * Math.Sin(yaw) + wy * Math.Cos(yaw);
        var sy3 = y1 * Math.Sin(pitch) - wz * Math.Cos(pitch);
        var depth = y1 * Math.Cos(pitch) + wz * Math.Sin(pitch);
        return (x1, sy3, depth);
    }

    /// <summary>
    /// 自动取景：把地形包围盒（八个角点）投影后缩放到画布内并居中。
    /// 这样无论旋转到哪个角度、换哪种地形，山体都铺满画面而不是缩在一角。
    /// </summary>
    private void UpdateCamera(Size size)
    {
        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;
        foreach (var h in new[] { (double)Field.MinHeight, Field.MaxHeight })
        foreach (var u in new[] { 0.0, 1.0 })
        foreach (var v in new[] { 0.0, 1.0 })
        {
            var (x, y, _) = RotateProject(u, v, h);
            if (x < minX) minX = x; if (x > maxX) maxX = x;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
        }

        var w = Math.Max(1e-6, maxX - minX);
        var hgt = Math.Max(1e-6, maxY - minY);
        var s = Math.Min(size.Width * 0.90 / w, size.Height * 0.90 / hgt) * _zoom;
        _camS = s;
        _camCx = size.Width / 2 - (minX + maxX) / 2 * s;
        _camCy = size.Height / 2 - (minY + maxY) / 2 * s;
    }

    private void RenderPerspective(DrawingContext dc, Size size)
    {
        _hitQuads.Clear();
        UpdateCamera(size);

        // 切割步骤：水平面以上的山体被削去（h' = min(h, 切面高度)），
        // 看到的是真实截面，而不是一张悬在半空的半透明板。
        var cutLevel = CurrentCutLevel;
        var cutting = _step == ExperimentStep.Cutting;
        var ceiling = cutting ? cutLevel : float.MaxValue;

        // 先画底座与四周侧壁，山体才有“厚度”，边缘不会悬空
        DrawBasePlate(dc, size);
        DrawSkirt(dc, size, ceiling);

        // 采样地形四边形，按深度从远到近绘制（画家算法）
        const int N = 62;
        var quads = new List<(Point P0, Point P1, Point P2, Point P3, double Depth, double H, double U, double V,
                              double GX, double GY)>();
        for (var j = 0; j < N; j++)
        for (var i = 0; i < N; i++)
        {
            var u0 = (double)i / N; var u1 = (double)(i + 1) / N;
            var v0 = (double)j / N; var v1 = (double)(j + 1) / N;
            var h00 = Field.SampleSmooth(u0, v0);
            var h10 = Field.SampleSmooth(u1, v0);
            var h11 = Field.SampleSmooth(u1, v1);
            var h01 = Field.SampleSmooth(u0, v1);
            if (cutting)
            {
                h00 = Math.Min(h00, cutLevel); h10 = Math.Min(h10, cutLevel);
                h11 = Math.Min(h11, cutLevel); h01 = Math.Min(h01, cutLevel);
            }
            var p00 = Project(u0, v0, h00, size).Screen;
            var p10 = Project(u1, v0, h10, size).Screen;
            var p11 = Project(u1, v1, h11, size).Screen;
            var p01 = Project(u0, v1, h01, size).Screen;
            var d = (Project(u0, v0, h00, size).Depth + Project(u1, v0, h10, size).Depth +
                     Project(u1, v1, h11, size).Depth + Project(u0, v1, h01, size).Depth) / 4.0;
            var hAvg = (h00 + h10 + h11 + h01) / 4f;
            // 坡度方向（用于光照）：h11 在东南角，所以 dx 向东、dy 向南
            var gx = ((h10 + h11) - (h00 + h01)) * 0.5;
            var gy = ((h01 + h11) - (h00 + h10)) * 0.5;
            quads.Add((p00, p10, p11, p01, d, hAvg, (u0 + u1) / 2, (v0 + v1) / 2, gx, gy));
        }
        quads.Sort((a, b) => a.Depth.CompareTo(b.Depth));

        var span = Math.Max(1f, Field.MaxHeight - Field.MinHeight);
        foreach (var q in quads)
        {
            var (cr, cg, cb) = Colored
                ? TerrainAnalyzer.ColorForHeightSmooth((float)q.H)
                : (0xF3, 0xF0, 0xE6);

            // 法线光照（西北主光 + 天顶补光）：坡面朝向决定明暗，山体才有立体感
            var nx = -q.GX / span * 2.2;
            var ny = -q.GY / span * 2.2;
            var nl = Math.Sqrt(nx * nx + ny * ny + 1.0);
            var lit = (nx * -0.55 + ny * -0.42 + 0.72) / nl;
            var shade = 0.80 + 0.26 * Math.Clamp(lit, 0, 1) + 0.08 * ((q.H - Field.MinHeight) / span - 0.5);
            shade = Math.Clamp(shade, 0.60, 1.10);
            var brush = ShadeBrush(
                (int)Math.Clamp(cr * shade, 0, 255),
                (int)Math.Clamp(cg * shade, 0, 255),
                (int)Math.Clamp(cb * shade, 0, 255));

            FillQuad(dc, brush, q.P0, q.P1, q.P2, q.P3);
            _hitQuads.Add((q.P0, q.P1, q.P2, q.P3, q.U, q.V));
        }

        // 四步实验可视化（叠加在地形上）
        switch (_step)
        {
            case ExperimentStep.Cutting:   DrawCutting(dc, size); break;
            case ExperimentStep.Lines:     DrawStackedLines(dc, size); break;
            case ExperimentStep.Projection: DrawProjection(dc, size); break;
            case ExperimentStep.Density:   DrawDensity3D(dc, size); break;
        }

        // 山峰 / 河流符号（3D 投影）
        DrawSymbols3D(dc, size);

        // 悬停标记
        if (_hasHover)
        {
            var h = Field.SampleSmooth(_hoverU, _hoverV);
            var at = Project(_hoverU, _hoverV, h, size).Screen;
            dc.DrawEllipse(new SolidColorBrush(Colors.White), new Pen(new SolidColorBrush(CAccent), 2), at, 5, 5);
            var stem = new Point(at.X, at.Y + 26);
            dc.DrawLine(new Pen(new SolidColorBrush(CAccent), 1.5), at, stem);
            Text(dc, $"{(int)h} m", stem.X, stem.Y + 2, 11.5, Colors.White, true, TextAlignment.Center);
        }

        DrawStepBadge(dc, size);
    }

    private static void FillQuad(DrawingContext dc, IBrush brush, Point a, Point b, Point c, Point d)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(a, true);
            g.LineTo(b); g.LineTo(c); g.LineTo(d);
            g.EndFigure(true);
        }
        // 同色描边：相邻四边形之间不会露出背景色缝（否则整片山体像罩了一层网格）
        dc.DrawGeometry(brush, new Pen(brush, 1.0), geo);
    }

    /// <summary>给地形四周补一圈裙边（垂直侧壁），避免山体边缘悬空、露出底座缝隙。</summary>
    private void DrawSkirt(DrawingContext dc, Size size, float ceiling)
    {
        float Top(double u, double v) => Math.Min(Field.SampleSmooth(u, v), ceiling);
        var rock = new SolidColorBrush(Color.Parse("#5A6B4A"));
        var dark = new SolidColorBrush(Color.Parse("#3C4A34"));
        const int N = 24;
        for (var i = 0; i < N; i++)
        {
            var t0 = (double)i / N; var t1 = (double)(i + 1) / N;
            // 南、北两条边
            foreach (var v in new[] { 1.0, 0.0 })
            {
                var top0 = Project(t0, v, Top(t0, v), size).Screen;
                var top1 = Project(t1, v, Top(t1, v), size).Screen;
                var bot0 = Project(t0, v, Field.MinHeight - 1, size).Screen;
                var bot1 = Project(t1, v, Field.MinHeight - 1, size).Screen;
                FillQuad(dc, v > 0.5 ? rock : dark, top0, top1, bot1, bot0);
            }
            // 东、西两条边
            foreach (var u in new[] { 0.0, 1.0 })
            {
                var top0 = Project(u, t0, Top(u, t0), size).Screen;
                var top1 = Project(u, t1, Top(u, t1), size).Screen;
                var bot0 = Project(u, t0, Field.MinHeight - 1, size).Screen;
                var bot1 = Project(u, t1, Field.MinHeight - 1, size).Screen;
                FillQuad(dc, u < 0.5 ? rock : dark, top0, top1, bot1, bot0);
            }
        }
    }

    private void DrawBasePlate(DrawingContext dc, Size size)
    {
        var corners = new[] { (0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0) }
            .Select(c => Project(c.Item1, c.Item2, Field.MinHeight - 1, size).Screen).ToArray();
        FillQuad(dc, new SolidColorBrush(Color.Parse("#132238")), corners[0], corners[1], corners[2], corners[3]);
        var pen = new Pen(new SolidColorBrush(Color.Parse("#2C4C73")), 1.2);
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(corners[0], true);
            g.LineTo(corners[1]); g.LineTo(corners[2]); g.LineTo(corners[3]);
            g.EndFigure(true);
        }
        dc.DrawGeometry(null, pen, geo);
    }

    // 起始就落在山顶下方一点，打开就能看到“削掉山顶”的截面，而不是一张贴在山尖的板
    private float CurrentCutLevel => (float)(Field.MaxHeight - (Field.MaxHeight - Field.MinHeight) * (0.06 + 0.94 * _anim));

    /// <summary>步骤1：水平切面从山顶下降，与山体相交处高亮出交线。</summary>
    private void DrawCutting(DrawingContext dc, Size size)
    {
        var lv = Math.Clamp(CurrentCutLevel, Field.MinHeight, Field.MaxHeight);
        // 切面
        var corners = new[] { (0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0) }
            .Select(c => Project(c.Item1, c.Item2, lv, size).Screen).ToArray();
        FillQuad(dc, new SolidColorBrush(Color.FromArgb(38, 31, 209, 232)), corners[0], corners[1], corners[2], corners[3]);
        var planePen = new Pen(new SolidColorBrush(Color.FromArgb(150, 31, 209, 232)), 1.2);
        var pg = new StreamGeometry();
        using (var g = pg.Open())
        {
            g.BeginFigure(corners[0], true);
            g.LineTo(corners[1]); g.LineTo(corners[2]); g.LineTo(corners[3]);
            g.EndFigure(true);
        }
        dc.DrawGeometry(null, planePen, pg);

        // 切面与山体的交线：在该高度上精确求解（不能只用缓存里的整百高程）
        DrawExactContour3D(dc, size, lv, new Pen(new SolidColorBrush(CCutting), 2.6));
        // 切面标注
        Text(dc, $"水平面 {lv:0} m", corners[0].X + 6, corners[0].Y - 4, 11, CCutting, true);
    }

    /// <summary>步骤2：多层水平切面各留下一条交线，像千层糕一样堆叠。</summary>
    private void DrawStackedLines(DrawingContext dc, Size size)
    {
        var interval = Math.Max(10f, Field.EffectiveInterval);
        var top = (float)(Math.Floor(Field.MaxHeight / interval) * interval);
        var levels = new List<float>();
        for (var lv = top; lv > Field.MinHeight + interval * 0.5f && levels.Count < 14; lv -= interval)
            levels.Add(lv);

        var reveal = Math.Max(1, (int)Math.Ceiling(_anim * levels.Count));
        for (var k = 0; k < Math.Min(levels.Count, reveal); k++)
        {
            var lv = levels[k];
            var alpha = (byte)(120 + 120 * (1 - (double)k / levels.Count));
            DrawExactContour3D(dc, size, lv,
                new Pen(new SolidColorBrush(Color.FromArgb(alpha, (byte)31, (byte)209, (byte)232)), 2.0));
        }

        // 最新的一层加一块很淡的切面，强调“水平切割”
        if (reveal > 0 && reveal <= levels.Count)
        {
            var lv = levels[reveal - 1];
            var corners = new[] { (0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0) }
                .Select(c => Project(c.Item1, c.Item2, lv, size).Screen).ToArray();
            FillQuad(dc, new SolidColorBrush(Color.FromArgb(16, 31, 209, 232)), corners[0], corners[1], corners[2], corners[3]);
        }
    }

    /// <summary>步骤3：各高度交线沿垂线落到同一平面，累积成等高线图。</summary>
    private void DrawProjection(DrawingContext dc, Size size)
    {
        var interval = Math.Max(10f, Field.EffectiveInterval);
        var top = (float)(Math.Floor(Field.MaxHeight / interval) * interval);
        var levels = new List<float>();
        for (var lv = top; lv > Field.MinHeight + interval * 0.5f && levels.Count < 9; lv -= interval)
            levels.Add(lv);

        var drop = Ease(Math.Clamp(_anim / 0.72, 0, 1));      // 0→1：交线整体垂降
        var beamPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 124, 92, 255)), 1.0);
        var livePen = new Pen(new SolidColorBrush(Color.FromArgb(240, 31, 209, 232)), 2.0);

        foreach (var lv in levels)
        {
            var y = lv - (lv - Field.MinHeight) * (float)drop;
            var lines = Field.ExtractContour(lv);
            foreach (var line in lines)
            {
                if (line.Count < 2) continue;

                // 投影轨迹：从原高度到当前位置的竖线，稀疏采样才不会糊成一片帘子
                if (drop > 0.02 && drop < 0.995)
                {
                    var step = Math.Max(5, line.Count / 10);
                    for (var i = 0; i < line.Count; i += step)
                    {
                        var a = Project(line[i].U, line[i].V, lv, size).Screen;
                        var b = Project(line[i].U, line[i].V, y, size).Screen;
                        dc.DrawLine(beamPen, a, b);
                    }
                }

                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(Project(line[0].U, line[0].V, y, size).Screen, false);
                    for (var i = 1; i < line.Count; i++)
                        g.LineTo(Project(line[i].U, line[i].V, y, size).Screen);
                    g.EndFigure(false);
                }
                dc.DrawGeometry(null, livePen, geo);
            }
        }

        // 底面（投影平面）轮廓
        var corners = new[] { (0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0) }
            .Select(c => Project(c.Item1, c.Item2, Field.MinHeight, size).Screen).ToArray();
        var pg = new StreamGeometry();
        using (var g = pg.Open())
        {
            g.BeginFigure(corners[0], true);
            g.LineTo(corners[1]); g.LineTo(corners[2]); g.LineTo(corners[3]);
            g.EndFigure(true);
        }
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(20, 124, 92, 255)),
            new Pen(new SolidColorBrush(Color.FromArgb(110, 124, 92, 255)), 1.2), pg);

        Text(dc, "垂直投影 → 同一平面", 16, 44, 12, CProjection, true);
    }

    /// <summary>步骤4：坡度着色——陡处等高线密、缓处疏，直接体现判坡规律。</summary>
    private void DrawDensity3D(DrawingContext dc, Size size)
    {
        const int N = 56;
        for (var j = 0; j < N; j++)
        for (var i = 0; i < N; i++)
        {
            var u = (i + 0.5) / N; var v = (j + 0.5) / N;
            var slope = TerrainAnalyzer.SlopeAt(Field, u, v);          // 归一化 0..1
            var (r, g, b) = SlopeColor(slope);
            var p0 = Project((double)i / N, (double)j / N, Field.SampleSmooth((double)i / N, (double)j / N), size).Screen;
            var p1 = Project((double)(i + 1) / N, (double)j / N, Field.SampleSmooth((double)(i + 1) / N, (double)j / N), size).Screen;
            var p2 = Project((double)(i + 1) / N, (double)(j + 1) / N, Field.SampleSmooth((double)(i + 1) / N, (double)(j + 1) / N), size).Screen;
            var p3 = Project((double)i / N, (double)(j + 1) / N, Field.SampleSmooth((double)i / N, (double)(j + 1) / N), size).Screen;
            FillQuad(dc, new SolidColorBrush(Color.FromArgb(200, r, g, b)), p0, p1, p2, p3);
        }
        // 等高线保持可见
        var contourPen = new Pen(new SolidColorBrush(Color.FromArgb(170, 26, 22, 16)), 1.0);
        foreach (var (_, lines) in _contours)
            foreach (var line in lines)
            {
                if (line.Count < 2) continue;
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(Project(line[0].U, line[0].V,
                        Field.SampleSmooth(line[0].U, line[0].V), size).Screen, false);
                    for (var i = 1; i < line.Count; i++)
                        g.LineTo(Project(line[i].U, line[i].V,
                            Field.SampleSmooth(line[i].U, line[i].V), size).Screen);
                    g.EndFigure(false);
                }
                dc.DrawGeometry(null, contourPen, geo);
            }
        DrawSlopeLegend(dc, size);
    }

    /// <summary>在指定高度画一条等高线（提 lift 到该高度，贴合切面）。</summary>
    private void DrawContourAtLevel3D(DrawingContext dc, Size size, float level, Pen pen)
    {
        foreach (var (lv, lines) in _contours)
        {
            if (Math.Abs(lv - level) > (Field.EffectiveInterval * 0.6f)) continue;
            foreach (var line in lines)
            {
                if (line.Count < 2) continue;
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    var p0 = Project(line[0].U, line[0].V, lv, size).Screen;
                    g.BeginFigure(p0, false);
                    for (var i = 1; i < line.Count; i++)
                        g.LineTo(Project(line[i].U, line[i].V, lv, size).Screen);
                    g.EndFigure(false);
                }
                dc.DrawGeometry(null, pen, geo);
            }
        }
    }

    /// <summary>
    /// 在指定高度精确求交线并绘制。线贴着地表走（按该点实际高度投影），
    /// 这样在斜面上不会因为“线在水平面上、山体在它前面”而看起来断成虚线。
    /// </summary>
    private void DrawExactContour3D(DrawingContext dc, Size size, float level, Pen pen)
    {
        var lines = Field.ExtractContour(level);
        var ceiling = _step == ExperimentStep.Cutting ? level : float.MaxValue;
        float Hug(double u, double v) => Math.Min(Field.SampleSmooth(u, v), ceiling);

        foreach (var line in lines)
        {
            if (line.Count < 2) continue;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(Project(line[0].U, line[0].V, Hug(line[0].U, line[0].V), size).Screen, false);
                for (var i = 1; i < line.Count; i++)
                    g.LineTo(Project(line[i].U, line[i].V, Hug(line[i].U, line[i].V), size).Screen);
                g.EndFigure(false);
            }
            dc.DrawGeometry(null, pen, geo);
        }
    }

    private void DrawSymbols3D(DrawingContext dc, Size size)
    {
        // 切割步骤里山体已被削到切面高度，符号也要跟着压在切面上，不能悬空
        var cut = _step == ExperimentStep.Cutting ? CurrentCutLevel : float.MaxValue;
        float Flat(float h) => Math.Min(h, cut);

        // 山峰 ▲ + 高程（山峰已被切掉时不再标注，免得山顶点飘在切面上）
        foreach (var (u, v, h) in Field.Peaks.Take(3))
        {
            if (h > cut + 5) continue;
            var at = Project(u, v, Flat(h), size).Screen;
            var tri = new StreamGeometry();
            using (var g = tri.Open())
            {
                g.BeginFigure(new Point(at.X, at.Y - 7), true);
                g.LineTo(new Point(at.X - 5.5, at.Y + 3));
                g.LineTo(new Point(at.X + 5.5, at.Y + 3));
                g.EndFigure(true);
            }
            dc.DrawGeometry(new SolidColorBrush(Color.Parse("#1B1308")), null, tri);
            Text(dc, ((int)h).ToString(), at.X + 8, at.Y - 8, 10, Color.Parse("#1B1308"), true);
        }
        // 河流（沿谷底的蓝线）
        if (Field.River.Count > 1)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                var p0 = Project(Field.River[0].U, Field.River[0].V, Flat(Field.SampleSmooth(Field.River[0].U, Field.River[0].V)), size).Screen;
                g.BeginFigure(p0, false);
                for (var i = 1; i < Field.River.Count; i++)
                    g.LineTo(Project(Field.River[i].U, Field.River[i].V, Flat(Field.SampleSmooth(Field.River[i].U, Field.River[i].V)), size).Screen);
                g.EndFigure(false);
            }
            var pen = new Pen(new SolidColorBrush(Color.Parse("#2F9BE0")), 2.4);
            pen.LineJoin = PenLineJoin.Round;
            dc.DrawGeometry(null, pen, geo);
        }
    }

    private void DrawStepBadge(DrawingContext dc, Size size)
    {
        var titles = new[] { "① 等高切割", "② 形成交线", "③ 投影成图", "④ 疏密判坡" };
        var text = titles[(int)_step];
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(Font.FontFamily, FontStyle.Normal, FontWeight.Bold), 14,
            new SolidColorBrush(Colors.White));
        var x = 14; var y = 12;
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(190, 8, 24, 46)), null,
            new Rect(x - 8, y - 5, ft.Width + 16, ft.Height + 10));
        dc.DrawText(ft, new Point(x, y));
    }

    // ================================================================ 俯视视角

    private static Rect TopMapRect(Size size)
    {
        var profile = Math.Min(120, size.Height * 0.26);
        return new Rect(14, 14, size.Width - 28, Math.Max(40, size.Height - 28 - profile));
    }

    private void RenderTop(DrawingContext dc, Size size)
    {
        var panel = new Rect(14, 14, size.Width - 28, size.Height - 28);
        var map = TopMapRect(size);
        var profileRect = new Rect(panel.X, map.Bottom, panel.Width, panel.Bottom - map.Bottom);

        // 分层设色 / 坡度着色底图
        const int N = 92;
        for (var j = 0; j < N; j++)
        for (var i = 0; i < N; i++)
        {
            var u = (i + 0.5) / N; var v = (j + 0.5) / N;
            Color col;
            if (_step == ExperimentStep.Density)
            {
                var (r, g, b) = SlopeColor(TerrainAnalyzer.SlopeAt(Field, u, v));
                col = Color.FromRgb(r, g, b);
            }
            else
            {
                var h = Field.SampleSmooth(u, v);
                var (r, g, b) = _colored ? TerrainAnalyzer.ColorForHeight(h, Field.MinHeight, Field.MaxHeight) : ((byte)0xF3, (byte)0xF0, (byte)0xE6);
                col = Color.FromRgb(r, g, b);
            }
            dc.FillRectangle(new SolidColorBrush(col), new Rect(
                map.X + map.Width * i / N, map.Y + map.Height * j / N,
                map.Width / N + 1, map.Height / N + 1));
        }

        Point P(double u, double v) => new(map.X + u * map.Width, map.Y + v * map.Height);

        // 等高线
        if (_showContours)
        {
            foreach (var (level, lines) in _contours)
            {
                var isIndex = Math.Abs(level % 500) < 0.5;
                var pen = new Pen(new SolidColorBrush(isIndex ? CContourIndex : CContour), isIndex ? 2.0 : 1.0);
                foreach (var line in lines)
                {
                    if (line.Count < 2) continue;
                    var geo = new StreamGeometry();
                    using (var g = geo.Open())
                    {
                        g.BeginFigure(P(line[0].U, line[0].V), false);
                        for (var i = 1; i < line.Count; i++) g.LineTo(P(line[i].U, line[i].V));
                        g.EndFigure(false);
                    }
                    dc.DrawGeometry(null, pen, geo);
                }
            }
        }

        // 高程标注（计曲线）
        if (_showLabels)
        {
            foreach (var (level, lines) in _contours)
            {
                if (Math.Abs(level % 500) > 0.5) continue;
                foreach (var line in lines)
                {
                    if (line.Count < 12) continue;
                    var mid = line[line.Count / 2];
                    var at = P(mid.U, mid.V);
                    if (at.X < map.X + 16 || at.X > map.Right - 16) continue;
                    Text(dc, $"{(int)level}", at.X, at.Y - 7, 9.5, CContourIndex, true, TextAlignment.Center);
                }
            }
        }

        // 河流
        if (Field.River.Count > 1)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(P(Field.River[0].U, Field.River[0].V), false);
                for (var i = 1; i < Field.River.Count; i++) g.LineTo(P(Field.River[i].U, Field.River[i].V));
                g.EndFigure(false);
            }
            var pen = new Pen(new SolidColorBrush(Color.Parse("#2F7FD0")), 2.2) { LineJoin = PenLineJoin.Round, LineCap = PenLineCap.Round };
            dc.DrawGeometry(null, pen, geo);
        }

        // 山峰
        foreach (var (u, v, h) in Field.Peaks.Take(4))
        {
            var at = P(u, v);
            var tri = new StreamGeometry();
            using (var g = tri.Open())
            {
                g.BeginFigure(new Point(at.X, at.Y - 6), true);
                g.LineTo(new Point(at.X - 5.5, at.Y + 4));
                g.LineTo(new Point(at.X + 5.5, at.Y + 4));
                g.EndFigure(true);
            }
            dc.DrawGeometry(new SolidColorBrush(Color.Parse("#3A2A18")), null, tri);
            Text(dc, ((int)h).ToString(), at.X + 8, at.Y - 8, 10, Color.Parse("#3A2A18"), true);
        }

        // 陡崖锯齿
        foreach (var (u, v) in Field.Cliffs.Take(40))
        {
            var at = P(u, v);
            var saw = new StreamGeometry();
            using (var g = saw.Open())
            {
                g.BeginFigure(new Point(at.X - 3, at.Y + 3), false);
                for (var i = 0; i < 3; i++)
                {
                    var x0 = at.X - 3 + i * 3;
                    g.LineTo(new Point(x0 + 1.5, at.Y - 2));
                    g.LineTo(new Point(x0 + 3, at.Y + 3));
                }
                g.EndFigure(false);
            }
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.Parse("#8B2E2E")), 1.4), saw);
        }

        // 悬停十字
        if (_hasHover)
        {
            var at = P(_hoverU, _hoverV);
            var pen = new Pen(new SolidColorBrush(CAccent), 1.6);
            dc.DrawLine(pen, new Point(map.X, at.Y), new Point(map.Right, at.Y));
            dc.DrawLine(pen, new Point(at.X, map.Y), new Point(at.X, map.Bottom));
            dc.DrawEllipse(new SolidColorBrush(Colors.White), new Pen(new SolidColorBrush(CAccent), 2), at, 5, 5);
        }

        DrawNorthArrow(dc, map);
        DrawScaleBar(dc, map);
        if (_step == ExperimentStep.Density) DrawSlopeLegend(dc, size); else DrawLegend(dc, map);
        if (profileRect.Height > 50) DrawProfile(dc, profileRect, map);
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(CLine), 1.2), map);
        DrawStepBadge(dc, size);
    }

    private void DrawProfile(DrawingContext dc, Rect r, Rect mapRect)
    {
        if (r.Height < 50) return;
        dc.FillRectangle(new SolidColorBrush(Color.FromArgb(235, 252, 250, 244)), r);
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.Parse("#2A3446")), 1.0), r);
        var maxH = Math.Max(1f, Field.MaxHeight);
        var baseY = r.Bottom - 18; var topY = r.Y + 16;
        for (var hh = 200f; hh < maxH; hh += 200)
        {
            var gy = baseY - (hh / maxH) * (baseY - topY);
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(70, 42, 52, 70)), 0.8), new Point(r.X, gy), new Point(r.Right, gy));
            Text(dc, $"{hh:0}", r.X + 2, gy - 8, 7.5, Color.Parse("#5A6B80"));
        }
        dc.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#2A3446")), 1.2), new Point(r.X, baseY), new Point(r.Right, baseY));
        const int N = 160;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            for (var i = 0; i <= N; i++)
            {
                var u = (double)i / N;
                var hv = Field.SampleSmooth(u, 0.5);
                var y = baseY - (hv / maxH) * (baseY - topY);
                var x = r.X + u * r.Width;
                if (i == 0) g.BeginFigure(new Point(x, y), false); else g.LineTo(new Point(x, y));
            }
            g.EndFigure(false);
        }
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.Parse("#B03A2E")), 2.0), geo);
        foreach (var u in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
        {
            var hv = Field.SampleSmooth(u, 0.5);
            var y = baseY - (hv / maxH) * (baseY - topY);
            var x = r.X + u * r.Width;
            dc.DrawEllipse(new SolidColorBrush(Color.Parse("#B03A2E")), null, new Point(x, y), 3, 3);
            Text(dc, $"{hv:0}", x, y - 13, 8, Color.Parse("#8B2E24"), true, TextAlignment.Center);
        }
        Text(dc, T("profile.title"), r.X + 6, r.Y + 3, 9, Color.Parse("#2A3446"), true);
    }

    private void DrawNorthArrow(DrawingContext dc, Rect r)
    {
        var x = r.Right - 26; var y = r.Y + 22;
        var tri = new StreamGeometry();
        using (var g = tri.Open())
        {
            g.BeginFigure(new Point(x, y - 12), true);
            g.LineTo(new Point(x - 5, y + 6)); g.LineTo(new Point(x, y + 2)); g.EndFigure(true);
        }
        dc.DrawGeometry(new SolidColorBrush(Color.Parse("#2A3446")), null, tri);
        Text(dc, "N", x, y + 8, 9.5, Color.Parse("#2A3446"), true, TextAlignment.Center);
    }

    private void DrawScaleBar(DrawingContext dc, Rect r)
    {
        var x = r.X + 14; var y = r.Bottom - 22; var w = 90.0; var h = 5.0;
        for (var i = 0; i < 4; i++)
        {
            var seg = new Rect(x + i * w / 4, y, w / 4, h);
            dc.FillRectangle(new SolidColorBrush(i % 2 == 0 ? Color.Parse("#2A3446") : Color.Parse("#FFFFFF")), seg);
            dc.DrawRectangle(new Pen(new SolidColorBrush(Color.Parse("#2A3446")), 0.8), seg);
        }
        Text(dc, "0", x, y - 11, 8.5, Color.Parse("#2A3446"), align: TextAlignment.Center);
        Text(dc, "1 km", x + w, y - 11, 8.5, Color.Parse("#2A3446"), align: TextAlignment.Center);
    }

    private void DrawLegend(DrawingContext dc, Rect r)
    {
        var bands = new (float From, float To)[]
        {
            (0,200),(200,400),(400,600),(600,800),(800,1000),
            (1000,1200),(1200,1400),(1400,1600),(1600,2000)
        };
        var w = 62.0; var rowH = 11.0; var h = bands.Length * rowH + 16;
        var x = r.Right - w - 12; var y = r.Bottom - h - 12;
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)), null, new Rect(x, y, w, h));
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.Parse("#2A3446")), 0.9), new Rect(x, y, w, h));
        Text(dc, T("legend.title"), x + 5, y + 3, 8.5, Color.Parse("#2A3446"), true);
        for (var i = 0; i < bands.Length; i++)
        {
            var (from, to) = bands[i]; var mid = (from + to) / 2;
            var (cr, cg, cb) = TerrainAnalyzer.ColorForHeight(mid, 0, 2000);
            var cy = y + 14 + i * rowH;
            dc.FillRectangle(new SolidColorBrush(Color.FromRgb(cr, cg, cb)), new Rect(x + 5, cy, 14, rowH - 2));
            dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.Parse("#2A3446")), 0.6), new Rect(x + 5, cy, 14, rowH - 2));
            Text(dc, to >= 2000 ? ">1600 m" : $"{from:0}–{to:0} m", x + 23, cy, 8, Color.Parse("#2A3446"));
        }
    }

    private void DrawSlopeLegend(DrawingContext dc, Size size)
    {
        var x = size.Width - 168; var y = size.Height - 58; var w = 150; var h = 12;
        for (var i = 0; i < w; i++)
        {
            var (r, g, b) = SlopeColor(i / (double)w);
            dc.FillRectangle(new SolidColorBrush(Color.FromRgb(r, g, b)), new Rect(x + i, y, 1.5, h));
        }
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.Parse("#8FA0B8")), 0.8), new Rect(x, y, w, h));
        Text(dc, T("slope.gentle"), x, y - 14, 9.5, Color.Parse("#EAF2FF"));
        Text(dc, T("slope.steep"), x + w, y - 14, 9.5, Color.Parse("#EAF2FF"), align: TextAlignment.Right);
    }

    private static (byte R, byte G, byte B) SlopeColor(double t)
    {
        t = Math.Clamp(t, 0, 1);
        // 绿(缓) → 黄 → 红(陡)
        byte r, g;
        const byte b = 70;
        if (t < 0.5)
        {
            var k = t / 0.5;
            r = (byte)(0x4E + (0xE8 - 0x4E) * k);
            g = (byte)(0x9E + (0xE2 - 0x9E) * k);
        }
        else
        {
            var k = (t - 0.5) / 0.5;
            r = (byte)(0xE8 + (0xC0 - 0xE8) * k);
            g = (byte)(0xE2 + (0x39 - 0xE2) * k);
        }
        return (r, g, b);
    }

    private static double Ease(double t) => t * t * (3 - 2 * t);

    private void Text(DrawingContext dc, string s, double x, double y, double size, Color color,
        bool bold = false, TextAlignment align = TextAlignment.Left)
    {
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(Font.FontFamily, FontStyle.Normal, bold ? FontWeight.Bold : FontWeight.Normal),
            size, new SolidColorBrush(color));
        ft.TextAlignment = align;
        dc.DrawText(ft, new Point(align == TextAlignment.Center ? x - ft.Width / 2 :
            align == TextAlignment.Right ? x - ft.Width : x, y));
    }
}

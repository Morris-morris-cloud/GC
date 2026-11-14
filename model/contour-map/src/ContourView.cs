using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace NeoRF.ContourMap;

/// <summary>
/// 等高线教学画布，左右两块：
///   · 左：三维地形块（斜视图），表现真实起伏
///   · 右：等高线俯视图（分层设色 + 等高线 + 标注）
///
/// 鼠标在任一侧移动，两侧同步显示该点的海拔与地形部位 —— 这正是课本
/// 「把立体地形转成平面等高线图」的关键对应关系。
/// </summary>
public sealed class ContourView : Control
{
    // ── 配色（与启动器深色舞台一致）
    private static readonly Color CInk = Color.Parse("#111A2E");
    private static readonly Color CLine = Color.Parse("#DCE7F8");
    private static readonly Color CContour = Color.Parse("#3B2A18");
    private static readonly Color CContourIndex = Color.Parse("#1F1508");
    private static readonly Color CAccent = Color.Parse("#2F6BFF");
    private static readonly Color CWarn = Color.Parse("#FF6B4A");

    private static readonly Typeface Font = new("Noto Sans CJK SC, Microsoft YaHei, Arial");

    public TerrainField Field { get; private set; }

    /// <summary>切换到另一块地形（重新提取等高线）。</summary>
    public void SetField(TerrainField field)
    {
        Field = field;
        _levels = Field.ContourLevels();
        _contours.Clear();
        foreach (var lv in _levels)
            _contours.Add((lv, Field.ExtractContour(lv)));
        InvalidateVisual();
    }

    /// <summary>等高线缓存：level -> 折线集合。</summary>
    private readonly List<(float Level, List<List<(double U, double V)>> Lines)> _contours = new();
    private List<float> _levels = new();

    public Func<string, string> T { get; set; } = k => k;

    // ── 交互状态
    private double _hoverU = 0.5, _hoverV = 0.5;
    private bool _hasHover;

    private bool _showContours = true;
    private bool _showLabels = true;
    private bool _showSlope = true;
    private bool _colored = true;

    public bool ShowContours
    {
        get => _showContours;
        set => SetFlag(ref _showContours, value);
    }

    public bool ShowLabels
    {
        get => _showLabels;
        set => SetFlag(ref _showLabels, value);
    }

    public bool ShowSlope
    {
        get => _showSlope;
        set => SetFlag(ref _showSlope, value);
    }

    /// <summary>是否分层设色（关闭后只看线稿，便于观察等高线本身）。</summary>
    public bool Colored
    {
        get => _colored;
        set => SetFlag(ref _colored, value);
    }

    private void SetFlag(ref bool slot, bool value)
    {
        if (slot == value) return;
        slot = value;
        InvalidateVisual();
    }

    /// <summary>当前悬停点信息，供外部状态栏显示。</summary>
    public event Action<double, double, float, TerrainFeature>? HoverChanged;

    public ContourView(TerrainField field, Func<string, string> text)
    {
        Field = field;
        T = text;
        ClipToBounds = true;

        _levels = Field.ContourLevels();
        foreach (var lv in _levels)
            _contours.Add((lv, Field.ExtractContour(lv)));

        PointerMoved += (_, e) =>
        {
            var p = e.GetPosition(this);
            UpdateHover(p);
        };
        PointerExited += (_, _) => { _hasHover = false; InvalidateVisual(); };
    }

    private void UpdateHover(Point p)
    {
        var size = Bounds.Size;
        if (size.Width < 40 || size.Height < 40) return;

        var (left, right) = SplitRects(size);
        double u = -1, v = -1;

        if (right.Contains(p))
        {
            u = (p.X - right.X) / right.Width;
            v = (p.Y - right.Y) / right.Height;
        }
        else if (left.Contains(p))
        {
            // 左图是斜视图，做逆变换近似：把点击位置反投到 u/v
            (u, v) = UnprojectIso(left, p);
        }

        if (u < 0 || u > 1 || v < 0 || v > 1) return;
        _hoverU = u; _hoverV = v; _hasHover = true;

        var h = Field.SampleSmooth(u, v);
        var f = TerrainAnalyzer.Classify(Field, u, v);
        HoverChanged?.Invoke(u, v, h, f);
        InvalidateVisual();
    }

    private (Rect Left, Rect Right) SplitRects(Size size)
    {
        var pad = 16.0;
        var half = (size.Width - pad * 3) / 2;
        var h = size.Height - pad * 2;
        return (new Rect(pad, pad, half, h),
                new Rect(pad * 2 + half, pad, half, h));
    }

    // ---------------------------------------------------------------- 渲染

    public override void Render(DrawingContext dc)
    {
        var size = Bounds.Size;
        if (size.Width < 80 || size.Height < 80) return;

        dc.FillRectangle(new SolidColorBrush(Color.Parse("#0B1524")), new Rect(size));

        var (left, right) = SplitRects(size);
        DrawIsoTerrain(dc, left);
        DrawContourMap(dc, right);
    }

    // ---- 左侧：三维地形块 ----
    private void DrawIsoTerrain(DrawingContext dc, Rect r)
    {
        // 斜投影：x 向右，y 向前（缩小），高度向上
        var cx = r.X + r.Width * 0.5;
        var cy = r.Y + r.Height * 0.62;
        var scaleX = r.Width * 0.62;
        var scaleY = r.Height * 0.30;
        var heightScale = r.Height * 0.00011;   // 高度压缩系数

        Point Project(double u, double v, double h) => new(
            cx + (u - 0.5) * scaleX,
            cy + (v - 0.5) * scaleY - h * heightScale);

        // 逐格绘制（远到近，保证遮挡顺序正确）
        const int N = 54;
        for (var j = 0; j < N; j++)
        {
            for (var i = 0; i < N; i++)
            {
                var u0 = (double)i / N; var u1 = (double)(i + 1) / N;
                var v0 = (double)j / N; var v1 = (double)(j + 1) / N;

                var h00 = Field.SampleSmooth(u0, v0);
                var h10 = Field.SampleSmooth(u1, v0);
                var h11 = Field.SampleSmooth(u1, v1);
                var h01 = Field.SampleSmooth(u0, v1);
                var hAvg = (h00 + h10 + h11 + h01) / 4f;

                var (cr, cg, cb) = TerrainAnalyzer.ColorForHeight(hAvg, Field.MinHeight, Field.MaxHeight);
                // 简易光照：面向西北的坡面更亮
                var shade = 0.78 + 0.42 * ((h00 - h11) / Math.Max(1f, Field.MaxHeight - Field.MinHeight) * -1 + 0.5);
                shade = Math.Clamp(shade, 0.55, 1.25);

                var brush = new SolidColorBrush(Color.FromRgb(
                    (byte)Math.Clamp(cr * shade, 0, 255),
                    (byte)Math.Clamp(cg * shade, 0, 255),
                    (byte)Math.Clamp(cb * shade, 0, 255)));

                var p00 = Project(u0, v0, h00);
                var p10 = Project(u1, v0, h10);
                var p11 = Project(u1, v1, h11);
                var p01 = Project(u0, v1, h01);

                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(p00, true);
                    g.LineTo(p10);
                    g.LineTo(p11);
                    g.LineTo(p01);
                    g.EndFigure(true);
                }
                dc.DrawGeometry(brush, null, geo);
            }
        }

        // 悬停指示
        if (_hasHover)
        {
            var h = Field.SampleSmooth(_hoverU, _hoverV);
            var p = Project(_hoverU, _hoverV, h);
            dc.DrawEllipse(new SolidColorBrush(Colors.White), new Pen(new SolidColorBrush(CAccent), 2),
                p, 5, 5);
            var stem = new Point(p.X, p.Y + 30);
            dc.DrawLine(new Pen(new SolidColorBrush(CAccent), 1.5), p, stem);
            Text(dc, $"{(int)h} m", stem.X, stem.Y + 3, 11.5, Colors.White, true, TextAlignment.Center);
        }
    }

    /// <summary>斜视图反投影（近似）：把屏幕点还原成 u/v。</summary>
    private (double U, double V) UnprojectIso(Rect r, Point p)
    {
        var cx = r.X + r.Width * 0.5;
        var cy = r.Y + r.Height * 0.62;
        var scaleX = r.Width * 0.62;
        var scaleY = r.Height * 0.30;
        var u = (p.X - cx) / scaleX + 0.5;
        var v = (p.Y - cy) / scaleY + 0.5;
        return (u, v);
    }

    // ---- 右侧：等高线俯视图 ----
    private void DrawContourMap(DrawingContext dc, Rect r)
    {
        // 分层设色底图
        const int N = 96;
        for (var j = 0; j < N; j++)
        {
            for (var i = 0; i < N; i++)
            {
                var u = (i + 0.5) / N; var v = (j + 0.5) / N;
                var h = Field.SampleSmooth(u, v);
                var (cr, cg, cb) = TerrainAnalyzer.ColorForHeight(h, Field.MinHeight, Field.MaxHeight);
                var brush = _colored
                    ? new SolidColorBrush(Color.FromRgb(cr, cg, cb))
                    : new SolidColorBrush(Color.Parse("#F3F0E6"));
                dc.FillRectangle(brush, new Rect(
                    r.X + r.Width * i / N,
                    r.Y + r.Height * j / N,
                    r.Width / N + 1,
                    r.Height / N + 1));
            }
        }

        Point P(double u, double v) => new(r.X + u * r.Width, r.Y + v * r.Height);

        // 等高线
        if (_showContours)
        {
            foreach (var (level, lines) in _contours)
            {
                var isIndex = Math.Abs(level % 500) < 0.5;   // 每 5 条一条计曲线
                var pen = new Pen(new SolidColorBrush(isIndex ? CContourIndex : CContour),
                    isIndex ? 2.0 : 1.0);

                foreach (var line in lines)
                {
                    if (line.Count < 2) continue;
                    var geo = new StreamGeometry();
                    using (var g = geo.Open())
                    {
                        g.BeginFigure(P(line[0].U, line[0].V), false);
                        for (var i = 1; i < line.Count; i++)
                            g.LineTo(P(line[i].U, line[i].V));
                        g.EndFigure(false);
                    }
                    dc.DrawGeometry(null, pen, geo);
                }
            }
        }

        // 等高线高程标注（计曲线）
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
                    if (at.X < r.X + 16 || at.X > r.Right - 16) continue;
                    Text(dc, $"{(int)level}", at.X, at.Y - 7, 9.5, CContourIndex, true, TextAlignment.Center);
                }
            }
        }

        // 悬停十字
        if (_hasHover)
        {
            var at = P(_hoverU, _hoverV);
            var pen = new Pen(new SolidColorBrush(CAccent), 1.6);
            dc.DrawLine(pen, new Point(r.X, at.Y), new Point(r.Right, at.Y));
            dc.DrawLine(pen, new Point(at.X, r.Y), new Point(at.X, r.Bottom));
            dc.DrawEllipse(new SolidColorBrush(Colors.White), new Pen(new SolidColorBrush(CAccent), 2), at, 5, 5);
        }

        // 边框
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(CLine), 1.2), r);
    }

    // ---------------------------------------------------------------- 文字

    private void Text(DrawingContext dc, string s, double x, double y, double size, Color color,
        bool bold = false, TextAlignment align = TextAlignment.Left)
    {
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(Font.FontFamily, FontStyle.Normal, bold ? FontWeight.Bold : FontWeight.Normal),
            size, new SolidColorBrush(color));
        ft.TextAlignment = align;
        dc.DrawText(ft, new Point(align == TextAlignment.Center ? x - ft.Width / 2 : x, y));
    }
}

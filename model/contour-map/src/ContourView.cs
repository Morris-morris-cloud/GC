using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    private Point _pointerPosition;
    private HoverRegion _hoverRegion;

    private enum HoverRegion
    {
        None,
        Terrain,
        ContourMap
    }

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
        PointerExited += (_, _) => ClearHover();
    }

    private void UpdateHover(Point p)
    {
        var size = Bounds.Size;
        if (size.Width < 40 || size.Height < 40) return;

        var (left, right) = SplitRects(size);
        var map = ContourMapRect(right);
        double u = -1, v = -1;
        var region = HoverRegion.None;

        if (map.Contains(p))
        {
            u = (p.X - map.X) / map.Width;
            v = (p.Y - map.Y) / map.Height;
            region = HoverRegion.ContourMap;
        }
        else if (left.Contains(p))
        {
            // 左图是斜视图，做逆变换近似：把点击位置反投到 u/v
            (u, v) = UnprojectIso(left, p);
            region = HoverRegion.Terrain;
        }

        if (region == HoverRegion.None || u < 0 || u > 1 || v < 0 || v > 1)
        {
            ClearHover();
            return;
        }
        _hoverU = u; _hoverV = v; _hasHover = true;
        _pointerPosition = p;
        _hoverRegion = region;

        var h = Field.SampleSmooth(u, v);
        var f = TerrainAnalyzer.Classify(Field, u, v);
        HoverChanged?.Invoke(u, v, h, f);
        InvalidateVisual();
    }

    private void ClearHover()
    {
        if (!_hasHover) return;
        _hasHover = false;
        _hoverRegion = HoverRegion.None;
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

    private static Rect ContourMapRect(Rect panel)
    {
        var profileHeight = Math.Min(132, panel.Height * 0.30);
        return profileHeight > 50
            ? new Rect(panel.X, panel.Y, panel.Width, panel.Height - profileHeight)
            : panel;
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
            var p = _hoverRegion == HoverRegion.Terrain
                ? _pointerPosition
                : Project(_hoverU, _hoverV, h);
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
        var panel = r;
        var mapRect = ContourMapRect(panel);
        var profileRect = new Rect(panel.X, mapRect.Bottom, panel.Width, panel.Bottom - mapRect.Bottom);
        r = mapRect;
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

        // ── 教科书地形符号 ──

        // 河流：蓝色线（沿谷底流）
        if (Field.River.Count > 1)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(P(Field.River[0].U, Field.River[0].V), false);
                for (var i = 1; i < Field.River.Count; i++)
                    g.LineTo(P(Field.River[i].U, Field.River[i].V));
                g.EndFigure(false);
            }
            var riverPen = new Pen(new SolidColorBrush(Color.Parse("#2F7FD0")), 2.2);
            riverPen.LineJoin = PenLineJoin.Round;
            riverPen.LineCap = PenLineCap.Round;
            dc.DrawGeometry(null, riverPen, geo);
        }

        // 山峰：▲ + 高程数字（教科书标注法）
        foreach (var (pu, pv, ph) in Field.Peaks.Take(4))
        {
            var at = P(pu, pv);
            var tri = new StreamGeometry();
            using (var g = tri.Open())
            {
                g.BeginFigure(new Point(at.X, at.Y - 6), true);
                g.LineTo(new Point(at.X - 5.5, at.Y + 4));
                g.LineTo(new Point(at.X + 5.5, at.Y + 4));
                g.EndFigure(true);
            }
            dc.DrawGeometry(new SolidColorBrush(Color.Parse("#3A2A18")), null, tri);
            Text(dc, ((int)ph).ToString(), at.X + 8, at.Y - 8, 10, Color.Parse("#3A2A18"), true);
        }

        // 陡崖：锯齿符号（教科书用锯齿线表示陡崖）
        foreach (var (cu, cv) in Field.Cliffs.Take(40))
        {
            var at = P(cu, cv);
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
            dc.DrawLine(pen, new Point(r.X, at.Y), new Point(r.Right, at.Y));
            dc.DrawLine(pen, new Point(at.X, r.Y), new Point(at.X, r.Bottom));
            dc.DrawEllipse(new SolidColorBrush(Colors.White), new Pen(new SolidColorBrush(CAccent), 2), at, 5, 5);
        }

        DrawNorthArrow(dc, r);
        DrawScaleBar(dc, r);
        DrawLegend(dc, r);
        if (profileRect.Height > 50) DrawProfile(dc, profileRect, mapRect);

        // 边框
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(CLine), 1.2), r);
    }

    /// <summary>
    /// 地形剖面图（教科书等高线图必配）：沿图幅中部的一条南北剖面线，
    /// 画出地面起伏并标注各点海拔。这是最能体现「等高线图 ↔ 立体地形」关系的图。
    /// </summary>
    private void DrawProfile(DrawingContext dc, Rect r, Rect mapRect)
    {
        if (r.Height < 50) return;

        // 背景
        dc.FillRectangle(new SolidColorBrush(Color.FromArgb(235, 252, 250, 244)), r);
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.Parse("#2A3446")), 1.0), r);

        // 纵轴比例：0 → 最大高程
        var maxH = Math.Max(1f, Field.MaxHeight);
        var baseY = r.Bottom - 18;
        var topY = r.Y + 16;

        // 水平网格（每 200 m 一条）
        for (var hh = 200f; hh < maxH; hh += 200)
        {
            var gy = baseY - (hh / maxH) * (baseY - topY);
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(70, 42, 52, 70)), 0.8),
                new Point(r.X, gy), new Point(r.Right, gy));
            Text(dc, $"{hh:0}", r.X + 2, gy - 8, 7.5, Color.Parse("#5A6B80"));
        }
        // 基准线
        dc.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#2A3446")), 1.2),
            new Point(r.X, baseY), new Point(r.Right, baseY));

        // 剖面线：沿图幅中部（v = 0.5）自西向东
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
                if (i == 0) g.BeginFigure(new Point(x, y), false);
                else g.LineTo(new Point(x, y));
            }
            g.EndFigure(false);
        }
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.Parse("#B03A2E")), 2.0), geo);

        // 剖面线上的几个关键点（与等高线图上的位置对应）
        var marks = new[] { 0.0, 0.25, 0.5, 0.75, 1.0 };
        foreach (var u in marks)
        {
            var hv = Field.SampleSmooth(u, 0.5);
            var y = baseY - (hv / maxH) * (baseY - topY);
            var x = r.X + u * r.Width;
            dc.DrawEllipse(new SolidColorBrush(Color.Parse("#B03A2E")), null, new Point(x, y), 3, 3);
            Text(dc, $"{hv:0}", x, y - 13, 8, Color.Parse("#8B2E24"), true, TextAlignment.Center);
        }

        // 标注：剖面线在图上的位置
        Text(dc, T("profile.title"), r.X + 6, r.Y + 3, 9, Color.Parse("#2A3446"), true);
        dc.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#2A3446")), 1.2, dashStyle: DashStyle.Dash),
            new Point(r.X + r.Width / 2, mapRect.Y), new Point(r.X + r.Width / 2, r.Y));
    }

    /// <summary>指北针（教科书地图必备，位于图幅右上角）。</summary>
    private void DrawNorthArrow(DrawingContext dc, Rect r)
    {
        var x = r.Right - 26;
        var y = r.Y + 20;
        var tri = new StreamGeometry();
        using (var g = tri.Open())
        {
            g.BeginFigure(new Point(x, y - 12), true);
            g.LineTo(new Point(x - 5, y + 6));
            g.LineTo(new Point(x, y + 2));
            g.EndFigure(true);
        }
        dc.DrawGeometry(new SolidColorBrush(Color.Parse("#2A3446")), null, tri);
        var tri2 = new StreamGeometry();
        using (var g2 = tri2.Open())
        {
            g2.BeginFigure(new Point(x, y - 12), true);
            g2.LineTo(new Point(x + 5, y + 6));
            g2.LineTo(new Point(x, y + 2));
            g2.EndFigure(true);
        }
        dc.DrawGeometry(new SolidColorBrush(Color.Parse("#8FA0B8")), null, tri2);
        Text(dc, "N", x, y + 8, 9.5, Color.Parse("#2A3446"), true, TextAlignment.Center);
    }

    /// <summary>比例尺（教科书地图必备，左下角）。</summary>
    private void DrawScaleBar(DrawingContext dc, Rect r)
    {
        var x = r.X + 14;
        var y = r.Bottom - 22;
        var w = 90.0;
        var h = 5.0;

        // 分黑白相间的格（教科书比例尺画法）
        for (var i = 0; i < 4; i++)
        {
            var seg = new Rect(x + i * w / 4, y, w / 4, h);
            dc.FillRectangle(new SolidColorBrush(i % 2 == 0
                ? Color.Parse("#2A3446") : Color.Parse("#FFFFFF")), seg);
            dc.DrawRectangle(new Pen(new SolidColorBrush(Color.Parse("#2A3446")), 0.8), seg);
        }
        Text(dc, "0", x, y - 11, 8.5, Color.Parse("#2A3446"), align: TextAlignment.Center);
        Text(dc, "1 km", x + w, y - 11, 8.5, Color.Parse("#2A3446"), align: TextAlignment.Center);
    }

    /// <summary>分层设色图例（教科书右下角的色阶表）。</summary>
    private void DrawLegend(DrawingContext dc, Rect r)
    {
        var bands = new (float From, float To)[]
        {
            (0, 200), (200, 400), (400, 600), (600, 800),
            (800, 1000), (1000, 1200), (1200, 1400), (1400, 1600), (1600, 2000)
        };

        var w = 62.0;
        var rowH = 11.0;
        var h = bands.Length * rowH + 16;
        var x = r.Right - w - 12;
        var y = r.Bottom - h - 12;

        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)), null,
            new Rect(x, y, w, h));
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.Parse("#2A3446")), 0.9),
            new Rect(x, y, w, h));

        Text(dc, T("legend.title"), x + 5, y + 3, 8.5, Color.Parse("#2A3446"), true);

        for (var i = 0; i < bands.Length; i++)
        {
            var (from, to) = bands[i];
            var mid = (from + to) / 2;
            var (cr, cg, cb) = TerrainAnalyzer.ColorForHeight(mid, 0, 2000);
            var cy = y + 14 + i * rowH;
            dc.FillRectangle(new SolidColorBrush(Color.FromRgb(cr, cg, cb)),
                new Rect(x + 5, cy, 14, rowH - 2));
            dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.Parse("#2A3446")), 0.6),
                new Rect(x + 5, cy, 14, rowH - 2));

            var label = to >= 2000 ? ">1600 m" : $"{from:0}–{to:0} m";
            Text(dc, label, x + 23, cy, 8, Color.Parse("#2A3446"));
        }
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

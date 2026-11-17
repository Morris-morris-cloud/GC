using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace GC.ScaleCalculator;

/// <summary>
/// 自绘可视化标尺：上条「图上距离」按真实厘米画，下条「实地距离」按同一比例尺压缩，
/// 再画线段式比例尺（自动换挡）与比例尺光谱。全部在 Render 里完成，不产生子控件。
/// </summary>
public sealed class ScaleRuler : Control
{
    // 与启动器 AppTheme 保持一致的调色（模型 dll 独立加载，取不到 DynamicResource 时也能显示）
    private static readonly Color CInk = Color.Parse("#111A2E");
    private static readonly Color CInkDim = Color.Parse("#5B6A86");
    private static readonly Color CInkFaint = Color.Parse("#8A97B1");
    private static readonly Color CLine = Color.Parse("#E3E8F3");
    private static readonly Color CLineStrong = Color.Parse("#D2DAEA");
    private static readonly Color CAccent = Color.Parse("#2F6BFF");
    private static readonly Color CAccentSoft = Color.Parse("#EAF1FF");
    private static readonly Color CAccentDark = Color.Parse("#1F4FD0");
    private static readonly Color CSuccess = Color.Parse("#1F9D6B");
    private static readonly Color CWarn = Color.Parse("#D98613");
    private static readonly Color CDanger = Color.Parse("#D64545");
    private static readonly Color CSurfaceAlt = Color.Parse("#F7F9FE");
    private static readonly Color CSurface = Color.Parse("#FFFFFF");

    private static readonly Typeface Font = new("Noto Sans CJK SC, Microsoft YaHei, Arial");

    /// <summary>1 厘米对应多少屏幕像素。</summary>
    public double PxPerCm = 46;

    /// <summary>能画下的最大宽度（超出即溢出提示）。</summary>
    public double MaxBarWidth = 520;

    public ScaleState State { get; set; } = new();
    public ScaleData Data { get; set; } = new();
    public Func<string, string> T { get; set; } = k => k;

    /// <summary>线段式比例尺换挡结果（供界面读数）。</summary>
    public SegmentScale Segment { get; private set; } = new() { MetersPerDiv = 1000, DivPx = 46 };

    /// <summary>图上距离是否超出可画范围。</summary>
    public bool MapOverflow { get; private set; }

    /// <summary>实地条溢出（按当前比例尺，这段实地图上也画不下）。</summary>
    public bool RealOverflow { get; private set; }

    public void Refresh() => InvalidateVisual();

    private static FormattedText MakeText(string text, double size, Color color, FontWeight? weight = null)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(Font.FontFamily, FontStyle.Normal, weight ?? FontWeight.Normal), size,
            new SolidColorBrush(color));
        return ft;
    }

    private static void Text(DrawingContext dc, string s, double x, double y, double size, Color color,
        FontWeight? weight = null, TextAlignment align = TextAlignment.Left)
    {
        var ft = MakeText(s, size, color, weight);
        ft.TextAlignment = align;
        dc.DrawText(ft, new Point(align == TextAlignment.Center ? x - ft.Width / 2 : x, y));
    }

    private static void RoundRect(DrawingContext dc, Rect r, double radius, IBrush fill, IPen? pen = null)
        => dc.DrawRectangle(fill, pen, new RoundedRect(r, radius));

    private static void Dot(DrawingContext dc, double x, double y, double r, IBrush fill)
        => dc.DrawEllipse(fill, null, new Point(x, y), r, r);

    public override void Render(DrawingContext dc)
    {
        base.Render(dc);
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w < 40 || h < 40) return;

        // 背景
        dc.FillRectangle(new SolidColorBrush(CSurface), new Rect(0, 0, w, h));

        var padL = 34.0;
        var padR = 30.0;
        var barW = Math.Max(60, w - padL - padR);
        MaxBarWidth = barW;

        var mapPx = ScaleMath.CmToPx(State.MapCm, PxPerCm);
        var realPx = ScaleMath.RealToMapPx(State.RealCm, State.N, PxPerCm);
        MapOverflow = mapPx > barW + 0.5;
        RealOverflow = realPx > barW + 0.5;

        // 线段式比例尺换挡，取"每格约 60px"
        Segment = ScaleMath.PickSegment(State.N, PxPerCm, 62);

        var y = 16.0;

        // ── 标题
        Text(dc, T("ruler.title"), padL, y, 13, CInk, FontWeight.SemiBold);
        Text(dc, ScaleMath.Digital(State.N), w - padR, y + 1, 14, CAccentDark,
            FontWeight.SemiBold, TextAlignment.Right);
        y += 30;

        // ═══════════════════════════════════════════ 1. 图上距离条
        y = DrawMapBar(dc, padL, y, barW, mapPx);
        y += 22;

        // ═══════════════════════════════════════════ 2. 等式连接
        DrawEquation(dc, padL, y, barW);
        y += 30;

        // ═══════════════════════════════════════════ 3. 实地距离条（压缩）
        y = DrawRealBar(dc, padL, y, barW, realPx);
        y += 24;

        // ═══════════════════════════════════════════ 4. 线段式比例尺
        y = DrawSegmentScale(dc, padL, y, barW);
        y += 24;

        // ═══════════════════════════════════════════ 5. 比例尺光谱
        y = DrawSpectrum(dc, padL, y, barW);
        y += 34;

        // ═══════════════════════════════════════════ 6. 提示 / 溢出
        DrawFooter(dc, padL, y, barW);
    }

    private double DrawMapBar(DrawingContext dc, double x, double y, double barW, double mapPx)
    {
        Text(dc, T("ruler.mapBar"), x, y, 11.5, CInkDim, FontWeight.SemiBold);
        Text(dc, ScaleMath.Num(State.MapCm) + " " + T("unit.cm"), x + barW, y, 12.5, CAccent,
            FontWeight.SemiBold, TextAlignment.Right);
        y += 20;

        var barH = 34.0;
        var r = new Rect(x, y, barW, barH);
        RoundRect(dc, r, 9, new SolidColorBrush(CSurfaceAlt), new Pen(new SolidColorBrush(CLine), 1));

        // 底上的厘米刻度（每 1cm 一格，最多画 30 格）
        var maxTicks = Math.Min(30, (int)(barW / PxPerCm) + 1);
        for (var i = 1; i <= maxTicks; i++)
        {
            var tx = x + i * PxPerCm;
            if (tx > x + barW - 2) break;
            dc.DrawLine(new Pen(new SolidColorBrush(CLineStrong), 1),
                new Point(tx, y + barH - 7), new Point(tx, y + barH));
            if (i % 2 == 0)
                Text(dc, i.ToString(CultureInfo.InvariantCulture), tx, y + barH + 2, 9.5, CInkFaint,
                    align: TextAlignment.Center);
        }

        // 实长（蓝色），超出则截断并染红
        var drawW = Math.Min(mapPx, barW);
        if (drawW > 0.5)
        {
            var fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new(CAccentSoft, 0), new(CAccent, 1)
                }
            };
            RoundRect(dc, new Rect(x + 1, y + 1, drawW - 2, barH - 2), 8, fill,
                new Pen(new SolidColorBrush(MapOverflow ? CDanger : CAccentDark), 1.5));
        }

        // 端点
        var endX = x + Math.Min(mapPx, barW);
        Dot(dc, x + 2, y + barH / 2, 4, new SolidColorBrush(CSurface));
        if (!MapOverflow)
        {
            Dot(dc, endX - 2, y + barH / 2, 5, new SolidColorBrush(CSurface));
            Dot(dc, endX - 2, y + barH / 2, 3, new SolidColorBrush(CAccentDark));
            // 长度标注
            Text(dc, ScaleMath.Num(State.MapCm) + " " + T("unit.cm"),
                endX - 4, y - 16, 11, CAccentDark, FontWeight.SemiBold, TextAlignment.Right);
        }
        else
        {
            Text(dc, "»", x + barW - 14, y + barH / 2 - 9, 16, CDanger, FontWeight.Bold);
        }

        return y + barH;
    }

    private void DrawEquation(DrawingContext dc, double x, double y, double barW)
    {
        var cx = x + barW / 2;
        dc.DrawLine(new Pen(new SolidColorBrush(CLineStrong), 1, dashStyle: DashStyle.Dash),
            new Point(x, y + 10), new Point(x + barW, y + 10));
        var label = T("ruler.equals");
        var ft = MakeText(label, 11, CInkDim, FontWeight.SemiBold);
        var bw = ft.Width + 22;
        RoundRect(dc, new Rect(cx - bw / 2, y, bw, 21), 10, new SolidColorBrush(CSurface),
            new Pen(new SolidColorBrush(CLine), 1));
        Text(dc, label, cx, y + 4, 11, CInkDim, FontWeight.SemiBold, TextAlignment.Center);
    }

    private double DrawRealBar(DrawingContext dc, double x, double y, double barW, double realPx)
    {
        var (rv, rkey) = ScaleMath.HumanReal(State.RealCm);
        Text(dc, T("ruler.realBar"), x, y, 11.5, CInkDim, FontWeight.SemiBold);
        Text(dc, ScaleMath.Num(rv) + " " + T(rkey), x + barW, y, 12.5, CSuccess,
            FontWeight.SemiBold, TextAlignment.Right);
        y += 20;

        var barH = 30.0;
        RoundRect(dc, new Rect(x, y, barW, barH), 9, new SolidColorBrush(CSurfaceAlt),
            new Pen(new SolidColorBrush(CLine), 1));

        // 用"按同一比例尺压缩后"的长度表现：与上图同宽才直观
        var drawW = Math.Min(realPx, barW);
        if (drawW > 0.5)
        {
            var fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = new GradientStops { new(Color.Parse("#DFF5EA"), 0), new(CSuccess, 1) }
            };
            RoundRect(dc, new Rect(x + 1, y + 1, drawW - 2, barH - 2), 8, fill,
                new Pen(new SolidColorBrush(RealOverflow ? CDanger : CSuccess), 1.5));
        }

        // 画上"实地"的等分虚线，表达这段距离很长
        for (var i = 1; i < 6; i++)
        {
            var tx = x + barW * i / 6.0;
            dc.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#80FFFFFF")), 1, dashStyle: DashStyle.Dash),
                new Point(tx, y + 5), new Point(tx, y + barH - 5));
        }

        if (!RealOverflow)
        {
            Dot(dc, x + drawW - 2, y + barH / 2, 4, new SolidColorBrush(CSurface));
        }
        else
        {
            Text(dc, "»", x + barW - 14, y + barH / 2 - 9, 16, CDanger, FontWeight.Bold);
        }

        // 中间写"= 图上这段"
        Text(dc, T("ruler.sameWidth"), x + barW / 2, y + barH / 2 - 7, 10.5, CInkDim,
            align: TextAlignment.Center);

        return y + barH;
    }

    private double DrawSegmentScale(DrawingContext dc, double x, double y, double barW)
    {
        Text(dc, T("ruler.segment"), x, y, 11.5, CInkDim, FontWeight.SemiBold);
        Text(dc, T("ruler.segmentHint"), x + barW, y + 1, 10, CInkFaint, align: TextAlignment.Right);
        y += 20;

        var seg = Segment;
        var total = seg.DivPx * seg.DivCount;
        if (total > barW)
        {
            // 再降一档，避免画不下
            seg = ScaleMath.PickSegment(State.N * 3, PxPerCm, 62);
            Segment = seg;
            total = seg.DivPx * seg.DivCount;
        }
        var sx = x + Math.Max(0, (barW - total) / 2);
        var sy = y;
        var segH = 13.0;

        for (var i = 0; i < seg.DivCount; i++)
        {
            var r = new Rect(sx + i * seg.DivPx, sy, seg.DivPx, segH);
            dc.FillRectangle(new SolidColorBrush(i % 2 == 0 ? CInk : CSurface), r);
            dc.DrawRectangle(null, new Pen(new SolidColorBrush(CInk), 1), r);
            Text(dc, i == 0 ? "0" : ScaleMath.SegmentLabel(seg.MetersPerDiv * i),
                sx + (i + 1) * seg.DivPx, sy + segH + 3, 9.5, CInkDim, align: TextAlignment.Center);
        }
        Text(dc, ScaleMath.SegmentLabel(seg.MetersPerDiv), sx + total / 2, sy + segH + 17, 10,
            CInkDim, FontWeight.SemiBold, TextAlignment.Center);

        return sy + segH + 32;
    }

    private double DrawSpectrum(DrawingContext dc, double x, double y, double barW)
    {
        Text(dc, T("ruler.spectrum"), x, y, 11.5, CInkDim, FontWeight.SemiBold);
        y += 20;

        var trackY = y + 6;
        var trackH = 8.0;
        var grad = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new(CAccent, 0), new(Color.Parse("#5B8DEF"), 0.4),
                new(Color.Parse("#8FB4F5"), 0.7), new(Color.Parse("#C9DAF8"), 1)
            }
        };
        RoundRect(dc, new Rect(x, trackY, barW, trackH), 4, grad);

        // 光谱层级刻度
        foreach (var item in Data.Spectrum)
        {
            var p = ScaleMath.SpectrumPosition(item.N);
            var px = x + barW * p;
            dc.DrawLine(new Pen(new SolidColorBrush(CSurface), 1.5),
                new Point(px, trackY), new Point(px, trackY + trackH));
            Text(dc, T(item.LabelKey), px, trackY + trackH + 3, 9.5, CInkFaint,
                align: TextAlignment.Center);
        }

        // 当前值指针
        var pos = ScaleMath.SpectrumPosition(State.N);
        var cx = x + barW * pos;
        dc.DrawLine(new Pen(new SolidColorBrush(CInk), 2),
            new Point(cx, trackY - 7), new Point(cx, trackY + trackH + 5));
        Dot(dc, cx, trackY - 9, 5, new SolidColorBrush(CAccent));
        Dot(dc, cx, trackY - 9, 2.5, new SolidColorBrush(CSurface));

        // 两端说明
        Text(dc, T("ruler.scopeUp"), x, trackY + trackH + 20, 10, CInkFaint);
        Text(dc, T("ruler.detailDown"), x + barW, trackY + trackH + 20, 10, CInkFaint,
            align: TextAlignment.Right);

        return trackY + trackH + 36;
    }

    private void DrawFooter(DrawingContext dc, double x, double y, double barW)
    {
        if (MapOverflow || RealOverflow)
        {
            var msg = T(MapOverflow ? "ruler.overflowMap" : "ruler.overflowReal");
            var r = new Rect(x, y, barW, 30);
            RoundRect(dc, r, 8, new SolidColorBrush(Color.Parse("#FDECEC")),
                new Pen(new SolidColorBrush(Color.Parse("#F5C2C2")), 1));
            Text(dc, "⚠  " + msg, x + 12, y + 7, 11, CDanger, FontWeight.SemiBold);
        }
        else
        {
            Text(dc, T("ruler.hint"), x, y + 4, 10.5, CInkFaint);
        }
    }
}

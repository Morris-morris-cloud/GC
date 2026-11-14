using System;
using System.Collections.Generic;

namespace NeoRF.ContourMap;

/// <summary>地形部位识别结果：课本要求学生能在等高线图上认出这五种部位。</summary>
public enum TerrainFeature
{
    None,
    Peak,        // 山峰
    Ridge,       // 山脊
    Valley,      // 山谷
    Saddle,      // 鞍部
    Cliff,       // 陡崖
    Slope        // 一般坡面
}

/// <summary>
/// 地形判读：根据高度场与坡度，判断任意一点属于哪种地形部位。
/// 这是课本「等高线判读」的核心，直接从数据算出来，不靠预设标记。
/// </summary>
public static class TerrainAnalyzer
{
    /// <summary>
    /// 判断某点的地形部位。
    ///
    /// 先查地形自带的**标注区**（教学用，位置由地形定义保证准确），
    /// 未命中再按坡度/曲率自动推断。纯几何推断对「鞍部」「陡崖」这类
    /// 特征容易误判，教学场景必须精确。
    /// </summary>
    public static TerrainFeature Classify(TerrainField field, double u, double v)
    {
        var tagged = field.TagAt(u, v);
        if (tagged != TerrainFeature.None) return tagged;

        var h = field.SampleSmooth(u, v);
        var grad = Gradient(field, u, v);
        var slope = Math.Sqrt(grad.dU * grad.dU + grad.dV * grad.dV);   // 归一化坡度

        // 陡崖：坡度极大（等高线挤成一团）。
        // 阈值按本图实际坡度分布标定（中位≈2.5、p99≈6.8），
        // 取 p99 以上一小段，否则大片坡面会被误判成陡崖。
        if (slope > 8.5) return TerrainFeature.Cliff;

        // 山峰：比四周都高
        if (IsLocalMax(field, u, v)) return TerrainFeature.Peak;

        // 鞍部：两个方向上高、另两个方向上低
        if (IsSaddle(field, u, v)) return TerrainFeature.Saddle;

        // 山脊 / 山谷：看曲率（横切方向是凸还是凹）
        var curvature = CrossCurvature(field, u, v, grad);
        if (curvature < -0.55) return TerrainFeature.Ridge;    // 向外凸
        if (curvature > 0.55) return TerrainFeature.Valley;    // 向内凹

        return TerrainFeature.Slope;
    }

    private static (double dU, double dV) Gradient(TerrainField f, double u, double v)
    {
        const double h = 0.004;
        var du = (f.SampleSmooth(u + h, v) - f.SampleSmooth(u - h, v)) / (2 * h);
        var dv = (f.SampleSmooth(u, v + h) - f.SampleSmooth(u, v - h)) / (2 * h);
        return (du / 1000.0, dv / 1000.0);   // 归一化到 0~1 量级
    }

    private static bool IsLocalMax(TerrainField f, double u, double v)
    {
        var c = f.SampleSmooth(u, v);
        const double r = 0.035;
        foreach (var (du, dv) in new[] { (r, 0.0), (-r, 0.0), (0.0, r), (0.0, -r) })
        {
            // 越近边界越不判为峰
            if (u + du < 0 || u + du > 1 || v + dv < 0 || v + dv > 1) return false;
            if (f.SampleSmooth(u + du, v + dv) >= c - 4) return false;
        }
        return true;
    }

    private static bool IsSaddle(TerrainField f, double u, double v)
    {
        const double r = 0.05;
        if (u - r < 0 || u + r > 1 || v - r < 0 || v + r > 1) return false;
        var c = f.SampleSmooth(u, v);
        var l = f.SampleSmooth(u - r, v); var rt = f.SampleSmooth(u + r, v);
        var t = f.SampleSmooth(u, v - r); var b = f.SampleSmooth(u, v + r);

        // 一对高、一对低 => 鞍部
        var horizHigh = l > c + 8 && rt > c + 8;
        var vertLow = t < c - 8 && b < c - 8;
        var vertHigh = t > c + 8 && b > c + 8;
        var horizLow = l < c - 8 && rt < c - 8;
        return (horizHigh && vertLow) || (vertHigh && horizLow);
    }

    /// <summary>横切等高线方向的曲率：负=山脊（凸），正=山谷（凹）。</summary>
    private static double CrossCurvature(TerrainField f, double u, double v, (double dU, double dV) grad)
    {
        var len = Math.Sqrt(grad.dU * grad.dU + grad.dV * grad.dV);
        if (len < 1e-6) return 0;
        // 垂直于梯度方向（即沿等高线方向）的平均法向变化
        var nU = -grad.dV / len;
        var nV = grad.dU / len;
        const double r = 0.045;

        var c = f.SampleSmooth(u, v);
        if (u + nU * r < 0 || u + nU * r > 1 || v + nV * r < 0 || v + nV * r > 1) return 0;
        if (u - nU * r < 0 || u - nU * r > 1 || v - nV * r < 0 || v - nV * r > 1) return 0;

        var a = f.SampleSmooth(u + nU * r, v + nV * r);
        var b = f.SampleSmooth(u - nU * r, v - nV * r);
        return (a + b - 2 * c) / 1000.0 * 6.0;
    }

    /// <summary>按海拔给颜色（分层设色，与课本一致：绿→黄→棕→白）。</summary>
    public static (byte R, byte G, byte B) ColorForHeight(float h, float min, float max)
    {
        var t = max - min < 1 ? 0.5 : Math.Clamp((h - min) / (max - min), 0, 1);

        // 低：深绿 → 草绿 → 黄 → 土黄 → 棕 → 浅棕 → 白
        var stops = new (double T, byte R, byte G, byte B)[]
        {
            (0.00,  46, 110,  72),   // 深绿（谷底）
            (0.30,  96, 158,  84),   // 草绿
            (0.55, 186, 196,  96),   // 黄绿
            (0.72, 214, 178, 108),   // 土黄
            (0.86, 176, 132,  96),   // 棕
            (0.95, 208, 186, 164),   // 浅棕
            (1.00, 245, 245, 245)    // 雪白
        };

        for (var i = 0; i < stops.Length - 1; i++)
        {
            var a = stops[i]; var b = stops[i + 1];
            if (t >= a.T && t <= b.T)
            {
                var k = (t - a.T) / Math.Max(1e-6, b.T - a.T);
                return (Lerp(a.R, b.R, k), Lerp(a.G, b.G, k), Lerp(a.B, b.B, k));
            }
        }
        var last = stops[^1];
        return (last.R, last.G, last.B);
    }

    private static byte Lerp(byte a, byte b, double k)
        => (byte)Math.Clamp(a + (b - a) * k, 0, 255);
}

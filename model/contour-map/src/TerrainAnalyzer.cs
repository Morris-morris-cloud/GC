using System;
using System.Collections.Generic;

namespace GC.ContourMap;

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

    /// <summary>
    /// 教科书规范的**分层设色**：按固定高程档位套用固定色阶，档内不渐变。
    ///
    /// 人教版七年级上册的分层设色色阶（自低到高）：
    ///   深绿 → 浅绿 → 绿 → 浅黄 → 黄 → 浅褐 → 褐 → 浅棕 → 白
    /// 关键点：**同一档内颜色完全相同**，这样一眼就能数出有几层，
    /// 而不是课本上那种连续渐变。
    /// </summary>
    public static (byte R, byte G, byte B) ColorForHeight(float h, float min, float max)
    {
        // 按绝对高程分档（教科书就是这么画的：看的是海拔，不是相对高度）
        if (h < 200) return (0x1E, 0x6B, 0x3A);      // 深绿：< 200 m
        if (h < 400) return (0x4E, 0x9E, 0x4E);      // 浅绿：200–400
        if (h < 600) return (0xA8, 0xC6, 0x66);      // 绿：400–600
        if (h < 800) return (0xE8, 0xE2, 0x9A);      // 浅黄：600–800
        if (h < 1000) return (0xE0, 0xC0, 0x5A);     // 黄：800–1000
        if (h < 1200) return (0xC8, 0x9A, 0x63);     // 浅褐：1000–1200
        if (h < 1400) return (0xA8, 0x74, 0x4C);     // 褐：1200–1400
        if (h < 1600) return (0xC8, 0xA8, 0x8C);     // 浅棕：1400–1600
        return (0xF5, 0xF5, 0xF0);                   // 白：> 1600（雪线以上）
    }

    /// <summary>
    /// 3D 立体视图用的连续设色：与俯视图同一套色阶，但在档位之间线性过渡。
    /// 立体视图的色块是按三角面着色的，直接用硬分档会在坡面上切出锯齿状阶梯，
    /// 看起来像渲染错误；俯视图仍保留硬分档（那才是课本的分层设色）。
    /// </summary>
    public static (byte R, byte G, byte B) ColorForHeightSmooth(float h)
    {
        // 每个档位的代表色（取档位中心），由低到高
        (byte R, byte G, byte B)[] stops =
        {
            (0x1E, 0x6B, 0x3A), (0x4E, 0x9E, 0x4E), (0xA8, 0xC6, 0x66),
            (0xE8, 0xE2, 0x9A), (0xE0, 0xC0, 0x5A), (0xC8, 0x9A, 0x63),
            (0xA8, 0x74, 0x4C), (0xC8, 0xA8, 0x8C), (0xF5, 0xF5, 0xF0)
        };
        const float band = 200f;
        var p = Math.Clamp(h / band - 0.5f, 0f, stops.Length - 1f);   // 0 => 档心 100 m
        var i = (int)Math.Floor(p);
        var j = Math.Min(i + 1, stops.Length - 1);
        var t = p - i;
        t = t * t * (3 - 2 * t);                                      // 平滑过渡
        return (Lerp(stops[i].R, stops[j].R, t),
                Lerp(stops[i].G, stops[j].G, t),
                Lerp(stops[i].B, stops[j].B, t));
    }

    /// <summary>该颜色属于哪一档（用于图例）。</summary>
    public static string BandName(float h) => h switch
    {
        < 200 => "200 米以下",
        < 400 => "200–400 米",
        < 600 => "400–600 米",
        < 800 => "600–800 米",
        < 1000 => "800–1000 米",
        < 1200 => "1000–1200 米",
        < 1400 => "1200–1400 米",
        < 1600 => "1400–1600 米",
        _ => "1600 米以上"
    };

    private static byte Lerp(byte a, byte b, double k)
        => (byte)Math.Clamp(a + (b - a) * k, 0, 255);

    /// <summary>
    /// 归一化坡度（0=平缓，1=陡峭），用于「疏密判坡」着色。
    /// 以有限差分求地面梯度，并按本模型的典型高差归一化。
    /// </summary>
    public static double SlopeAt(TerrainField f, double u, double v)
    {
        const double h = 0.006;
        var du = (f.SampleSmooth(u + h, v) - f.SampleSmooth(u - h, v)) / (2 * h);
        var dv = (f.SampleSmooth(u, v + h) - f.SampleSmooth(u, v - h)) / (2 * h);
        var grad = Math.Sqrt(du * du + dv * dv);   // 米 / 归一化距离
        // 按本图实际起伏自适应归一化：跨度越大，同样的相对坡度对应更大的梯度。
        // 固定阈值会让所有山体都顶到 1（整片红），判坡就失效了。
        var span = Math.Max(60f, f.MaxHeight - f.MinHeight);
        return Math.Clamp(grad / (span * 2.6), 0, 1);
    }
}

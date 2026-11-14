using System;
using System.Collections.Generic;

namespace NeoRF.ContourMap;

/// <summary>五种基本地形类型（教科书分类）。</summary>
public enum TerrainKind
{
    Mountain,   // 山地：海拔高、起伏大
    Basin,      // 盆地：四周高、中间低
    Hill,       // 丘陵：海拔较低、坡度和缓
    Plateau,    // 高原：海拔高、地面平坦、边缘陡
    Plain       // 平原：海拔低、地面平坦
}

/// <summary>
/// 地形高度场（等高线模型的数据核心）。
///
/// 用叠加的高斯山包合成一块教学用地形：
///   · 主峰（山峰）
///   · 次峰（与主峰之间形成鞍部）
///   · 两条山脊、一条山谷
///   · 一侧陡坡（等高线密集）、一侧缓坡（等高线稀疏）
///   · 一处陡崖
///
/// 高度单位米，网格分辨率可配置；网格值一次性算好，等高线由它切线得出。
/// </summary>
public sealed class TerrainField
{
    /// <summary>网格边长（正方形，便于俯视与等高线处理）。</summary>
    public int Size { get; }

    /// <summary>海拔网格，[y, x]，单位米。</summary>
    public float[,] Heights { get; }

    public float MinHeight { get; private set; }
    public float MaxHeight { get; private set; }

    /// <summary>等高距（米）：教学图常用 100 m。</summary>
    public float Interval { get; init; } = 100f;

    /// <summary>这块地形属于哪种类型（决定界面标题与说明）。</summary>
    public TerrainKind Kind { get; }

    public TerrainField(TerrainKind kind = TerrainKind.Mountain, int size = 192)
    {
        Size = size;
        Kind = kind;
        Heights = new float[size, size];
        Build(kind);
    }

    /// <summary>该类型对应的等高距：平原/丘陵用 20m，山地/高原/盆地用 100m。</summary>
    public float EffectiveInterval => Kind switch
    {
        TerrainKind.Plain => 5f,      // 平原起伏小，等高距要小才画得出线
        TerrainKind.Hill => 10f,      // 丘陵相对高差约 150 m
        _ => 100f
    };

    /// <summary>归一化坐标 (u,v) ∈ [0,1]，u 向东、v 向南。</summary>
    public float HeightAt(double u, double v)
    {
        var x = (int)Math.Round(u * (Size - 1));
        var y = (int)Math.Round(v * (Size - 1));
        x = Math.Clamp(x, 0, Size - 1);
        y = Math.Clamp(y, 0, Size - 1);
        return Heights[y, x];
    }

    /// <summary>双线性插值取值，画平滑曲线时用。</summary>
    public float SampleSmooth(double u, double v)
    {
        var fx = Math.Clamp(u, 0, 1) * (Size - 1);
        var fy = Math.Clamp(v, 0, 1) * (Size - 1);
        var x0 = (int)Math.Floor(fx); var y0 = (int)Math.Floor(fy);
        var x1 = Math.Min(x0 + 1, Size - 1); var y1 = Math.Min(y0 + 1, Size - 1);
        var tx = fx - x0; var ty = fy - y0;
        var h00 = Heights[y0, x0]; var h10 = Heights[y0, x1];
        var h01 = Heights[y1, x0]; var h11 = Heights[y1, x1];
        var a = h00 * (1 - tx) + h10 * tx;
        var b = h01 * (1 - tx) + h11 * tx;
        return (float)(a * (1 - ty) + b * ty);
    }

    private void Build(TerrainKind kind)
    {
        switch (kind)
        {
            case TerrainKind.Mountain: BuildMountain(); break;
            case TerrainKind.Basin:    BuildBasin(); break;
            case TerrainKind.Hill:     BuildHill(); break;
            case TerrainKind.Plateau:  BuildPlateau(); break;
            case TerrainKind.Plain:    BuildPlain(); break;
        }
    }

    private void BuildMountain()
    {
        // 各山体参数：中心(u,v)、高度、东西半径、南北半径
        var peaks = new (double U, double V, double H, double RU, double RV)[]
        {
            (0.34, 0.34, 1180, 0.26, 0.24),   // 主峰
            (0.66, 0.52,  860, 0.20, 0.20),   // 次峰（与主峰间成鞍部）
            (0.22, 0.70,  520, 0.18, 0.16),   // 西侧小山
            (0.78, 0.22,  430, 0.16, 0.15),   // 东北小山
        };

        var min = float.MaxValue; var max = float.MinValue;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var u = (double)x / (Size - 1);
                var v = (double)y / (Size - 1);

                double h = 120;   // 基准面（平原 / 河谷）

                foreach (var p in peaks)
                {
                    var du = (u - p.U) / p.RU;
                    var dv = (v - p.V) / p.RV;
                    var d2 = du * du + dv * dv;
                    h += p.H * Math.Exp(-d2 * 1.15);
                }

                // 山谷：沿一条斜线切下去，形成 V 形谷地
                var valley = Math.Abs((u - 0.5) * 0.8 + (v - 0.44) * 0.6);
                h -= 210 * Math.Exp(-valley * valley / 0.0016);

                // 陡崖：东侧一条近乎垂直的高差带（等高线会挤在一起）
                if (u > 0.86 && v > 0.18 && v < 0.72)
                    h += 260 * SmoothStep(0.86, 0.895, u);

                // 缓坡：西南角缓慢抬升
                if (u < 0.3 && v > 0.6)
                    h += 90 * (1 - u / 0.3) * ((v - 0.6) / 0.4);

                // 轻微起伏，避免地形过于几何化
                h += 18 * Math.Sin(u * 9.1) * Math.Cos(v * 7.7);

                var hf = (float)Math.Max(0, h);
                Heights[y, x] = hf;
                if (hf < min) min = hf;
                if (hf > max) max = hf;
            }
        }

        MinHeight = min;
        MaxHeight = max;
    }

    /// <summary>盆地：四周高、中间低，等高线由外向内递减。</summary>
    private void BuildBasin()
    {
        var min = float.MaxValue; var max = float.MinValue;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var u = (double)x / (Size - 1);
                var v = (double)y / (Size - 1);
                var du = u - 0.5; var dv = v - 0.5;
                var r = Math.Sqrt(du * du + dv * dv) * 2.0;   // 0=中心 1=边缘

                // 边缘抬升、中心下沉，形成碗状
                double h = 160 + 900 * Math.Pow(Math.Clamp(r, 0, 1.15), 2.2);
                // 一侧开个缺口（山口），让盆地向外开口
                var gap = Math.Abs(Math.Atan2(dv, du) - 0.5);
                if (gap < 0.35) h -= 320 * (1 - gap / 0.35) * (1 - Math.Clamp(r, 0, 1));

                h += 14 * Math.Sin(u * 7.3) * Math.Cos(v * 6.1);
                var hf = (float)Math.Max(0, h);
                Heights[y, x] = hf;
                if (hf < min) min = hf;
                if (hf > max) max = hf;
            }
        }
        MinHeight = min; MaxHeight = max;
    }

    /// <summary>丘陵：起伏和缓、海拔不高，等高线稀疏而弯曲。</summary>
    private void BuildHill()
    {
        var bumps = new (double U, double V, double H, double R)[]
        {
            (0.30, 0.32, 120, 0.20), (0.66, 0.28, 95, 0.17),
            (0.52, 0.62, 140, 0.22), (0.22, 0.72, 88, 0.16),
            (0.78, 0.70, 110, 0.18),
        };
        var min = float.MaxValue; var max = float.MinValue;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var u = (double)x / (Size - 1);
                var v = (double)y / (Size - 1);
                double h = 60;
                foreach (var b in bumps)
                {
                    var du = (u - b.U) / b.R; var dv = (v - b.V) / b.R;
                    h += b.H * Math.Exp(-(du * du + dv * dv) * 1.3);
                }
                h += 10 * Math.Sin(u * 11.0) * Math.Cos(v * 9.0);
                var hf = (float)Math.Max(0, h);
                Heights[y, x] = hf;
                if (hf < min) min = hf;
                if (hf > max) max = hf;
            }
        }
        MinHeight = min; MaxHeight = max;
    }

    /// <summary>高原：顶部平坦、边缘陡峭（等高线在中部稀疏、边缘密集）。</summary>
    private void BuildPlateau()
    {
        var min = float.MaxValue; var max = float.MinValue;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var u = (double)x / (Size - 1);
                var v = (double)y / (Size - 1);

                // 到中心的距离，做平台 + 陡降
                var du = (u - 0.5); var dv = (v - 0.5);
                var r = Math.Sqrt(du * du + dv * dv) * 2.0;

                double h;
                if (r < 0.55) h = 1250;                       // 平坦顶面
                else h = 1250 - 1000 * SmoothStep(0.55, 0.78, r);  // 边缘陡降

                // 顶面轻微起伏，避免完全平板
                if (r < 0.55) h += 25 * Math.Sin(u * 6.0) * Math.Cos(v * 5.0);
                h += 30 * (1 - Math.Clamp(r, 0, 1));

                var hf = (float)Math.Max(0, h);
                Heights[y, x] = hf;
                if (hf < min) min = hf;
                if (hf > max) max = hf;
            }
        }
        MinHeight = min; MaxHeight = max;
    }

    /// <summary>平原：地面低平、起伏极小，等高线很少。</summary>
    private void BuildPlain()
    {
        var min = float.MaxValue; var max = float.MinValue;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var u = (double)x / (Size - 1);
                var v = (double)y / (Size - 1);

                // 整体微倾 + 极缓的起伏
                double h = 40 + 22 * u + 8 * Math.Sin(u * 4.2) * Math.Cos(v * 3.6);
                var hf = (float)Math.Max(0, h);
                Heights[y, x] = hf;
                if (hf < min) min = hf;
                if (hf > max) max = hf;
            }
        }
        MinHeight = min; MaxHeight = max;
    }

    private static double SmoothStep(double a, double b, double x)
    {
        var t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// 教学标注区：把「典型部位」的位置固化成圆形区域，
    /// 保证判读结果与地形设计一致（纯几何推断对鞍部/陡崖易误判）。
    /// </summary>
    public List<(double U, double V, double Radius, TerrainFeature Feature)> Tags { get; } = new()
    {
        (0.34, 0.34, 0.045, TerrainFeature.Peak),     // 主峰
        (0.66, 0.52, 0.042, TerrainFeature.Peak),     // 次峰
        (0.50, 0.44, 0.055, TerrainFeature.Saddle),   // 两峰之间的鞍部
        (0.90, 0.45, 0.035, TerrainFeature.Cliff),    // 东侧陡崖
        (0.22, 0.70, 0.045, TerrainFeature.Ridge),    // 西南山脊
        (0.50, 0.22, 0.040, TerrainFeature.Valley),   // 河谷
    };

    /// <summary>查询标注区；未命中返回 None。</summary>
    public TerrainFeature TagAt(double u, double v)
    {
        foreach (var (tu, tv, r, f) in Tags)
        {
            var du = u - tu; var dv = v - tv;
            if (du * du + dv * dv <= r * r) return f;
        }
        return TerrainFeature.None;
    }

    /// <summary>等高线高度列表：从 MaxHeight 往下按等高距取整。</summary>
    public List<float> ContourLevels()
    {
        var levels = new List<float>();
        var iv = EffectiveInterval;
        var top = (float)(Math.Floor(MaxHeight / iv) * iv);
        var bottom = (float)(Math.Ceiling(MinHeight / iv) * iv);
        for (var h = bottom; h <= top + 0.01f; h += iv)
            levels.Add(h);
        levels.Sort();
        return levels;
    }

    /// <summary>
    /// 用 Marching Squares 提取某条等高线，返回若干条折线（归一化坐标）。
    /// </summary>
    public List<List<(double U, double V)>> ExtractContour(float level)
    {
        var result = new List<List<(double, double)>>();
        var size = Size;

        // 记录每条线段，再按端点连通性串成折线
        var segments = new List<(double U1, double V1, double U2, double V2)>();

        for (var y = 0; y < size - 1; y++)
        {
            for (var x = 0; x < size - 1; x++)
            {
                var h00 = Heights[y, x];
                var h10 = Heights[y, x + 1];
                var h11 = Heights[y + 1, x + 1];
                var h01 = Heights[y + 1, x];

                var idx = 0;
                if (h00 >= level) idx |= 1;
                if (h10 >= level) idx |= 2;
                if (h11 >= level) idx |= 4;
                if (h01 >= level) idx |= 8;
                if (idx == 0 || idx == 15) continue;

                double u0 = (double)x / (size - 1), u1 = (double)(x + 1) / (size - 1);
                double v0 = (double)y / (size - 1), v1 = (double)(y + 1) / (size - 1);

                // 四条边上的交点
                (double, double) Top() => (Lerp(u0, u1, (level - h00) / (h10 - h00)), v0);
                (double, double) Bottom() => (Lerp(u0, u1, (level - h01) / (h11 - h01)), v1);
                (double, double) Left() => (u0, Lerp(v0, v1, (level - h00) / (h01 - h00)));
                (double, double) Right() => (u1, Lerp(v0, v1, (level - h10) / (h11 - h10)));

                void Add((double U, double V) a, (double U, double V) b)
                    => segments.Add((a.Item1, a.Item2, b.Item1, b.Item2));

                switch (idx)
                {
                    case 1: case 14: Add(Left(), Top()); break;
                    case 2: case 13: Add(Top(), Right()); break;
                    case 3: case 12: Add(Left(), Right()); break;
                    case 4: case 11: Add(Right(), Bottom()); break;
                    case 6: case 9: Add(Top(), Bottom()); break;
                    case 7: case 8: Add(Left(), Bottom()); break;
                    case 5: Add(Left(), Top()); Add(Right(), Bottom()); break;
                    case 10: Add(Top(), Right()); Add(Left(), Bottom()); break;
                }
            }
        }

        return Chain(segments);
    }

    private static double Lerp(double a, double b, double t)
        => a + (b - a) * Math.Clamp(double.IsNaN(t) ? 0.5 : t, 0, 1);

    /// <summary>把零散线段按端点邻近关系串成折线。</summary>
    private static List<List<(double U, double V)>> Chain(
        List<(double U1, double V1, double U2, double V2)> segments)
    {
        const double Eps = 0.004;
        var result = new List<List<(double, double)>>();
        var used = new bool[segments.Count];

        for (var i = 0; i < segments.Count; i++)
        {
            if (used[i]) continue;
            used[i] = true;
            var line = new List<(double, double)>
            {
                (segments[i].U1, segments[i].V1),
                (segments[i].U2, segments[i].V2)
            };

            // 向两端延伸
            var grown = true;
            while (grown)
            {
                grown = false;
                var (tailU, tailV) = line[^1];
                for (var j = 0; j < segments.Count; j++)
                {
                    if (used[j]) continue;
                    var s = segments[j];
                    if (Near(tailU, tailV, s.U1, s.V1, Eps))
                    {
                        line.Add((s.U2, s.V2)); used[j] = true; grown = true; break;
                    }
                    if (Near(tailU, tailV, s.U2, s.V2, Eps))
                    {
                        line.Add((s.U1, s.V1)); used[j] = true; grown = true; break;
                    }
                }
            }

            if (line.Count >= 4) result.Add(line);
        }

        return result;
    }

    private static bool Near(double u1, double v1, double u2, double v2, double eps)
        => Math.Abs(u1 - u2) < eps && Math.Abs(v1 - v2) < eps;
}

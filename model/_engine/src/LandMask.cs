using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GC.EarthModels;

/// <summary>&lt;id&gt;.model/land.json —— 简化的大陆轮廓（经纬度多边形）。</summary>
public sealed class LandData
{
    [JsonPropertyName("polygons")] public List<LandPolygon> Polygons { get; set; } = new();

    public static LandData? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<LandData>(File.ReadAllText(path), ModelJson.Options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"解析地表数据失败 {path}: {ex.Message}");
            return null;
        }
    }
}

public sealed class LandPolygon
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    /// <summary>[[lon, lat], ...]</summary>
    [JsonPropertyName("points")] public List<double[]> Points { get; set; } = new();
}

/// <summary>
/// 等经纬度栅格地表掩膜（0=海洋 255=陆地），双线性采样。
/// 每个模型只生成一次，受内存预算约束。
/// </summary>
public sealed class LandMask
{
    public int Width { get; init; }
    public int Height { get; init; }
    public byte[] Bits { get; init; } = Array.Empty<byte>();

    public long ByteSize => Bits.LongLength;

    public static LandMask Build(LandData data, int width = 1440, int height = 720)
    {
        var bits = new byte[width * height];
        foreach (var poly in data.Polygons)
        {
            if (poly.Points.Count < 3) continue;
            var pts = Unwrap(poly.Points);
            if (pts.Count < 3) continue;
            for (var shift = -1; shift <= 1; shift++)
                Rasterize(bits, width, height, pts, shift * 360.0);
        }
        return new LandMask { Width = width, Height = height, Bits = bits };
    }

    private static List<(double Lon, double Lat)> Unwrap(List<double[]> points)
    {
        var result = new List<(double, double)>(points.Count);
        if (points.Count == 0 || points[0].Length < 2) return result;
        var prev = points[0][0];
        result.Add((prev, points[0][1]));
        for (var i = 1; i < points.Count; i++)
        {
            if (points[i].Length < 2) continue;
            var lon = points[i][0];
            while (lon - prev > 180) lon -= 360;
            while (prev - lon > 180) lon += 360;
            result.Add((lon, points[i][1]));
            prev = lon;
        }
        return result;
    }

    private static void Rasterize(byte[] bits, int w, int h, List<(double Lon, double Lat)> pts, double lonShift)
    {
        var n = pts.Count;
        var xs = new double[n];
        var ys = new double[n];
        double minY = double.MaxValue, maxY = double.MinValue;
        for (var i = 0; i < n; i++)
        {
            xs[i] = (pts[i].Lon + lonShift + 180.0) / 360.0 * w;
            ys[i] = (90.0 - pts[i].Lat) / 180.0 * h;
            if (ys[i] < minY) minY = ys[i];
            if (ys[i] > maxY) maxY = ys[i];
        }

        var y0 = Math.Max(0, (int)Math.Floor(minY));
        var y1 = Math.Min(h - 1, (int)Math.Ceiling(maxY));
        var crossings = new List<double>(16);

        for (var y = y0; y <= y1; y++)
        {
            var sy = y + 0.5;
            crossings.Clear();
            for (var i = 0; i < n; i++)
            {
                var j = (i + 1) % n;
                double ya = ys[i], yb = ys[j];
                if (!((ya <= sy && yb > sy) || (yb <= sy && ya > sy))) continue;
                var t = (sy - ya) / (yb - ya);
                crossings.Add(xs[i] + t * (xs[j] - xs[i]));
            }
            if (crossings.Count < 2) continue;
            crossings.Sort();
            for (var k = 0; k + 1 < crossings.Count; k += 2)
            {
                var xa = (int)Math.Ceiling(crossings[k] - 0.5);
                var xb = (int)Math.Floor(crossings[k + 1] - 0.5);
                if (xb < xa) continue;
                if (xb - xa >= w - 1)
                {
                    for (var x = 0; x < w; x++) bits[y * w + x] = 255;
                    continue;
                }
                for (var x = xa; x <= xb; x++)
                {
                    var mx = x % w;
                    if (mx < 0) mx += w;
                    bits[y * w + mx] = 255;
                }
            }
        }
    }

    /// <summary>返回 0..1 的陆地覆盖度。</summary>
    public double Sample(double lonDeg, double latDeg)
    {
        var u = (lonDeg + 180.0) / 360.0 * Width - 0.5;
        var v = (90.0 - latDeg) / 180.0 * Height - 0.5;
        var x0 = (int)Math.Floor(u);
        var y0 = (int)Math.Floor(v);
        var fx = u - x0;
        var fy = v - y0;
        double sum = 0;
        for (var dy = 0; dy < 2; dy++)
        {
            var y = y0 + dy;
            if (y < 0) y = 0;
            if (y >= Height) y = Height - 1;
            var wy = dy == 0 ? 1 - fy : fy;
            for (var dx = 0; dx < 2; dx++)
            {
                var x = x0 + dx;
                var mx = x % Width;
                if (mx < 0) mx += Width;
                var wx = dx == 0 ? 1 - fx : fx;
                sum += Bits[y * Width + mx] * wx * wy;
            }
        }
        return sum / 255.0;
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Media;

namespace GC.EarthModels;

/// <summary>
/// 预烘焙的等经纬度地表贴图：反照率 + 高度 + 冰盖 + 云层 + 夜间灯光。
/// 只在模型加载时算一次，之后每帧只做采样 —— 比逐像素跑噪声快一个数量级，
/// 而且能画出「真实一点」的地表：海洋深度、沙漠/雨林/针叶林带、山脉阴影、极地冰盖。
/// </summary>
public sealed class PlanetTexture
{
    public const int DefaultWidth = 1024;

    public int Width { get; }
    public int Height { get; }

    /// <summary>打包的反照率（0xRRGGBB），每像素一次双线性即可拿三通道。</summary>
    public uint[] Albedo { get; }
    /// <summary>地形高度（0=海平面，255=高山）</summary>
    public byte[] Elevation { get; }
    /// <summary>云量</summary>
    public byte[] Cloud { get; }
    /// <summary>夜间灯光</summary>
    public byte[] Lights { get; }
    /// <summary>预烘焙的地形坡度：东向 / 北向高度梯度（加载时算一次，每帧直接采样）。</summary>
    public float[] SlopeEast { get; }
    public float[] SlopeNorth { get; }

    public long ByteSize => (long)(Albedo.Length * 4 + Elevation.Length + Cloud.Length + Lights.Length
                                  + SlopeEast.Length * 4 + SlopeNorth.Length * 4);

    private PlanetTexture(int width, int height)
    {
        Width = width;
        Height = height;
        var count = width * height;
        Albedo = new uint[count];
        Elevation = new byte[count];
        Cloud = new byte[count];
        Lights = new byte[count];
        SlopeEast = new float[count];
        SlopeNorth = new float[count];
    }

    public static PlanetTexture Build(LandData? landData, int width, Color ocean, Color land, Color ice,
        Action<double>? progress = null)
    {
        width = Math.Clamp(width, 256, 2048);
        var height = width / 2;
        var texture = new PlanetTexture(width, height);

        progress?.Invoke(0.08);
        var mask = landData == null ? null : LandMask.Build(landData, width, height);
        progress?.Invoke(0.28);

        // 到海岸的距离：海里用来做深度，陆地上用来做大陆性（内陆更干）
        var landDistance = new float[width * height];
        var oceanDistance = new float[width * height];
        ComputeDistances(mask, width, height, landDistance, oceanDistance);
        progress?.Invoke(0.42);

        Parallel.For(0, height, y =>
        {
            var lat = 90.0 - (y + 0.5) * 180.0 / height;
            var latAbs = MathF.Abs((float)lat);
            var rowBase = y * width;
            for (var x = 0; x < width; x++)
            {
                var lon = (x + 0.5) * 360.0 / width - 180.0;
                var index = rowBase + x;
                var isLand = mask == null || mask.Bits[index] > 127;

                var elevation = Fbm(lon * 2.1, lat * 2.1, 4, 0x51A3);
                var moisture = Fbm(lon * 1.35 + 40, lat * 1.35 - 20, 3, 0x9E2B);
                var detail = Fbm(lon * 0.85, lat * 0.85, 3, 0x2C77);

                if (isLand)
                {
                    // 内陆更干，靠海更湿
                    var inland = Math.Clamp(landDistance[index] / (width * 0.06f), 0f, 1f);
                    var wet = Math.Clamp(moisture * 0.78f + (1f - inland) * 0.16f
                                         - MathF.Max(0f, latAbs - 45f) / 90f, 0f, 1f);

                    var (r, g, b) = LandPalette(latAbs, wet, inland, elevation, detail, land, ice);
                    texture.Albedo[index] = Pack(r, g, b);
                    texture.Elevation[index] = (byte)Math.Clamp(elevation * 235f + detail * 20f, 0, 255);

                    // 夜间灯光：沿海平原、中低纬，稀疏分布
                    // 夜间灯光：低频的城市群辉光（高频点会在小尺寸球面上变成噪点）
                    var cityGlow = Smooth(0.55f, 0.92f, Fbm(lon * 1.7, lat * 1.7, 3, 0x7A31));
                    if (latAbs < 60 && inland < 0.55f && elevation < 0.75f)
                        texture.Lights[index] = ToByte(cityGlow * 0.55f);
                }
                else
                {
                    // 海洋：按离岸距离做深浅
                    var depth = Math.Clamp(oceanDistance[index] / (width * 0.09f), 0f, 1f);
                    depth = (float)Math.Pow(depth, 0.65);
                    var shallowR = Math.Min(1f, ocean.R / 255f + 0.16f);
                    var shallowG = Math.Min(1f, ocean.G / 255f + 0.22f);
                    var shallowB = Math.Min(1f, ocean.B / 255f + 0.16f);
                    var deepR = ocean.R / 255f * 0.32f;
                    var deepG = ocean.G / 255f * 0.40f;
                    var deepB = ocean.B / 255f * 0.52f;
                    var wave = (detail - 0.5f) * 0.035f;

                    texture.Albedo[index] = Pack(
                        ToByte(shallowR + (deepR - shallowR) * depth + wave),
                        ToByte(shallowG + (deepG - shallowG) * depth + wave),
                        ToByte(shallowB + (deepB - shallowB) * depth + wave));
                    texture.Elevation[index] = 0;
                }

                // 极地冰盖：纬度 + 噪声边界，海洋也能结冰
                var iceEdge = 62f + (detail - 0.5f) * 9f;
                var iceAmt = Smooth(iceEdge - 7f, iceEdge + 6f, latAbs);
                if (iceAmt > 0.001f)
                {
                    var iceR = ice.R / 255f;
                    var iceG = ice.G / 255f;
                    var iceB = ice.B / 255f;
                    var packed = texture.Albedo[index];
                    texture.Albedo[index] = Pack(
                        ToByte(Lerp(((packed >> 16) & 0xFF) / 255f, iceR, iceAmt)),
                        ToByte(Lerp(((packed >> 8) & 0xFF) / 255f, iceG, iceAmt)),
                        ToByte(Lerp((packed & 0xFF) / 255f, iceB, iceAmt)));
                }

                // 云：多个尺度的 fbm，赤道与中纬多云
                var cloudBase = Fbm(lon * 0.55, lat * 0.7, 3, 0x1144);
                var band = 0.5f + 0.5f * MathF.Cos(latAbs / 90f * MathF.PI * 3.1f);
                var cloud = Smooth(0.48f, 0.84f, cloudBase * 0.86f + band * 0.14f);
                texture.Cloud[index] = ToByte(cloud * 0.72f);
            }
        });

        // 云层做一次模糊，避免小尺寸球面上采样出噪点
        Blur(texture.Cloud, width, height, 2);

        // 预烘焙地形坡度：渲染时不必每像素再取 4 次高度
        BakeSlopes(texture, width, height);
        progress?.Invoke(0.62);
        return texture;
    }

    private static (byte R, byte G, byte B) LandPalette(float latAbs, float wet, float inland, float elevation,
        float detail, Color landBase, Color ice)
    {
        // 基色随纬度：赤道雨林 -> 温带 -> 针叶林 -> 苔原
        float r, g, b;
        var tropical = (1f - Smooth(8f, 24f, latAbs));
        var temperate = Smooth(20f, 34f, latAbs) * (1f - Smooth(46f, 60f, latAbs));
        var boreal = Smooth(46f, 60f, latAbs) * (1f - Smooth(62f, 70f, latAbs));
        var tundra = Smooth(60f, 72f, latAbs);

        r = 0.13f; g = 0.36f; b = 0.17f;                     // 温带草原绿
        r = Lerp(r, 0.10f, tropical); g = Lerp(g, 0.42f, tropical); b = Lerp(b, 0.19f, tropical);
        r = Lerp(r, 0.17f, boreal); g = Lerp(g, 0.28f, boreal); b = Lerp(b, 0.17f, boreal);
        r = Lerp(r, 0.34f, tundra); g = Lerp(g, 0.36f, tundra); b = Lerp(b, 0.31f, tundra);

        // 湿润度：干 -> 沙漠/草原
        var dry = Math.Clamp(1f - wet * 0.95f, 0f, 1f);
        var desertBand = Math.Clamp(1f - MathF.Abs(latAbs - 24f) / 13f, 0f, 1f);
        var desert = Math.Clamp(dry * 0.72f + desertBand * 0.62f, 0f, 1f);
        r = Lerp(r, 0.72f, desert * 0.85f);
        g = Lerp(g, 0.60f, desert * 0.85f);
        b = Lerp(b, 0.38f, desert * 0.85f);

        // 高原/山地：岩石和雪
        var rock = Smooth(0.62f, 0.86f, elevation);
        r = Lerp(r, 0.44f, rock * 0.7f);
        g = Lerp(g, 0.40f, rock * 0.7f);
        b = Lerp(b, 0.34f, rock * 0.7f);

        var snow = Smooth(0.86f, 0.97f, elevation + (latAbs - 40f) / 220f);
        r = Lerp(r, ice.R / 255f, snow);
        g = Lerp(g, ice.G / 255f, snow);
        b = Lerp(b, ice.B / 255f, snow);

        // 细节扰动，避免大面积纯色
        var grain = (detail - 0.5f) * 0.07f;
        var baseMix = 0.25f;
        return (
            ToByte(Math.Clamp(r + grain + (landBase.R / 255f - 0.3f) * baseMix * 0.2f, 0f, 1f)),
            ToByte(Math.Clamp(g + grain + (landBase.G / 255f - 0.45f) * baseMix * 0.2f, 0f, 1f)),
            ToByte(Math.Clamp(b + grain + (landBase.B / 255f - 0.3f) * baseMix * 0.2f, 0f, 1f)));
    }

    /// <summary>可分离盒式模糊（原地），用于软化云层。</summary>
    private static void Blur(byte[] data, int width, int height, int radius)
    {
        var temp = new byte[data.Length];
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var sum = 0;
                var count = 0;
                for (var k = -radius; k <= radius; k++)
                {
                    var sx = (x + k + width) % width;
                    sum += data[row + sx];
                    count++;
                }
                temp[row + x] = (byte)(sum / count);
            }
        }
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                var sum = 0;
                var count = 0;
                for (var k = -radius; k <= radius; k++)
                {
                    var sy = Math.Clamp(y + k, 0, height - 1);
                    sum += temp[sy * width + x];
                    count++;
                }
                data[y * width + x] = (byte)(sum / count);
            }
        }
    }

    /// <summary>由高度图算东西/南北梯度，供球面光照直接使用。</summary>
    private static void BakeSlopes(PlanetTexture t, int width, int height)
    {
        const float relief = 0.055f * 90f;
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            var up = Math.Max(0, y - 1) * width;
            var down = Math.Min(height - 1, y + 1) * width;
            for (var x = 0; x < width; x++)
            {
                var left = (x - 1 + width) % width;
                var right = (x + 1) % width;
                t.SlopeEast[row + x] = (t.Elevation[row + right] - t.Elevation[row + left]) / 255f * relief;
                t.SlopeNorth[row + x] = (t.Elevation[up + x] - t.Elevation[down + x]) / 255f * relief;
            }
        }
    }

    // ---------------------------------------------------------------- 距离场
    private static void ComputeDistances(LandMask? mask, int width, int height, float[] landDistance, float[] oceanDistance)
    {
        var count = width * height;
        const float big = 1e6f;
        for (var i = 0; i < count; i++)
        {
            var isLand = mask != null && mask.Bits[i] > 127;
            landDistance[i] = isLand ? big : -big;
            oceanDistance[i] = isLand ? -big : big;
        }

        // 两遍 chamfer：陆地到海洋的距离 / 海洋到陆地的距离
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                if (mask != null && mask.Bits[i] > 127)
                {
                    var best = big;
                    if (x > 0 && mask.Bits[i - 1] <= 127) best = 0.5f;
                    if (x < width - 1 && mask.Bits[i + 1] <= 127) best = Math.Min(best, 0.5f);
                    if (y > 0 && mask.Bits[i - width] <= 127) best = Math.Min(best, 0.5f);
                    if (y < height - 1 && mask.Bits[i + width] <= 127) best = Math.Min(best, 0.5f);
                    if (best < big) landDistance[i] = best;
                }
                if (mask != null && mask.Bits[i] <= 127)
                {
                    var best = big;
                    if (x > 0 && mask.Bits[i - 1] > 127) best = 0.5f;
                    if (x < width - 1 && mask.Bits[i + 1] > 127) best = Math.Min(best, 0.5f);
                    if (y > 0 && mask.Bits[i - width] > 127) best = Math.Min(best, 0.5f);
                    if (y < height - 1 && mask.Bits[i + width] > 127) best = Math.Min(best, 0.5f);
                    if (best < big) oceanDistance[i] = best;
                }
            }
        }

        // 正向传递
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                Propagate(landDistance, i, width, height, x, y, -1, 0, 1.4142f);
                Propagate(oceanDistance, i, width, height, x, y, -1, 0, 1.4142f);
                if (x > 0)
                {
                    landDistance[i] = Math.Min(landDistance[i], landDistance[i - 1] + 1f);
                    oceanDistance[i] = Math.Min(oceanDistance[i], oceanDistance[i - 1] + 1f);
                }
                if (y > 0)
                {
                    landDistance[i] = Math.Min(landDistance[i], landDistance[i - width] + 1f);
                    oceanDistance[i] = Math.Min(oceanDistance[i], oceanDistance[i - width] + 1f);
                    if (x > 0)
                    {
                        landDistance[i] = Math.Min(landDistance[i], landDistance[i - width - 1] + 1.4142f);
                        oceanDistance[i] = Math.Min(oceanDistance[i], oceanDistance[i - width - 1] + 1.4142f);
                    }
                    if (x < width - 1)
                    {
                        landDistance[i] = Math.Min(landDistance[i], landDistance[i - width + 1] + 1.4142f);
                        oceanDistance[i] = Math.Min(oceanDistance[i], oceanDistance[i - width + 1] + 1.4142f);
                    }
                }
            }
        }
        // 反向传递
        for (var y = height - 1; y >= 0; y--)
        {
            for (var x = width - 1; x >= 0; x--)
            {
                var i = y * width + x;
                if (x < width - 1)
                {
                    landDistance[i] = Math.Min(landDistance[i], landDistance[i + 1] + 1f);
                    oceanDistance[i] = Math.Min(oceanDistance[i], oceanDistance[i + 1] + 1f);
                }
                if (y < height - 1)
                {
                    landDistance[i] = Math.Min(landDistance[i], landDistance[i + width] + 1f);
                    oceanDistance[i] = Math.Min(oceanDistance[i], oceanDistance[i + width] + 1f);
                    if (x < width - 1)
                    {
                        landDistance[i] = Math.Min(landDistance[i], landDistance[i + width + 1] + 1.4142f);
                        oceanDistance[i] = Math.Min(oceanDistance[i], oceanDistance[i + width + 1] + 1.4142f);
                    }
                    if (x > 0)
                    {
                        landDistance[i] = Math.Min(landDistance[i], landDistance[i + width - 1] + 1.4142f);
                        oceanDistance[i] = Math.Min(oceanDistance[i], oceanDistance[i + width - 1] + 1.4142f);
                    }
                }
            }
        }

        for (var i = 0; i < count; i++)
        {
            landDistance[i] = MathF.Max(0f, landDistance[i]);
            oceanDistance[i] = MathF.Max(0f, oceanDistance[i]);
        }
    }

    private static void Propagate(float[] map, int index, int width, int height, int x, int y, int dx, int dy, float w)
    {
        var nx = x + dx;
        var ny = y + dy;
        if (nx < 0 || nx >= width || ny < 0 || ny >= height) return;
        var other = ny * width + nx;
        map[index] = Math.Min(map[index], map[other] + w);
    }

    // ---------------------------------------------------------------- 噪声
    /// <summary>值噪声 + 分形叠加（确定性，跨平台一致）。</summary>
    public static float Fbm(double x, double y, int octaves, int seed)
    {
        var amplitude = 1.0;
        var frequency = 1.0;
        var sum = 0.0;
        var norm = 0.0;
        for (var i = 0; i < octaves; i++)
        {
            sum += amplitude * ValueNoise(x * frequency, y * frequency, seed + i * 101);
            norm += amplitude;
            amplitude *= 0.5;
            frequency *= 2.07;
        }
        return (float)(sum / norm);
    }

    private static double ValueNoise(double x, double y, int seed)
    {
        var xi = (int)Math.Floor(x);
        var yi = (int)Math.Floor(y);
        var xf = x - xi;
        var yf = y - yi;
        var u = xf * xf * (3 - 2 * xf);
        var v = yf * yf * (3 - 2 * yf);
        var a = Hash(xi, yi, seed);
        var b = Hash(xi + 1, yi, seed);
        var c = Hash(xi, yi + 1, seed);
        var d = Hash(xi + 1, yi + 1, seed);
        return (a + (b - a) * u) * (1 - v) + (c + (d - c) * u) * v;
    }

    private static double Hash(int x, int y, int seed)
    {
        var h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (double)0xFFFFFF;
    }

    // ---------------------------------------------------------------- 采样
    public void Sample(double lon, double lat, out float r, out float g, out float b,
        out float elevation, out float cloud, out float lights,
        out float slopeEast, out float slopeNorth)
    {
        var u = (lon + 180.0) / 360.0 * Width - 0.5;
        var v = (90.0 - lat) / 180.0 * Height - 0.5;

        var x0 = (int)Math.Floor(u);
        var y0 = (int)Math.Floor(v);
        var fx = (float)(u - x0);
        var fy = (float)(v - y0);

        var x1 = x0 + 1;
        var y1 = y0 + 1;
        if (x0 < 0) x0 += Width; if (x1 < 0) x1 += Width;
        if (x0 >= Width) x0 -= Width; if (x1 >= Width) x1 -= Width;
        y0 = Math.Clamp(y0, 0, Height - 1);
        y1 = Math.Clamp(y1, 0, Height - 1);

        var i00 = y0 * Width + x0;
        var i10 = y0 * Width + x1;
        var i01 = y1 * Width + x0;
        var i11 = y1 * Width + x1;

        float w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;

        var p00 = Albedo[i00];
        var p10 = Albedo[i10];
        var p01 = Albedo[i01];
        var p11 = Albedo[i11];
        var rr = ((p00 >> 16) & 0xFF) * w00 + ((p10 >> 16) & 0xFF) * w10 + ((p01 >> 16) & 0xFF) * w01 + ((p11 >> 16) & 0xFF) * w11;
        var gg = ((p00 >> 8) & 0xFF) * w00 + ((p10 >> 8) & 0xFF) * w10 + ((p01 >> 8) & 0xFF) * w01 + ((p11 >> 8) & 0xFF) * w11;
        var bb = (p00 & 0xFF) * w00 + (p10 & 0xFF) * w10 + (p01 & 0xFF) * w01 + (p11 & 0xFF) * w11;
        r = rr / 255f;
        g = gg / 255f;
        b = bb / 255f;

        elevation = (Elevation[i00] * w00 + Elevation[i10] * w10 + Elevation[i01] * w01 + Elevation[i11] * w11) / 255f;
        cloud = (Cloud[i00] * w00 + Cloud[i10] * w10 + Cloud[i01] * w01 + Cloud[i11] * w11) / 255f;
        lights = Lights[i00] / 255f;
        slopeEast = SlopeEast[i00] * w00 + SlopeEast[i10] * w10 + SlopeEast[i01] * w01 + SlopeEast[i11] * w11;
        slopeNorth = SlopeNorth[i00] * w00 + SlopeNorth[i10] * w10 + SlopeNorth[i01] * w01 + SlopeNorth[i11] * w11;
    }

    /// <summary>只采样高度（法线扰动用）。</summary>
    public float SampleHeight(double lon, double lat)
    {
        var u = (lon + 180.0) / 360.0 * Width - 0.5;
        var v = (90.0 - lat) / 180.0 * Height - 0.5;
        var x = Math.Clamp((int)Math.Round(u), 0, Width - 1);
        var y = Math.Clamp((int)Math.Round(v), 0, Height - 1);
        return Elevation[y * Width + x] / 255f;
    }

    public float SampleCloud(double lon, double lat)
    {
        var u = (lon + 180.0) / 360.0 * Width - 0.5;
        var v = (90.0 - lat) / 180.0 * Height - 0.5;
        var x = Math.Clamp((int)Math.Round(u), 0, Width - 1);
        var y = Math.Clamp((int)Math.Round(v), 0, Height - 1);
        return Cloud[y * Width + x] / 255f;
    }

    public static byte ToByte(float v) => (byte)Math.Clamp((int)(v * 255f + 0.5f), 0, 255);
    private static uint Pack(byte r, byte g, byte b) => ((uint)r << 16) | ((uint)g << 8) | b;
    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);
    private static float Smooth(float edge0, float edge1, float x)
    {
        if (edge1 <= edge0) return x >= edge1 ? 1f : 0f;
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}

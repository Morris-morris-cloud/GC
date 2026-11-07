using System;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace NeoRF.EarthModels;

/// <summary>
/// 球体像素级渲染：正交投影 + 预烘焙地表纹理 + 地形光照 + 云层 + 夜灯 + 大气边缘光。
/// 纯 CPU 光栅、按行并行，不依赖 GPU，三平台效果一致。
/// </summary>
public static unsafe class SphereRaster
{
    /// <summary>把球体画进 WriteableBitmap（整张位图会被覆盖）。</summary>
    public static void Draw(WriteableBitmap bitmap, int size, Matrix4x4 cam, Matrix4x4 planet,
        Vector3 light, PlanetTexture texture, double cloudShift, bool showClouds)
    {
        using var framebuffer = bitmap.Lock();
        var stride = framebuffer.RowBytes / 4;
        Render((uint*)framebuffer.Address, stride, size, cam, planet, light, texture, cloudShift, showClouds);
    }

    public static void Render(uint* dst, int stride, int size, Matrix4x4 cam, Matrix4x4 planet,
        Vector3 light, PlanetTexture texture, double cloudShift, bool showClouds)
    {
        var radius = size * 0.5f - 1.0f;
        var center = size * 0.5f;
        var inv = 1f / radius;

        // 相机空间法线 -> 行星本地法线
        var combined = Matrix4x4.Multiply(planet, cam);
        var mt = Matrix4x4.Transpose(combined);
        float m11 = mt.M11, m12 = mt.M12, m13 = mt.M13;
        float m21 = mt.M21, m22 = mt.M22, m23 = mt.M23;
        float m31 = mt.M31, m32 = mt.M32, m33 = mt.M33;

        var lx = light.X; var ly = light.Y; var lz = light.Z;

        Parallel.For(0, size, y =>
        {
            var ny = (center - y - 0.5f) * inv;
            var row = dst + (long)y * stride;
            for (var x = 0; x < size; x++)
            {
                var nx = (x + 0.5f - center) * inv;
                var r2 = nx * nx + ny * ny;
                if (r2 >= 1f)
                {
                    row[x] = 0;
                    continue;
                }

                var nz = MathF.Sqrt(1f - r2);

                // 本地法线（用于取经纬度）
                var ux = nx * m11 + ny * m21 + nz * m31;
                var uy = nx * m12 + ny * m22 + nz * m32;
                var uz = nx * m13 + ny * m23 + nz * m33;

                var lat = MathF.Asin(Math.Clamp(uy, -1f, 1f)) * 57.2957795f;
                var lon = MathF.Atan2(ux, uz) * 57.2957795f;

                var cloudLon = lon + cloudShift;
                if (cloudLon > 180) cloudLon -= 360;
                if (cloudLon < -180) cloudLon += 360;

                texture.Sample(lon, lat, out var ar, out var ag, out var ab,
                    out var elevation, out var cloud, out var lights,
                    out var dEast, out var dNorth);

                // ---- 地形法线：局部基向量可由球面法线直接推出，不必再算 6 次三角函数
                //  点 P=(cosLat·sinLon, sinLat, cosLat·cosLon) = (ux,uy,uz)
                //  东向 = (cosLon,0,-sinLon)，北向 = (-sinLat·sinLon, cosLat, -sinLat·cosLon)
                var cosLat = MathF.Sqrt(MathF.Max(ux * ux + uz * uz, 1e-6f));
                var invCos = 1f / cosLat;
                var eastX = uz * invCos;
                var eastZ = -ux * invCos;
                var northX = -uy * ux * invCos;
                var northY = cosLat;
                var northZ = -uy * uz * invCos;

                var nLocalX = ux + eastX * dEast + northX * dNorth;
                var nLocalY = uy + northY * dNorth;
                var nLocalZ = uz + eastZ * dEast + northZ * dNorth;
                var nLen = MathF.Sqrt(nLocalX * nLocalX + nLocalY * nLocalY + nLocalZ * nLocalZ);
                if (nLen > 0.0001f) { nLocalX /= nLen; nLocalY /= nLen; nLocalZ /= nLen; }

                var nCamX = nLocalX * combined.M11 + nLocalY * combined.M21 + nLocalZ * combined.M31;
                var nCamY = nLocalX * combined.M12 + nLocalY * combined.M22 + nLocalZ * combined.M32;
                var nCamZ = nLocalX * combined.M13 + nLocalY * combined.M23 + nLocalZ * combined.M33;

                var lam = nx * lx + ny * ly + nz * lz;          // 几何光照（晨昏线）
                var bump = nCamX * lx + nCamY * ly + nCamZ * lz; // 地形光照
                var day = Smooth(-0.18f, 0.22f, lam);
                var terrainLight = 0.82f + 0.42f * MathF.Max(0f, bump - lam);

                var shade = (0.055f + 0.945f * day) * terrainLight;

                var br = ar * shade;
                var bg = ag * shade;
                var bb = ab * shade;

                // 海面高光（高度为 0 视为水面）
                if (elevation < 0.02f)
                {
                    var t2 = MathF.Max(0f, lam);
                    var t4 = t2 * t2 * t2 * t2;
                    var t8 = t4 * t4;
                    var t16 = t8 * t8;
                    var spec = t16 * t8 * t4 * t2 * t2 * 0.62f;
                    br += spec * 0.95f;
                    bg += spec * 0.98f;
                    bb += spec;
                }

                // 云层（随经度缓慢漂移）
                if (showClouds && cloud > 0.02f)
                {
                    var cloudLight = 0.10f + 0.90f * day;
                    var c = Math.Clamp(cloud * 0.92f, 0f, 1f);
                    var cloudR = 0.98f * cloudLight;
                    var cloudG = 0.985f * cloudLight;
                    var cloudB = 1.0f * cloudLight;
                    br += (cloudR - br) * c;
                    bg += (cloudG - bg) * c;
                    bb += (cloudB - bb) * c;
                }

                // 夜间灯光
                if (lights > 0.01f)
                {
                    var night = 1f - day;
                    var glow = lights * night * 0.35f;
                    br += glow * 0.95f;
                    bg += glow * 0.72f;
                    bb += glow * 0.38f;
                }

                // 大气边缘散射
                var rim = 1f - nz;
                rim *= rim;
                var litRim = rim * MathF.Max(0f, lam) * 0.98f;
                br += 0.30f * litRim;
                bg += 0.55f * litRim;
                bb += 1.00f * litRim;

                // 夜面极淡反照
                var nightSide = 1f - day;
                br += 0.008f * nightSide;
                bg += 0.013f * nightSide;
                bb += 0.028f * nightSide;

                var coverage = 1f;
                var edge = (1f - MathF.Sqrt(r2)) * radius;
                if (edge < 1f) coverage = MathF.Max(0f, edge);

                var a = (uint)(coverage * 255f);
                var r8 = (uint)(Math.Clamp(br, 0f, 1f) * 255f * coverage);
                var g8 = (uint)(Math.Clamp(bg, 0f, 1f) * 255f * coverage);
                var b8 = (uint)(Math.Clamp(bb, 0f, 1f) * 255f * coverage);
                row[x] = (a << 24) | (r8 << 16) | (g8 << 8) | b8;
            }
        });
    }

    private static float Smooth(float edge0, float edge1, float x)
    {
        if (edge1 <= edge0) return x >= edge1 ? 1f : 0f;
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}

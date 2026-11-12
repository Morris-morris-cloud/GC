using System;
using System.Collections.Generic;
using System.Numerics;

namespace NeoRF.EarthModels;

/// <summary>
/// 地理模型的球面几何工具：经纬度 ↔ 单位球坐标、正交投影到屏幕、
/// 以及把一条经纬度折线投影成「可见弧段」（背面自动裁掉）。
/// 纯 CPU、无依赖，供 GeographyView 画线稿用。
/// </summary>
public static class Geometry
{
    public const float DegToRad = (float)(Math.PI / 180.0);
    public const float RadToDeg = (float)(180.0 / Math.PI);

    /// <summary>经纬度 → 单位球坐标（y 轴为地轴，lon 向东为正）。</summary>
    public static Vector3 LatLonToUnit(double latDeg, double lonDeg)
    {
        var lat = (float)(latDeg * Math.PI / 180.0);
        var lon = (float)(lonDeg * Math.PI / 180.0);
        var cosLat = (float)Math.Cos(lat);
        return new Vector3(cosLat * (float)Math.Sin(lon), (float)Math.Sin(lat), cosLat * (float)Math.Cos(lon));
    }

    /// <summary>把世界坐标旋转到相机空间（先用 planet 旋转地球，再用 cam 转到屏幕）。</summary>
    public static Vector3 ToCamera(Vector3 world, Matrix4x4 planet, Matrix4x4 cam)
        => Vector3.Transform(Vector3.Transform(world, planet), cam);

    /// <summary>相机空间的正交投影：球心在原点，半径 radius。</summary>
    public static Vector2 Project(Vector3 camPos, float radius, float centerX, float centerY)
        => new(centerX + camPos.X * radius, centerY - camPos.Y * radius);

    /// <summary>相机空间里该点是否朝向观察者（z>0 为正面）。</summary>
    public static bool IsVisible(Vector3 camPos, float threshold = 0f) => camPos.Z > threshold;

    /// <summary>
    /// 把一条经纬度折线拆成若干段「可见」的屏幕折线。
    /// 跨越地平线的相邻两点用线性插值求出交点作为端点，保证线不会穿过球体背面。
    /// </summary>
    public static List<List<Vector2>> ProjectPolyline(
        IReadOnlyList<double[]> latLonPoints,
        Matrix4x4 planet, Matrix4x4 cam,
        float radius, float centerX, float centerY,
        bool closed = false)
    {
        // 兼容旧调用：按需转换（点位少的分支，如洲界）
        var unit = new Vector3[latLonPoints.Count];
        for (var i = 0; i < latLonPoints.Count; i++)
        {
            var p = latLonPoints[i];
            unit[i] = p.Length >= 2 ? LatLonToUnit(p[1], p[0]) : Vector3.Zero;
        }
        var valid = new bool[unit.Length];
        for (var i = 0; i < latLonPoints.Count; i++) valid[i] = latLonPoints[i].Length >= 2;
        return ProjectUnitPolyline(unit, valid, planet, cam, radius, centerX, centerY, closed);
    }

    /// <summary>
    /// 预计算单位球坐标后的快速投影：每帧只做矩阵乘 + 正交投影，
    /// 不再重复计算 sin/cos（地理模型约 2.6 万个点，这是最大开销）。
    /// </summary>
    public static List<List<Vector2>> ProjectUnitPolyline(
        IReadOnlyList<Vector3> unitPoints,
        IReadOnlyList<bool> valid,
        Matrix4x4 planet, Matrix4x4 cam,
        float radius, float centerX, float centerY,
        bool closed = false)
    {
        var result = new List<List<Vector2>>();
        if (unitPoints.Count < 2) return result;

        var count = unitPoints.Count;
        var total = closed ? count + 1 : count;
        List<Vector2>? current = null;

        Vector3 prevCam = default;
        var havePrev = false;

        for (var i = 0; i < total; i++)
        {
            var k = i % count;
            if (!valid[k]) { havePrev = false; continue; }

            var camPos = Vector3.Transform(Vector3.Transform(unitPoints[k], planet), cam);
            var visible = camPos.Z > 0f;

            if (!havePrev)
            {
                if (visible)
                {
                    current = new List<Vector2> { Project(camPos, radius, centerX, centerY) };
                }
                prevCam = camPos;
                havePrev = true;
                continue;
            }

            var prevVisible = prevCam.Z > 0f;
            if (visible && prevVisible)
            {
                current ??= new List<Vector2>();
                current.Add(Project(camPos, radius, centerX, centerY));
            }
            else if (visible != prevVisible)
            {
                // 求出与地平线（z=0）的交点
                var t = prevCam.Z / (prevCam.Z - camPos.Z);
                var hit = Vector3.Lerp(prevCam, camPos, t);
                hit.Z = 0f;
                var hitScreen = Project(hit, radius, centerX, centerY);

                if (visible)
                {
                    current = new List<Vector2> { hitScreen };
                    current.Add(Project(camPos, radius, centerX, centerY));
                }
                else
                {
                    current?.Add(hitScreen);
                    if (current is { Count: > 1 }) result.Add(current);
                    current = null;
                }
            }

            prevCam = camPos;
        }

        if (current is { Count: > 1 }) result.Add(current);
        return result;
    }
}

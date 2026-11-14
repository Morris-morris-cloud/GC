using System.Collections.Generic;

namespace NeoRF.ContourMap;

/// <summary>内置全量文案兜底：没有语言包也能完整显示。</summary>
internal static class DefaultTexts
{
    public static readonly Dictionary<string, string> Zh = new()
    {
        ["app.title"] = "等高线地形图",
        ["app.subtitle"] = "七年级上册 · 地形图的判读：从立体地形到平面等高线",

        ["kind.mountain"] = "山地",
        ["kind.basin"] = "盆地",
        ["kind.hill"] = "丘陵",
        ["kind.plateau"] = "高原",
        ["kind.plain"] = "平原",
        ["btn.color"] = "上色",
        ["btn.colorOn"] = "取消上色",

        ["side.features"] = "五种地形部位",
        ["feature.peak"] = "山峰",
        ["feature.peakDesc"] = "等高线闭合，数值由中心向四周递减。",
        ["feature.ridge"] = "山脊",
        ["feature.ridgeDesc"] = "等高线凸向低处，脊线是分水线。",
        ["feature.valley"] = "山谷",
        ["feature.valleyDesc"] = "等高线凸向高处，谷线是集水线，常发育河流。",
        ["feature.saddle"] = "鞍部",
        ["feature.saddleDesc"] = "两个山峰之间的低地，形如马鞍。",
        ["feature.cliff"] = "陡崖",
        ["feature.cliffDesc"] = "等高线重叠或非常密集，近乎垂直。",
        ["feature.slope"] = "坡面",

        ["side.rules"] = "判读要点",
        ["rule.interval"] = "等高距：相邻两条等高线的高度差，本图 100 米。",
        ["rule.dense"] = "等高线越密坡越陡，越疏坡越缓。",
        ["rule.index"] = "每 5 条加粗一条计曲线，标注海拔便于读数。",
        ["rule.height"] = "海拔：某地高出海平面的垂直距离；相对高度：两点海拔之差。",

        ["footer.hint"] = "把鼠标移到任意一侧，可查看该点的海拔与地形部位",
        ["footer.readout"] = "海拔 {0} m　相对高度 {1} m"
    };

    public static readonly Dictionary<string, string> En = new()
    {
        ["app.title"] = "Contour Map",
        ["app.subtitle"] = "Grade 7 · Reading terrain maps: from 3D relief to contour lines",

        ["kind.mountain"] = "Mountains",
        ["kind.basin"] = "Basin",
        ["kind.hill"] = "Hills",
        ["kind.plateau"] = "Plateau",
        ["kind.plain"] = "Plain",
        ["btn.color"] = "Colour",
        ["btn.colorOn"] = "No colour",

        ["side.features"] = "Five terrain features",
        ["feature.peak"] = "Peak",
        ["feature.peakDesc"] = "Closed contours; values decrease outward from the centre.",
        ["feature.ridge"] = "Ridge",
        ["feature.ridgeDesc"] = "Contours bulge toward lower ground; a watershed line.",
        ["feature.valley"] = "Valley",
        ["feature.valleyDesc"] = "Contours bulge toward higher ground; a drainage line, often a stream.",
        ["feature.saddle"] = "Saddle",
        ["feature.saddleDesc"] = "Low ground between two peaks, shaped like a saddle.",
        ["feature.cliff"] = "Cliff",
        ["feature.cliffDesc"] = "Contours overlap or crowd tightly — nearly vertical.",
        ["feature.slope"] = "Slope",

        ["side.rules"] = "Key rules",
        ["rule.interval"] = "Contour interval: height gap between neighbours (100 m here).",
        ["rule.dense"] = "Closer contours = steeper slope; wider spacing = gentler.",
        ["rule.index"] = "Every 5th contour is bolded and labelled as an index contour.",
        ["rule.height"] = "Elevation above sea level; relative height is the difference of two elevations.",

        ["footer.hint"] = "Move the pointer over either side to read elevation and terrain feature",
        ["footer.readout"] = "Elevation {0} m　Relative height {1} m"
    };
}

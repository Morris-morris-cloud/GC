using System.Collections.Generic;

namespace GC.ContourMap;

/// <summary>内置全量文案兜底：没有语言包也能完整显示（复刻 HTML 课件全部文案）。</summary>
internal static class DefaultTexts
{
    public static readonly Dictionary<string, string> Zh = new()
    {
        // 标题
        ["app.title"] = "等高线地形 · 五大典型地貌判读",
        ["app.subtitle"] = "七年级上册 · 从立体地形到平面等高线，判读山峰/山脊/山谷/鞍部/陡崖",

        // 实验流程四步
        ["flow.title"] = "实验流程",
        ["flow.step.0"] = "等高切割",
        ["flow.step.1"] = "形成交线",
        ["flow.step.2"] = "投影成图",
        ["flow.step.3"] = "疏密判坡",
        ["flow.desc.0"] = "观察水平面与山体在同一海拔相交，形成连续闭合线。",
        ["flow.desc.1"] = "每个水平切面都会留下同高交线，多层交线共同描述山体起伏。",
        ["flow.desc.2"] = "把各高度交线垂直投影到同一平面，得到完整等高线地形图。",
        ["flow.desc.3"] = "等高线越密，坡度越陡；等高线越疏，坡度越缓。",

        // 观察面板
        ["observe.title"] = "实验演示",
        ["observe.default"] = "点击开始实验，在立体地形中观察等高切割与交线形成，再通过四步图解理解投影与判坡。",
        ["observe.features"] = "判读要点",
        ["observe.default.f1"] = "同一条等高线上海拔相等",
        ["observe.default.f2"] = "相邻等高线高差保持一致",

        // 五大典型地貌
        ["side.landforms"] = "五大典型地貌",
        ["landform.peak"] = "山峰",
        ["landform.ridge"] = "山脊",
        ["landform.valley"] = "山谷",
        ["landform.saddle"] = "鞍部",
        ["landform.cliff"] = "陡崖",
        ["landform.peak.sub"] = "闭合等高线",
        ["landform.ridge.sub"] = "凸低为脊",
        ["landform.valley.sub"] = "凸高为谷",
        ["landform.saddle.sub"] = "两峰之间",
        ["landform.cliff.sub"] = "密集或重合",

        ["landform.peak.summary"] = "山峰四周较低、中心突起；等高线闭合成环，海拔数值由外向内逐渐升高。",
        ["landform.ridge.summary"] = "等高线弯曲部分向海拔低处凸出，脊线常成为两侧水系的分水岭。",
        ["landform.valley.summary"] = "等高线弯曲部分向海拔高处凸出，谷底低洼并容易汇水形成河流。",
        ["landform.saddle.summary"] = "鞍部位于相邻两座山峰之间，是沿山脊方向相对低洼的过渡地带。",
        ["landform.cliff.summary"] = "陡崖坡面近直立，不同高程的等高线会高度密集，局部可投影到同一位置而重合。",

        ["landform.peak.f1"] = "中心海拔高、四周海拔低",
        ["landform.peak.f2"] = "闭合线越向内，数值越大",
        ["landform.peak.f3"] = "若数值向内降低，则应判为洼地",
        ["landform.ridge.f1"] = "等高线凸向低值方向",
        ["landform.ridge.f2"] = "脊线由高处向低处延伸",
        ["landform.ridge.f3"] = "雨水沿脊线两侧分流",
        ["landform.valley.f1"] = "等高线凸向高值方向",
        ["landform.valley.f2"] = "V 形尖端指向上游高处",
        ["landform.valley.f3"] = "谷底常发育河流",
        ["landform.saddle.f1"] = "两组闭合等高线相邻",
        ["landform.saddle.f2"] = "中部线条收窄，形似马鞍",
        ["landform.saddle.f3"] = "常是翻越山岭的低垭口",
        ["landform.cliff.f1"] = "密集表示陡坡，重合才是陡崖特征",
        ["landform.cliff.f2"] = "单位水平距离内高差很大",
        ["landform.cliff.f3"] = "n 条线重合时：(n−1)d ≤ ΔH < (n+1)d",

        ["landform.peak.hint"] = "山峰：闭合曲线，数值向中心升高",
        ["landform.ridge.hint"] = "山脊：等高线向低处凸出，凸低为脊",
        ["landform.valley.hint"] = "山谷：等高线向高处凸出，凸高为谷",
        ["landform.saddle.hint"] = "鞍部：两峰之间相对低洼，俯视轮廓呈收腰形",
        ["landform.cliff.hint"] = "陡崖：等高线密集或重合，坡度极陡",

        // 按钮
        ["btn.play"] = "开始实验",
        ["btn.pause"] = "暂停实验",
        ["btn.replay"] = "重新实验",
        ["btn.reset"] = "重置",
        ["btn.view3d"] = "立体图",
        ["btn.viewTop"] = "俯视图",
        ["btn.color"] = "上色",
        ["btn.colorOn"] = "取消上色",
        ["btn.fullscreen"] = "全屏",
        ["btn.exitFullscreen"] = "退出全屏",
        ["btn.backExperiment"] = "返回实验",

        // 侧栏判读要点（通用地形）
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

        // 判读规则
        ["side.rules"] = "判读要点",
        ["rule.interval"] = "等高距：相邻两条等高线的高度差，本图 100 米。",
        ["rule.dense"] = "等高线越密坡越陡，越疏坡越缓。",
        ["rule.index"] = "每 5 条加粗一条计曲线，标注海拔便于读数。",
        ["rule.height"] = "海拔：某地高出海平面的垂直距离；相对高度：两点海拔之差。",

        // 底部读数 / 图例
        ["footer.hint"] = "拖动立体地形旋转、滚轮缩放；鼠标悬停可查看该点海拔与地形部位",
        ["footer.readout"] = "海拔 {0} m　相对高度 {1} m",
        ["legend.title"] = "分层设色",
        ["profile.title"] = "地形剖面图",
        ["slope.gentle"] = "缓坡（线疏）",
        ["slope.steep"] = "陡坡（线密）",

        ["hint.play"] = "点击“开始实验”，观察等高切割与交线形成",
        ["hint.playing"] = "实验播放中",
        ["hint.paused"] = "实验已暂停 · 点击继续恢复播放",
        ["hint.done"] = "实验流程播放完成 · 可重新实验或选择典型地貌观察"
    };

    public static readonly Dictionary<string, string> En = new()
    {
        ["app.title"] = "Contour Lines · Five Classic Landforms",
        ["app.subtitle"] = "Grade 7 · From 3D relief to contour maps: peaks, ridges, valleys, saddles and cliffs",

        ["flow.title"] = "Experiment flow",
        ["flow.step.0"] = "Contour cutting",
        ["flow.step.1"] = "Forming lines",
        ["flow.step.2"] = "Projection",
        ["flow.step.3"] = "Slope density",
        ["flow.desc.0"] = "Watch a horizontal plane intersect the mountain at one elevation, forming closed lines.",
        ["flow.desc.1"] = "Each horizontal slice leaves an equal-height line; stacked lines describe the relief.",
        ["flow.desc.2"] = "Project every intersection line vertically onto one plane to build the contour map.",
        ["flow.desc.3"] = "Contours closer together mean a steeper slope; wider apart mean a gentler one.",

        ["observe.title"] = "Experiment demo",
        ["observe.default"] = "Press play to watch contour cutting and line formation, then use the four steps to learn projection and slope reading.",
        ["observe.features"] = "Key points",
        ["observe.default.f1"] = "All points on one contour share the same elevation",
        ["observe.default.f2"] = "Neighbouring contours keep a constant height interval",

        ["side.landforms"] = "Five classic landforms",
        ["landform.peak"] = "Peak",
        ["landform.ridge"] = "Ridge",
        ["landform.valley"] = "Valley",
        ["landform.saddle"] = "Saddle",
        ["landform.cliff"] = "Cliff",
        ["landform.peak.sub"] = "Closed contours",
        ["landform.ridge.sub"] = "Bulges toward low ground",
        ["landform.valley.sub"] = "Bulges toward high ground",
        ["landform.saddle.sub"] = "Between two peaks",
        ["landform.cliff.sub"] = "Dense or overlapping",

        ["landform.peak.summary"] = "A peak rises in the centre with lower ground around it; contours close into rings and values increase inward.",
        ["landform.ridge.summary"] = "Contours bulge toward lower ground; a ridge line often divides two drainage systems.",
        ["landform.valley.summary"] = "Contours bulge toward higher ground; valleys are low and collect water into streams.",
        ["landform.saddle.summary"] = "A saddle lies between two adjacent peaks, a relatively low pass along the ridge.",
        ["landform.cliff.summary"] = "A cliff face is nearly vertical, so contours crowd tightly or even overlap.",

        ["landform.peak.f1"] = "High centre, low surroundings",
        ["landform.peak.f2"] = "Closed lines grow in value inward",
        ["landform.peak.f3"] = "If values drop inward, it is a depression",
        ["landform.ridge.f1"] = "Contours bulge toward lower values",
        ["landform.ridge.f2"] = "Ridge line runs from high to low",
        ["landform.ridge.f3"] = "Rain drains to either side of the ridge",
        ["landform.valley.f1"] = "Contours bulge toward higher values",
        ["landform.valley.f2"] = "V tip points upstream to high ground",
        ["landform.valley.f3"] = "Streams often form along the valley floor",
        ["landform.saddle.f1"] = "Two neighbouring closed contour groups",
        ["landform.saddle.f2"] = "Contours pinch in the middle like a saddle",
        ["landform.saddle.f3"] = "Often a low mountain pass",
        ["landform.cliff.f1"] = "Dense means steep; overlap means cliff",
        ["landform.cliff.f2"] = "Large elevation change over a short distance",
        ["landform.cliff.f3"] = "n overlapping lines: (n−1)d ≤ ΔH < (n+1)d",

        ["landform.peak.hint"] = "Peak: closed curves, values rise toward the centre",
        ["landform.ridge.hint"] = "Ridge: contours bulge toward low ground",
        ["landform.valley.hint"] = "Valley: contours bulge toward high ground",
        ["landform.saddle.hint"] = "Saddle: low ground between two peaks",
        ["landform.cliff.hint"] = "Cliff: contours dense or overlapping, very steep",

        ["btn.play"] = "Start experiment",
        ["btn.pause"] = "Pause experiment",
        ["btn.replay"] = "Replay experiment",
        ["btn.reset"] = "Reset",
        ["btn.view3d"] = "3D view",
        ["btn.viewTop"] = "Top view",
        ["btn.color"] = "Colour",
        ["btn.colorOn"] = "No colour",
        ["btn.fullscreen"] = "Fullscreen",
        ["btn.exitFullscreen"] = "Exit fullscreen",
        ["btn.backExperiment"] = "Back to experiment",

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

        ["footer.hint"] = "Drag the 3D terrain to rotate, scroll to zoom; hover to read elevation and terrain feature",
        ["footer.readout"] = "Elevation {0} m　Relative height {1} m",
        ["legend.title"] = "Hypsometric tint",
        ["profile.title"] = "Terrain profile",
        ["slope.gentle"] = "Gentle (sparse lines)",
        ["slope.steep"] = "Steep (dense lines)",

        ["hint.play"] = "Press start to watch contour cutting and line formation",
        ["hint.playing"] = "Experiment playing",
        ["hint.paused"] = "Experiment paused · press to resume",
        ["hint.done"] = "Experiment finished · replay or choose a landform"
    };
}

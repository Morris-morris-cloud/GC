using System;
using System.Globalization;

namespace GC.ScaleCalculator;

/// <summary>三种求解模式：待求的量。</summary>
public enum SolveMode
{
    /// <summary>求实地距离</summary>
    Real,
    /// <summary>求图上距离</summary>
    Map,
    /// <summary>求比例尺</summary>
    Scale
}

public enum ScaleGrade { Large, Medium, Small }

/// <summary>
/// 三个量始终同时存在：图上距离(cm)、实地距离(cm)、比例尺分母 N。
/// <see cref="Mode"/> 指定哪个是"待求结果"，另两个是输入；
/// <see cref="ScaleMath.Solve"/> 只由两个输入重算结果量，切换模式时数值保持连续。
/// </summary>
public sealed class ScaleState
{
    public double MapCm = 3.0;
    public double RealCm = 300000.0;   // 3 km
    public double N = 100000.0;         // 1:100000
    public SolveMode Mode = SolveMode.Real;

    public ScaleState Clone() => new()
    {
        MapCm = MapCm, RealCm = RealCm, N = N, Mode = Mode
    };
}

/// <summary>线段式比例尺换挡结果。</summary>
public sealed class SegmentScale
{
    /// <summary>每一格代表的实地距离（米）。</summary>
    public double MetersPerDiv;
    /// <summary>每一格在屏幕上的像素宽度。</summary>
    public double DivPx;
    /// <summary>绘制的格数。</summary>
    public int DivCount = 5;
}

/// <summary>一道练习题。</summary>
public sealed class QuizItem
{
    public SolveMode Mode;
    public string PromptKey = "";
    public object[] PromptArgs = Array.Empty<object>();
    public double Answer;
    /// <summary>相对容差。</summary>
    public double Tolerance = 0.02;
    public string AnswerUnitKey = "unit.cm";
    public string[] StepKeys = Array.Empty<string>();
}

public static class ScaleMath
{
    public const double CmPerM = 100.0;
    public const double CmPerKm = 100000.0;

    /// <summary>由两个输入量重算"待求"量。</summary>
    public static void Solve(ScaleState s)
    {
        // 先做一次合法性收敛，避免 0 / 负数进入除法
        if (s.MapCm < 0) s.MapCm = 0;
        if (s.RealCm < 0) s.RealCm = 0;
        if (s.N < 1) s.N = 1;

        switch (s.Mode)
        {
            case SolveMode.Real:
                s.RealCm = s.MapCm * s.N;
                break;
            case SolveMode.Map:
                s.MapCm = s.N > 0 ? s.RealCm / s.N : 0;
                break;
            case SolveMode.Scale:
                s.N = s.MapCm > 1e-9 ? s.RealCm / s.MapCm : 1;
                if (s.N < 1) s.N = 1;
                break;
        }
    }

    /// <summary>厘米 → 更合适的单位与数值。</summary>
    public static (double value, string unitKey) HumanReal(double cm)
    {
        if (cm < 1) return (cm, "unit.cm");
        if (cm < CmPerM) return (cm, "unit.cm");
        if (cm < CmPerKm) return (cm / CmPerM, "unit.m");
        return (cm / CmPerKm, "unit.km");
    }

    public static string Num(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return "—";
        var a = Math.Abs(v);
        if (a >= 1000) return v.ToString("#,##0", CultureInfo.InvariantCulture);
        if (a >= 10) return v.ToString("0.#", CultureInfo.InvariantCulture);
        if (a >= 1) return v.ToString("0.##", CultureInfo.InvariantCulture);
        if (a >= 0.01) return v.ToString("0.###", CultureInfo.InvariantCulture);
        if (a == 0) return "0";
        return v.ToString("0.####", CultureInfo.InvariantCulture);
    }

    public static string FormatRealCm(double cm)
    {
        var (v, key) = HumanReal(cm);
        return Num(v) + " " + key;
    }

    public static string FormatN(double n) => Num(Math.Round(n));

    /// <summary>数字式写法 1:N。</summary>
    public static string Digital(double n) => "1 : " + FormatN(n);

    /// <summary>"文字式"里那一段实地距离（含单位），由调用方套 i18n 模板。</summary>
    public static string VerbalDistance(double n)
    {
        // 图上 1 厘米代表实地 n 厘米
        var (v, key) = HumanReal(n);
        return Num(v) + " " + key;
    }

    /// <summary>按当前比例尺，把一段实地距离换算成图上厘米。</summary>
    public static double RealToMapCm(double realCm, double n) => n > 0 ? realCm / n : 0;

    /// <summary>把图上厘米换算为屏幕像素。</summary>
    public static double CmToPx(double cm, double pxPerCm) => cm * pxPerCm;

    /// <summary>把一段实地距离按当前比例尺换算成屏幕像素。</summary>
    public static double RealToMapPx(double realCm, double n, double pxPerCm)
        => RealToMapCm(realCm, n) * pxPerCm;

    /// <summary>
    /// 线段式比例尺自动换挡：在 1/2/5×10^k（米）里挑一个，
    /// 使每格画出来最接近 targetPx，保证任何比例尺都画得下、刻度又是整数。
    /// </summary>
    public static SegmentScale PickSegment(double n, double pxPerCm, double targetPx)
    {
        if (n < 1) n = 1;
        if (pxPerCm < 1) pxPerCm = 1;
        // 每格实地米数（理想值）：px = m*100/n*pxPerCm
        var idealMeters = targetPx * n / (100.0 * pxPerCm);
        if (idealMeters <= 0) idealMeters = 1;

        double best = 1;
        double bestErr = double.MaxValue;
        var logIdeal = Math.Log10(idealMeters);
        for (var k = -3; k <= 9; k++)
        {
            foreach (var m in new[] { 1.0, 2.0, 5.0 })
            {
                var cand = m * Math.Pow(10, k);
                var err = Math.Abs(Math.Log10(cand) - logIdeal);
                if (err < bestErr) { bestErr = err; best = cand; }
            }
        }

        var divPx = best * 100.0 / n * pxPerCm;
        // 极端情况下（换挡后仍过长/过短）把格数收敛一下，保证总长在合理区间
        var count = 5;
        var total = divPx * count;
        if (total > targetPx * 9) count = 4;
        else if (total < targetPx * 2.5) count = 6;

        return new SegmentScale { MetersPerDiv = best, DivPx = divPx, DivCount = count };
    }

    /// <summary>线段式每格的标注文字（不带 i18n，纯数值+单位）。</summary>
    public static string SegmentLabel(double meters)
    {
        if (meters < 1) return Num(meters * 100) + " cm";
        if (meters < 1000) return Num(meters) + " m";
        return Num(meters / 1000) + " km";
    }

    public static ScaleGrade Grade(double n)
    {
        if (n <= 100000) return ScaleGrade.Large;
        if (n <= 1000000) return ScaleGrade.Medium;
        return ScaleGrade.Small;
    }

    public static string GradeKey(double n) => Grade(n) switch
    {
        ScaleGrade.Large => "grade.large",
        ScaleGrade.Medium => "grade.medium",
        _ => "grade.small"
    };

    /// <summary>光谱上 0..1 的位置（对数刻度，1:1千 ~ 1:10亿）。</summary>
    public static double SpectrumPosition(double n)
    {
        if (n < 1) n = 1;
        var p = (Math.Log10(n) - 3.0) / 6.0;
        return p < 0 ? 0 : p > 1 ? 1 : p;
    }

    /// <summary>把用户输入的"数值+单位"解析成厘米。</summary>
    public static bool TryParseReal(string text, string unitKey, out double cm)
    {
        cm = 0;
        if (!double.TryParse(text.Replace(",", "").Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var v) || v < 0 || double.IsNaN(v) || double.IsInfinity(v))
            return false;
        cm = unitKey switch
        {
            "unit.m" => v * CmPerM,
            "unit.km" => v * CmPerKm,
            _ => v * 1.0
        };
        return true;
    }

    /// <summary>由厘米得到该单位下的数值（用于输入框显示）。</summary>
    public static double ToUnit(double cm, string unitKey) => unitKey switch
    {
        "unit.m" => cm / CmPerM,
        "unit.km" => cm / CmPerKm,
        _ => cm
    };

    // ------------------------------------------------------------------ 练习

    public static QuizItem GenerateQuiz(Random rnd)
    {
        var mode = (SolveMode)rnd.Next(0, 3);
        // 用整齐的数，方便心算
        var mapCm = new[] { 1.0, 2.0, 2.5, 3.0, 4.0, 5.0, 6.0, 8.0, 10.0, 12.0 }[rnd.Next(0, 10)];
        var ns = new[] { 1000.0, 5000.0, 10000.0, 25000.0, 50000.0, 100000.0, 500000.0, 1000000.0, 5000000.0, 10000000.0 };
        var n = ns[rnd.Next(0, ns.Length)];

        switch (mode)
        {
            case SolveMode.Real:
            {
                var realCm = mapCm * n;
                var (v, key) = HumanReal(realCm);
                return new QuizItem
                {
                    Mode = SolveMode.Real,
                    PromptKey = "quiz.promptReal",
                    PromptArgs = new object[] { FormatN(n), Num(mapCm) },
                    Answer = v,
                    AnswerUnitKey = key,
                    StepKeys = new[] { "steps.2.formulaReal", "steps.4.result" }
                };
            }
            case SolveMode.Map:
            {
                // 让实地距离是整齐数字，反推图上距离也是整齐数字
                var realCm = mapCm * n;
                var (v, key) = HumanReal(realCm);
                return new QuizItem
                {
                    Mode = SolveMode.Map,
                    PromptKey = "quiz.promptMap",
                    PromptArgs = new object[] { FormatN(n), Num(v) },
                    Answer = mapCm,
                    AnswerUnitKey = "unit.cm",
                    StepKeys = new[] { "steps.2.formulaMap", "steps.4.result" }
                };
            }
            default:
            {
                var realCm = mapCm * n;
                var (v, key) = HumanReal(realCm);
                return new QuizItem
                {
                    Mode = SolveMode.Scale,
                    PromptKey = "quiz.promptScale",
                    PromptArgs = new object[] { Num(mapCm), Num(v) },
                    Answer = n,
                    AnswerUnitKey = "unit.none",
                    StepKeys = new[] { "steps.1.unify", "steps.2.formulaScale", "steps.4.result" }
                };
            }
        }
    }

    public static bool CheckAnswer(QuizItem q, double input)
    {
        if (q.Answer <= 0) return false;
        return Math.Abs(input - q.Answer) / q.Answer <= q.Tolerance;
    }
}

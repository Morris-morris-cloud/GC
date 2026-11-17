using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace GC.ScaleCalculator;

public sealed partial class ScaleCalculatorView
{
    private readonly ScaleState _state = new();
    private bool _suppress;
    private string _realUnit = "unit.km";
    private QuizItem _quiz = new();
    private int _streak;
    private bool _quizAnswered;

    private void WireEvents()
    {
        _btnReal.Click += (_, _) => SetMode(SolveMode.Real);
        _btnMap.Click += (_, _) => SetMode(SolveMode.Map);
        _btnScale.Click += (_, _) => SetMode(SolveMode.Scale);

        _mapSlider.ValueChanged += (_, e) =>
        {
            if (_suppress) return;
            _state.MapCm = e.NewValue;
            _suppress = true;
            _mapBox.Text = ScaleMath.Num(_state.MapCm);
            _suppress = false;
            RefreshAll(syncInputs: false);
        };
        _mapBox.TextChanged += (_, _) => OnTextInput(_mapBox, v => { _state.MapCm = v; }, v =>
        {
            _suppress = true;
            _mapSlider.Value = Math.Clamp(v, 0.1, 20);
            _suppress = false;
        });
        _realBox.TextChanged += (_, _) =>
        {
            if (_suppress) return;
            if (ScaleMath.TryParseReal(_realBox.Text ?? "", _realUnit, out var cm))
            {
                _state.RealCm = cm;
                RefreshAll(syncInputs: false);
            }
        };
        _nBox.TextChanged += (_, _) => OnTextInput(_nBox, v => { _state.N = v; }, null);

        _quizInput.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) CheckQuiz();
        };
    }

    private void OnTextInput(TextBox box, Action<double> apply, Action<double>? extra)
    {
        if (_suppress) return;
        if (!double.TryParse((box.Text ?? "").Replace(",", "").Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var v) || v < 0 || double.IsNaN(v) || double.IsInfinity(v))
            return;
        apply(v);
        extra?.Invoke(v);
        RefreshAll(syncInputs: false);
    }

    private void SetMode(SolveMode mode, bool initial = false)
    {
        _state.Mode = mode;
        if (!initial) ScaleMath.Solve(_state);
        UpdateModeButtons();
        RefreshAll(syncInputs: true);
    }

    private void UpdateModeButtons()
    {
        StyleMode(_btnReal, _state.Mode == SolveMode.Real);
        StyleMode(_btnMap, _state.Mode == SolveMode.Map);
        StyleMode(_btnScale, _state.Mode == SolveMode.Scale);

        // 结果卡高亮：待求的那一张变蓝
        StyleCard(_mapCard, _state.Mode == SolveMode.Map);
        StyleCard(_realCard, _state.Mode == SolveMode.Real);
        StyleCard(_scaleCard, _state.Mode == SolveMode.Scale);

        _mapResultText.Foreground = B(_state.Mode == SolveMode.Map ? CAccentDark : CInk);
        _realResultText.Foreground = B(_state.Mode == SolveMode.Real ? CAccentDark : CInk);
        _scaleResultText.Foreground = B(_state.Mode == SolveMode.Scale ? CAccentDark : CInk);
    }

    private void StyleMode(Button b, bool active)
    {
        b.Background = B(active ? CAccent : CSurface);
        b.BorderBrush = B(active ? CAccent : CLineStrong);
        b.BorderThickness = new Thickness(active ? 0 : 1);
        if (b.Content is TextBlock t)
            t.Foreground = B(active ? Colors.White : CInk);
    }

    private static void StyleCard(Border card, bool active)
    {
        card.Background = B(active ? Color.Parse("#F0F5FF") : Color.Parse("#F7F9FE"));
        card.BorderBrush = B(active ? Color.Parse("#2F6BFF") : Color.Parse("#E3E8F3"));
        card.BorderThickness = new Thickness(active ? 1.5 : 1);
    }

    private void ApplyPreset(PresetItem p)
    {
        _state.N = p.N;
        ScaleMath.Solve(_state);
        RefreshAll(syncInputs: true);
    }

    private void ResetState()
    {
        _state.MapCm = 3.0;
        _state.N = 100000.0;
        ScaleMath.Solve(_state);
        RefreshAll(syncInputs: true);
    }

    private void SyncUnitDisplay()
    {
        // ComboBox 的 ItemsSource 是 "unit.km" 等键；这里只记录选择，显示由下拉项本身处理
        var idx = _unitBox.SelectedIndex;
        _realUnit = idx switch { 1 => "unit.m", 2 => "unit.cm", _ => "unit.km" };
    }

    /// <summary>
    /// 全量刷新。syncInputs=true 时把三个输入框/滑块同步成 state（切换模式、预设、重置后需要）；
    /// false 时保留用户正在输入的文本（避免光标跳动）。
    /// </summary>
    private void RefreshAll(bool syncInputs)
    {
        ScaleMath.Solve(_state);

        if (syncInputs)
        {
            _suppress = true;
            _mapBox.Text = ScaleMath.Num(_state.MapCm);
            _mapSlider.Value = Math.Clamp(_state.MapCm, 0.1, 20);
            _nBox.Text = ScaleMath.FormatN(_state.N);
            var (rv, _) = ScaleMath.HumanReal(_state.RealCm);
            // 用当前选择的单位显示
            _realBox.Text = ScaleMath.Num(ScaleMath.ToUnit(_state.RealCm, _realUnit));
            _suppress = false;
        }

        UpdateModeButtons();

        // 结果卡读数（三张都显示当前值，蓝色那张是"答案"）
        _mapResultText.Text = ScaleMath.Num(_state.MapCm) + " " + T("unit.cm");
        var (rv2, rkey2) = ScaleMath.HumanReal(_state.RealCm);
        _realResultText.Text = ScaleMath.Num(rv2) + " " + T(rkey2);
        _scaleResultText.Text = "1 : " + ScaleMath.FormatN(_state.N);

        // 三种写法
        _digitalText.Text = ScaleMath.Digital(_state.N);
        _verbalText.Text = TF("forms.verbalTpl", ScaleMath.VerbalDistance(_state.N));
        _segmentText.Text = TF("forms.segmentDesc",
            ScaleMath.SegmentLabel(_ruler.Segment.MetersPerDiv), _ruler.Segment.DivCount);

        // 判读
        var gradeKey = ScaleMath.GradeKey(_state.N);
        _gradeTitle.Text = T(gradeKey);
        _gradeChip.Child = Tb(T(gradeKey), 11, CAccentDark, FontWeight.SemiBold);
        _gradeNote.Text = T(gradeKey + "Note");
        _gradeRule.Text = T("grade.rule");

        // 标尺
        _ruler.State = _state.Clone();
        _ruler.Refresh();
        _segmentText.Text = TF("forms.segmentDesc",
            ScaleMath.SegmentLabel(_ruler.Segment.MetersPerDiv), _ruler.Segment.DivCount);

        BuildSteps();
        RefreshField();
    }

    private void BuildSteps()
    {
        _stepsPanel.Children.Clear();
        var map = ScaleMath.Num(_state.MapCm);
        var realCm = ScaleMath.Num(_state.RealCm);
        var n = ScaleMath.FormatN(_state.N);
        var (rv, rk) = ScaleMath.HumanReal(_state.RealCm);
        var result = _state.Mode switch
        {
            SolveMode.Real => ScaleMath.Num(rv) + " " + T(rk),
            SolveMode.Map => ScaleMath.Num(_state.MapCm) + " " + T("unit.cm"),
            _ => "1 : " + ScaleMath.FormatN(_state.N)
        };
        var formulaKey = _state.Mode switch
        {
            SolveMode.Real => "steps.2.formulaReal",
            SolveMode.Map => "steps.2.formulaMap",
            _ => "steps.2.formulaScale"
        };
        var substitute = _state.Mode switch
        {
            SolveMode.Real => map + " cm × " + n,
            SolveMode.Map => realCm + " cm ÷ " + n,
            _ => realCm + " cm ÷ " + map + " cm"
        };

        var lines = new System.Collections.Generic.List<string>
        {
            T("steps.1.unify"),
            T(formulaKey),
            TF("steps.3.substitute", substitute),
            TF("steps.4.result", result),
            T("steps.5.convert")
        };
        var i = 0;
        foreach (var line in lines)
        {
            _stepsPanel.Children.Add(Tb(line, 11, i == 3 ? CAccentDark : CInkDim,
                i == 3 ? FontWeight.SemiBold : null));
            i++;
        }
    }

    private void ToggleSteps()
    {
        _stepsOpen = !_stepsOpen;
        _stepsPanel.IsVisible = _stepsOpen;
        _stepsToggle.Content = Tb(T(_stepsOpen ? "steps.hide" : "steps.show"), 12.5, CInk, FontWeight.SemiBold);
    }

    private void RefreshField()
    {
        var idx = _landmarkBox.SelectedIndex;
        if (idx < 0 || idx >= _data.Landmarks.Count) return;
        var meters = _data.Landmarks[idx].Meters;
        var realCm = meters * ScaleMath.CmPerM;
        var mapCm = ScaleMath.RealToMapCm(realCm, _state.N);
        var px = ScaleMath.CmToPx(mapCm, _ruler.PxPerCm);
        // mapCm 是厘米，按 HumanReal 换算成"图上长度"的合适单位
        var (mv, mk) = ScaleMath.HumanReal(mapCm);

        string text;
        if (px > _ruler.MaxBarWidth)
            text = TF("field.mapLength", ScaleMath.Num(mv) + " " + T(mk)) + " · " + T("field.tooLong");
        else if (px < 1)
            text = TF("field.mapLength", ScaleMath.Num(mv) + " " + T(mk)) + " · " + T("field.tooShort");
        else
            text = TF("field.mapLength", ScaleMath.Num(mv) + " " + T(mk));
        _landmarkResult.Text = text;
        _landmarkResult.Foreground = B(px > _ruler.MaxBarWidth ? CDanger : px < 1 ? CWarn : CSuccess);
    }

    // ────────────────────────────────────────────────────────── 练习

    private void NewQuiz()
    {
        _quiz = ScaleMath.GenerateQuiz(_rnd);
        _quizAnswered = false;
        _quizInput.Text = "";
        _quizFeedback.Text = "";
        _quizSteps.Children.Clear();
        _quizPrompt.Text = TF(_quiz.PromptKey, _quiz.PromptArgs);
        var unitText = _quiz.AnswerUnitKey == "unit.none" ? T("unit.none") : T(_quiz.AnswerUnitKey);
        _quizUnit.Text = TF("quiz.unit", unitText);
        _quizStreak.Text = TF("quiz.streak", _streak);
    }

    private void CheckQuiz()
    {
        if (_quizAnswered) return;
        if (!double.TryParse((_quizInput.Text ?? "").Replace(",", "").Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var v))
        {
            _quizFeedback.Text = T("quiz.wrong");
            _quizFeedback.Foreground = B(CDanger);
            return;
        }

        if (ScaleMath.CheckAnswer(_quiz, v))
        {
            _streak++;
            _quizFeedback.Text = T("quiz.correct");
            _quizFeedback.Foreground = B(CSuccess);
        }
        else
        {
            _streak = 0;
            _quizFeedback.Text = T("quiz.wrong");
            _quizFeedback.Foreground = B(CDanger);
            ShowQuizSteps();
        }
        _quizAnswered = true;
        _quizStreak.Text = TF("quiz.streak", _streak);
    }

    private void ShowQuizSteps()
    {
        _quizSteps.Children.Clear();
        _quizSteps.Children.Add(Tb(T("quiz.showSteps"), 10.5, CInkFaint, FontWeight.SemiBold));
        foreach (var key in _quiz.StepKeys)
        {
            var line = T(key);
            if (line.Contains("{0}")) line = TF(key, ScaleMath.Num(_quiz.Answer) + " " +
                (_quiz.AnswerUnitKey == "unit.none" ? "" : T(_quiz.AnswerUnitKey)));
            _quizSteps.Children.Add(Tb(line, 10.5, CInkDim));
        }
        _quizSteps.Children.Add(Tb(TF("steps.4.result",
            ScaleMath.Num(_quiz.Answer) + " " +
            (_quiz.AnswerUnitKey == "unit.none" ? "" : T(_quiz.AnswerUnitKey))),
            10.5, CAccentDark, FontWeight.SemiBold));
    }
}

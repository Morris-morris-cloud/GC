using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace GC.ScaleCalculator;

public sealed partial class ScaleCalculatorView
{
    // 模式条
    private Button _btnReal = null!, _btnMap = null!, _btnScale = null!;

    // 左栏输入
    private Slider _mapSlider = null!;
    private TextBox _mapBox = null!, _realBox = null!, _nBox = null!;
    private ComboBox _unitBox = null!;
    private TextBlock _mapCardLabel = null!, _realCardLabel = null!, _scaleCardLabel = null!;
    private TextBlock _mapResultText = null!, _realResultText = null!, _scaleResultText = null!;
    private Border _mapCard = null!, _realCard = null!, _scaleCard = null!;
    private WrapPanel _presetPanel = null!;

    // 中栏
    private ScaleRuler _ruler = null!;

    // 右栏
    private TextBlock _digitalText = null!, _verbalText = null!, _segmentText = null!;
    private TextBlock _gradeTitle = null!, _gradeNote = null!, _gradeRule = null!;
    private Border _gradeChip = null!;
    private StackPanel _stepsPanel = null!;
    private Button _stepsToggle = null!;
    private bool _stepsOpen;

    // 底部：实地尺
    private ComboBox _landmarkBox = null!;
    private TextBlock _landmarkResult = null!;

    // 底部：练习
    private TextBlock _quizPrompt = null!, _quizFeedback = null!, _quizStreak = null!, _quizUnit = null!;
    private TextBox _quizInput = null!;
    private Button _quizCheck = null!, _quizNext = null!;
    private StackPanel _quizSteps = null!;

    private void BuildLayout()
    {
        var root = new Grid { Background = B(CAppBg) };
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));   // 顶部
        root.RowDefinitions.Add(new RowDefinition(GridLength.Star));   // 三栏
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));   // 底部
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));   // 页脚

        var header = BuildHeader();
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var columns = new Grid { Margin = new Thickness(16, 12, 16, 8) };
        columns.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(300)));
        columns.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(16)));
        columns.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        columns.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(16)));
        columns.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(320)));

        var left = BuildLeft();
        Grid.SetColumn(left, 0);
        columns.Children.Add(left);
        var mid = BuildMiddle();
        Grid.SetColumn(mid, 2);
        columns.Children.Add(mid);
        var right = BuildRight();
        Grid.SetColumn(right, 4);
        columns.Children.Add(right);
        Grid.SetRow(columns, 1);
        root.Children.Add(columns);

        var bottom = BuildBottom();
        Grid.SetRow(bottom, 2);
        root.Children.Add(bottom);

        var footer = new Border
        {
            Background = B(CSurfaceAlt),
            BorderBrush = B(CLine),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(20, 9),
            Child = Tb(T("common.footer"), 11, CInkFaint)
        };
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        Content = root;
    }

    private Control BuildHeader()
    {
        var title = Tb(T("app.title"), 22, CInk, FontWeight.SemiBold);
        var sub = Tb(T("app.subtitle"), 12, CInkDim);

        _btnReal = Btn(T("mode.real"));
        _btnMap = Btn(T("mode.map"));
        _btnScale = Btn(T("mode.scale"));

        var modes = H(8);
        modes.Children.Add(_btnReal);
        modes.Children.Add(_btnMap);
        modes.Children.Add(_btnScale);

        var leftStack = V(2);
        leftStack.Children.Add(title);
        leftStack.Children.Add(sub);

        var grid = new Grid
        {
            Background = B(CSurface),
            Margin = new Thickness(0)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        var leftBorder = new Border { Padding = new Thickness(20, 14, 10, 14), Child = leftStack };
        Grid.SetColumn(leftBorder, 0);
        grid.Children.Add(leftBorder);
        var rightBorder = new Border
        {
            Padding = new Thickness(10, 14, 20, 14),
            VerticalAlignment = VerticalAlignment.Center,
            Child = V(4)
        };
        // 右侧：提示 + 模式按钮
        var rv = (StackPanel)rightBorder.Child!;
        rv.Children.Add(Tb(T("mode.hint"), 10.5, CInkFaint, align: TextAlignment.Right));
        rv.Children.Add(modes);
        Grid.SetColumn(rightBorder, 1);
        grid.Children.Add(rightBorder);

        var outer = new Border
        {
            Background = B(CSurface),
            BorderBrush = B(CLine),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
        return outer;
    }

    private Control BuildLeft()
    {
        var panel = V(10);

        var head = H(6);
        head.Children.Add(new Border
        {
            Width = 4, Height = 16, Background = B(CAccent),
            CornerRadius = new CornerRadius(2)
        });
        head.Children.Add(H3(T("input.title")));
        panel.Children.Add(head);

        // ── 图上距离卡
        panel.Children.Add(BuildInputCard(
            "input.map", "unit.cm", out _mapCard, out _mapCardLabel, out _mapResultText,
            out _mapSlider, out _mapBox, withSlider: true));

        // ── 实地距离卡
        panel.Children.Add(BuildRealCard());

        // ── 比例尺卡
        panel.Children.Add(BuildScaleCard());

        // 预设
        panel.Children.Add(Label(T("input.presets"), 11.5, CInkFaint));
        _presetPanel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var p in _data.Presets)
        {
            var b = Btn(T(p.NameKey));
            b.Tag = p;
            b.Click += (_, _) => ApplyPreset((PresetItem)b.Tag!);
            b.Margin = new Thickness(0, 0, 6, 6);
            _presetPanel.Children.Add(b);
        }
        panel.Children.Add(_presetPanel);

        panel.Children.Add(Divider());
        var reset = Btn(T("input.reset"));
        reset.Click += (_, _) => ResetState();
        panel.Children.Add(reset);
        panel.Children.Add(Tb(T("input.dragHint"), 10.5, CInkFaint));

        var scroll = new ScrollViewer
        {
            Content = new Border { Padding = new Thickness(14), Child = panel },
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        return Card(scroll);
    }

    private Control BuildInputCard(string labelKey, string unitKey,
        out Border card, out TextBlock label, out TextBlock resultText,
        out Slider slider, out TextBox box, bool withSlider)
    {
        label = Label(T(labelKey), 11.5);
        resultText = Tb("—", 15, CInk, FontWeight.SemiBold);
        var top = new DockPanel();
        top.Children.Add(resultText);
        DockPanel.SetDock(resultText, Dock.Right);
        top.Children.Add(label);

        box = Input("3");
        box.Width = 110;
        box.HorizontalAlignment = HorizontalAlignment.Left;

        slider = new Slider
        {
            Minimum = 0.1,
            Maximum = 20,
            Value = 3,
            TickFrequency = 0.5,
            IsSnapToTickEnabled = false,
            Foreground = B(CAccent),
            Margin = new Thickness(0, 2, 0, 0)
        };

        var stack = V(6);
        stack.Children.Add(top);
        stack.Children.Add(box);
        if (withSlider) stack.Children.Add(slider);

        card = new Border
        {
            Background = B(CSurfaceAlt),
            BorderBrush = B(CLine),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Child = stack
        };
        return card;
    }

    private Control BuildRealCard()
    {
        _realCardLabel = Label(T("input.real"), 11.5);
        _realResultText = Tb("—", 15, CInk, FontWeight.SemiBold);
        var top = new DockPanel();
        top.Children.Add(_realResultText);
        DockPanel.SetDock(_realResultText, Dock.Right);
        top.Children.Add(_realCardLabel);

        _realBox = Input("3");
        _unitBox = new ComboBox
        {
            FontSize = 12.5,
            Background = B(CSurface),
            Foreground = B(CInk),
            BorderBrush = B(CLineStrong),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10, 6),
            MinHeight = 34,
            Width = 92,
            ItemsSource = new[] { "unit.km", "unit.m", "unit.cm" },
            SelectedIndex = 0
        };
        // 显示本地化单位名
        _unitBox.SelectionChanged += (_, _) => { SyncUnitDisplay(); RefreshAll(syncInputs: true); };

        var row = H(8);
        row.Children.Add(_realBox);
        row.Children.Add(_unitBox);

        var stack = V(6);
        stack.Children.Add(top);
        stack.Children.Add(row);

        _realCard = new Border
        {
            Background = B(CSurfaceAlt),
            BorderBrush = B(CLine),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Child = stack
        };
        return _realCard;
    }

    private Control BuildScaleCard()
    {
        _scaleCardLabel = Label(T("input.scale"), 11.5);
        _scaleResultText = Tb("—", 15, CInk, FontWeight.SemiBold);
        var top = new DockPanel();
        top.Children.Add(_scaleResultText);
        DockPanel.SetDock(_scaleResultText, Dock.Right);
        top.Children.Add(_scaleCardLabel);

        _nBox = Input("100000");
        var prefix = Tb(T("input.nPrefix"), 13, CInkDim, FontWeight.SemiBold);
        var row = H(8);
        row.Children.Add(prefix);
        row.Children.Add(_nBox);

        var stack = V(6);
        stack.Children.Add(top);
        stack.Children.Add(row);

        _scaleCard = new Border
        {
            Background = B(CSurfaceAlt),
            BorderBrush = B(CLine),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Child = stack
        };
        return _scaleCard;
    }

    private Control BuildMiddle()
    {
        _ruler = new ScaleRuler
        {
            Data = _data,
            State = _state,
            T = T,
            Height = 470,
            VerticalAlignment = VerticalAlignment.Top
        };
        var holder = new Border
        {
            Background = B(CSurface),
            BorderBrush = B(CLine),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(6),
            Child = _ruler
        };
        return holder;
    }

    private Control BuildRight()
    {
        var panel = V(10);

        // 三种写法
        panel.Children.Add(SectionTitle(T("forms.title")));
        _digitalText = Tb("", 16, CAccentDark, FontWeight.SemiBold);
        _verbalText = Tb("", 12, CInk);
        _segmentText = Tb("", 12, CInk);
        var forms = V(8);
        forms.Children.Add(FormRow(T("forms.digital"), _digitalText));
        forms.Children.Add(FormRow(T("forms.verbal"), _verbalText));
        forms.Children.Add(FormRow(T("forms.segment"), _segmentText));
        panel.Children.Add(forms);

        panel.Children.Add(Divider());

        // 解题步骤
        _stepsToggle = Btn(T("steps.show"));
        _stepsToggle.Click += (_, _) => ToggleSteps();
        var sh = new DockPanel();
        DockPanel.SetDock(_stepsToggle, Dock.Right);
        sh.Children.Add(_stepsToggle);
        sh.Children.Add(SectionTitle(T("steps.title")));
        panel.Children.Add(sh);
        _stepsPanel = V(4);
        _stepsPanel.IsVisible = false;
        panel.Children.Add(_stepsPanel);

        panel.Children.Add(Divider());

        // 判读
        panel.Children.Add(SectionTitle(T("grade.title")));
        _gradeChip = Chip("", CAccentSoft, CAccentDark);
        _gradeChip.HorizontalAlignment = HorizontalAlignment.Left;
        _gradeTitle = Tb("", 13, CInk, FontWeight.SemiBold);
        _gradeNote = Tb("", 11.5, CInkDim);
        _gradeRule = Tb(T("grade.rule"), 11, CAccentDark, FontWeight.SemiBold);
        var gradeBox = V(6);
        gradeBox.Children.Add(_gradeChip);
        gradeBox.Children.Add(_gradeNote);
        gradeBox.Children.Add(_gradeRule);
        panel.Children.Add(gradeBox);

        panel.Children.Add(Divider());

        // 易错点
        panel.Children.Add(SectionTitle(T("pit.title")));
        var pit = V(4);
        pit.Children.Add(Tb(T("pit.1"), 11, CInkDim));
        pit.Children.Add(Tb(T("pit.2"), 11, CInkDim));
        pit.Children.Add(Tb(T("pit.3"), 11, CInkDim));
        panel.Children.Add(pit);

        var scroll = new ScrollViewer
        {
            Content = new Border { Padding = new Thickness(14), Child = panel },
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        return Card(scroll);
    }

    private Control FormRow(string name, TextBlock value)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(62)));
        g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        var n = Tb(name, 11, CInkFaint);
        Grid.SetColumn(n, 0);
        g.Children.Add(n);
        Grid.SetColumn(value, 1);
        g.Children.Add(value);
        return g;
    }

    private Control SectionTitle(string text)
        => Tb(text, 12.5, CInk, FontWeight.SemiBold);

    private Control BuildBottom()
    {
        var grid = new Grid { Margin = new Thickness(16, 4, 16, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(12)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1.4, GridUnitType.Star)));

        // ── 实地尺
        var field = V(8);
        field.Children.Add(SectionTitle(T("field.title")));
        field.Children.Add(Tb(T("field.hint"), 10.5, CInkFaint));
        _landmarkBox = new ComboBox
        {
            FontSize = 12.5,
            Background = B(CSurface),
            Foreground = B(CInk),
            BorderBrush = B(CLineStrong),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10, 6),
            MinHeight = 34,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var lm in _data.Landmarks) _landmarkBox.Items!.Add(T(lm.NameKey));
        _landmarkBox.SelectedIndex = 0;
        _landmarkBox.SelectionChanged += (_, _) => RefreshField();
        field.Children.Add(_landmarkBox);
        _landmarkResult = Tb("", 14, CSuccess, FontWeight.SemiBold);
        field.Children.Add(_landmarkResult);
        var fieldCard = Card(field);
        Grid.SetColumn(fieldCard, 0);
        grid.Children.Add(fieldCard);

        // ── 练习
        var quiz = V(8);
        _quizStreak = Tb(TF("quiz.streak", 0), 11, CAccentDark, FontWeight.SemiBold);
        var qh = new DockPanel();
        DockPanel.SetDock(_quizStreak, Dock.Right);
        qh.Children.Add(_quizStreak);
        qh.Children.Add(SectionTitle(T("quiz.title")));
        quiz.Children.Add(qh);

        _quizPrompt = Tb("", 12, CInk);
        quiz.Children.Add(_quizPrompt);
        _quizUnit = Tb("", 10.5, CInkFaint);
        quiz.Children.Add(_quizUnit);

        var qrow = H(8);
        _quizInput = Input("");
        _quizInput.Width = 140;
        _quizCheck = Btn(T("quiz.check"), primary: true);
        _quizNext = Btn(T("quiz.next"));
        _quizCheck.Click += (_, _) => CheckQuiz();
        _quizNext.Click += (_, _) => NewQuiz();
        qrow.Children.Add(_quizInput);
        qrow.Children.Add(_quizCheck);
        qrow.Children.Add(_quizNext);
        quiz.Children.Add(qrow);

        _quizFeedback = Tb("", 12, CInkDim, FontWeight.SemiBold);
        quiz.Children.Add(_quizFeedback);
        _quizSteps = V(2);
        quiz.Children.Add(_quizSteps);

        var quizCard = Card(quiz);
        Grid.SetColumn(quizCard, 2);
        grid.Children.Add(quizCard);

        return grid;
    }
}

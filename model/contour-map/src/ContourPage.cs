using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace GC.ContourMap;

/// <summary>
/// 等高线模型界面（完整复刻 HTML 课件功能）：
/// 顶部标题 + 全屏；左侧可交互 3D 画布；右侧实验流程/五大地貌/判读要点观察面板；
/// 底部播放控制、俯视切换、上色开关、实时读数；支持键盘 1-5 / 空格快捷键。
/// </summary>
public sealed class ContourPage
{
    private static readonly Color CAppBg = Color.Parse("#0B1524");
    private static readonly Color CPanel = Color.Parse("#101E31");
    private static readonly Color CPanel2 = Color.Parse("#16263C");
    private static readonly Color CLine = Color.Parse("#1E3350");
    private static readonly Color CInk = Color.Parse("#EAF2FF");
    private static readonly Color CInkDim = Color.Parse("#93A8C6");
    private static readonly Color CInkFaint = Color.Parse("#6A80A0");
    private static readonly Color CAccent = Color.Parse("#4D8CFF");
    private static readonly Color CWarn = Color.Parse("#FFB454");
    private static readonly Color CGood = Color.Parse("#5FD69B");

    private readonly Dictionary<string, string> _texts;
    private readonly Dictionary<LandformKind, TerrainField> _landformCache = new();
    private TerrainField _experimentField;

    private ContourView? _view;
    private TextBlock? _readout;
    private TextBlock? _featureText;
    private TextBlock? _hintText;
    private TextBlock? _playLabel;
    private TextBlock? _observeHeading;
    private TextBlock? _observeSummary;
    private StackPanel? _featureList;
    private TextBlock? _viewToggleLabel;

    private readonly List<Border> _flowButtons = new();
    private readonly List<Border> _landformButtons = new();
    private Border? _playBtn;
    private Border? _backBtn;
    private Border? _colorBtn;
    private TextBlock? _colorBtnText;
    private ProgressBar? _progress;

    private int _step = 0;
    private LandformKind? _selectedLandform;
    private bool _disposed;
    private TopLevel? _topLevel;

    private static readonly LandformKind[] LandformOrder =
        { LandformKind.Peak, LandformKind.Ridge, LandformKind.Valley, LandformKind.Saddle, LandformKind.Cliff };

    public ContourPage(Dictionary<string, string> texts)
    {
        _texts = texts;
        _experimentField = new TerrainField(TerrainKind.Mountain, 192);
    }

    private string T(string key) => _texts.TryGetValue(key, out var v) ? v : key;
    private string T(string key, params object[] args)
    {
        var s = T(key);
        try { return string.Format(System.Globalization.CultureInfo.InvariantCulture, s, args); }
        catch { return s; }
    }
    private static SolidColorBrush B(Color c) => new(c);

    private TerrainField GetLandformField(LandformKind kind)
    {
        if (!_landformCache.TryGetValue(kind, out var f))
        {
            f = new TerrainField(kind, 192);
            _landformCache[kind] = f;
        }
        return f;
    }

    public Control Build()
    {
        var root = new Grid { Background = B(CAppBg) };
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        root.Children.Add(BuildHeader());

        var body = new Grid { Margin = new Thickness(12, 8, 12, 6) };
        body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        body.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(344)));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        _view = new ContourView(_experimentField, T);
        _view.HoverChanged += OnHover;
        _view.StepChanged += OnStepChanged;
        _view.ProgressChanged += OnProgressChanged;
        _view.PlaybackFinished += OnPlaybackFinished;

        var canvasHolder = new Border
        {
            Background = B(Color.Parse("#08111E")),
            BorderBrush = B(CLine),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true,
            Child = _view
        };
        Grid.SetRow(canvasHolder, 1);
        body.Children.Add(canvasHolder);

        // ── 五大典型地貌标签（横向，像课件的选项卡）──
        var tabs = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        _backBtn = MakePillButton(T("btn.backExperiment"), CAccent);
        _backBtn.IsVisible = false;
        _backBtn.PointerPressed += (_, _) => BackToExperiment();
        tabs.Children.Add(_backBtn);
        foreach (var kind in LandformOrder)
        {
            var btn = MakeLandformButton(kind);
            _landformButtons.Add(btn);
            tabs.Children.Add(btn);
        }
        body.Children.Add(tabs);

        var side = BuildSidePanel();
        Grid.SetColumn(side, 1);
        Grid.SetRow(side, 0);
        Grid.SetRowSpan(side, 2);
        body.Children.Add(side);

        // 窄窗口（封面缩略图、小屏）隐藏右侧栏，画布优先；宽窗口才展示流程与观察面板
        void ApplyResponsive(double width)
        {
            var narrow = width < 980;
            side.IsVisible = !narrow;
            body.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(344);
        }
        root.SizeChanged += (_, e) => ApplyResponsive(e.NewSize.Width);

        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var footer = BuildFooter();
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        root.AttachedToVisualTree += (_, _) =>
        {
            _topLevel = TopLevel.GetTopLevel(root);
        };
        root.KeyDown += OnKeyDown;
        root.Focusable = true;

        return root;
    }

    /// <summary>窗口关闭时释放视图计时器，避免持有控件。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _view?.DisposeView();
    }

    private Control BuildHeader()
    {
        var title = new TextBlock
        {
            Text = T("app.title"),
            Foreground = B(CInk),
            FontSize = 18,
            FontWeight = FontWeight.SemiBold
        };
        var sub = new TextBlock
        {
            Text = T("app.subtitle"),
            Foreground = B(CInkDim),
            FontSize = 11.5
        };
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(title);
        stack.Children.Add(sub);

        var fsLabel = new TextBlock { Text = T("btn.fullscreen"), Foreground = B(CInk), FontSize = 11.5 };
        var fsBtn = new Border
        {
            Background = B(CPanel2),
            BorderBrush = B(CLine),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = fsLabel
        };
        fsBtn.PointerPressed += (_, _) => ToggleFullscreen(fsLabel);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(stack);
        Grid.SetColumn(fsBtn, 1);
        grid.Children.Add(fsBtn);

        return new Border
        {
            Background = B(CPanel),
            BorderBrush = B(CLine),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(18, 11, 18, 12),
            Child = grid
        };
    }

    private Control BuildSidePanel()
    {
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(12, 0, 0, 0) };

        // ── 实验流程四步 ──
        panel.Children.Add(Section(T("flow.title")));
        for (var i = 0; i < 4; i++)
        {
            var btn = MakeFlowButton(i);
            _flowButtons.Add(btn);
            panel.Children.Add(btn);
        }

        panel.Children.Add(Divider());

        // ── 观察面板 ──
        _observeHeading = new TextBlock
        {
            Text = T("observe.title"),
            Foreground = B(CWarn),
            FontSize = 12.5,
            FontWeight = FontWeight.SemiBold
        };
        _observeSummary = new TextBlock
        {
            Text = T("observe.default"),
            Foreground = B(CInkDim),
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 16
        };
        _featureList = new StackPanel { Spacing = 3 };
        RenderObservation(null);

        panel.Children.Add(_observeHeading);
        panel.Children.Add(_observeSummary);
        panel.Children.Add(_featureList);

        // ── 判读要点 ──
        panel.Children.Add(Section(T("side.rules")));
        panel.Children.Add(Bullet(T("rule.interval")));
        panel.Children.Add(Bullet(T("rule.dense")));
        panel.Children.Add(Bullet(T("rule.index")));
        panel.Children.Add(Bullet(T("rule.height")));

        var scroll = new ScrollViewer
        {
            Content = new Border
            {
                Background = B(CPanel),
                BorderBrush = B(CLine),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 12),
                Child = panel
            },
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        return scroll;
    }

    private Border MakeFlowButton(int index)
    {
        var name = new TextBlock
        {
            Text = $"{index + 1}. {T("flow.step." + index)}",
            Foreground = B(CInk),
            FontSize = 12.5,
            FontWeight = FontWeight.SemiBold
        };
        var desc = new TextBlock
        {
            Text = T("flow.desc." + index),
            Foreground = B(CInkFaint),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 15
        };
        var stack = new StackPanel { Spacing = 1 };
        stack.Children.Add(name);
        stack.Children.Add(desc);
        var border = new Border
        {
            Background = B(CPanel2),
            BorderBrush = B(CLine),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(9, 6),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = stack,
            Tag = index
        };
        border.PointerPressed += (_, _) =>
        {
            BackToExperiment();
            _view?.SetStep((ExperimentStep)index);
            UpdateFlowStyles();
        };
        return border;
    }

    private Border MakeLandformButton(LandformKind kind)
    {
        var key = kind.ToString().ToLowerInvariant();
        var name = new TextBlock
        {
            Text = T("landform." + key),
            Foreground = B(CInk),
            FontSize = 12.5,
            FontWeight = FontWeight.SemiBold
        };
        var sub = new TextBlock
        {
            Text = T("landform." + key + ".sub"),
            Foreground = B(CInkFaint),
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center
        };
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        stack.Children.Add(name);
        stack.Children.Add(sub);
        var border = new Border
        {
            Background = B(CPanel2),
            BorderBrush = B(CLine),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(14, 7),
            Margin = new Thickness(0, 0, 8, 4),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = stack,
            Tag = kind
        };
        border.PointerPressed += (_, _) => SelectLandform(kind);
        return border;
    }

    private void SelectLandform(LandformKind kind)
    {
        _selectedLandform = kind;
        _view?.Pause();
        _view?.SetField(GetLandformField(kind));
        _view?.SetMode(ViewMode.Perspective);
        _view?.ShowContourPattern();      // 地貌专题先看完整的等高线形态
        UpdateLandformStyles();
        UpdateViewToggleLabel();

        var key = kind.ToString().ToLowerInvariant();
        if (_observeHeading != null) _observeHeading.Text = T("landform." + key) + " · " + T("landform." + key + ".sub");
        if (_observeSummary != null) _observeSummary.Text = T("landform." + key + ".summary");
        RenderFeatureList(key);
        if (_backBtn != null) _backBtn.IsVisible = true;
        if (_hintText != null) _hintText.Text = T("landform." + key + ".hint");
        SetPlayLabel(T("btn.replay"));
    }

    private void BackToExperiment()
    {
        if (_selectedLandform == null) return;
        _selectedLandform = null;
        _view?.SetField(_experimentField);
        _view?.SetMode(ViewMode.Perspective);
        _view?.Reset();
        UpdateLandformStyles();
        UpdateViewToggleLabel();
        RenderObservation(null);
        if (_backBtn != null) _backBtn.IsVisible = false;
        if (_hintText != null) _hintText.Text = T("hint.play");
        SetPlayLabel(T("btn.play"));
    }

    private void RenderObservation(LandformKind? kind)
    {
        if (_observeHeading == null || _observeSummary == null || _featureList == null) return;
        if (kind == null)
        {
            _observeHeading.Text = T("observe.title");
            _observeSummary.Text = T("observe.default");
            _featureList.Children.Clear();
            _featureList.Children.Add(Bullet(T("observe.default.f1")));
            _featureList.Children.Add(Bullet(T("observe.default.f2")));
        }
        else
        {
            var key = kind.Value.ToString().ToLowerInvariant();
            _observeHeading.Text = T("landform." + key) + " · " + T("landform." + key + ".sub");
            _observeSummary.Text = T("landform." + key + ".summary");
            RenderFeatureList(key);
        }
    }

    private void RenderFeatureList(string key)
    {
        if (_featureList == null) return;
        _featureList.Children.Clear();
        for (var i = 1; i <= 3; i++)
        {
            var k = $"landform.{key}.f{i}";
            if (_texts.ContainsKey(k)) _featureList.Children.Add(Bullet(T(k)));
        }
    }

    private Control BuildFooter()
    {
        // 播放 / 重置
        _playLabel = new TextBlock { Text = T("btn.play"), Foreground = B(Colors.White), FontSize = 11.5, FontWeight = FontWeight.SemiBold };
        _playBtn = new Border
        {
            Background = B(CAccent),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 7),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = _playLabel
        };
        _playBtn.PointerPressed += (_, _) => TogglePlay();

        var resetBtn = MakePillButton(T("btn.reset"), Color.Parse("#2A3B55"));
        resetBtn.PointerPressed += (_, _) =>
        {
            BackToExperiment();
            _view?.Reset();
            if (_progress != null) _progress.Value = 0;
            if (_hintText != null) _hintText.Text = T("hint.play");
            SetPlayLabel(T("btn.play"));
        };

        // 俯视 / 立体切换
        _viewToggleLabel = new TextBlock { Text = T("btn.viewTop"), Foreground = B(CInk), FontSize = 11.5 };
        var viewBtn = new Border
        {
            Background = B(CPanel2),
            BorderBrush = B(CLine),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(11, 6),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = _viewToggleLabel
        };
        viewBtn.PointerPressed += (_, _) =>
        {
            _view?.ToggleMode();
            UpdateViewToggleLabel();
        };

        // 上色开关
        _colorBtnText = new TextBlock { Text = T("btn.colorOn"), Foreground = B(Colors.White), FontSize = 11.5 };
        _colorBtn = new Border
        {
            Background = B(CAccent),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(11, 6),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = _colorBtnText
        };
        _colorBtn.PointerPressed += (_, _) => ToggleColor();

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        controls.Children.Add(_playBtn);
        controls.Children.Add(resetBtn);
        controls.Children.Add(viewBtn);
        controls.Children.Add(_colorBtn);

        _progress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = B(CAccent),
            Background = B(Color.Parse("#1E3350"))
        };

        _hintText = new TextBlock { Text = T("hint.play"), Foreground = B(CInkDim), FontSize = 11.5 };

        _readout = new TextBlock { Text = T("footer.hint"), Foreground = B(CInk), FontSize = 11.5 };
        _featureText = new TextBlock
        {
            Text = "",
            Foreground = B(CGood),
            FontSize = 11.5,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var info = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        info.Children.Add(_readout);
        Grid.SetColumn(_featureText, 1);
        info.Children.Add(_featureText);

        var leftStack = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        leftStack.Children.Add(_hintText);
        leftStack.Children.Add(info);

        var progStack = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        progStack.Children.Add(leftStack);
        progStack.Children.Add(_progress);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(progStack);
        Grid.SetColumn(controls, 1);
        row.Children.Add(controls);

        return new Border
        {
            Background = B(CPanel),
            BorderBrush = B(CLine),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(16, 9),
            Child = row
        };
    }

    private void TogglePlay()
    {
        if (_view == null) return;
        if (_selectedLandform != null) BackToExperiment();
        _view.TogglePlay();
        SetPlayLabel(_view.IsPlaying ? T("btn.pause") : T("btn.play"));
        if (_hintText != null)
            _hintText.Text = _view.IsPlaying ? T("hint.playing") : T("hint.paused");
    }

    private void ToggleColor()
    {
        if (_view == null) return;
        _view.Colored = !_view.Colored;
        if (_colorBtn != null) _colorBtn.Background = B(_view.Colored ? CAccent : Color.Parse("#2A3B55"));
        if (_colorBtnText != null) _colorBtnText.Text = _view.Colored ? T("btn.colorOn") : T("btn.color");
    }

    private void SetPlayLabel(string text) { if (_playLabel != null) _playLabel.Text = text; }

    private void UpdateViewToggleLabel()
    {
        if (_viewToggleLabel == null || _view == null) return;
        _viewToggleLabel.Text = _view.Mode == ViewMode.Top ? T("btn.view3d") : T("btn.viewTop");
    }

    private void UpdateFlowStyles()
    {
        for (var i = 0; i < _flowButtons.Count; i++)
        {
            var b = _flowButtons[i];
            var active = i == _step && _selectedLandform == null;
            var done = i < _step && _selectedLandform == null;
            StylePill(b, active ? CAccent : done ? Color.Parse("#1C3A5E") : CPanel2,
                active ? Colors.White : done ? CGood : CInkDim);
        }
    }

    private void UpdateLandformStyles()
    {
        for (var i = 0; i < _landformButtons.Count; i++)
        {
            var b = _landformButtons[i];
            var kind = LandformOrder[i];
            var active = _selectedLandform == kind;
            StylePill(b, active ? CAccent : CPanel2, active ? Colors.White : CInkDim);
        }
    }

    private static void StylePill(Border b, Color bg, Color fg)
    {
        b.Background = B(bg);
        if (b.Child is TextBlock tb) tb.Foreground = B(fg);
        else if (b.Child is StackPanel sp)
            foreach (var c in sp.Children)
                if (c is TextBlock t) t.Foreground = B(fg);
    }

    private void OnStepChanged(ExperimentStep step)
    {
        _step = (int)step;
        UpdateFlowStyles();
        if (_selectedLandform == null && _observeSummary != null)
            _observeSummary.Text = T("flow.desc." + _step);
    }

    private void OnProgressChanged(ExperimentStep step, double value)
        => _progress?.Value = value * 100;

    private void OnPlaybackFinished()
    {
        SetPlayLabel(T("btn.replay"));
        if (_hintText != null) _hintText.Text = T("hint.done");
    }

    private void OnHover(double u, double v, float height, TerrainFeature feature)
    {
        if (_readout == null || _featureText == null) return;
        var field = _view?.Field ?? _experimentField;
        var relative = height - field.MinHeight;
        _readout.Text = T("footer.readout", (int)height, (int)relative);
        _featureText.Text = FeatureName(feature);
        _featureText.Foreground = B(feature switch
        {
            TerrainFeature.Peak => Color.Parse("#FFD166"),
            TerrainFeature.Cliff => Color.Parse("#FF7B7B"),
            TerrainFeature.Saddle => Color.Parse("#C08CFF"),
            TerrainFeature.Ridge => Color.Parse("#5FD69B"),
            TerrainFeature.Valley => Color.Parse("#5BC8FF"),
            _ => CInkDim
        });
    }

    private string FeatureName(TerrainFeature f) => f switch
    {
        TerrainFeature.Peak => T("feature.peak"),
        TerrainFeature.Ridge => T("feature.ridge"),
        TerrainFeature.Valley => T("feature.valley"),
        TerrainFeature.Saddle => T("feature.saddle"),
        TerrainFeature.Cliff => T("feature.cliff"),
        TerrainFeature.Slope => T("feature.slope"),
        _ => ""
    };

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // 空格：播放/暂停
        if (e.Key == Key.Space)
        {
            e.Handled = true;
            TogglePlay();
            return;
        }
        // 1-5：切换五大地貌
        var idx = -1;
        switch (e.Key)
        {
            case Key.D1: case Key.NumPad1: idx = 0; break;
            case Key.D2: case Key.NumPad2: idx = 1; break;
            case Key.D3: case Key.NumPad3: idx = 2; break;
            case Key.D4: case Key.NumPad4: idx = 3; break;
            case Key.D5: case Key.NumPad5: idx = 4; break;
        }
        if (idx >= 0 && idx < LandformOrder.Length)
        {
            e.Handled = true;
            SelectLandform(LandformOrder[idx]);
        }
    }

    private void ToggleFullscreen(TextBlock label)
    {
        if (_topLevel is not Window window) return;
        if (window.WindowState == WindowState.FullScreen)
        {
            window.WindowState = WindowState.Normal;
            label.Text = T("btn.fullscreen");
        }
        else
        {
            window.WindowState = WindowState.FullScreen;
            label.Text = T("btn.exitFullscreen");
        }
    }

    // ── 控件工厂

    private static TextBlock Section(string text) => new()
    {
        Text = text,
        Foreground = BConst(CInk),
        FontSize = 12.5,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 2, 0, 2)
    };

    private static Border Divider() => new()
    {
        Height = 1,
        Background = BConst(CLine),
        Margin = new Thickness(0, 4, 0, 2)
    };

    private static TextBlock Bullet(string text) => new()
    {
        Text = "•  " + text,
        Foreground = BConst(CInkDim),
        FontSize = 11.5,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = 17
    };

    private Border MakePillButton(string label, Color bg)
    {
        var tb = new TextBlock { Text = label, Foreground = B(Colors.White), FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center };
        return new Border
        {
            Background = B(bg),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(11, 6),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = tb
        };
    }

    private static SolidColorBrush BConst(Color c) => new(c);
}

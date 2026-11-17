using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using GC.ModelSdk;

namespace GC.EarthModels;

/// <summary>
/// 模型自己的界面：画布 + 触控友好工具栏 + 课堂演示卡 + 节气定格与步进。
/// 启动器只负责下载、开窗口和进度条，不参与任何模型渲染或教学交互。
/// </summary>
public sealed class EarthModelView : UserControl, IDisposable
{
    private static readonly IBrush PanelBg = new SolidColorBrush(Color.Parse("#C40A1020"));
    private static readonly IBrush ControlBg = new SolidColorBrush(Color.Parse("#16233C"));
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#2E6BE6"));
    private static readonly IBrush TitleBrush = new SolidColorBrush(Color.Parse("#F2F6FF"));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#DCE7FA"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#7E93B8"));
    private static readonly IBrush FaintBrush = new SolidColorBrush(Color.Parse("#61779C"));
    private static readonly IBrush BodyBrush = new SolidColorBrush(Color.Parse("#8CA0C4"));
    private static readonly IBrush WarmBrush = new SolidColorBrush(Color.Parse("#FFC46B"));

    private static readonly TimeSpan HoverDuration = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan PanelDuration = TimeSpan.FromMilliseconds(190);

    private readonly IModelContext _context;
    private readonly ModelStrings _texts;
    private readonly SceneRuntime _runtime;
    private readonly List<string> _views;
    private readonly List<ModelButton> _viewButtons = new();
    private readonly CelestialView _canvas;
    private readonly CelestialView _closeup;
    private readonly DispatcherTimer _uiTimer;

    private Border? _viewSwitch;
    private StackPanel? _seasonPanel;
    private Border? _lessons;
    private Border? _closeupHost;
    private TextBlock? _closeupLabel;
    private ModelButton? _playButton;
    private ModelButton? _overheadButton;
    private TextBlock? _zoomText;
    private TextBlock? _cardState;
    private TextBlock? _cardTitle;
    private TextBlock? _cardDate;
    private TextBlock? _cardDirect;
    private TextBlock? _cardNote;

    private bool _playing = true;
    private bool _labels = true;
    private bool _orbit = true;
    private bool _grid = true;
    private bool _tilt;
    private bool _showLessons = true;
    private bool _showCloseup = true;
    private bool _overhead;
    private double _lastDeclination = double.NaN;

    public EarthModelView(IModelContext context, SceneRuntime runtime)
    {
        _context = context;
        _runtime = runtime;
        _texts = ModelStrings.Load(context.DataDirectory, context.Language);
        _views = ResolveViews(runtime.Scene);
        // 自转模型主画布本身就是放大的地球，不需要再来一份特写
        _showCloseup = IsRevolution;

        Background = new SolidColorBrush(Color.Parse("#05070F"));

        _canvas = new CelestialView
        {
            Scene = runtime,
            Texts = _texts,
            ShowLabels = true,
            ShowOrbit = true,
            ShowGraticule = true,
            ShowKeyLatitudes = true,
            FrameRate = 120,
            View = IsRevolution ? SceneView.Revolution : SceneView.Rotation
        };
        _canvas.JumpToOrbit(StartAngle);
        _canvas.JumpToSpin(20);
        _canvas.ZoomChanged += zoom =>
        {
            if (_zoomText != null) _zoomText.Text = $"{zoom * 100:0}%";
        };

        // 课堂特写：同一套渲染器，把当前直射纬度放大给全班看
        _closeup = new CelestialView
        {
            Scene = runtime,
            Texts = _texts,
            View = SceneView.Rotation,
            IsPlaying = false,
            ShowHud = false,
            ShowLabels = true,
            ShowGraticule = false,
            ShowKeyLatitudes = true,
            InsetMode = true,
            Width = 240,
            Height = 240
        };
        _closeup.JumpToSpin(20);

        _uiTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(140),
            DispatcherPriority.Render, (_, _) => RefreshTeaching());

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };

        var top = BuildTopBar();
        root.Children.Add(top);

        var options = BuildOptionsBar();
        Grid.SetRow(options, 1);
        root.Children.Add(options);

        var stage = new Grid();
        stage.Children.Add(_canvas);
        stage.Children.Add(BuildZoomPad());
        _lessons = BuildClassroomCard();
        stage.Children.Add(_lessons);
        Grid.SetRow(stage, 2);
        root.Children.Add(stage);

        var bottom = BuildBottomBar();
        Grid.SetRow(bottom, 3);
        root.Children.Add(bottom);

        Content = root;
        ApplyVisibility();
        RefreshTeaching();

        // 打开时整窗淡入，避免「啪」地出现
        Opacity = 0;
        Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(260),
                Easing = new CubicEaseOut()
            }
        };
        AttachedToVisualTree += (_, _) =>
        {
            Opacity = 1;
            _uiTimer.Start();
            if (TopLevel.GetTopLevel(this) is Window window)
            {
                _hostWindow = window;
                window.KeyDown += OnKeyDown;
            }
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _uiTimer.Stop();
            if (_hostWindow != null)
            {
                _hostWindow.KeyDown -= OnKeyDown;
                _hostWindow = null;
            }
        };
    }

    private Window? _hostWindow;

    /// <summary>
    /// 窗口关闭时释放计时器、解绑窗口事件并释放两块球面位图，
    /// 否则 DispatcherTimer / KeyDown 会持有控件，使 collectible ALC 无法卸载。
    /// </summary>
    public void Dispose()
    {
        _uiTimer.Stop();
        if (_hostWindow != null)
        {
            _hostWindow.KeyDown -= OnKeyDown;
            _hostWindow = null;
        }
        _canvas.Dispose();
        _closeup.Dispose();
        System.GC.SuppressFinalize(this);
    }

    private string CurrentView { get; set; } = "rotation";
    private double StartAngle => IsRevolution ? 60 : 0;
    private bool IsRevolution => string.Equals(CurrentView, "revolution", StringComparison.OrdinalIgnoreCase);
    private bool HasViewSwitch => _views.Count > 1;
    private List<SeasonPoint> Seasons => _runtime.Scene.Orbit.Seasons;

    private static string NormalizeView(string? view)
        => string.Equals(view, "revolution", StringComparison.OrdinalIgnoreCase) ? "revolution" : "rotation";

    private List<string> ResolveViews(SceneDefinition scene)
    {
        var list = new List<string>();
        foreach (var v in scene.Views)
        {
            var normalized = NormalizeView(v);
            if (!list.Contains(normalized)) list.Add(normalized);
        }
        if (list.Count == 0) list.Add(NormalizeView(scene.DefaultView));
        CurrentView = list[0];
        return list;
    }

    // ---------------------------------------------------------------- 顶部主工具栏
    private Control BuildTopBar()
    {
        // 关闭按钮统一放在窗口右上角的自建标题栏，这里不再重复放左上角
        var title = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock
        {
            Text = _texts.Pick(_runtime.Scene.NameI18n, _texts["title"]),
            Foreground = TitleBrush,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold
        });
        title.Children.Add(new TextBlock
        {
            Text = _texts.Pick(_runtime.Scene.DescriptionI18n, ""),
            Foreground = MutedBrush,
            FontSize = 10.5,
            MaxWidth = 330,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var switchPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        switchPanel.Children.Add(MakeViewButton("rotation", _texts["view.rotation"]));
        switchPanel.Children.Add(MakeViewButton("revolution", _texts["view.revolution"]));
        _viewSwitch = new Border
        {
            Background = ControlBg,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(3),
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = switchPanel
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        _playButton = MakeButton(_texts["pause"], TogglePlay);
        _playButton.MinWidth = 74;
        actions.Children.Add(_playButton);
        actions.Children.Add(MakeButton(_texts["reset"], () =>
        {
            _canvas.ResetAnimation();
            _canvas.JumpToOrbit(StartAngle);
            RefreshTeaching();
        }));
        actions.Children.Add(MakeButton("◀", () => Step(-5), large: true));
        actions.Children.Add(MakeButton("▶", () => Step(5), large: true));
        actions.Children.Add(MakeButton(_texts["classroom"], () => SetLessonsVisible(!_showLessons)));
        actions.Children.Add(MakeButton("⛶", ToggleFullScreen, large: true));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto") };
        Grid.SetColumn(title, 0);
        title.Margin = new Thickness(14, 0, 0, 0);
        grid.Children.Add(title);
        Grid.SetColumn(_viewSwitch, 1);
        grid.Children.Add(_viewSwitch);
        Grid.SetColumn(actions, 3);
        grid.Children.Add(actions);

        return new Border { Background = PanelBg, Padding = new Thickness(16, 9), Child = grid };
    }

    // ---------------------------------------------------------------- 显示选项行
    private Control BuildOptionsBar()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(MakeToggle(_texts["labels"], _labels, on => { _labels = on; _canvas.ShowLabels = on; }));
        row.Children.Add(MakeToggle(_texts["orbit"], _orbit, on => { _orbit = on; _canvas.ShowOrbit = on; }));
        row.Children.Add(MakeToggle(_texts["grid"], _grid, on => { _grid = on; _canvas.ShowGraticule = on; }));
        row.Children.Add(MakeToggle(_texts["tilt"], _tilt, on => { _tilt = on; _canvas.ExaggerateTilt = on; }));
        // 「放大」切换按钮已按需求移除；特写视图本身仍随公转显示
        _overheadButton = MakeToggle(_texts["overhead"], _overhead, on =>
        {
            _overhead = on;
            // 视角只由这个显式控件改变；拖动永远不会碰它
            _canvas.CameraElevation = on ? (IsRevolution ? 74 : 62) : double.NaN;
        });
        row.Children.Add(_overheadButton);

        var shortcut = new TextBlock
        {
            Text = _texts["shortcutHint"],
            Foreground = FaintBrush,
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(row);
        Grid.SetColumn(shortcut, 1);
        grid.Children.Add(shortcut);
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#A00A1020")),
            Padding = new Thickness(16, 7),
            Child = grid
        };
    }

    private ModelButton MakeViewButton(string view, string label)
    {
        var button = MakeButton(label, () => SwitchView(view));
        button.SetBase(string.Equals(CurrentView, view, StringComparison.OrdinalIgnoreCase) ? Accent : ControlBg);
        button.Tag = view;
        button.IsVisible = _views.Contains(view);
        _viewButtons.Add(button);
        return button;
    }

    private ModelButton MakeToggle(string label, bool initial, Action<bool> changed)
    {
        var state = initial;
        ModelButton? button = null;
        button = MakeButton(label, () =>
        {
            state = !state;
            changed(state);
            button?.SetBase(state ? Accent : ControlBg);
        });
        button.SetBase(initial ? Accent : ControlBg);
        return button;
    }

    private void SwitchView(string view)
    {
        CurrentView = NormalizeView(view);
        _canvas.View = IsRevolution ? SceneView.Revolution : SceneView.Rotation;
        _canvas.JumpToOrbit(StartAngle);
        _canvas.CameraElevation = _overhead ? (IsRevolution ? 74 : 62) : double.NaN;
        foreach (var button in _viewButtons)
            button.SetBase(string.Equals(button.Tag as string, CurrentView, StringComparison.OrdinalIgnoreCase)
                ? Accent
                : ControlBg);
        ApplyVisibility();
        RefreshTeaching();
    }

    // ---------------------------------------------------------------- 画布浮动缩放板（触控大按钮）
    private Control BuildZoomPad()
    {
        var plus = MakeButton("＋", () => _canvas.ZoomBy(1.25), large: true);
        var minus = MakeButton("－", () => _canvas.ZoomBy(1 / 1.25), large: true);
        var reset = MakeButton("⟲", () => _canvas.ResetView(), large: true);
        foreach (var b in new[] { plus, minus, reset })
        {
            b.MinWidth = 62;
            b.MinHeight = 48;
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        _zoomText = new TextBlock
        {
            Text = "100%",
            Foreground = TextBrush,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 2, 0, 2)
        };

        var stack = new StackPanel { Spacing = 6, Width = 62 };
        stack.Children.Add(new TextBlock
        {
            Text = _texts["zoom"],
            Foreground = MutedBrush,
            FontSize = 10.5,
            TextAlignment = TextAlignment.Center
        });
        stack.Children.Add(plus);
        stack.Children.Add(_zoomText);
        stack.Children.Add(minus);
        stack.Children.Add(reset);

        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(16, 0, 0, 16),
            Background = new SolidColorBrush(Color.Parse("#C00B1226")),
            BorderBrush = new SolidColorBrush(Color.Parse("#26374F")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10),
            Child = stack
        };
    }

    // ---------------------------------------------------------------- 底部
    private Control BuildBottomBar()
    {
        var speedLabel = new TextBlock
        {
            Text = _texts["speed"],
            Foreground = new SolidColorBrush(Color.Parse("#9DB0D0")),
            VerticalAlignment = VerticalAlignment.Center
        };

        var slider = new Slider
        {
            Minimum = 0.25,
            Maximum = 4,
            Value = 1,
            Margin = new Thickness(12, 0, 0, 0),
            Width = 180,
            Height = 38
        };
        var speedText = new TextBlock
        {
            Text = "1×",
            Foreground = TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 46,
            Margin = new Thickness(6, 0, 0, 0)
        };
        slider.ValueChanged += (_, e) =>
        {
            _canvas.Speed = e.NewValue;
            speedText.Text = $"{e.NewValue:0.##}×";
        };

        var hint = new TextBlock
        {
            Text = _texts["hint"],
            Foreground = FaintBrush,
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        _seasonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            VerticalAlignment = VerticalAlignment.Center
        };
        _seasonPanel.Children.Add(new TextBlock
        {
            Text = _texts["seasons"],
            Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });
        foreach (var season in Seasons) _seasonPanel.Children.Add(MakeSeasonButton(season));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto") };
        grid.Children.Add(speedLabel);
        Grid.SetColumn(slider, 1);
        grid.Children.Add(slider);
        Grid.SetColumn(speedText, 2);
        grid.Children.Add(speedText);
        Grid.SetColumn(hint, 3);
        grid.Children.Add(hint);
        Grid.SetColumn(_seasonPanel, 4);
        grid.Children.Add(_seasonPanel);

        return new Border { Background = PanelBg, Padding = new Thickness(16, 8), Child = grid };
    }

    private ModelButton MakeSeasonButton(SeasonPoint season)
    {
        var captured = season;
        var button = MakeButton(_texts[season.Key], () => JumpToSeason(captured));
        button.MinHeight = 44;
        return button;
    }

    // ---------------------------------------------------------------- 课堂演示卡
    private Border BuildClassroomCard()
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock
        {
            Text = _texts["classroom"],
            Foreground = TitleBrush,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        });
        var close = MakeButton("✕", () => SetLessonsVisible(false));
        close.SetBase(Brushes.Transparent);
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        _cardState = new TextBlock
        {
            Text = _texts["running"],
            Foreground = MutedBrush,
            FontSize = 11,
            Margin = new Thickness(0, 6, 0, 0)
        };
        _cardTitle = new TextBlock
        {
            Foreground = TitleBrush,
            FontSize = 20,
            FontWeight = FontWeight.Bold
        };
        _cardDate = new TextBlock { Foreground = MutedBrush, FontSize = 12 };
        _cardDirect = new TextBlock
        {
            Foreground = WarmBrush,
            FontSize = 27,
            FontWeight = FontWeight.Bold
        };
        _cardNote = new TextBlock
        {
            Foreground = BodyBrush,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18
        };

        _closeupHost = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderBrush = new SolidColorBrush(Color.Parse("#26374F")),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 2),
            Child = _closeup
        };

        var readouts = new StackPanel { Spacing = 2 };
        readouts.Children.Add(_cardState);
        readouts.Children.Add(_cardTitle);
        readouts.Children.Add(_cardDate);
        readouts.Children.Add(new TextBlock
        {
            Text = _texts["directPoint"],
            Foreground = MutedBrush,
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0)
        });
        readouts.Children.Add(_cardDirect);
        readouts.Children.Add(_cardNote);
        _closeupLabel = new TextBlock
        {
            Text = _texts["closeup"],
            Foreground = MutedBrush,
            FontSize = 11,
            Margin = new Thickness(0, 6, 0, 0)
        };
        readouts.Children.Add(_closeupLabel);
        readouts.Children.Add(_closeupHost);

        var list = new StackPanel { Spacing = 2 };
        foreach (var lesson in _runtime.Scene.Lessons)
        {
            var title = new TextBlock
            {
                Text = _texts.Pick(lesson.TitleI18n, ""),
                Foreground = new SolidColorBrush(Color.Parse("#CFE0FF")),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            var body = new TextBlock
            {
                Text = _texts.Pick(lesson.BodyI18n, ""),
                Foreground = BodyBrush,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            };
            var stack = new StackPanel { Spacing = 3 };
            stack.Children.Add(title);
            stack.Children.Add(body);

            var button = new Button
            {
                Content = stack,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 9),
                Margin = new Thickness(0, 1),
                MinHeight = 44,
                Cursor = new Cursor(StandardCursorType.Hand),
                Transitions = new Transitions
                {
                    new BrushTransition { Property = BackgroundProperty, Duration = HoverDuration }
                }
            };
            var captured = lesson;
            button.Click += (_, _) => ApplyLesson(captured);
            button.PointerEntered += (_, _) => button.Background = new SolidColorBrush(Color.Parse("#1B2A47"));
            button.PointerExited += (_, _) => button.Background = Brushes.Transparent;
            list.Children.Add(button);
        }

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(readouts);
        content.Children.Add(new TextBlock
        {
            Text = _texts["lessons"],
            Foreground = TitleBrush,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12.5
        });
        content.Children.Add(list);

        var bodyGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        bodyGrid.Children.Add(header);
        var scroller = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 8, 0, 0)
        };
        Grid.SetRow(scroller, 1);
        bodyGrid.Children.Add(scroller);

        var panel = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Width = 296,
            Margin = new Thickness(0, 12, 16, 12),
            Background = new SolidColorBrush(Color.Parse("#D00B1226")),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(13),
            BorderBrush = new SolidColorBrush(Color.Parse("#26374F")),
            BorderThickness = new Thickness(1),
            Child = bodyGrid,
            RenderTransform = new TranslateTransform(0, 0)
        };
        panel.Transitions = new Transitions
        {
            new DoubleTransition { Property = OpacityProperty, Duration = PanelDuration, Easing = new CubicEaseOut() },
            new DoubleTransition
            {
                Property = TranslateTransform.XProperty,
                Duration = PanelDuration,
                Easing = new CubicEaseOut()
            }
        };
        return panel;
    }

    private void SetLessonsVisible(bool visible)
    {
        _showLessons = visible;
        if (_lessons == null) return;

        if (visible)
        {
            _lessons.IsVisible = true;
            _lessons.Opacity = 1;
            if (_lessons.RenderTransform is TranslateTransform transform) transform.X = 0;
            return;
        }

        _lessons.Opacity = 0;
        if (_lessons.RenderTransform is TranslateTransform hidden) hidden.X = 26;

        var timer = new DispatcherTimer { Interval = PanelDuration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!_showLessons && _lessons != null) _lessons.IsVisible = false;
        };
        timer.Start();
    }

    /// <summary>课堂卡与地球特写跟随当前轨道位置刷新（播放时每 140ms 更新一次）。</summary>
    private void RefreshTeaching()
    {
        if (_cardTitle == null) return;
        var season = IsRevolution ? _canvas.NearestSeason(6) : null;
        var decl = _canvas.SubsolarLatitude;

        _cardState!.Text = _playing ? _texts["running"] : _texts["frozen"];
        _cardState.Foreground = _playing ? MutedBrush : WarmBrush;
        _cardTitle.Text = season != null
            ? _texts[season.Key]
            : (IsRevolution ? _texts["seasons"] : _texts.Pick(_runtime.Scene.NameI18n, _texts["title"]));
        _cardDate!.Text = IsRevolution ? _canvas.CurrentDate : _texts["dayNight"];
        _cardDirect!.Text = season?.DeclinationDeg.HasValue == true
            ? FormatDeclination(season.DeclinationDeg.Value)
            : _canvas.DeclinationLabel;
        _cardNote!.Text = !IsRevolution
            ? _texts["rotationTeach"]
            : season != null
                ? _texts.Pick(season.TeachingI18n, _texts["orbitTeach"])
                : _texts["orbitTeach"];

        // 特写的直射纬度用缓动逼近目标，而不是瞬间跳变：
        // 公转时拖动/改变轨道角，晨昏线会平滑地移过去（每次 timer 步进一帧）。
        if (_showCloseup)
        {
            var target = decl;
            var current = _closeup.DeclinationDeg;
            var diff = target - current;
            if (Math.Abs(diff) < 0.02)
            {
                _closeup.DeclinationDeg = target;
            }
            else
            {
                // 每 140ms 走剩余距离的 ~18%，形成自然的 ease-out 动画
                _closeup.DeclinationDeg = current + diff * 0.18;
            }
            _lastDeclination = target;
        }
    }

    private string FormatDeclination(double d)
        => Math.Abs(d) < 0.05
            ? "0° " + _texts["lat.equator"]
            : $"{Math.Abs(d):0.0}°" + (d > 0 ? _texts["northShort"] : _texts["southShort"]);

    private void ApplyLesson(Lesson lesson)
    {
        if (!string.IsNullOrWhiteSpace(lesson.View))
        {
            var view = NormalizeView(lesson.View);
            if (!string.Equals(view, CurrentView, StringComparison.OrdinalIgnoreCase)) SwitchView(view);
        }
        // 讲要点时自动定格，读数不再跳动，方便指读板书
        SetPlaying(false);
        if (lesson.AngleDeg.HasValue) _canvas.JumpToOrbit(lesson.AngleDeg.Value);
        RefreshTeaching();
    }

    // ---------------------------------------------------------------- 教学操作
    private void SetPlaying(bool play)
    {
        _playing = play;
        _canvas.IsPlaying = play;
        if (_playButton?.Content is TextBlock block)
            block.Text = play ? _texts["pause"] : _texts["play"];
        RefreshTeaching();
    }

    private void TogglePlay() => SetPlaying(!_playing);

    private void Step(double deltaDeg)
    {
        SetPlaying(false);
        if (IsRevolution) _canvas.StepOrbit(deltaDeg);
        else _canvas.StepSpin(deltaDeg);
        RefreshTeaching();
    }

    private void JumpToSeason(SeasonPoint season)
    {
        if (!IsRevolution) SwitchView("revolution");
        SetPlaying(false);
        _canvas.JumpToOrbit(season.AngleDeg);
        RefreshTeaching();
    }

    private void JumpToSeasonIndex(int index)
    {
        if (index < 0 || index >= Seasons.Count) return;
        JumpToSeason(Seasons[index]);
    }

    private void ToggleFullScreen()
    {
        try
        {
            if (TopLevel.GetTopLevel(this) is Window window)
                window.WindowState = window.WindowState == WindowState.FullScreen
                    ? WindowState.Normal
                    : WindowState.FullScreen;
        }
        catch (Exception ex)
        {
            _context.Log("全屏切换失败: " + ex.Message);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space:
                TogglePlay();
                e.Handled = true;
                break;
            case Key.Left:
                Step(-5);
                e.Handled = true;
                break;
            case Key.Right:
                Step(5);
                e.Handled = true;
                break;
            case Key.D1: JumpToSeasonIndex(0); e.Handled = true; break;
            case Key.D2: JumpToSeasonIndex(1); e.Handled = true; break;
            case Key.D3: JumpToSeasonIndex(2); e.Handled = true; break;
            case Key.D4: JumpToSeasonIndex(3); e.Handled = true; break;
            case Key.F: ToggleFullScreen(); e.Handled = true; break;
            case Key.Add: _canvas.ZoomBy(1.25); e.Handled = true; break;
            case Key.Subtract: _canvas.ZoomBy(1 / 1.25); e.Handled = true; break;
            case Key.D0: _canvas.ResetView(); e.Handled = true; break;
        }
    }

    // ---------------------------------------------------------------- 工具
    private ModelButton MakeButton(string text, Action onClick, bool large = false)
    {
        var button = new ModelButton
        {
            Content = new TextBlock { Text = text, FontSize = large ? 20 : 12.5 },
            Foreground = TextBrush,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(9),
            Padding = large ? new Thickness(10, 8) : new Thickness(13, 9),
            MinWidth = large ? 46 : 44,
            MinHeight = 40,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        button.SetBase(ControlBg);
        button.Click += (_, _) => onClick();
        return button;
    }

    private void ApplyVisibility()
    {
        if (_viewSwitch != null) _viewSwitch.IsVisible = HasViewSwitch;
        if (_lessons != null) _lessons.IsVisible = _showLessons;
        if (_seasonPanel != null) _seasonPanel.IsVisible = IsRevolution;
        if (_closeupHost != null) _closeupHost.IsVisible = _showCloseup;
        if (_closeupLabel != null) _closeupLabel.IsVisible = _showCloseup;
    }

    /// <summary>带悬停过渡的按钮：本地基础色 + 悬停提亮。</summary>
    private sealed class ModelButton : Button
    {
        private IBrush _base = ControlBg;
        private IBrush _hover = ControlBg;

        public ModelButton()
        {
            Transitions = new Transitions
            {
                new BrushTransition { Property = BackgroundProperty, Duration = HoverDuration }
            };
        }

        public void SetBase(IBrush brush)
        {
            _base = brush;
            _hover = Lighten(brush);
            Background = brush;
        }

        protected override void OnPointerEntered(PointerEventArgs e)
        {
            base.OnPointerEntered(e);
            Background = _hover;
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            Background = _base;
        }

        private static IBrush Lighten(IBrush brush)
        {
            if (brush is not SolidColorBrush solid) return brush;
            var c = solid.Color;
            return new SolidColorBrush(Color.FromArgb(c.A,
                (byte)Math.Min(255, c.R + 26),
                (byte)Math.Min(255, c.G + 26),
                (byte)Math.Min(255, c.B + 30)));
        }
    }
}

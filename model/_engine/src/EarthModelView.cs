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
using EarthModelStudio.ModelSdk;

namespace NeoRF.EarthModels;

/// <summary>
/// 模型自己的界面：画布 + 工具栏 + 速度条 + 教学要点。
/// 带过渡动画、缩放控件与「暂停后可拖动」提示；启动器只负责开窗口。
/// </summary>
public sealed class EarthModelView : UserControl
{
    private static readonly IBrush PanelBg = new SolidColorBrush(Color.Parse("#C40A1020"));
    private static readonly IBrush ControlBg = new SolidColorBrush(Color.Parse("#16233C"));
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#2E6BE6"));
    private static readonly IBrush TitleBrush = new SolidColorBrush(Color.Parse("#F2F6FF"));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#DCE7FA"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#7E93B8"));
    private static readonly IBrush FaintBrush = new SolidColorBrush(Color.Parse("#61779C"));
    private static readonly IBrush BodyBrush = new SolidColorBrush(Color.Parse("#8CA0C4"));

    private static readonly TimeSpan HoverDuration = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan PanelDuration = TimeSpan.FromMilliseconds(190);

    private readonly IModelContext _context;
    private readonly ModelStrings _texts;
    private readonly SceneRuntime _runtime;
    private readonly List<string> _views;
    private readonly List<ModelButton> _viewButtons = new();
    private readonly CelestialView _canvas;

    private Border? _viewSwitch;
    private StackPanel? _seasonPanel;
    private Border? _lessons;
    private ModelButton? _playButton;
    private TextBlock? _zoomText;

    private bool _playing = true;
    private bool _labels = true;
    private bool _orbit = true;
    private bool _grid = true;
    private bool _tilt;
    private bool _showLessons = true;

    public EarthModelView(IModelContext context, SceneRuntime runtime)
    {
        _context = context;
        _runtime = runtime;
        _texts = ModelStrings.Load(context.DataDirectory, context.Language);
        _views = ResolveViews(runtime.Scene);

        Background = new SolidColorBrush(Color.Parse("#05070F"));

        _canvas = new CelestialView
        {
            Scene = runtime,
            Texts = _texts,
            ShowLabels = true,
            ShowOrbit = true,
            ShowGraticule = true,
            FrameRate = 60,
            View = IsRevolution ? SceneView.Revolution : SceneView.Rotation
        };
        _canvas.JumpToOrbit(StartAngle);
        _canvas.JumpToSpin(20);
        _canvas.ZoomChanged += zoom =>
        {
            if (_zoomText != null) _zoomText.Text = $"{zoom * 100:0}%";
        };

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };

        var top = BuildTopBar();
        root.Children.Add(top);

        var stage = new Grid();
        stage.Children.Add(_canvas);
        _lessons = BuildLessonsPanel();
        stage.Children.Add(_lessons);
        Grid.SetRow(stage, 1);
        root.Children.Add(stage);

        var bottom = BuildBottomBar();
        Grid.SetRow(bottom, 2);
        root.Children.Add(bottom);

        Content = root;
        ApplyVisibility();

        // 打开时整窗淡入，避免“啪”地出现
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
        AttachedToVisualTree += (_, _) => Opacity = 1;
    }

    private string CurrentView { get; set; } = "rotation";

    private double StartAngle => IsRevolution ? 60 : 0;

    private bool IsRevolution => string.Equals(CurrentView, "revolution", StringComparison.OrdinalIgnoreCase);

    private bool HasViewSwitch => _views.Count > 1;

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

    // ---------------------------------------------------------------- 顶部
    private Control BuildTopBar()
    {
        var close = MakeButton(_texts["close"], () => _context.RequestClose());

        var title = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock
        {
            Text = _texts.Pick(_runtime.Scene.NameI18n, _texts["title"]),
            Foreground = TitleBrush,
            FontSize = 13,
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
            Spacing = 7,
            VerticalAlignment = VerticalAlignment.Center
        };

        // 缩放：− / 百分比 / + / 复位
        actions.Children.Add(MakeButton("−", () => _canvas.ZoomBy(1 / 1.25), compact: true));
        _zoomText = new TextBlock
        {
            Text = "100%",
            Foreground = TextBrush,
            FontSize = 11,
            Width = 38,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        actions.Children.Add(_zoomText);
        actions.Children.Add(MakeButton("+", () => _canvas.ZoomBy(1.25), compact: true));
        actions.Children.Add(MakeButton("⟲", () =>
        {
            _canvas.ResetView();
            _canvas.JumpToOrbit(StartAngle);
            _canvas.JumpToSpin(20);
        }, compact: true));

        _playButton = MakeButton(_texts["pause"], TogglePlay);
        actions.Children.Add(_playButton);
        actions.Children.Add(MakeButton(_texts["reset"], () =>
        {
            _canvas.ResetAnimation();
            _canvas.JumpToOrbit(StartAngle);
        }));
        actions.Children.Add(MakeToggle(_texts["labels"], _labels, on => { _labels = on; _canvas.ShowLabels = on; }));
        actions.Children.Add(MakeToggle(_texts["orbit"], _orbit, on => { _orbit = on; _canvas.ShowOrbit = on; }));
        actions.Children.Add(MakeToggle(_texts["grid"], _grid, on => { _grid = on; _canvas.ShowGraticule = on; }));
        actions.Children.Add(MakeToggle(_texts["tilt"], _tilt, on => { _tilt = on; _canvas.ExaggerateTilt = on; }));
        actions.Children.Add(MakeButton(_texts["lessons"], () => SetLessonsVisible(!_showLessons)));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto") };
        grid.Children.Add(close);
        Grid.SetColumn(title, 1);
        title.Margin = new Thickness(14, 0, 0, 0);
        grid.Children.Add(title);
        Grid.SetColumn(_viewSwitch, 2);
        grid.Children.Add(_viewSwitch);
        Grid.SetColumn(actions, 4);
        grid.Children.Add(actions);

        return new Border { Background = PanelBg, Padding = new Thickness(16, 11), Child = grid };
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
        foreach (var button in _viewButtons)
            button.SetBase(string.Equals(button.Tag as string, CurrentView, StringComparison.OrdinalIgnoreCase)
                ? Accent
                : ControlBg);
        ApplyVisibility();
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
            Width = 180
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
            Margin = new Thickness(22, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        _seasonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        _seasonPanel.Children.Add(new TextBlock
        {
            Text = _texts["seasons"],
            Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });
        _seasonPanel.Children.Add(MakeSeasonButton(0));
        _seasonPanel.Children.Add(MakeSeasonButton(90));
        _seasonPanel.Children.Add(MakeSeasonButton(180));
        _seasonPanel.Children.Add(MakeSeasonButton(270));

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

        return new Border { Background = PanelBg, Padding = new Thickness(16, 10), Child = grid };
    }

    private ModelButton MakeSeasonButton(double angle)
    {
        var key = angle switch
        {
            0 => "season.springEquinox",
            90 => "season.summerSolstice",
            180 => "season.autumnEquinox",
            _ => "season.winterSolstice"
        };
        return MakeButton(_texts[key], () =>
        {
            if (!IsRevolution) SwitchView("revolution");
            _canvas.JumpToOrbit(angle);
        });
    }

    // ---------------------------------------------------------------- 教学要点
    private Border BuildLessonsPanel()
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock
        {
            Text = _texts["lessons"],
            Foreground = TitleBrush,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center
        });
        var close = MakeButton("✕", () => SetLessonsVisible(false));
        close.SetBase(Brushes.Transparent);
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

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
                Padding = new Thickness(8, 7),
                Margin = new Thickness(0, 1),
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
        content.Children.Add(header);
        content.Children.Add(new ScrollViewer
        {
            Content = list,
            MaxHeight = 380,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });

        var panel = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 272,
            Margin = new Thickness(0, 58, 16, 80),
            Background = new SolidColorBrush(Color.Parse("#D00B1226")),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(13),
            BorderBrush = new SolidColorBrush(Color.Parse("#26374F")),
            BorderThickness = new Thickness(1),
            Child = content,
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

    private void ApplyLesson(Lesson lesson)
    {
        if (!string.IsNullOrWhiteSpace(lesson.View))
        {
            var view = NormalizeView(lesson.View);
            if (!string.Equals(view, CurrentView, StringComparison.OrdinalIgnoreCase)) SwitchView(view);
        }
        if (lesson.AngleDeg.HasValue) _canvas.JumpToOrbit(lesson.AngleDeg.Value);
    }

    // ---------------------------------------------------------------- 工具
    private ModelButton MakeButton(string text, Action onClick, bool compact = false)
    {
        var button = new ModelButton
        {
            Content = new TextBlock { Text = text, FontSize = 11.5 },
            Foreground = TextBrush,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Padding = compact ? new Thickness(8, 4) : new Thickness(9, 5),
            MinWidth = compact ? 26 : 0,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        button.SetBase(ControlBg);
        button.Click += (_, _) => onClick();
        return button;
    }

    private void TogglePlay()
    {
        _playing = !_playing;
        _canvas.IsPlaying = _playing;
        if (_playButton?.Content is TextBlock block)
            block.Text = _playing ? _texts["pause"] : _texts["play"];
    }

    private void ApplyVisibility()
    {
        if (_viewSwitch != null) _viewSwitch.IsVisible = HasViewSwitch;
        if (_lessons != null) _lessons.IsVisible = _showLessons;
        if (_seasonPanel != null) _seasonPanel.IsVisible = IsRevolution;
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

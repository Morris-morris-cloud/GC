using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Shapes = Avalonia.Controls.Shapes;
using Avalonia.VisualTree;
using EarthModelStudio.Models;
using EarthModelStudio.Services;

namespace EarthModelStudio.Views;

/// <summary>
/// 模型窗口：**只做窗口宿主**。
/// 内容完全来自模型程序集（<see cref="LoadedModel.View"/>），启动器不渲染任何东西。
/// 快捷键：Esc 关闭窗口，F11 全屏。
/// </summary>
public sealed class PlayerWindow : Window
{
    private LoadedModel? _model;
    private bool _closing;
    private Button? _maximizeButton;
    private Border? _frame;
    private TextBlock? _titleText;
    private Border? _titleBar;
    private readonly List<Button> _titleButtons = new();
    private readonly List<Shapes.Path> _titleGlyphs = new();
    private Shapes.Path? _maximizeGlyph;
    private Shapes.Path? _restoreGlyph;
    private string _maxMaxPath = "";
    private string _maxRestorePath = "";
    private Button? _helpButton;
    /// <summary>基准尺寸（1240×820 时的标题栏高度与按钮尺寸）。</summary>
    private const double BaseWidth = 1240;
    private const double BaseTitleBarHeight = 34;
    private const double BaseButtonWidth = 38;
    private const double BaseButtonHeight = 28;
    private const double BaseTitleFont = 11.5;
    private const double BaseHelpFont = 11.5;

    public PlayerWindow(ModelPackage package, LoadedModel model)
    {
        _model = model;
        Title = package.LocalizedName;
        Width = 1240;
        Height = 820;
        MinWidth = 1000;
        MinHeight = 680;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        // 与启动器一致：去掉系统标题栏，自己做一个极简顶栏（否则 Esc 之外无法关闭）
        SystemDecorations = Avalonia.Controls.SystemDecorations.None;

        // 标题栏配色来自模型清单，跟随模型自己的界面风格：
        // 地球模型是深色舞台，比例尺计算器是浅色卡片，两者不能共用一套深色。
        var chrome = package.Manifest.Chrome;
        var barBg = ParseColor(chrome?.TitleBar, "#0C1828");
        var barBorder = ParseColor(chrome?.TitleBorder, "#1B2740");
        var barText = ParseColor(chrome?.TitleText, "#EAF2FF");
        var btnText = ParseColor(chrome?.Button, "#9FB4D4");
        var frameBg = ParseColor(chrome?.Frame, "#0A1524");

        var titleText = new TextBlock
        {
            Text = package.LocalizedName,
            Foreground = new SolidColorBrush(barText),
            FontSize = 11.5,
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            [Grid.ColumnProperty] = 0
        };
        _titleText = titleText;

        var helpButton = new Button
        {
            Content = IsEnglish() ? "How to use?" : "如何使用?",
            Height = 26,
            Padding = new Thickness(12, 0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(btnText),
            BorderThickness = new Thickness(0),
            FontSize = 11.5,
            CornerRadius = new CornerRadius(7),
            Margin = new Thickness(0, 0, 6, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            [Grid.ColumnProperty] = 1
        };
        helpButton.Click += (_, _) => ShowHelp(package, barBg, barBorder, barText, btnText, frameBg);

        // 标题栏三键：最小化 / 最大化 / 关闭 —— 卡通画风。
        // 每颗按钮 = 彩色实心圆底 + 粗圆头描边图标，像一颗小糖豆。
        (Button Btn, Shapes.Path Icon) MakeBadge(int column, string iconPath)
        {
            // 纯矢量图标，没有彩色圆圈
            var icon = new Shapes.Path
            {
                Data = Geometry.Parse(iconPath),
                Stroke = new SolidColorBrush(btnText),
                StrokeThickness = 1.5,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
                Fill = null,
                Stretch = Stretch.None,
                Width = 10,
                Height = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var layer = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            layer.Children.Add(icon);

            var b = new Button
            {
                Width = 38,
                Height = 28,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(0),
                Margin = new Thickness(2, 0, 0, 0),
                Content = layer,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand),
                [Grid.ColumnProperty] = column
            };
            var hoverBg = IsLight(barBg)
                ? new SolidColorBrush(Color.Parse("#EEF1F7"))
                : new SolidColorBrush(Color.Parse("#1E2C46"));
            var isCloseBtn = column == 4;
            b.PointerEntered += (_, _) =>
            {
                b.Background = hoverBg;
                icon.Stroke = new SolidColorBrush(isCloseBtn ? Colors.White
                    : IsLight(barBg) ? Color.Parse("#111A2E") : Colors.White);
            };
            b.PointerExited += (_, _) =>
            {
                b.Background = Brushes.Transparent;
                icon.Stroke = new SolidColorBrush(btnText);
            };
            b.Classes.Add("model-titlebtn");
            _titleButtons.Add(b);
            _titleGlyphs.Add(icon);
            return (b, icon);
        }

        // 卡通配色：暖黄 / 薄荷绿 / 珊瑚红
        var minBadge = MakeBadge(2, "M1.5,5 L8.5,5");
        var maxBadge = MakeBadge(3, "M2.2,2.2 L7.8,2.2 L7.8,7.8 L2.2,7.8 Z");
        var closeBadge = MakeBadge(4, "M2,2 L8,8 M8,2 L2,8");

        var minimizeButton = minBadge.Btn;
        var maximizeButton = maxBadge.Btn;
        var closeButton = closeBadge.Btn;

        minimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        maximizeButton.Click += (_, _) => ToggleMaximize();
        closeButton.Click += (_, _) => _ = CloseWithAnimationAsync();
        _maximizeGlyph = maxBadge.Icon;
        _restoreGlyph = maxBadge.Icon;
        _maxRestorePath = "M2.6,3.6 L7.4,3.6 L7.4,8.4 L2.6,8.4 Z M2.6,3.6 L2.6,1.6 L7.4,1.6 L7.4,6.4";
        _maxMaxPath = "M2.2,2.2 L7.8,2.2 L7.8,7.8 L2.2,7.8 Z";

        var titleBar = new Border
        {
            Height = 34,
            Background = new SolidColorBrush(barBg),
            BorderBrush = new SolidColorBrush(barBorder),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto,Auto"),
                Margin = new Thickness(0, 0, 10, 0),
                Children = { titleText, helpButton, minimizeButton, maximizeButton, closeButton }
            }
        };
        titleBar.PointerPressed += OnTitleBarPointerPressed;
        // 双击标题栏 = 最大化 / 还原（与系统标题栏一致）
        titleBar.DoubleTapped += (_, e) =>
        {
            if (e.Source is Button) return;
            ToggleMaximize();
        };
        _maximizeButton = maximizeButton;
        _titleBar = titleBar;
        _helpButton = helpButton;
        _titleButtons.Add(minimizeButton);
        _titleButtons.Add(maximizeButton);
        _titleButtons.Add(closeButton);

        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(titleBar, Dock.Top);
        dock.Children.Add(titleBar);
        dock.Children.Add(model.View);

        Content = new Border
        {
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(frameBg),
            ClipToBounds = true,
            Child = dock
        };
        _frame = (Border)Content;

        // 最大化 / 还原时同步圆角与按钮图标
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty) UpdateMaximizeVisual();
        };

        // 窗口随心拉大 / 拉小时，标题栏文字与按钮同比缩放。
        // 同时监听 Bounds 属性（不只是 SizeChanged），保证最大化/还原也立刻生效。
        SizeChanged += (_, _) => ApplyChromeScale();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty) ApplyChromeScale();
        };
        ApplyChromeScale();

        KeyDown += OnKeyDown;
        Closed += OnClosed;
    }

    /// <summary>
    /// 让标题栏的文字与按钮尺寸跟随窗口大小。
    /// 以 1240px 宽为基准线性缩放，限制在 0.85–1.9 倍，
    /// 避免最大化到 4K 时按钮大得离谱、缩到最小又点不中。
    /// </summary>
    private void ApplyChromeScale()
    {
        if (Bounds.Width < 100) return;
        var scale = Math.Clamp(Bounds.Width / BaseWidth, 0.85, 1.9);

        if (_titleBar != null) _titleBar.Height = Math.Round(BaseTitleBarHeight * scale);
        foreach (var b in _titleButtons)
        {
            b.Width = Math.Round(BaseButtonWidth * scale);
            b.Height = Math.Round(BaseButtonHeight * scale);
            b.FontSize = Math.Round(13 * scale, 1);
            b.CornerRadius = new CornerRadius(Math.Round(7 * scale, 1));
        }
        if (_helpButton != null)
        {
            _helpButton.Height = Math.Round(26 * scale);
            _helpButton.FontSize = Math.Round(BaseHelpFont * scale, 1);
            _helpButton.Padding = new Thickness(Math.Round(12 * scale), 0);
            _helpButton.CornerRadius = new CornerRadius(Math.Round(7 * scale, 1));
        }
        foreach (var g in _titleGlyphs)
        {
            g.Width = Math.Round(10 * scale, 1);
            g.Height = Math.Round(10 * scale, 1);
            g.StrokeThickness = Math.Round(1.5 * scale, 2);
        }
        if (_titleText != null)
        {
            _titleText.FontSize = Math.Round(BaseTitleFont * scale, 1);
            _titleText.Margin = new Thickness(Math.Round(14 * scale), 0, 0, 0);
        }
    }

    /// <summary>判断颜色是否属于浅色系（用于决定悬停用深色还是浅色）。</summary>
    private static bool IsLight(Color c)
    {
        // 相对亮度（sRGB 近似）
        var lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        return lum > 0.6;
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void UpdateMaximizeVisual()
    {
        var max = WindowState == WindowState.Maximized || WindowState == WindowState.FullScreen;
        if (_maximizeGlyph != null)
        {
            // 只换 Data，不换控件对象（换对象会触发 "already has a parent"）
            _maximizeGlyph.Data = Geometry.Parse(max ? _maxRestorePath : _maxMaxPath);
        }
        if (_maximizeButton != null)
        {
            Avalonia.Controls.ToolTip.SetTip(_maximizeButton, max
                ? (IsEnglish() ? "Restore" : "还原")
                : (IsEnglish() ? "Maximize" : "最大化"));
        }
        // 最大化后同样保留圆角（用户要求：任何状态都要圆角）
        if (_frame != null) _frame.CornerRadius = new CornerRadius(16);
    }

    private static Color ParseColor(string? hex, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            try { return Color.Parse(hex!); }
            catch { /* 模型写错颜色时退回默认 */ }
        }
        return Color.Parse(fallback);
    }

    private static bool IsEnglish() => L.I.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 「如何使用?」：读取模型自带数据目录里的 help.json（中英双语），
    /// 用与标题栏一致的配色弹出一个独立小窗。说明内容完全由模型提供，启动器不内置文案。
    /// </summary>
    private void ShowHelp(ModelPackage package, Color barBg, Color barBorder, Color barText,
        Color btnText, Color frameBg)
    {
        var lang = IsEnglish() ? "en" : "zh-CN";
        var file = Path.Combine(package.DataDirectory, "help.json");
        string title = IsEnglish() ? "How to use" : "如何使用";
        // 分节内容：每节一个标题 + 若干条说明；兼容旧的扁平 lines 格式
        var sections = new List<(string Heading, List<string> Lines)>();

        if (File.Exists(file))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                var node = root.TryGetProperty(lang, out var exact) ? exact
                    : root.TryGetProperty("zh-CN", out var zh) ? zh : root;
                if (node.TryGetProperty("title", out var t)) title = t.GetString() ?? title;

                if (node.TryGetProperty("sections", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var sec in arr.EnumerateArray())
                    {
                        var heading = sec.TryGetProperty("h", out var h) ? h.GetString() ?? "" : "";
                        var items = new List<string>();
                        if (sec.TryGetProperty("lines", out var ls) && ls.ValueKind == JsonValueKind.Array)
                            foreach (var item in ls.EnumerateArray())
                                if (item.ValueKind == JsonValueKind.String) items.Add(item.GetString() ?? "");
                        if (items.Count > 0) sections.Add((heading, items));
                    }
                }
                else if (node.TryGetProperty("lines", out var flat) && flat.ValueKind == JsonValueKind.Array)
                {
                    var items = new List<string>();
                    foreach (var item in flat.EnumerateArray())
                        if (item.ValueKind == JsonValueKind.String) items.Add(item.GetString() ?? "");
                    if (items.Count > 0) sections.Add(("", items));
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"读取模型帮助失败 {file}: {ex.Message}");
            }
        }
        if (sections.Count == 0)
            sections.Add(("", new List<string>
            {
                IsEnglish() ? "This model has no usage notes yet." : "该模型暂未提供使用说明。"
            }));

        var body = new StackPanel { Spacing = 6 };
        foreach (var (heading, items) in sections)
        {
            if (!string.IsNullOrWhiteSpace(heading))
                body.Children.Add(new TextBlock
                {
                    Text = heading,
                    Foreground = new SolidColorBrush(barText),
                    FontSize = 13.5,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 12, 0, 2)
                });
            foreach (var line in items)
                body.Children.Add(new TextBlock
                {
                    Text = "•  " + line,
                    Foreground = new SolidColorBrush(btnText),
                    FontSize = 12.5,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 19
                });
        }

        // ── 帮助窗口自建标题栏（带 ✕），与模型窗口同款配色 ──
        // 纯矢量图标，无彩色圆底（与三个窗口按钮保持一致）
        var helpCross = new Shapes.Path
        {
            Data = Geometry.Parse("M2,2 L8,8 M8,2 L2,8"),
            Stroke = new SolidColorBrush(btnText),
            StrokeThickness = 1.5,
            StrokeLineCap = PenLineCap.Round,
            Fill = null,
            Stretch = Stretch.None,
            Width = 10, Height = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var helpClose = new Button
        {
            Content = helpCross,
            Width = 34,
            Height = 26,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(0),
            Margin = new Thickness(6, 0, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        helpClose.PointerEntered += (_, _) =>
        {
            helpClose.Background = new SolidColorBrush(IsLight(frameBg)
                ? Color.Parse("#EEF1F7") : Color.Parse("#1E2C46"));
            helpCross.Stroke = new SolidColorBrush(Colors.White);
        };
        helpClose.PointerExited += (_, _) =>
        {
            helpClose.Background = Brushes.Transparent;
            helpCross.Stroke = new SolidColorBrush(btnText);
        };

        var helpTitle = new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush(barText),
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        var helpBarGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(22, 0, 14, 0)
        };
        helpBarGrid.Children.Add(helpTitle);
        Grid.SetColumn(helpClose, 1);
        helpBarGrid.Children.Add(helpClose);

        var helpTitleBar = new Border
        {
            Height = 48,
            Background = new SolidColorBrush(barBg),
            BorderBrush = new SolidColorBrush(barBorder),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = helpBarGrid
        };
        helpTitleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(helpTitleBar).Properties.IsLeftButtonPressed)
                helpClose.FindAncestorOfType<Window>()?.BeginMoveDrag(e);
        };

        // ── 底部固定操作条：无论正文多长，「知道了」始终可见 ──
        var ok = new Button
        {
            Content = IsEnglish() ? "Got it" : "知道了",
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(20, 8),
            Background = new SolidColorBrush(Color.Parse("#2F6BFF")),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        var footer = new Border
        {
            Padding = new Thickness(24, 10, 24, 14),
            BorderBrush = new SolidColorBrush(barBorder),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = ok
        };

        // 标题栏（顶） + 正文（中，滚动） + 操作条（底，固定）
        var rootPanel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(helpTitleBar, Dock.Top);
        rootPanel.Children.Add(helpTitleBar);
        DockPanel.SetDock(footer, Dock.Bottom);
        rootPanel.Children.Add(footer);
        var scroll = new ScrollViewer
        {
            Content = new Border
            {
                Margin = new Thickness(24, 14, 24, 10),
                Child = body
            },
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        rootPanel.Children.Add(scroll);

        var win = new Window
        {
            Title = title,
            Width = 660,
            Height = Math.Min(760, 230 + sections.Sum(s => s.Lines.Count * 24 + 40)),
            MinWidth = 480,
            MinHeight = 320,
            Background = Brushes.Transparent,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            SystemDecorations = SystemDecorations.None,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Border
            {
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(frameBg),
                BorderBrush = new SolidColorBrush(barBorder),
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
                Child = rootPanel
            }
        };
        ok.Click += (_, _) => win.Close();
        helpClose.Click += (_, _) => win.Close();
        win.Show(this);
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                _ = CloseWithAnimationAsync();
                e.Handled = true;
                break;
            case Key.F11:
                WindowState = WindowState == WindowState.FullScreen
                    ? WindowState.Normal
                    : WindowState.FullScreen;
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// 关闭动画：淡出 + 轻微缩小，动画结束后再真正关闭窗口。
    /// 与主窗口的关闭动画保持同一节奏（180ms）。
    /// </summary>
    private async Task CloseWithAnimationAsync()
    {
        if (_closing) return;
        _closing = true;

        Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(180),
                Easing = new CubicEaseIn()
            }
        };
        Opacity = 0;

        await Task.Delay(190);
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Content = null;
        _model?.Dispose();
        _model = null;
    }
}

/// <summary>模型加载失败时显示的提示窗口（不含任何渲染逻辑）。</summary>
public sealed class ModelErrorWindow : Window
{
    public ModelErrorWindow(string title, string message)
    {
        Title = title;
        Width = 560;
        Height = 340;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var lines = new List<Control>
        {
            new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Color.Parse("#F2F6FF")),
                FontSize = 16,
                FontWeight = FontWeight.SemiBold
            },
            new TextBlock
            {
                Text = message,
                Foreground = new SolidColorBrush(Color.Parse("#8CA0C4")),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            },
            new Button
            {
                Content = "确定",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Padding = new Thickness(18, 6)
            }
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 14
        };
        foreach (var line in lines) panel.Children.Add(line);
        if (lines[^1] is Button ok) ok.Click += (_, _) => Close();
        Content = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.Parse("#0B1226")),
            Child = panel
        };
    }
}

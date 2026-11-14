using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace NeoRF.ScaleCalculator;

/// <summary>
/// 比例尺计算器主界面。三栏（已知量 / 可视化标尺 / 三种写法与判读）+ 底部实地尺与练习。
/// 全部用显式画刷（与启动器 AppTheme 同色），不依赖 DynamicResource —— 模型 dll 在
/// 独立 ALC 里加载，headless 生成封面时也没有 Application 样式。
/// </summary>
public sealed partial class ScaleCalculatorView : UserControl
{
    // 调色（与 GC-BOOT/Styles/AppTheme.axaml 一致）
    internal static readonly Color CAppBg = Color.Parse("#F4F6FB");
    internal static readonly Color CSurface = Color.Parse("#FFFFFF");
    internal static readonly Color CSurfaceAlt = Color.Parse("#F7F9FE");
    internal static readonly Color CLine = Color.Parse("#E3E8F3");
    internal static readonly Color CLineStrong = Color.Parse("#D2DAEA");
    internal static readonly Color CInk = Color.Parse("#111A2E");
    internal static readonly Color CInkDim = Color.Parse("#5B6A86");
    internal static readonly Color CInkFaint = Color.Parse("#8A97B1");
    internal static readonly Color CAccent = Color.Parse("#2F6BFF");
    internal static readonly Color CAccentDark = Color.Parse("#1F4FD0");
    internal static readonly Color CAccentSoft = Color.Parse("#EAF1FF");
    internal static readonly Color CSuccess = Color.Parse("#1F9D6B");
    internal static readonly Color CDanger = Color.Parse("#D64545");
    internal static readonly Color CWarn = Color.Parse("#D98613");
    internal static readonly Color CHero = Color.Parse("#12203C");

    internal static SolidColorBrush B(Color c) => new(c);
    internal static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));

    private readonly ScaleData _data;
    private readonly Dictionary<string, string> _texts;
    private readonly string _language;
    private readonly Random _rnd = new();

    public string T(string key)
        => _texts.TryGetValue(key, out var v) ? v
            : DefaultTexts.Zh.TryGetValue(key, out var f) ? f : key;

    public string TF(string key, params object[] args)
        => string.Format(CultureInfo.InvariantCulture, T(key), args);

    public ScaleCalculatorView(ScaleData data, Dictionary<string, string> texts, string language)
    {
        _data = data;
        _texts = texts;
        _language = language;

        BuildLayout();
        WireEvents();
        SetMode(SolveMode.Real, initial: true);
        NewQuiz();
        RefreshAll(syncInputs: true);
    }

    // ────────────────────────────────────────────────────────── 小工具

    internal static Border Card(Control content, double radius = 14)
        => new()
        {
            Background = B(CSurface),
            BorderBrush = B(CLine),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(radius),
            Padding = new Thickness(14),
            Child = content
        };

    internal static TextBlock Tb(string text, double size, Color color, FontWeight? weight = null,
        TextAlignment align = TextAlignment.Left)
        => new()
        {
            Text = text,
            FontSize = size,
            Foreground = B(color),
            FontWeight = weight ?? FontWeight.Normal,
            TextAlignment = align,
            TextWrapping = TextWrapping.Wrap
        };

    internal static TextBlock Label(string text, double size = 12, Color? color = null)
        => Tb(text, size, color ?? CInkDim);

    internal static TextBlock H3(string text) => Tb(text, 13.5, CInk, FontWeight.SemiBold);

    internal static Border Chip(string text, Color bg, Color fg, double radius = 8)
        => new()
        {
            Background = B(bg),
            CornerRadius = new CornerRadius(radius),
            Padding = new Thickness(10, 4),
            Child = Tb(text, 11, fg, FontWeight.SemiBold)
        };

    internal static Button Btn(string text, bool primary = false)
        => new()
        {
            Content = Tb(text, 12.5, primary ? Colors.White : CInk, FontWeight.SemiBold),
            Background = B(primary ? CAccent : CSurface),
            BorderBrush = B(primary ? CAccent : CLineStrong),
            BorderThickness = new Thickness(primary ? 0 : 1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 7),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };

    internal static TextBox Input(string text = "")
        => new()
        {
            Text = text,
            FontSize = 12.5,
            Background = B(CSurface),
            Foreground = B(CInk),
            BorderBrush = B(CLineStrong),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10, 7),
            VerticalContentAlignment = VerticalAlignment.Center,
            MinHeight = 34
        };

    internal static Border Divider(double h = 1)
        => new() { Background = B(CLine), Height = h, Margin = new Thickness(0, 10, 0, 10) };

    internal static StackPanel V(double spacing = 8)
        => new() { Orientation = Orientation.Vertical, Spacing = spacing };

    internal static StackPanel H(double spacing = 8)
        => new() { Orientation = Orientation.Horizontal, Spacing = spacing, VerticalAlignment = VerticalAlignment.Center };
}

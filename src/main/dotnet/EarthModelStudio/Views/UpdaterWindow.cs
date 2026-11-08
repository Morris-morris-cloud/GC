using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using EarthModelStudio.Services;

namespace EarthModelStudio.Views;

/// <summary>
/// 启动器更新进度窗：更新期间主窗口已关闭，由这个独立小窗展示下载进度。
/// 窗口本身透明，圆角由内部 Border 裁出。
/// </summary>
public sealed class UpdaterWindow : Window
{
    private readonly ProgressBar _bar;
    private readonly TextBlock _stage;
    private readonly TextBlock _detail;
    private readonly TextBlock _percent;

    public UpdaterWindow(string title)
    {
        Title = title;
        Width = 460;
        Height = 210;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        // 必须透明，否则会把内部 Border 的圆角填平
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        SystemDecorations = SystemDecorations.None;

        var titleBlock = new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush(Color.Parse("#F2F6FF")),
            FontSize = 15,
            FontWeight = FontWeight.SemiBold
        };

        _stage = new TextBlock
        {
            Text = "正在准备…",
            Foreground = new SolidColorBrush(Color.Parse("#8CA0C4")),
            FontSize = 12
        };

        _bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 1,
            Value = 0,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Foreground = new SolidColorBrush(Color.Parse("#3B7BFF")),
            Background = new SolidColorBrush(Color.Parse("#1B2740"))
        };

        _percent = new TextBlock
        {
            Text = "0%",
            Foreground = new SolidColorBrush(Color.Parse("#6E86AD")),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        _detail = new TextBlock
        {
            Text = "",
            Foreground = new SolidColorBrush(Color.Parse("#4F6382")),
            FontSize = 10.5
        };

        var panel = new StackPanel { Margin = new Thickness(24, 22), Spacing = 12 };
        panel.Children.Add(titleBlock);
        panel.Children.Add(_bar);
        panel.Children.Add(_stage);
        panel.Children.Add(_percent);
        panel.Children.Add(_detail);

        Content = new Border
        {
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.Parse("#0B1226")),
            BorderBrush = new SolidColorBrush(Color.Parse("#223252")),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = panel
        };
    }

    /// <summary>任意线程调用都可以。</summary>
    public void Report(double value, string? stage, string? detail = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Report(value, stage, detail));
            return;
        }

        var clamped = Math.Clamp(value, 0, 1);
        _bar.Value = clamped;
        _percent.Text = $"{clamped * 100:0}%";
        if (!string.IsNullOrWhiteSpace(stage)) _stage.Text = stage;
        if (!string.IsNullOrWhiteSpace(detail)) _detail.Text = detail;
    }
}

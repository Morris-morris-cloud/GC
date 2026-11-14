using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace GC.Views;

/// <summary>
/// 模型启动进度窗：加载模型程序集、生成地表纹理、构建界面的真实进度。
/// 只显示进度，不做任何渲染。
/// </summary>
public sealed class LoadingWindow : Window
{
    private readonly ProgressBar _bar;
    private readonly TextBlock _stage;
    private readonly TextBlock _percent;

    public LoadingWindow(string modelName)
    {
        Title = modelName;
        Width = 460;
        Height = 172;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#0B1226"));
        SystemDecorations = SystemDecorations.BorderOnly;

        var title = new TextBlock
        {
            Text = modelName,
            Foreground = new SolidColorBrush(Color.Parse("#F2F6FF")),
            FontSize = 15,
            FontWeight = FontWeight.SemiBold
        };

        _stage = new TextBlock
        {
            Text = "正在准备模型…",
            Foreground = new SolidColorBrush(Color.Parse("#8CA0C4")),
            FontSize = 12
        };

        _bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 1,
            Value = 0,
            Height = 6,
            IsIndeterminate = true,
            CornerRadius = new CornerRadius(3),
            Foreground = new SolidColorBrush(Color.Parse("#3B7BFF")),
            Background = new SolidColorBrush(Color.Parse("#1B2740")),
            Transitions = new Transitions
            {
                // 进度平滑增长，不会一跳一跳
                new DoubleTransition
                {
                    Property = RangeBase.ValueProperty,
                    Duration = TimeSpan.FromMilliseconds(240),
                    Easing = new CubicEaseOut()
                }
            }
        };

        _percent = new TextBlock
        {
            Text = "",
            Foreground = new SolidColorBrush(Color.Parse("#6E86AD")),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(22, 20),
            Spacing = 12
        };
        panel.Children.Add(title);
        panel.Children.Add(_bar);
        panel.Children.Add(_stage);
        panel.Children.Add(_percent);

        Content = panel;
    }

    /// <summary>任意线程调用都可以。</summary>
    public void Report(double value, string? message)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Report(value, message));
            return;
        }

        var clamped = Math.Clamp(value, 0, 1);
        _bar.IsIndeterminate = false;
        _bar.Value = clamped;
        _percent.Text = $"{clamped * 100:0}%";
        if (!string.IsNullOrWhiteSpace(message)) _stage.Text = message;
    }
}

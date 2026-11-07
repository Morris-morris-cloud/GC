using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System.Collections.Generic;
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

    public PlayerWindow(ModelPackage package, LoadedModel model)
    {
        _model = model;
        Title = package.LocalizedName;
        Width = 1240;
        Height = 820;
        MinWidth = 1000;
        MinHeight = 680;
        Background = new SolidColorBrush(Color.Parse("#05070F"));
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        // 与启动器一致：去掉系统标题栏，自己做一个极简顶栏（否则 Esc 之外无法关闭）
        SystemDecorations = Avalonia.Controls.SystemDecorations.None;

        var titleBar = new Border
        {
            Height = 34,
            Background = new SolidColorBrush(Color.Parse("#0A1120")),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    new TextBlock
                    {
                        Text = package.LocalizedName,
                        Foreground = new SolidColorBrush(Color.Parse("#A9B8D4")),
                        FontSize = 11.5,
                        Margin = new Thickness(14, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        [Grid.ColumnProperty] = 0
                    },
                    new Button
                    {
                        Content = "✕",
                        Width = 40,
                        Height = 28,
                        Background = Brushes.Transparent,
                        Foreground = new SolidColorBrush(Color.Parse("#A9B8D4")),
                        BorderThickness = new Thickness(0),
                        FontSize = 13,
                        CornerRadius = new CornerRadius(7),
                        Margin = new Thickness(0, 0, 8, 0),
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        [Grid.ColumnProperty] = 1
                    }
                }
            }
        };
        titleBar.PointerPressed += OnTitleBarPointerPressed;
        if (titleBar.Child is Grid g)
            foreach (var child in g.Children)
                if (child is Button b) b.Click += (_, _) => Close();

        Content = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(titleBar, Dock.Top);
        ((DockPanel)Content).Children.Add(titleBar);
        ((DockPanel)Content).Children.Add(model.View);

        KeyDown += OnKeyDown;
        Closed += OnClosed;
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
                Close();
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
        Background = new SolidColorBrush(Color.Parse("#0B1226"));
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
        Content = panel;
    }
}

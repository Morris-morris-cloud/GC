using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Threading;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using EarthModelStudio.Models;
using EarthModelStudio.Services;
using EarthModelStudio.ViewModels;

namespace EarthModelStudio.Views;

public partial class MainWindow : Window
{
    private readonly List<PlayerWindow> _players = new();
    private MainViewModel? _viewModel;
    private ContentControl? _pageHost;
    private bool _isClosing;

    public MainWindow()
    {
        InitializeComponent();
        _pageHost = this.FindControl<ContentControl>("PageHost");
        if (_pageHost != null)
        {
            // 页面切换：淡入过渡，避免瞬间硬切
            _pageHost.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = Visual.OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(200),
                    Easing = new CubicEaseOut()
                }
            };
        }

        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;

        // 最大化 / 还原：切换图标（单框 ⇄ 双框），圆角始终保持
        PropertyChanged += (_, e) =>
        {
            if (e.Property != WindowStateProperty) return;
            var max = WindowState == WindowState.Maximized;
            if (this.FindControl<Avalonia.Controls.Shapes.Path>("MaximizeGlyph") is { } glyph)
                glyph.Data = Avalonia.Media.Geometry.Parse(max
                    ? "M2.6,3.4 L7.4,3.4 L7.4,8.4 L2.6,8.4 Z M2.6,3.4 L2.6,1.6 L7.4,1.6 L7.4,6.6"
                    : "M2.2,2.2 L7.8,2.2 L7.8,7.8 L2.2,7.8 Z");
            if (Content is Border frame) frame.CornerRadius = new Avalonia.CornerRadius(16);
        };
    }

    /// <summary>当前打开的模型窗口（每个模型一个独立窗口）。</summary>
    public IReadOnlyList<PlayerWindow> PlayerWindows => _players;

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.PlayerRequested -= OpenPlayerWindow;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
        _viewModel = DataContext as MainViewModel;
        if (_viewModel != null)
        {
            _viewModel.PlayerRequested += OpenPlayerWindow;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    /// <summary>切换页面时让内容淡入，避免「一卡一卡」的硬切。</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.Current) || _pageHost == null) return;
        _pageHost.Opacity = 0.55;
        // 下一帧回到 1，OpacityTransition 会自动插值
        Dispatcher.UIThread.Post(() =>
        {
            if (_pageHost != null) _pageHost.Opacity = 1;
        }, DispatcherPriority.Render);
    }

    // 事件处理器必须是 void，因此包一层可观察异常的入口：
    // fire-and-forget 的 Task 也要观察异常，绝不外泄到 Avalonia 事件循环导致进程崩溃。
    private void OpenPlayerWindow(ModelPackage package) => _ = OpenPlayerWindowSafeAsync(package);

    private async Task OpenPlayerWindowSafeAsync(ModelPackage package)
    {
        try { await OpenPlayerWindowAsync(package); }
        catch (Exception ex) { Console.Error.WriteLine($"打开模型窗口失败: {ex}"); }
    }

    private async Task OpenPlayerWindowAsync(ModelPackage package)
    {
        if (_viewModel == null || _isClosing) return;

        // 启动进度窗：模型加载、地表纹理生成、界面构建的真实进度
        var loading = new LoadingWindow(package.LocalizedName);
        loading.Show(this);
        loading.Report(0, "正在加载模型…");
        // 让进度窗先画出来（用调度器让出一帧，测试环境也能推进）
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

        PlayerWindow? window = null;
        LoadedModel? loaded = null;
        Exception? error = null;

        // 关键修复：requestClose 过去捕获的是「尚未赋值的 window」，加载期恒为 null，
        // 模型在 Prepare/CreateView 里调用 context.RequestClose() 会静默失效。
        // 改为关闭当前存在的窗口（加载期=进度窗，加载完成=模型窗），统一走 UI 线程。
        void RequestClose() => Dispatcher.UIThread.Post(() =>
        {
            (window ?? (Window?)loading)?.Close();
        });

        try
        {
            loaded = await _viewModel.Models.LoadAsync(package, RequestClose,
                (value, message) => loading.Report(value, message));
        }
        catch (Exception ex)
        {
            error = ex;
        }

        loading.Close();

        // 加载期间用户关掉了主窗口：不要再弹出幽灵模型窗口，直接释放已加载模型
        if (_isClosing)
        {
            loaded?.Dispose();
            return;
        }

        if (loaded == null || error != null)
        {
            var message = error?.Message ?? L.I["player.errorHint"];
            var dialog = new ModelErrorWindow(
                L.I.F("player.errorTitle", package.LocalizedName),
                $"{message}\n\n{L.I["player.errorHint"]}");
            dialog.Show(this);
            return;
        }

        window = new PlayerWindow(package, loaded);
        window.Closed += (_, _) => _players.Remove(window);
        _players.Add(window);
        window.Show(this);
        window.Activate();
    }

    // ---- 自建标题栏 ------------------------------------------------------
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 双击标题栏 = 最大化 / 还原
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnMinimizeClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    // 关闭动画：先淡出再真正 Close（第 4 项）
    private async void OnCloseClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isClosing) return;
        Opacity = 1;
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
        _isClosing = true;
        if (_viewModel != null)
        {
            _viewModel.PlayerRequested -= OpenPlayerWindow;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
        // 先取快照再关闭：PlayerWindow.Closed 回调会 _players.Remove，
        // 直接遍历原集合会在 foreach 期间修改底层导致异常。
        foreach (var window in _players.ToList()) window.Close();
        _players.Clear();
    }
}

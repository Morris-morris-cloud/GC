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

    private async void OpenPlayerWindow(ModelPackage package)
    {
        if (_viewModel == null) return;

        // 启动进度窗：模型加载、地表纹理生成、界面构建的真实进度
        var loading = new LoadingWindow(package.LocalizedName);
        loading.Show(this);
        loading.Report(0, "正在加载模型…");
        // 让进度窗先画出来（用调度器让出一帧，测试环境也能推进）
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

        PlayerWindow? window = null;
        LoadedModel? loaded = null;
        Exception? error = null;
        try
        {
            loaded = await _viewModel.Models.LoadAsync(package, () => window?.Close(),
                (value, message) => loading.Report(value, message));
        }
        catch (Exception ex)
        {
            error = ex;
        }

        loading.Close();

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

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.PlayerRequested -= OpenPlayerWindow;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
        foreach (var window in _players.ToList()) window.Close();
        _players.Clear();
    }
}

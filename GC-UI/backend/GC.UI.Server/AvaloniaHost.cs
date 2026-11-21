using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using GC.Models;
using GC.Services;
using GC.Views;

namespace GC.UI.Server;

/// <summary>
/// 模型窗口宿主。
///
/// 为什么渲染这一层留在 .NET：模型 dll 通过 <c>IModelRuntime.CreateView</c> 返回的是
/// Avalonia <see cref="Control"/>，这是模型契约的一部分。把渲染栈换成 Flutter 等于换掉契约，
/// 所有已发布模型都会失效。所以前后端分离的切法是：
///   · 界面（首页 / 下载 / 设置 / 关于）→ Flutter 前端
///   · 模型窗口（模型自带界面）→ 仍由 .NET 承载，前端只发一条「启动模型」的指令
///
/// 这里惰性启动一个没有主窗口的 Avalonia 桌面生命周期，之后所有模型窗口都在它的 UI 线程上创建。
/// 无桌面会话（CI / 无头环境）时启动会失败，此时只有「启动模型」不可用，其余接口照常工作。
/// </summary>
public sealed class AvaloniaHost
{
    private readonly AppServices _services;
    private readonly object _gate = new();
    private readonly Dictionary<string, PlayerWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly TaskCompletionSource<bool> _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _started;

    public AvaloniaHost(AppServices services) => _services = services;

    public bool IsAvailable { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>
    /// 后端线程调用：等待「主线程已把 Avalonia 事件循环跑起来」。
    ///
    /// 关键点：macOS 的 AppKit 要求 NSApplication 在**主线程**。以前这里把
    /// <c>StartWithClassicDesktopLifetime</c> 丢到一个新建的后台线程里跑，在 macOS 上
    /// 会直接崩掉整个后端进程（表现为点「启动模型」界面无反应、随后后端消失）。
    /// 现在改为由启动器在主线程调用 <see cref="RunOnCurrentThread"/>，
    /// 这里只等待就绪信号。
    /// </summary>
    public Task<bool> EnsureStartedAsync() => _ready.Task;

    /// <summary>
    /// 必须在 UI 主线程调用。启动 Avalonia 桌面生命周期并阻塞，直到 <see cref="Shutdown"/>。
    /// 返回后（或初始化失败后）调用方负责收尾。
    /// </summary>
    public void RunOnCurrentThread()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;

        try
        {
            AppBuilder.Configure<HostApp>()
                .UsePlatformDetect()
                .AfterSetup(_ =>
                {
                    IsAvailable = true;
                    _ready.TrySetResult(true);
                })
                .StartWithClassicDesktopLifetime(Array.Empty<string>());
        }
        catch (Exception ex)
        {
            // 无桌面会话（CI / 无头环境）：模型窗口不可用，但后端其余接口照常。
            LastError = ex.Message;
            _ready.TrySetResult(false);
        }
    }

    /// <summary>从任意线程请求结束事件循环，使 <see cref="RunOnCurrentThread"/> 返回。</summary>
    public void Shutdown()
    {
        if (_started == 0) return;
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
            });
        }
        catch
        {
            // 事件循环尚未建立或已退出：无需处理
        }
    }

    /// <summary>在 Avalonia UI 线程上加载模型并打开它自己的窗口。</summary>
    public async Task<string> LaunchAsync(ModelPackage package)
    {
        if (!await EnsureStartedAsync().ConfigureAwait(false))
            throw new InvalidOperationException(
                LastError ?? "无法初始化图形环境（当前会话没有桌面？）");

        return await Dispatcher.UIThread.InvokeAsync<string>(async () =>
        {
            // 同一个模型已经在运行：把已有窗口提到前台，不再加载第二份实例
            // （连点「启动模型」会各占一份内存与显存，低配机器直接卡死）。
            lock (_gate)
            {
                if (_windows.TryGetValue(package.Id, out var existing))
                {
                    try
                    {
                        if (existing.WindowState == WindowState.Minimized)
                            existing.WindowState = WindowState.Normal;
                        existing.Activate();
                        return $"模型 {package.Id} 已在运行";
                    }
                    catch
                    {
                        // 窗口已失效：清掉记录，按新窗口重新打开
                        _windows.Remove(package.Id);
                    }
                }
            }

            PlayerWindow? window = null;
            LoadedModel? loaded = null;
            try
            {
                Action requestClose = () => Dispatcher.UIThread.Post(() =>
                {
                    try { window?.Close(); } catch { /* 已关闭 */ }
                });

                loaded = await _services.ModelHost.LoadAsync(package, requestClose, null);

                window = new PlayerWindow(package, loaded);
                var captured = loaded;
                var id = package.Id;
                window.Closed += (_, _) =>
                {
                    try { captured.Dispose(); } catch { /* 释放失败不影响退出 */ }
                    lock (_gate)
                    {
                        if (_windows.TryGetValue(id, out var current) && ReferenceEquals(current, window))
                            _windows.Remove(id);
                    }
                };

                lock (_gate) _windows[id] = window;
                window.Show();
                return $"已启动模型 {package.Id}";
            }
            catch
            {
                try { loaded?.Dispose(); } catch { /* 忽略 */ }
                throw;
            }
        });
    }
}

/// <summary>后端自己的 Avalonia 应用：没有主窗口，只用来承载模型窗口。</summary>
internal sealed class HostApp : Application
{
    public override void Initialize() { }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = null;
        }

        base.OnFrameworkInitializationCompleted();
    }
}

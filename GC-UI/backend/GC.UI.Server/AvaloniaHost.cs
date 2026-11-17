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
    private readonly List<PlayerWindow> _windows = new();
    private Task<bool>? _startTask;

    public AvaloniaHost(AppServices services) => _services = services;

    public bool IsAvailable { get; private set; }

    public string? LastError { get; private set; }

    public Task<bool> EnsureStartedAsync()
    {
        lock (_gate)
        {
            _startTask ??= Task.Run(StartCore);
            return _startTask;
        }
    }

    private bool StartCore()
    {
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<HostApp>()
                    .UsePlatformDetect()
                    .AfterSetup(_ => ready.TrySetResult(true))
                    .StartWithClassicDesktopLifetime(Array.Empty<string>());
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                ready.TrySetResult(false);
            }
        })
        {
            IsBackground = true,
            Name = "gc-ui-avalonia"
        };

        // Windows 上 Avalonia 需要 STA 线程；macOS / Linux 不支持也不必要。
        if (OperatingSystem.IsWindows())
        {
            try { thread.SetApartmentState(ApartmentState.STA); }
            catch { /* 忽略：部分运行时不允许设置 */ }
        }

        thread.Start();

        if (!ready.Task.Wait(TimeSpan.FromSeconds(30)))
        {
            LastError = "图形环境初始化超时";
            return false;
        }

        IsAvailable = ready.Task.Result;
        return IsAvailable;
    }

    /// <summary>在 Avalonia UI 线程上加载模型并打开它自己的窗口。</summary>
    public async Task<string> LaunchAsync(ModelPackage package)
    {
        if (!await EnsureStartedAsync().ConfigureAwait(false))
            throw new InvalidOperationException(
                LastError ?? "无法初始化图形环境（当前会话没有桌面？）");

        return await Dispatcher.UIThread.InvokeAsync<string>(async () =>
        {
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
                window.Closed += (_, _) =>
                {
                    try { captured.Dispose(); } catch { /* 释放失败不影响退出 */ }
                    lock (_gate) _windows.Remove(window!);
                };

                lock (_gate) _windows.Add(window);
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

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GC.UI.Server;
using Microsoft.AspNetCore.Builder;

namespace GC;

/// <summary>
/// 新界面启动壳。
///
/// 发布包带 <c>GC-UI</c>（Flutter 前端）时，启动器只做三件事：
///   1. 在自己的进程里起后端（本地 HTTP API + 模型窗口宿主），等 <c>/api/health</c> 就绪；
///   2. 拉起 Flutter 窗口；
///   3. 界面退出后关掉后端。
///
/// 后端跑在进程内而不是单独一个 exe：两者用的是同一套 .NET 运行时，
/// 拆成两个自包含进程等于把 60MB 运行时复制两份，发布包会白白大一倍。
///
/// 缺 Flutter 前端时返回 <c>false</c>，由调用方回退到内置 Avalonia 界面——
/// 这样 `dotnet run`、只编了启动器的开发场景都能正常打开。
/// </summary>
internal static class FlutterShell
{
    /// <summary>与前端、后端共用的默认端口（定义在 GC.Services.GcUiContract）。</summary>
    private const int Port = Services.GcUiContract.DefaultPort;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(1.5) };

    private static WebApplication? _backend;

    public static bool TryRun(string[] args)
    {
        var dir = AppContext.BaseDirectory;
        var uiPath = FindUi(dir);
        if (uiPath == null) return false;

        var port = Port;

        // 用户可能重复启动：已有后端在跑就直接复用，退出时也不要关掉别人的进程。
        if (!IsHealthy(port))
        {
            // 端口可能被别的程序占用：17890 起向后试几个，选第一个能起来且健康的。
            var start = Environment.GetEnvironmentVariable(Services.GcUiContract.PortEnvVar) is { Length: > 0 }
                ? Services.GcUiContract.PortFromEnvironment()
                : Port;

            for (var candidate = start; candidate < start + Services.GcUiContract.PortScanRange; candidate++)
            {
                var app = TryStartBackend(args, candidate);
                if (app == null) continue;

                if (WaitHealthy(candidate, TimeSpan.FromSeconds(25)))
                {
                    port = candidate;
                    _backend = app;
                    break;
                }

                StopBackend(app);
            }

            if (_backend == null) return false;
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => StopBackend(_backend);

        var ui = Start(uiPath, dir, port);
        if (ui == null)
        {
            StopBackend(_backend);
            _backend = null;
            return false;
        }

        ui.WaitForExit();
        StopBackend(_backend);
        _backend = null;
        return true;
    }

    // ---------------------------------------------------------------- 产物定位

    /// <summary>
    /// Flutter 桌面产物。
    /// macOS 是 <c>GC-UI.app/Contents/MacOS/GC-UI</c>；Windows 是 <c>GC-UI.exe</c>；
    /// Linux 是 <c>GC-UI</c>。
    /// </summary>
    private static string? FindUi(string dir)
    {
        if (OperatingSystem.IsMacOS())
        {
            var exe = Path.Combine(dir, "GC-UI.app", "Contents", "MacOS", "GC-UI");
            if (File.Exists(exe)) return exe;
        }

        foreach (var name in OperatingSystem.IsWindows() ? new[] { "GC-UI.exe" } : new[] { "GC-UI" })
        {
            var path = Path.Combine(dir, name);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    // ---------------------------------------------------------------- 进程管理

    /// <summary>在本进程内启动后端；端口被占用等失败情况返回 null。</summary>
    private static WebApplication? TryStartBackend(string[] args, int port)
    {
        try
        {
            var app = BackendServer.Start(args, port);
            app.StartAsync().GetAwaiter().GetResult();
            return app;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[shell] 后端启动失败（端口 {port}）: {ex.Message}");
            return null;
        }
    }

    private static void StopBackend(WebApplication? app)
    {
        if (app == null) return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            app.StopAsync(cts.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[shell] 关闭后端失败: {ex.Message}");
        }
    }

    private static Process Start(string fileName, string workingDirectory, int port)
    {
        var info = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        };

        // 把实际端口传给前端：它读这个环境变量拼 baseUrl，换端口不需要重新编译界面。
        info.Environment[Services.GcUiContract.PortEnvVar] = port.ToString();

        // macOS .app 内的二进制也需要可执行位；发布包由 zip 解开时可能丢失。
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(fileName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
            catch { /* 权限不足时交给系统判断 */ }
        }

        return Process.Start(info)!;
    }

    // ---------------------------------------------------------------- 健康检查

    private static bool IsHealthy(int port)
    {
        try
        {
            using var response = Http.GetAsync($"http://127.0.0.1:{port}/api/health")
                .GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static bool WaitHealthy(int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (IsHealthy(port)) return true;
            Thread.Sleep(250);
        }
        return false;
    }
}

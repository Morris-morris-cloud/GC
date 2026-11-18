using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GC;

/// <summary>
/// 新界面启动壳。
///
/// 发布包同时带 <c>GC.UI.Server</c>（本地 HTTP 后端 + 模型窗口宿主）和
/// <c>GC-UI</c>（Flutter 前端）时，启动器只做三件事：
///   1. 拉起后端并等 <c>/api/health</c> 就绪；
///   2. 拉起 Flutter 窗口；
///   3. 界面退出后关掉后端。
///
/// 两个产物缺任意一个就返回 <c>false</c>，由调用方回退到内置 Avalonia 界面——
/// 这样 `dotnet run`、只编了启动器的开发场景、以及交叉编译缺前端产物的包都能正常打开。
/// </summary>
internal static class FlutterShell
{
    /// <summary>与前端、后端共用的默认端口（定义在 GC.Services.GcUiContract）。</summary>
    private const int Port = Services.GcUiContract.DefaultPort;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(1.5) };
    private static Process? _backend;

    public static bool TryRun(string[] args)
    {
        var dir = AppContext.BaseDirectory;
        var backendPath = FindBackend(dir);
        var uiPath = FindUi(dir);
        if (backendPath == null || uiPath == null) return false;

        // 用户可能重复启动：已有后端在跑就直接复用，退出时也不要关掉别人的进程。
        var port = Port;

        if (!IsHealthy(port))
        {
            // 端口可能被别的程序占用：17890 起向后试几个，选第一个能起来且健康的。
            var start = Environment.GetEnvironmentVariable(Services.GcUiContract.PortEnvVar) is { Length: > 0 }
                ? Services.GcUiContract.PortFromEnvironment()
                : Port;

            for (var candidate = start; candidate < start + Services.GcUiContract.PortScanRange; candidate++)
            {
                var process = Start(backendPath, $"--port {candidate}", dir);
                if (process == null) continue;

                if (WaitHealthy(candidate, process, TimeSpan.FromSeconds(25)))
                {
                    port = candidate;
                    _backend = process;
                    break;
                }

                Stop(process);
            }

            if (_backend == null) return false;
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop(_backend);

        var ui = Start(uiPath, null, dir, port);
        if (ui == null)
        {
            Stop(_backend);
            _backend = null;
            return false;
        }

        ui.WaitForExit();
        Stop(_backend);
        _backend = null;
        return true;
    }

    // ---------------------------------------------------------------- 产物定位

    private static string? FindBackend(string dir)
    {
        foreach (var name in OperatingSystem.IsWindows()
                     ? new[] { "GC.UI.Server.exe", "GC.UI.Server" }
                     : new[] { "GC.UI.Server", "GC.UI.Server.exe" })
        {
            var path = Path.Combine(dir, name);
            if (File.Exists(path)) return path;
        }
        return null;
    }

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

    private static Process? Start(string fileName, string? arguments, string workingDirectory, int port = 0)
    {
        try
        {
            var info = new ProcessStartInfo(fileName)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (!string.IsNullOrEmpty(arguments)) info.Arguments = arguments;

            // 把实际端口传给子进程：后端读它监听，前端读它拼 baseUrl。
            // 这样换端口不需要重新编译前端。
            if (port > 0)
                info.Environment[Services.GcUiContract.PortEnvVar] = port.ToString();

            // macOS .app 内的二进制也需要可执行位；发布包由 zip 解开时可能丢失。
            if (!OperatingSystem.IsWindows())
            {
                try { File.SetUnixFileMode(fileName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
                catch { /* 权限不足时交给系统判断 */ }
            }

            return Process.Start(info);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[shell] 启动失败 {fileName}: {ex.Message}");
            return null;
        }
    }

    private static void Stop(Process? process)
    {
        if (process == null) return;
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[shell] 关闭后端失败: {ex.Message}");
        }
        finally
        {
            process.Dispose();
        }
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

    private static bool WaitHealthy(int port, Process backend, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            // 后端进程已经退出就没必要再等：端口被占、依赖缺失都会走这里。
            if (backend.HasExited) return false;

            if (IsHealthy(port)) return true;
            Thread.Sleep(250);
        }
        return false;
    }
}

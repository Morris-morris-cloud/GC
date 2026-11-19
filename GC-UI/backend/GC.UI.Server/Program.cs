using GC.UI.Server;
using Microsoft.Extensions.Hosting;

// 独立进程入口：`GC.UI.Server --port 17890`
// 启动器在进程内直接调用 BackendServer.Start，不会走到这里。
var app = BackendServer.StartFromCommandLine(args, out var host);

// 模型窗口由 Avalonia 承载，macOS 的 AppKit 要求 NSApplication 在主线程。
// 所以后端交给 Kestrel 自己的线程池在后台跑，主线程留给 Avalonia 事件循环。
app.StartAsync().GetAwaiter().GetResult();
host.RunOnCurrentThread();

if (host.IsAvailable)
{
    // 事件循环结束 → 收尾后端
    app.StopAsync().GetAwaiter().GetResult();
}
else
{
    // 无图形环境（无头调试）：退化为纯后端常驻
    app.WaitForShutdown();
}

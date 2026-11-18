using GC.UI.Server;

// 独立进程入口：`GC.UI.Server --port 17890`
// 启动器在进程内直接调用 BackendServer.Start，不会走到这里。
var app = BackendServer.StartFromCommandLine(args);
app.Run();

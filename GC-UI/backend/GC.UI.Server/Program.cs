using GC.UI.Server;

// 独立进程入口：`GC.UI.Server --port 17890`
// 启动器在进程内直接调用 BackendServer.Start，不会走到这里。
var app = BackendServer.StartFromCommandLine(args);

// 纯 HTTP 后端（模型渲染在 Flutter 客户端里），跑起来常驻即可。
app.Run();

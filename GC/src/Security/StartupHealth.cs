using System;
using System.IO;

namespace GC.Services;

/// <summary>
/// 启动成功的健康标记。
///
/// 新版启动器启动后写这个文件；GC-SAVEDOG 在替换完程序后会等它出现，
/// 等不到就回滚到旧版本 —— 这样「新版本一启动就崩」不会把用户卡死。
/// </summary>
public static class StartupHealth
{
    public const string FileName = "health.json";

    public static string PathFor(string updateRoot) => Path.Combine(updateRoot, FileName);

    /// <summary>主窗口显示后调用：证明本次启动真的跑起来了。</summary>
    public static void MarkHealthy(string updateRoot, string version)
    {
        try
        {
            Directory.CreateDirectory(updateRoot);
            Json.Write(PathFor(updateRoot), new HealthRecord
            {
                Version = version,
                Pid = Environment.ProcessId,
                At = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                Ok = true
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"写入健康标记失败: {ex.Message}");
        }
    }

    public static HealthRecord? Read(string updateRoot)
        => Json.Read<HealthRecord>(PathFor(updateRoot));

    public static void Clear(string updateRoot)
    {
        try
        {
            var path = PathFor(updateRoot);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    public sealed class HealthRecord
    {
        public string Version { get; set; } = "";
        public int Pid { get; set; }
        public string At { get; set; } = "";
        public bool Ok { get; set; }
    }
}

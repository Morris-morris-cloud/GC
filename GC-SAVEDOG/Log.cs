using System;
using System.IO;

namespace GC.Savedog;

/// <summary>同时写 stdout 与 .GC/update/savedog.log，便于用户离线排查。</summary>
internal sealed class Log
{
    private readonly string? _path;
    private readonly object _gate = new();

    public Log(string? path) => _path = path;

    public void Info(string message) => Write("INFO ", message);
    public void Warn(string message) => Write("WARN ", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
        Console.WriteLine(line);
        if (string.IsNullOrWhiteSpace(_path)) return;
        try
        {
            lock (_gate)
            {
                var dir = Path.GetDirectoryName(_path!);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(_path!, line + "\n");
            }
        }
        catch { /* 日志失败不能影响更新 */ }
    }
}

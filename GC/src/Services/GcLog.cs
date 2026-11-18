using System;
using System.IO;
using System.Text;

namespace GC.Services;

/// <summary>
/// 双语文件日志。
///
/// 每条记录同时写两个文件（都在 <c>.GC/logs</c> 下，按天滚动）：
/// <code>
/// gc-20261118.log      中文
/// gc-20261118.en.log   英文
/// </code>
/// 中英分开而不是写在一个文件里：用户排查时不用在一行里找自己看得懂的那半句；
/// 也给反馈问题时直接发对应语言的文件即可。
///
/// 日志失败绝不影响主流程（磁盘满、只读目录都只是少一份日志）。
/// </summary>
public static class GcLog
{
    private static readonly object Gate = new();
    private static string? _dir;
    private static string _day = "";
    private static string _zhPath = "";
    private static string _enPath = "";

    public static bool Enabled
    {
        get { lock (Gate) return _dir != null; }
    }

    /// <summary>绑定日志目录（通常是 <c>.GC/logs</c>）。重复调用会切到新目录。</summary>
    public static void Init(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch
        {
            // 目录都建不了就退化成只写控制台
            return;
        }

        lock (Gate)
        {
            _dir = dir;
            _day = "";
        }
    }

    public static void Info(string zh, string en) => Write("INFO ", zh, en);
    public static void Warn(string zh, string en) => Write("WARN ", zh, en);
    public static void Error(string zh, string en) => Write("ERROR", zh, en);

    private static void Write(string level, string zh, string en)
    {
        var now = DateTime.Now;
        var prefix = $"{now:yyyy-MM-dd HH:mm:ss} [{level}] ";
        var lineZh = prefix + zh;
        var lineEn = prefix + en;

        try { Console.WriteLine(lineZh); } catch { /* 无控制台 */ }

        lock (Gate)
        {
            if (_dir == null) return;

            var day = now.ToString("yyyyMMdd");
            if (day != _day)
            {
                _day = day;
                _zhPath = Path.Combine(_dir, $"gc-{day}.log");
                _enPath = Path.Combine(_dir, $"gc-{day}.en.log");
            }

            Append(_zhPath, lineZh);
            Append(_enPath, lineEn);
        }
    }

    private static void Append(string path, string line)
    {
        try
        {
            File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
        }
        catch
        {
            // 日志失败不能影响程序
        }
    }
}

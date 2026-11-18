using System;
using System.Globalization;
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
/// 写入方式：常驻 <see cref="StreamWriter"/>（<c>AutoFlush</c> 打开，崩溃也不丢最后几行），
/// 不再每行 open/append/close 一遍 —— 那条路径每写一行要开两个文件句柄，
/// 在下载 / 启动高峰期会明显拖慢调用方。
///
/// 保留策略：<see cref="RetentionDays"/> 天之前的日志在初始化与跨天滚动时删除；
/// 总量超过 <see cref="MaxTotalBytes"/> 时从最旧的开始删。日志目录不会无限长。
///
/// 日志失败绝不影响主流程（磁盘满、只读目录都只是少一份日志）。
/// </summary>
public static class GcLog
{
    /// <summary>日志保留天数。</summary>
    public const int RetentionDays = 14;

    /// <summary>日志目录总量上限（中英两份合计）。</summary>
    public const long MaxTotalBytes = 64L * 1024 * 1024;

    private static readonly object Gate = new();
    private const string FilePrefix = "gc-";

    private static string? _dir;
    private static string _day = "";
    private static StreamWriter? _zh;
    private static StreamWriter? _en;

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
            CloseWriters();
            _dir = dir;
            _day = "";
            PruneOldFiles(dir);
        }
    }

    public static void Info(string zh, string en) => Write("INFO ", zh, en);
    public static void Warn(string zh, string en) => Write("WARN ", zh, en);
    public static void Error(string zh, string en) => Write("ERROR", zh, en);

    private static void Write(string level, string zh, string en)
    {
        var now = DateTime.Now;
        var prefix = $"{now:yyyy-MM-dd HH:mm:ss} [{level}] ";

        try { Console.WriteLine(prefix + zh); } catch { /* 无控制台 */ }

        lock (Gate)
        {
            if (_dir == null) return;

            var day = now.ToString("yyyyMMdd");
            if (day != _day)
            {
                // 跨天换文件；顺便清一次旧日志，长时间挂机的进程也不会攒满磁盘
                CloseWriters();
                _day = day;
                PruneOldFiles(_dir);
            }

            if (_zh == null)
            {
                _zh = OpenWriter(Path.Combine(_dir, $"{FilePrefix}{day}.log"));
                _en = OpenWriter(Path.Combine(_dir, $"{FilePrefix}{day}.en.log"));
            }

            Append(_zh, prefix + zh);
            Append(_en, prefix + en);
        }
    }

    private static StreamWriter? OpenWriter(string path)
    {
        try
        {
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            return new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        }
        catch
        {
            // 写不了就不写；下一次跨天会再试一次
            return null;
        }
    }

    private static void Append(StreamWriter? writer, string line)
    {
        if (writer == null) return;
        try
        {
            writer.WriteLine(line);
        }
        catch
        {
            // 磁盘满 / 文件被删：关掉写入器，下次跨天重建
            try { writer.Dispose(); } catch { }
            if (ReferenceEquals(writer, _zh)) _zh = null; else _en = null;
        }
    }

    private static void CloseWriters()
    {
        try { _zh?.Dispose(); } catch { }
        try { _en?.Dispose(); } catch { }
        _zh = null;
        _en = null;
    }

    /// <summary>
    /// 清理过期与超量日志。只动本模块自己写的 <c>gc-YYYYMMDD[.en].log</c>，
    /// 目录里用户放的其他文件一概不碰。
    /// </summary>
    private static void PruneOldFiles(string dir)
    {
        try
        {
            var files = new DirectoryInfo(dir).GetFiles(FilePrefix + "*.log");
            if (files.Length == 0) return;

            var cutoff = DateTime.Now.Date.AddDays(-RetentionDays);
            var alive = new System.Collections.Generic.List<FileInfo>(files.Length);
            foreach (var file in files)
            {
                if (!TryReadDate(file.Name, out var date))
                {
                    // 名字不是我们的格式，忽略（既不删也不计入配额）
                    continue;
                }

                if (date < cutoff)
                {
                    try { file.Delete(); } catch { }
                    continue;
                }

                alive.Add(file);
            }

            long total = 0;
            foreach (var file in alive) total += file.Length;
            if (total <= MaxTotalBytes) return;

            alive.Sort((a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
            foreach (var file in alive)
            {
                if (total <= MaxTotalBytes) break;
                try
                {
                    total -= file.Length;
                    file.Delete();
                }
                catch { }
            }
        }
        catch
        {
            // 清理失败不影响写日志
        }
    }

    private static bool TryReadDate(string name, out DateTime date)
    {
        date = default;
        // gc-20261118.log / gc-20261118.en.log
        if (!name.StartsWith(FilePrefix, StringComparison.Ordinal)) return false;
        var rest = name.Substring(FilePrefix.Length);
        var dot = rest.IndexOf('.');
        if (dot != 8) return false;
        return DateTime.TryParseExact(rest.Substring(0, 8), "yyyyMMdd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}

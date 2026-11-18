using System;
using System.IO;
using System.Linq;
using GC.Services;

namespace GC.Tests;

/// <summary>
/// 日志模块的用例：双语写入、跨天文件命名、旧日志清理、不误删用户文件。
///
/// 这些看起来是小事，但 <c>.GC/logs</c> 会随程序跑很久：写入路径每行开两个文件句柄，
/// 或者完全不清理，都会在几个月后变成用户磁盘上的几 GB。用例锁住这两个行为。
/// </summary>
internal static class LogTests
{
    private static int _failed;

    public static int Run()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gc-log-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            // 过期日志（2000 年）+ 用户自己的文件
            var oldZh = Path.Combine(dir, "gc-20000101.log");
            var oldEn = Path.Combine(dir, "gc-20000101.en.log");
            var userFile = Path.Combine(dir, "keep-me.txt");
            File.WriteAllText(oldZh, "old");
            File.WriteAllText(oldEn, "old");
            File.WriteAllText(userFile, "user data");

            GcLog.Init(dir);
            GcLog.Info("测试中文", "test english");

            var day = DateTime.Now.ToString("yyyyMMdd");
            var zhPath = Path.Combine(dir, $"gc-{day}.log");
            var enPath = Path.Combine(dir, $"gc-{day}.en.log");

            Check("中文日志写入", File.Exists(zhPath) && File.ReadAllText(zhPath).Contains("测试中文"), "中文日志缺失");
            Check("英文日志写入", File.Exists(enPath) && File.ReadAllText(enPath).Contains("test english"), "英文日志缺失");
            Check("过期日志被清理", !File.Exists(oldZh) && !File.Exists(oldEn), "2000 年的日志还在");
            Check("目录里的用户文件不被误删", File.Exists(userFile), "keep-me.txt 被删了");

            var names = Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(n => n).ToArray();
            Check("日志目录只剩当天两份 + 用户文件",
                names.Length == 3, "目录内容: " + string.Join(", ", names));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }

        Console.WriteLine(_failed == 0 ? "日志测试全部通过" : $"日志测试失败 {_failed} 项");
        return _failed;
    }

    private static void Check(string label, bool ok, string detail)
    {
        if (ok)
        {
            Console.WriteLine("  ✓ " + label);
            return;
        }
        _failed++;
        Console.WriteLine($"  ✗ {label} —— {detail}");
    }
}

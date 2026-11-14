using System;
using System.Diagnostics;
using System.IO;

namespace GC.Services;

public static class Shell
{
    public static void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            StartWith(path);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"打开目录失败: {ex.Message}");
        }
    }

    /// <summary>用系统默认浏览器打开一个 URL。</summary>
    public static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            StartWith(url);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"打开链接失败: {ex.Message}");
        }
    }

    private static void StartWith(string argument)
    {
        var info = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "explorer.exe"
                : OperatingSystem.IsMacOS() ? "open"
                : "xdg-open",
            UseShellExecute = false
        };
        info.ArgumentList.Add(argument);
        Process.Start(info);
    }
}

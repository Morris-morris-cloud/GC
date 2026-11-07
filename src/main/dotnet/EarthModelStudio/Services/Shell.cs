using System;
using System.Diagnostics;
using System.IO;

namespace EarthModelStudio.Services;

public static class Shell
{
    public static void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            var info = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "explorer.exe"
                    : OperatingSystem.IsMacOS() ? "open"
                    : "xdg-open",
                UseShellExecute = false
            };
            info.ArgumentList.Add(path);
            Process.Start(info);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"打开目录失败: {ex.Message}");
        }
    }
}

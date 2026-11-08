using System;
using System.IO;
using System.IO.Compression;

namespace EarthModelStudio.Services;

/// <summary>
/// 安全解压：拒绝 zip slip（条目名含 ../、绝对路径、盘符），
/// 并在解压前逐条校验目标路径完整落在目标目录内。
/// </summary>
public static class ZipSafety
{
    public static void ExtractToDirectory(string zipPath, string destination)
    {
        var destFull = Path.GetFullPath(destination)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        using (var archive = ZipFile.OpenRead(zipPath))
        {
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (name.StartsWith('/') || name.Contains("..", StringComparison.Ordinal) ||
                    (name.Length >= 2 && name[1] == ':'))
                    throw new InvalidOperationException($"压缩包含非法路径条目: {entry.FullName}");

                var target = Path.GetFullPath(Path.Combine(destFull, name));
                if (!target.StartsWith(destFull, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"压缩包条目越界: {entry.FullName}");
            }
        }

        // 校验通过后交给框架解压（保留 overwrite 语义）
        ZipFile.ExtractToDirectory(zipPath, destination, true);
    }
}

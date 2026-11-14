using System;
using System.IO;
using System.IO.Compression;

namespace GC.Services;

/// <summary>
/// 安全解压：更新包来自网络，必须当成不可信输入处理。
///
/// 拦截：
///   · zip slip（<c>../</c>、绝对路径、盘符、UNC）
///   · 符号链接 / 硬链接条目（解压后可指向系统任意文件）
///   · zip 炸弹（条目数过多、解压后总体积过大、压缩比异常）
/// 逐条校验通过后**自己写文件**，不使用 <see cref="ZipFile.ExtractToDirectory(string,string)"/>，
/// 避免框架在写入阶段才发现问题、留下半成品。
/// </summary>
public static class SafeArchive
{
    /// <summary>单个包最多条目数。</summary>
    public const int MaxEntries = 20000;

    /// <summary>解压后总体积上限（2 GiB）。</summary>
    public const long MaxUncompressedBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>单个条目解压上限（1 GiB）。</summary>
    public const long MaxSingleEntryBytes = 1L * 1024 * 1024 * 1024;

    /// <summary>压缩比上限：超过视为 zip 炸弹。</summary>
    public const int MaxCompressionRatio = 200;

    public sealed record Inspection(int Entries, long UncompressedBytes, long CompressedBytes)
    {
        public double Ratio => CompressedBytes <= 0 ? 0 : (double)UncompressedBytes / CompressedBytes;
    }

    /// <summary>只检查不解压：条目数、体积、路径、链接。发现问题抛异常。</summary>
    public static Inspection Inspect(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        long total = 0;
        var count = 0;

        foreach (var entry in archive.Entries)
        {
            count++;
            if (count > MaxEntries)
                throw new InvalidOperationException($"压缩包条目过多（>{MaxEntries}），疑似恶意包");

            ValidateName(entry.FullName);
            AssertNotLink(entry);

            if (entry.Length > MaxSingleEntryBytes)
                throw new InvalidOperationException($"条目过大: {entry.FullName}");

            total += entry.Length;
            if (total > MaxUncompressedBytes)
                throw new InvalidOperationException("解压后体积超出上限，疑似 zip 炸弹");
        }

        var compressed = new FileInfo(zipPath).Length;
        var inspection = new Inspection(count, total, compressed);
        if (compressed > 0 && inspection.Ratio > MaxCompressionRatio && total > 64L * 1024 * 1024)
            throw new InvalidOperationException($"压缩比异常（{inspection.Ratio:0.#}:1），疑似 zip 炸弹");

        return inspection;
    }

    public static void ExtractToDirectory(string zipPath, string destination)
    {
        Inspect(zipPath);

        Directory.CreateDirectory(destination);
        var destFull = Path.GetFullPath(destination)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(Path.GetFullPath(Path.Combine(destFull, name)));
                continue;
            }

            var target = Path.GetFullPath(Path.Combine(destFull, name));
            if (!target.StartsWith(destFull, StringComparison.Ordinal))
                throw new InvalidOperationException($"压缩包条目越界: {entry.FullName}");

            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using (var source = entry.Open())
            using (var output = new FileStream(target, FileMode.Create, FileAccess.Write,
                       FileShare.None, 81920, FileOptions.None))
            {
                source.CopyTo(output);
            }

            TryApplyUnixMode(entry, target);
        }
    }

    /// <summary>校验条目名，拒绝一切能逃出目标目录的写法。</summary>
    public static void ValidateName(string fullName)
    {
        var name = fullName.Replace('\\', '/');
        if (name.Length == 0) throw new InvalidOperationException("压缩包含空文件名条目");
        if (name.StartsWith('/')) throw new InvalidOperationException($"压缩包含绝对路径: {fullName}");
        if (name.StartsWith("//")) throw new InvalidOperationException($"压缩包含 UNC 路径: {fullName}");
        if (name.Length >= 2 && name[1] == ':')
            throw new InvalidOperationException($"压缩包含盘符路径: {fullName}");

        foreach (var segment in name.Split('/'))
        {
            if (segment == "..")
                throw new InvalidOperationException($"压缩包含父目录穿越: {fullName}");
        }

        if (name.Contains('\0'))
            throw new InvalidOperationException($"压缩包含非法字符: {fullName}");
    }

    /// <summary>Unix 模式下 S_IFLNK = 0xA000，符号链接条目一律拒绝。</summary>
    private static void AssertNotLink(ZipArchiveEntry entry)
    {
        var mode = (entry.ExternalAttributes >> 16) & 0xF000;
        if (mode == 0xA000)
            throw new InvalidOperationException($"压缩包含符号链接条目: {entry.FullName}");
    }

    private static void TryApplyUnixMode(ZipArchiveEntry entry, string target)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            var mode = (UnixFileMode)((entry.ExternalAttributes >> 16) & 0x1FF);
            if (mode == 0) return;
            File.SetUnixFileMode(target, mode);
        }
        catch { /* 权限位设置失败不影响文件内容 */ }
    }
}

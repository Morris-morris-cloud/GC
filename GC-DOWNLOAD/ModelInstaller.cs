using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GC.Models;

namespace GC.Services;

public static class ModelInstaller
{
    // 与后端 /api/models/{id} 的白名单保持一致：单段名称，首字符必须字母或数字。
    // 拒绝 . / .. / 路径分隔符 / 盘符 / 绝对路径，杜绝把 model.Id 当成路径来用。
    private static readonly Regex SafeId = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.Compiled);

    /// <summary>把源里的模型目录复制进本地模型库（先写临时目录再原子替换）。</summary>
    public static async Task<string> InstallAsync(RemoteModel model, string modelRoot,
        IProgress<(long Done, long Total)>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (string.IsNullOrWhiteSpace(modelRoot))
            throw new InvalidOperationException("模型根目录未配置");

        // ---- 1. 模型标识必须是单段安全名称，且解析后仍在模型根目录内 ----
        var target = SafeChildPath(modelRoot, model.Id);
        var rootFull = Path.GetFullPath(modelRoot);
        var staging = VerifyUnderRoot(target + ".installing", rootFull);
        var backup = VerifyUnderRoot(target + ".old", rootFull);

        // ---- 2. 源目录与三个目标路径都不能是符号链接/重解析点 ----
        EnsureNoLink(model.Directory);
        foreach (var p in new[] { target, staging, backup }) EnsureNoLink(p);

        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);

        // 枚举时跳过符号链接/重解析点：源目录里若有人放了指向外部的链接，
        // 绝不能顺着它把模型库外的文件读进来。
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false
        };
        var files = Directory.GetFiles(model.Directory, "*", enumeration);

        long total = 0;
        foreach (var file in files)
        {
            try { total += new FileInfo(file).Length; } catch { }
        }

        long done = 0;
        try
        {
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(model.Directory, file);
                // 路径穿越防护：相对路径必须完整落在 staging 目录内
                var dest = VerifyUnderRoot(Path.Combine(staging, relative), Path.GetFullPath(staging));
                var destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

                await using (var src = File.OpenRead(file))
                await using (var dst = File.Create(dest))
                {
                    await src.CopyToAsync(dst, 81920, ct);
                }
                long length = 0;
                try { length = new FileInfo(dest).Length; } catch { }
                done += length;
                progress?.Report((done, total <= 0 ? done : total));
            }

            // 原子替换：先备份旧目录，第二次 Move 失败时完整回滚，绝不把模型库清空
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            var hasBackup = false;
            try
            {
                if (Directory.Exists(target))
                {
                    Directory.Move(target, backup);
                    hasBackup = true;
                }
                Directory.Move(staging, target);
            }
            catch
            {
                // 新目录尚未就位时把旧目录还原；staging 由外层 catch 清理
                if (hasBackup && !Directory.Exists(target) && Directory.Exists(backup))
                    Directory.Move(backup, target);
                throw;
            }
            if (hasBackup)
            {
                try { Directory.Delete(backup, true); } catch { /* 占用时留到下次 */ }
            }

            progress?.Report((total, total));
            return target;
        }
        catch
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            throw;
        }
    }

    /// <summary>把 id 限定为单段安全名称，并确认拼出来的路径仍在 root 内。</summary>
    private static string SafeChildPath(string root, string? id)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            id == "." || id == ".." ||
            id.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 ||
            Path.IsPathRooted(id) ||
            !SafeId.IsMatch(id))
            throw new InvalidOperationException($"非法的模型标识: {id}");

        var rootFull = Path.GetFullPath(root);
        return VerifyUnderRoot(Path.Combine(rootFull, id), rootFull);
    }

    /// <summary>解析绝对路径并确认 candidate 位于 root 之内。</summary>
    private static string VerifyUnderRoot(string candidate, string rootFull)
    {
        var full = Path.GetFullPath(candidate);
        var prefix = rootFull.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"路径越界: {candidate}");
        return full;
    }

    /// <summary>拒绝符号链接 / junction / 重解析点，避免顺着链接写到模型库外。</summary>
    private static void EnsureNoLink(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"拒绝符号链接路径: {path}");
    }
}

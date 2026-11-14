using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GC.Models;

namespace GC.Services;

public static class ModelInstaller
{
    /// <summary>把源里的模型目录复制进本地模型库（先写临时目录再原子替换）。</summary>
    public static async Task<string> InstallAsync(RemoteModel model, string modelRoot,
        IProgress<(long Done, long Total)>? progress, CancellationToken ct)
    {
        var target = Path.Combine(modelRoot, model.Id);
        var staging = target + ".installing";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);

        long total = 0;
        var files = Directory.GetFiles(model.Directory, "*", SearchOption.AllDirectories);
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
                var dest = Path.GetFullPath(Path.Combine(staging, relative));
                var stagingFull = Path.GetFullPath(staging)
                    .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!dest.StartsWith(stagingFull, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"非法的模型文件路径: {relative}");
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
            var backup = target + ".old";
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
}

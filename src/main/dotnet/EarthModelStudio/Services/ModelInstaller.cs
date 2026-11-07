using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EarthModelStudio.Models;

namespace EarthModelStudio.Services;

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
                var dest = Path.Combine(staging, relative);
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

            if (Directory.Exists(target))
            {
                var backup = target + ".old";
                if (Directory.Exists(backup)) Directory.Delete(backup, true);
                Directory.Move(target, backup);
                Directory.Move(staging, target);
                try { Directory.Delete(backup, true); } catch { }
            }
            else
            {
                Directory.Move(staging, target);
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

using System;
using System.IO;

namespace GC.Services;

/// <summary>
/// 安全解压的统一入口（实现在 <see cref="SafeArchive"/>）。
/// 保留这个名字是因为模型源导入 / 启动器更新两处都在用，
/// 真正的校验逻辑集中在一处，避免两套实现走偏。
/// </summary>
public static class ZipSafety
{
    public static void ExtractToDirectory(string zipPath, string destination)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("压缩包不存在", zipPath);
        SafeArchive.ExtractToDirectory(zipPath, destination);
    }

    /// <summary>只检查不落盘：给下载完成后的预检用。</summary>
    public static SafeArchive.Inspection Inspect(string zipPath) => SafeArchive.Inspect(zipPath);
}

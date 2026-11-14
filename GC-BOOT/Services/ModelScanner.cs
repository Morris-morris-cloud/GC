using System;
using System.Collections.Generic;
using System.IO;
using GC.Models;

namespace GC.Services;

public static class ModelScanner
{
    /// <summary>扫描模型库：model/&lt;id&gt;/&lt;id&gt;.json + &lt;id&gt;.model/</summary>
    public static List<(ModelManifest Manifest, string Dir)> Scan(string modelRoot)
    {
        var list = new List<(ModelManifest, string)>();
        if (!Directory.Exists(modelRoot)) return list;

        foreach (var dir in Directory.GetDirectories(modelRoot))
        {
            var folder = Path.GetFileName(dir);
            if (folder.StartsWith(".")) continue;

            var manifestPath = Path.Combine(dir, folder + ".json");
            if (!File.Exists(manifestPath))
            {
                var candidates = Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly);
                if (candidates.Length == 0) continue;
                manifestPath = candidates[0];
            }

            ModelManifest? manifest;
            try { manifest = Json.Read<ModelManifest>(manifestPath); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"模型清单解析失败 {manifestPath}: {ex.Message}");
                continue;
            }

            if (manifest == null) continue;
            if (string.IsNullOrWhiteSpace(manifest.Id)) manifest.Id = folder;
            list.Add((manifest, dir));
        }

        list.Sort((a, b) => string.Compare(a.Item1.Id, b.Item1.Id, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    public static long DirectorySize(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; }
                catch { /* 忽略被占用的文件 */ }
            }
        }
        catch { /* 忽略权限问题 */ }
        return total;
    }

    public static string HumanSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }
}

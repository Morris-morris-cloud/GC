using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GC.Savedog;

namespace GC.Savedog.Tests;

/// <summary>
/// GC-SAVEDOG 的应用器测试：更新真正落盘的那一段（整目录替换 / .GC/bin 合并 / 失败回滚）。
///
/// 为什么单独一个工程：<c>GC.Tests</c> 引用的是启动器程序集，而 GC-SAVEDOG 为了保持
/// 「安全逻辑只有一份实现」把 <c>GC/src/Security</c> 与 <c>Json.cs</c> 以源码链接的方式
/// 编进了自己 —— 两个程序集里存在同名类型。同一个测试工程里同时引用两者会撞类型，
/// 所以应用器测试单独放这里，只引用守护进程。
///
/// 为什么需要这组用例：<c>--update-test</c> 覆盖的是「怎么判断该不该更新」，
/// <see cref="Applier"/> 才是会动用户安装目录的代码 —— 它出错就是用户目录被删空。
/// 全部在临时目录里跑，不碰真实安装、不联网。
/// </summary>
internal static class ApplierTests
{
    private static int _failed;
    private static string _root = "";

    public static int Main()
    {
        _root = Path.Combine(Path.GetTempPath(), "gc-applier-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        Console.WriteLine("Applier 测试目录: " + _root);

        try
        {
            CaseRefusesTraversalPackageName();
            CaseRefusesShaMismatch();
            CaseRefusesSourceTree();
            CaseDryRunKeepsInstallUntouched();
            CaseRollbackRestoresOldFiles();
            CaseSuccessMergesGcBinAndKeepsUserData();
        }
        finally
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        Console.WriteLine(_failed == 0
            ? "Applier 测试全部通过"
            : $"Applier 测试失败 {_failed} 项");
        return _failed == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ 用例

    /// <summary>包名带 ../ 时必须拒绝（路径穿越的第一道闸）。</summary>
    private static void CaseRefusesTraversalPackageName()
    {
        var (install, update) = Sandbox("traversal");
        WritePackage(update, new Dictionary<string, string>
        {
            ["GCL-9.9.9-test-amd64.exe"] = "new"
        });
        WriteRequest(update, "../evil.exe", sha: null);

        var code = Apply(update, install, dryRun: true);
        Check("路径穿越的包名被拒绝", code == 3, $"exit={code}（期望 3）");
    }

    /// <summary>zip 内容与清单 sha256 不符时必须丢弃。</summary>
    private static void CaseRefusesShaMismatch()
    {
        var (install, update) = Sandbox("sha");
        WritePackage(update, new Dictionary<string, string>
        {
            ["GCL-9.9.9-test-amd64.exe"] = "new"
        });
        WriteRequest(update, "GCL-9.9.9-test-amd64.exe", sha: new string('a', 64));

        var code = Apply(update, install, dryRun: true);
        Check("sha256 不符的包被丢弃", code == 5, $"exit={code}（期望 5）");
    }

    /// <summary>安装目录里有 GC.sln 时拒绝替换 —— 防止误在源码树里跑更新。</summary>
    private static void CaseRefusesSourceTree()
    {
        var (install, update) = Sandbox("srctree");
        File.WriteAllText(Path.Combine(install, "GC.sln"), "fake solution");
        var zip = WritePackage(update, new Dictionary<string, string>
        {
            ["GCL-9.9.9-test-amd64.exe"] = "new"
        });
        WriteRequest(update, "GCL-9.9.9-test-amd64.exe", Sha256File(zip));

        var code = Apply(update, install, dryRun: false);
        Check("源码目录被拒绝替换", code == 4, $"exit={code}（期望 4）");
        Check("源码目录里的文件没被动过",
            File.ReadAllText(Path.Combine(install, "GC.sln")) == "fake solution",
            "GC.sln 内容变了");
    }

    /// <summary>dry-run 只校验不落盘：安装目录必须一个字节都不变。</summary>
    private static void CaseDryRunKeepsInstallUntouched()
    {
        var (install, update) = Sandbox("dryrun");
        File.WriteAllText(Path.Combine(install, "GCL-1.0.0-test-amd64.exe"), "old");
        File.WriteAllText(Path.Combine(install, "only-in-old.txt"), "keep me");

        var zip = WritePackage(update, new Dictionary<string, string>
        {
            ["GCL-9.9.9-test-amd64.exe"] = "new",
            ["GC-UI.exe"] = "ui"
        });
        WriteRequest(update, "GCL-9.9.9-test-amd64.exe", Sha256File(zip));

        var code = Apply(update, install, dryRun: true);
        var names = Directory.GetFileSystemEntries(install).Select(Path.GetFileName).OrderBy(n => n).ToArray();
        Check("dry-run 返回 0", code == 0, $"exit={code}");
        Check("dry-run 不改安装目录",
            names.SequenceEqual(new[] { "GCL-1.0.0-test-amd64.exe", "only-in-old.txt" }),
            "安装目录变成: " + string.Join(", ", names));
    }

    /// <summary>
    /// 新版本启动不健康 → 回滚：旧文件原样回来，新包独有的文件被删掉，zip 与申请被清理。
    /// </summary>
    private static void CaseRollbackRestoresOldFiles()
    {
        var (install, update) = Sandbox("rollback");
        var oldExe = Path.Combine(install, "GCL-1.0.0-test-amd64.exe");
        var oldOnly = Path.Combine(install, "only-in-old.txt");
        File.WriteAllText(oldExe, "old-binary");
        File.WriteAllText(oldOnly, "old-only");

        // 新版什么都不做直接退出：健康标记不会出现，必然回滚
        var zip = WritePackage(update, new Dictionary<string, string>
        {
            ["GCL-9.9.9-test-amd64.exe"] = "#!/bin/sh\nexit 1\n",
            ["only-in-new.txt"] = "new-only"
        });
        WriteRequest(update, "GCL-9.9.9-test-amd64.exe", Sha256File(zip));

        var code = Apply(update, install, dryRun: false, timeoutSeconds: 5);
        Check("启动失败后回滚（exit=8）", code == 8, $"exit={code}（期望 8）");
        Check("旧启动器被还原",
            File.Exists(oldExe) && File.ReadAllText(oldExe) == "old-binary",
            "旧启动器没回来");
        Check("旧版独有文件被还原",
            File.Exists(oldOnly) && File.ReadAllText(oldOnly) == "old-only",
            "旧版独有文件没回来");
        Check("新包独有文件被删除", !File.Exists(Path.Combine(install, "only-in-new.txt")),
            "only-in-new.txt 残留");
        Check("更新包与申请已清理",
            !File.Exists(Path.Combine(update, "package.zip")) &&
            !File.Exists(Path.Combine(update, "apply-request.json")),
            "update 目录里还有残留");
    }

    /// <summary>正常路径：新文件就位、.GC/bin 被合并、.GC 下的用户数据原地不动。</summary>
    private static void CaseSuccessMergesGcBinAndKeepsUserData()
    {
        var (install, update) = Sandbox("success");
        var updateDirFull = Path.GetFullPath(update);

        // 用户数据：设置、模型、守护进程源文件
        Directory.CreateDirectory(Path.Combine(install, ".GC", "config"));
        Directory.CreateDirectory(Path.Combine(install, ".GC", "model", "earth-rotation"));
        Directory.CreateDirectory(Path.Combine(install, ".GC", "bin"));
        File.WriteAllText(Path.Combine(install, ".GC", "config", "settings.json"), "{\"theme\":\"dark\"}");
        File.WriteAllText(Path.Combine(install, ".GC", "model", "earth-rotation", "a.bin"), "model");
        File.WriteAllText(Path.Combine(install, ".GC", "bin", "GC-SAVEDOG.exe"), "old-savedog");

        // 假启动器：写健康标记后退出，模拟「新版本起来了」
        var launcher =
            "#!/bin/sh\n" +
            "cat > '" + Path.Combine(updateDirFull, "health.json") + "' <<'JSON'\n" +
            "{\"version\":\"9.9.9\",\"pid\":4242,\"at\":\"2026-01-01T00:00:00Z\",\"ok\":true}\n" +
            "JSON\n" +
            "exit 0\n";

        var zip = WritePackage(update, new Dictionary<string, string>
        {
            ["GCL-9.9.9-test-amd64.exe"] = launcher,
            [".GC/bin/GC-SAVEDOG.exe"] = "new-savedog",
            ["GC-UI.exe"] = "ui"
        });
        WriteRequest(update, "GCL-9.9.9-test-amd64.exe", Sha256File(zip));

        var code = Apply(update, install, dryRun: false, timeoutSeconds: 10);
        Check("启动健康后更新成功（exit=0）", code == 0, $"exit={code}");
        Check("新启动器就位",
            File.Exists(Path.Combine(install, "GCL-9.9.9-test-amd64.exe")), "新启动器缺失");
        Check("新前端就位", File.Exists(Path.Combine(install, "GC-UI.exe")), "GC-UI.exe 缺失");
        Check(".GC/bin 被合并",
            File.ReadAllText(Path.Combine(install, ".GC", "bin", "GC-SAVEDOG.exe")) == "new-savedog",
            ".GC/bin/GC-SAVEDOG.exe 没被替换");
        Check("用户设置原样保留",
            File.ReadAllText(Path.Combine(install, ".GC", "config", "settings.json")) == "{\"theme\":\"dark\"}",
            "settings.json 被覆盖");
        Check("模型目录原样保留",
            File.Exists(Path.Combine(install, ".GC", "model", "earth-rotation", "a.bin")),
            "模型文件被删除");
    }

    // ------------------------------------------------------------------ 辅助

    private static int Apply(string updateDir, string installDir, bool dryRun, int timeoutSeconds = 10)
    {
        var log = new Log(Path.Combine(updateDir, "logs"));
        var applier = new Applier(new ApplyOptions
        {
            UpdateDir = updateDir,
            InstallDir = installDir,
            DryRun = dryRun,
            TimeoutSeconds = timeoutSeconds,
            RequireSignature = false,   // 用例不生成签名清单；验签本身由 --update-test 覆盖
            Force = false
        }, log);
        return applier.Run();
    }

    private static (string Install, string Update) Sandbox(string name)
    {
        var baseDir = Path.Combine(_root, name);
        var install = Path.Combine(baseDir, "app");
        var update = Path.Combine(baseDir, "update");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(update);
        File.WriteAllText(Path.Combine(update, "version.json"), "{\"launcher\":{\"version\":\"9.9.9\"}}");
        return (install, update);
    }

    private static string WritePackage(string updateDir, IDictionary<string, string> files)
    {
        var zipPath = Path.Combine(updateDir, "package.zip");
        try { File.Delete(zipPath); } catch { }
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var (name, text) in files)
            {
                var entry = archive.CreateEntry(name.Replace('\\', '/'), CompressionLevel.Fastest);
                using var stream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        return zipPath;
    }

    /// <summary>手写申请文件：不引用 Json 序列化助手（同名类型在两个程序集里都有）。</summary>
    private static void WriteRequest(string updateDir, string packageName, string? sha)
    {
        var zipPath = Path.Combine(updateDir, "package.zip");
        var size = File.Exists(zipPath) ? new FileInfo(zipPath).Length : 0;
        var json = new StringBuilder()
            .Append("{\n")
            .Append("  \"schema\": 1,\n")
            .Append("  \"version\": \"9.9.9\",\n")
            .Append("  \"rid\": \"test-amd64\",\n")
            .Append("  \"platformLabel\": \"test-amd64\",\n")
            .Append("  \"packageName\": ").Append(Quote(packageName)).Append(",\n")
            .Append("  \"payloadDir\": \"payload\",\n")
            .Append("  \"installRelative\": \"../app\",\n")
            .Append("  \"manifestPath\": \"version.json\",\n")
            .Append("  \"packageZip\": \"package.zip\",\n")
            .Append("  \"packageSha256\": ").Append(sha == null ? "null" : Quote(sha)).Append(",\n")
            .Append("  \"packageSize\": ").Append(size).Append(",\n")
            .Append("  \"requireSignature\": false\n")
            .Append("}\n")
            .ToString();
        File.WriteAllText(Path.Combine(updateDir, "apply-request.json"), json);
    }

    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
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

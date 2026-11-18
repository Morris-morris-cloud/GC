using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GC.Services;

namespace GC.Savedog;

/// <summary>
/// GC-SAVEDOG 命令行。
///
///   GC-SAVEDOG apply  --update &lt;.GC/update&gt; [--install &lt;dir&gt;] [--wait-pid N]
///                     [--timeout 45] [--dry-run] [--json]
///   GC-SAVEDOG verify --manifest &lt;version.json&gt; [--require 1] [--json]
///   GC-SAVEDOG verify-model --dir &lt;model/&lt;id&gt;&gt; --id &lt;id&gt; [--json]
///   GC-SAVEDOG keygen [--out .signing] [--key-id gc-2026-11]
///   GC-SAVEDOG sign   --manifest &lt;version.json&gt; [--key .signing/gc-signing.pem]
///                     [--key-id ...] [--out &lt;file&gt;]
///   GC-SAVEDOG status --update &lt;.GC/update&gt; [--json]
/// </summary>
internal static class Program
{
    private const string DefaultKeyId = "gc-2026-12";
    private const string DefaultKeyPath = ".signing/gc-signing.pem";

    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }

        // 必须在任何 Json.Read/Write 之前接上源生成上下文（裁剪构建必需）
        SavedogJson.Initialize();

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Help();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        var options = Parse(rest);

        return command switch
        {
            "apply" => Apply(options),
            "verify" => Verify(options),
            "verify-model" => VerifyModel(options),
            "keygen" => Keygen(options),
            "sign" => Sign(options),
            "status" => Status(options),
            "version" or "--version" => PrintVersion(),
            _ => Unknown(command)
        };
    }

    // ------------------------------------------------------------------ apply

    private static int Apply(Dictionary<string, string> options)
    {
        var updateDir = Option(options, "update", "");
        if (string.IsNullOrWhiteSpace(updateDir))
        {
            Console.Error.WriteLine("apply 需要 --update <.GC/update>");
            return 3;
        }

        // 日志统一放 .GC/logs（update 目录的上一层），不再塞在 update/ 里
        var updateRoot = Path.GetFullPath(updateDir);
        var home = Path.GetDirectoryName(updateRoot) ?? updateRoot;
        var log = new Log(Path.Combine(home, "logs"));
        var applyOptions = new ApplyOptions
        {
            UpdateDir = updateDir,
            InstallDir = Option(options, "install", null),
            WaitPid = Int(options, "wait-pid", 0),
            TimeoutSeconds = Int(options, "timeout", 45),
            DryRun = Flag(options, "dry-run"),
            Force = Flag(options, "force"),
            RequireSignature = Int(options, "require-signature", 1) != 0
        };

        var applier = new Applier(applyOptions, log);
        var code = applier.Run();
        if (Flag(options, "json"))
        {
            if (applyOptions.DryRun)
            {
                // dry-run 不落盘，state.json 里是上一次的真实结果，直接打印会误导调用方
                var request = Json.Read<SavedogRequest>(
                    Path.Combine(Path.GetFullPath(updateDir), SavedogFiles.ApplyRequest));
                Console.WriteLine(Json.Serialize(new SavedogState
                {
                    Status = code == 0 ? "checked" : "failed",
                    Version = request?.Version ?? "",
                    PackageName = request?.PackageName ?? "",
                    Verified = code == 0,
                    Message = code == 0 ? "校验全部通过（未安装，dry-run）" : "校验未通过（dry-run）"
                }));
            }
            else
            {
                var state = Json.Read<SavedogState>(
                    Path.Combine(Path.GetFullPath(updateDir), SavedogFiles.State));
                Console.WriteLine(Json.Serialize(state));
            }
        }
        return code;
    }

    // ------------------------------------------------------------------ verify

    private static int Verify(Dictionary<string, string> options)
    {
        var manifest = Option(options, "manifest", "");
        if (string.IsNullOrWhiteSpace(manifest) || !File.Exists(manifest))
        {
            Console.Error.WriteLine("verify 需要 --manifest <version.json>");
            return 3;
        }

        var require = Int(options, "require", 1) != 0;
        var text = File.ReadAllText(manifest);
        var result = ManifestSignature.Verify(text, require);

        if (Flag(options, "json"))
        {
            var report = new VerifyReport
            {
                Trust = result.Trust.ToString(),
                KeyId = result.KeyId,
                Error = result.Error
            };
            foreach (var key in SignatureKeyring.Keys)
                report.TrustedKeys.Add(new TrustedKey
                {
                    KeyId = key.KeyId,
                    Fingerprint = ManifestSignature.PublicKeyFingerprint(key.PublicKeyBase64)
                });
            Console.WriteLine(Json.Serialize(report));
        }
        else
        {
            Console.WriteLine($"信任状态: {result.Trust}");
            if (result.KeyId != null) Console.WriteLine($"密钥: {result.KeyId}");
            if (result.Error != null) Console.WriteLine($"说明: {result.Error}");
            foreach (var key in SignatureKeyring.Keys)
                Console.WriteLine($"受信任公钥: {key.KeyId} ({ManifestSignature.PublicKeyFingerprint(key.PublicKeyBase64)})");
        }

        return result.IsTrusted ? 0 : 2;
    }

    private static int VerifyModel(Dictionary<string, string> options)
    {
        var dir = Option(options, "dir", "");
        var id = Option(options, "id", "");
        if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(id))
        {
            Console.Error.WriteLine("verify-model 需要 --dir <model 目录> --id <模型 id>");
            return 3;
        }

        var result = ModelIntegrity.VerifyForLoad(Path.GetFullPath(dir), id);
        if (Flag(options, "json"))
            Console.WriteLine(Json.Serialize(new VerifyModelReport
            {
                Ok = result.Ok,
                Present = result.Present,
                Message = result.Message
            }));
        else
            Console.WriteLine($"{(result.Ok ? "OK" : "FAIL")} 记录={(result.Present ? "有" : "无")} {result.Message}");

        return result.Ok ? 0 : 2;
    }

    // ------------------------------------------------------------------ keygen / sign

    private static int Keygen(Dictionary<string, string> options)
    {
        var outDir = Option(options, "out", ".signing")!;
        var keyId = Option(options, "key-id", DefaultKeyId)!;
        Directory.CreateDirectory(outDir);

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privatePath = Path.Combine(outDir, "gc-signing.pem");
        var publicPath = Path.Combine(outDir, "gc-signing.pub");

        File.WriteAllText(privatePath, key.ExportPkcs8PrivateKeyPem());
        var publicBase64 = ManifestSignature.ExportPublicKeyBase64(key);
        File.WriteAllText(publicPath, publicBase64 + "\n");

        try
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(privatePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch { }

        Console.WriteLine("已生成 ECDSA P-256 密钥对：");
        Console.WriteLine("  私钥（绝不要提交）: " + privatePath);
        Console.WriteLine("  公钥（写进源码）  : " + publicPath);
        Console.WriteLine("  keyId             : " + keyId);
        Console.WriteLine("  公钥 Base64       : " + publicBase64);
        Console.WriteLine("  公钥指纹          : " + ManifestSignature.PublicKeyFingerprint(publicBase64));
        Console.WriteLine();
        Console.WriteLine("把下面一行加进 GC/src/Security/SignatureKeyring.cs 的 Keys 列表：");
        Console.WriteLine($"  new KeyEntry(\"{keyId}\", \"{publicBase64}\"),");
        return 0;
    }

    private static int Sign(Dictionary<string, string> options)
    {
        var manifest = Option(options, "manifest", "");
        var keyPath = Option(options, "key", DefaultKeyPath)!;
        var keyId = Option(options, "key-id",
            Environment.GetEnvironmentVariable("GC_SIGNING_KEY_ID") ?? DefaultKeyId)!;

        if (string.IsNullOrWhiteSpace(manifest) || !File.Exists(manifest))
        {
            Console.Error.WriteLine("sign 需要 --manifest <version.json>");
            return 3;
        }

        using var key = ManifestSignature.TryLoadPrivateKey(keyPath);
        if (key == null)
        {
            Console.Error.WriteLine($"找不到可用的签名私钥: {Path.GetFullPath(keyPath)}");
            return 4;
        }

        var text = File.ReadAllText(manifest);
        var signed = ManifestSignature.SignDocument(text, key, keyId);
        var output = Option(options, "out", manifest)!;
        File.WriteAllText(output, signed);

        var check = ManifestSignature.Verify(signed, requireSignature: true);
        Console.WriteLine($"已签名: {output}（{check.Trust}）");
        return check.Trust == ManifestTrust.Verified ? 0 : 5;
    }

    // ------------------------------------------------------------------ status

    private static int Status(Dictionary<string, string> options)
    {
        var updateDir = Path.GetFullPath(Option(options, "update", ".GC/update")!);
        var state = Json.Read<SavedogState>(Path.Combine(updateDir, SavedogFiles.State));
        var lastGood = Json.Read<LastGoodVersion>(Path.Combine(updateDir, SavedogFiles.LastGood));
        var health = StartupHealth.Read(updateDir);

        if (Flag(options, "json"))
        {
            Console.WriteLine(Json.Serialize(new StatusReport
            {
                State = state,
                LastGood = lastGood,
                Health = health
            }));
            return 0;
        }

        Console.WriteLine(state == null
            ? "没有更新记录"
            : $"最近更新: {state.Status} v{state.Version} -> {state.Message}（{state.At}）");
        Console.WriteLine(lastGood == null
            ? "上次成功版本: 未知"
            : $"上次成功版本: v{lastGood.Version}（{lastGood.PackageName}）");
        Console.WriteLine(health == null
            ? "健康标记: 无"
            : $"健康标记: v{health.Version} pid={health.Pid} ok={health.Ok}");
        return 0;
    }

    private static int PrintVersion()
    {
        Console.WriteLine("GC-SAVEDOG 1.0.0");
        Console.WriteLine("签名算法: " + ManifestSignature.Algorithm);
        return 0;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"未知命令: {command}");
        Help();
        return 3;
    }

    private static void Help()
    {
        Console.WriteLine("""
GC-SAVEDOG —— 启动器更新守护进程

  apply        --update <.GC/update> [--install <dir>] [--wait-pid N]
               [--timeout 45] [--dry-run] [--force] [--json]
  verify       --manifest <version.json> [--require 1] [--json]
  verify-model --dir <model/ID> --id <ID> [--json]
  keygen       [--out .signing] [--key-id gc-2026-11]
  sign         --manifest <version.json> [--key .signing/gc-signing.pem] [--out <file>]
  status       --update <.GC/update> [--json]

退出码：0 成功 / 2 验签失败（拒绝） / 3 参数或申请错误 / 5 包校验失败 / 8 已回滚
""");
    }

    // ------------------------------------------------------------------ 参数

    private static Dictionary<string, string> Parse(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                map[arg] = "1";
                continue;
            }

            var name = arg[2..];
            var eq = name.IndexOf('=');
            if (eq > 0)
            {
                map[name[..eq]] = name[(eq + 1)..];
                continue;
            }

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                map[name] = args[++i];
                continue;
            }
            map[name] = "1";
        }
        return map;
    }

    private static string? Option(Dictionary<string, string> options, string name, string? fallback)
        => options.TryGetValue(name, out var value) ? value : fallback;

    private static int Int(Dictionary<string, string> options, string name, int fallback)
        => options.TryGetValue(name, out var value) && int.TryParse(value, out var parsed)
            ? parsed : fallback;

    private static bool Flag(Dictionary<string, string> options, string name)
        => options.TryGetValue(name, out var value) &&
           (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
}

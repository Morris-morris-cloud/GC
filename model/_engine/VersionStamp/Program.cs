using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using GC.Services;

// VersionStamp：构建时自动给模型打版本。
// 用法：dotnet run --project model/_engine/VersionStamp -- <仓库根> <启动器版本>
// 输出：更新 model/<id>/<id>.json 的 version / updatedAt，并生成
//       仓库根 version.json 与 model/version.json（内容相同，便于两种源地址读取）。

// 仓库地址（编译产物的 Release 也发在这里）：与启动器默认 updateRepository 保持一致。
const string Repository = "https://git.xhmcl.com/Morris/GC";

// 位置参数：<仓库根> <启动器版本>；开关：--launcher-only
// --launcher-only 只刷新 launcher.packages（构建脚本在打完 zip 之后再调一次），
// 不重算模型指纹 —— 模型文件可能有几百 MB，重复哈希纯属浪费。
var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
var launcherOnly = args.Any(a => a.Equals("--launcher-only", StringComparison.OrdinalIgnoreCase));

var root = positional.Length > 0 ? Path.GetFullPath(positional[0]) : Directory.GetCurrentDirectory();
var launcherVersion = positional.Length > 1 && !string.IsNullOrWhiteSpace(positional[1])
    ? positional[1] : "1.0.0";
var modelRoot = Path.Combine(root, "model");

if (!Directory.Exists(modelRoot))
{
    Console.Error.WriteLine($"找不到模型库: {modelRoot}");
    return 1;
}

var relaxed = new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};

static string Rel(string baseDir, string file) =>
    Path.GetRelativePath(baseDir, file).Replace('\\', '/');

static bool Distributable(string file)
{
    var name = Path.GetFileName(file);
    if (name.StartsWith('.')) return false;
    if (name.EndsWith(".user", StringComparison.OrdinalIgnoreCase)) return false;
    var rel = file.Replace('\\', '/');
    return !rel.Contains("/bin/", StringComparison.OrdinalIgnoreCase) &&
           !rel.Contains("/obj/", StringComparison.OrdinalIgnoreCase) &&
           !rel.EndsWith(".installing", StringComparison.OrdinalIgnoreCase);
}

static string Fingerprint(IEnumerable<byte[]> chunks)
{
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    foreach (var chunk in chunks) hash.AppendData(chunk);
    return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
}

var models = new List<Dictionary<string, object>>();

if (!launcherOnly)
{
Console.WriteLine("==> 自动计算模型版本（按模型包内容指纹）");

foreach (var dir in Directory.GetDirectories(modelRoot)
             .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal))
{
    var id = Path.GetFileName(dir);
    if (id.StartsWith('_')) continue;                 // _sdk / _engine 是共享源码，不是模型

    var manifestPath = Path.Combine(dir, id + ".json");
    var dataDir = Path.Combine(dir, id + ".model");
    if (!File.Exists(manifestPath) || !Directory.Exists(dataDir)) continue;

    var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject();
    if (manifest == null) continue;

    // 版本号由模型清单里手写的 version 决定（作者手动升到 1.0 / 1.1 …），
    // 不再用内容指纹覆盖：指纹形式（1.0-393cb2b7d4）对用户不可读、也没有语义。
    var declared = manifest["version"]?.GetValue<string>();
    var version = string.IsNullOrWhiteSpace(declared) ? "1.0" : declared!.Trim();

    // 内容指纹保留在 build 字段，仅用于构建溯源，不参与版本比较。
    // 注意：updatedAt / build 都是构建元数据，必须从指纹里剔除，
    // 否则「同一份内容」每次构建都会算出不同指纹（等于每次都触发更新）。
    manifest.Remove("build");
    manifest.Remove("updatedAt");
    var canonical = manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    var chunks = new List<byte[]> { Encoding.UTF8.GetBytes(canonical) };
    foreach (var file in Directory.GetFiles(dataDir, "*", SearchOption.AllDirectories)
                 .Where(Distributable)
                 .OrderBy(f => Rel(dataDir, f), StringComparer.Ordinal))
    {
        chunks.Add(Encoding.UTF8.GetBytes("\n" + Rel(dataDir, file) + "\n"));
        chunks.Add(File.ReadAllBytes(file));
    }
    var fingerprint = Fingerprint(chunks)[..10];

    manifest["version"] = version;
    manifest["build"] = fingerprint;
    manifest["updatedAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
    var manifestText = manifest.ToJsonString(relaxed) + "\n";
    File.WriteAllText(manifestPath, manifestText);

    // 完整性哈希：客户端下载后按同一算法复算，不一致就丢弃。
    //   manifestSha256 = 清单文件本身的 sha256
    //   filesSha256    = <id>.model 目录指纹（算法见 GC/src/Security/ContentHash.cs）
    var manifestSha = ContentHash.Sha256Hex(manifestText);
    var filesMap = ContentHash.MapFiles(dataDir);
    var filesSha = ContentHash.DirectoryHash(filesMap);

    models.Add(new Dictionary<string, object>
    {
        ["id"] = id,
        ["version"] = version,
        ["build"] = fingerprint,
        ["manifestSha256"] = manifestSha,
        ["filesSha256"] = filesSha,
        ["files"] = filesMap.Count,
        ["dir"] = "model/" + id
    });
    Console.WriteLine($"    {id,-20} {version,-8} (build {fingerprint}, {filesMap.Count} files)");
}
}   // !launcherOnly

// 启动器源码指纹（只展示，不触发升级；升级看 launcher.version）
var srcRoot = Path.Combine(root, "src");
var srcChunks = new List<byte[]>();
if (Directory.Exists(srcRoot))
{
    foreach (var file in Directory.GetFiles(srcRoot, "*", SearchOption.AllDirectories)
                 .Where(Distributable)
                 .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                             f.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase) ||
                             f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                             f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                             f.EndsWith(".props", StringComparison.OrdinalIgnoreCase))
                 .OrderBy(f => Rel(srcRoot, f), StringComparer.Ordinal))
    {
        srcChunks.Add(Encoding.UTF8.GetBytes(Rel(srcRoot, file) + "\n"));
        srcChunks.Add(File.ReadAllBytes(file));
    }
}
var launcherRevision = srcChunks.Count > 0 ? Fingerprint(srcChunks)[..10] : "";

var packages = new Dictionary<string, Dictionary<string, object>>();
// 启动器更新包的两种落点，清单里都写清楚，下载端按顺序尝试：
//   1) Release 附件（url）—— 常规发布方式；
//   2) 仓库里的编译产物 dist/<系统>-<架构>/GC-<版本>-<系统>-<架构>.zip ——
//      不发 Release、只提交产物也能更新（用户明确要求支持这种布局）。
// 两种都写：url 只是「去哪儿找」，真正的完整性凭据是下面的 sha256（受签名保护）。
var releaseBase = $"{Repository}/releases/download/{launcherVersion}";
foreach (var rid in new[] { "win-x64", "win-arm64", "osx-arm64", "osx-x64", "linux-x64" })
{
    var label = LauncherArtifacts.LabelOf(rid);
    var fileName = LauncherArtifacts.PackageFileName(launcherVersion, rid);
    var dir = $"dist/{label}";

    var entry = new Dictionary<string, object>
    {
        ["path"] = $"{dir}/{fileName}",
        ["dir"] = dir,
        ["name"] = fileName,
        ["url"] = $"{releaseBase}/{fileName}"
    };

    // 优先认平台产物目录，其次认 dist 根目录（历史构建把 zip 直接放在 dist/）
    var zipPath = new[]
    {
        Path.Combine(root, dir.Replace('/', Path.DirectorySeparatorChar), fileName),
        Path.Combine(root, "dist", fileName),
        Path.Combine(root, "dist", $"GC-{rid}.zip")
    }.FirstOrDefault(File.Exists);

    if (zipPath != null)
    {
        var bytes = File.ReadAllBytes(zipPath);
        entry["path"] = Rel(root, zipPath);
        entry["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        entry["size"] = bytes.LongLength;
        Console.WriteLine($"    包 {label,-12} {entry["path"]}（{bytes.LongLength / 1024 / 1024} MB）");
    }
    else
    {
        Console.WriteLine($"    包 {label,-12} 未找到产物（清单里不会有 sha256，下载端会跳过该平台）");
    }
    packages[rid] = entry;
}

var versionDoc = new Dictionary<string, object>
{
    ["schema"] = 1,
    ["generated"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
    ["repository"] = Repository,
    ["branch"] = "main",
    ["modelDir"] = "model",
    ["launcher"] = new Dictionary<string, object>
    {
        ["version"] = launcherVersion,
        ["revision"] = launcherRevision,
        ["packages"] = packages
    },
    ["models"] = models
};

// --launcher-only：模型段沿用现有清单，只换启动器包，避免重复哈希几百 MB 模型文件
if (launcherOnly)
{
    var manifestPath = Path.Combine(root, "version.json");
    if (File.Exists(manifestPath))
    {
        var existing = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject();
        if (existing?["models"] is JsonNode existingModels)
            versionDoc["models"] = existingModels.DeepClone();
        if (existing?["generated"] is JsonNode generated)
            versionDoc["generated"] = generated.DeepClone();
    }
    else
    {
        Console.Error.WriteLine("!! --launcher-only 需要已存在的 version.json，退回完整模式");
    }
}

var json = JsonSerializer.Serialize(versionDoc, relaxed) + "\n";
File.WriteAllText(Path.Combine(root, "version.json"), json);
File.WriteAllText(Path.Combine(modelRoot, "version.json"), json);
Console.WriteLine($"==> version.json 已更新（启动器 {launcherVersion}，模型 {models.Count} 个）");
return 0;

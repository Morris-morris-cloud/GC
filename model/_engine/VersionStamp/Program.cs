using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

// VersionStamp：构建时自动给模型打版本。
// 用法：dotnet run --project model/_engine/VersionStamp -- <仓库根> <启动器版本>
// 输出：更新 model/<id>/<id>.json 的 version / updatedAt，并生成
//       仓库根 version.json 与 model/version.json（内容相同，便于两种源地址读取）。

// 仓库地址（编译产物的 Release 也发在这里）：与启动器默认 updateRepository 保持一致。
const string Repository = "https://gitcode.com/2509_92447106/GC";

var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
var launcherVersion = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]) ? args[1] : "1.0.0";
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

Console.WriteLine("==> 自动计算模型版本（按模型包内容指纹）");
var models = new List<Dictionary<string, string>>();

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

    // 版本号只由「清单内容 + 模型包数据」决定，重复构建结果稳定
    manifest.Remove("version");
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

    var version = "1.0-" + Fingerprint(chunks)[..10];
    manifest["version"] = version;
    manifest["updatedAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
    File.WriteAllText(manifestPath, manifest.ToJsonString(relaxed) + "\n");

    models.Add(new Dictionary<string, string>
    {
        ["id"] = id,
        ["version"] = version,
        ["dir"] = "model/" + id
    });
    Console.WriteLine($"    {id,-20} {version}");
}

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
// 启动器更新只访问「编译的产物」：gitcode Release 里发布的 dist/GC-<rid>.zip 附件。
// 运行时若存在同名 Release，GitCodeReleaseRemote 会用附件真实直链覆盖下面的 url；
// 下面的地址是兜底（编译产物尚未发 Release 时也能指向正确的编译产物位置）。
var releaseBase = $"{Repository}/releases/download/v{launcherVersion}";
foreach (var rid in new[] { "win-x64", "win-arm64", "osx-arm64", "osx-x64", "linux-x64" })
{
    var entry = new Dictionary<string, object>
    {
        ["path"] = $"dist/GC-{rid}.zip",
        ["url"] = $"{releaseBase}/GC-{rid}.zip"
    };
    // 已生成的启动器更新包：把校验值和体积写进清单，供下载端校验
    var zipPath = Path.Combine(root, "dist", $"GC-{rid}.zip");
    if (File.Exists(zipPath))
    {
        var bytes = File.ReadAllBytes(zipPath);
        entry["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        entry["size"] = bytes.LongLength;
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

var json = JsonSerializer.Serialize(versionDoc, relaxed) + "\n";
File.WriteAllText(Path.Combine(root, "version.json"), json);
File.WriteAllText(Path.Combine(modelRoot, "version.json"), json);
Console.WriteLine($"==> version.json 已生成（启动器 {launcherVersion}，模型 {models.Count} 个）");
return 0;

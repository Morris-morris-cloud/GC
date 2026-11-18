using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace GC.Services;

// ===========================================================================
// 启动器更新模块（唯一实现）
//
// 需求（按用户）：
//   · 只认 GitCode Release 的 latest 版本号（tag），文件名格式：
//       GC-<版本>-<系统>-<架构>.exe / .app
//       例 GC-1.0.3-win-amd64.exe / GC-1.0.3-mac-arm64.app
//   · 当前运行版本与 latest 不同 => 提示更新；相同 => 无需更新
//   · 下载的新版解压到「当前运行目录」，与旧版并列；.GC 完全不动
//   · 更新完成后：删除同系统同架构、版本号 != latest 的其它 GC-* 文件；
//     异系统 / 异架构的文件一律不碰
//   · 一切落盘内容（脚本、标记）只用相对路径，绝不写绝对路径
// ===========================================================================

/// <summary>下载 / 检查 / 应用启动器更新的唯一入口。</summary>
public static class LauncherUpdater
{
    /// <summary>当前运行程序的版本号（来自程序集）。</summary>
    public static string RunningVersion => LauncherRuntimeInfo.CurrentLauncherVersion;

    /// <summary>当前 RID，如 osx-arm64。</summary>
    public static string CurrentRid => LauncherRuntimeInfo.CurrentRid;

    /// <summary>文件名里用的「系统-架构」标签，如 mac-arm64 / win-amd64。</summary>
    public static string PlatformLabel => CurrentRid switch
    {
        "win-x64" => "win-amd64",
        "win-arm64" => "win-arm64",
        "osx-arm64" => "mac-arm64",
        "osx-x64" => "mac-x64",
        "linux-x64" => "linux-amd64",
        _ => CurrentRid
    };

    // ---------------------------------------------------------------- 检查

    /// <summary>
    /// 检查 Release latest 版本。返回 latest 版本号；若与当前运行版本相同返回 null。
    /// 只比版本号，不比 sha256。
    /// </summary>
    /// <summary>
    /// 检查启动器新版本：**两个源（Xhmcl / GitCode）的 Release 都要查**，
    /// 取版本号最高的那个作为最新版。任何一个源失败不影响另一个。
    /// </summary>
    public static async Task<string?> CheckAsync(CancellationToken ct = default)
        => await CheckAllAsync(null, ct);

    /// <summary>兼容旧调用：优先用给定 remote，同时仍会比对另一个源。</summary>
    public static async Task<string?> CheckAsync(IModelRemote remote, CancellationToken ct = default)
        => await CheckAllAsync(remote, ct);

    private static async Task<string?> CheckAllAsync(IModelRemote? preferred, CancellationToken ct)
    {
        var candidates = new List<(string Source, string Version)>();

        foreach (var preset in SourcePreset.All)
        {
            try
            {
                // 优先复用调用方已建好的 remote（避免重复建实例）
                var remote = preferred != null &&
                             SourcePreset.Detect(preferred.DisplayName).Id == preset.Id
                    ? preferred
                    : SourceSelector.CreateRemote(preset);

                var version = await remote.GetVersionAsync(ct);
                var v = (version?.Launcher.Version ?? "").Trim();
                if (v.Length == 0)
                {
                    Console.WriteLine($"[update] {preset.Name}: 没有版本信息（可能还没发 Release）");
                    continue;
                }
                Console.WriteLine($"[update] {preset.Name}: {v}");
                candidates.Add((preset.Name, v));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[update] {preset.Name} 检查失败: {ex.Message}");
            }
        }

        if (candidates.Count == 0) return null;

        // 取版本号最高的（第 7 项）
        var best = candidates.OrderByDescending(c => VersionUtil.Parse(c.Version)).First();
        Console.WriteLine($"[update] 最新版：{best.Version}（来自 {best.Source}）");

        var cmp = VersionUtil.Compare(best.Version, RunningVersion);

        // 远端版本更新 → 提示更新
        if (cmp > 0)
        {
            Console.WriteLine($"[update] 远端 {best.Version} > 本地 {RunningVersion}，提示更新");
            return best.Version;
        }

        // 远端版本更旧 → 不提示
        if (cmp < 0)
        {
            Console.WriteLine($"[update] 远端 {best.Version} < 本地 {RunningVersion}，不提示");
            return null;
        }

        // 版本号相同：比对当前程序的 sha256 与 Release 包
        //   相符   → 视为最新，不提示
        //   不相符 → 说明是重新编译的同版本程序，必须提示下载
        var same = await SameFingerprintAsync(best.Source, best.Version, ct);
        if (same == null)
        {
            Console.WriteLine("[update] 版本相同但无法比对 sha256，按最新处理");
            return null;
        }
        if (same.Value)
        {
            Console.WriteLine("[update] 版本相同且 sha256 一致，已是最新");
            return null;
        }

        // 版本相同但指纹不同：是自行编译的构建，不提示更新
        Console.WriteLine("[update] 版本相同但 sha256 不一致，不提示更新");
        return null;
    }
    /// <summary>
    /// 比对「当前运行程序」与「Release 平台包」的 sha256。
    /// 返回 true=一致（确属同一份构建）、false=不一致（需重新下载）、null=无法比对。
    /// </summary>
    private static async Task<bool?> SameFingerprintAsync(string sourceName, string version,
        CancellationToken ct)
    {
        try
        {
            var preset = SourcePreset.All.FirstOrDefault(p =>
                string.Equals(p.Name, sourceName, StringComparison.OrdinalIgnoreCase));
            if (preset == null) return null;

            var rid = CurrentRid;
            Console.WriteLine($"[update] 抓取 {preset.Name} 的 {rid} 发布包用于比对…");

            // 定位进程对应的可执行文件 / .app
            var self = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(self)) return null;
            var selfPath = self!;
            // 单文件 / dotnet 宿主时退回到程序集位置
            if (Path.GetFileNameWithoutExtension(selfPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                selfPath = typeof(LauncherUpdater).Assembly.Location;

            var selfHash = await HashFileAsync(selfPath, ct);
            Console.WriteLine($"[update] 本地程序 sha256 = {selfHash[..16]}…");

            // 下载 Release 对应平台包，取包内主程序再算
            var remote = SourceSelector.CreateRemote(preset);
            var v = await remote.GetVersionAsync(ct);
            if (v == null || !v.Launcher.Packages.TryGetValue(rid, out var pkg) || pkg == null)
                return null;

            var bytes = await remote.DownloadPackageAsync(pkg, ct);
            if (bytes == null || bytes.Length == 0) return null;

            var remoteHash = await HashZipPayloadAsync(bytes, ct);
            if (remoteHash == null) return null;
            Console.WriteLine($"[update] 远端包 sha256 = {remoteHash[..16]}…");

            return string.Equals(selfHash, remoteHash, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[update] sha256 比对失败: {ex.Message}");
            return null;
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        var hash = await System.Security.Cryptography.SHA256.HashDataAsync(fs, ct);
        return Convert.ToHexString(hash);
    }

    /// <summary>从发布包（zip）里取出主程序并算 sha256。</summary>
    private static async Task<string?> HashZipPayloadAsync(byte[] zip, CancellationToken ct)
    {
        try
        {
            using var ms = new MemoryStream(zip);
            using var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
            // 找主程序：app 内的可执行文件 / GC.dll / 单文件 exe
            var entry = archive.Entries.FirstOrDefault(e =>
                            e.Name.Equals("GC.dll", StringComparison.OrdinalIgnoreCase))
                        ?? archive.Entries.FirstOrDefault(e =>
                            e.Name.Equals("GC", StringComparison.OrdinalIgnoreCase))
                        ?? archive.Entries.FirstOrDefault(e =>
                            e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                            e.Name.StartsWith("GC", StringComparison.OrdinalIgnoreCase));
            if (entry == null) return null;

            await using var es = entry.Open();
            using var buf = new MemoryStream();
            await es.CopyToAsync(buf, ct);
            var hash = System.Security.Cryptography.SHA256.HashData(buf.ToArray());
            return Convert.ToHexString(hash);
        }
        catch { return null; }
    }

    /// <summary>
    /// 遍历所有源的 Release，取**版本号最高且包含本平台发布包**的那个。
    /// 某个源没有 Release（返回 null）时自动跳过。
    /// </summary>
    private static async Task<(RepoVersion Version, IModelRemote Remote)?> GetBestReleaseAsync(
        IModelRemote fallback, CancellationToken ct)
    {
        var rid = CurrentRid;
        var found = new List<(string Source, RepoVersion Version, IModelRemote Remote)>();

        foreach (var preset in SourcePreset.All)
        {
            try
            {
                var remote = SourceSelector.CreateRemote(preset);
                var v = await remote.GetVersionAsync(ct);
                if (v == null)
                {
                    Console.WriteLine($"[update] {preset.Name}: 无 Release");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(v.Launcher.Version) || v.Launcher.Packages.Count == 0)
                {
                    Console.WriteLine($"[update] {preset.Name}: 没有可用的启动器发布包");
                    continue;
                }
                var hasPkg = v.Launcher.Packages.ContainsKey(rid) ||
                             v.Launcher.Packages.ContainsKey("default");
                if (!hasPkg)
                {
                    Console.WriteLine($"[update] {preset.Name}: v{v.Launcher.Version} 缺少 {rid} 发布包");
                    continue;
                }
                Console.WriteLine($"[update] {preset.Name}: v{v.Launcher.Version} 可用");
                found.Add((preset.Name, v, remote));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[update] {preset.Name} 读取失败: {ex.Message}");
            }
        }

        if (found.Count == 0) return null;

        var best = found.OrderByDescending(f => VersionUtil.Parse(f.Version.Launcher.Version)).First();
        Console.WriteLine($"[update] 选中最新的可用发布包：v{best.Version.Launcher.Version}（{best.Source}）");
        return (best.Version, best.Remote);
    }

    // ---------------------------------------------------------------- 下载

    /// <summary>
    /// 下载 latest 版本到当前运行目录（与旧版并列）。
    /// 返回新版包名，供后续替换脚本使用。
    /// </summary>
    public static async Task<UpdateRunResult> DownloadAsync(
        AppPaths paths, IModelRemote remote,
        IProgress<UpdateProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            progress?.Report(new UpdateProgressInfo { Value = 0.05, Stage = "正在读取最新版本" });

            // 两个源都问一遍：某个源没发 Release 时自动换另一个，
            // 与「检查更新」保持同一套规则，避免检查通过、下载却报无法读取。
            var version = await remote.GetVersionAsync(ct);

            // 当前源没有可用的启动器版本（没发 Release / 版本号为空）→ 换另一个源。
            // 注意：模型清单可能仍然有效，但启动器升级必须有 Release。
            var hasUsable = version != null &&
                            !string.IsNullOrWhiteSpace(version.Launcher.Version) &&
                            version.Launcher.Packages.Count > 0;

            if (!hasUsable)
            {
                var tried = SourcePreset.Find(remote.SourceId)?.Name ?? remote.DisplayName;
                Console.WriteLine($"[update] {tried} 没有可用的启动器发布包，换另一个源");
                var pick = await GetBestReleaseAsync(remote, ct);
                if (pick == null) return Fail("两个源都没有可用的 Release 发布包");
                version = pick.Value.Version;
                remote = pick.Value.Remote;      // 后续下载必须用这个源的 remote
            }

            var latest = (version!.Launcher.Version ?? "").Trim();
            var rid = CurrentRid;
            if (!version.Launcher.Packages.TryGetValue(rid, out var pkg) &&
                !version.Launcher.Packages.TryGetValue("default", out pkg))
                return Fail($"该平台（{rid}）没有对应发布包");

            // ---- 信任闸门 ---------------------------------------------------
            // 启动器更新会替换可执行文件，风险最高：清单没签名就一律不动。
            // 只有显式设置 GC_ALLOW_UNSIGNED_MANIFEST=1（自建私有构建）才放行。
            var allowUnsigned =
                Environment.GetEnvironmentVariable("GC_ALLOW_UNSIGNED_MANIFEST") == "1";

            if (!version.IsTrusted && !allowUnsigned)
            {
                var reason = version.SignatureTrust == nameof(ManifestTrust.Invalid)
                    ? "清单签名无效"
                    : "清单未签名";
                Console.Error.WriteLine($"[update] 启动器更新被拒绝：{reason}");
                return Fail($"{reason}，已拒绝下载启动器更新");
            }

            if (string.IsNullOrWhiteSpace(pkg!.Sha256))
            {
                Console.Error.WriteLine("[update] 清单没有提供 sha256，拒绝下载启动器更新");
                return Fail("清单缺少该平台的 sha256，已拒绝下载（无法校验完整性）");
            }

            progress?.Report(new UpdateProgressInfo { Value = 0.20, Stage = $"正在下载 v{latest}" });

            var bytes = await remote.DownloadPackageAsync(pkg!, ct);
            if (bytes == null || bytes.Length == 0) return Fail("下载失败：发布包为空");

            // sha256 校验（清单已验签，这里的 sha256 就是"正版"的定义）
            {
                var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (!string.Equals(actual, pkg.Sha256!.Trim(), StringComparison.OrdinalIgnoreCase))
                    return Fail("下载文件校验失败（sha256 与已签名清单不符）");
            }

            progress?.Report(new UpdateProgressInfo { Value = 0.55, Stage = "正在校验更新包" });

            var updateRoot = paths.UpdateRoot;
            Directory.CreateDirectory(updateRoot);

            // 更新包留在 .GC/update，交给 SAVEDOG 自己解压 —— 不信任"预解压目录"，
            // 避免「解压后被替换」的中间窗口。
            var zip = Path.Combine(updateRoot, "package.zip");
            if (File.Exists(zip)) File.Delete(zip);
            await File.WriteAllBytesAsync(zip, bytes, ct);

            try
            {
                var inspection = SafeArchive.Inspect(zip);
                Console.WriteLine($"[update] 更新包检查通过：{inspection.Entries} 个条目，" +
                                  $"{inspection.UncompressedBytes / 1024 / 1024} MB");
            }
            catch (Exception ex)
            {
                try { File.Delete(zip); } catch { }
                return Fail("更新包安全性检查失败: " + ex.Message);
            }

            // 二次验签用的清单副本：SAVEDOG 不信任"启动器说它验过了"
            var rawManifest = await remote.GetRawVersionTextAsync(ct);
            var manifestCopy = Path.Combine(updateRoot, "version.json");
            if (!string.IsNullOrWhiteSpace(rawManifest))
                File.WriteAllText(manifestCopy, rawManifest!);
            else if (File.Exists(manifestCopy))
                File.Delete(manifestCopy);

            // 包名：GCL-<版本>-<平台标签>.app/.exe（SAVEDOG 找不到时会自行扫描包内顶层）。
            // Linux 的启动器没有扩展名，别硬加 .exe —— 那会让按包名查找永远落空。
            var packageName = $"GCL-{latest}-{PlatformLabel}" +
                              (OperatingSystem.IsMacOS() ? ".app"
                                  : OperatingSystem.IsWindows() ? ".exe"
                                  : "");

            var request = new SavedogRequest
            {
                Version = latest,
                Rid = rid,
                PlatformLabel = PlatformLabel,
                PackageName = packageName,
                PackageZip = "package.zip",
                PayloadDir = "payload",
                ManifestPath = "version.json",
                PackageSha256 = pkg.Sha256!.Trim().ToLowerInvariant(),
                PackageSize = bytes.LongLength,
                RequireSignature = version.IsTrusted,
                InstallRelative = Path.GetRelativePath(updateRoot, paths.InstallDirectory),
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            };
            Json.Write(Path.Combine(updateRoot, SavedogFiles.ApplyRequest), request);

            // 兼容兜底：没有 SAVEDOG 的旧安装，仍然按老方式预解压 + 脚本替换
            string? legacyPath = null;
            if (SavedogLauncher.Find(paths) == null)
            {
                Console.WriteLine("[update] 未找到 GC-SAVEDOG，使用兼容脚本流程");
                progress?.Report(new UpdateProgressInfo { Value = 0.7, Stage = "正在解压更新包" });

                var staging = Path.Combine(paths.CacheRoot, "launcher-staging");
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                Directory.CreateDirectory(staging);
                SafeArchive.ExtractToDirectory(zip, staging);
                FlattenStaging(staging);

                var payload = Path.Combine(updateRoot, "payload");
                if (Directory.Exists(payload)) Directory.Delete(payload, true);
                Directory.CreateDirectory(payload);
                var legacyName = MoveToPayload(staging, payload);
                try { Directory.Delete(staging, true); } catch { }
                if (legacyName == null) return Fail("解压结果里没有找到新版启动器");

                packageName = legacyName;
                request.PackageName = legacyName;
                Json.Write(Path.Combine(updateRoot, SavedogFiles.ApplyRequest), request);
                legacyPath = Path.Combine(payload, legacyName);

                // 老脚本读这个纯文本标记，只含相对信息
                File.WriteAllText(Path.Combine(updateRoot, "update.txt"),
                    $"NAME={legacyName}\nVERSION={latest}\nPLATFORM={PlatformLabel}\n" +
                    $"INSTALL_REL={request.InstallRelative}\n");
            }

            progress?.Report(new UpdateProgressInfo { Value = 1.0, Stage = "下载完成" });

            return new UpdateRunResult
            {
                State = UpdateRunState.Ready,
                Version = latest,
                Message = $"v{latest} 已暂存到 .GC/update",
                PackageName = packageName,
                NewAppPath = legacyPath
            };
        }
        catch (OperationCanceledException) { return Fail("已取消"); }
        catch (Exception ex) { return Fail(ex.Message); }
    }

    private static UpdateRunResult Fail(string msg) => new()
    {
        State = UpdateRunState.Failed,
        Message = msg
    };

    // ---------------------------------------------------------------- 应用（下次启动）

    /// <summary>
    /// 安排应用已下载的新版。
    ///   首选 <b>GC-SAVEDOG</b>：二次验签、原子替换、启动后健康检查、失败自动回滚；
    ///   找不到 SAVEDOG 时退回旧脚本流程（只用于从老版本升级的过渡）。
    /// 返回 true 表示调用方应立即退出进程。
    /// </summary>
    /// <param name="execute">只写脚本不执行（测试用，避免真的搬动文件）。</param>
    public static bool ScheduleApply(AppPaths paths, string packageName, bool execute = true)
    {
        // 安全阀：绝不在源码目录执行替换（防止误删源码）
        if (File.Exists(Path.Combine(paths.InstallDirectory, "GC.sln")))
        {
            Console.Error.WriteLine("源码版：新版已下载，但拒绝在源码目录执行替换");
            return false;
        }

        if (File.Exists(Path.Combine(paths.UpdateRoot, SavedogFiles.ApplyRequest)))
        {
            string? savedogError = null;
            if (SavedogLauncher.TryStart(paths, Environment.ProcessId, out savedogError))
                return true;
            Console.Error.WriteLine($"[update] {savedogError}，改用兼容脚本");
        }

        var marker = Path.Combine(paths.UpdateRoot, "update.txt");
        if (!File.Exists(marker)) return false;

        try
        {
            var isWin = OperatingSystem.IsWindows();
            var script = Path.Combine(paths.UpdateRoot, isWin ? "apply.bat" : "apply.sh");
            var content = isWin ? WindowsScript() : UnixScript();
            File.WriteAllText(script, content, new System.Text.UTF8Encoding(false));

            if (!isWin)
            {
                try
                {
                    File.SetUnixFileMode(script,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
                catch { }
            }

            if (!execute) return true;

            var psi = isWin
                ? new System.Diagnostics.ProcessStartInfo("cmd.exe")
                { UseShellExecute = false, CreateNoWindow = true }
                : new System.Diagnostics.ProcessStartInfo("/bin/sh")
                { UseShellExecute = false, CreateNoWindow = true };
            if (isWin) psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(script);
            System.Diagnostics.Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"启动替换脚本失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>启动时若有待应用更新：交给 SAVEDOG，由调用方退出进程。</summary>
    public static bool TryApplyPending(AppPaths paths)
    {
        if (paths.IsDevelopment) return false;
        if (Environment.GetEnvironmentVariable("EMS_SELFTEST") == "1") return false;
        if (Environment.GetEnvironmentVariable("EMS_NO_SELF_UPDATE") == "1") return false;

        // 新协议：apply-request.json + package.zip（SAVEDOG 自己解压 + 验签）
        var requestPath = Path.Combine(paths.UpdateRoot, SavedogFiles.ApplyRequest);
        if (File.Exists(requestPath))
        {
            var request = Json.Read<SavedogRequest>(requestPath);
            var zipPath = request == null
                ? null
                : Path.Combine(paths.UpdateRoot, request.PackageZip);

            if (request != null && zipPath != null && File.Exists(zipPath))
                return ScheduleApply(paths, request.PackageName);

            Console.Error.WriteLine("[update] 更新申请不完整，已清理");
            try { File.Delete(requestPath); } catch { }
        }

        // 旧协议：update.txt + payload（从老版本升级时仍可能遇到）
        var marker = Path.Combine(paths.UpdateRoot, "update.txt");
        if (!File.Exists(marker)) return false;

        var name = ReadMarker(marker, "NAME");
        if (string.IsNullOrWhiteSpace(name)) return false;
        var payloadPath = Path.Combine(paths.UpdateRoot, "payload", name!);
        if (!File.Exists(payloadPath) && !Directory.Exists(payloadPath))
            return false;   // 新版本不在了，放弃

        return ScheduleApply(paths, name!);
    }

    // ---------------------------------------------------------------- 脚本（全部相对路径）

    private static string UnixScript()
    {
        var s = new System.Text.StringBuilder();
        s.AppendLine("#!/bin/sh");
        s.AppendLine("# script lives in .GC/update/ ; install path is recorded relatively");
        s.AppendLine("HERE=$(CDPATH= cd -- \"$(dirname -- \"$0\")\" && pwd)");
        s.AppendLine("MARKER=\"$HERE/update.txt\"");
        s.AppendLine();
        s.AppendLine("# read marker");
        s.AppendLine("NEWNAME=$(sed -n 's/^NAME=//p' \"$MARKER\" 2>/dev/null | head -1)");
        s.AppendLine("NEWVER=$(sed -n 's/^VERSION=//p' \"$MARKER\" 2>/dev/null | head -1)");
        s.AppendLine("NEWPLAT=$(sed -n 's/^PLATFORM=//p' \"$MARKER\" 2>/dev/null | head -1)");
        s.AppendLine("INSTALLREL=$(sed -n 's/^INSTALL_REL=//p' \"$MARKER\" 2>/dev/null | head -1)");
        s.AppendLine("if [ -z \"$NEWNAME\" ] || [ -z \"$NEWVER\" ] || [ -z \"$INSTALLREL\" ]; then");
        s.AppendLine("  echo \"marker incomplete, abort\"; rm -f \"$HERE\"/*.sh 2>/dev/null; exit 1");
        s.AppendLine("fi");
        s.AppendLine("DST=$(CDPATH= cd -- \"$HERE/$INSTALLREL\" && pwd)");
        s.AppendLine("PAYLOAD=\"$HERE/payload/$NEWNAME\"");
        s.AppendLine("if [ ! -e \"$PAYLOAD\" ]; then");
        s.AppendLine("  echo \"new package missing, abort\"; rm -f \"$HERE\"/*.sh 2>/dev/null; exit 1");
        s.AppendLine("fi");
        s.AppendLine();
        s.AppendLine("# wait for old process to exit (this script is started by it)");
        s.AppendLine("sleep 2");
        s.AppendLine();
        s.AppendLine("# move the staged package into the install directory");
        s.AppendLine("NEW=\"$DST/$NEWNAME\"");
        s.AppendLine("rm -rf \"$NEW\"");
        s.AppendLine("mv \"$PAYLOAD\" \"$NEW\"");
        s.AppendLine();
        s.AppendLine("# launch new version");
        s.AppendLine("if [ -d \"$NEW\" ]; then open \"$NEW\" 2>/dev/null &");
        s.AppendLine("elif [ -x \"$NEW\" ]; then \"$NEW\" >/dev/null 2>&1 &");
        s.AppendLine("fi");
        s.AppendLine();
        s.AppendLine("# delete old GC-* files: SAME platform only, version != latest");
        s.AppendLine("# files of OTHER platforms are NEVER touched");
        // Linux 的启动器没有扩展名（GC-1.0.9-linux-amd64），只匹配 *.app/*.exe 会漏掉，
        // 结果是每次更新都在安装目录里多留一个旧版本。这里统一按 GC- 前缀扫，
        // 再用「版本号-平台」的形态兜底，避免误删同目录的其它文件。
        s.AppendLine("for item in \"$DST\"/GC-*; do");
        s.AppendLine("  [ -e \"$item\" ] || continue");
        s.AppendLine("  [ \"$item\" = \"$NEW\" ] && continue");
        s.AppendLine("  base=$(basename \"$item\")");
        s.AppendLine("  stem=${base#GC-}");
        s.AppendLine("  case \"$base\" in");
        s.AppendLine("    *.app) stem=${stem%.app} ;;");
        s.AppendLine("    *.exe) stem=${stem%.exe} ;;");
        s.AppendLine("  esac");
        s.AppendLine("  # 只认 <版本>-<系统>-<架构>，其它名字一律不动");
        s.AppendLine("  case \"$stem\" in");
        s.AppendLine("    [0-9]*-*-*) ;;");
        s.AppendLine("    *) continue ;;");
        s.AppendLine("  esac");
        s.AppendLine("  ver=${stem%%-*}");
        s.AppendLine("  rest=${stem#*-}");
        s.AppendLine("  plat=\"${rest%-*}-${rest##*-}\"");
        s.AppendLine("  # skip other platforms entirely");
        s.AppendLine("  [ \"$plat\" != \"$NEWPLAT\" ] && continue");
        s.AppendLine("  # same platform: keep only latest version");
        s.AppendLine("  if [ \"$ver\" = \"$NEWVER\" ]; then");
        s.AppendLine("    echo \"keep: $base\"");
        s.AppendLine("  else");
        s.AppendLine("    echo \"remove (version $ver != $NEWVER): $base\"");
        s.AppendLine("    rm -rf \"$item\" 2>/dev/null");
        s.AppendLine("  fi");
        s.AppendLine("done");
        s.AppendLine();
        s.AppendLine("rm -f \"$MARKER\"");
        s.AppendLine("rm -f \"$HERE\"/*.sh 2>/dev/null");
        return s.ToString();
    }

    private static string WindowsScript()
    {
        var s = new System.Text.StringBuilder();
        s.AppendLine("@echo off");
        s.AppendLine("chcp 65001 >nul 2>&1");
        s.AppendLine("set \"HERE=%~dp0\"");
        s.AppendLine("set \"MARKER=%~dp0update.txt\"");
        s.AppendLine("timeout /t 2 /nobreak >nul");
        s.AppendLine();
        s.AppendLine("set \"NEWNAME=\"");
        s.AppendLine("set \"NEWVER=\"");
        s.AppendLine("set \"NEWPLAT=\"");
        s.AppendLine("set \"INSTALLREL=\"");
        s.AppendLine("for /f \"tokens=2 delims==\" %%A in ('findstr /b \"NAME=\" \"%MARKER%\" 2^>nul') do set \"NEWNAME=%%A\"");
        s.AppendLine("for /f \"tokens=2 delims==\" %%A in ('findstr /b \"VERSION=\" \"%MARKER%\" 2^>nul') do set \"NEWVER=%%A\"");
        s.AppendLine("for /f \"tokens=2 delims==\" %%A in ('findstr /b \"PLATFORM=\" \"%MARKER%\" 2^>nul') do set \"NEWPLAT=%%A\"");
        s.AppendLine("for /f \"tokens=2 delims==\" %%A in ('findstr /b \"INSTALL_REL=\" \"%MARKER%\" 2^>nul') do set \"INSTALLREL=%%A\"");
        s.AppendLine("if \"%NEWNAME%\"==\"\" (echo marker incomplete, abort & del /Q \"%~f0\" >nul 2>&1 & exit /b 1)");
        s.AppendLine("if \"%INSTALLREL%\"==\"\" (echo install path missing, abort & del /Q \"%~f0\" >nul 2>&1 & exit /b 1)");
        s.AppendLine("for %%I in (\"%HERE%%INSTALLREL%\") do set \"DST=%%~fI\"");
        s.AppendLine("set \"PAYLOAD=%HERE%payload\\%NEWNAME%\"");
        s.AppendLine("if not exist \"%PAYLOAD%\" (echo new package missing, abort & del /Q \"%~f0\" >nul 2>&1 & exit /b 1)");
        s.AppendLine();
        s.AppendLine("rem move the staged package into the install directory");
        s.AppendLine("if exist \"%DST%\\%NEWNAME%\" rmdir /S /Q \"%DST%\\%NEWNAME%\" 2>nul");
        s.AppendLine("if exist \"%DST%\\%NEWNAME%\" del /Q \"%DST%\\%NEWNAME%\" 2>nul");
        s.AppendLine("move \"%PAYLOAD%\" \"%DST%\\%NEWNAME%\" >nul");
        s.AppendLine("if errorlevel 1 (echo failed to install update & exit /b 1)");
        s.AppendLine();
        s.AppendLine("rem launch new version");
        s.AppendLine("start \"\" \"%DST%\\%NEWNAME%\"");
        s.AppendLine();
        s.AppendLine("rem delete old GC-* files: SAME platform only, version != latest");
        s.AppendLine("powershell -NoProfile -ExecutionPolicy Bypass -Command \"^");
        s.AppendLine("$dst='%DST%'; $new='%NEWNAME%'; $ver='%NEWVER%'; $plat='%NEWPLAT%';^");
        s.AppendLine("Get-ChildItem -LiteralPath $dst -Filter 'GC-*' | ForEach-Object {^");
        s.AppendLine("  $n=$_.Name; if($n -eq $new){return};^");
        s.AppendLine("  $m=[regex]::Match($n,'^GC-(.+?)-([^-]+)-([^-]+)\\.(app|exe)$');^");
        s.AppendLine("  if(-not $m.Success){return};^");
        s.AppendLine("  $fver=$m.Groups[1].Value; $fplat=$m.Groups[2].Value+'-'+$m.Groups[3].Value;^");
        s.AppendLine("  if($fplat -ne $plat){return};^");
        s.AppendLine("  if($fver -eq $ver){ Write-Host \\\"keep: $n\\\" }^");
        s.AppendLine("  else { Write-Host \\\"remove: $n\\\"; Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }^");
        s.AppendLine("}\"");
        s.AppendLine();
        s.AppendLine("del /Q \"%MARKER%\" >nul 2>&1");
        s.AppendLine("del /Q \"%~f0\" >nul 2>&1");
        return s.ToString();
    }

    // ---------------------------------------------------------------- 内部工具

    /// <summary>展平只有一个顶层目录、且不是 .app 的 zip。</summary>
    private static void FlattenStaging(string dir)
    {
        var entries = Directory.GetFileSystemEntries(dir);
        if (entries.Length != 1 || !Directory.Exists(entries[0])) return;
        var inner = entries[0];
        if (Path.GetFileName(inner).EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return;

        var flat = dir + "-flat";
        if (Directory.Exists(flat)) Directory.Delete(flat, true);
        Directory.Move(inner, flat);
        Directory.Delete(dir, true);
        Directory.Move(flat, dir);
    }

    /// <summary>
    /// 把 staging 里的新版搬入下载目录的 payload。
    /// 返回新版包名（GC-&lt;版本&gt;-&lt;平台&gt;.app/.exe）。
    /// </summary>
    private static string? MoveToPayload(string staging, string payload)
    {
        try
        {
            foreach (var entry in Directory.GetFileSystemEntries(staging))
            {
                var name = Path.GetFileName(entry);
                if (string.Equals(name, ".GC", StringComparison.OrdinalIgnoreCase)) continue;
                var dest = Path.Combine(payload, name);

                if (Directory.Exists(dest)) Directory.Delete(dest, true);
                else if (File.Exists(dest)) File.Delete(dest);

                if (Directory.Exists(entry)) Directory.Move(entry, dest);
                else File.Move(entry, dest);

                // 保证 .app 里可执行文件可执行
                if (name.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && Directory.Exists(dest))
                {
                    var macOs = Path.Combine(dest, "Contents", "MacOS");
                    if (Directory.Exists(macOs))
                    {
                        try
                        {
                            foreach (var f in Directory.GetFiles(macOs, "*", SearchOption.TopDirectoryOnly))
                            {
                                var mode = File.GetUnixFileMode(f);
                                File.SetUnixFileMode(f, mode | UnixFileMode.UserExecute);
                            }
                        }
                        catch { }
                    }
                    return name;
                }
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return name;

                // Linux 的启动器没有扩展名（GC-1.0.9-linux-amd64），以前只认 .app/.exe，
                // 结果解压完判定「没找到新版启动器」，Linux 更新永远失败。
                // 注意不能用 Path.HasExtension：版本号 9.9.9 里的点会让它误判成有扩展名。
                if ((name.StartsWith("GC-", StringComparison.OrdinalIgnoreCase) ||
                     name.StartsWith("GCL-", StringComparison.OrdinalIgnoreCase)) &&
                    name.EndsWith("-" + PlatformLabel, StringComparison.OrdinalIgnoreCase))
                {
                    TryMarkExecutable(dest);
                    return name;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"安装新版失败: {ex.Message}");
        }
        return null;
    }

    /// <summary>给 Linux/macOS 上的可执行文件补上执行位（zip 不保留权限）。</summary>
    private static void TryMarkExecutable(string path)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(path)) return;
        try
        {
            var mode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(path, mode | UnixFileMode.UserExecute |
                                       UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
        catch
        {
            // 权限设置失败不影响后续流程，用户可手动 chmod
        }
    }

    private static string? ReadMarker(string file, string key)
    {
        try
        {
            foreach (var line in File.ReadAllLines(file))
            {
                if (line.StartsWith(key + "=", StringComparison.Ordinal))
                    return line[(key.Length + 1)..].Trim();
            }
        }
        catch { }
        return null;
    }

    private static string Safe(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '-');
        return value.Replace('/', '-').Replace(' ', '-');
    }
}

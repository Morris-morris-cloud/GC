using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Avalonia.Controls;
using GC.Models;
using GC.ModelSdk;

namespace GC.Services;

/// <summary>
/// 模型宿主：按清单加载模型自己的程序集，拿到模型自带的界面。
/// 启动器对“场景 / 行星 / 节气”一无所知，只认契约。
/// </summary>
public sealed class ModelHost
{
    public const int SupportedApiVersion = 1;

    private readonly AppPaths _paths;
    private readonly SettingsService _settings;
    private readonly MemoryBudget _memory;

    public ModelHost(AppPaths paths, SettingsService settings, MemoryBudget memory)
    {
        _paths = paths;
        _settings = settings;
        _memory = memory;
    }

    /// <summary>同步加载（全部在 UI 线程，用于自检 / 命令行）。</summary>
    public LoadedModel Load(ModelPackage package, Action requestClose, Action<double, string?>? progress = null)
    {
        var session = CreateSession(package, requestClose, progress);
        try
        {
            session.Runtime.Prepare(session.Context);
            var view = session.Runtime.CreateView(session.Context)
                       ?? throw new InvalidOperationException("模型没有返回界面");
            return session.Complete(view);
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 异步加载：数据准备放在后台线程（<see cref="IModelRuntime.Prepare"/>），
    /// 界面构建回到 UI 线程，进度通过 <paramref name="progress"/> 实时汇报。
    /// </summary>
    public async Task<LoadedModel> LoadAsync(ModelPackage package, Action requestClose,
        Action<double, string?>? progress = null)
    {
        var session = CreateSession(package, requestClose, progress);
        try
        {
            await Task.Run(() => session.Runtime.Prepare(session.Context)).ConfigureAwait(true);
            var view = session.Runtime.CreateView(session.Context)
                       ?? throw new InvalidOperationException("模型没有返回界面");
            return session.Complete(view);
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private Session CreateSession(ModelPackage package, Action requestClose, Action<double, string?>? progress)
    {
        // ---- 契约协商 -------------------------------------------------------
        // 先做廉价检查：不满足就直接拒绝，避免加载了才发现不兼容。
        var declaredApi = package.Manifest.ApiVersion;
        if (declaredApi < 1)
            throw new InvalidOperationException($"模型清单的 apiVersion 非法: {declaredApi}");

        if (declaredApi > SupportedApiVersion)
            throw new InvalidOperationException(
                $"该模型需要更新的启动器（模型契约 v{declaredApi}，当前支持 v{SupportedApiVersion}）。" +
                "请先更新启动器，或让模型作者发布兼容版本。");

        var minApp = package.Manifest.MinAppVersion;
        if (!string.IsNullOrWhiteSpace(minApp) &&
            CompareVersions(LauncherRuntimeInfo.CurrentLauncherVersion, minApp!) < 0)
            throw new InvalidOperationException(
                $"该模型要求启动器 ≥ v{minApp}，当前为 v{LauncherRuntimeInfo.CurrentLauncherVersion}");

        // ---- 完整性：dll 会被直接执行，加载前必须确认它没被改过 ----------------
        var integrity = ModelIntegrity.VerifyForLoad(package.Directory, package.Id);
        if (!integrity.Ok)
        {
            var message = $"模型完整性校验失败（{package.Id}）：{integrity.Message}。" +
                          "请删除该模型目录让启动器重新下载。";
            if (Environment.GetEnvironmentVariable("GC_ALLOW_TAMPERED_MODELS") == "1")
                Console.Error.WriteLine("[warn] " + message + "（已按 GC_ALLOW_TAMPERED_MODELS 放行）");
            else
                throw new InvalidOperationException(message);
        }
        else if (!integrity.Present && Environment.GetEnvironmentVariable("GC_STRICT_MODELS") == "1")
        {
            throw new InvalidOperationException(
                $"模型缺少完整性记录（{package.Id}），严格模式下拒绝加载");
        }

        var assemblyPath = package.AssemblyPath;
        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException($"模型缺少功能程序集：{assemblyPath}", assemblyPath);

        var directory = Path.GetDirectoryName(assemblyPath)!;
        var context = new ModelLoadContext(directory);
        try
        {
            var assembly = context.LoadFromAssemblyPath(assemblyPath);
            var type = ResolveFactoryType(assembly, package.Manifest.Type);
            if (type == null)
                throw new InvalidOperationException("模型程序集里没有找到 IModelFactory 实现");

            if (Activator.CreateInstance(type) is not IModelFactory factory)
                throw new InvalidOperationException($"无法创建模型入口：{type.FullName}");

            if (factory.ApiVersion != SupportedApiVersion)
                throw new InvalidOperationException(
                    $"模型程序集的契约版本为 v{factory.ApiVersion}，启动器支持 v{SupportedApiVersion}；" +
                    "请更新到匹配的版本");

            var runtime = factory.Create();
            var modelContext = new ModelContext(package, L.I.Language, _memory.BudgetMb, requestClose, progress);
            return new Session(context, runtime, modelContext);
        }
        catch
        {
            context.Unload();
            throw;
        }
    }

    private static Type? ResolveFactoryType(Assembly assembly, string? typeName)
    {
        if (!string.IsNullOrWhiteSpace(typeName))
        {
            var exact = assembly.GetType(typeName!, throwOnError: false);
            if (exact != null) return exact;
        }

        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface) continue;
            if (typeof(IModelFactory).IsAssignableFrom(type)) return type;
        }
        return null;
    }

    /// <summary>比较 1.0.10 与 1.0.9 这类版本号（按数字段比较，不按字符串）。</summary>
    private static int CompareVersions(string left, string right)
    {
        static int[] Parts(string value)
        {
            var text = value.Trim().TrimStart('v', 'V');
            var pieces = text.Split('.');
            var numbers = new int[Math.Max(4, pieces.Length)];
            for (var i = 0; i < pieces.Length; i++)
            {
                var digits = new string(Array.FindAll(pieces[i].ToCharArray(), char.IsDigit));
                if (digits.Length > 6) digits = digits[..6];
                numbers[i] = int.TryParse(digits, out var n) ? n : 0;
            }
            return numbers;
        }

        var a = Parts(left);
        var b = Parts(right);
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return x < y ? -1 : 1;
        }
        return 0;
    }

    private sealed class Session : IDisposable
    {
        private readonly ModelLoadContext _context;

        public Session(ModelLoadContext context, IModelRuntime runtime, ModelContext modelContext)
        {
            _context = context;
            Runtime = runtime;
            Context = modelContext;
        }

        public IModelRuntime Runtime { get; }
        public ModelContext Context { get; }

        public LoadedModel Complete(Control view) => new(_context, Runtime, view);

        public void Dispose()
        {
            try { Runtime.Dispose(); } catch (Exception ex) { Console.Error.WriteLine($"释放模型失败: {ex.Message}"); }
            try { _context.Unload(); } catch (Exception ex) { Console.Error.WriteLine($"卸载模型程序集失败: {ex.Message}"); }
        }
    }
}

/// <summary>一次性模型实例：窗口关闭后释放视图、卸载程序集。</summary>
public sealed class LoadedModel : IDisposable
{
    private readonly ModelLoadContext _context;
    private readonly IModelRuntime _runtime;
    private bool _disposed;

    public LoadedModel(ModelLoadContext context, IModelRuntime runtime, Control view)
    {
        _context = context;
        _runtime = runtime;
        View = view;
    }

    public Control View { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // 顺序：先释放视图（停计时器/Dispose 位图），再释放运行时，最后卸载程序集。
        // 否则视图里的事件订阅会继续持有 ALC 内的类型，collectible 上下文无法回收。
        try { (View as IDisposable)?.Dispose(); }
        catch (Exception ex) { Console.Error.WriteLine($"释放模型视图失败: {ex.Message}"); }
        try { _runtime.Dispose(); } catch (Exception ex) { Console.Error.WriteLine($"释放模型失败: {ex.Message}"); }
        try { _context.Unload(); } catch (Exception ex) { Console.Error.WriteLine($"卸载模型程序集失败: {ex.Message}"); }
    }
}

/// <summary>模型程序集加载上下文：契约与 Avalonia 一律回到默认上下文，保证类型一致。</summary>
public sealed class ModelLoadContext : AssemblyLoadContext
{
    private static readonly HashSet<string> Shared = new(StringComparer.OrdinalIgnoreCase)
    {
        "GC.ModelSdk",
        "Avalonia",
        "Avalonia.Base",
        "Avalonia.Controls",
        "Avalonia.Desktop",
        "Avalonia.Skia",
        "Avalonia.Themes.Fluent",
        "SkiaSharp",
        "HarfBuzzSharp"
    };

    private readonly string _directory;

    public ModelLoadContext(string directory) : base("model:" + Path.GetFileName(directory), isCollectible: true)
        => _directory = directory;

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (string.IsNullOrEmpty(name)) return null;
        if (Shared.Contains(name)) return null;                       // 用启动器已加载的那份
        if (name.StartsWith("System.", StringComparison.OrdinalIgnoreCase)) return null;
        if (name is "System" or "netstandard" or "mscorlib") return null;

        var candidate = Path.Combine(_directory, name + ".dll");
        return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
    }
}

/// <summary>启动器提供给模型的环境。</summary>
public sealed class ModelContext : IModelContext
{
    private readonly Action _requestClose;
    private readonly Action<double, string?>? _progress;

    public ModelContext(ModelPackage package, string language, int memoryMb, Action requestClose,
        Action<double, string?>? progress)
    {
        _progress = progress;
        ModelId = package.Id;
        ModelDirectory = package.Directory;
        DataDirectory = package.DataDirectory;
        Language = language;
        MemoryBudgetMb = memoryMb;
        _requestClose = requestClose;
    }

    public string ModelId { get; }
    public string ModelDirectory { get; }
    public string DataDirectory { get; }
    public string Language { get; }
    public int MemoryBudgetMb { get; }

    public void RequestClose() => _requestClose();

    public void ReportProgress(double value, string? message = null) => _progress?.Invoke(value, message);

    public void Log(string message) => Console.WriteLine(message);
}

using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EarthModelStudio.Services;
using EarthModelStudio.ViewModels;
using EarthModelStudio.Views;

namespace EarthModelStudio;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = AppServices.Create();

            // 诊断模式：只验证模型程序集能否加载（发布包里也用它做冒烟测试）
            if (Environment.GetEnvironmentVariable("EMS_SELFTEST") == "1")
            {
                var code = SelfTest(services);
                Avalonia.Threading.Dispatcher.UIThread.Post(
                    () => desktop.Shutdown(code), Avalonia.Threading.DispatcherPriority.Background);
                return;
            }

            var vm = new MainViewModel(services);
            var window = new MainWindow { DataContext = vm };

            vm.ConfirmHandler = async message =>
            {
                var dialog = new ConfirmWindow(message);
                return await dialog.ShowDialog<bool>(window);
            };

            // 发现启动器新版本：弹窗询问，用户选「更新」才下载并安排替换
            vm.LauncherUpdateHandler = async newVersion =>
            {
                var dialog = new ConfirmWindow(
                    title: "发现新版本",
                    message: $"启动器有新版本 v{newVersion}，是否立即更新？\n\n" +
                             "更新会下载新版程序，下次启动时自动替换当前版本；" +
                             "选择取消则继续使用当前版本，但模型仍会更新到最新。",
                    confirmText: "更新",
                    cancelText: "取消");
                return await dialog.ShowDialog<bool>(window);
            };

            desktop.MainWindow = window;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            // 主窗口已建好，后台检查模型 / 启动器更新，不阻塞界面
            if (Environment.GetEnvironmentVariable("EMS_NO_UPDATE") != "1")
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(
                    () => _ = vm.RunStartupUpdateAsync(),
                    Avalonia.Threading.DispatcherPriority.Background);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>逐个加载模型，打印结果；返回退出码（0 = 全部成功）。</summary>
    private static int SelfTest(AppServices services)
    {
        var packages = services.Models.Scan();
        Console.WriteLine($"模型库: {services.Paths.ModelRoot}");
        Console.WriteLine($"模型数: {packages.Count}");
        var failed = 0;
        foreach (var package in packages)
        {
            try
            {
                using var loaded = services.ModelHost.Load(package, () => { });
                Console.WriteLine($"OK   {package.Id} -> {loaded.View.GetType().FullName}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"FAIL {package.Id}: {ex.Message}");
            }
        }
        return failed == 0 ? 0 : 1;
    }
}

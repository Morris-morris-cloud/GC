using System;
using System.Runtime.InteropServices;

namespace GC;

/// <summary>
/// macOS Dock 图标控制。
///
/// 有 Flutter 前端时，启动器（GC.app 的主进程）只承担后端宿主，界面全在 GC-UI.app 里。
/// 两个 .app 各自带 Dock 图标 → 用户会看到两个图标。
///
/// 三重保险，任意一条生效就够了：
///   1. 打包时 Info.plist 写 <c>LSUIElement=true</c>（buildsh/lib/package.sh）；
///   2. 运行期 <c>TransformProcessType</c> 转成 UIElement（Carbon 老 API，但对已启动的
///      进程最可靠，不依赖 LaunchServices 缓存）；
///   3. 运行期 <c>[NSApp setActivationPolicy:accessory]</c>。
/// 只用其中一条都出现过「某些机器上仍是两个图标」，所以三条一起上。
/// </summary>
internal static class MacDock
{
    private const string Objc = "/usr/lib/libobjc.dylib";
    private const string Carbon =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    // NSApplicationActivationPolicy: Regular=0, Accessory=1, Prohibited=2
    private const long ActivationPolicyAccessory = 1;

    // ProcessApplicationTransformState: kProcessTransformToUIElementApplication = 4
    private const int TransformToUIElement = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessSerialNumber
    {
        public uint HighLongOfPSN;
        public uint LowLongOfPSN;
    }

    [DllImport(Objc)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(Objc)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_int(IntPtr receiver, IntPtr selector, long arg);

    [DllImport(Carbon)]
    private static extern int GetCurrentProcess(out ProcessSerialNumber psn);

    [DllImport(Carbon)]
    private static extern int TransformProcessType(ref ProcessSerialNumber psn, int transformState);

    /// <summary>把当前进程从 Dock 隐藏掉（仅 macOS，其它平台空操作）。</summary>
    public static void HideFromDock()
    {
        if (!OperatingSystem.IsMacOS()) return;

        // 2. Carbon：对「已经在运行」的进程最直接
        try
        {
            if (GetCurrentProcess(out var psn) == 0)
                TransformProcessType(ref psn, TransformToUIElement);
        }
        catch
        {
            // 旧系统 / 沙箱下可能不可用，继续尝试下一条
        }

        // 3. AppKit：需要先有 NSApplication 实例
        try
        {
            var cls = objc_getClass("NSApplication");
            if (cls == IntPtr.Zero) return;

            var app = objc_msgSend(cls, sel_registerName("sharedApplication"));
            if (app == IntPtr.Zero) return;

            objc_msgSend_int(app, sel_registerName("setActivationPolicy:"), ActivationPolicyAccessory);
        }
        catch
        {
            // 无窗口/沙箱环境下 NSApplication 不可用，忽略即可：
            // 最多是多一个 Dock 图标，不影响功能。
        }
    }
}

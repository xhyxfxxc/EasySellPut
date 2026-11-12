using System.Runtime.InteropServices;

namespace Meili.Mac.Platform;

/// <summary>
/// The few AppKit / Foundation calls Avalonia does not wrap, through the Objective-C runtime. Called on the main (UI)
/// thread and only on macOS.
/// </summary>
internal static class MacNative
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    /// <summary>NSActivityUserInitiatedAllowingIdleSystemSleep: keeps timers on schedule (no App Nap) but lets the Mac sleep.</summary>
    private const ulong UserInitiatedAllowingIdleSystemSleep = 0x00FFFFFFUL & ~(1UL << 20);
    private const long NSInformationalRequest = 10;

    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, ulong options, IntPtr reason);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern long Send(IntPtr receiver, IntPtr selector, long arg);

    private static IntPtr Sel(string name) => sel_registerName(name);
    private static IntPtr SharedApp => Send(objc_getClass("NSApplication"), Sel("sharedApplication"));

    /// <summary>
    /// Opts out of App Nap while price alerts are monitored: a napping app's timers are coalesced and delayed by
    /// minutes when its window is hidden or covered. Returns the retained activity token for <see cref="EndActivity"/>.
    /// </summary>
    public static IntPtr BeginActivity(string reason)
    {
        var info = Send(objc_getClass("NSProcessInfo"), Sel("processInfo"));
        var token = Send(info, Sel("beginActivityWithOptions:reason:"), UserInitiatedAllowingIdleSystemSleep, NSString(reason));
        return Send(token, Sel("retain"));
    }

    public static void EndActivity(IntPtr token)
    {
        var info = Send(objc_getClass("NSProcessInfo"), Sel("processInfo"));
        Send(info, Sel("endActivity:"), token);
        Send(token, Sel("release"));
    }

    /// <summary>Bounces the Dock icon once (informational) when the app is not frontmost.</summary>
    public static void RequestAttention() => Send(SharedApp, Sel("requestUserAttention:"), NSInformationalRequest);

    /// <summary>⌘H: hides the whole app like every Mac app.</summary>
    public static void HideApp() => Send(SharedApp, Sel("hide:"), IntPtr.Zero);

    private static IntPtr NSString(string value)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try { return Send(objc_getClass("NSString"), Sel("stringWithUTF8String:"), utf8); }
        finally { Marshal.FreeCoTaskMem(utf8); }
    }
}

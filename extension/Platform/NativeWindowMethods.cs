using Godot;
using System;
using System.Runtime.InteropServices;

namespace GDEWebView;

/// <summary>
/// 跨平台原生窗口操作方法。
/// Win32: SetWindowPos / SetFocus
/// macOS: setFrame: / makeFirstResponder (ObjC)
/// Linux: XMoveResizeWindow / XSetInputFocus (X11)
/// </summary>
internal static class NativeWindowMethods
{
    internal static void MovePlatformWindow(IntPtr handle, int x, int y, int width, int height)
    {
        if (OperatingSystem.IsMacOS())
            MacSetViewFrame(handle, x, y, width, height);
        else if (OperatingSystem.IsLinux())
            LinuxMoveResizeWindow(handle, x, y, width, height);
        else
            Win32SetWindowPos(handle, HWND_TOP, x, y, width, height,
                SWP_NOZORDER | SWP_NOACTIVATE);
    }

    internal static void SetPlatformFocus(IntPtr window)
    {
        if (OperatingSystem.IsLinux())
            LinuxSetInputFocus(window);
        else if (OperatingSystem.IsMacOS())
            MacMakeFirstResponder(window);
        else
            Win32SetFocus(window);
    }

    // ═══════════════════════════════════════════════
    //  Win32
    // ═══════════════════════════════════════════════

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr Win32SetFocus(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool Win32SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal static readonly IntPtr HWND_TOP = IntPtr.Zero;

    // ═══════════════════════════════════════════════
    //  macOS (ObjC 运行时)
    // ═══════════════════════════════════════════════

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr sel_registerName(string name);

    private static void MacSetViewFrame(IntPtr nsView, int x, int y, int width, int height)
    {
        IntPtr sel = sel_registerName("setFrame:");
        // NSRect = 4 doubles (x, y, width, height) = 32 bytes
        byte[] rect = new byte[32];
        BitConverter.GetBytes((double)x).CopyTo(rect, 0);
        BitConverter.GetBytes((double)y).CopyTo(rect, 8);
        BitConverter.GetBytes((double)width).CopyTo(rect, 16);
        BitConverter.GetBytes((double)height).CopyTo(rect, 24);
        objc_msgSend(nsView, sel);
    }

    private static void MacMakeFirstResponder(IntPtr window)
    {
        IntPtr sel = sel_registerName("makeFirstResponder:");
        objc_msgSend(window, sel);
    }

    // ═══════════════════════════════════════════════
    //  Linux (X11)
    // ═══════════════════════════════════════════════

    [DllImport("libX11.so.6")]
    private static extern int XMoveResizeWindow(IntPtr display, IntPtr window, int x, int y, int width, int height);

    [DllImport("libX11.so.6")]
    private static extern int XSetInputFocus(IntPtr display, IntPtr window, int revertTo, IntPtr time);

    private static IntPtr _x11Display;
    private static bool _x11Initialized;

    internal static void SetX11Display(IntPtr display)
    {
        _x11Display = display;
        _x11Initialized = true;
    }

    private static void LinuxMoveResizeWindow(IntPtr window, int x, int y, int width, int height)
    {
        if (_x11Initialized)
            XMoveResizeWindow(_x11Display, window, x, y, width, height);
    }

    private static void LinuxSetInputFocus(IntPtr window)
    {
        if (_x11Initialized)
            XSetInputFocus(_x11Display, window, 1, IntPtr.Zero);
    }
}
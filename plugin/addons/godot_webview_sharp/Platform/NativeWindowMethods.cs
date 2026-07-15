using Godot;
using System;
using System.Runtime.InteropServices;

namespace WebViewSharp
{
    /// <summary>
    /// 跨平台原生窗口操作方法。
    /// 参考 GDCefGlue 的 NativeWindowMethods + NativeWebView 的平台抽象。
    /// </summary>
    internal static class NativeWindowMethods
    {
        /// <summary>
        /// 移动并调整窗口位置和大小。
        /// Win32: SetWindowPos | macOS: setFrame: | Linux: XMoveResizeWindow
        /// </summary>
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

        /// <summary>
        /// 将键盘焦点设置到指定窗口。
        /// Win32: SetFocus | macOS: makeFirstResponder | Linux: XSetInputFocus
        /// </summary>
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
        //  macOS (通过 ObjC 运行时)
        // ═══════════════════════════════════════════════

        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern IntPtr sel_registerName(string name);

        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern IntPtr objc_getClass(string name);

        private static void MacSetViewFrame(IntPtr nsView, int x, int y, int width, int height)
        {
            // [nsView setFrame:NSMakeRect(x, y, width, height)]
            IntPtr sel = sel_registerName("setFrame:");
            // NSRect 在 macOS 64-bit 上是 32 bytes (4 doubles)
            byte[] rect = new byte[32];
            BitConverter.GetBytes((double)x).CopyTo(rect, 0);
            BitConverter.GetBytes((double)y).CopyTo(rect, 8);
            BitConverter.GetBytes((double)width).CopyTo(rect, 16);
            BitConverter.GetBytes((double)height).CopyTo(rect, 24);
            objc_msgSend(nsView, sel);
        }

        private static void MacMakeFirstResponder(IntPtr window)
        {
            // [[window contentView] makeFirstResponder:webview]
            // 简化：调用 window makeFirstResponder
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
                XSetInputFocus(_x11Display, window, 1, IntPtr.Zero); // RevertToParent
        }
    }
}
#if MACOS
using Godot;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace GDEWebView;

/// <summary>
/// macOS WKWebView 后端实现 (GDExtension 版本)。
/// 使用 ObjC 运行时直接操作 WKWebView。
/// </summary>
internal class MacWebViewBackend : IWebViewBackend
{
    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_getClass(string n);
    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr sel_registerName(string n);
    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_msgSend(IntPtr r, IntPtr s);
    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern void objc_msgSend_v1(IntPtr r, IntPtr s, IntPtr a);

    private IntPtr _wv, _parent;
    private WebViewControl _host;
    private int _lx, _ly, _lw, _lh;

    public event Action<string> NavigationStarting, WebMessageReceived, SourceChanged;
    public event Action<bool, int> NavigationCompleted;
    public bool IsCreated => _wv != IntPtr.Zero;
    public bool CanGoBack => false;
    public bool CanGoForward => false;

    public async Task CreateAsync(Control host)
    {
        _host = host as WebViewControl;
        await Task.CompletedTask;
        try
        {
            _parent = new IntPtr(DisplayServer.Singleton.WindowGetNativeHandle(
                DisplayServer.HandleType.WindowView, 0));

            var alloc = objc_msgSend(objc_getClass("WKWebView"), sel_registerName("alloc"));
            _wv = objc_msgSend(alloc, sel_registerName("init"));

            if (_wv != IntPtr.Zero)
            {
                objc_msgSend_v1(_parent, sel_registerName("addSubview:"), _wv);
                GD.Print("[GDEWebView] WKWebView created");
            }
        }
        catch (Exception ex) { GD.PrintErr("[GDEWebView] " + ex.Message); }
    }

    public void SetBounds(int x, int y, int w, int h)
    {
        _lx = x; _ly = y; _lw = w; _lh = h;
        var sel = sel_registerName("setFrame:");
        byte[] rect = new byte[32];
        BitConverter.GetBytes((double)x).CopyTo(rect, 0);
        BitConverter.GetBytes((double)y).CopyTo(rect, 8);
        BitConverter.GetBytes((double)w).CopyTo(rect, 16);
        BitConverter.GetBytes((double)h).CopyTo(rect, 24);
        objc_msgSend(_wv, sel);
    }

    public void Navigate(string url) { }
    public void NavigateToString(string html) { }
    public void Reload() { }
    public void GoBack() { }
    public void GoForward() { }
    public void ExecuteScript(string script) { }
    public void SetZoomFactor(double factor) { }
    public void Print() { }
    public void OpenDevTools() { }
    public void CloseDevTools() { }
    public bool IsDevToolsOpen => false;
    public void ClearBrowsingData() { }
    public void Focus() { }
    public void FocusParent() { }
    public void SetVisible(bool visible) { }
    public void SetTransparent(bool transparent) { }

    public void Dispose() { _wv = IntPtr.Zero; }
}
#endif
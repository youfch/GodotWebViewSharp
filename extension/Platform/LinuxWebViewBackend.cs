#if LINUX
using Godot;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace GDEWebView;

/// <summary>
/// Linux WebKitGTK 后端实现 (GDExtension 版本)。
/// 使用 WebKitGTK 嵌入视图 + X11 Socket 机制。
/// </summary>
internal class LinuxWebViewBackend : IWebViewBackend
{
    [DllImport("libgtk-3.so.0")]
    private static extern bool gtk_init_check(IntPtr a, IntPtr b);
    [DllImport("libgtk-3.so.0")]
    private static extern IntPtr gtk_window_new(int t);
    [DllImport("libgtk-3.so.0")]
    private static extern void gtk_widget_show(IntPtr w);
    [DllImport("libgtk-3.so.0")]
    private static extern void gtk_widget_set_size_request(IntPtr w, int x, int y);
    [DllImport("libgtk-3.so.0")]
    private static extern void gtk_container_add(IntPtr c, IntPtr w);
    [DllImport("libgtk-3.so.0")]
    private static extern IntPtr gtk_socket_new();
    [DllImport("libgtk-3.so.0")]
    private static extern IntPtr gtk_widget_get_window(IntPtr w);
    [DllImport("libgtk-3.so.0")]
    private static extern bool gtk_events_pending();
    [DllImport("libgtk-3.so.0")]
    private static extern void gtk_main_iteration_do(bool b);
    [DllImport("libgdk-3.so.0")]
    private static extern IntPtr gdk_x11_window_get_xid(IntPtr w);
    [DllImport("libwebkit2gtk-4.1.so.0")]
    private static extern IntPtr webkit_web_view_new();
    [DllImport("libwebkit2gtk-4.1.so.0")]
    private static extern void webkit_web_view_load_uri(IntPtr w, string u);

    private IntPtr _wv, _gtkWin, _socket;
    private WebViewControl _host;
    private bool _gtkInit;
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
            if (!_gtkInit) { gtk_init_check(IntPtr.Zero, IntPtr.Zero); _gtkInit = true; }
            _wv = webkit_web_view_new();
            _gtkWin = gtk_window_new(0);
            _socket = gtk_socket_new();
            gtk_container_add(_gtkWin, _socket);
            gtk_widget_show(_socket);
            gtk_widget_show(_gtkWin);

            // 获取 X11 Socket ID 并嵌入到 Godot 窗口
            var win = gtk_widget_get_window(_socket);
            var xid = gdk_x11_window_get_xid(win);
            GD.Print($"[GDEWebView] WebKitGTK created, XID={xid}");
        }
        catch (Exception ex) { GD.PrintErr("[GDEWebView] " + ex.Message); }
    }

    public void PumpGtkEvents()
    {
        while (gtk_events_pending())
            gtk_main_iteration_do(false);
    }

    public void SetBounds(int x, int y, int w, int h)
    {
        _lx = x; _ly = y; _lw = w; _lh = h;
        gtk_widget_set_size_request(_gtkWin, w, h);
    }

    public void Navigate(string url) => webkit_web_view_load_uri(_wv, url);
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

    public void Dispose() { _wv = IntPtr.Zero; _gtkWin = IntPtr.Zero; }
}
#endif
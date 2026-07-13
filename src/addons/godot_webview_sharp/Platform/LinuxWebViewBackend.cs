#if LINUX
using Godot;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace WebViewSharp
{
    internal class LinuxWebViewBackend : IWebViewBackend
    {
        [DllImport("libgtk-3.so.0")] private static extern bool gtk_init_check(IntPtr a, IntPtr b);
        [DllImport("libgtk-3.so.0")] private static extern IntPtr gtk_window_new(int t);
        [DllImport("libgtk-3.so.0")] private static extern void gtk_widget_show(IntPtr w);
        [DllImport("libgtk-3.so.0")] private static extern void gtk_widget_set_size_request(IntPtr w, int x, int y);
        [DllImport("libgtk-3.so.0")] private static extern void gtk_container_add(IntPtr c, IntPtr w);
        [DllImport("libgtk-3.so.0")] private static extern IntPtr gtk_socket_new();
        [DllImport("libgtk-3.so.0")] private static extern IntPtr gtk_widget_get_window(IntPtr w);
        [DllImport("libgtk-3.so.0")] private static extern bool gtk_events_pending();
        [DllImport("libgtk-3.so.0")] private static extern void gtk_main_iteration_do(bool b);
        [DllImport("libgdk-3.so.0")] private static extern IntPtr gdk_x11_window_get_xid(IntPtr w);
        [DllImport("libwebkit2gtk-4.1.so.0")] private static extern IntPtr webkit_web_view_new();
        [DllImport("libwebkit2gtk-4.1.so.0")] private static extern void webkit_web_view_load_uri(IntPtr w, string u);

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
            try
            {
                if (!_gtkInit) { gtk_init_check(IntPtr.Zero, IntPtr.Zero); _gtkInit = true; }
                _wv = webkit_web_view_new();
                _gtkWin = gtk_window_new(0);
                _socket = gtk_socket_new();
                gtk_container_add(_gtkWin, _socket);
                gtk_widget_show(_socket);
                gtk_widget_show(_gtkWin);
                GD.Print("[WebViewSharp] WebKitGTK created");
            }
            catch (Exception ex) { GD.PrintErr("[WebViewSharp] " + ex.Message); }
        }

        public void PumpGtkEvents() { while (gtk_events_pending()) gtk_main_iteration_do(false); }
        public void SetBounds(int x, int y, int w, int h) { _lx = x; _ly = y; _lw = w; _lh = h; }
        public void Navigate(string u) { if (_wv != IntPtr.Zero) webkit_web_view_load_uri(_wv, u); }
        public void NavigateToString(string h) { }
        public void Reload() { }
        public void GoBack() { }
        public void GoForward() { }
        public void ExecuteScript(string s) { }
        public void OpenDevTools() { }
        public void CloseDevTools() { }
        public void ClearBrowsingData() { }
        public void Focus() { }
        public void SetVisible(bool v) { }
        public void SetTransparent(bool t) { }
        public void Dispose() { }
    }
}
#endif
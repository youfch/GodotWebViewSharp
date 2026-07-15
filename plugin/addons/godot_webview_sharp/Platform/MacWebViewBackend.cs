#if MACOS || MAC
using Godot;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace WebViewSharp
{
    internal class MacWebViewBackend : IWebViewBackend
    {
        [DllImport("/usr/lib/libobjc.dylib")] private static extern IntPtr objc_getClass(string n);
        [DllImport("/usr/lib/libobjc.dylib")] private static extern IntPtr sel_registerName(string n);
        [DllImport("/usr/lib/libobjc.dylib")] private static extern IntPtr objc_msgSend(IntPtr r, IntPtr s);
        [DllImport("/usr/lib/libobjc.dylib")] private static extern IntPtr objc_msgSend_1(IntPtr r, IntPtr s, IntPtr a);
        [DllImport("/usr/lib/libobjc.dylib")] private static extern void objc_msgSend_v(IntPtr r, IntPtr s);
        [DllImport("/usr/lib/libobjc.dylib")] private static extern void objc_msgSend_v1(IntPtr r, IntPtr s, IntPtr a);

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
            try
            {
                _parent = new IntPtr(DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowView, 0));
                var c = objc_msgSend(objc_getClass("WKWebViewConfiguration"), sel_registerName("new"));
                var alloc = objc_msgSend(objc_getClass("WKWebView"), sel_registerName("alloc"));
                _wv = objc_msgSend(alloc, sel_registerName("init"));
                if (_wv != IntPtr.Zero)
                {
                    objc_msgSend_v1(_parent, sel_registerName("addSubview:"), _wv);
                    GD.Print("[WebViewSharp] WKWebView created");
                }
            }
            catch (Exception ex) { GD.PrintErr("[WebViewSharp] " + ex.Message); }
        }

        public void SetBounds(int x, int y, int w, int h) { _lx = x; _ly = y; _lw = w; _lh = h; }
        public void Navigate(string url) { }
        public void NavigateToString(string html) { }
        public void Reload() { }
        public void GoBack() { }
        public void GoForward() { }
        public void ExecuteScript(string s) { }
        public void OpenDevTools() { GD.Print("[WebViewSharp] Use Safari Web Inspector"); }
        public void CloseDevTools() { }
        public void ClearBrowsingData() { }
        public void Focus() { }
        public void SetVisible(bool v) { }
        public void SetTransparent(bool t) { }
        public void Dispose() { }
    }
}
#endif
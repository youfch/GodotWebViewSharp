using Godot;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace GDEWebView;

/// <summary>
/// Windows WebView2 后端实现 (GDExtension 版本)。
/// 使用系统 Edge Chromium WebView2 嵌入窗口模式渲染。
///
/// 参考:
/// - GodotWebViewSharp WindowsWebViewBackend.cs (addon 版本)
/// - GDCefGlue 的 EmbeddedWindow 模式 + CefGlueControl.Embedded.cs
/// - godot_wry: GetWindowLong/SetWindowLong 去除 WS_CLIPCHILDREN
/// </summary>
internal class WindowsWebViewBackend : IWebViewBackend
{
    // ── Win32 ──
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string lpszWindow);

    private const int GWL_STYLE = -16;
    private const int WS_CLIPCHILDREN = 0x02000000;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_TOP = IntPtr.Zero;

    // ── WebView2 ──
    private CoreWebView2Controller _controller;
    private CoreWebView2Environment _environment;
    private IntPtr _parentHwnd;
    private IntPtr _childHwnd;
    private WebViewControl _host;

    // ── 事件 ──
    public event Action<string> NavigationStarting;
    public event Action<bool, int> NavigationCompleted;
    public event Action<string> WebMessageReceived;
    public event Action<string> SourceChanged;

    public bool IsCreated => _controller != null;
    public bool CanGoBack => _controller?.CoreWebView2?.CanGoBack ?? false;
    public bool CanGoForward => _controller?.CoreWebView2?.CanGoForward ?? false;

    // ── 输入转发 JS 脚本 ──
    private const string InputForwardingScript = @"
(function() {
    if (window.__gdeWebViewInputForwardingInstalled) return;
    window.__gdeWebViewInputForwardingInstalled = true;

    function postToGodot(data) {
        window.chrome.webview.postMessage(JSON.stringify(data));
    }

    document.addEventListener('mousemove', (e) => {
        if (!document.hasFocus()) return;
        postToGodot({type:'_mouse_move',x:e.clientX,y:e.clientY,movementX:e.movementX,movementY:e.movementY});
    });
    document.addEventListener('mousedown', (e) => {
        if (!document.hasFocus()) return;
        postToGodot({type:'_mouse_down',x:e.clientX,y:e.clientY,button:e.button});
    });
    document.addEventListener('mouseup', (e) => {
        if (!document.hasFocus()) return;
        postToGodot({type:'_mouse_up',x:e.clientX,y:e.clientY,button:e.button});
    });
    document.addEventListener('wheel', (e) => {
        if (!document.hasFocus()) return;
        postToGodot({type:'_mouse_wheel',x:e.clientX,y:e.clientY,deltaX:e.deltaX,deltaY:e.deltaY});
    });
    document.addEventListener('keydown', (e) => {
        if (!document.hasFocus()) return;
        const mod = ['Alt','Shift','Control','Meta'].includes(e.key);
        postToGodot({type:'_key_down',key:e.key,code:e.code,keyCode:e.keyCode,
            shift:mod?false:e.shiftKey,ctrl:mod?false:e.ctrlKey,alt:mod?false:e.altKey});
    });
    document.addEventListener('keyup', (e) => {
        if (!document.hasFocus()) return;
        const mod = ['Alt','Shift','Control','Meta'].includes(e.key);
        postToGodot({type:'_key_up',key:e.key,code:e.code,keyCode:e.keyCode,
            shift:mod?false:e.shiftKey,ctrl:mod?false:e.ctrlKey,alt:mod?false:e.altKey});
    });
})();";

    public async Task CreateAsync(Control hostControl)
    {
        _host = hostControl as WebViewControl;

        try
        {
            // 1. 获取 Godot 窗口 HWND
            _parentHwnd = GetGodotWindowHandle();
            if (_parentHwnd == IntPtr.Zero)
            {
                GD.PrintErr("[GDEWebView] Failed to get Godot window handle");
                return;
            }

            // 2. 移除 WS_CLIPCHILDREN，否则透明背景不工作
            int currentStyle = GetWindowLong(_parentHwnd, GWL_STYLE);
            if ((currentStyle & WS_CLIPCHILDREN) != 0)
            {
                SetWindowLong(_parentHwnd, GWL_STYLE, currentStyle & ~WS_CLIPCHILDREN);
            }

            // 3. 创建 WebView2 环境
            var envOptions = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = "--allow-file-access-from-files --disable-web-security"
            };

            string userDataDir = GetUserDataDirectory();
            _environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataDir,
                options: envOptions
            );

            // 4. 创建 WebView2 控制器（嵌入到 Godot 窗口）
            _controller = await _environment.CreateCoreWebView2ControllerAsync(_parentHwnd);

            if (_controller == null)
            {
                GD.PrintErr("[GDEWebView] Failed to create WebView2 controller");
                return;
            }

            // 5. 配置
            ConfigureWebView2();

            // 6. 将 WebView2 子窗口挂载到 Godot 窗口
            _childHwnd = FindWebView2ChildHwnd();
            if (_childHwnd != IntPtr.Zero)
            {
                SetParent(_childHwnd, _parentHwnd);
            }

            // 7. 注入输入转发脚本
            if (_host?.ForwardInputEvents == true)
            {
                await _controller.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(InputForwardingScript);
            }

            // 8. 注入 WRY 兼容层
            await _controller.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                (function() {
                    if (!window.ipc) window.ipc = {};
                    if (!window.ipc.postMessage) {
                        window.ipc.postMessage = function(msg) {
                            try {
                                var wv = window.chrome && window.chrome.webview;
                                if (wv && wv.postMessage) {
                                    wv.postMessage(typeof msg === 'string' ? msg : JSON.stringify(msg));
                                }
                            } catch(e) {}
                        };
                    }
                })();
            ");
            _controller.CoreWebView2.Settings.IsWebMessageEnabled = true;

            // 9. 设置虚拟主机映射
            try
            {
                string projectPath = ProjectSettings.Singleton.GlobalizePath("res://");
                _controller.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "godot.local", projectPath,
                    CoreWebView2HostResourceAccessKind.Allow);
                GD.Print($"[GDEWebView] Virtual host mapping: godot.local -> {projectPath}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[GDEWebView] Virtual host mapping failed: {ex.Message}");
            }

            // 10. 连接事件
            _controller.CoreWebView2.NavigationStarting += OnNavigationStarting;
            _controller.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            _controller.CoreWebView2.SourceChanged += OnSourceChanged;
            _controller.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            // 11. 透明背景
            if (_host?.Transparent == true)
                SetTransparent(true);

            GD.Print("[GDEWebView] WebView2 created");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GDEWebView] WebView2 creation failed: {ex.Message}");
            Cleanup();
        }
    }

    private void ConfigureWebView2()
    {
        if (_controller?.CoreWebView2 == null) return;

        var settings = _controller.CoreWebView2.Settings;
        settings.IsWebMessageEnabled = true;
        settings.AreDevToolsEnabled = _host?.DevToolsEnabled ?? true;
        settings.IsZoomControlEnabled = false;
        settings.IsScriptEnabled = true;
        settings.AreDefaultContextMenusEnabled = true;

        _controller.IsVisible = true;

        // 设置默认背景色
        var bgColor = _host?.BackgroundColor ?? Colors.White;
        _controller.DefaultBackgroundColor = System.Drawing.Color.FromArgb(
            (int)(bgColor.A * 255),
            (int)(bgColor.R * 255),
            (int)(bgColor.G * 255),
            (int)(bgColor.B * 255));
    }

    // ═══════════════════════════════════════════════
    //  位置同步
    // ═══════════════════════════════════════════════

    public void SetBounds(int x, int y, int width, int height)
    {
        if (_controller == null) return;

        _controller.Bounds = new System.Drawing.Rectangle(x, y, width, height);

        if (_childHwnd != IntPtr.Zero)
        {
            SetWindowPos(_childHwnd, HWND_TOP, x, y, width, height, SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }

    // ═══════════════════════════════════════════════
    //  导航
    // ═══════════════════════════════════════════════

    public void Navigate(string url) => _controller?.CoreWebView2?.Navigate(url);
    public void NavigateToString(string html) => _controller?.CoreWebView2?.NavigateToString(html);
    public void Reload() => _controller?.CoreWebView2?.Reload();
    public void GoBack() => _controller?.CoreWebView2?.GoBack();
    public void GoForward() => _controller?.CoreWebView2?.GoForward();

    // ═══════════════════════════════════════════════
    //  JS 执行
    // ═══════════════════════════════════════════════

    public void ExecuteScript(string script)
    {
        _ = _controller?.CoreWebView2?.ExecuteScriptAsync(script);
    }

    // ═══════════════════════════════════════════════
    //  DevTools
    // ═══════════════════════════════════════════════

    public void OpenDevTools() => _controller?.CoreWebView2?.OpenDevToolsWindow();
    public void CloseDevTools()
    {
        ExecuteScript("if (window.__gdeWebViewDevTools) window.__gdeWebViewDevTools.close();");
    }

    // ═══════════════════════════════════════════════
    //  数据 / 焦点 / 可见性
    // ═══════════════════════════════════════════════

    public void ClearBrowsingData()
    {
        _ = _controller?.CoreWebView2?.CallDevToolsProtocolMethodAsync(
            "Storage.clearDataForOrigin",
            "{\"origin\":\"*\",\"storageTypes\":\"all\"}");
    }

    public void Focus() => _controller?.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);

    public void SetVisible(bool visible)
    {
        if (_controller != null)
            _controller.IsVisible = visible;
    }

    public void SetTransparent(bool transparent)
    {
        if (_controller == null) return;

        if (transparent)
        {
            _controller.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            try
            {
                DisplayServer.Singleton.WindowSetFlag(DisplayServer.WindowFlags.Transparent, true, 0);
                GD.Print("[GDEWebView] Godot window transparent flag enabled");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[GDEWebView] Failed to set window transparent: {ex.Message}");
            }
        }
        else
        {
            _controller.DefaultBackgroundColor = System.Drawing.Color.White;
        }
    }

    // ═══════════════════════════════════════════════
    //  事件处理
    // ═══════════════════════════════════════════════

    private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
    {
        NavigationStarting?.Invoke(e.Uri);
    }

    private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        NavigationCompleted?.Invoke(e.IsSuccess, (int)e.HttpStatusCode);
    }

    private void OnSourceChanged(object sender, CoreWebView2SourceChangedEventArgs e)
    {
        SourceChanged?.Invoke(_controller?.CoreWebView2?.Source ?? "");
    }

    private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string message = e.TryGetWebMessageAsString();
        if (!string.IsNullOrEmpty(message))
        {
            WebMessageReceived?.Invoke(message);
        }
    }

    // ═══════════════════════════════════════════════
    //  辅助方法
    // ═══════════════════════════════════════════════

    private IntPtr GetGodotWindowHandle()
    {
        try
        {
            long handle = DisplayServer.Singleton.WindowGetNativeHandle(
                DisplayServer.HandleType.WindowHandle, 0);
            return new IntPtr(handle);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GDEWebView] Failed to get window handle: {ex.Message}");
            return IntPtr.Zero;
        }
    }

    private IntPtr FindWebView2ChildHwnd()
    {
        const string chromeWidgetClass = "Chrome_WidgetWin_0";
        IntPtr hwnd = FindWindowEx(_parentHwnd, IntPtr.Zero, chromeWidgetClass, null);

        if (hwnd == IntPtr.Zero)
        {
            hwnd = FindWindowEx(_parentHwnd, IntPtr.Zero, "Static", null);
        }

        return hwnd;
    }

    private string GetUserDataDirectory()
    {
        if (_host != null && !string.IsNullOrEmpty(_host.DataDirectory))
        {
            string dir = _host.DataDirectory;
            if (dir.StartsWith("user://"))
            {
                string path = dir.Substring(7).Replace("\\", "/");
                string globalPath = ProjectSettings.Singleton.GlobalizePath($"user://{path}");
                Directory.CreateDirectory(globalPath);
                return globalPath;
            }
            return dir;
        }

        string defaultDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GDEWebView"
        );
        Directory.CreateDirectory(defaultDir);
        return defaultDir;
    }

    // ═══════════════════════════════════════════════
    //  清理 / 释放
    // ═══════════════════════════════════════════════

    private void Cleanup()
    {
        if (_controller != null)
        {
            if (_controller.CoreWebView2 != null)
            {
                _controller.CoreWebView2.NavigationStarting -= OnNavigationStarting;
                _controller.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
                _controller.CoreWebView2.SourceChanged -= OnSourceChanged;
                _controller.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            }
            _controller.Close();
            _controller = null;
        }
        _environment = null;
        _childHwnd = IntPtr.Zero;
    }

    public void Dispose()
    {
        Cleanup();
    }
}
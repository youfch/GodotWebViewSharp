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
    private bool _devToolsOpen;

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
            //    不再整体关闭 web 安全（原来带的 --allow-file-access-from-files --disable-web-security
            //    已移除）：res:// 资源由 WebResourceServer 用 Godot 的 FileAccess 读，走 https://godot.local/，
            //    ES Module / fetch 都不会被 CORS 拦截，无需牺牲安全性。
            var envOptions = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = BuildBrowserArguments(),
            };

            string userDataDir = GetUserDataDirectory();

            _environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataDir,
                options: envOptions
            );

            // 4. 创建 WebView2 控制器（无痕模式需要 controller options）
            if (_host?.Incognito == true)
            {
                var controllerOptions = _environment.CreateCoreWebView2ControllerOptions();
                controllerOptions.IsInPrivateModeEnabled = true;
                _controller = await _environment.CreateCoreWebView2ControllerAsync(_parentHwnd, controllerOptions);
            }
            else
            {
                _controller = await _environment.CreateCoreWebView2ControllerAsync(_parentHwnd);
            }

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

            // 9. 资源服务：拦截 https://godot.local/*（res://）与 https://godot-user.local/*（user://）
            //    走自定义响应而不是 SetVirtualHostNameToFolderMapping —— 后者映射的是文件系统目录，
            //    导出成 PCK 后 res:// 资源读不到；这里经 Godot 的 FileAccess 读，PCK 内也能读
            WebResourceServer.Install(_controller.CoreWebView2);
            _controller.CoreWebView2.WebResourceRequested += OnWebResourceRequested;
            GD.Print($"[GDEWebView] Resource server: https://{WebResourceServer.ResHost}/ -> res://, " +
                     $"https://{WebResourceServer.UserHost}/ -> user://");

            // 10. 连接事件
            _controller.CoreWebView2.NavigationStarting += OnNavigationStarting;
            _controller.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            _controller.CoreWebView2.SourceChanged += OnSourceChanged;
            _controller.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            _controller.CoreWebView2.PermissionRequested += OnPermissionRequested;

            // 11. 透明背景
            if (_host?.Transparent == true)
                SetTransparent(true);

            GD.Print($"[GDEWebView] WebView2 created " +
                     $"(incognito={_host?.Incognito == true}, autoplay={_host?.Autoplay == true}, " +
                     $"userAgent={(string.IsNullOrEmpty(_host?.UserAgent) ? "(default)" : _host.UserAgent)}, " +
                     $"browserArgs=[{BuildBrowserArguments()}])");
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

        // 自定义 User-Agent（原来声明了属性但从未生效）
        if (_host != null && !string.IsNullOrEmpty(_host.UserAgent))
            settings.UserAgent = _host.UserAgent;

        _controller.IsVisible = true;

        // 设置默认背景色
        Color bgColor = _host?.BackgroundColor ?? NamedColors.White;
        _controller.DefaultBackgroundColor = System.Drawing.Color.FromArgb(
            (int)(bgColor.A * 255),
            (int)(bgColor.R * 255),
            (int)(bgColor.G * 255),
            (int)(bgColor.B * 255));
    }

    /// <summary>
    /// 组装 Chromium 命令行参数。只在需要时加，默认不加任何降低安全性的开关。
    /// </summary>
    private string BuildBrowserArguments()
    {
        var args = new System.Collections.Generic.List<string>();

        // 自动播放媒体（对标 godot_wry 的 autoplay）
        if (_host?.Autoplay == true)
            args.Add("--autoplay-policy=no-user-gesture-required");

        return string.Join(" ", args);
    }

    /// <summary>
    /// 权限请求：剪贴板读取直接放行（extension 侧暂无 ClipboardEnabled 开关，先对齐"默认可用"）。
    /// </summary>
    private void OnPermissionRequested(object sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        if (e.PermissionKind == CoreWebView2PermissionKind.ClipboardRead)
            e.State = CoreWebView2PermissionState.Allow;
    }

    /// <summary>
    /// 资源请求拦截：只接管我们自己的虚拟主机，其余不设置响应，交给 WebView2 正常走网络。
    /// </summary>
    private void OnWebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            var response = WebResourceServer.TryBuildResponse(_environment, e);
            if (response != null)
                e.Response = response;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GDEWebView] Resource request failed for {e.Request.Uri}: {ex.Message}");
        }
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
    //  缩放
    // ═══════════════════════════════════════════════

    public void SetZoomFactor(double factor)
    {
        if (_controller != null)
            _controller.ZoomFactor = factor;
    }

    // ═══════════════════════════════════════════════
    //  打印
    // ═══════════════════════════════════════════════

    public void Print()
    {
        _controller?.CoreWebView2?.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
    }

    // ═══════════════════════════════════════════════
    //  DevTools
    // ═══════════════════════════════════════════════

    public void OpenDevTools()
    {
        if (_controller?.CoreWebView2 == null) return;

        _controller.CoreWebView2.OpenDevToolsWindow();
        _devToolsOpen = true;
    }

    public void CloseDevTools()
    {
        if (_controller?.CoreWebView2 == null) return;

        // WebView2 没有"关闭 DevTools"的公开 API，这里只维护状态标记
        // （用户在开发者工具窗口里手动关掉的话，这个标记不会自动更新）
        _devToolsOpen = false;
    }

    public bool IsDevToolsOpen => _devToolsOpen;

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

        /// <summary>
        /// 把焦点从 webview 子窗口切回 Godot 父窗口（对标 godot_wry 的 focus_parent()）。
        /// 点击 webview 区域外的 Godot 场景时需要主动抢回焦点，否则键盘事件会一直留在网页里。
        /// </summary>
        public void FocusParent()
        {
            if (_parentHwnd != IntPtr.Zero)
                NativeWindowMethods.SetPlatformFocus(_parentHwnd);
        }

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
                _controller.CoreWebView2.PermissionRequested -= OnPermissionRequested;
                _controller.CoreWebView2.WebResourceRequested -= OnWebResourceRequested;
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
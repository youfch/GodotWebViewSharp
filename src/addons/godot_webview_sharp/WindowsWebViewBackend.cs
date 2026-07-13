using Godot;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace WebViewSharp
{
    /// <summary>
    /// Windows WebView2 后端实现。
    /// 使用系统 Edge Chromium WebView2，与 godot_wry 在 Windows 上使用的 WRY 底层相同。
    ///
    /// 参考：
    /// - godot_wry: GetWindowLong/SetWindowLong 去除 WS_CLIPCHILDREN
    /// - 604Assembly WebView2Edge.cs: CoreWebView2Controller 初始化 + SetParent
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

        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int WS_CLIPCHILDREN = 0x02000000;
        private const int WS_CHILD = 0x40000000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private static readonly IntPtr HWND_TOP = IntPtr.Zero;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

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

        // ── JS 注入脚本（输入转发，对标 godot_wry 的 forward_input_events） ──
        private const string InputForwardingScript = @"
(function() {
    if (window.__webviewSharpInputForwardingInstalled) return;
    window.__webviewSharpInputForwardingInstalled = true;

    function postToGodot(data) {
        window.chrome.webview.postMessage(JSON.stringify(data));
    }

    document.addEventListener('mousemove', (e) => {
        if (!document.hasFocus()) return;
        postToGodot({
            type: '_mouse_move',
            x: e.clientX, y: e.clientY,
            movementX: e.movementX, movementY: e.movementY,
            button: e.button
        });
    });

    document.addEventListener('mousedown', (e) => {
        if (!document.hasFocus()) return;
        postToGodot({
            type: '_mouse_down', x: e.clientX, y: e.clientY, button: e.button
        });
    });

    document.addEventListener('mouseup', (e) => {
        if (!document.hasFocus()) return;
        postToGodot({
            type: '_mouse_up', x: e.clientX, y: e.clientY, button: e.button
        });
    });

    document.addEventListener('wheel', (e) => {
        if (!document.hasFocus()) return;
        postToGodot({
            type: '_mouse_wheel', x: e.clientX, y: e.clientY,
            deltaX: e.deltaX, deltaY: e.deltaY,
            shift: e.shiftKey, ctrl: e.ctrlKey, alt: e.altKey, meta: e.metaKey
        });
    });

    document.addEventListener('keydown', (e) => {
        if (!document.hasFocus()) return;
        const isModifier = ['Alt', 'Shift', 'Control', 'Meta'].includes(e.key);
        postToGodot({
            type: '_key_down', key: e.key, code: e.code, keyCode: e.keyCode,
            shift: isModifier ? false : e.shiftKey,
            ctrl: isModifier ? false : e.ctrlKey,
            alt: isModifier ? false : e.altKey,
            meta: isModifier ? false : e.metaKey
        });
    });

    document.addEventListener('keyup', (e) => {
        if (!document.hasFocus()) return;
        const isModifier = ['Alt', 'Shift', 'Control', 'Meta'].includes(e.key);
        postToGodot({
            type: '_key_up', key: e.key, code: e.code, keyCode: e.keyCode,
            shift: isModifier ? false : e.shiftKey,
            ctrl: isModifier ? false : e.ctrlKey,
            alt: isModifier ? false : e.altKey,
            meta: isModifier ? false : e.metaKey
        });
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
                    GD.PrintErr("[WebViewSharp] Failed to get Godot window handle");
                    return;
                }

                // 2. 移除 WS_CLIPCHILDREN（参考 godot_wry），否则透明背景不工作
                int currentStyle = GetWindowLong(_parentHwnd, GWL_STYLE);
                if ((currentStyle & WS_CLIPCHILDREN) != 0)
                {
                    SetWindowLong(_parentHwnd, GWL_STYLE, currentStyle & ~WS_CLIPCHILDREN);
                }

// 3. 创建 WebView2 环境
            var envOptions = new CoreWebView2EnvironmentOptions
            {
                // 允许 file:// 加载 ES Module
                AdditionalBrowserArguments = "--allow-file-access-from-files --disable-web-security"
            };

            string userDataDir = GetUserDataDirectory();

                _environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataDir,
                    options: envOptions
                );

                // 4. 创建 WebView2 控制器
                _controller = await _environment.CreateCoreWebView2ControllerAsync(_parentHwnd);

                if (_controller == null)
                {
                    GD.PrintErr("[WebViewSharp] Failed to create WebView2 controller");
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
                    _controller.CoreWebView2.Settings.IsWebMessageEnabled = true;
                }

                // 8. 设置初始背景
                _controller.DefaultBackgroundColor = System.Drawing.Color.White;

                // 9. 连接事件
                _controller.CoreWebView2.NavigationStarting += OnNavigationStarting;
                _controller.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
                _controller.CoreWebView2.SourceChanged += OnSourceChanged;
                _controller.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

                // 10. 设置虚拟主机映射：godot.local → Godot 项目根目录
                // 这样 ES Module 通过 HTTPS 加载，不被 CORS 拦截
                try
                {
                    string projectPath = ProjectSettings.GlobalizePath("res://");
                    _controller.CoreWebView2.SetVirtualHostNameToFolderMapping(
                        "godot.local", projectPath,
                        CoreWebView2HostResourceAccessKind.Allow);
                    GD.Print($"[WebViewSharp] Virtual host mapping: godot.local -> {projectPath}");
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[WebViewSharp] Virtual host mapping failed: {ex.Message}");
                }

                // 11. 透明背景（对标 godot_wry 的 transparent 属性）
                if (_host?.Transparent == true)
                    SetTransparent(true);

                GD.Print($"[WebViewSharp] WebView2 created, parent HWND=0x{_parentHwnd.ToInt64():X8}, child HWND=0x{_childHwnd.ToInt64():X8}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[WebViewSharp] WebView2 creation failed: {ex.Message}");
                Cleanup();
            }
        }

        private void ConfigureWebView2()
        {
            if (_controller?.CoreWebView2 == null) return;

            var settings = _controller.CoreWebView2.Settings;

            // 应用导出属性配置
            settings.AreDefaultContextMenusEnabled = true;
            settings.IsStatusBarEnabled = true;
            settings.IsWebMessageEnabled = true;

            if (_host != null)
            {
                settings.AreDevToolsEnabled = _host.DevToolsEnabled;
                settings.IsZoomControlEnabled = _host.ZoomHotkeysEnabled;
                settings.IsScriptEnabled = true;
            }

            _controller.IsVisible = true;
        }

        // ═══════════════════════════════════════════════
        //  位置同步
        // ═══════════════════════════════════════════════

        public void SetBounds(int x, int y, int width, int height)
        {
            if (_controller == null) return;

            // 更新 WebView2 控制器边界
            _controller.Bounds = new System.Drawing.Rectangle(x, y, width, height);

            // 同步子窗口位置
            if (_childHwnd != IntPtr.Zero)
            {
                SetWindowPos(_childHwnd, HWND_TOP, x, y, width, height, SWP_NOZORDER | SWP_NOACTIVATE);
            }
        }

        // ═══════════════════════════════════════════════
        //  导航
        // ═══════════════════════════════════════════════

        public void Navigate(string url)
        {
            _controller?.CoreWebView2?.Navigate(url);
        }

        public void NavigateToString(string html)
        {
            _controller?.CoreWebView2?.NavigateToString(html);
        }

        public void Reload()
        {
            _controller?.CoreWebView2?.Reload();
        }

        public void GoBack()
        {
            _controller?.CoreWebView2?.GoBack();
        }

        public void GoForward()
        {
            _controller?.CoreWebView2?.GoForward();
        }

        // ═══════════════════════════════════════════════
        //  JS 执行
        // ═══════════════════════════════════════════════

        public void ExecuteScript(string script)
        {
            _controller?.CoreWebView2?.ExecuteScriptAsync(script);
        }

        // ═══════════════════════════════════════════════
        //  DevTools
        // ═══════════════════════════════════════════════

        public void OpenDevTools()
        {
            _controller?.CoreWebView2?.OpenDevToolsWindow();
        }

        public void CloseDevTools()
        {
            // WebView2 没有直接的关闭 DevTools API，可通过 ExecuteScript 实现
            ExecuteScript("if (window.__webviewDevTools) window.__webviewDevTools.close();");
        }

        // ═══════════════════════════════════════════════
        //  数据 / 焦点 / 可见性
        // ═══════════════════════════════════════════════

        public void ClearBrowsingData()
        {
            _controller?.CoreWebView2?.CallDevToolsProtocolMethodAsync("Storage.clearDataForOrigin",
                "{\"origin\":\"*\",\"storageTypes\":\"all\"}");
        }

        public void Focus()
        {
            _controller?.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
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
                // WebView2 背景透明（参考 604Assembly WebView2Edge.cs）
                _controller.DefaultBackgroundColor = System.Drawing.Color.Transparent;

                // 开启 Godot 窗口透明，使 WebView 透明区域穿透显示 3D 场景
                // 注意：必须在 WebView2 创建成功后调用，否则有冲突
                try
                {
                    DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Transparent, true, 0);
                    GD.Print("[WebViewSharp] Godot window transparent flag enabled");
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[WebViewSharp] Failed to set window transparent: {ex.Message}");
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
                long handle = DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, 0);
                return new IntPtr(handle);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[WebViewSharp] Failed to get window handle: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        private IntPtr FindWebView2ChildHwnd()
        {
            // WebView2 默认创建名为 "Chrome_WidgetWin_0" 的子窗口
            const string chromeWidgetClass = "Chrome_WidgetWin_0";
            IntPtr hwnd = FindWindowEx(_parentHwnd, IntPtr.Zero, chromeWidgetClass, null);

            if (hwnd == IntPtr.Zero)
            {
                // 重试：有些 WebView2 版本使用不同的窗口类
                hwnd = FindWindowEx(_parentHwnd, IntPtr.Zero, "Static", null);
            }

            return hwnd;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string lpszWindow);

        private string GetUserDataDirectory()
        {
            if (_host != null && !string.IsNullOrEmpty(_host.DataDirectory))
            {
                string dir = _host.DataDirectory;
                if (dir.StartsWith("user://"))
                {
                    string path = dir.Substring(7).Replace("\\", "/");
                    string globalPath = ProjectSettings.GlobalizePath($"user://{path}");
                    Directory.CreateDirectory(globalPath);
                    return globalPath;
                }
                return dir;
            }

            string defaultDir = global::System.IO.Path.Combine(
                global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.LocalApplicationData),
                "GodotWebViewSharp"
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
}
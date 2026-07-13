using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WebViewSharp
{
    /// <summary>
    /// Godot WRY 的 C# 对标实现。
    /// 使用系统原生 WebView（Windows: WebView2, macOS: WKWebView, Linux: WebKitGTK）
    /// 以嵌入窗口模式（EmbeddedWindow）渲染，不支持 OSR。
    ///
    /// 参考源：
    /// - godot_wry (Rust): 窗口嵌入、输入事件转发、IPC 模式
    /// - NativeWebView (C#): 跨平台系统 WebView 分层架构
    /// </summary>
    [Tool]
    [GlobalClass]
    public partial class WebViewControl : Control
    {
        // ── 平台 WebView 实现 ──
        private IWebViewBackend _webview;

        // ── 位置/大小同步缓存 ──
        private Vector2 _previousGlobalPos;
        private Vector2 _previousSize;
        private Vector2I _previousWindowPos;
        private float _previousContentScale = 1.0f;

        // ── 状态 ──
        private bool _isReady;

        // ═══════════════════════════════════════════════
        //  导出属性 (对标 godot_wry WebView struct)
        // ═══════════════════════════════════════════════

        /// <summary>
        /// 全窗口模式：webview 铺满整个 Godot 窗口
        /// </summary>
        [Export]
        public bool FullWindowSize { get; set; } = false;

        /// <summary>
        /// 初始 URL
        /// </summary>
        [Export]
        public string Url { get; set; } = "about:blank";

        /// <summary>
        /// 初始 HTML（与 Url 二选一）
        /// </summary>
        [Export]
        public string Html { get; set; } = "";

        /// <summary>
        /// 用户数据目录（浏览器缓存、Cookie 等）
        /// </summary>
        [Export]
        public string DataDirectory { get; set; } = "user://webview_data";

        /// <summary>
        /// 是否启用 DevTools (F12)
        /// </summary>
        [Export]
        public bool DevToolsEnabled { get; set; } = true;

        /// <summary>
        /// 自定义 User-Agent
        /// </summary>
        [Export]
        public string UserAgent { get; set; } = "";

        /// <summary>
        /// 是否启用缩放快捷键 (Ctrl++/-)
        /// </summary>
        [Export]
        public bool ZoomHotkeysEnabled { get; set; } = false;

        /// <summary>
        /// 是否允许剪贴板访问
        /// </summary>
        [Export]
        public bool ClipboardEnabled { get; set; } = true;

        /// <summary>
        /// 无痕模式（不保存浏览数据）
        /// </summary>
        [Export]
        public bool Incognito { get; set; } = false;

        /// <summary>
        /// 创建时自动获取焦点
        /// </summary>
        [Export]
        public bool FocusedWhenCreated { get; set; } = true;

        /// <summary>
        /// 是否将输入事件转发到 Godot 输入系统
        /// 对标 godot_wry 的 forward_input_events
        /// </summary>
        [Export]
        public bool ForwardInputEvents { get; set; } = true;

        /// <summary>
        /// 页面加载时自动播放媒体
        /// </summary>
        [Export]
        public bool Autoplay { get; set; } = false;

        /// <summary>
        /// 背景颜色
        /// </summary>
        [Export]
        public Color BackgroundColor { get; set; } = Colors.White;

        /// <summary>
        /// 当前页面 URL（只读）
        /// </summary>
        [Export]
        public string CurrentUrl { get; private set; } = "";

        /// <summary>
        /// 是否正在加载（只读）
        /// </summary>
        [Export]
        public bool IsLoading { get; private set; }

        // ═══════════════════════════════════════════════
        //  信号 (对标 godot_wry)
        // ═══════════════════════════════════════════════

        /// <summary>
        /// JS 通过 IPC 发送消息到 C#
        /// 对标 godot_wry 的 ipc_message
        /// </summary>
        [Signal]
        public delegate void IpcMessageEventHandler(string message);

        /// <summary>
        /// 页面开始加载
        /// 对标 godot_wry 的 page_load_started
        /// </summary>
        [Signal]
        public delegate void PageLoadStartedEventHandler(string url);

        /// <summary>
        /// 页面加载完成
        /// 对标 godot_wry 的 page_load_finished
        /// </summary>
        [Signal]
        public delegate void PageLoadFinishedEventHandler(string url);

        // ═══════════════════════════════════════════════
        //  生命周期
        // ═══════════════════════════════════════════════

        public override void _Ready()
        {
            // 编辑器模式下不创建 WebView，避免嵌入窗口和初始化异常
            if (Engine.IsEditorHint())
                return;

            _isReady = true;
            InitializeWebView();
        }

        public override void _ExitTree()
        {
            CleanupWebView();
        }

        public override void _Process(double delta)
        {
            if (Engine.IsEditorHint())
                return;

            if (_webview == null || !_webview.IsCreated)
                return;

            SyncPosition();
        }

        // ═══════════════════════════════════════════════
        //  初始化 / 清理
        // ═══════════════════════════════════════════════

        private async void InitializeWebView()
        {
            try
            {
                _webview = new WindowsWebViewBackend();
                await _webview.CreateAsync(this);

                if (!_webview.IsCreated)
                {
                    GD.PrintErr("[WebViewSharp] Failed to create webview");
                    return;
                }

                // 加载 URL 或 HTML
                if (!string.IsNullOrEmpty(Url))
                    _webview.Navigate(ResolveUrl(Url));
                else if (!string.IsNullOrEmpty(Html))
                    _webview.NavigateToString(Html);

                // 连接事件
                _webview.NavigationStarting += OnNavigationStarting;
                _webview.NavigationCompleted += OnNavigationCompleted;
                _webview.WebMessageReceived += OnWebMessageReceived;
                _webview.SourceChanged += OnSourceChanged;

                // 初始同步
                SyncPosition();

                GD.Print($"[WebViewSharp] WebView initialized, Url={Url}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[WebViewSharp] WebView initialization failed: {ex.Message}");
            }
        }

        private void CleanupWebView()
        {
            if (_webview != null)
            {
                _webview.Dispose();
                _webview = null;
            }
        }

        // ═══════════════════════════════════════════════
        //  位置同步（参考 godot_wry update_webview）
        // ═══════════════════════════════════════════════

        private void SyncPosition()
        {
            if (_webview == null || !_webview.IsCreated)
                return;

            var globalPos = GlobalPosition;
            var size = Size;
            if (size.X <= 0 || size.Y <= 0 && !FullWindowSize)
                return;

            float contentScale = DisplayServer.ScreenGetScale();
            var windowPos = FullWindowSize
                ? Vector2I.Zero
                : DisplayServer.WindowGetPosition();

            // 仅在有变化时更新，避免不必要的开销
            if (globalPos == _previousGlobalPos
                && size == _previousSize
                && windowPos == _previousWindowPos
                && Math.Abs(contentScale - _previousContentScale) < 0.001f)
            {
                return;
            }

            _previousGlobalPos = globalPos;
            _previousSize = size;
            _previousWindowPos = windowPos;
            _previousContentScale = contentScale;

            int physX, physY, physW, physH;

            if (FullWindowSize)
            {
                var windowSize = DisplayServer.WindowGetSize();
                physX = 0;
                physY = 0;
                physW = (int)(windowSize.X * contentScale);
                physH = (int)(windowSize.Y * contentScale);
            }
            else
            {
                physX = (int)(globalPos.X * contentScale);
                physY = (int)(globalPos.Y * contentScale);
                physW = (int)(size.X * contentScale);
                physH = (int)(size.Y * contentScale);
            }

            _webview.SetBounds(physX, physY, physW, physH);
        }

        // ═══════════════════════════════════════════════
        //  事件处理
        // ═══════════════════════════════════════════════

        private void OnNavigationStarting(string url)
        {
            IsLoading = true;
            CallDeferred(nameof(EmitPageLoadStarted), url);
        }

        private void OnNavigationCompleted(bool success, int httpStatusCode)
        {
            IsLoading = false;
            CallDeferred(nameof(EmitPageLoadFinished), CurrentUrl);
        }

        private void OnSourceChanged(string url)
        {
            CurrentUrl = url;
        }

        private void OnWebMessageReceived(string message)
        {
            // 如果启用了输入转发，先检查是否是内部输入事件
            if (ForwardInputEvents && InputEventRouter.TryProcessInputEvent(message, this))
                return;

            // 否则作为普通 IPC 消息触发 signal
            CallDeferred(nameof(EmitIpcMessage), message);
        }

        private void EmitPageLoadStarted(string url)
        {
            EmitSignal(SignalName.PageLoadStarted, url);
        }

        private void EmitPageLoadFinished(string url)
        {
            EmitSignal(SignalName.PageLoadFinished, url);
        }

        private void EmitIpcMessage(string message)
        {
            EmitSignal(SignalName.IpcMessage, message);
        }

        // ═══════════════════════════════════════════════
        //  公开 API（对标 godot_wry 的 func 方法）
        // ═══════════════════════════════════════════════

        /// <summary>
        /// 向网页发送消息（触发 JS 的 message 事件）
        /// 对标 godot_wry 的 post_message
        /// </summary>
        public void PostMessage(string message)
        {
            if (_webview?.IsCreated == true)
            {
                string script = $"document.dispatchEvent(new CustomEvent('message', {{ detail: {Json.Stringify(message)} }}))";
                _webview.ExecuteScript(script);
            }
        }

        /// <summary>
        /// 在网页中执行 JavaScript
        /// 对标 godot_wry 的 eval
        /// </summary>
        public void EvaluateJavaScript(string script)
        {
            _webview?.ExecuteScript(script);
        }

        /// <summary>
        /// 加载 URL
        /// 对标 godot_wry 的 load_url
        /// </summary>
        public void LoadUrl(string url)
        {
            if (_webview?.IsCreated == true)
            {
                string resolvedUrl = ResolveUrl(url);
                _webview.Navigate(resolvedUrl);
            }
        }

        /// <summary>
        /// 加载 HTML 字符串
        /// 对标 godot_wry 的 load_html
        /// </summary>
        public void LoadHtml(string html)
        {
            _webview?.NavigateToString(html);
        }

        /// <summary>
        /// 重新加载当前页面
        /// 对标 godot_wry 的 reload
        /// </summary>
        public void Reload()
        {
            _webview?.Reload();
        }

        /// <summary>
        /// 后退
        /// </summary>
        public void GoBack()
        {
            _webview?.GoBack();
        }

        /// <summary>
        /// 前进
        /// </summary>
        public void GoForward()
        {
            _webview?.GoForward();
        }

        /// <summary>
        /// 打开 DevTools
        /// </summary>
        public void OpenDevTools()
        {
            _webview?.OpenDevTools();
        }

        /// <summary>
        /// 关闭 DevTools
        /// </summary>
        public void CloseDevTools()
        {
            _webview?.CloseDevTools();
        }

        /// <summary>
        /// 清除所有浏览数据
        /// </summary>
        public void ClearAllBrowsingData()
        {
            _webview?.ClearBrowsingData();
        }

        /// <summary>
        /// 获取焦点
        /// </summary>
        public void FocusWebView()
        {
            _webview?.Focus();
        }

        // ═══════════════════════════════════════════════
        //  辅助方法
        // ═══════════════════════════════════════════════

        private string ResolveUrl(string url)
        {
            // res:// → https://godot.local/ 虚拟主机映射
            // 避免 file:// 协议下 ES Module 被 CORS 拦截
            if (url.StartsWith("res://"))
            {
                string path = url.Substring(6).Replace("\\", "/");
                return $"https://godot.local/{path}";
            }

            // user:// 协议
            if (url.StartsWith("user://"))
            {
                string path = url.Substring(7).Replace("\\", "/");
                string globalPath = ProjectSettings.GlobalizePath($"user://{path}");
                return $"file:///{globalPath.Replace("\\", "/")}";
            }

            return url;
        }
    }
}
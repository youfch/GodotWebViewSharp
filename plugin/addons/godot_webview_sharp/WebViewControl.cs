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
        private Rect2I? _previousRect;

        // ── 状态 ──
        private bool _isReady;
        private bool _visibilityHooked;

        public WebViewControl()
        {
        }

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
        /// 透明背景：webview 不遮挡父窗口内容
        /// 对标 godot_wry 的 transparent
        /// </summary>
        [Export]
        public bool Transparent { get; set; } = false;

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

            // 隐藏/显示时同步原生窗口可见性，否则控件隐藏后 webview 仍然浮在最上层
            // （对标 godot_wry 的 visibility_changed -> update_visibility）
            HookVisibility();

            InitializeWebView();
        }

        public override void _ExitTree()
        {
            // 注意：必须与订阅对称。编辑器模式下 _Ready 会提前 return（没订阅），
            // 此时若无条件 -= 就会报 "Attempt to disconnect a nonexistent connection"。
            UnhookVisibility();
            CleanupWebView();
        }

        private void HookVisibility()
        {
            if (_visibilityHooked)
                return;

            VisibilityChanged += OnVisibilityChanged;
            _visibilityHooked = true;
        }

        private void UnhookVisibility()
        {
            if (!_visibilityHooked)
                return;

            VisibilityChanged -= OnVisibilityChanged;
            _visibilityHooked = false;
        }

        public override void _Process(double delta)
        {
            if (Engine.IsEditorHint())
                return;

            if (_webview == null || !_webview.IsCreated)
                return;

            SyncPosition();

            // Linux GTK 事件泵浦（参考 godot_wry）
            if (OperatingSystem.IsLinux() && _webview is IDisposable)
            {
                try
                {
                    // 通过反射调用 PumpGtkEvents（避免编译时依赖 LINUX 条件编译）
                    var method = _webview.GetType().GetMethod("PumpGtkEvents");
                    method?.Invoke(_webview, null);
                }
                catch { }
            }
        }

        // ═══════════════════════════════════════════════
        //  焦点管理（对标 godot_wry 的 input()）
        // ═══════════════════════════════════════════════

        /// <summary>
        /// 当 webview 非全窗口大小、鼠标按下、且点击位置在 webview rect 外时，
        /// 把焦点从 WebView2 子窗口切回 Godot 父窗口。
        /// 否则键盘事件会停留在 webview，Godot 后续的键盘输入收不到。
        /// 直接对标 godot_wry (rust/src/lib.rs) 的 fn input()。
        /// </summary>
        public override void _Input(InputEvent @event)
        {
            if (_webview == null || !_webview.IsCreated || FullWindowSize)
                return;

            if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
            {
                var mousePos = GetGlobalMousePosition();
                var rect = GetGlobalRect();

                if (!rect.HasPoint(mousePos))
                {
                    _webview.FocusParent();
                }
            }
        }

        // ═══════════════════════════════════════════════
        //  初始化 / 清理
        // ═══════════════════════════════════════════════

        private async void InitializeWebView()
        {
            try
            {
                if (OperatingSystem.IsMacOS())
                {
#if MACOS || MAC
                    _webview = new MacWebViewBackend();
#else
                    GD.PrintErr("[WebViewSharp] macOS backend not compiled. Rebuild with target macOS.");
                    return;
#endif
                }
                else if (OperatingSystem.IsLinux())
                {
#if LINUX
                    _webview = new LinuxWebViewBackend();
#else
                    GD.PrintErr("[WebViewSharp] Linux backend not compiled. Rebuild with target Linux.");
                    return;
#endif
                }
                else
                {
                    _webview = new WindowsWebViewBackend();
                }

                await _webview.CreateAsync(this);

                if (!_webview.IsCreated)
                {
                    GD.PrintErr("[WebViewSharp] Failed to create webview");
                    return;
                }

                // 连接事件（必须在导航之前，否则首个页面的 load 事件会丢）
                _webview.NavigationStarting += OnNavigationStarting;
                _webview.NavigationCompleted += OnNavigationCompleted;
                _webview.WebMessageReceived += OnWebMessageReceived;
                _webview.SourceChanged += OnSourceChanged;

                // 加载 URL 或 HTML
                if (!string.IsNullOrEmpty(Url))
                    _webview.Navigate(ResolveUrl(Url));
                else if (!string.IsNullOrEmpty(Html))
                    _webview.NavigateToString(Html);

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

            var rect = ComputeNativeRect(this, FullWindowSize);

            if (!FullWindowSize && (rect.Size.X <= 0 || rect.Size.Y <= 0))
                return;

            // 无变化就不动（子窗口拖动、窗口移动、缩放变化都会体现在 rect 里）
            if (_previousRect.HasValue && _previousRect.Value == rect)
                return;

            _previousRect = rect;
            _webview.SetBounds(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y);
        }

        /// <summary>
        /// 计算 webview 在原生宿主窗口（主窗口）客户区中的物理像素矩形。
        ///
        /// 实测（Godot 4.6.3 / Windows）得出的三条结论：
        /// 1. <c>Control.GlobalPosition</c> 只到"最近的 Window 祖先"的视口空间。
        ///    控件挂在嵌入式子窗口（Window 节点）里时，直接用它会丢掉子窗口偏移 ——
        ///    这就是"浏览器没有出现在 Window 节点里"的根因。
        /// 2. <c>GetScreenTransform().Origin</c> 会累加所有祖先子窗口偏移，且恰好**不含**
        ///    操作系统窗口自身的屏幕位置 —— 正是需要的坐标空间（webview 是主窗口的子窗口）。
        /// 3. 窗口像素 ÷ 视口尺寸 = 物理/逻辑比例，同时涵盖拉伸（content_scale）与 DPI 缩放，
        ///    比 <c>DisplayServer.ScreenGetScale()</c> 更准确。
        /// </summary>
        internal static Rect2I ComputeNativeRect(Control control, bool fullWindowSize)
        {
            var window = control.GetWindow();
            var viewport = control.GetViewport();

            float scaleX = 1.0f, scaleY = 1.0f;
            if (window != null && viewport != null)
            {
                Vector2 vpSize = viewport.GetVisibleRect().Size;
                Vector2I winSize = window.Size;
                if (vpSize.X > 0.0f) scaleX = winSize.X / vpSize.X;
                if (vpSize.Y > 0.0f) scaleY = winSize.Y / vpSize.Y;
            }

            if (fullWindowSize)
            {
                Vector2I winSize = window?.Size ?? DisplayServer.WindowGetSize();
                return new Rect2I(0, 0, winSize.X, winSize.Y);
            }

            Vector2 origin = control.GetScreenTransform().Origin;
            Vector2 size = control.Size;

            return new Rect2I(
                Mathf.RoundToInt(origin.X * scaleX),
                Mathf.RoundToInt(origin.Y * scaleY),
                Mathf.RoundToInt(size.X * scaleX),
                Mathf.RoundToInt(size.Y * scaleY));
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
            if (ForwardInputEvents && InputEventRouter.TryProcessInputEvent(message, this))
                return;

            EmitSignal(SignalName.IpcMessage, message);
        }

        private void OnVisibilityChanged()
        {
            if (_webview == null || !_webview.IsCreated)
                return;

            bool visible = IsVisibleInTree();
            _webview.SetVisible(visible);

            if (visible)
            {
                // 重新显示后强制同步一次位置（显示期间窗口/子窗口可能已经移动过）
                _previousRect = null;
                SyncPosition();
            }
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

        /// <summary>
        /// 把焦点还回 Godot 父窗口。
        /// 对标 godot_wry 的 focus_parent()：点击 webview 区域外的 Godot 场景时，
        /// 需要主动把焦点抢回来，否则键盘事件会一直留在网页里。
        /// </summary>
        public void FocusParent()
        {
            _webview?.FocusParent();
        }

        /// <summary>
        /// 改变页面缩放，1.0 为默认。
        /// 对标 godot_wry 的 zoom(factor)
        /// </summary>
        public void Zoom(float factor)
        {
            _webview?.SetZoomFactor(factor);
        }

        /// <summary>
        /// 打开打印对话框。
        /// 对标 godot_wry 的 print()
        /// </summary>
        public void Print()
        {
            _webview?.Print();
        }

        /// <summary>
        /// 只切换原生 webview 的可见性，不动 Control 自己的 visible。
        /// 对标 godot_wry 的 set_visible()。
        ///
        /// 注意没有沿用同名 <c>SetVisible</c>：Control 基类本来就有 set_visible（visible 属性的 setter），
        /// 同名会把基类方法遮蔽掉，GDScript 里 <c>$WebView.set_visible(false)</c> 的含义会被悄悄改掉。
        /// 控件自身 visible 变化时的同步是自动的。
        /// </summary>
        public void SetWebViewVisible(bool visible)
        {
            _webview?.SetVisible(visible);
        }

        /// <summary>
        /// DevTools 窗口是否打开。
        /// 对标 godot_wry 的 is_devtools_open()。
        ///
        /// WebView2 没有查询 API，只能返回我们自己记录的状态：
        /// 用 OpenDevTools()/CloseDevTools() 开关能正确反映，
        /// 但用户在右键菜单里手动打开/关闭 DevTools 时看不到。
        /// </summary>
        public bool IsDevToolsOpen()
        {
            return _webview?.IsDevToolsOpen ?? false;
        }

        // ═══════════════════════════════════════════════
        //  辅助方法
        // ═══════════════════════════════════════════════

        private string ResolveUrl(string url)
        {
            // res:// → https://godot.local/、user:// → https://godot-user.local/
            // 由 WebResourceServer 拦截并用 Godot 的 FileAccess 读取（PCK 内也能读），
            // 避免 file:// 协议下 ES Module 被 CORS 拦截，也不需要关闭 web 安全
            if (url.StartsWith("res://"))
            {
                string path = url.Substring(6).Replace("\\", "/");
                return $"https://godot.local/{path}";
            }

            if (url.StartsWith("user://"))
            {
                string path = url.Substring(7).Replace("\\", "/");
                return $"https://godot-user.local/{path}";
            }

            return url;
        }
    }
}
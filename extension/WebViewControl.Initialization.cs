using Godot;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace GDEWebView;

public partial class WebViewControl
{
    // ═══════════════════════════════════════════════
    //  导出属性
    // ═══════════════════════════════════════════════

    public string Url { get; set; } = "about:blank";
    public string Html { get; set; } = "";
    public string DataDirectory { get; set; } = "user://webview_data";
    public bool DevToolsEnabled { get; set; } = true;
    public string UserAgent { get; set; } = "";
    public bool Transparent { get; set; } = false;
    public bool Incognito { get; set; } = false;
    public bool ForwardInputEvents { get; set; } = true;
    public bool Autoplay { get; set; } = false;
    public Color BackgroundColor { get; set; } = NamedColors.White;

    // ── 只读 ──
    public string CurrentUrl { get; private set; } = "";
    public bool IsLoading { get; private set; }

    // ═══════════════════════════════════════════════
    //  信号
    // ═══════════════════════════════════════════════

    /// <summary>
    /// JS 通过 IPC 发送消息到 C#
    /// </summary>
    public event Action<string> IpcMessage;

    /// <summary>
    /// 页面开始加载
    /// </summary>
    public event Action<string> PageLoadStarted;

    /// <summary>
    /// 页面加载完成
    /// </summary>
    public event Action<string> PageLoadFinished;

    // ═══════════════════════════════════════════════
    //  生命周期
    // ═══════════════════════════════════════════════

    protected override void _Ready()
    {
        if (Engine.Singleton.IsEditorHint())
            return;

        _isReady = true;

        // 隐藏/显示时同步原生窗口可见性（对标 godot_wry 的 visibility_changed -> update_visibility）
        HookVisibility();

        _ = InitializeWebViewAsync();
    }

    protected override void _ExitTree()
    {
        // 必须与订阅对称：编辑器模式下 _Ready 提前 return（没订阅），
        // 无条件 -= 会报 "Attempt to disconnect a nonexistent connection"
        UnhookVisibility();
        CleanupWebView();
    }

    protected override void _Process(double delta)
    {
        if (Engine.Singleton.IsEditorHint())
            return;

        if (_webview == null || !_webview.IsCreated)
            return;

        SyncPosition();
    }

    /// <summary>
    /// 点击 webview 区域外时把焦点还给 Godot 父窗口（对标 godot_wry 的 input()），
    /// 否则键盘事件会一直留在网页里。
    /// </summary>
    protected override void _Input(InputEvent @event)
    {
        if (_webview == null || !_webview.IsCreated)
            return;

        if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
        {
            Vector2 mousePos = GetGlobalMousePosition();
            Rect2 rect = GetGlobalRect();

            if (!rect.HasPoint(mousePos))
                _webview.FocusParent();
        }
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

    private void OnVisibilityChanged()
    {
        if (_webview == null || !_webview.IsCreated)
            return;

        bool visible = IsVisibleInTree();
        _webview.SetVisible(visible);

        if (visible)
        {
            _previousRect = null;
            SyncPosition();
        }
    }

    // ═══════════════════════════════════════════════
    //  初始化 / 清理
    // ═══════════════════════════════════════════════

    private async Task InitializeWebViewAsync()
    {
        try
        {
            // 平台选择
            if (OperatingSystem.IsMacOS())
            {
#if MACOS
                _webview = new MacWebViewBackend();
#else
                GD.PrintErr("[GDEWebView] macOS backend not compiled.");
                return;
#endif
            }
            else if (OperatingSystem.IsLinux())
            {
#if LINUX
                _webview = new LinuxWebViewBackend();
#else
                GD.PrintErr("[GDEWebView] Linux backend not compiled.");
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
                GD.PrintErr("[GDEWebView] Failed to create webview");
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

            _browserCreated = true;
            GD.Print($"[GDEWebView] WebView initialized, Url={Url}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GDEWebView] WebView initialization failed: {ex.Message}");
        }
    }

    private void CleanupWebView()
    {
        if (_webview != null)
        {
            _webview.NavigationStarting -= OnNavigationStarting;
            _webview.NavigationCompleted -= OnNavigationCompleted;
            _webview.WebMessageReceived -= OnWebMessageReceived;
            _webview.SourceChanged -= OnSourceChanged;

            _webview.Dispose();
            _webview = null;
        }
    }

    // ═══════════════════════════════════════════════
    //  位置同步
    // ═══════════════════════════════════════════════

    private void SyncPosition()
    {
        if (_webview == null || !_webview.IsCreated)
            return;

        Rect2I rect = ComputeNativeRect(this);

        if (rect.Size.X <= 0 || rect.Size.Y <= 0)
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
    /// 实测（Godot 4.6.3 / Windows）：
    /// 1. Control.GlobalPosition 只到"最近的 Window 祖先"的视口空间，控件挂在嵌入式子窗口
    ///    （Window 节点）里时用它会丢掉子窗口偏移 —— 这正是"浏览器不在 Window 节点内"的根因。
    /// 2. GetScreenTransform().Origin 会累加所有祖先子窗口偏移，且不含 OS 窗口自身的屏幕位置，
    ///    正是需要的坐标空间（webview 是主窗口的子窗口）。
    /// 3. 窗口像素 ÷ 视口尺寸 = 物理/逻辑比例，同时涵盖拉伸与 DPI 缩放。
    /// </summary>
    internal static Rect2I ComputeNativeRect(Control control)
    {
        Window window = control.GetWindow();
        Viewport viewport = control.GetViewport();

        float scaleX = 1.0f, scaleY = 1.0f;
        if (window != null && viewport != null)
        {
            Vector2 vpSize = viewport.GetVisibleRect().Size;
            Vector2I winSize = window.Size;
            if (vpSize.X > 0.0f) scaleX = winSize.X / vpSize.X;
            if (vpSize.Y > 0.0f) scaleY = winSize.Y / vpSize.Y;
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
        EmitSignal(new StringName(nameof(PageLoadStarted)), Variant.From(url));
        PageLoadStarted?.Invoke(url);
    }

    private void OnNavigationCompleted(bool success, int httpStatusCode)
    {
        IsLoading = false;
        EmitSignal(new StringName(nameof(PageLoadFinished)), Variant.From(CurrentUrl));
        PageLoadFinished?.Invoke(CurrentUrl);
    }

    private void OnSourceChanged(string url)
    {
        CurrentUrl = url;
    }

    private void OnWebMessageReceived(string message)
    {
        // _ 开头的载荷是输入事件转发，先交给路由处理
        if (ForwardInputEvents && InputEventRouter.TryProcessInputEvent(message, this))
            return;

        EmitSignal(new StringName(nameof(IpcMessage)), Variant.From(message));
        IpcMessage?.Invoke(message);
    }

    // ═══════════════════════════════════════════════
    //  辅助方法
    // ═══════════════════════════════════════════════

    private string ResolveUrl(string url)
    {
        // res:// → https://godot.local/、user:// → https://godot-user.local/
        // 由 WebResourceServer 拦截并用 Godot 的 FileAccess 读取（PCK 内也能读）
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
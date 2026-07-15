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
    public Color BackgroundColor { get; set; } = Colors.White;

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
        _ = InitializeWebViewAsync();
    }

    protected override void _ExitTree()
    {
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

        var globalPos = GlobalPosition;
        var size = Size;
        if (size.X <= 0 || size.Y <= 0)
            return;

        float contentScale = DisplayServer.Singleton.ScreenGetScale();
        var windowPos = DisplayServer.Singleton.WindowGetPosition();

        // 仅在有变化时更新
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

        int physX = (int)(globalPos.X * contentScale);
        int physY = (int)(globalPos.Y * contentScale);
        int physW = (int)(size.X * contentScale);
        int physH = (int)(size.Y * contentScale);

        _webview.SetBounds(physX, physY, physW, physH);
    }

    // ═══════════════════════════════════════════════
    //  事件处理
    // ═══════════════════════════════════════════════

    private void OnNavigationStarting(string url)
    {
        IsLoading = true;
        EmitSignal(new StringName(nameof(PageLoadStarted)), new Variant[] { new Variant(url) });
        PageLoadStarted?.Invoke(url);
    }

    private void OnNavigationCompleted(bool success, int httpStatusCode)
    {
        IsLoading = false;
        EmitSignal(new StringName(nameof(PageLoadFinished)), new Variant[] { new Variant(CurrentUrl) });
        PageLoadFinished?.Invoke(CurrentUrl);
    }

    private void OnSourceChanged(string url)
    {
        CurrentUrl = url;
    }

    private void OnWebMessageReceived(string message)
    {
        EmitSignal(new StringName(nameof(IpcMessage)), new Variant[] { new Variant(message) });
        IpcMessage?.Invoke(message);
    }

    // ═══════════════════════════════════════════════
    //  辅助方法
    // ═══════════════════════════════════════════════

    private string ResolveUrl(string url)
    {
        if (url.StartsWith("res://"))
        {
            string path = url.Substring(6).Replace("\\", "/");
            return $"https://godot.local/{path}";
        }

        if (url.StartsWith("user://"))
        {
            string path = url.Substring(7).Replace("\\", "/");
            string globalPath = ProjectSettings.Singleton.GlobalizePath($"user://{path}");
            return $"file:///{globalPath.Replace("\\", "/")}";
        }

        return url;
    }
}
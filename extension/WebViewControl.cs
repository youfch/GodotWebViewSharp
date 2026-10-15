using Godot;
using System;

namespace GDEWebView;

/// <summary>
/// 渲染模式
/// </summary>
public enum RenderMode
{
    /// <summary>
    /// 嵌入窗口模式：系统 WebView 作为原生子窗口嵌入。
    /// 性能更好，支持视频/WebGL，不支持透明背景。
    /// </summary>
    EmbeddedWindow = 0
}

/// <summary>
/// Godot WebView GDExtension 控件。
/// 基于 Godot 原生 GDExtension API (godot-dotnet bindings)，
/// 使用系统原生 WebView（Windows: WebView2, macOS: WKWebView, Linux: WebKitGTK）。
///
/// 参考:
/// - GDCefGlue extension/ 的 GDExtension 结构
/// - GodotWebViewSharp addon/ 的 WebViewControl 逻辑
/// - godot_wry 的窗口嵌入 + 输入转发 + IPC 模式
/// </summary>
[GodotClass]
public partial class WebViewControl : Control
{
    // ── 平台 WebView 后端 ──
    private IWebViewBackend _webview;

    // ── 位置/大小同步缓存 ──
    private Rect2I? _previousRect;

    // ── 状态 ──
    private bool _isReady;
    private bool _browserCreated;
    private bool _visibilityHooked;

    // ── 构造 ──
    public WebViewControl()
    {
        GD.Print("GDEWebView: WebViewControl constructed");
    }
}
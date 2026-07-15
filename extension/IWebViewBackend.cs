using Godot;
using System;

namespace GDEWebView;

/// <summary>
/// 平台 WebView 后端接口。
/// 各平台实现：Windows (WebView2) | macOS (WKWebView) | Linux (WebKitGTK)
/// </summary>
internal interface IWebViewBackend : IDisposable
{
    bool IsCreated { get; }

    // ── 生命周期 ──
    System.Threading.Tasks.Task CreateAsync(Control hostControl);

    // ── 导航 ──
    void Navigate(string url);
    void NavigateToString(string html);
    void Reload();
    void GoBack();
    void GoForward();
    bool CanGoBack { get; }
    bool CanGoForward { get; }

    // ── 位置 ──
    void SetBounds(int x, int y, int width, int height);

    // ── JS 执行 ──
    void ExecuteScript(string script);

    // ── DevTools ──
    void OpenDevTools();
    void CloseDevTools();

    // ── 数据 ──
    void ClearBrowsingData();

    // ── 焦点 ──
    void Focus();

    // ── 可见性 ──
    void SetVisible(bool visible);

    // ── 透明背景（仅当平台支持时生效） ──
    void SetTransparent(bool transparent);

    // ── 事件 ──
    event Action<string> NavigationStarting;
    event Action<bool, int> NavigationCompleted;
    event Action<string> WebMessageReceived;
    event Action<string> SourceChanged;
}
using Godot;

namespace GDEWebView;

public partial class WebViewControl
{
    // ═══════════════════════════════════════════════
    //  公开 API
    // ═══════════════════════════════════════════════

    /// <summary>
    /// 向网页发送消息（触发 JS 的 message 事件）
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
    /// </summary>
    public void EvaluateJavaScript(string script)
    {
        _webview?.ExecuteScript(script);
    }

    /// <summary>
    /// 加载 URL
    /// </summary>
    public void LoadUrl(string url)
    {
        if (_webview?.IsCreated == true)
        {
            string resolvedUrl = ResolveUrl(url);
            _webview.Navigate(resolvedUrl);
        }
    }

    public void NavigateToUrl(string url) => LoadUrl(url);

    /// <summary>
    /// 加载 HTML 字符串
    /// </summary>
    public void LoadHtml(string html)
    {
        _webview?.NavigateToString(html);
    }

    /// <summary>
    /// 重新加载当前页面
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
    /// 把焦点还回 Godot 父窗口（对标 godot_wry 的 focus_parent()）。
    /// 点击 webview 区域外的 Godot 场景时需要主动抢回焦点，否则键盘事件会一直留在网页里。
    /// </summary>
    public void FocusParent()
    {
        _webview?.FocusParent();
    }

    /// <summary>
    /// 改变页面缩放，1.0 为默认。对标 godot_wry 的 zoom(factor)
    /// </summary>
    public void Zoom(float factor)
    {
        _webview?.SetZoomFactor(factor);
    }

    /// <summary>
    /// 打开打印对话框。对标 godot_wry 的 print()
    /// </summary>
    public void Print()
    {
        _webview?.Print();
    }

    /// <summary>
    /// 只切换原生 webview 的可见性，不动 Control 自己的 visible。
    /// 对标 godot_wry 的 set_visible()。
    ///
    /// 注意没有沿用同名 SetVisible：Control 基类本来就有 set_visible（visible 属性的 setter），
    /// 同名会把基类方法遮蔽掉，让 GDScript 里 $WebView.set_visible(false) 的含义变得反直觉。
    /// 控件自身 visible 变化时的同步是自动的。
    /// </summary>
    public void SetWebViewVisible(bool visible)
    {
        _webview?.SetVisible(visible);
    }

    /// <summary>
    /// DevTools 窗口是否打开。对标 godot_wry 的 is_devtools_open()。
    ///
    /// WebView2 没有查询 API，只能返回我们自己记录的状态：
    /// 用 OpenDevTools()/CloseDevTools() 开关能正确反映，用户在右键菜单里手动开关时看不到。
    /// </summary>
    public bool IsDevToolsOpen()
    {
        return _webview?.IsDevToolsOpen ?? false;
    }

    /// <summary>
    /// 获取焦点
    /// </summary>
    public void FocusWebView()
    {
        _webview?.Focus();
    }
}
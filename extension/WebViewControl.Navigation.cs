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
    /// 获取焦点
    /// </summary>
    public void FocusWebView()
    {
        _webview?.Focus();
    }
}
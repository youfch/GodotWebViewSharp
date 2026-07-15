using Godot;
using WebViewSharp;

namespace GodotWebViewSharp.Demo
{
    /// <summary>
    /// GodotWebViewSharp 演示脚本。
    /// 对标 godot_wry 的 example.tscn 功能：
    /// - 加载 URL / HTML
    /// - JS ↔ C# IPC
    /// - 输入事件转发
    /// - 导航控制（后退/前进/刷新）
    /// </summary>
    public partial class DemoBrowser : Control
    {
        private WebViewControl _browser;
        private LineEdit _urlInput;
        private Button _goButton;
        private Button _backButton;
        private Button _forwardButton;
        private Button _reloadButton;
        private Button _devToolsButton;
        private Label _statusLabel;
        private Label _urlLabel;

        public override void _Ready()
        {
            _browser = GetNode<WebViewControl>("WebView");
            _urlInput = GetNode<LineEdit>("Toolbar/UrlInput");
            _goButton = GetNode<Button>("Toolbar/GoButton");
            _backButton = GetNode<Button>("Toolbar/BackButton");
            _forwardButton = GetNode<Button>("Toolbar/ForwardButton");
            _reloadButton = GetNode<Button>("Toolbar/ReloadButton");
            _devToolsButton = GetNode<Button>("Toolbar/DevToolsButton");
            _statusLabel = GetNode<Label>("StatusBar/StatusLabel");
            _urlLabel = GetNode<Label>("StatusBar/UrlLabel");

            // 连接 WebView 信号
            _browser.PageLoadStarted += OnPageLoadStarted;
            _browser.PageLoadFinished += OnPageLoadFinished;
            _browser.IpcMessage += OnIpcMessage;

            // 连接 UI 按钮
            _goButton.Pressed += OnGoPressed;
            _backButton.Pressed += () => _browser.GoBack();
            _forwardButton.Pressed += () => _browser.GoForward();
            _reloadButton.Pressed += () => _browser.Reload();
            _devToolsButton.Pressed += () => _browser.OpenDevTools();
            _urlInput.TextSubmitted += _ => OnGoPressed();

            UpdateStatus("Ready");
        }

        public override void _Process(double delta)
        {
            // 更新 URL 显示
            if (_urlLabel != null && _browser != null)
            {
                _urlLabel.Text = _browser.CurrentUrl;
            }
        }

        private void OnGoPressed()
        {
            var url = _urlInput?.Text?.Trim();
            if (string.IsNullOrEmpty(url)) return;

            if (!url.StartsWith("http://") && !url.StartsWith("https://") &&
                !url.StartsWith("file://") && !url.StartsWith("res://") &&
                !url.StartsWith("about:"))
            {
                url = "https://" + url;
            }

            _browser.LoadUrl(url);
        }

        private void OnPageLoadStarted(string url)
        {
            Callable.From(() => UpdateStatus($"Loading: {url}")).CallDeferred();
        }

        private void OnPageLoadFinished(string url)
        {
            Callable.From(() => UpdateStatus("Done")).CallDeferred();
        }

        private void OnIpcMessage(string message)
        {
            GD.Print($"[Demo] IPC from JS: {message}");
            Callable.From(() => UpdateStatus($"IPC: {message.Left(50)}")).CallDeferred();
        }

        private void UpdateStatus(string text)
        {
            if (_statusLabel != null)
                _statusLabel.Text = text;
        }
    }
}
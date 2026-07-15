# GodotWebViewSharp

Godot 4.x 系统原生 WebView 插件，支持 **C# 插件模式** (Godot.NET.Sdk) 和 **GDExtension 原生模式** (AOT)。

## 架构

```
GodotWebViewSharp/
├── plugin/                          ← C# 插件模式 (Godot.NET.Sdk)
│   ├── addons/godot_webview_sharp/  ← 插件源码
│   │   ├── WebViewControl.cs         主控件
│   │   ├── WindowsWebViewBackend.cs  WebView2 (Windows)
│   │   ├── Platform/MacWebViewBackend.cs WKWebView (macOS)
│   │   ├── Platform/LinuxWebViewBackend.cs WebKitGTK (Linux)
│   │   └── InputEventRouter.cs      输入事件转发
│   ├── Demo/                      演示场景
│   └── project.godot
│
├── extension/                    ← GDExtension 模式 (新增 ✨)
│   ├── GDEWebView.csproj         AOT 原生项目
│   ├── Main.cs                   入口点 [UnmanagedCallersOnly]
│   ├── WebViewControl.cs         控件骨架
│   ├── WebViewControl.Bindings.cs 属性/方法/信号注册
│   ├── WebViewControl.Initialization.cs 生命周期
│   ├── WebViewControl.Navigation.cs 导航 API
│   ├── WindowsWebViewBackend.cs  WebView2 后端
│   ├── IWebViewBackend.cs        后端接口
│   ├── Platform/
│   │   ├── NativeWindowMethods.cs   跨平台窗口操作
│   │   ├── MacWebViewBackend.cs     WKWebView stub
│   │   └── LinuxWebViewBackend.cs   WebKitGTK stub
│   └── Dll/                        godot-dotnet 绑定(需自行编译)
│
├── test/GDExtensionGame/           ← GDExtension 测试工程
│   ├── project.godot
│   ├── lib/gde_webview.gdextension
│   └── MainScript.gd
│
└── README.md
```

## 两种模式对比

| 特性 | C# 插件模式 (plugin/) | GDExtension 模式 (extension/) |
|------|-------------------|------------------------------|
| 项目 SDK | `Godot.NET.Sdk` | `Microsoft.NET.Sdk` + `PublishAot` |
| 入口方式 | Godot Mono 自动加载 | `[UnmanagedCallersOnly]` 原生入口 |
| 属性注册 | `[Export]` 特性 | `ClassRegistrationContext.BindProperty` |
| 编译输出 | .dll 托管程序集 | AOT 原生 .dll (无 JIT) |
| 运行时依赖 | .NET SDK / Mono 运行时 | 无 (独立原生库) |
| 适用场景 | 开发期快速迭代 | 发布部署、性能敏感场景 |
| 参考实现 | 原生架构 | GDCefGlue extension/ 模式 |

## Windows 平台支持

基于 **Microsoft Edge WebView2** (系统内置)，支持：
- 嵌入窗口模式 (EmbeddedWindow) — 高性能，支持视频/WebGL
- JavaScript ↔ C# IPC (window.chrome.webview.postMessage)
- 输入事件转发 (鼠标/键盘 → Godot)
- 虚拟主机映射 (`godot.local` → Godot 项目 `res://`)
- F12 DevTools
- 透明背景 (Experimental)

## GDExtension 构建说明

### 前置要求

1. **Godot 4.6+** 带 .NET 支持
2. **.NET 8 SDK** 或更高
3. **WebView2 Runtime** (Windows 11 内置，Win10 需安装)

### 获取 godot-dotnet 绑定

```bash
git clone https://github.com/raulsntos/godot-dotnet.git
cd godot-dotnet
dotnet build -p:GenerateGodotBindings=true
```

将生成的 `Godot.Bindings.dll` 及相关文件复制到 `extension/Dll/`。

### 构建

```bash
cd extension
dotnet publish -c Release -r win-x64 --self-contained true
```

或使用构建脚本：

```bash
extension\build.bat publish
```

### 部署

将 `extension/bin/Release/net8.0/win-x64/publish/` 目录内容复制到
`test/GDExtensionGame/lib/`，然后用 Godot 4.6+ 打开 `test/GDExtensionGame/` 运行。

## C# 插件模式说明

```bash
cd src
dotnet build
```

然后在 Godot 编辑器中打开 `plugin/project.godot` 即可使用。

## 参考项目

- [GDCefGlue](https://github.com/youfch/GDCefGlue) — Godot + CEF (Chromium) 浏览器控件，本项目 GDExtension 架构参考
- [godot_wry](https://github.com/MikeSchulze/godot_wry) — Godot + WRY (Rust) 系统 WebView
- [NativeWebView](https://github.com/Incari/NativeWebView) — 跨平台系统 WebView C# 封装

## License

MIT
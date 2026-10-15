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
│   ├── IWebViewBackend.cs        后端接口
│   ├── Platform/
│   │   ├── WindowsWebViewBackend.cs  WebView2 后端
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
- 输入事件转发 (鼠标/键盘 → Godot)；webview 盖住的 Godot 控件照样能点（事件回注 `Viewport.PushInput`）
- 网页资源自建服务（`res://` → `https://godot.local/`，`user://` → `https://godot-user.local/`）：拦截请求后用 Godot 的 `FileAccess` 读文件，因此**导出成 PCK 后依然可用**；自带 MIME、目录回退 `index.html`、Range(206)、404，也不需关闭 web 安全
- F12 DevTools
- 透明背景 (Experimental)

## GDExtension 构建说明

### 前置要求

1. **Godot 4.7+** 带 .NET 支持（godot-dotnet 的绑定是按 4.7 的 extension_api.json 生成的）
2. **.NET 10 SDK**（同一原因：Godot.Bindings 目标框架是 net10.0）
3. **WebView2 Runtime** (Windows 11 内置，Win10 需安装)

> [!WARNING]
> 已知限制：NativeAOT 下 `Microsoft.Web.WebView2` 的托管包装建不起 webview，报
> `COM Interop requires ComWrapper instance registered for marshalling`（试过
> `BuiltInComInteropSupport` 与 `IlcArg --feature:System.Runtime.InteropServices.BuiltInComInterop.IsSupported=true`
> 都没绕过）。扩展本体能加载、类型与方法都注册正常，卡在 COM 这一层；
> 后续要么改成 CsWin32/`[LibraryImport]` 直接调 COM，要么放弃 AOT（放弃 AOT 就等于要带 .NET 运行时）。

### 获取 godot-dotnet 绑定

```bash
git clone --depth 1 https://github.com/raulsntos/godot-dotnet.git
cd godot-dotnet
dotnet build src/Godot.Bindings/Godot.Bindings.csproj -p:GenerateGodotBindings=true -c Release
```

生成的 `artifacts/bin/Godot.Bindings/Release/net10.0/Godot.Bindings.dll` 复制到 `extension/Dll/`。

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
cd plugin
dotnet build
```

然后在 Godot 编辑器中打开 `plugin/project.godot` 即可使用。

### 从 GDScript 调用

C# 脚本的方法、属性和信号是**按 C# 原名**暴露给 Godot 的，不会自动转成 snake_case，
所以 GDScript 里必须写成和 C# 一致的名字（`has_method()`/`connect()` 也是）：

```gdscript
$WebView.LoadUrl("res://Demo/character_creator/ui/index.html")  # 不是 load_url()
$WebView.OpenDevTools()
$WebView.PostMessage("hello")
$WebView.connect("IpcMessage", _on_ipc)                          # IpcMessage / PageLoadStarted / PageLoadFinished
```

场景文件里同理：`Url = "..."`、`Transparent = true`（不是 `url` / `transparent`）。

可用方法：`LoadUrl` / `LoadHtml` / `Reload` / `GoBack` / `GoForward` / `EvaluateJavaScript` /
`PostMessage` / `OpenDevTools` / `CloseDevTools` / `IsDevToolsOpen` / `Zoom` / `Print` /
`FocusWebView` / `FocusParent` / `SetWebViewVisible` / `ClearAllBrowsingData`。

> `SetWebViewVisible` 对应 godot_wry 的 `set_visible()`：只切原生 webview 的可见性。之所以不叫
> `SetVisible`，是因为 `Control` 基类本来就有 `set_visible`（`visible` 属性的 setter），同名会把基类
> 方法遮蔽掉、让 `$WebView.set_visible(false)` 的含义变得反直觉。控件自身 `visible` 变化时的同步是自动的。

## 导出（部署）

网页资源（html / css / js / wasm / 字体 / 图片…）默认**不会**被打进导出包 —— Godot 只导出它认识的资源类型。
所以导出前必须把 web 目录加进白名单：

**项目 → 导出 → 选中预设 → 资源 → 「导出非资源文件/文件夹的过滤器」**，例如：

```
*.html, *.css, *.js, *.mjs, *.json, *.wasm, *.svg, *.png, *.webp, *.ttf, *.woff2, Demo/character_creator/ui/**
```

以本仓库的 character_creator 为例，它是个 SvelteKit 构建产物，里面有 `_app/immutable/chunks/*.js`
这类深层目录，直接写目录通配 `Demo/character_creator/ui/**` 最省事。

### 导出后为什么 res:// 还能用

导出后 `res://` 的内容位于 `.pck` 内，磁盘上并没有对应文件。插件的资源服务**不映射文件夹**，
而是拦截 `https://godot.local/*`（`res://`）与 `https://godot-user.local/*`（`user://`）这两个请求，
再用 Godot 的 `FileAccess` 读内容 —— `FileAccess` 能读 `.pck`，所以：

- 不需要把 web 文件放到 exe 旁边，也不需要改 `Url` 的写法（`res://Demo/.../index.html` 照写）
- 不需要 `--disable-web-security` 之类的开关
- 「导出为独立 PCK」和「打包进 exe」两种模式都可以
- 目录形式也能用：`LoadUrl("res://Demo/character_creator/ui")` 会自动回退到该目录下的 `index.html`

### 目标机器的要求

- **WebView2 Runtime**：Windows 11 自带；Windows 10 需要装一次
  （<https://developer.microsoft.com/microsoft-edge/webview2/>）
- 调试开关在导出包里照样生效：`DevToolsEnabled = true`（默认）时可用 F12 或右键菜单打开 DevTools

### 出问题时的排查顺序

1. 页面 404 / 白屏 → 十有八九是上面的 Filters 漏了扩展名（在 webview 里按 F12 看 Network 面板最直接）
2. 启动即报 `Failed to create WebView2 controller` → 目标机没装 WebView2 Runtime
3. 报找不到 `WebView2Loader.dll` → 把 NuGet 包（`microsoft.web.webview2`）里的
   `runtimes/win-x64/native/WebView2Loader.dll` 拷到 exe 同目录
4. 正常启动时日志里会有两行资源服务映射：`Resource server: https://godot.local/ -> res://, ...`

## 参考项目

- [GDCefGlue](https://github.com/youfch/GDCefGlue) — Godot + CEF (Chromium) 浏览器控件，本项目 GDExtension 架构参考
- [godot_wry](https://github.com/MikeSchulze/godot_wry) — Godot + WRY (Rust) 系统 WebView
- [NativeWebView](https://github.com/Incari/NativeWebView) — 跨平台系统 WebView C# 封装

## License

MIT
# GodotWebViewSharp

Godot 4.x native system WebView plugin, supporting **C# Plugin Mode** (Godot.NET.Sdk) and **GDExtension Native Mode** (AOT).

## Architecture

```
GodotWebViewSharp/
├── plugin/                          ← C# Plugin Mode (Godot.NET.Sdk)
│   ├── addons/godot_webview_sharp/  ← Plugin source
│   │   ├── WebViewControl.cs         Main control
│   │   ├── WindowsWebViewBackend.cs  WebView2 (Windows)
│   │   ├── Platform/MacWebViewBackend.cs WKWebView (macOS)
│   │   ├── Platform/LinuxWebViewBackend.cs WebKitGTK (Linux)
│   │   └── InputEventRouter.cs      Input event forwarding
│   ├── Demo/                        Demo scene
│   └── project.godot
│
├── extension/                    ← GDExtension Mode
│   ├── GDEWebView.csproj         AOT native project
│   ├── Main.cs                   Entry point [UnmanagedCallersOnly]
│   ├── WebViewControl.cs         Control skeleton
│   ├── WebViewControl.Bindings.cs Property/method/signal registration
│   ├── WebViewControl.Initialization.cs Lifecycle
│   ├── WebViewControl.Navigation.cs Navigation API
│   ├── IWebViewBackend.cs        Backend interface
│   ├── Platform/
│   │   ├── WindowsWebViewBackend.cs  WebView2 backend
│   │   ├── NativeWindowMethods.cs    Cross-platform window operations
│   │   ├── MacWebViewBackend.cs     WKWebView stub
│   │   └── LinuxWebViewBackend.cs   WebKitGTK stub
│   └── Dll/                        godot-dotnet bindings (build required)
│
├── test/GDExtensionGame/           ← GDExtension test project
│   ├── project.godot
│   ├── lib/gde_webview.gdextension
│   └── MainScript.gd
│
└── README.md
```

## Mode Comparison

| Feature | C# Plugin (plugin/) | GDExtension (extension/) |
|---------|---------------------|-------------------------|
| Project SDK | `Godot.NET.Sdk` | `Microsoft.NET.Sdk` + `PublishAot` |
| Entry Method | Godot Mono auto-load | `[UnmanagedCallersOnly]` native entry |
| Property Registration | `[Export]` attribute | `ClassRegistrationContext.BindProperty` |
| Build Output | .dll managed assembly | AOT native .dll (no JIT) |
| Runtime Dependency | .NET SDK / Mono runtime | None (standalone native lib) |
| Use Case | Rapid development | Deployment, performance-critical |
| Reference | Native architecture | GDCefGlue extension/ pattern |

## Windows Platform

Based on **Microsoft Edge WebView2** (built-in), supports:
- EmbeddedWindow mode — high performance, video/WebGL support
- JavaScript ↔ C# IPC (window.chrome.webview.postMessage)
- Input event forwarding (mouse/keyboard → Godot)
- Virtual host mapping (`godot.local` → Godot project `res://`)
- F12 DevTools
- Transparent background (Experimental)

## GDExtension Build

### Prerequisites

1. **Godot 4.6+** with .NET support
2. **.NET 8 SDK** or later
3. **WebView2 Runtime** (Windows 11 built-in, Win10 requires install)

### Get godot-dotnet Bindings

```bash
git clone https://github.com/raulsntos/godot-dotnet.git
cd godot-dotnet
dotnet build -p:GenerateGodotBindings=true
```

Copy the generated `Godot.Bindings.dll` and related files to `extension/Dll/`.

### Build

```bash
cd extension
dotnet publish -c Release -r win-x64 --self-contained true
```

Or use the build script:

```bash
extension\build.bat publish
```

### Deploy

Copy `extension/bin/Release/net8.0/win-x64/publish/` contents to
`test/GDExtensionGame/lib/`, then open `test/GDExtensionGame/` with Godot 4.6+.

## C# Plugin Mode

```bash
cd plugin
dotnet build
```

Then open `plugin/project.godot` in the Godot editor.

## Reference Projects

- [GDCefGlue](https://github.com/youfch/GDCefGlue) — Godot + CEF (Chromium) browser control, GDExtension architecture reference
- [godot_wry](https://github.com/MikeSchulze/godot_wry) — Godot + WRY (Rust) system WebView
- [NativeWebView](https://github.com/Incari/NativeWebView) — Cross-platform system WebView C# wrapper

## License

MIT
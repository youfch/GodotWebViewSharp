using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Godot;
using Godot.Bridge;

[assembly: DisableGodotEntryPointGeneration]
[assembly: DisableRuntimeMarshalling]

namespace GDEWebView;

/// <summary>
/// GDEWebView GDExtension 入口点。
/// 对标 GDCefGlue 的 extension/Main.cs 结构：
/// - [UnmanagedCallersOnly] 为 Godot 原生加载入口
/// - GodotBridge.Initialize 注册类型初始化器
/// - RegisterClass<WebViewControl> 注册自定义节点
/// </summary>
public class Main
{
    public static void InitializeWebViewTypes(InitializationLevel level)
    {
        if (level != InitializationLevel.Scene)
            return;

        GodotRegistry.RegisterClass<WebViewControl>(WebViewControl.BindMembers);
    }

    public static void DeinitializeWebViewTypes(InitializationLevel level)
    {
        // 清理资源（如有必要）
    }

    /// <summary>
    /// Godot GDExtension 原生入口点。
    /// 函数名与 .gdextension 文件中的 entry_symbol 一致。
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "gde_webview_library_init")]
    public static bool GDEWebViewLibraryInit(nint getProcAddress, nint library, nint initialization)
    {
        GodotBridge.Initialize(getProcAddress, library, initialization, config =>
        {
            config.SetMinimumLibraryInitializationLevel(InitializationLevel.Scene);
            config.RegisterInitializer(InitializeWebViewTypes);
            config.RegisterTerminator(DeinitializeWebViewTypes);
        });

        return true;
    }
}
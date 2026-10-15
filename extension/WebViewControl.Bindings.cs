using Godot;
using Godot.Bridge;

namespace GDEWebView;

public partial class WebViewControl
{
    internal static void BindMembers(ClassRegistrationContext context)
    {
        context.BindConstructor(() => new WebViewControl());

        // ══════════════════════════════════════════
        //  属性分组: Browser Settings
        // ══════════════════════════════════════════
        context.AddPropertyGroup("Browser Settings");

        context.BindProperty(
            new PropertyDefinition(new StringName(nameof(Url)), VariantType.String),
            static (WebViewControl i) => i.Url,
            static (WebViewControl i, string v) => i.Url = v);

        context.BindProperty(
            new PropertyDefinition(new StringName(nameof(Html)), VariantType.String),
            static (WebViewControl i) => i.Html,
            static (WebViewControl i, string v) => i.Html = v);

        context.BindProperty(
            new PropertyDefinition(new StringName(nameof(DataDirectory)), VariantType.String),
            static (WebViewControl i) => i.DataDirectory,
            static (WebViewControl i, string v) => i.DataDirectory = v);

        context.BindProperty(
            new PropertyDefinition(new StringName(nameof(DevToolsEnabled)), VariantType.Bool),
            static (WebViewControl i) => i.DevToolsEnabled,
            static (WebViewControl i, bool v) => i.DevToolsEnabled = v);

        context.BindProperty(
            new PropertyDefinition(new StringName(nameof(UserAgent)), VariantType.String),
            static (WebViewControl i) => i.UserAgent,
            static (WebViewControl i, string v) => i.UserAgent = v);

        context.BindProperty(
            new PropertyDefinition(new StringName(nameof(Transparent)), VariantType.Bool),
            static (WebViewControl i) => i.Transparent,
            static (WebViewControl i, bool v) => i.Transparent = v);

        context.BindProperty(
            new PropertyDefinition(new StringName(nameof(Incognito)), VariantType.Bool),
            static (WebViewControl i) => i.Incognito,
            static (WebViewControl i, bool v) => i.Incognito = v);

        context.BindProperty(
            new PropertyDefinition(new StringName(nameof(ForwardInputEvents)), VariantType.Bool),
            static (WebViewControl i) => i.ForwardInputEvents,
            static (WebViewControl i, bool v) => i.ForwardInputEvents = v);

        context.BindProperty(
            new PropertyDefinition(new StringName(nameof(Autoplay)), VariantType.Bool),
            static (WebViewControl i) => i.Autoplay,
            static (WebViewControl i, bool v) => i.Autoplay = v);

        context.BindProperty(
            new PropertyDefinition(new StringName(nameof(BackgroundColor)), VariantType.Color),
            static (WebViewControl i) => i.BackgroundColor,
            static (WebViewControl i, Color v) => i.BackgroundColor = v);

        // ══════════════════════════════════════════
        //  只读属性：注册一个 getter 方法，setter 名留空
        // ══════════════════════════════════════════
        context.BindMethod(new StringName("GetCurrentUrl"),
            new ReturnDefinition(VariantType.String),
            static (WebViewControl i) => i.CurrentUrl);
        context.BindProperty(new PropertyDefinitionWithAccessors(
            new StringName(nameof(CurrentUrl)), VariantType.String,
            new StringName("GetCurrentUrl"), new StringName(""))
        {
            Usage = PropertyUsageFlags.Default,
        });

        context.BindMethod(new StringName("GetIsLoading"),
            new ReturnDefinition(VariantType.Bool),
            static (WebViewControl i) => i.IsLoading);
        context.BindProperty(new PropertyDefinitionWithAccessors(
            new StringName(nameof(IsLoading)), VariantType.Bool,
            new StringName("GetIsLoading"), new StringName(""))
        {
            Usage = PropertyUsageFlags.Default,
        });

        // ══════════════════════════════════════════
        //  方法
        // ══════════════════════════════════════════
        context.BindMethod(new StringName(nameof(GoBack)),
            static (WebViewControl i) => i.GoBack());

        context.BindMethod(new StringName(nameof(GoForward)),
            static (WebViewControl i) => i.GoForward());

        context.BindMethod(new StringName(nameof(NavigateToUrl)),
            new ParameterDefinition(new StringName("url"), VariantType.String),
            static (WebViewControl i, string url) => i.NavigateToUrl(url));

        context.BindMethod(new StringName(nameof(Reload)),
            new ParameterDefinition(new StringName("ignoreCache"), VariantType.Bool),
            static (WebViewControl i, bool ignoreCache) => i.Reload());

        context.BindMethod(new StringName(nameof(LoadUrl)),
            new ParameterDefinition(new StringName("url"), VariantType.String),
            static (WebViewControl i, string url) => i.LoadUrl(url));

        context.BindMethod(new StringName(nameof(LoadHtml)),
            new ParameterDefinition(new StringName("html"), VariantType.String),
            static (WebViewControl i, string html) => i.LoadHtml(html));

        context.BindMethod(new StringName(nameof(EvaluateJavaScript)),
            new ParameterDefinition(new StringName("script"), VariantType.String),
            static (WebViewControl i, string script) => i.EvaluateJavaScript(script));

        context.BindMethod(new StringName(nameof(PostMessage)),
            new ParameterDefinition(new StringName("message"), VariantType.String),
            static (WebViewControl i, string message) => i.PostMessage(message));

        context.BindMethod(new StringName(nameof(OpenDevTools)),
            static (WebViewControl i) => i.OpenDevTools());

        context.BindMethod(new StringName(nameof(CloseDevTools)),
            static (WebViewControl i) => i.CloseDevTools());

        context.BindMethod(new StringName(nameof(ClearAllBrowsingData)),
            static (WebViewControl i) => i.ClearAllBrowsingData());

        context.BindMethod(new StringName(nameof(FocusWebView)),
            static (WebViewControl i) => i.FocusWebView());

        context.BindMethod(new StringName(nameof(FocusParent)),
            static (WebViewControl i) => i.FocusParent());

        context.BindMethod(new StringName(nameof(Zoom)),
            new ParameterDefinition(new StringName("factor"), VariantType.Float),
            static (WebViewControl i, float factor) => i.Zoom(factor));

        context.BindMethod(new StringName(nameof(Print)),
            static (WebViewControl i) => i.Print());

        context.BindMethod(new StringName(nameof(SetWebViewVisible)),
            new ParameterDefinition(new StringName("visible"), VariantType.Bool),
            static (WebViewControl i, bool visible) => i.SetWebViewVisible(visible));

        context.BindMethod(new StringName(nameof(IsDevToolsOpen)),
            new ReturnDefinition(VariantType.Bool),
            static (WebViewControl i) => i.IsDevToolsOpen());

        // ══════════════════════════════════════════
        //  信号
        // ══════════════════════════════════════════
        context.BindSignal(SignalWithStringParam(nameof(IpcMessage), "message"));
        context.BindSignal(SignalWithStringParam(nameof(PageLoadStarted), "url"));
        context.BindSignal(SignalWithStringParam(nameof(PageLoadFinished), "url"));
    }

    private static SignalDefinition SignalWithStringParam(string name, string paramName)
    {
        SignalDefinition signal = new(new StringName(name));
        signal.Parameters.Add(new ParameterDefinition(new StringName(paramName), VariantType.String));
        return signal;
    }
}
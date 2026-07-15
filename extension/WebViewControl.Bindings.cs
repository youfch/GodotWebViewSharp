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
            new PropertyInfo(new StringName(nameof(Url)), VariantType.String)
            { Usage = PropertyUsageFlags.Default },
            static (WebViewControl i) => i.Url,
            static (WebViewControl i, string v) => i.Url = v);

        context.BindProperty(
            new PropertyInfo(new StringName(nameof(Html)), VariantType.String)
            { Usage = PropertyUsageFlags.Default },
            static (WebViewControl i) => i.Html,
            static (WebViewControl i, string v) => i.Html = v);

        context.BindProperty(
            new PropertyInfo(new StringName(nameof(DataDirectory)), VariantType.String)
            { Usage = PropertyUsageFlags.Default },
            static (WebViewControl i) => i.DataDirectory,
            static (WebViewControl i, string v) => i.DataDirectory = v);

        context.BindProperty(
            new PropertyInfo(new StringName(nameof(DevToolsEnabled)), VariantType.Bool)
            { Usage = PropertyUsageFlags.Default },
            static (WebViewControl i) => i.DevToolsEnabled,
            static (WebViewControl i, bool v) => i.DevToolsEnabled = v);

        context.BindProperty(
            new PropertyInfo(new StringName(nameof(UserAgent)), VariantType.String)
            { Usage = PropertyUsageFlags.Default },
            static (WebViewControl i) => i.UserAgent,
            static (WebViewControl i, string v) => i.UserAgent = v);

        context.BindProperty(
            new PropertyInfo(new StringName(nameof(Transparent)), VariantType.Bool)
            { Usage = PropertyUsageFlags.Default },
            static (WebViewControl i) => i.Transparent,
            static (WebViewControl i, bool v) => i.Transparent = v);

        context.BindProperty(
            new PropertyInfo(new StringName(nameof(Incognito)), VariantType.Bool)
            { Usage = PropertyUsageFlags.Default },
            static (WebViewControl i) => i.Incognito,
            static (WebViewControl i, bool v) => i.Incognito = v);

        context.BindProperty(
            new PropertyInfo(new StringName(nameof(ForwardInputEvents)), VariantType.Bool)
            { Usage = PropertyUsageFlags.Default },
            static (WebViewControl i) => i.ForwardInputEvents,
            static (WebViewControl i, bool v) => i.ForwardInputEvents = v);

        context.BindProperty(
            new PropertyInfo(new StringName(nameof(Autoplay)), VariantType.Bool)
            { Usage = PropertyUsageFlags.Default },
            static (WebViewControl i) => i.Autoplay,
            static (WebViewControl i, bool v) => i.Autoplay = v);

        context.BindProperty(
            new PropertyInfo(new StringName(nameof(BackgroundColor)), VariantType.Color)
            { Usage = PropertyUsageFlags.Default },
            static (WebViewControl i) => i.BackgroundColor,
            static (WebViewControl i, Color v) => i.BackgroundColor = v);

        // ══════════════════════════════════════════
        //  只读属性
        // ══════════════════════════════════════════
        context.BindProperty(
            new PropertyInfo(new StringName(nameof(CurrentUrl)), VariantType.String)
            { Usage = PropertyUsageFlags.Default },
            static (WebViewControl i) => i.CurrentUrl,
            null);

        context.BindProperty(
            new PropertyInfo(new StringName(nameof(IsLoading)), VariantType.Bool)
            { Usage = PropertyUsageFlags.Default },
            static (WebViewControl i) => i.IsLoading,
            null);

        // ══════════════════════════════════════════
        //  方法
        // ══════════════════════════════════════════
        context.BindMethod(new StringName(nameof(GoBack)),
            (WebViewControl i) => i.GoBack());

        context.BindMethod(new StringName(nameof(GoForward)),
            (WebViewControl i) => i.GoForward());

        context.BindMethod(new StringName(nameof(NavigateToUrl)),
            new ParameterInfo(new StringName("url"), VariantType.String),
            (WebViewControl i, string url) => i.NavigateToUrl(url));

        context.BindMethod(new StringName(nameof(Reload)),
            new ParameterInfo(new StringName("ignoreCache"), VariantType.Bool),
            (WebViewControl i, bool ignoreCache) => i.Reload());

        context.BindMethod(new StringName(nameof(LoadUrl)),
            new ParameterInfo(new StringName("url"), VariantType.String),
            (WebViewControl i, string url) => i.LoadUrl(url));

        context.BindMethod(new StringName(nameof(LoadHtml)),
            new ParameterInfo(new StringName("html"), VariantType.String),
            (WebViewControl i, string html) => i.LoadHtml(html));

        context.BindMethod(new StringName(nameof(EvaluateJavaScript)),
            new ParameterInfo(new StringName("script"), VariantType.String),
            (WebViewControl i, string script) => i.EvaluateJavaScript(script));

        context.BindMethod(new StringName(nameof(OpenDevTools)),
            (WebViewControl i) => i.OpenDevTools());

        context.BindMethod(new StringName(nameof(CloseDevTools)),
            (WebViewControl i) => i.CloseDevTools());

        context.BindMethod(new StringName(nameof(ClearAllBrowsingData)),
            (WebViewControl i) => i.ClearAllBrowsingData());

        context.BindMethod(new StringName(nameof(FocusWebView)),
            (WebViewControl i) => i.FocusWebView());

        // ══════════════════════════════════════════
        //  信号
        // ══════════════════════════════════════════
        context.BindSignal(new SignalInfo(new StringName(nameof(IpcMessage))));
        context.BindSignal(new SignalInfo(new StringName(nameof(PageLoadStarted))));
        context.BindSignal(new SignalInfo(new StringName(nameof(PageLoadFinished))));
    }
}
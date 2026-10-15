using Godot;
using System;
using System.Text.Json;

namespace GDEWebView;

/// <summary>
/// 输入事件转发（对标 godot_wry 的 ipc_handler 输入分支，以及 addon 版的 InputEventRouter）。
///
/// 原生 webview 是盖在最上层的子窗口，会吃掉鼠标/键盘事件；网页把事件通过 IPC 送回来，
/// 这里再注入 Godot 输入系统，于是 webview 覆盖区域的 Godot 控件依然可点。
///
/// 注意：这里用 System.Text.Json 解析载荷，而不是 Godot 的 Json + Variant 拆箱。
/// 旧实现踩过的坑：Godot 的 JSON 把所有数字都解析成 float 变体（Variant.Obj 装箱 Double），
/// (float)/(int) 强转会抛 InvalidCastException 并被吞掉，导致整条转发静默失效。
/// JsonDocument 是 AOT 友好的（无反射），也不会引入那类拆箱问题。
/// </summary>
internal static class InputEventRouter
{
    private static MouseButtonMask _currentButtonMask = (MouseButtonMask)0;
    private static bool _errorLogged;

    public static bool TryProcessInputEvent(string json, Control target)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement payload = doc.RootElement;

            if (payload.ValueKind != JsonValueKind.Object)
                return false;

            string type = Str(payload, "type");
            if (!type.StartsWith("_", StringComparison.Ordinal))
                return false;

            Viewport viewport = target.GetViewport();
            if (viewport == null)
                return true; // 消费掉，但不是有效事件

            // 网页坐标是 webview 视口的 CSS 像素，加上控件在自身视口里的原点
            Vector2 origin = target.GlobalPosition;
            float vpX = origin.X + (float)Num(payload, "x");
            float vpY = origin.Y + (float)Num(payload, "y");

            switch (type)
            {
                case "_mouse_move":
                    return HandleMouseMove(payload, vpX, vpY, viewport);

                case "_mouse_down":
                case "_mouse_up":
                    return HandleMouseButton(type, payload, vpX, vpY, viewport);

                case "_mouse_wheel":
                    return HandleMouseWheel(payload, vpX, vpY, viewport);

                case "_key_down":
                case "_key_up":
                    return HandleKeyboard(type, payload, viewport);

                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            // 不再静默吞掉：转发失败时至少留一次痕迹
            if (!_errorLogged)
            {
                _errorLogged = true;
                GD.PrintErr($"[GDEWebView] Input event forwarding failed: {ex.Message} (payload: {json})");
            }
            return false;
        }
    }

    private static bool HandleMouseMove(JsonElement payload, float vpX, float vpY, Viewport viewport)
    {
        var ev = new InputEventMouseMotion
        {
            Position = new Vector2(vpX, vpY),
            GlobalPosition = new Vector2(vpX, vpY),
            ButtonMask = _currentButtonMask,
            Relative = new Vector2((float)Num(payload, "movementX"), (float)Num(payload, "movementY")),
        };
        viewport.PushInput(ev);
        return true;
    }

    private static bool HandleMouseButton(string type, JsonElement payload, float vpX, float vpY, Viewport viewport)
    {
        MouseButton button = MapMouseButton(Int(payload, "button"));
        bool pressed = type == "_mouse_down";

        UpdateButtonMask(button, pressed);

        var ev = new InputEventMouseButton
        {
            ButtonIndex = button,
            Position = new Vector2(vpX, vpY),
            GlobalPosition = new Vector2(vpX, vpY),
            Pressed = pressed,
            ButtonMask = _currentButtonMask,
        };
        viewport.PushInput(ev);
        return true;
    }

    private static bool HandleMouseWheel(JsonElement payload, float vpX, float vpY, Viewport viewport)
    {
        float deltaX = (float)Num(payload, "deltaX");
        float deltaY = (float)Num(payload, "deltaY");
        var position = new Vector2(vpX, vpY);

        // 修饰键要带给滚轮事件，否则 Ctrl+滚轮 之类的操作会失效
        bool shift = Flag(payload, "shift");
        bool ctrl = Flag(payload, "ctrl");
        bool alt = Flag(payload, "alt");
        bool meta = Flag(payload, "meta");

        if (deltaY != 0.0f)
        {
            MouseButton button = deltaY < 0.0f ? MouseButton.WheelUp : MouseButton.WheelDown;
            float factor = Math.Max(Math.Abs(deltaY) / 100.0f, 1.0f);
            SendWheelEvent(button, position, factor, shift, ctrl, alt, meta, viewport);
        }

        if (deltaX != 0.0f)
        {
            MouseButton button = deltaX < 0.0f ? MouseButton.WheelLeft : MouseButton.WheelRight;
            float factor = Math.Max(Math.Abs(deltaX) / 100.0f, 1.0f);
            SendWheelEvent(button, position, factor, shift, ctrl, alt, meta, viewport);
        }

        return true;
    }

    private static void SendWheelEvent(
        MouseButton button, Vector2 position, float factor,
        bool shift, bool ctrl, bool alt, bool meta, Viewport viewport)
    {
        for (int i = 0; i < 2; i++)
        {
            var ev = new InputEventMouseButton
            {
                ButtonIndex = button,
                Position = position,
                GlobalPosition = position,
                Pressed = i == 0,
                Factor = factor,
                ButtonMask = _currentButtonMask,
                ShiftPressed = shift,
                CtrlPressed = ctrl,
                AltPressed = alt,
                MetaPressed = meta,
            };
            viewport.PushInput(ev);
        }
    }

    private static bool HandleKeyboard(string type, JsonElement payload, Viewport viewport)
    {
        var ev = new InputEventKey
        {
            Keycode = MapGodotKey(Str(payload, "key"), Str(payload, "code")),
            Pressed = type == "_key_down",
            ShiftPressed = Flag(payload, "shift"),
            CtrlPressed = Flag(payload, "ctrl"),
            AltPressed = Flag(payload, "alt"),
            MetaPressed = Flag(payload, "meta"),
        };
        viewport.PushInput(ev);
        return true;
    }

    // ═══════════════════════════════════════════════
    //  按键映射（对标 godot_wry 的 GODOT_KEYS）
    //
    //  以 e.key 为主（可打印字符传的就是字符本身），小键盘额外用 e.code：
    //  按 DOM 规范小键盘数字的 e.key 就是 "0"，和主键盘区分不了，只有 e.code 是 "Numpad0"。
    // ═══════════════════════════════════════════════

    private static Key MapGodotKey(string key, string code)
    {
        string lowerCode = code.ToLowerInvariant();
        if (lowerCode.StartsWith("numpad", StringComparison.Ordinal) &&
            NumpadByCode.TryGetValue(lowerCode, out Key numpadKey))
        {
            return numpadKey;
        }

        return MapByKey(key);
    }

    private static Key MapByKey(string key)
    {
        return key.ToLowerInvariant() switch
        {
            "a" => Key.A, "b" => Key.B, "c" => Key.C, "d" => Key.D, "e" => Key.E,
            "f" => Key.F, "g" => Key.G, "h" => Key.H, "i" => Key.I, "j" => Key.J,
            "k" => Key.K, "l" => Key.L, "m" => Key.M, "n" => Key.N, "o" => Key.O,
            "p" => Key.P, "q" => Key.Q, "r" => Key.R, "s" => Key.S, "t" => Key.T,
            "u" => Key.U, "v" => Key.V, "w" => Key.W, "x" => Key.X, "y" => Key.Y,
            "z" => Key.Z,

            "0" => Key.Key0, "1" => Key.Key1, "2" => Key.Key2,
            "3" => Key.Key3, "4" => Key.Key4, "5" => Key.Key5,
            "6" => Key.Key6, "7" => Key.Key7, "8" => Key.Key8, "9" => Key.Key9,

            "f1" => Key.F1, "f2" => Key.F2, "f3" => Key.F3, "f4" => Key.F4,
            "f5" => Key.F5, "f6" => Key.F6, "f7" => Key.F7, "f8" => Key.F8,
            "f9" => Key.F9, "f10" => Key.F10, "f11" => Key.F11, "f12" => Key.F12,
            "f13" => Key.F13, "f14" => Key.F14, "f15" => Key.F15, "f16" => Key.F16,
            "f17" => Key.F17, "f18" => Key.F18, "f19" => Key.F19, "f20" => Key.F20,
            "f21" => Key.F21, "f22" => Key.F22, "f23" => Key.F23, "f24" => Key.F24,

            "arrowup" => Key.Up, "arrowdown" => Key.Down,
            "arrowleft" => Key.Left, "arrowright" => Key.Right,

            "enter" => Key.Enter, "tab" => Key.Tab,
            "space" or " " => Key.Space,
            "backspace" => Key.Backspace, "escape" => Key.Escape,
            "capslock" => Key.Capslock, "scrolllock" => Key.Scrolllock,
            "numlock" => Key.Numlock, "printscreen" => Key.Print,
            "pause" => Key.Pause, "insert" => Key.Insert,
            "home" => Key.Home, "pageup" => Key.Pageup,
            "delete" => Key.Delete, "end" => Key.End,
            "pagedown" => Key.Pagedown,

            "shift" => Key.Shift, "control" => Key.Ctrl,
            "alt" => Key.Alt, "altgraph" => Key.Alt,
            "meta" => Key.Meta, "contextmenu" => Key.Menu,

            "`" => Key.Quoteleft, "~" => Key.Asciitilde,
            "!" => Key.Exclam, "@" => Key.At, "#" => Key.Numbersign,
            "$" => Key.Dollar, "%" => Key.Percent, "^" => Key.Asciicircum,
            "&" => Key.Ampersand, "*" => Key.Asterisk,
            "(" => Key.Parenleft, ")" => Key.Parenright,
            "-" => Key.Minus, "_" => Key.Underscore,
            "=" => Key.Equal, "+" => Key.Plus,
            "[" => Key.Bracketleft, "{" => Key.Braceleft,
            "]" => Key.Bracketright, "}" => Key.Braceright,
            "\\" => Key.Backslash, "|" => Key.Bar,
            ";" => Key.Semicolon, ":" => Key.Colon,
            "'" => Key.Apostrophe, "\"" => Key.Quotedbl,
            "," => Key.Comma, "<" => Key.Less,
            "." => Key.Period, ">" => Key.Greater,
            "/" => Key.Slash, "?" => Key.Question,

            "mediaplaypause" or "mediaplay" => Key.Mediaplay,
            "mediastop" => Key.Mediastop,
            "mediatracknext" => Key.Medianext,
            "mediatrackprevious" => Key.Mediaprevious,
            "mediarecord" => Key.Mediarecord,
            "volumedown" => Key.Volumedown,
            "volumeup" => Key.Volumeup,
            "volumemute" => Key.Volumemute,

            "browserback" => Key.Back,
            "browserforward" => Key.Forward,
            "browserrefresh" => Key.Refresh,
            "browserstop" => Key.Stop,
            "browsersearch" => Key.Search,
            "browserhome" => Key.Homepage,
            "launchmail" => Key.Launchmail,
            "launchmediaplayer" => Key.Launchmedia,

            _ => Key.None,
        };
    }

    private static readonly System.Collections.Generic.Dictionary<string, Key> NumpadByCode =
        new(StringComparer.Ordinal)
        {
            ["numpad0"] = Key.Kp0, ["numpad1"] = Key.Kp1, ["numpad2"] = Key.Kp2,
            ["numpad3"] = Key.Kp3, ["numpad4"] = Key.Kp4, ["numpad5"] = Key.Kp5,
            ["numpad6"] = Key.Kp6, ["numpad7"] = Key.Kp7, ["numpad8"] = Key.Kp8,
            ["numpad9"] = Key.Kp9,
            ["numpadenter"] = Key.KpEnter,
            ["numpadadd"] = Key.KpAdd,
            ["numpadsubtract"] = Key.KpSubtract,
            ["numpadmultiply"] = Key.KpMultiply,
            ["numpaddivide"] = Key.KpDivide,
            ["numpaddecimal"] = Key.KpPeriod,
        };

    private static MouseButton MapMouseButton(int button)
    {
        return button switch
        {
            0 => MouseButton.Left,
            1 => MouseButton.Middle,
            2 => MouseButton.Right,
            3 => MouseButton.WheelUp,
            4 => MouseButton.WheelDown,
            _ => MouseButton.Left,
        };
    }

    private static void UpdateButtonMask(MouseButton button, bool pressed)
    {
        int maskInt = button switch
        {
            MouseButton.Left => 1,
            MouseButton.Right => 2,
            MouseButton.Middle => 4,
            _ => -1,
        };

        if (maskInt == -1)
            return;

        int current = (int)_currentButtonMask;

        if (pressed)
            current |= maskInt;
        else
            current &= ~maskInt;

        _currentButtonMask = (MouseButtonMask)current;
    }

    // ═══════════════════════════════════════════════
    //  JSON 取值（类型不匹配一律走默认值，不抛异常）
    // ═══════════════════════════════════════════════

    private static double Num(JsonElement payload, string name)
        => payload.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : 0.0;

    private static int Int(JsonElement payload, string name)
        => payload.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number
            ? (int)v.GetDouble()
            : 0;

    private static bool Flag(JsonElement payload, string name)
        => payload.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.True;

    private static string Str(JsonElement payload, string name)
        => payload.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";
}
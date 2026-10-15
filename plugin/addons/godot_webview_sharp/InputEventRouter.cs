using Godot;
using Godot.Collections;
using System;
using static Godot.DisplayServer;

namespace WebViewSharp
{
    /// <summary>
    /// 输入事件转发。
    /// 将 WebView 中捕获的鼠标/键盘事件转发到 Godot 输入系统。
    ///
    /// 直接参考 godot_wry (lib.rs) 的 JS IPC input 事件处理逻辑：
    /// - _mouse_move → InputEventMouseMotion
    /// - _mouse_down/_mouse_up → InputEventMouseButton
    /// - _mouse_wheel → InputEventMouseButton (WHEEL_UP/WHEEL_DOWN)
    /// - _key_down/_key_up → InputEventKey
    /// </summary>
    internal static class InputEventRouter
    {
        private static MouseButtonMask _currentButtonMask = (MouseButtonMask)0;
        private static bool _errorLogged;

        /// <summary>
        /// 尝试将 WebView IPC 消息作为输入事件处理。
        /// 返回 true 表示已处理（是输入事件），false 表示是普通 IPC 消息。
        /// </summary>
        public static bool TryProcessInputEvent(string json, Control target)
        {
            try
            {
                var jsonObj = Json.ParseString(json);
                if (jsonObj.VariantType != Variant.Type.Dictionary)
                    return false;

                var dict = jsonObj.AsGodotDictionary();
                if (!dict.ContainsKey("type"))
                    return false;

                string type = dict["type"].AsString();
                if (!type.StartsWith("_"))
                    return false;

                var viewport = target.GetViewport();
                if (viewport == null) return true; // 消费掉，但不是有效事件

                float x = (float)Num(dict, "x");
                float y = (float)Num(dict, "y");
                // 网页坐标是 WebView 视口的 CSS 像素，加上控件在**自身视口**里的原点
                // （子窗口内的 Control，GlobalPosition 是子窗口视口坐标，PushInput 也发给同一个视口，两者一致）
                var globalPos = target.GlobalPosition;
                float vpX = globalPos.X + x;
                float vpY = globalPos.Y + y;

                switch (type)
                {
                    case "_mouse_move":
                        return HandleMouseMove(dict, vpX, vpY, viewport);

                    case "_mouse_down":
                    case "_mouse_up":
                        return HandleMouseButton(type, dict, vpX, vpY, viewport);

                    case "_mouse_wheel":
                        return HandleMouseWheel(dict, vpX, vpY, viewport);

                    case "_key_down":
                    case "_key_up":
                        return HandleKeyboard(type, dict, viewport);

                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                // 不再静默吞掉：转发失败时至少留下一次痕迹，否则表现为"点击穿透静默失效"
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    GD.PrintErr($"[WebViewSharp] Input event forwarding failed: {ex.Message} (payload: {json})");
                }
                return false;
            }
        }

        private static bool HandleMouseMove(Dictionary dict, float vpX, float vpY, Viewport viewport)
        {
            float movementX = (float)Num(dict, "movementX");
            float movementY = (float)Num(dict, "movementY");

            var ev = new InputEventMouseMotion
            {
                Position = new Vector2(vpX, vpY),
                GlobalPosition = new Vector2(vpX, vpY),
                ButtonMask = _currentButtonMask,
                Relative = new Vector2(movementX, movementY)
            };
            viewport.PushInput(ev);
            return true;
        }

        private static bool HandleMouseButton(string type, Dictionary dict, float vpX, float vpY, Viewport viewport)
        {
            int button = Int(dict, "button");
            var godotButton = MapMouseButton(button);
            bool pressed = type == "_mouse_down";

            // 更新按钮掩码
            UpdateButtonMask(godotButton, pressed);

            var ev = new InputEventMouseButton
            {
                ButtonIndex = godotButton,
                Position = new Vector2(vpX, vpY),
                GlobalPosition = new Vector2(vpX, vpY),
                Pressed = pressed,
                ButtonMask = _currentButtonMask
            };
            viewport.PushInput(ev);
            return true;
        }

        private static bool HandleMouseWheel(Dictionary dict, float vpX, float vpY, Viewport viewport)
        {
            float deltaX = (float)Num(dict, "deltaX");
            float deltaY = (float)Num(dict, "deltaY");
            var position = new Vector2(vpX, vpY);

            // 修饰键要带给滚轮事件（godot_wry 也这么做；缺了会让 Ctrl+滚轮之类的操作失效）
            var modifiers = (Flag(dict, "shift"), Flag(dict, "ctrl"), Flag(dict, "alt"), Flag(dict, "meta"));

            if (deltaY != 0.0f)
            {
                var button = deltaY < 0.0f ? MouseButton.WheelUp : MouseButton.WheelDown;
                float factor = Math.Max(Math.Abs(deltaY) / 100.0f, 1.0f);
                SendWheelEvent(button, position, factor, modifiers, viewport);
            }

            if (deltaX != 0.0f)
            {
                var button = deltaX < 0.0f ? MouseButton.WheelLeft : MouseButton.WheelRight;
                float factor = Math.Max(Math.Abs(deltaX) / 100.0f, 1.0f);
                SendWheelEvent(button, position, factor, modifiers, viewport);
            }

            return true;
        }

        private static void SendWheelEvent(
            MouseButton button, Vector2 position, float factor,
            (bool shift, bool ctrl, bool alt, bool meta) modifiers, Viewport viewport)
        {
            for (int i = 0; i < 2; i++)
            {
                bool pressed = i == 0;
                var ev = new InputEventMouseButton
                {
                    ButtonIndex = button,
                    Position = position,
                    GlobalPosition = position,
                    Pressed = pressed,
                    Factor = factor,
                    ButtonMask = _currentButtonMask,
                    ShiftPressed = modifiers.shift,
                    CtrlPressed = modifiers.ctrl,
                    AltPressed = modifiers.alt,
                    MetaPressed = modifiers.meta
                };
                viewport.PushInput(ev);
            }
        }

        private static bool HandleKeyboard(string type, Dictionary dict, Viewport viewport)
        {
            string keyStr = Str(dict, "key");
            string code = Str(dict, "code");

            var godotKey = MapGodotKey(keyStr, code);

            var ev = new InputEventKey
            {
                Keycode = godotKey,
                Pressed = type == "_key_down",
                ShiftPressed = Flag(dict, "shift"),
                CtrlPressed = Flag(dict, "ctrl"),
                AltPressed = Flag(dict, "alt"),
                MetaPressed = Flag(dict, "meta")
            };
            viewport.PushInput(ev);
            return true;
        }

        // ═══════════════════════════════════════════════
        //  按键映射（对标 godot_wry 的 GODOT_KEYS HashMap）
        //
        //  以 e.key 为主（可打印字符传的就是字符本身），小键盘额外用 e.code：
        //  按 DOM 规范，小键盘数字的 e.key 就是 "0"，和主键盘区无法区分，
        //  只有 e.code 是 "Numpad0"，所以 code 里带 numpad 时优先按 code 映射。
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
                // ── 字母 / 数字 ──
                "a" => Key.A, "b" => Key.B, "c" => Key.C, "d" => Key.D, "e" => Key.E,
                "f" => Key.F, "g" => Key.G, "h" => Key.H, "i" => Key.I, "j" => Key.J,
                "k" => Key.K, "l" => Key.L, "m" => Key.M, "n" => Key.N, "o" => Key.O,
                "p" => Key.P, "q" => Key.Q, "r" => Key.R, "s" => Key.S, "t" => Key.T,
                "u" => Key.U, "v" => Key.V, "w" => Key.W, "x" => Key.X, "y" => Key.Y,
                "z" => Key.Z,

                "0" => Key.Key0, "1" => Key.Key1, "2" => Key.Key2,
                "3" => Key.Key3, "4" => Key.Key4, "5" => Key.Key5,
                "6" => Key.Key6, "7" => Key.Key7, "8" => Key.Key8, "9" => Key.Key9,

                // ── 功能键 ──
                "f1" => Key.F1, "f2" => Key.F2, "f3" => Key.F3, "f4" => Key.F4,
                "f5" => Key.F5, "f6" => Key.F6, "f7" => Key.F7, "f8" => Key.F8,
                "f9" => Key.F9, "f10" => Key.F10, "f11" => Key.F11, "f12" => Key.F12,
                "f13" => Key.F13, "f14" => Key.F14, "f15" => Key.F15, "f16" => Key.F16,
                "f17" => Key.F17, "f18" => Key.F18, "f19" => Key.F19, "f20" => Key.F20,
                "f21" => Key.F21, "f22" => Key.F22, "f23" => Key.F23, "f24" => Key.F24,

                // ── 方向 / 编辑 ──
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

                // ── 修饰键 ──
                "shift" => Key.Shift, "control" => Key.Ctrl,
                "alt" => Key.Alt, "altgraph" => Key.Alt,
                "meta" => Key.Meta, "contextmenu" => Key.Menu,

                // ── 符号（e.key 直接给字符） ──
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

                // ── 媒体键 ──
                "mediaplaypause" or "mediaplay" => Key.Mediaplay,
                "mediastop" => Key.Mediastop,
                "mediatracknext" => Key.Medianext,
                "mediatrackprevious" => Key.Mediaprevious,
                "mediarecord" => Key.Mediarecord,
                "volumedown" => Key.Volumedown,
                "volumeup" => Key.Volumeup,
                "volumemute" => Key.Volumemute,

                // ── 浏览器键 ──
                "browserback" => Key.Back,
                "browserforward" => Key.Forward,
                "browserrefresh" => Key.Refresh,
                "browserstop" => Key.Stop,
                "browsersearch" => Key.Search,
                "browserhome" => Key.Homepage,
                "launchmail" => Key.Launchmail,
                "launchmediaplayer" => Key.Launchmedia,

                _ => Key.None
            };
        }

        // 小键盘按 e.code 映射（e.key 区分不出来）
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
                _ => MouseButton.Left
            };
        }

        private static void UpdateButtonMask(MouseButton button, bool pressed)
        {
            int maskInt = button switch
            {
                MouseButton.Left => 1,     // MouseButtonMask.Left
                MouseButton.Right => 2,    // MouseButtonMask.Right
                MouseButton.Middle => 4,   // MouseButtonMask.Middle
                _ => -1
            };

            if (maskInt == -1) return;

            // 注意：不能写成 (int)(object)enum —— 装箱后拆箱到 Int32 会抛
            // InvalidCastException（探针实测），这是与 Variant.Obj 同族的坑。
            int current = (int)_currentButtonMask;

            if (pressed)
                current |= maskInt;
            else
                current &= ~maskInt;

            _currentButtonMask = (MouseButtonMask)current;
        }

        // ═══════════════════════════════════════════════
        //  Dictionary 取值
        //
        //  Godot 的 JSON 解析把**所有**数字都存成 float 变体
        //  （实测 {"x":5} → VariantType.Float，Variant.Obj 装箱的是 System.Double），
        //  所以绝不能写 (T)val.Obj 这种拆箱——(float)/(int) 都会抛
        //  InvalidCastException，被上面的 catch 吞掉，输入转发会整体失效。
        // ═══════════════════════════════════════════════

        private static double Num(Dictionary dict, string key)
            => dict.ContainsKey(key) ? dict[key].AsDouble() : 0.0;

        private static int Int(Dictionary dict, string key)
            => dict.ContainsKey(key) ? dict[key].AsInt32() : 0;

        private static bool Flag(Dictionary dict, string key)
            => dict.ContainsKey(key) && dict[key].AsBool();

        private static string Str(Dictionary dict, string key)
            => dict.ContainsKey(key) ? dict[key].AsString() : "";
    }
}
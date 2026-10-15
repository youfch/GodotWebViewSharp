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

            if (deltaY != 0.0f)
            {
                var button = deltaY < 0.0f ? MouseButton.WheelUp : MouseButton.WheelDown;
                float factor = Math.Max(Math.Abs(deltaY) / 100.0f, 1.0f);
                SendWheelEvent(button, position, factor, viewport);
            }

            if (deltaX != 0.0f)
            {
                var button = deltaX < 0.0f ? MouseButton.WheelLeft : MouseButton.WheelRight;
                float factor = Math.Max(Math.Abs(deltaX) / 100.0f, 1.0f);
                SendWheelEvent(button, position, factor, viewport);
            }

            return true;
        }

        private static void SendWheelEvent(MouseButton button, Vector2 position, float factor, Viewport viewport)
        {
            foreach (bool pressed in new[] { true, false })
            {
                var ev = new InputEventMouseButton
                {
                    ButtonIndex = button,
                    Position = position,
                    GlobalPosition = position,
                    Pressed = pressed,
                    Factor = factor,
                    ButtonMask = _currentButtonMask
                };
                viewport.PushInput(ev);
            }
        }

        private static bool HandleKeyboard(string type, Dictionary dict, Viewport viewport)
        {
            string keyStr = Str(dict, "key");
            var godotKey = MapGodotKey(keyStr);

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
        // ═══════════════════════════════════════════════

        private static Key MapGodotKey(string key)
        {
            return key.ToLowerInvariant() switch
            {
                "a" or "A" => Key.A,
                "b" or "B" => Key.B,
                "c" or "C" => Key.C,
                "d" or "D" => Key.D,
                "e" or "E" => Key.E,
                "f" or "F" => Key.F,
                "g" or "G" => Key.G,
                "h" or "H" => Key.H,
                "i" or "I" => Key.I,
                "j" or "J" => Key.J,
                "k" or "K" => Key.K,
                "l" or "L" => Key.L,
                "m" or "M" => Key.M,
                "n" or "N" => Key.N,
                "o" or "O" => Key.O,
                "p" or "P" => Key.P,
                "q" or "Q" => Key.Q,
                "r" or "R" => Key.R,
                "s" or "S" => Key.S,
                "t" or "T" => Key.T,
                "u" or "U" => Key.U,
                "v" or "V" => Key.V,
                "w" or "W" => Key.W,
                "x" or "X" => Key.X,
                "y" or "Y" => Key.Y,
                "z" or "Z" => Key.Z,

                "0" => Key.Key0, "1" => Key.Key1, "2" => Key.Key2,
                "3" => Key.Key3, "4" => Key.Key4, "5" => Key.Key5,
                "6" => Key.Key6, "7" => Key.Key7, "8" => Key.Key8, "9" => Key.Key9,

                "f1" => Key.F1, "f2" => Key.F2, "f3" => Key.F3,
                "f4" => Key.F4, "f5" => Key.F5, "f6" => Key.F6,
                "f7" => Key.F7, "f8" => Key.F8, "f9" => Key.F9,
                "f10" => Key.F10, "f11" => Key.F11, "f12" => Key.F12,

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
                "alt" => Key.Alt, "meta" => Key.Meta,
                "contextmenu" => Key.Menu,

                _ => Key.None
            };
        }

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
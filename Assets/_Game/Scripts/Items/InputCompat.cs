using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace NscUnity.Items
{
    /// <summary>
    /// ตัวกลางอ่าน Input ให้ทำงานได้ทั้ง Input Manager (เก่า) และ New Input System
    /// เพื่อไม่ให้สคริปต์ระบบไอเทมพังเวลาสลับโหมด Input ใน Project Settings
    /// </summary>
    public static class InputCompat
    {
        public static Vector2 MousePosition
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                if (Mouse.current != null) return Mouse.current.position.ReadValue();
#endif
                return Input.mousePosition;
            }
        }

        public static bool GetKeyDown(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            ButtonControl control = ResolveKey(key);
            if (control != null) return control.wasPressedThisFrame;
#endif
            return Input.GetKeyDown(key);
        }

        public static bool GetKey(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            ButtonControl control = ResolveKey(key);
            if (control != null) return control.isPressed;
#endif
            return Input.GetKey(key);
        }

        public static bool GetKeyUp(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            ButtonControl control = ResolveKey(key);
            if (control != null) return control.wasReleasedThisFrame;
#endif
            return Input.GetKeyUp(key);
        }

        public static bool GetMouseButtonDown(int button)
        {
#if ENABLE_INPUT_SYSTEM
            ButtonControl control = ResolveMouse(button);
            if (control != null) return control.wasPressedThisFrame;
#endif
            return Input.GetMouseButtonDown(button);
        }

        public static bool GetMouseButton(int button)
        {
#if ENABLE_INPUT_SYSTEM
            ButtonControl control = ResolveMouse(button);
            if (control != null) return control.isPressed;
#endif
            return Input.GetMouseButton(button);
        }

        public static bool GetMouseButtonUp(int button)
        {
#if ENABLE_INPUT_SYSTEM
            ButtonControl control = ResolveMouse(button);
            if (control != null) return control.wasReleasedThisFrame;
#endif
            return Input.GetMouseButtonUp(button);
        }

        /// <summary>ค่าการหมุนล้อเมาส์ (บวก = หมุนขึ้น)</summary>
        public static float ScrollDelta
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                if (Mouse.current != null) return Mouse.current.scroll.ReadValue().y;
#endif
                return Input.mouseScrollDelta.y;
            }
        }

#if ENABLE_INPUT_SYSTEM
        private static ButtonControl ResolveMouse(int button)
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return null;

            switch (button)
            {
                case 0: return mouse.leftButton;
                case 1: return mouse.rightButton;
                case 2: return mouse.middleButton;
                default: return null;
            }
        }

        private static ButtonControl ResolveKey(KeyCode code)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return null;

            Key key = ToKey(code);
            return key == Key.None ? null : keyboard[key];
        }

        /// <summary>แปลง KeyCode (ระบบเก่า) ให้เป็น Key (ระบบใหม่) เท่าที่ระบบไอเทมต้องใช้</summary>
        private static Key ToKey(KeyCode code)
        {
            // A-Z และ 0-9 เรียงต่อกันเป็นช่วงในทั้งสอง enum เลยบวกลบ offset ได้ตรงๆ
            if (code >= KeyCode.A && code <= KeyCode.Z) return Key.A + (code - KeyCode.A);
            if (code >= KeyCode.Alpha0 && code <= KeyCode.Alpha9) return Key.Digit0 + (code - KeyCode.Alpha0);

            switch (code)
            {
                case KeyCode.Tab: return Key.Tab;
                case KeyCode.Space: return Key.Space;
                case KeyCode.Escape: return Key.Escape;
                case KeyCode.Return: return Key.Enter;
                case KeyCode.LeftShift: return Key.LeftShift;
                case KeyCode.RightShift: return Key.RightShift;
                case KeyCode.LeftControl: return Key.LeftCtrl;
                case KeyCode.RightControl: return Key.RightCtrl;
                case KeyCode.LeftAlt: return Key.LeftAlt;
                case KeyCode.RightAlt: return Key.RightAlt;
                default: return Key.None;
            }
        }
#endif
    }
}

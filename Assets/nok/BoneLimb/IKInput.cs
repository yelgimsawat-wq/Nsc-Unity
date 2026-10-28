using UnityEngine;
using UnityEngine.InputSystem;

namespace Nsc.BoneLimb
{
    /// <summary>เมาส์/คีย์บอร์ดหนึ่งเฟรมที่หุ่น IK ใช้ — อ่านที่เดียว โค้ดที่เหลือไม่ต้องรู้จัก Input System</summary>
    public struct IKInput
    {
        public Ray ray;              // จากกล้องผ่านเคอร์เซอร์
        public Vector2 lookDelta;
        public bool looking;         // คลิกขวาค้าง = หันกล้อง
        public bool held;            // คลิกซ้ายค้าง (ไม่นับตอนหันกล้อง)
        public bool pressed;         // เพิ่งกดคลิกซ้ายเฟรมนี้ (ไม่นับตอนหันกล้อง)
        public bool shift;
        public float scroll;
        public float turn;           // E = +1, Q = -1
        public int pressedSlot;      // ปุ่ม 1-4 หรือ F1-F4 ที่เพิ่งกด (0-3), ไม่กด = -1

        public static bool TryRead(Camera cam, int slotCount, out IKInput input)
        {
            input = default;
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb == null || mouse == null) return false;

            input.lookDelta = mouse.delta.ReadValue();
            input.looking = mouse.rightButton.isPressed;
            input.held = mouse.leftButton.isPressed && !input.looking;
            input.pressed = mouse.leftButton.wasPressedThisFrame && !input.looking;
            input.shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            input.scroll = mouse.scroll.ReadValue().y;
            input.turn = (kb.eKey.isPressed ? 1f : 0f) - (kb.qKey.isPressed ? 1f : 0f);
            input.ray = cam ? cam.ScreenPointToRay(mouse.position.ReadValue()) : default;

            input.pressedSlot = -1;
            for (int i = 0; i < slotCount; i++)
                if (kb[Key.Digit1 + i].wasPressedThisFrame || kb[Key.F1 + i].wasPressedThisFrame) input.pressedSlot = i;
            return true;
        }
    }
}

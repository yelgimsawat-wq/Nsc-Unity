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
        public bool kick;            // เพิ่งกด Shift เฟรมนี้ (คุมขา = เตะ)
        public float scroll;
        public int pressedSlot;      // ปุ่ม 1-4 หรือ F1-F4 ที่เพิ่งกด (0-3), ไม่กด = -1
        public bool knockDown;       // B = ทดสอบล้ม (Editor/Dev build)
        public bool standUp;         // เพิ่งกด Q เฟรมนี้ — ตอนหุ่นล้ม = ขอลุก (กดค้างไว้ก่อนล้มไม่นับ)
        public bool pull;            // R ค้าง = ดึงชิ้นที่หลุดกลับ (แบบหุ่นเดิม)
        public bool detachTest;      // X = ทดสอบชิ้นที่คุมหลุด (Editor/Dev build)

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
            input.kick = kb.leftShiftKey.wasPressedThisFrame || kb.rightShiftKey.wasPressedThisFrame;
            input.scroll = mouse.scroll.ReadValue().y;
            input.ray = cam ? cam.ScreenPointToRay(mouse.position.ReadValue()) : default;
            input.knockDown = kb.bKey.wasPressedThisFrame;
            input.standUp = kb.qKey.wasPressedThisFrame;
            input.pull = kb.rKey.isPressed;
            input.detachTest = kb.xKey.wasPressedThisFrame;

            input.pressedSlot = -1;
            for (int i = 0; i < slotCount; i++)
                if (kb[Key.Digit1 + i].wasPressedThisFrame || kb[Key.F1 + i].wasPressedThisFrame) input.pressedSlot = i;
            return true;
        }
    }
}

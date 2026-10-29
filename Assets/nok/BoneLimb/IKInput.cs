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
        public bool released;        // เพิ่งปล่อยคลิกซ้ายเฟรมนี้ — ถืออาวุธ: คลิกเฉยๆ ไม่ลาก = ท่าถัดไปของคอมโบ
        public bool attack;          // จอย: X/□ หรือ R1 = ท่าถัดไปของคอมโบ
        public int padSlash;         // จอย: ดีดสติ๊กขวา = ทิศฟัน (0 ลง, 1 ขวา, 2 ซ้าย, 3 ขึ้น) | ไม่ดีด = -1
        private static bool stickOut; // สติ๊กยังค้างนอกกลาง — ดีดหนึ่งครั้ง = ฟันหนึ่งครั้ง
        public bool shift;
        public bool grab;            // F ค้าง (โหมดกดค้าง)
        public bool grabPressed;     // เพิ่งกด F เฟรมนี้ (โหมดกดครั้งเดียว = สลับจับ/ปล่อย)
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
            input.released = mouse.leftButton.wasReleasedThisFrame && !input.looking;
            input.padSlash = -1;
            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                input.attack = pad.buttonWest.wasPressedThisFrame || pad.rightShoulder.wasPressedThisFrame;
                Vector2 stick = pad.rightStick.ReadValue();
                if (stick.magnitude > 0.7f && !stickOut)
                    input.padSlash = Mathf.Abs(stick.x) > Mathf.Abs(stick.y) ? (stick.x > 0f ? 1 : 2) : (stick.y < 0f ? 0 : 3);
                stickOut = stick.magnitude > 0.5f;
            }
            input.shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            input.grab = kb.fKey.isPressed;
            input.grabPressed = kb.fKey.wasPressedThisFrame;
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

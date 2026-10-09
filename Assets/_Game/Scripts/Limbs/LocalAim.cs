using Nsc.Match;
using UnityEngine;

namespace Nsc.Limbs
{
    /// <summary>
    /// จุดเล็งและการล็อกเคอร์เซอร์ของ "เครื่องนี้" — มีชุดเดียวต่อเครื่องโดยธรรมชาติ จึงเป็น static
    ///
    /// แทน static ที่กระจายอยู่ในมือ/เท้าเดิม (sharedVirtualCursor, sharedAimOffsetWorld, HandleCursorLock ×2)
    /// มือสองข้างบนเครื่องเดียวกันแชร์จุดเล็งเดียวกันเหมือนเดิม และปืน/เท้าอ่านจุดเล็งผ่าน AimNormalized
    /// </summary>
    public static class LocalAim
    {
        /// <summary>จุดเล็งเสมือน normalized [-1,1] — ใช้ตอนเคอร์เซอร์จริงถูกล็อกกลางจอ</summary>
        public static Vector2 VirtualCursor;

        /// <summary>
        /// จุดเล็งมือ = offset 3 แกนจากหัวไหล่ ในแกนโลก (World-Anchored Aim)
        /// หมุนกล้อง = ไม่มี delta = offset ไม่ขยับ = มือค้างอยู่จุดเดิมในโลก
        /// </summary>
        public static Vector3 ArmOffsetWorld = Vector3.down;

        private static int virtualCursorFrame = -1;
        private static int armAimFrame = -1;
        private static int crosshairFrame = -1;
        private static bool everLocked;

        public static bool IsLocked => Cursor.lockState == CursorLockMode.Locked;

        /// <summary>คลิกขวาค้าง = โหมดกล้องเต็มตัว — เมาส์และล้อเป็นของกล้อง ห้ามขยับจุดเล็ง</summary>
        public static bool CameraModeHeld => Input.GetMouseButton(1);

        /// <summary>
        /// จุดเล็ง normalized [-1,1] สำหรับระบบอื่นทั้งเกม (ปืน, เท้าโหมด fallback)
        /// ตอนเคอร์เซอร์ถูกล็อก Input.mousePosition ค้างกลางจอ — ต้องอ่านผ่านตัวนี้เสมอ
        /// </summary>
        public static Vector2 AimNormalized => IsLocked ? VirtualCursor : AbsoluteMouseNormalized();

        public static Vector2 AbsoluteMouseNormalized() => new Vector2(
            (Mathf.Clamp(Input.mousePosition.x, 0, Screen.width) / Screen.width) * 2f - 1f,
            (Mathf.Clamp(Input.mousePosition.y, 0, Screen.height) / Screen.height) * 2f - 1f);

        /// <summary>mouse delta ของเฟรมนี้ คูณความไวของชิ้นส่วนและค่าที่ผู้เล่นตั้ง</summary>
        public static Vector2 MouseDelta(float sensitivity) =>
            new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * (sensitivity * MouseSettings.Multiplier * 0.1f);

        /// <summary>ขยับจุดเล็งเสมือนครั้งเดียวต่อเฟรม (มือสองข้างห้ามบวก delta ซ้ำ)</summary>
        public static void UpdateVirtualCursor(float sensitivity)
        {
            if (!IsLocked || CameraModeHeld || Time.frameCount == virtualCursorFrame) return;
            virtualCursorFrame = Time.frameCount;

            VirtualCursor += MouseDelta(sensitivity);
            VirtualCursor.x = Mathf.Clamp(VirtualCursor.x, -1f, 1f);
            VirtualCursor.y = Mathf.Clamp(VirtualCursor.y, -1f, 1f);
        }

        /// <summary>คืน true ครั้งเดียวต่อเฟรม — ผู้ที่ได้ true เป็นคนอัปเดต ArmOffsetWorld ของเฟรมนี้</summary>
        public static bool TryClaimArmAimFrame()
        {
            if (Time.frameCount == armAimFrame) return false;
            armAimFrame = Time.frameCount;
            return true;
        }

        /// <summary>วาด crosshair ได้ครั้งเดียวต่อเฟรม (มือสองข้างห้ามวาดซ้อน)</summary>
        public static bool TryClaimCrosshairFrame()
        {
            if (Time.frameCount == crosshairFrame) return false;
            crosshairFrame = Time.frameCount;
            return true;
        }

        /// <summary>
        /// Esc = ปลดล็อกชั่วคราว (ไปกดเมนู) / ครั้งแรกที่เข้าเกม หรือคลิกซ้ายกลับเข้าเกม = ล็อกต่อ
        /// คืน true เฉพาะเฟรมที่เพิ่งล็อก — คลิกนั้นใช้ดึงเมาส์กลับ ห้ามนับเป็นหมัด/ก้าว
        /// </summary>
        public static bool HandleCursorLock()
        {
            // จบเกม/ยังไม่เริ่ม — ห้ามล็อกเมาส์กลับ ผู้เล่นต้องคลิกปุ่มบนหน้าจอ
            if (!GameplayGate.CanAct) return false;

            // มี UI เปิดอยู่ (วงล้อไอเทม/เมนู) — ห้ามแย่งเมาส์ ไม่งั้นกดปุ่มแรกบน UI แล้วเคอร์เซอร์หาย
            if (UiFocus.IsCaptured) return false;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Release();
                return false;
            }

            if (IsLocked || (everLocked && !Input.GetMouseButtonDown(0))) return false;

            // เริ่มจุดเล็งเสมือนจากตำแหน่งเมาส์จริง ณ ตอนล็อก — จุดเล็งไม่กระโดด
            VirtualCursor = AbsoluteMouseNormalized();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            everLocked = true;
            return true;
        }

        public static void Release()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            VirtualCursor = Vector2.zero;
            ArmOffsetWorld = Vector3.down;
            virtualCursorFrame = armAimFrame = crosshairFrame = -1;
            everLocked = false;
        }
    }
}

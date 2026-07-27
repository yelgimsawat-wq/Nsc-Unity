using Unity.Netcode;
using UnityEngine;

namespace NscUnity.Items
{
    /// <summary>
    /// หา component ของระบบไอเทมบน "หุ่นที่ผู้เล่นเครื่องนี้คุมอยู่จริง"
    ///
    /// ⚠️ ห้ามใช้ IsOwner เพียวๆ ในการหา — Host เป็นเจ้าของชิ้นส่วนที่ "ไม่มีใครเลือก" โดยอัตโนมัติด้วย
    /// ถ้าดูจาก ownership อย่างเดียวจะเจอแขนผิดข้าง (เช่น UI ไปผูกกับแขนที่ไม่มีใครคุม
    /// แล้วเก็บของลงอีกแขนนึง ทำให้ของไม่โผล่ในวงล้อ)
    ///
    /// เลยยืมผลลัพธ์จาก LocalRobotBinder ที่โปรเจกต์คำนวณไว้ถูกต้องอยู่แล้ว (ยึดตามชิ้นที่เลือกในลอบบี้)
    ///
    /// ⚠️ ห้ามให้เมธอดนี้ตกไปสแกนทั้งฉากทุกเฟรม — UI เรียกมันทุกเฟรมเพื่อเช็คว่ายังผูกถูกตัวอยู่ไหม
    /// ถ้าสแกนทุกครั้งจะทำให้เกมหน่วงหนักในแมพใหญ่ๆ (เคยเกิดมาแล้ว)
    /// </summary>
    internal static class LocalItemOwner
    {
        /// <summary>เว้นช่วงก่อนสแกนฉากใหม่ ตอนไม่มี LocalRobotBinder ให้พึ่ง</summary>
        private const float FallbackScanInterval = 0.5f;

        private static LocalRobotBinder cachedBinder;
        private static float nextFallbackScanTime;

        public static T Find<T>() where T : NetworkBehaviour
        {
            LocalRobotBinder binder = ResolveBinder();

            if (binder != null && binder.IsBound)
            {
                // ค้นเฉพาะ "กิ่ง" ของชิ้นที่เราคุมกับลำตัว — เล็กพอที่จะเรียกทุกเฟรมได้โดยไม่หน่วง
                // (ต่างจากการสแกนทั้งฉากซึ่งแพงมาก) ค้นทั้งตัวเองและลูก เพราะ JointPullAndReconnect
                // กับ PlayerHandMovement อาจอยู่คนละ GameObject กันในลำดับชั้นของแขน

                // 1. ชิ้นที่เราคุมเอง (แขน) — HandItemHolder / ItemPickupInteractor อยู่ที่นี่
                if (binder.OwnedJoint != null)
                {
                    T onLimb = binder.OwnedJoint.GetComponentInChildren<T>(true);
                    if (onLimb != null) return onLimb;
                }

                // 2. ลำตัวของหุ่นตัวเดียวกัน — PlayerInventory อยู่ที่นี่ (เก็บที่ลำตัวกันกระเป๋าหายถ้าแขนหลุด)
                if (binder.Torso != null)
                {
                    T onTorso = binder.Torso.GetComponent<T>();
                    if (onTorso != null) return onTorso;
                }

                // bind ได้แล้วแต่ไม่เจอ = หุ่นตัวนี้ไม่มี component นี้จริงๆ (เช่นคุมขาซึ่งถือของไม่ได้)
                // คืน null ไปเลย ห้ามตกไปสแกนทั้งฉาก ไม่งั้นจะสแกนทุกเฟรมและทำให้เกมหน่วง
                return null;
            }

            return FindViaOwnershipThrottled<T>();
        }

        private static LocalRobotBinder ResolveBinder()
        {
            if (cachedBinder == null) cachedBinder = Object.FindFirstObjectByType<LocalRobotBinder>();
            return cachedBinder;
        }

        /// <summary>
        /// ทางสำรองตอนไม่มี LocalRobotBinder ในฉาก (เทสในฉากเปล่าที่ไม่มีระบบ HUD/ลอบบี้)
        /// สแกนแค่ทุกๆ ครึ่งวินาที ไม่ใช่ทุกเฟรม เพราะการสแกนทั้งฉากมีราคาแพงมาก
        /// </summary>
        private static T FindViaOwnershipThrottled<T>() where T : NetworkBehaviour
        {
            if (Time.unscaledTime < nextFallbackScanTime) return null;
            nextFallbackScanTime = Time.unscaledTime + FallbackScanInterval;

            foreach (T candidate in Object.FindObjectsByType<T>(FindObjectsSortMode.None))
            {
                if (candidate.IsSpawned && candidate.IsOwner) return candidate;
            }
            return null;
        }

        // เคลียร์ค่า static ทุกครั้งที่เริ่มเล่นใหม่ เผื่อโปรเจกต์ปิด Domain Reload ไว้
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            cachedBinder = null;
            nextFallbackScanTime = 0f;
        }
    }
}

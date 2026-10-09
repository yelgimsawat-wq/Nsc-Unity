using Nsc.Match;
using Nsc.Robots;
using Unity.Netcode;
using UnityEngine;

namespace NscUnity.Items
{
    /// <summary>
    /// หา component ของระบบไอเทมบน "หุ่นที่ผู้เล่นเครื่องนี้คุมอยู่จริง"
    ///
    /// ⚠️ ห้ามใช้ IsOwner เพียวๆ — Host เป็นเจ้าของชิ้นส่วนที่ "ไม่มีใครเลือก" ด้วย
    /// ดูจาก ownership อย่างเดียวจะเจอแขนผิดข้าง จึงยึดตามชิ้นที่ LimbControlBinder ผูกไว้ (ชิ้นที่จองในลอบบี้)
    ///
    /// ⚠️ ห้ามตกไปสแกนทั้งฉากทุกเฟรม — UI เรียกเมธอดนี้ทุกเฟรม สแกนทุกครั้งแล้วแมพใหญ่หน่วงหนัก
    /// </summary>
    internal static class LocalItemOwner
    {
        /// <summary>เว้นช่วงก่อนสแกนฉากใหม่ ตอนไม่มีลอบบี้ให้พึ่ง (ฉากเทส)</summary>
        private const float FallbackScanInterval = 0.5f;

        private static float nextFallbackScanTime;

        public static T Find<T>() where T : NetworkBehaviour
        {
            LimbControlBinder binder = LimbControlBinder.Current;
            if (binder != null)
            {
                if (!binder.IsBound) return null;

                // 1. ชิ้นที่เราคุม (แขน) — HandItemHolder / ItemPickupInteractor อยู่ที่นี่
                RobotLimb limb = binder.LocalLimb;
                if (limb != null)
                {
                    T onLimb = limb.GetComponentInChildren<T>(true);
                    if (onLimb != null) return onLimb;
                }

                // 2. ลำตัวของหุ่นตัวเดียวกัน — PlayerInventory อยู่ที่นี่ (กระเป๋าไม่หายตอนแขนหลุด)
                TorsoBalance torso = binder.LocalRobot.Torso;
                return torso != null ? torso.GetComponent<T>() : null;
            }

            return FindViaOwnershipThrottled<T>();
        }

        /// <summary>ทางสำรองตอนไม่มีลอบบี้ในฉาก — สแกนแค่ทุกครึ่งวินาที</summary>
        private static T FindViaOwnershipThrottled<T>() where T : NetworkBehaviour
        {
            if (Time.unscaledTime < nextFallbackScanTime) return null;
            nextFallbackScanTime = Time.unscaledTime + FallbackScanInterval;

            foreach (T candidate in Object.FindObjectsByType<T>(FindObjectsSortMode.None))
                if (candidate.IsSpawned && candidate.IsOwner) return candidate;
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => nextFallbackScanTime = 0f;
    }
}

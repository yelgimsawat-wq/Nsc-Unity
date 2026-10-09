using Nsc.Match;
using Unity.Netcode;
using UnityEngine;

namespace Nsc.Combat
{
    /// <summary>
    /// ทางเข้าเดียวของดาเมจทั้งเกม — กติกา "ตีได้ไหม" (server เท่านั้น / แมตช์กำลังเล่น / คนละทีม)
    /// อยู่ที่นี่ที่เดียว ผู้โจมตีห้ามหาคอมโพเนนต์เลือดเอง
    ///
    /// เก็บให้เล็ก: ไม่รู้จักโหมดเกม ไม่รู้จักหุ่น — รู้แค่ IDamageable กับ GameplayGate
    /// </summary>
    public static class DamageRouter
    {
        /// <summary>[SERVER] หาเป้าที่รับดาเมจได้จาก collider ที่ชน แล้วส่งดาเมจให้ถ้ากติกาอนุญาต</summary>
        public static bool TryApply(Collider hit, DamageInfo info)
        {
            if (hit == null) return false;
            return TryApply(hit.GetComponentInParent<IDamageable>(), info);
        }

        /// <summary>[SERVER] ส่งดาเมจให้เป้าที่รู้อยู่แล้ว (เช่นบอสเลือกชิ้นส่วนที่ใกล้จุดกระแทกสุดเอง)</summary>
        public static bool TryApply(IDamageable target, DamageInfo info)
        {
            if (target == null || !IsAllowed(target, info)) return false;
            return target.ServerApplyDamage(info);
        }

        /// <summary>ตรวจกติกาโดยไม่ทำดาเมจ — ใช้ตัดสินว่าการชนครั้งนี้ "เข้าเป้า" ไหมก่อนส่งจริง</summary>
        public static bool IsAllowed(IDamageable target, DamageInfo info)
        {
            if (target == null) return false;

            // ดาเมจเป็นของ server คนเดียว — client เห็นผลผ่าน NetworkVariable ของผู้รับ
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsServer) return false;

            if (!GameplayGate.CanDamage) return false;
            if (info.amount <= 0f && !info.detachLimb) return false;
            if (!info.attackerTeam.IsHostileTo(target.GetTeam())) return false;

            return true;
        }
    }
}

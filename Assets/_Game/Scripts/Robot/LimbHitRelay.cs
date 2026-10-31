using Nsc.Combat;
using UnityEngine;

namespace Nsc.Robots
{
    /// <summary>
    /// ลูกบอลรับหมัดที่ลอยตามปลายแขน/ขาของหุ่น IK — ส่งดาเมจต่อให้เลือดของชิ้นนั้น
    ///
    /// ลูกบอลพวกนี้เป็น Rigidbody ที่ถูกย้ายไปที่ปลายแขนขาทุกเฟรม ไม่ได้เป็นลูกของช่อง RobotLimb
    /// DamageRouter ไล่หา IDamageable จากพาเรนต์ของ collider ที่โดน จึงหาเลือดของชิ้นไม่เจอ
    /// (ต่อยแขนศัตรูแล้วดาเมจไม่เข้า เหลือแต่ตอนโดนลำตัว)
    /// RobotIKControl แปะตัวนี้ให้ตอน Start แล้วชี้ไปที่ LimbHealth ของช่องเดียวกัน
    /// </summary>
    [DisallowMultipleComponent]
    public class LimbHitRelay : MonoBehaviour, IDamageable
    {
        [SerializeField] private LimbHealth health;

        public void Bind(LimbHealth limbHealth) => health = limbHealth;

        public Team GetTeam() => health != null ? health.GetTeam() : Team.None;

        public bool ServerApplyDamage(DamageInfo info) => health != null && health.ServerApplyDamage(info);
    }
}

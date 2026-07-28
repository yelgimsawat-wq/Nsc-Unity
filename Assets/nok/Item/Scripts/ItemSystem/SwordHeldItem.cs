using UnityEngine;

namespace NscUnity.Items
{
    /// <summary>
    /// ดาบ (หรืออาวุธประชิดตัวชนิดอื่น) — ดาเมจมาจาก "แรงกระแทกจริง" ตอนสวิงไปชน ไม่ใช่กดปุ่มสั่งฟัน
    /// ใช้ระบบเดียวกับหมัด/เตะที่มีอยู่แล้ว (PvpDamageSender คำนวณดาเมจจากแรงปะทะจริง F = ma)
    /// ฟาดแรง = ดาเมจเยอะ ฟาดเบา = แทบไม่รู้สึกอะไร เป็นฟิสิกส์จริง ไม่ใช่เลขตายตัวเหมือนปืน
    ///
    /// วิธีทำงาน: ตอนถือขึ้นมา จะสร้าง FixedJoint ยึดดาบไว้กับ Rigidbody ของมือ ให้ดาบสวิงตามการขยับมือ
    /// จริงๆ (ไม่ใช่แค่เกาะตำแหน่งเฉยๆ แบบไอเทมทั่วไป) พอไปชนอะไร Unity จะคำนวณแรงกระแทกจากความเร็วจริง
    /// แล้ว PvpDamageSender ที่แปะอยู่บนใบดาบจะแปลงแรงนั้นเป็นดาเมจให้เอง — ไม่ต้องเขียนสูตรดาเมจเพิ่มเลย
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class SwordHeldItem : HeldItem
    {
        [Tooltip("ความแข็งของข้อต่อที่ยึดดาบไว้กับมือ ปล่อยเป็น Infinity ไว้ได้ (ไม่มีวันหลุดจากมือ)")]
        [SerializeField] private float jointBreakForce = Mathf.Infinity;

        private FixedJoint joint;

        public override void OnEquipped()
        {
            Rigidbody swordBody = GetComponent<Rigidbody>();
            Rigidbody handBody = GetComponentInParent<Rigidbody>();

            if (handBody == null || handBody == swordBody)
            {
                Debug.LogError($"[SwordHeldItem] หา Rigidbody ของมือไม่เจอ (ไต่จาก parent) — " +
                               "ดาบจะแค่เกาะตำแหน่งเฉยๆ ไม่สวิงตามแรงจริง เช็คว่ามือมี Rigidbody อยู่หรือเปล่า", this);
                return;
            }

            joint = gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = handBody;
            joint.breakForce = jointBreakForce;
            joint.breakTorque = jointBreakForce;
        }

        public override void OnUnequipped()
        {
            if (joint != null) Destroy(joint);
        }
    }
}

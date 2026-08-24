using NscGame.Pvp;
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
    ///
    /// ⚠️ ต้องสั่งไม่ให้ Physics คิดชนกันระหว่างดาบกับตัวหุ่นเราเอง ไม่งั้นตอนสวิงใบดาบจะไปโดนแขน/ลำตัว/ขา
    /// ของตัวเอง แรงชนนั้นจะดันผ่าน FixedJoint เข้าไปรบกวน Joint ของแขน (JointPullAndReconnect) จนหมุนเพี้ยน
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class SwordHeldItem : HeldItem
    {
        [Tooltip("ความแข็งของข้อต่อที่ยึดดาบไว้กับมือ ปล่อยเป็น Infinity ไว้ได้ (ไม่มีวันหลุดจากมือ)")]
        [SerializeField] private float jointBreakForce = Mathf.Infinity;

        [Tooltip("ให้ข้อต่อมองว่าดาบเบากว่าความจริงกี่เท่า — ยิ่งสูงยิ่งไม่ถ่วงแขน\n" +
                 "แขนขยับด้วยมอเตอร์ที่มีแรงจำกัด ถ้าดาบถ่วงเต็มน้ำหนักจะขยับแขนแทบไม่ไหว")]
        [SerializeField, Min(1f)] private float weightRelief = 20f;

        [Header("ความแรง")]
        [Tooltip("ดาบแรงกว่าต่อยมือเปล่ากี่เท่า — 2.5 = ฟาดด้วยความเร็วเท่ากับต่อย แต่เจ็บกว่า 2.5 เท่า")]
        [SerializeField, Min(1f)] private float damageMultiplier = 2.5f;

        [Tooltip("เพดานดาเมจต่อการฟาดหนึ่งครั้ง — ตั้งสูงกว่าหมัดเพราะดาบควรแรงกว่า")]
        [SerializeField] private float maxDamagePerHit = 180f;

        private FixedJoint joint;

        public override void OnEquipped()
        {
            Rigidbody swordBody = GetComponent<Rigidbody>();

            // GetComponentInParent เช็คตัวเองก่อนเสมอ ดาบมี Rigidbody ของตัวเองอยู่แล้ว
            // ถ้าเรียกจาก transform ตรงๆ จะเจอ Rigidbody ของดาบเอง ไม่ใช่ของมือ — ต้องเริ่มค้นจาก parent แทน
            Rigidbody handBody = transform.parent != null ? transform.parent.GetComponentInParent<Rigidbody>() : null;

            if (handBody == null || handBody == swordBody)
            {
                Debug.LogError($"[SwordHeldItem] หา Rigidbody ของมือไม่เจอ (ไต่จาก parent) — " +
                               "ดาบจะแค่เกาะตำแหน่งเฉยๆ ไม่สวิงตามแรงจริง เช็คว่ามือมี Rigidbody อยู่หรือเปล่า", this);
                return;
            }

            // ดาบต้องไม่เป็น kinematic ไม่งั้นมันจะกลายเป็น "สมอ" ตรึงแขนไว้กับที่จนขยับไม่ได้เลย
            if (swordBody.isKinematic)
            {
                swordBody.isKinematic = false;
                Debug.LogWarning($"[SwordHeldItem] Rigidbody ของ '{name}' ตั้ง Is Kinematic ไว้ — " +
                                 "ปิดให้อัตโนมัติแล้ว (ถ้าเปิดค้างไว้จะตรึงแขนจนขยับไม่ได้) แนะนำให้แก้ที่ prefab ด้วย", this);
            }

            joint = gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = handBody;
            joint.breakForce = jointBreakForce;
            joint.breakTorque = jointBreakForce;

            // บอกให้ solver มองว่าดาบเบากว่าความจริง — แขนขยับด้วยมอเตอร์ที่มีแรงจำกัด
            // ถ้าปล่อยให้ดาบถ่วงเต็มน้ำหนัก มอเตอร์จะแบกไม่ไหวจนแขนขยับแทบไม่ได้
            joint.massScale = 1f;
            joint.connectedMassScale = weightRelief;

            IgnoreCollisionsWithOwnRobot();
            ConfigureDamage(handBody);
        }

        /// <summary>
        /// ตั้งค่า PvpDamageSender บนดาบให้แรงกว่าต่อยมือเปล่า
        ///
        /// ปกติ PvpDamageSender บนชิ้นที่ไม่มี PlayerHandCombat จะคิดดาเมจจาก impulse (แบบเท้า)
        /// ซึ่งดาบจะได้ดาเมจน้อยมากเพราะมวลเบา — เลยยืมค่าจาก PvpDamageSender ของ "มือ" ที่ถือดาบอยู่
        /// (ซึ่งคิดจากความเร็วพีคของหมัด) แล้วคูณเพิ่มด้วย damageMultiplier
        /// </summary>
        private void ConfigureDamage(Rigidbody handBody)
        {
            PvpDamageSender swordDamage = GetComponent<PvpDamageSender>();
            if (swordDamage == null) return;

            PvpDamageSender handDamage = handBody.GetComponent<PvpDamageSender>();
            if (handDamage == null)
            {
                Debug.LogWarning($"[SwordHeldItem] มือ '{handBody.name}' ไม่มี PvpDamageSender — " +
                                 "ดาบจะใช้ค่าดาเมจที่ตั้งไว้ใน prefab ตรงๆ แทนการอิงจากหมัด", this);
                return;
            }

            // ดาบไม่มี PlayerHandCombat ของตัวเอง เลยคิดดาเมจจาก impulse ซึ่งมวลเบาทำให้ได้น้อย
            // ชดเชยด้วยการดันตัวคูณ impulse ขึ้นตามสัดส่วนที่อยากให้แรงกว่าหมัด
            swordDamage.forceToDamage = handDamage.forceToDamage * damageMultiplier;
            swordDamage.speedToDamage = handDamage.speedToDamage * damageMultiplier;
            swordDamage.maxDamagePerHit = maxDamagePerHit;

            // ฟาดเบาๆ ควรเข้าง่ายกว่าต่อย เพราะคมดาบไม่ต้องใช้แรงเท่าหมัด
            swordDamage.minVelocityThreshold = handDamage.minVelocityThreshold * 0.6f;
        }

        public override void OnUnequipped()
        {
            if (joint != null) Destroy(joint);
        }

        /// <summary>
        /// สั่งให้ Collider ของดาบ "มองไม่เห็น" Collider ทุกชิ้นของหุ่นตัวเราเอง (แขน/ลำตัว/ขา)
        /// ไม่กระทบการชนศัตรู/สิ่งแวดล้อม เพราะจำกัดแค่ collider ที่อยู่ใต้ root ของหุ่นตัวนี้เท่านั้น
        /// ไม่ต้องคืนค่าตอน Unequip — Unity ล้าง ignore-collision ให้เองอัตโนมัติตอน GameObject ถูกทำลาย
        /// </summary>
        private void IgnoreCollisionsWithOwnRobot()
        {
            Collider swordCollider = GetComponent<Collider>();
            if (swordCollider == null) return;

            PvpRobotTeam ownRobot = PvpRobotTeam.FindByPart(transform);
            Transform root = ownRobot != null ? ownRobot.RobotRoot : transform.root;
            if (root == null) return;

            foreach (Collider ownCollider in root.GetComponentsInChildren<Collider>(true))
            {
                if (ownCollider != null && ownCollider != swordCollider)
                {
                    Physics.IgnoreCollision(swordCollider, ownCollider, true);
                }
            }
        }
    }
}

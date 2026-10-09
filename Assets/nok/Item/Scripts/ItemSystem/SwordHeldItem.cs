using Nsc.Combat;
using Nsc.Robots;
using UnityEngine;

namespace NscUnity.Items
{
    /// <summary>
    /// ดาบ (อาวุธประชิด) — ดาเมจมาจากการสวิงไปชนจริง ไม่ใช่กดปุ่มสั่งฟัน
    /// ตอนถือขึ้นมาสร้าง FixedJoint ยึดดาบกับ Rigidbody ของมือ ดาบจึงสวิงตามมือจริง
    /// LimbStrike บนใบดาบถามท่านี้ (IStrikeSource) ว่าความเร็วสวิงพีคเท่าไหร่ แล้วคิดดาเมจแบบเดียวกับหมัด
    ///
    /// ⚠️ ต้องปิดการชนระหว่างดาบกับหุ่นตัวเอง ไม่งั้นแรงชนดันผ่าน FixedJoint ไปรบกวนข้อต่อแขน
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class SwordHeldItem : HeldItem, IStrikeSource
    {
        // ความเร็วพีคของการสวิงจำไว้ช่วงสั้นๆ — ชนกลางวงสวิงหรือปลายวงก็ได้ดาเมจจากความเร็วสูงสุดของวงนั้น
        private const float SwingMemorySeconds = 0.25f;

        [Tooltip("ความแข็งของข้อต่อที่ยึดดาบกับมือ ปล่อยเป็น Infinity ไว้ได้ (ไม่มีวันหลุดจากมือ)")]
        [SerializeField] private float jointBreakForce = Mathf.Infinity;

        [Tooltip("ให้ข้อต่อมองว่าดาบเบากว่าความจริงกี่เท่า — แขนขยับด้วยมอเตอร์ที่มีแรงจำกัด ดาบหนักจะถ่วงจนขยับไม่ไหว")]
        [SerializeField, Min(1f)] private float weightRelief = 20f;

        [Header("ความแรง")]
        [Tooltip("ดาบแรงกว่าต่อยมือเปล่ากี่เท่า — 2.5 = สวิงเร็วเท่าหมัดแต่เจ็บกว่า 2.5 เท่า")]
        [SerializeField, Min(1f)] private float damageMultiplier = 2.5f;

        [Tooltip("เพดานดาเมจต่อการฟาดหนึ่งครั้ง — สูงกว่าหมัดเพราะดาบควรแรงกว่า")]
        [SerializeField] private float maxDamagePerHit = 180f;

        private FixedJoint joint;
        private Rigidbody swordBody;
        private float peakSpeed;
        private float peakSpeedTime;

        public override void OnEquipped()
        {
            swordBody = GetComponent<Rigidbody>();

            // GetComponentInParent เช็คตัวเองก่อนเสมอ — ต้องเริ่มค้นจาก parent ไม่งั้นได้ Rigidbody ของดาบเอง
            Rigidbody handBody = transform.parent != null ? transform.parent.GetComponentInParent<Rigidbody>() : null;
            if (handBody == null || handBody == swordBody)
            {
                Debug.LogError($"[SwordHeldItem] หา Rigidbody ของมือไม่เจอ (ไต่จาก parent) — " +
                               "ดาบจะแค่เกาะตำแหน่งเฉยๆ ไม่สวิงตามแรงจริง เช็คว่ามือมี Rigidbody อยู่หรือเปล่า", this);
                return;
            }

            // ดาบเป็น kinematic = "สมอ" ตรึงแขนไว้กับที่จนขยับไม่ได้
            if (swordBody.isKinematic)
            {
                swordBody.isKinematic = false;
                Debug.LogWarning($"[SwordHeldItem] Rigidbody ของ '{name}' ตั้ง Is Kinematic ไว้ — " +
                                 "ปิดให้อัตโนมัติแล้ว แนะนำให้แก้ที่ prefab ด้วย", this);
            }

            joint = gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = handBody;
            joint.breakForce = jointBreakForce;
            joint.breakTorque = jointBreakForce;
            joint.massScale = 1f;
            joint.connectedMassScale = weightRelief;

            IgnoreCollisionsWithOwnRobot();
            ConfigureStrike(handBody);
        }

        /// <summary>
        /// ยืมค่าจาก LimbStrike ของมือที่ถือ (ซึ่งจูนไว้สำหรับหมัด) แล้วคูณให้แรงกว่า
        /// ฟาดเบาๆ เข้าง่ายกว่าต่อย เพราะคมดาบไม่ต้องใช้แรงเท่าหมัด
        /// </summary>
        private void ConfigureStrike(Rigidbody handBody)
        {
            LimbStrike strike = GetComponent<LimbStrike>();
            if (strike == null) return;
            strike.SetSource(this);

            LimbStrike handStrike = handBody.GetComponent<LimbStrike>();
            if (handStrike == null)
            {
                Debug.LogWarning($"[SwordHeldItem] มือ '{handBody.name}' ไม่มี LimbStrike — ใช้ค่าดาเมจที่ตั้งไว้ใน prefab ตรงๆ", this);
                return;
            }

            strike.Configure(handStrike, damageMultiplier, 0.6f, maxDamagePerHit);
        }

        public override void OnUnequipped()
        {
            if (joint != null) Destroy(joint);
        }

        private void FixedUpdate()
        {
            if (swordBody == null) return;

            float speed = swordBody.linearVelocity.magnitude;
            if (speed >= peakSpeed || Time.time - peakSpeedTime > SwingMemorySeconds)
            {
                peakSpeed = speed;
                peakSpeedTime = Time.time;
            }
        }

        // ================================================================
        //  IStrikeSource — ดาบไม่มีจังหวะท่า: สวิงเร็วพอเมื่อไหร่ก็ทำดาเมจได้
        // ================================================================

        public bool CanDealDamage() => joint != null;
        public float PeakSpeed() => peakSpeed;
        public DamageSource Source() => DamageSource.MeleeWeapon;

        /// <summary>เข้าเป้าแล้วล้างความเร็วพีค — หนึ่งวงสวิง = หนึ่งดาเมจ ครูดต่อไม่นับซ้ำ</summary>
        public void ResolveStrike(bool landed)
        {
            if (!landed) return;
            peakSpeed = 0f;
            peakSpeedTime = Time.time;
        }

        /// <summary>
        /// ให้ Collider ของดาบ "มองไม่เห็น" collider ทุกชิ้นของหุ่นตัวเอง — ไม่กระทบการชนศัตรู/สิ่งแวดล้อม
        /// ไม่ต้องคืนค่าตอน Unequip — Unity ล้าง ignore-collision ให้เองตอน GameObject ถูกทำลาย
        /// </summary>
        private void IgnoreCollisionsWithOwnRobot()
        {
            Collider swordCollider = GetComponent<Collider>();
            if (swordCollider == null) return;

            Robot ownRobot = Robot.FromTransform(transform);
            Transform root = ownRobot != null ? ownRobot.Root : transform.root;
            if (root == null) return;

            foreach (Collider ownCollider in root.GetComponentsInChildren<Collider>(true))
                if (ownCollider != null && ownCollider != swordCollider)
                    Physics.IgnoreCollision(swordCollider, ownCollider, true);
        }
    }
}

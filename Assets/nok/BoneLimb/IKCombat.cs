using System;
using Nsc.Feel;
using UnityEngine;

namespace Nsc.BoneLimb
{
    /// <summary>
    /// โหมดต่อสู้ของแขน IK (ยกมาจากหุ่นฟิสิกส์ใน TestItem): ท่าการ์ด, เล็งด้วยเคอร์เซอร์, ชาร์จหมัด, ลำตัวบิดตามหมัด
    /// ปิดทั้งหมด (pointAndClick/idleGuard = false) = แขนทำงานแบบเดิมของ <see cref="RobotIKControl"/>
    /// ทุกอย่างคำนวณจากเป้าในแกนตัวหุ่น จึงไม่ต้องซิงก์เพิ่ม — เครื่องอื่นเห็นหมัดจากเป้าที่ซิงก์อยู่แล้ว
    /// </summary>
    [Serializable]
    public class IKCombat
    {
        [Tooltip("เปิด = แขนอยู่ท่าการ์ด เคอร์เซอร์เล็งเป้า คลิกซ้ายค้าง = ชาร์จ ปล่อย = ต่อย | ปิด = มือตามเคอร์เซอร์แบบเดิม")]
        public bool pointAndClick;
        [Tooltip("แขนที่ไม่มีคนคุมยกการ์ดเอง")]
        public bool idleGuard;
        [Tooltip("ท่าการ์ด: มือเทียบไหล่ เป็นสัดส่วนความยาวแขน (x ลบ = เข้าหาลำตัว, y = บน, z = หน้า)")]
        public Vector3 guard = new Vector3(-0.25f, 0.18f, 0.42f);
        [Tooltip("ทิศศอกตอนคุมแบบต่อสู้ (แกนตัวหุ่น, x = ออกนอกตัว) — ศอกชี้ลงหุบเข้าแบบนักมวย | ศูนย์ = ทิศเดิมของ IK")]
        public Vector3 elbowPole = new Vector3(0.35f, -1f, -0.2f);
        [Tooltip("เปิด = หมัดพุ่งตรงไปหน้าจากท่าการ์ด (แนวลึกอย่างเดียว) | ปิด = พุ่งไปทางที่เคอร์เซอร์เล็ง")]
        public bool straightPunch;

        [Header("หมัด")]
        public float chargeTime = 0.8f;
        [Tooltip("ชาร์จเต็มแล้วค้างเกินนี้ (วินาที) ต่อยออกเอง")]
        public float autoFire = 2f;
        [Tooltip("ชาร์จเต็ม ง้างมือถอยจากเป้ากี่ส่วนของแขน")]
        public float windup = 0.25f;
        [Tooltip("เวลามือพุ่งถึงเป้า (วินาที): x = แตะ, y = ชาร์จเต็ม")]
        public Vector2 thrustTime = new Vector2(0.3f, 0.15f);
        [Tooltip("หมัดพุ่งไปไกลกี่เท่าของความยาวแขนตามทิศที่เล็ง — เกิน 1 = แขนเหยียดสุดเสมอ ทะลุเป้าไป")]
        public float punchReach = 1.15f;
        [Tooltip("ค้างที่ปลายหมัดก่อนดึงกลับ")]
        public float holdTime = 0.12f;
        public float recoverTime = 0.4f;

        [Header("ลำตัวตามหมัด (องศาตอนแขนเหยียดสุด)")]
        public float twist = 25f;
        public float lean = 8f;
        [Tooltip("ตัวพุ่งไปหน้ากี่หน่วยตอนแขนเหยียดสุด (ภาพเท่านั้น — เท้าอยู่ที่เดิม)")]
        public float lunge;
        public float bodySmoothing = 10f;

        [Header("น้ำหนักมือ")]
        [Tooltip("มือไหลตามท่าด้วยสปริง (Hz) — ต่ำ = มือหนัก ตามช้า เลยเป้านิดแล้วกลับ | 0 = มือไปตรงท่าทันที")]
        public float handFrequency;
        [Range(0.1f, 1f)] public float handDamping = 0.6f;

        public enum State { Guard, Charging, Punching, Recovering }

        /// <summary>สถานะหมัดของแขนหนึ่งข้าง (ไม่ซิงก์ — เฉพาะเครื่องที่คุมแขนนั้น)</summary>
        public class Arm
        {
            public State state;
            public float t, charge;
            public Vector3 from, dir;  // แกนตัวหุ่น: จุดเริ่มหมัด (ตอนดึงกลับ = ระยะจากไหล่), ทิศหมัด
            public Vector3 shoulder;   // ไหล่ตอนท่าตั้งต้น ในแกนตัวหุ่น — ไม่ขยับตามลำตัวบิด ท่าการ์ดจึงนิ่ง
            public Vector3 desired;    // ท่าที่ตรรกะหมัดอยากให้มืออยู่ (แกนตัวหุ่น) — มือจริงไหลตามด้วยสปริง
            public Vector3 world, velocity; // มือจริงในโลก — สปริงในโลกทำให้มือตามตัวช้าตอนตัวขยับ (มีแรงเฉื่อย)
            public bool hit;           // หมัดนี้โดนของไปแล้ว (เล่นเอฟเฟกต์ครั้งเดียว)
            public bool local;         // เครื่องนี้เดินท่าแขนนี้เอง (คุมอยู่/ยกการ์ดเอง) — ใช้ drive แทนการเดาจากเป้า
            public float drive;        // จังหวะหมัด -1 (ง้างสุด) .. 0 (การ์ด) .. 1 (เหยียดสุด) — ลำตัวบิดตามค่านี้
        }

        public bool Enabled => pointAndClick || idleGuard;

        /// <summary>ลำตัวบิด/โน้มตอนนี้ (แกนตัวหุ่น) — ตั้งจาก RobotIKControl ทุกเฟรม ท่าการ์ดหมุนตามลำตัว มือไม่ค้างกลางอากาศตอนตัวบิด</summary>
        [NonSerialized] public Quaternion torsoTurn = Quaternion.identity;

        public float Charge01(Arm a) => a.state == State.Charging ? Mathf.Clamp01(a.t / chargeTime) : a.charge;

        private static Vector3 Shoulder(IKLimb arm, Transform body) => body.InverseTransformPoint(arm.root.position);

        /// <summary>ท่าการ์ดในแกนตัวหุ่น: จากไหล่จริงตอนนี้ (ขยับตามตัวพุ่ง/บิด) + ระยะการ์ดที่หมุนตามลำตัว</summary>
        public Vector3 GuardLocal(IKLimb arm, Arm a, Transform body) =>
            Shoulder(arm, body) + torsoTurn * (new Vector3(guard.x * arm.Side, guard.y, guard.z) * arm.Reach);

        /// <summary>แขนที่ผู้เล่นคุม: การ์ด → (ค้าง) ง้าง → (ปล่อย) พุ่งไปที่ aim → ค้าง → ดึงกลับการ์ด</summary>
        public void Drive(IKLimb arm, Arm a, Transform body, Vector3 aim, bool held, float follow, float dt)
        {
            if (a.state == State.Punching && CameraShake.IsFrozen) return; // หยุดภาพตอนหมัดโดน: กำปั้นค้างที่จุดปะทะ
            Vector3 guardPos = GuardLocal(arm, a, body);
            float smooth = 1f - Mathf.Exp(-follow * dt);
            a.t += dt;
            switch (a.state)
            {
                case State.Guard:
                    a.desired = Vector3.Lerp(a.desired, guardPos, smooth);
                    a.drive = 0f;
                    if (held) Enter(a, State.Charging);
                    break;

                case State.Charging:
                    Vector3 away = straightPunch ? Vector3.back : -(body.InverseTransformPoint(aim) - a.shoulder).normalized;
                    a.desired = Vector3.Lerp(a.desired, guardPos + away * (windup * Charge01(a) * arm.Reach), smooth);
                    a.drive = -Charge01(a);
                    if (!held || a.t >= chargeTime + autoFire) Fire(arm, a, body, aim);
                    break;

                case State.Punching:
                    float d = Mathf.Lerp(thrustTime.x, thrustTime.y, a.charge);
                    float k = 1f - Mathf.Clamp01(a.t / d);
                    // ปลายหมัดนับจากไหล่ที่ขยับอยู่จริง — ตัวพุ่ง/บิดตามหมัดแล้วแขนยังเหยียดสุด ไม่งอเพราะไหล่ไล่ทันเป้า
                    Vector3 to = Shoulder(arm, body) + a.dir * (arm.Reach * punchReach);
                    a.desired = Vector3.LerpUnclamped(a.from, to, 1f - k * k); // ออกตัวแรง ชะลอตอนถึง
                    a.drive = Mathf.Lerp(a.drive, 1f, 1f - k * k);
                    if (a.t >= d + holdTime) { a.from = a.desired - Shoulder(arm, body); Enter(a, State.Recovering); }
                    break;

                case State.Recovering:
                    float r = Mathf.Clamp01(a.t / recoverTime);
                    // ดึงกลับตรงๆ เทียบไหล่ที่ขยับอยู่ (ไม่กวาดเข้ากลางตัวตอนลำตัวหมุนกลับ) หนักๆ แล้วนิ่ง
                    Vector3 sh = Shoulder(arm, body);
                    a.desired = sh + Vector3.Lerp(a.from, guardPos - sh, r * r * (3f - 2f * r));
                    a.drive = 1f - r * r * (3f - 2f * r);
                    if (held) Enter(a, State.Charging);
                    else if (r >= 1f) Enter(a, State.Guard);
                    break;
            }
            a.local = true;
            Settle(arm, a, body, dt);
        }

        /// <summary>แขนที่ไม่มีคนคุม: ไหลกลับท่าการ์ด</summary>
        public void Idle(IKLimb arm, Arm a, Transform body, float follow, float dt)
        {
            a.state = State.Guard;
            a.local = true;
            a.drive = 0f;
            a.desired = Vector3.Lerp(a.desired, GuardLocal(arm, a, body), 1f - Mathf.Exp(-follow * dt));
            Settle(arm, a, body, dt);
        }

        /// <summary>แขนที่เครื่องอื่นคุม: จำท่าปัจจุบันไว้ ตอนได้คุมคืนมือจะไม่กระโดดไปท่าเก่า</summary>
        public void Release(IKLimb arm, Arm a)
        {
            a.state = State.Guard;
            a.desired = arm.target.localPosition;
            a.world = arm.target.position;
            a.velocity = Vector3.zero;
            a.local = false;
        }

        // มือจริงตามท่าที่ต้องการ: สปริงในโลก (ตัวขยับแล้วมือตามช้า เลยนิดแล้วกลับ) | ตอนพุ่งหมัด = ตรงทันที ให้เหยียดสุดแบบรุนแรง
        private void Settle(IKLimb arm, Arm a, Transform body, float dt)
        {
            if (handFrequency <= 0f || a.state == State.Punching)
            {
                arm.target.localPosition = a.desired;
                a.world = arm.target.position;
                a.velocity = Vector3.zero;
                return;
            }
            IKFeel.Spring(ref a.world, ref a.velocity, body.TransformPoint(a.desired), handFrequency, handDamping, dt);
            arm.target.position = a.world;
        }

        /// <summary>
        /// ลำตัวบิด/โน้มตามแขนที่ยื่นออกจากท่าการ์ด (x = บิดรอบแกนตั้ง, y = โน้มไปหน้า, z = ตัวพุ่งไปหน้า)
        /// ดูจากเป้าเท่านั้น — ง้าง = ไหล่ข้างนั้นถอยหลัง, ต่อย = ไหล่ข้างนั้นพุ่งไปหน้า
        /// </summary>
        public Vector3 BodyPose(IKLimb[] limbs, Arm[] arms)
        {
            float yaw = 0f, pitch = 0f, reach = 0f;
            for (int i = 0; i < limbs.Length; i++)
            {
                if (arms[i] == null) continue;
                // วัดจากการ์ดท่าตั้งต้น ไม่ใช่การ์ดที่หมุนตามลำตัว — ไม่งั้นลำตัวบิดย้อนป้อนกลับมาเอง หมุนช้ากว่าหมัด
                float gz = arms[i].shoulder.z + guard.z * limbs[i].Reach;
                float ext = arms[i].local ? arms[i].drive : Mathf.Clamp((limbs[i].target.localPosition.z - gz) / (limbs[i].Reach * (1f - guard.z)), -1f, 1f);
                yaw -= limbs[i].Side * ext * twist;
                pitch = Mathf.Max(pitch, ext * lean);
                reach = Mathf.Max(reach, ext);
            }
            return new Vector3(yaw, pitch, reach * lunge);
        }

        private void Fire(IKLimb arm, Arm a, Transform body, Vector3 aim)
        {
            a.charge = Charge01(a);
            a.hit = false;
            a.from = a.desired;
            // จำกัดเทียบไหล่ท่าตั้งต้น ไม่ใช่ไหล่ที่บิดถอยตอนง้าง — ไม่งั้นหมัดชาร์จเต็มยื่นสั้นกว่าหมัดแตะ
            if (straightPunch)
            {
                // ตรงไปหน้า: ปลายหมัดอยู่ระดับ/ฝั่งเดียวกับกำปั้นตอนการ์ด แค่ลึกออกไปจนแขนเหยียดสุด
                Vector3 g = GuardLocal(arm, a, body) - Shoulder(arm, body);
                float r = arm.Reach * punchReach;
                a.dir = new Vector3(g.x, g.y, Mathf.Sqrt(Mathf.Max(0f, r * r - g.x * g.x - g.y * g.y))).normalized;
            }
            else
            {
                Vector3 shift = arm.root.position - body.TransformPoint(a.shoulder);
                a.dir = (body.InverseTransformPoint(arm.Limits.Limit(arm, body.rotation, aim + shift) - shift) - a.shoulder).normalized;
            }
            Enter(a, State.Punching);
        }

        private static void Enter(Arm a, State s)
        {
            if (s == State.Charging) a.charge = 0f;
            a.state = s;
            a.t = 0f;
        }
    }
}

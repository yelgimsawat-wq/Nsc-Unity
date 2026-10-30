using System;
using Nsc.Feel;
using UnityEngine;

namespace Nsc.BoneLimb
{
    /// <summary>
    /// ภาพแขน/ขาที่หลุดของหุ่น IK — สถานะหลุด/ต่อจริงอยู่ที่ LimbAttachment (ซิงก์ข้ามเน็ต, RobotLimb/Robot ฟังอยู่แล้ว)
    /// หลุด: ซ่อนชิ้นบนตัวด้วยการย่อกระดูกโคนเหลือเกือบศูนย์ (เมชบางชิ้นผูกกับขาสองข้าง ปิด renderer ทั้งชิ้นไม่ได้)
    ///       แล้วคัดลอกโมเดลทั้งตัว ย้ายกระดูกชิ้นนั้นออกมาเป็นวัตถุแยก ย่อส่วนที่เหลือทิ้ง → ตกพื้นตามฟิสิกส์ (แต่ละเครื่องจำลองเอง)
    /// ดึงกลับ: ชิ้นลอยกลับเข้าโคน (ไหล่/สะโพก) ใน pullTime วิ — Server นับเวลาเดียวกันแล้วสั่งต่อ
    /// </summary>
    [Serializable]
    public class IKDetach
    {
        [Tooltip("กด R ค้างนานเท่านี้ ชิ้นที่หลุดลอยกลับมาต่อ (วินาที)")]
        public float pullTime = 1f;
        [Tooltip("ความเร็วที่ชิ้นกระเด็นออกตอนหลุด — 6 เดิมตกไกล ~11 ม. เกินระยะเอื้อมของแขนอีกข้าง (หยิบมาฟาดไม่ได้) 2.5 ตกใกล้ตัว")]
        public float flingSpeed = 2.5f;
        [Tooltip("มวลของชิ้นที่หลุด")]
        public float mass = 15f;

        [Header("ฟีลตอนต่อกลับ")]
        [Tooltip("ความแรงตอนเสียบเข้าที่ (เทียบดาเมจของ ImpactFeel — ตั้งแต่ heavyHitDamage = เสียงเหล็กหนัก + บูม + หยุดภาพ + ซูม)")]
        public float reattachImpact = 45f;
        [Tooltip("จอสั่นตอนเริ่มดึง (ชิ้นกระชากขึ้นจากพื้น)")]
        public float pullShake = 0.35f;
        [Tooltip("เสียบแล้วชิ้นเด้งใหญ่ขึ้นกี่ส่วนก่อนยุบเข้าที่ (0.12 = 12%)")]
        public float seatPunch = 0.12f;
        [Tooltip("เวลาที่ชิ้นยุบกลับเข้าที่ (วินาที)")]
        public float seatTime = 0.25f;

        private const float Hidden = 0.001f; // ย่อจนมองไม่เห็น — ศูนย์เป๊ะแล้ว Unity เตือนเรื่อง scale

        private GameObject model;
        private Collider[] ignore;
        private Rigidbody[] props;           // ท่อนบน (ต้นแขน/ต้นขา) — ตัวที่จับ/ดึงกลับ
        private Rigidbody[] lowers;          // ท่อนล่าง (ปลายแขน+มือ / หน้าแข้ง+เท้า) ห้อยด้วยบานพับศอก/เข่า — ragdoll สองท่อน
        private Quaternion[] propToRoot;          // หมุนของกระดูกโคนในวัตถุที่หลุด — ใช้หันกลับให้ตรงตอนดึงเข้าโคน
        private Vector3[] pullFrom;
        private Quaternion[] pullFromRot;
        private float[] pullT;

        public void Build(GameObject source, int count, Collider[] robotColliders)
        {
            model = source;
            ignore = robotColliders;
            props = new Rigidbody[count];
            lowers = new Rigidbody[count];
            propToRoot = new Quaternion[count];
            pullFrom = new Vector3[count];
            pullFromRot = new Quaternion[count];
            pullT = new float[count];
        }

        public bool IsOut(int i) => props[i] != null;
        /// <summary>สำเนาชิ้นที่หลุดอยู่บนพื้น (null = ต่ออยู่) — แขนอีกข้างหยิบมาฟาดเป็นกระบองได้</summary>
        public Rigidbody Prop(int i) => props[i];

        /// <summary>ทุกเครื่อง: ชิ้นนี้หลุด — ซ่อนบนตัว แล้วปล่อยสำเนาให้ตก</summary>
        public void Detach(int i, IKLimb limb, Vector3 fling)
        {
            if (props[i] != null) return;

            // สำเนาทั้งโมเดลในท่าตอนนี้ แล้วถอดของที่ ragdoll/ฉากเติมไว้บนกระดูกออก (ข้อต่อก่อน ไม่งั้นลบ Rigidbody ไม่ได้)
            GameObject copy = UnityEngine.Object.Instantiate(model, model.transform.parent);
            copy.name = $"{limb.name}_Detached";
            foreach (Joint j in copy.GetComponentsInChildren<Joint>(true)) UnityEngine.Object.DestroyImmediate(j);
            foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (Rigidbody b in copy.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(b);
            foreach (Behaviour b in copy.GetComponentsInChildren<Behaviour>(true)) b.enabled = false; // Animator, RigBuilder ฯลฯ

            Transform root = Find(copy.transform, limb.root.name), mid = Find(copy.transform, limb.mid.name), hips = Find(copy.transform, "Hips");
            var prop = new GameObject(copy.name);
            prop.layer = limb.root.gameObject.layer; // Ignore Raycast — เท้าไม่เหยียบ/เคอร์เซอร์ไม่ชี้โดน
            prop.transform.SetPositionAndRotation(limb.root.position, limb.root.rotation);
            root.SetParent(prop.transform, true);
            copy.transform.SetParent(prop.transform, true);
            hips.localScale = Vector3.one * Hidden; // ตัว/แขนขาอื่นในสำเนาหายไป เหลือชิ้นนี้
            propToRoot[i] = root.localRotation;

            // ragdoll สองท่อน: ท่อนบน = prop (โคน → ศอก/เข่า) | ท่อนล่าง = กระดูกศอก/เข่าในสำเนา แยกออกมาเป็นวัตถุชั้นนอก
            // (ไม่เป็นลูกของ prop — ตอนมือถือ prop ถูกตั้งตำแหน่งตรงๆ ทุกเฟรม ลูกจะวาร์ปตามแทนที่จะสะบัดตามฟิสิกส์) ต่อด้วยบานพับแกนเดียวกับข้อต่อจริง
            float radius = limb.Reach * 0.12f;
            Vector3 lowerEnd = limb.tip.position + (limb.isArm ? (limb.tip.position - limb.mid.position).normalized * (limb.Lower * 0.4f) : Vector3.zero); // แขน: รวมกำปั้น
            CapsuleCollider upperCap = Capsule(prop.transform, limb.root.position, limb.mid.position, radius);
            mid.SetParent(null, true);
            mid.gameObject.layer = prop.layer;
            CapsuleCollider lowerCap = Capsule(mid, limb.mid.position, lowerEnd, radius);
            Physics.IgnoreCollision(upperCap, lowerCap);
            foreach (Collider c in ignore)
                if (c && c.enabled) { Physics.IgnoreCollision(upperCap, c); Physics.IgnoreCollision(lowerCap, c); }

            Rigidbody body = Body(prop, mass * 0.55f, fling);
            Rigidbody lower = Body(mid.gameObject, mass * 0.45f, fling);
            // มุมพับตอนนี้เป็นมุมศูนย์ของข้อต่อ — ขอบเขตคิดแบบ IKRagdoll.Hinge แต่ใช้มุมตอนหลุดแทนมุมท่าตั้งต้น (งอได้ 0..maxBend ทางเดียวแบบคน)
            float bend = 180f - Vector3.Angle(limb.root.position - limb.mid.position, limb.tip.position - limb.mid.position);
            HingeJoint hinge = mid.gameObject.AddComponent<HingeJoint>();
            hinge.connectedBody = body;
            hinge.axis = limb.HingeAxis;
            float lo = -bend * limb.HingeSign, hi = (limb.Limits.maxBend - bend) * limb.HingeSign;
            hinge.limits = new JointLimits { min = Mathf.Min(lo, hi), max = Mathf.Max(lo, hi) };
            hinge.useLimits = true;
            props[i] = body;
            lowers[i] = lower;

            limb.root.localScale = Vector3.one * Hidden;
        }

        /// <summary>ทุกเครื่อง: ต่อกลับ — ลบสำเนา แสดงชิ้นบนตัวตามเดิม</summary>
        public void Reattach(int i, IKLimb limb)
        {
            limb.root.localScale = Vector3.one;
            if (props[i] == null) return;
            UnityEngine.Object.Destroy(props[i].gameObject);
            if (lowers[i]) UnityEngine.Object.Destroy(lowers[i].gameObject);
            props[i] = null;
            lowers[i] = null;
        }

        /// <summary>ทุกเครื่อง ทุกเฟรม: ถูกดึง = ลอยเข้าโคนให้ถึงใน pullTime | ปล่อยกลางทาง = ตกลงไปใหม่</summary>
        public void Tick(int i, IKLimb limb, bool pulling, float dt)
        {
            Rigidbody body = props[i];
            if (body == null) return;
            Rigidbody lower = lowers[i];
            if (!pulling)
            {
                if (body.isKinematic) body.isKinematic = false;
                if (lower && lower.isKinematic) lower.isKinematic = false;
                pullT[i] = 0f;
                return;
            }
            if (lower && !lower.isKinematic) lower.isKinematic = true; // ลอยกลับ: ท่อนล่างแข็งตามท่อนบน (บานพับยังพาไปตามข้อต่อ)
            if (!body.isKinematic)
            {
                body.isKinematic = true;
                pullFrom[i] = body.position;
                pullFromRot[i] = body.rotation;
                CameraShake.Add(body.position, pullShake, 0.35f); // กระชากขึ้นจากพื้น
            }
            pullT[i] = Mathf.Min(1f, pullT[i] + dt / pullTime);
            Quaternion want = limb.root.rotation * Quaternion.Inverse(propToRoot[i]); // กระดูกในสำเนาหันเท่ากระดูกบนตัว
            // ตำแหน่งเร่งเข้าหาโคน (t²) = พุ่งกระแทกเข้าล็อก ไม่ชะลอนุ่มๆ | หันให้ตรงก่อนถึง (SmoothStep) จะได้เสียบตรงแนว
            body.MovePosition(Vector3.Lerp(pullFrom[i], limb.root.position, pullT[i] * pullT[i]));
            body.MoveRotation(Quaternion.Slerp(pullFromRot[i], want, Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, pullT[i] * 1.5f))));
        }

        // แคปซูลจาก a ถึง b (โลก) บนวัตถุ t ตามแกน local ที่ใกล้แนว a→b ที่สุด | กระดูก FBX มีสเกลสะสม — รัศมีส่งเป็นหน่วยโลก
        private static CapsuleCollider Capsule(Transform t, Vector3 a, Vector3 b, float radius)
        {
            Vector3 la = t.InverseTransformPoint(a), lb = t.InverseTransformPoint(b), d = lb - la;
            CapsuleCollider c = t.gameObject.AddComponent<CapsuleCollider>();
            c.direction = Mathf.Abs(d.x) >= Mathf.Abs(d.y) && Mathf.Abs(d.x) >= Mathf.Abs(d.z) ? 0 : Mathf.Abs(d.y) >= Mathf.Abs(d.z) ? 1 : 2;
            c.center = (la + lb) * 0.5f;
            c.radius = radius / t.lossyScale.x;
            c.height = d.magnitude + c.radius * 2f;
            return c;
        }

        private static Rigidbody Body(GameObject go, float mass, Vector3 fling)
        {
            var body = go.AddComponent<Rigidbody>();
            body.mass = mass;
            body.linearDamping = 0.5f;
            body.angularDamping = 5f; // แคปซูลนอนกลิ้งบนพื้นแทบไม่เสียแรง — เดิมไถลไม่หยุด (วัดได้ ~2.5 m/s ไกลเกือบ 30 ม.) แขนอีกข้างเอื้อมไม่ถึง
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // ชิ้นใหญ่ตกเร็ว — ไม่งั้นทะลุพื้นบางๆ
            body.linearVelocity = fling;
            return body;
        }

        private static Transform Find(Transform parent, string suffix)
        {
            foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
                if (t.name == suffix || t.name.EndsWith(":" + suffix)) return t;
            throw new InvalidOperationException($"[IKDetach] ไม่เจอกระดูก {suffix} ในสำเนา");
        }
    }
}

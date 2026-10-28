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
        [Tooltip("ความเร็วที่ชิ้นกระเด็นออกตอนหลุด")]
        public float flingSpeed = 6f;
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
        private Rigidbody[] props;
        private Quaternion[] propToRoot;          // หมุนของกระดูกโคนในวัตถุที่หลุด — ใช้หันกลับให้ตรงตอนดึงเข้าโคน
        private Vector3[] pullFrom;
        private Quaternion[] pullFromRot;
        private float[] pullT;

        public void Build(GameObject source, int count, Collider[] robotColliders)
        {
            model = source;
            ignore = robotColliders;
            props = new Rigidbody[count];
            propToRoot = new Quaternion[count];
            pullFrom = new Vector3[count];
            pullFromRot = new Quaternion[count];
            pullT = new float[count];
        }

        public bool IsOut(int i) => props[i] != null;

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

            Transform root = Find(copy.transform, limb.root.name), hips = Find(copy.transform, "Hips");
            var prop = new GameObject(copy.name);
            prop.layer = limb.root.gameObject.layer; // Ignore Raycast — เท้าไม่เหยียบ/เคอร์เซอร์ไม่ชี้โดน
            prop.transform.SetPositionAndRotation(limb.root.position, limb.root.rotation);
            root.SetParent(prop.transform, true);
            copy.transform.SetParent(prop.transform, true);
            hips.localScale = Vector3.one * Hidden; // ตัว/แขนขาอื่นในสำเนาหายไป เหลือชิ้นนี้
            propToRoot[i] = root.localRotation;

            Vector3 along = prop.transform.InverseTransformPoint(limb.tip.position);
            var capsule = prop.AddComponent<CapsuleCollider>();
            capsule.direction = Mathf.Abs(along.x) > Mathf.Abs(along.y) ? (Mathf.Abs(along.x) > Mathf.Abs(along.z) ? 0 : 2) : (Mathf.Abs(along.y) > Mathf.Abs(along.z) ? 1 : 2);
            capsule.center = along * 0.5f;
            capsule.radius = limb.Reach * 0.12f;
            capsule.height = along.magnitude + capsule.radius * 2f;
            foreach (Collider c in ignore) if (c && c.enabled) Physics.IgnoreCollision(capsule, c);

            var body = prop.AddComponent<Rigidbody>();
            body.mass = mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // ชิ้นใหญ่ตกเร็ว — ไม่งั้นทะลุพื้นบางๆ
            body.linearVelocity = fling;
            props[i] = body;

            limb.root.localScale = Vector3.one * Hidden;
        }

        /// <summary>ทุกเครื่อง: ต่อกลับ — ลบสำเนา แสดงชิ้นบนตัวตามเดิม</summary>
        public void Reattach(int i, IKLimb limb)
        {
            limb.root.localScale = Vector3.one;
            if (props[i] == null) return;
            UnityEngine.Object.Destroy(props[i].gameObject);
            props[i] = null;
        }

        /// <summary>ทุกเครื่อง ทุกเฟรม: ถูกดึง = ลอยเข้าโคนให้ถึงใน pullTime | ปล่อยกลางทาง = ตกลงไปใหม่</summary>
        public void Tick(int i, IKLimb limb, bool pulling, float dt)
        {
            Rigidbody body = props[i];
            if (body == null) return;
            if (!pulling)
            {
                if (body.isKinematic) body.isKinematic = false;
                pullT[i] = 0f;
                return;
            }
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

        private static Transform Find(Transform parent, string suffix)
        {
            foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
                if (t.name == suffix || t.name.EndsWith(":" + suffix)) return t;
            throw new InvalidOperationException($"[IKDetach] ไม่เจอกระดูก {suffix} ในสำเนา");
        }
    }
}

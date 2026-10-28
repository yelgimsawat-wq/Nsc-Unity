using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nsc.BoneLimb
{
    /// <summary>
    /// แขนหรือขาหนึ่งข้างของหุ่น IK: กระดูก 3 ท่อน (โคน → ข้อพับ → ปลาย) + เป้าที่ปลายต้องไปถึง
    /// จำท่าตั้งต้น (T-pose) ของตัวเอง หาแกนบานพับเอง แก้ IK ให้ปลายถึงเป้า หันข้อมือ และกำ/แบมือ
    /// ข้อจำกัดของไหล่/สะโพกเสียบเข้ามาเป็น <see cref="JointLimit"/>
    /// </summary>
    [Serializable]
    public class IKLimb
    {
        // ⚠️ ชื่อ field ต้องคงเดิม — ค่าในฉาก IKTest ผูกกับชื่อเหล่านี้
        public string name;
        public bool isArm;
        public Transform root, mid, tip;   // ไหล่/สะโพก → ศอก/เข่า → มือ/เท้า
        public Transform target;           // เป้าของ IK (แขน = ลูกของตัวหุ่น, ขา = อยู่ในโลก)
        public Rigidbody hit;              // collider kinematic ที่ตามมือ/เท้า
        public AnimationClip gripClip;     // แขนเท่านั้น: เฟรมแรก = แบ, เฟรมท้าย = กำ

        public JointLimit Limits { get; private set; }
        public float Upper { get; private set; }
        public float Lower { get; private set; }
        public float Reach => Upper + Lower;
        /// <summary>-1 ซ้าย / +1 ขวา ในแกนตัวหุ่น</summary>
        public float Side { get; private set; }
        /// <summary>ความสูงปลาย (ข้อเท้า) จากพื้นตอนยืน</summary>
        public float RestHeight { get; private set; }
        /// <summary>มุมเป้าตอนเริ่ม เทียบตัวหุ่น — ใช้วางเท้าให้ราบ</summary>
        public Quaternion RestRotation { get; private set; }
        public float Grip { get; set; }
        /// <summary>ทิศศอก/เข่าในแกนตัวหุ่นที่บังคับ (เช่นศอกชี้ลงตอนตั้งการ์ด) — ศูนย์ = ทิศตามท่าตั้งต้นแบบเดิม</summary>
        public Vector3 PoleOverride { get; set; }
        public bool Lifted { get; set; }
        /// <summary>หุ่นนอนอยู่: ผู้เล่นคลิกค้างดึงปลายชิ้นนี้ไปที่เป้า (ragdoll ออกแรงตาม)</summary>
        public bool Pull { get; set; }
        /// <summary>แขน: ระยะระนาบมือหน้าไหล่</summary>
        public float Depth { get; set; }
        /// <summary>บานพับศอก/เข่า (แกน local ของกระดูกกลาง, ทิศงอ, มุมงอตอนท่าตั้งต้น) — ragdoll ใช้ตั้งข้อต่อให้พับทางเดียวกับ IK</summary>
        public Vector3 HingeAxis => hingeAxis;
        public float HingeSign => hingeSign;
        public float RestBend => restBend;

        // ท่าตั้งต้นในแกนตัวหุ่น — ใช้หมุนทิศศอก/เข่าและข้อมือตามการแกว่งแบบต่อเนื่อง
        private Vector3 restDir, restPole, restFingerDir;
        private Vector3 palmLocal; // ทิศฝ่ามือในแกนของกระดูกมือ (หาจากท่ากำ: ปลายนิ้วงอเข้าหาฝ่ามือ) — ศูนย์ = ไม่รู้
        private Quaternion restTipRotation;
        // บานพับ: แกน local ของกระดูกกลางที่โมเดลออกแบบให้พับ
        private Vector3 hingeAxis;
        private float hingeSign, restBend;
        private Quaternion midRest;
        private Transform[] fingers;
        private Quaternion[] open, closed;

        /// <summary>เรียกตอน Awake ขณะกระดูกยังเป็นท่าตั้งต้น</summary>
        public void Init(Transform body, float groundY, JointLimit limits, GameObject model)
        {
            Debug.Assert(root && mid && tip && target, $"[IKLimb] {name}: ใส่กระดูก/เป้าไม่ครบ");
            Limits = limits;
            Quaternion toBody = Quaternion.Inverse(body.rotation);

            Upper = Vector3.Distance(root.position, mid.position);
            Lower = Vector3.Distance(mid.position, tip.position);
            Side = Mathf.Sign(body.InverseTransformPoint(root.position).x);
            RestHeight = tip.position.y - groundY;
            RestRotation = toBody * target.rotation;
            Depth = Reach * 0.6f;

            restDir = toBody * (tip.position - root.position).normalized;
            restPole = (isArm ? new Vector3(0f, -1f, -0.5f) : Vector3.forward).normalized;
            restTipRotation = toBody * tip.rotation;
            Transform finger = FindChild(tip, "Middle1");
            restFingerDir = finger ? toBody * (finger.position - tip.position).normalized : restDir;

            DetectHinge(body.rotation);
            if (isArm) BakeGrip(model);
        }

        // กฎของโคไซน์: มุมงอ (0 = ตรง) ↔ ระยะโคนถึงปลาย
        public float LengthForBend(float bendDeg) =>
            Mathf.Sqrt(Upper * Upper + Lower * Lower + 2f * Upper * Lower * Mathf.Cos(bendDeg * Mathf.Deg2Rad));

        public float BendForLength(float length)
        {
            float c = (length * length - Upper * Upper - Lower * Lower) / (2f * Upper * Lower);
            return Mathf.Acos(Mathf.Clamp(c, -1f, 1f)) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// IK บานพับ: ศอก/เข่าพับรอบแกนเดียวของกระดูกตามที่โมเดลออกแบบ ไหล่/สะโพกหมุนแบบลูกกลมให้ปลายถึงเป้า
        /// แล้วหมุนทั้งแขนรอบแนวโคน→เป้าให้ข้อพับชี้ทิศ pole: ทิศตอน T-pose (ศอกลง-หลัง, เข่าหน้า)
        /// หมุนตามการแกว่งแบบสั้นที่สุด — ต่อเนื่องทุกท่าที่ข้อต่อไปถึง จึงไม่พลิกกลางทาง
        /// </summary>
        public void Solve(Quaternion bodyRot)
        {
            Vector3 s = root.position;
            Vector3 toTarget = target.position - s;
            float dist = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(Upper - Lower) + 1e-3f, Reach - 1e-3f);
            float bend = Mathf.Clamp(BendForLength(dist), 0f, Limits.maxBend);
            mid.localRotation = midRest * Quaternion.AngleAxis((bend - restBend) * hingeSign, hingeAxis);

            root.rotation = Quaternion.FromToRotation(tip.position - s, toTarget) * root.rotation;

            Vector3 axis = toTarget.normalized;
            Quaternion swing = Quaternion.FromToRotation(restDir, Quaternion.Inverse(bodyRot) * axis);
            Vector3 pole = Vector3.ProjectOnPlane(bodyRot * (PoleOverride != Vector3.zero ? PoleOverride : swing * restPole), axis);
            Vector3 joint = Vector3.ProjectOnPlane(mid.position - s, axis);
            if (pole.sqrMagnitude > 1e-6f && joint.sqrMagnitude > 1e-6f)
                root.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(joint, pole, axis), axis) * root.rotation;

            tip.rotation = target.rotation;
        }

        /// <summary>ข้อมือหันไปทางที่แขนเอื้อม (ไหล่ → มือ) หมุนจากท่าตั้งต้นแบบสั้นที่สุด ฝ่ามือจึงไม่บิดมั่ว</summary>
        public void OrientHand(Quaternion bodyRot) => OrientHand(bodyRot, target.position - root.position);

        /// <summary>
        /// ข้อมือหันตามทิศที่กำหนด เช่นแนวท่อนแขนล่าง (ข้อมือตรง ไม่หักเป็นมือแมวตอนตั้งการ์ด)
        /// palmToward (ไม่บังคับ): หมุนกำปั้นรอบแนวแขนให้ฝ่ามือหันไปทางนั้นมากที่สุด
        /// </summary>
        public void OrientHand(Quaternion bodyRot, Vector3 along, Vector3 palmToward = default)
        {
            Vector3 dir = along.normalized;
            Quaternion rot = Quaternion.FromToRotation(bodyRot * restFingerDir, dir) * (bodyRot * restTipRotation);
            Vector3 palmNow = Vector3.ProjectOnPlane(rot * palmLocal, dir);
            Vector3 palmWant = Vector3.ProjectOnPlane(palmToward, dir);
            if (palmNow.sqrMagnitude > 1e-6f && palmWant.sqrMagnitude > 1e-6f)
                rot = Quaternion.AngleAxis(Vector3.SignedAngle(palmNow, palmWant, dir), dir) * rot;
            target.rotation = rot;
        }

        /// <summary>นิ้ว: ทับหลังแก้ IK (นิ้วเป็นลูกของมือ ใช้ local rotation)</summary>
        public void ApplyFingers()
        {
            if (fingers == null) return;
            for (int i = 0; i < fingers.Length; i++)
                fingers[i].localRotation = Quaternion.Slerp(open[i], closed[i], Grip);
        }

        // ลองพับกระดูกกลางรอบแกน local X/Y/Z ทีละแกน แกนที่ปลายขยับไปทิศที่ควรพับมากสุด = บานพับ
        // (แขน: ปลายแขนพับมาข้างหน้า, ขา: ปลายขาพับไปข้างหลัง)
        private void DetectHinge(Quaternion bodyRot)
        {
            Vector3 want = bodyRot * (isArm ? Vector3.forward : Vector3.back);
            Quaternion rest = mid.localRotation;
            Vector3 before = tip.position;
            float best = 0f;
            foreach (Vector3 axis in new[] { Vector3.right, Vector3.up, Vector3.forward })
            {
                mid.localRotation = rest * Quaternion.AngleAxis(10f, axis);
                float move = Vector3.Dot(tip.position - before, want);
                mid.localRotation = rest;
                if (Mathf.Abs(move) > Mathf.Abs(best)) { best = move; hingeAxis = axis; hingeSign = Mathf.Sign(move); }
            }
            midRest = rest;
            restBend = 180f - Vector3.Angle(root.position - mid.position, tip.position - mid.position);

            // ท่าตั้งต้นงอสวนทางบานพับ (เช่นเข่าแอ่นไปหลังนิดๆ) → มุมตั้งต้นติดลบ ไม่งั้นพับขาดไป 2 เท่าของมุมนั้น ปลายไม่ถึงเป้า
            mid.localRotation = rest * Quaternion.AngleAxis(hingeSign, hingeAxis);
            float bentMore = 180f - Vector3.Angle(root.position - mid.position, tip.position - mid.position);
            mid.localRotation = rest;
            if (bentMore < restBend) restBend = -restBend;
        }

        // เก็บท่านิ้วตอนแบ/กำจากคลิปไว้ครั้งเดียว ตอนเล่นแค่ Slerp ระหว่างสองท่า (ไม่ต้องมี Animator)
        private void BakeGrip(GameObject model)
        {
            if (gripClip == null || model == null) return;

            var list = new List<Transform>(tip.GetComponentsInChildren<Transform>(true));
            list.Remove(tip);
            fingers = list.ToArray();
            open = new Quaternion[fingers.Length];
            closed = new Quaternion[fingers.Length];

            model.transform.GetLocalPositionAndRotation(out Vector3 pos, out Quaternion rot);
            Transform fingertip = FindChild(tip, "Middle3");
            gripClip.SampleAnimation(model, 0f);
            for (int i = 0; i < fingers.Length; i++) open[i] = fingers[i].localRotation;
            Vector3 openTip = fingertip ? tip.InverseTransformPoint(fingertip.position) : Vector3.zero;
            gripClip.SampleAnimation(model, gripClip.length);
            for (int i = 0; i < fingers.Length; i++) closed[i] = fingers[i].localRotation;
            if (fingertip) palmLocal = (tip.InverseTransformPoint(fingertip.position) - openTip).normalized;
            gripClip.SampleAnimation(model, 0f);
            model.transform.SetLocalPositionAndRotation(pos, rot);
        }

        private static Transform FindChild(Transform parent, string contains)
        {
            foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
                if (t != parent && t.name.Contains(contains)) return t;
            return null;
        }
    }
}

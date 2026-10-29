using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nsc.BoneLimb
{
    /// <summary>
    /// ล้มแบบ ragdoll บนกระดูกของโมเดล: ตอนเริ่ม (ท่าตั้งต้น) สร้าง Rigidbody + collider + ข้อต่อให้กระดูกหลัก 11 ท่อน
    /// ยืน = kinematic + collider ปิด (IK คุมกระดูก) | ล้ม = ฟิสิกส์คุม นอนจนกว่าจะสั่งลุก | ลุก = ไล่จากท่านอนกลับไปท่าที่ IK แก้ได้
    /// Server จำลองทั้งตัว ส่วน client ให้สะโพกตามค่าที่ซิงก์มา ท่อนอื่นห้อยตามข้อต่อเอง
    /// </summary>
    [Serializable]
    public class IKRagdoll
    {
        [Tooltip("เวลาลุกจากท่านอนกลับเป็นท่ายืน (วินาที)")]
        public float getUpTime = 1.2f;
        [Tooltip("ความเร็วแนวนอนที่ผลักลำตัวตอนเริ่มล้ม — ล้มไปทางที่ตัวเคลื่อน (ยืนนิ่ง = หงายหลัง) ไม่ใช่ทรุดลงตรงๆ")]
        public float topple = 3f;
        [Tooltip("มวลรวมทั้งตัว แบ่งให้แต่ละท่อนตามสัดส่วน")]
        public float totalMass = 120f;
        [Tooltip("ความหนาของ collider แขนขา (คูณรัศมี)")]
        public float thickness = 1f;
        [Tooltip("นอนอยู่แล้วคลิกค้าง: แรงสปริงดึงปลายแขนขาไปที่เป้า (ความเร่งต่อหน่วยระยะ) — ใช้ยันพื้น/คลาน/ปัดของ")]
        public float pullStrength = 25f;
        [Tooltip("หน่วงแรงดึง กันปลายแขนขาเหวี่ยงเลยเป้า")]
        public float pullDamping = 6f;

        private struct Part
        {
            public Transform bone;
            public Rigidbody body;
            public Vector3 restPos;
            public Quaternion restRot;
            public Quaternion fromRot;
        }

        private Part[] parts;               // สะโพก (ตัวแรก) อก หัว แขน×2 ขา×2
        private readonly List<Collider> colliders = new List<Collider>();
        private readonly List<Collider> others = new List<Collider>();
        // ชิ้นที่หลุด (กระดูกถูกย่อซ่อนอยู่): ปิด collider (collider จิ๋ว = ความเฉื่อยเกือบศูนย์ ทั้งตัวสั่น) + เบาเกือบศูนย์ ห้อยตามฟิสิกส์
        // ห้ามเป็น kinematic ตอนล้ม — ข้อต่อผูกกับสะโพก ตัวเลยห้อยค้างกลางอากาศกับขาที่มองไม่เห็น | ค่า = มวลเดิมไว้คืนตอนต่อกลับ
        private readonly Dictionary<Rigidbody, float> frozen = new Dictionary<Rigidbody, float>();
        private const float FrozenMassScale = 0.02f;
        private readonly HashSet<Collider> frozenColliders = new HashSet<Collider>();
        private bool physicsOn;
        private Vector3 hipsFrom;
        private Quaternion hipsFromRot;

        public Rigidbody Hips => parts[0].body;

        /// <summary>เรียกตอน Awake หลัง IKLimb.Init ขณะกระดูกยังเป็นท่าตั้งต้น (ข้อต่อจำท่านี้เป็นมุมศูนย์)</summary>
        public void Build(Transform body, GameObject model, IKLimb[] limbs, IEnumerable<Collider> ignore)
        {
            Transform hips = Bone(model, "Hips"), chest = Bone(model, "Spine1"), neck = Bone(model, "Neck"),
                      head = Bone(model, "Head"), headTop = Bone(model, "HeadTop_End");
            IKLimb la = limbs[0], ra = limbs[1], ll = limbs[2], rl = limbs[3];
            float hipWidth = Vector3.Distance(ll.root.position, rl.root.position);
            float shoulderWidth = Vector3.Distance(la.root.position, ra.root.position);

            var list = new List<Part>();
            Rigidbody hipsBody = Add(list, hips, 0.15f);
            Capsule(hips, ll.root.position, rl.root.position, hipWidth * 0.4f);

            Rigidbody chestBody = Add(list, chest, 0.25f);
            Capsule(chest, chest.position, neck.position, shoulderWidth * 0.28f);
            Capsule(chest, la.root.position, ra.root.position, shoulderWidth * 0.2f);
            Twist(chest, chestBody, hipsBody, neck.position, 15f, 20f);

            Rigidbody headBody = Add(list, head, 0.05f);
            Sphere(head, (head.position + headTop.position) * 0.5f, Vector3.Distance(head.position, headTop.position) * 0.45f);
            Twist(head, headBody, chestBody, headTop.position, 30f, 30f);

            foreach (IKLimb limb in limbs)
            {
                Rigidbody parent = limb.isArm ? chestBody : hipsBody;
                Rigidbody upper = Add(list, limb.root, limb.isArm ? 0.05f : 0.1f);
                Capsule(limb.root, limb.root.position, limb.mid.position, limb.Upper * (limb.isArm ? 0.25f : 0.35f) * thickness);
                Twist(limb.root, upper, parent, limb.mid.position, limb.isArm ? 45f : 20f, limb.isArm ? 80f : 60f);

                Rigidbody lower = Add(list, limb.mid, limb.isArm ? 0.06f : 0.09f);
                Vector3 along = (limb.tip.position - limb.mid.position).normalized;
                if (limb.isArm) // ปลายแขน + กำปั้น
                    Capsule(limb.mid, limb.mid.position, limb.tip.position + along * limb.Lower * 0.4f, limb.Lower * 0.35f * thickness);
                else // หน้าแข้ง + ฝ่าเท้า (ยื่นไปหน้า)
                {
                    Capsule(limb.mid, limb.mid.position, limb.tip.position, limb.Lower * 0.28f * thickness);
                    Capsule(limb.tip, limb.tip.position, limb.tip.position + body.forward * limb.Lower * 0.4f, limb.Lower * 0.18f * thickness);
                }
                Hinge(limb, lower, upper);
            }
            parts = list.ToArray();

            others.AddRange(ignore);
            Enforce(false, false);
            SetPhysics(false, Vector3.zero);
        }

        /// <summary>เปิด/ปิดฟิสิกส์ — ล้ม: กระดูกเริ่มจากท่าตอนนี้ + ความเร็ว push | ยืน: IK คุม</summary>
        public void SetPhysics(bool on, Vector3 push)
        {
            physicsOn = on;
            foreach (Collider c in colliders) c.enabled = on && !frozenColliders.Contains(c);
            if (on) IgnoreOwnCollisions();
            foreach (Part p in parts)
            {
                // interpolate เฉพาะตอนฟิสิกส์คุม — ตอน IK คุม มันจะเขียนท่าเก่าทับกระดูกที่ IK ไม่ได้แตะ (สะโพก/อก/หัว)
                p.body.interpolation = on ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
                if (on && !p.body.isKinematic)
                {
                    p.body.linearVelocity = push;
                    p.body.angularVelocity = Vector3.zero;
                }
            }
        }

        /// <summary>ทุก FixedUpdate: kinematic ตามสถานะ — Robot.SetSimulationEnabled ปลด kinematic ทุกชิ้นตอนเริ่มแมตช์</summary>
        public void Enforce(bool physics, bool hipsFollowNetwork)
        {
            for (int i = 0; i < parts.Length; i++)
            {
                bool kinematic = !physics || (i == 0 && hipsFollowNetwork);
                if (parts[i].body.isKinematic != kinematic) parts[i].body.isKinematic = kinematic;
            }
        }

        /// <summary>แขน/ขาหลุด (on) หรือต่อกลับ — ชิ้นที่หลุดไม่มี collider และเบาเกือบศูนย์ ห้อยตามตัวไปเฉยๆ ไม่ถ่วง/ไม่ค้ำตัว</summary>
        public void SetFrozen(IKLimb limb, bool on)
        {
            foreach (Rigidbody b in limb.root.GetComponentsInChildren<Rigidbody>(true))
            {
                if (on && !frozen.ContainsKey(b)) { frozen[b] = b.mass; b.mass *= FrozenMassScale; }
                else if (!on && frozen.TryGetValue(b, out float mass)) { b.mass = mass; frozen.Remove(b); }
            }
            foreach (Collider c in limb.root.GetComponentsInChildren<Collider>(true))
            {
                if (!colliders.Contains(c)) continue;
                if (on) frozenColliders.Add(c); else frozenColliders.Remove(c);
                c.enabled = physicsOn && !on;
            }
            if (physicsOn && !on) IgnoreOwnCollisions(); // เปิด collider ใหม่ = Unity ล้าง IgnoreCollision
        }

        /// <summary>client ตอนล้ม: สะโพกตามท่าที่ Server ส่งมา ท่อนอื่นห้อยตามข้อต่อ</summary>
        public void FollowHips(Vector3 position, Quaternion rotation, float t)
        {
            Rigidbody hips = Hips;
            hips.MovePosition(Vector3.Lerp(hips.position, position, t));
            hips.MoveRotation(Quaternion.Slerp(hips.rotation, rotation, t));
        }

        /// <summary>นอนอยู่: ดึงปลายแขน/ขา (ท่อนล่าง) ไปที่เป้าด้วยสปริง — แรงส่งต่อผ่านข้อต่อไปทั้งตัว</summary>
        public void Pull(IKLimb limb, Vector3 target)
        {
            Rigidbody lower = limb.mid.GetComponent<Rigidbody>();
            if (lower.isKinematic) return;
            Vector3 tip = limb.tip.position;
            Vector3 accel = (target - tip) * pullStrength - lower.GetPointVelocity(tip) * pullDamping;
            lower.AddForceAtPosition(accel * lower.mass, tip);
        }

        /// <summary>เริ่มลุก: จำท่านอนตอนนี้ไว้ไล่ออก — สะโพกในโลก, ท่อนอื่นเป็นมุมข้อต่อ (local)</summary>
        public void BeginBlend()
        {
            hipsFrom = parts[0].bone.position;
            hipsFromRot = parts[0].bone.rotation;
            for (int i = 0; i < parts.Length; i++) parts[i].fromRot = parts[i].bone.localRotation;
        }

        /// <summary>ระหว่างลุก ก่อนแก้ IK: กระดูกที่ฟิสิกส์ขยับกลับเป็นท่าตั้งต้น (สะโพก/อก/หัวไม่มีใครตั้งให้)</summary>
        public void RestoreRest()
        {
            foreach (Part p in parts) p.bone.SetLocalPositionAndRotation(p.restPos, p.restRot);
        }

        /// <summary>
        /// ระหว่างลุก หลังแก้ IK: ไล่จากท่านอน (t = 0) ไปท่าที่ IK แก้ได้ (t = 1)
        /// ไล่มุมข้อต่อ (local) ไม่ใช่มุมโลก — มุมโลกของพ่อกับลูกไล่แยกกันจะทำให้ศอก/เข่าหมุนวนผ่านท่าที่ข้อต่อพับไม่ได้
        /// </summary>
        public void Blend(float t)
        {
            Transform hips = parts[0].bone;
            hips.SetPositionAndRotation(Vector3.Lerp(hipsFrom, hips.position, t), Quaternion.Slerp(hipsFromRot, hips.rotation, t));
            for (int i = 1; i < parts.Length; i++)
                parts[i].bone.localRotation = Quaternion.Slerp(parts[i].fromRot, parts[i].bone.localRotation, t);
        }

        // ไม่ชนกันเองและไม่ชน collider อื่นของหุ่น (กล่องลำตัว, _Hit มือ/เท้า) — ข้อต่อคุมท่าอยู่แล้ว แคปซูลที่ซ้อนกันตรงข้อต่อจะดีดตัวลอย
        // ต้องตั้งใหม่ทุกครั้งที่เปิด collider: Unity ล้าง IgnoreCollision เมื่อ collider ถูกปิด
        private void IgnoreOwnCollisions()
        {
            for (int i = 0; i < colliders.Count; i++)
            {
                if (!colliders[i].enabled) continue; // ปิดอยู่ = Unity ไม่ยอมตั้ง (และไม่ต้องตั้ง)
                for (int j = i + 1; j < colliders.Count; j++)
                    if (colliders[j].enabled) Physics.IgnoreCollision(colliders[i], colliders[j]);
                foreach (Collider c in others) if (c && c.enabled) Physics.IgnoreCollision(colliders[i], c);
            }
        }

        // ================================================================ สร้าง

        private Rigidbody Add(List<Part> list, Transform bone, float massShare)
        {
            Rigidbody rb = bone.gameObject.AddComponent<Rigidbody>();
            rb.mass = totalMass * massShare;
            rb.isKinematic = true;
            rb.solverIterations = 12;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            list.Add(new Part { bone = bone, body = rb, restPos = bone.localPosition, restRot = bone.localRotation });
            return rb;
        }

        // แคปซูลจาก a ถึง b (พิกัดโลก) ตามแกน local ของกระดูกที่ใกล้แนว a→b ที่สุด | รัศมีส่งเป็นหน่วยโลก (กระดูก FBX มีสเกลสะสม ~100)
        private void Capsule(Transform bone, Vector3 a, Vector3 b, float radius)
        {
            radius /= bone.lossyScale.x;
            Vector3 la = bone.InverseTransformPoint(a), lb = bone.InverseTransformPoint(b), d = lb - la;
            int axis = Mathf.Abs(d.x) >= Mathf.Abs(d.y) && Mathf.Abs(d.x) >= Mathf.Abs(d.z) ? 0 : Mathf.Abs(d.y) >= Mathf.Abs(d.z) ? 1 : 2;
            CapsuleCollider c = bone.gameObject.AddComponent<CapsuleCollider>();
            c.direction = axis;
            c.center = (la + lb) * 0.5f;
            c.radius = radius;
            c.height = Mathf.Abs(d[axis]) + radius * 2f;
            colliders.Add(c);
        }

        private void Sphere(Transform bone, Vector3 center, float radius)
        {
            SphereCollider c = bone.gameObject.AddComponent<SphereCollider>();
            c.center = bone.InverseTransformPoint(center);
            c.radius = radius / bone.lossyScale.x;
            colliders.Add(c);
        }

        // ข้อต่อลูกกลม: บิดรอบแนวกระดูกได้ ±twist, แกว่งได้ swing ทุกทิศ
        private static void Twist(Transform bone, Rigidbody body, Rigidbody parent, Vector3 toward, float twist, float swing)
        {
            CharacterJoint j = body.gameObject.AddComponent<CharacterJoint>();
            j.connectedBody = parent;
            j.axis = bone.InverseTransformDirection(toward - bone.position).normalized;
            j.swingAxis = Vector3.Cross(j.axis, Mathf.Abs(j.axis.x) < 0.9f ? Vector3.right : Vector3.up).normalized;
            j.lowTwistLimit = new SoftJointLimit { limit = -twist };
            j.highTwistLimit = new SoftJointLimit { limit = twist };
            j.swing1Limit = new SoftJointLimit { limit = swing };
            j.swing2Limit = new SoftJointLimit { limit = swing };
            j.enableProjection = true;
        }

        // ศอก/เข่า: บานพับแกนเดียวกับ IK งอได้ 0..maxBend เหมือนตอนยืน (ข้อต่อจำท่าตั้งต้นเป็นมุมศูนย์ = งอ RestBend)
        private static void Hinge(IKLimb limb, Rigidbody body, Rigidbody parent)
        {
            HingeJoint j = body.gameObject.AddComponent<HingeJoint>();
            j.connectedBody = parent;
            j.axis = limb.HingeAxis;
            float a = (0f - limb.RestBend) * limb.HingeSign, b = (limb.Limits.maxBend - limb.RestBend) * limb.HingeSign;
            j.limits = new JointLimits { min = Mathf.Min(a, b), max = Mathf.Max(a, b) };
            j.useLimits = true;
        }

        private static Transform Bone(GameObject model, string suffix)
        {
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                if (t.name.EndsWith(":" + suffix) || t.name == suffix) return t;
            throw new InvalidOperationException($"[IKRagdoll] ไม่เจอกระดูก {suffix} ในโมเดล {model.name}");
        }
    }
}

using System;
using UnityEngine;

namespace Nsc.BoneLimb
{
    /// <summary>
    /// ข้อจำกัดข้อต่อแบบคนของโคนแขน/ขา (ลูกกลม) + ข้อพับ (บานพับ)
    /// แปลงทิศโคน→ปลาย (แกนตัวหุ่น: +X ขวา, +Y บน, +Z หน้า) เป็นมุมข้อต่อ จำกัด แล้วแปลงกลับ
    /// "ออก" = ห่างจากลำตัว (คูณ side ของแขน/ขา)
    /// </summary>
    [Serializable]
    public abstract class JointLimit
    {
        [Tooltip("ข้อพับ (ศอก/เข่า) งอได้สูงสุดกี่องศา (0 = เหยียดตรง)")]
        public float maxBend;

        /// <summary>มุมข้อต่อจากทิศในแกนตัวหุ่น — yawRef ใช้เฉพาะไหล่ (ดู <see cref="ShoulderLimit"/>)</summary>
        public abstract Vector2 Angles(float side, Vector3 dir, float yawRef = float.NaN);

        /// <summary>ทิศในแกนตัวหุ่นจากมุมข้อต่อ (จำกัดมุมในตัว)</summary>
        public abstract Vector3 Direction(float side, Vector2 angles);

        public Vector2 AnglesOf(IKLimb limb, Quaternion bodyRot, Vector3 world, float yawRef = float.NaN) =>
            Angles(limb.Side, Quaternion.Inverse(bodyRot) * (world - limb.root.position), yawRef);

        /// <summary>ดันเป้าใดๆ ให้อยู่ในท่าที่ข้อต่อทำได้ — Server ใช้ตรวจเป้าจาก client ด้วย</summary>
        public Vector3 Limit(IKLimb limb, Quaternion bodyRot, Vector3 world, float yawRef = float.NaN)
        {
            Vector3 d = Quaternion.Inverse(bodyRot) * (world - limb.root.position);
            Vector3 dir = Direction(limb.Side, Angles(limb.Side, d, yawRef));
            float bend = Mathf.Clamp(limb.BendForLength(d.magnitude), 3f, maxBend);
            return limb.root.position + bodyRot * dir * limb.LengthForBend(bend);
        }

        /// <summary>นอกช่วง → ขอบฝั่งที่อยู่ตอนนี้ (reference) หรือถ้าไม่มีก็ขอบที่ใกล้กว่าแบบวนรอบ</summary>
        protected static float ClampAngle(float a, Vector2 range, float reference = float.NaN)
        {
            if (a >= range.x && a <= range.y) return a;
            if (!float.IsNaN(reference)) return Mathf.Abs(reference - range.x) < Mathf.Abs(reference - range.y) ? range.x : range.y;
            return Mathf.Abs(Mathf.DeltaAngle(a, range.x)) < Mathf.Abs(Mathf.DeltaAngle(a, range.y)) ? range.x : range.y;
        }
    }

    /// <summary>ไหล่: มุม = (หันออก, เงย)</summary>
    [Serializable]
    public class ShoulderLimit : JointLimit
    {
        [Tooltip("หันซ้าย-ขวา: ลบ = ข้ามลำตัว, บวก = กางออกไปทางหลัง")]
        public Vector2 yaw = new Vector2(-50f, 120f);
        public Vector2 pitch = new Vector2(-80f, 80f);

        public ShoulderLimit() => maxBend = 140f;

        // yawRef = มุมหันตอนนี้: เลยขอบแล้วติดขอบฝั่งที่แขนอยู่ ไม่กระโดดไปขอบอีกฝั่ง (ชี้หลังตรงๆ ไม่มี ref = กางไปหลัง)
        public override Vector2 Angles(float side, Vector3 dir, float yawRef = float.NaN)
        {
            float outward = dir.x * side;
            return new Vector2(ClampAngle(Mathf.Atan2(outward, dir.z) * Mathf.Rad2Deg, yaw, yawRef),
                               Mathf.Atan2(dir.y, new Vector2(outward, dir.z).magnitude) * Mathf.Rad2Deg);
        }

        public override Vector3 Direction(float side, Vector2 angles)
        {
            float y = Mathf.Clamp(angles.x, yaw.x, yaw.y) * Mathf.Deg2Rad;
            float p = Mathf.Clamp(angles.y, pitch.x, pitch.y) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(y) * side * Mathf.Cos(p), Mathf.Sin(p), Mathf.Cos(y) * Mathf.Cos(p));
        }
    }

    /// <summary>สะโพก: มุม = (แกว่งไปหน้า, กางออก) วัดจากแนวดิ่ง — เท้าอยู่ใต้สะโพกเสมอจึงไม่กำกวม</summary>
    [Serializable]
    public class HipLimit : JointLimit
    {
        [Tooltip("แกว่ง: ลบ = ไปหลัง, บวก = ไปหน้า")]
        public Vector2 swing = new Vector2(-35f, 70f);
        [Tooltip("กาง: ลบ = หุบข้ามขาอีกข้าง, บวก = กางออก")]
        public Vector2 spread = new Vector2(-15f, 35f);

        public HipLimit() => maxBend = 120f;

        public override Vector2 Angles(float side, Vector3 dir, float yawRef = float.NaN)
        {
            float down = Mathf.Max(-dir.y, 1e-3f);
            return new Vector2(Mathf.Atan2(dir.z, down) * Mathf.Rad2Deg, Mathf.Atan2(dir.x * side, down) * Mathf.Rad2Deg);
        }

        public override Vector3 Direction(float side, Vector2 angles)
        {
            float sw = Mathf.Clamp(angles.x, swing.x, swing.y) * Mathf.Deg2Rad;
            float sp = Mathf.Clamp(angles.y, spread.x, spread.y) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Tan(sp) * side, -1f, Mathf.Tan(sw)).normalized;
        }
    }
}

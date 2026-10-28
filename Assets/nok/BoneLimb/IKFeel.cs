using System;
using UnityEngine;

namespace Nsc.BoneLimb
{
    /// <summary>
    /// ความหนักของหุ่น IK (ยกมาจากหุ่นฟิสิกส์ใน TestItem): ปล่อยเท้าแล้วกระทืบลง, เท้าหันตามก้าว, ตัวมีแรงเฉื่อย + ยวบตอนเท้าลง,
    /// เสียง/ฝุ่น/จอสั่นตอนเท้าลง และหยุดภาพ/ประกายไฟตอนหมัดโดน (เอฟเฟกต์จาก Nsc.Feel — ค่าอยู่ใน FeelConfig)
    /// ปิดทั้งหมดเป็นค่าเริ่มต้น = หุ่นทำงานแบบเดิมของ <see cref="RobotIKControl"/>
    /// </summary>
    [Serializable]
    public class IKFeel
    {
        [Header("ขา")]
        [Tooltip("ปล่อยคลิกแล้วเท้ากระทืบลงใช้เวลา (วินาที) เร่งแรงตอนท้าย | 0 = วางลงทันทีแบบเดิม — ความสูงยกเท้าคือ footLift")]
        public float stompTime;
        [Tooltip("หมุนตัวด้วยเท้าแบบหุ่นเดิม: ก้าวเฉียงแล้วเท้าหันตามครึ่งหนึ่งของมุมก้าว, Q/E = บิดเท้าที่คุมอยู่กับที่ — ตัวหันตามค่าเฉลี่ยของเท้า | ปิด = Q/E หมุนทั้งตัว")]
        public bool footTurn;
        [Tooltip("เท้าหันได้มากสุดต่อก้าว (องศา)")]
        public float maxTurnPerStep = 30f;
        [Tooltip("ตัวหันตามเท้าเร็วแค่ไหน — ต่ำ = ตัวหนัก หันตามช้า")]
        public float bodyTurnSpeed = 2f;

        [Header("น้ำหนักตัว")]
        [Tooltip("ความถี่สปริงของตัวหุ่นที่ไหลตามเท้า (Hz) — ต่ำ = หนัก ออกตัวช้า เลยนิดแล้วกลับ | 0 = ใช้ bodyFollow เดิม")]
        public float bodyFrequency;
        [Range(0.1f, 1f)] public float bodyDamping = 0.6f;
        [Tooltip("เท้าลงแล้วตัวยวบลง (ความเร็วลงตอนเท้ากระแทก)")]
        public float landingDip = 3f;
        [Tooltip("ลำตัวเอียงตามทิศที่ตัวเคลื่อน (องศาต่อ m/s) — เดินแล้วโน้มไปหน้า ถ่ายน้ำหนักข้างแล้วเอียงข้าง | 0 = ปิด")]
        public float leanPerSpeed;
        public float maxLean = 12f;

        [Header("เอฟเฟกต์ (FeelConfig)")]
        [Tooltip("เท้าลง: เสียง ฝุ่น จอสั่น")]
        public bool footstepFeel;
        [Tooltip("หมัดโดนของที่มี Rigidbody: หยุดภาพแวบหนึ่ง จอสั่น ประกายไฟ เสียง")]
        public bool hitFeel;
        [Tooltip("รัศมีรอบกำปั้นที่นับว่าโดน")]
        public float hitRadius = 1.2f;

        /// <summary>
        /// สปริง-แดมเปอร์แบบ implicit (ไม่ระเบิดแม้เฟรมตก): x ไหลไปหา goal ด้วยความถี่ freq (Hz)
        /// damping &lt; 1 = เลยเป้านิดแล้วกลับ ให้ความรู้สึกมีมวล
        /// </summary>
        public static void Spring(ref Vector3 x, ref Vector3 v, Vector3 goal, float freq, float damping, float dt)
        {
            float w = 2f * Mathf.PI * freq;
            float f = 1f + 2f * dt * damping * w;
            float hoo = dt * w * w;
            float detInv = 1f / (f + dt * hoo);
            Vector3 x0 = x;
            x = (f * x0 + dt * v + dt * hoo * goal) * detInv;
            v = (v + hoo * (goal - x0)) * detInv;
        }
    }
}

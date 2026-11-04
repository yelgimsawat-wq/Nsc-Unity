using UnityEngine;

namespace Nsc.BoneLimb
{
    /// <summary>
    /// ท่าฟัน 360° ของแขนหนึ่งข้าง (เครื่องที่คุมแขนเท่านั้น ไม่ซิงก์ — เครื่องอื่นเห็นผ่านท่ามือใน LimbNetState)
    /// มุมทั้งหมดเป็น (yaw°, สูง°) ในแกนตัวหุ่น | ตรรกะอยู่ใน RobotIKControl.Slash.cs
    /// </summary>
    internal sealed class SlashState
    {
        public int kind = -1;            // -1 = ไม่ได้ฟัน
        public float t;                  // เวลาตั้งแต่เริ่มท่า
        public bool striking;            // อยู่ในช่วงสะบัดจริง — รอยแสงขึ้นเฉพาะตอนนี้ ไม่ใช่ตอนง้าง/จัดท่า
        public float windup;             // เวลาง้างของท่านี้ — ใกล้ท่าเริ่มอยู่แล้ว (ฟาดกลับ) = ง้างสั้น
        public Vector2 from;             // ท่าเริ่ม (yaw, สูง)
        public float fromR;              // รัศมีมือตอนเริ่มท่า (ส่วนของแขน) — ง้างค่อยๆ งอศอกจากตรงนั้น

        // มุมแนวฟัน (0 = ไปขวา, 90 = ขึ้น), จุดเล็ง, ค้างชาร์จที่ยอด
        public float angle, holdT, charge;
        public Vector2 aim;
        public bool hold;

        // ลากระหว่างฟัน = จองท่าถัดไป ต่อทันทีที่ฟันจบ (ฟาดกลับไม่ต้องรอ)
        public int next = -1;
        public float nextAngle;
        public Vector2 nextAim;
        public bool nextHold;

        // อินพุตของการกดครั้งนี้: -1 = ยังไม่สั่งท่า, 0 = สั่งท่าที่กำลังง้าง (ลากต่อ = ปรับมุม), 1 = จองท่าถัดไปไว้
        public int dragKind;
        public Vector2 dragAcc, dragAim; // ระยะลากเมาส์สะสม / จุดเล็งตอนกด
        public int comboStep;
        public float comboTime;

        // ท่ามือ/ใบที่ท่าฟันสั่ง (AimWeapon อ่าน)
        public Vector3 blade, tangent;   // โลก: ทิศใบที่ต้องการ / ทิศแนวฟันตอนนี้ — ศูนย์ = ไม่ได้ฟัน
        public Vector3 palmGoal;         // ทิศฝ่ามือของการฟันครั้งนี้ (แกนตัวหุ่น) — คิดตอนเริ่มท่า/เปลี่ยนมุม แล้วค้างไว้
        public float poseWeight;         // กฎท่ามือมีผลแค่ไหน (0..1) — ค่อยๆ เข้าตอนเริ่ม ค่อยๆ ออกตอนดึงกลับ
        public float wideBlade;          // ท่ากวาดเข้าตัว: ข้อมือหันใบออกตามแนวแขนแค่ไหน (0..1)
        public float edgeLock = float.NaN; // ทิศคมที่ล็อกตอนเริ่มท่า (NaN = ยังไม่ล็อก) — หนึ่งการฟันไม่พลิกดาบ

        // เส้นนำ (เครื่องนี้เท่านั้น)
        public LineRenderer guide;
        public float bladeLen;           // จุดจับ → ปลายของที่ถือ (หน่วยโลก)
        public float bladeAngle;         // มุมใบจริงในระนาบวงฟัน เทียบแนวไหล่ → มือ (+ = ไปทางที่ฟัน)

        // [ตรวจมือ] ต่ำสุด/สูงสุดของท่านี้ — log ด้วย logSlashWrist
        public Vector2 rollRange, flexRange, radialRange, twistRange, edgeErr, bladeArm;
        public float rollJump;

        public bool Active => kind >= 0;

        /// <summary>จบท่า: ล้างทุกอย่างที่ AimWeapon อ่าน (ช่วงจองท่า/อินพุตไม่แตะ)</summary>
        public void End()
        {
            kind = -1;
            striking = false;
            poseWeight = 0f;
            wideBlade = 0f;
            edgeLock = float.NaN;
            blade = tangent = Vector3.zero;
        }

        public void ResetStats()
        {
            rollRange = flexRange = radialRange = twistRange = edgeErr = bladeArm = new Vector2(float.MaxValue, float.MinValue);
            rollJump = 0f;
        }
    }

    /// <summary>ข้อมือของแขนที่ถืออาวุธ (ทุกเครื่องคิดเอง จากเป้ามือที่ซิงก์) — ค่าเฟรมก่อนสำหรับกันกระตุก/พลิก</summary>
    internal sealed class WieldState
    {
        public Vector3 lastArmDir, armDirVel; // ทิศไหล่ → มือ (แกนตัวหุ่น) เฟรมก่อน / ความเร็ว (rad/วิ)
        public float swing01;                 // ความเร็วฟาดแบบนุ่ม 0..1 — ช้า = ใบตั้ง, เร็ว = ใบต่อแนวแขน / ลำตัวนำ
        public Vector3 swingDir;              // คลิกซ้ายค้าง (โหมดอิสระ): ทิศไหล่ → มือ ในแกนตัวหุ่น

        public float lastRoll = float.NaN;    // มุมคว่ำ/หงายหลังจำกัดเฟรมก่อน — NaN = เริ่มใหม่
        public float lastRollRaw = float.NaN; // มุมบิดแขนท่อนล่างเฟรมก่อน (ต่อเนื่อง ไม่พับที่ ±180)
        public Vector3 thumbNeutral;          // ท่าจับมือเฟรมล่าสุดที่ศอกงอพอให้วัดได้ (ใช้เมื่อยังไม่รู้ neutralSide)
        public float neutralSide;             // ท่าจับมืออยู่ฝั่งไหนของ (แกนบานพับศอก × แนวปลายแขน) — 0 = ยังไม่รู้

        public float edgeRoll;                // มุมบิดหน้าไม้รอบแกนของตอนนี้
        public float edgeLatch = float.NaN;   // มุมหน้าไม้ที่ล็อกตอนเริ่มฟาดเร็ว — NaN = ไม่ได้ฟาด
        public float edgeLatchT;              // ฟาดเร็วมานานเท่าไร
        public Vector3 lastAimTarget;         // เป้ามือเฟรมก่อน — ทิศที่ฟาด (บิดหน้าไม้)

        public Quaternion lastHandRot;        // ท่ามือถืออาวุธเฟรมก่อน (โลก) — กันกระตุก
        public bool handRotValid;

        /// <summary>ไม่ได้ถือ / คนอื่นคุม: ครั้งหน้าเริ่มนับใหม่ ไม่ต่อค่าเก่า</summary>
        public void Forget()
        {
            lastRoll = lastRollRaw = float.NaN;
            handRotValid = false;
        }
    }
}

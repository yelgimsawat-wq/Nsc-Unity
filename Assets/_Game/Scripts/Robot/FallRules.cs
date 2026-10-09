using System;
using UnityEngine;

namespace Nsc.Robots
{
    /// <summary>
    /// กฎการล้มของหุ่น — ลอจิกล้วน รับ BalanceInput ทีละ tick แล้วบอกว่าควรล้มไหมและเพราะอะไร
    /// ย้ายตัวเลขและลำดับของกฎมาจาก TorsoMovement.HandleFakeHoverAndPosture แบบเป๊ะๆ
    /// (กฎแรกที่ครบเงื่อนไขชนะ ตัวจับเวลาของกฎที่อยู่หลังจากนั้นไม่ขยับใน tick นั้น)
    ///
    /// ⭐ หลักการออกแบบที่ต้องคงไว้: "มีขายันอยู่ = ห้ามล้มจากระบบสมดุลเด็ดขาด"
    /// ความยากของเกมต้องมาจาก "สองคนต้องไม่ก้าวพร้อมกัน" ไม่ใช่จากการทรงตัวรายเฟรม
    /// </summary>
    [Serializable]
    public class FallRules
    {
        // สัดส่วนของ airborneGrace ที่ใช้เป็นเกณฑ์ "เอียงค้างนานเกิน"
        private const float TiltGraceRatio = 0.5f;
        // ตัวจับเวลาค่อยๆ ลดลงเร็วกว่าที่นับขึ้นสองเท่า — กระพริบสั้นๆ ไม่สะสมจนล้ม
        private const float TimerDecayRate = 2f;

        [Tooltip("เพดานแรงดึงสะสม (stress) — เกินแล้วล้ม ยกเว้นมือจับของนิ่งอยู่")]
        public float maxStress = 500f;

        [Tooltip("มุมเอียงสูงสุดตอนไม่มีอะไรพยุงเลย (คะมำกลางอากาศ)")]
        public float maxTiltAngle = 55f;

        [Tooltip("ลอยทั้งตัว (ไม่มีเท้าแตะพื้น) นานเกินนี้ = ล้ม | กฎเอียงใช้ครึ่งหนึ่งของค่านี้")]
        public float airborneGrace = 1.0f;

        [Tooltip("⭐ กติกาหลัก: ทั้งสองเท้าก้าวพร้อมกันนานกว่านี้ถึงจะล้ม\n" +
                 "กัน state ซ้อนกันชั่วขณะจากดีเลย์เครือข่าย — overlap แค่ 1 เฟรมห้ามล้มเด็ดขาด")]
        public float bothSteppingGrace = 0.15f;

        [Tooltip("ทั้งสองเท้าลอยและไม่สมดุลพร้อมกันนานกว่านี้ถึงจะล้ม")]
        public float bothOffBalanceGrace = 0.15f;

        [Tooltip("นับขาที่หลุดแล้วเป็นเท้าในกฎการล้มด้วยไหม\n" +
                 "true = พฤติกรรมเดิม (ขาที่หลุดนอนบนพื้นยังนับว่าแตะพื้น) — เปลี่ยนหลังทีมลองเล่นเทียบแล้ว")]
        public bool countDetachedLegs = true;

        private float tiltTimer;
        private float steppingTimer;
        private float offBalanceTimer;
        private float airborneTimer;

        /// <summary>มีอะไรกันล้มจากระบบสมดุลอยู่ไหม (มือจับ / กระโดด / เตะ / ขายัน)</summary>
        public static bool IsBalanceProtected(in BalanceInput input) =>
            input.handSupport || input.jumpProtected || input.kickInProgress || input.supportFoot;

        /// <summary>เรียกทุก physics tick ตอนหุ่นยืนอยู่ — คืน None ถ้ายังไม่ล้ม</summary>
        public FallReason Evaluate(in BalanceInput input, float dt)
        {
            if (!input.handSupport && input.stress >= maxStress)
                return FallReason.StressOverload;

            bool protectedBalance = IsBalanceProtected(input);

            // เช็กเอียงทำงานเฉพาะตอนไม่มีอะไรพยุงเลย (เช่นคะมำกลางอากาศ)
            if (!protectedBalance && input.groundedFeet == 0 && input.tiltAngle > maxTiltAngle)
            {
                tiltTimer += dt;
                if (tiltTimer >= airborneGrace * TiltGraceRatio)
                {
                    tiltTimer = 0f;
                    return FallReason.TiltedInAir;
                }
            }
            else Decay(ref tiltTimer, dt);

            // ⭐ กฎหลัก: สองขาก้าวพร้อมกัน — ไม่ผูกกับการแตะพื้นเลย
            // ขาที่ยันอยู่ไม่ได้ช่วยกันกฎนี้ (มีแค่มือจับกับการกระโดดที่กันได้)
            if (!input.handSupport && !input.jumpProtected &&
                input.footCount >= 2 && input.walkSteppingFeet >= 2)
            {
                steppingTimer += dt;
                if (steppingTimer >= bothSteppingGrace)
                {
                    steppingTimer = 0f;
                    return FallReason.BothFeetStepping;
                }
            }
            else Decay(ref steppingTimer, dt);

            // ต้อง "เท้าทั้งคู่ลอยจริง" ด้วย — กันธงที่ยังไม่ตรงกับฟิสิกส์ (กระโดดแต่เท้ายังไม่ลอย) หลอกล้ม
            if (!protectedBalance && input.footCount >= 2 &&
                input.balancedFeet == 0 && input.groundedFeet == 0)
            {
                offBalanceTimer += dt;
                if (offBalanceTimer >= bothOffBalanceGrace)
                {
                    offBalanceTimer = 0f;
                    return FallReason.BothFeetOffBalance;
                }
            }
            else Decay(ref offBalanceTimer, dt);

            // ตาข่ายรับสุดท้าย: ลอยทั้งตัวนานเกิน — ถ้าตัดทิ้งหุ่นจะไม่มีวันล้มจากการตกที่สูง
            // (จงใจไม่ล้างตัวจับเวลาตอนล้ม เหมือนของเดิม — ล้างตอนลุกขึ้นผ่าน Reset())
            if (!protectedBalance && input.footCount > 0 && input.groundedFeet == 0)
            {
                airborneTimer += dt;
                if (airborneTimer >= airborneGrace)
                    return FallReason.AirborneTooLong;
            }
            else Decay(ref airborneTimer, dt);

            return FallReason.None;
        }

        /// <summary>ล้างตัวจับเวลาทุกตัว — ตอนลุกขึ้นยืนและตอน respawn (กันลุกปุ๊บล้มซ้ำ)</summary>
        public void Reset()
        {
            tiltTimer = 0f;
            steppingTimer = 0f;
            offBalanceTimer = 0f;
            airborneTimer = 0f;
        }

        private static void Decay(ref float timer, float dt) =>
            timer = Mathf.Max(0f, timer - dt * TimerDecayRate);
    }
}

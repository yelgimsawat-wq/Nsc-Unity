using System.Collections.Generic;
using Nsc.Combat;
using Nsc.Feel;
using Nsc.Match;
using Nsc.Robots;
using NscGame.Enemy;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Nsc.BoneLimb
{
    // ท่าฟัน 360° (เครื่องที่คุมแขน) + ข้อมือ/ท่ามือถืออาวุธ (ทุกเครื่อง) — แยกจาก RobotIKControl.cs ให้อ่านง่าย สถานะต่อแขนอยู่ใน SlashState.cs
    public partial class RobotIKControl
    {
        // ================================================================ ท่าฟัน 360°
        // ท่าต้นแบบ 4 ทิศ (ผ่านผู้ตรวจมือแล้ว): (yaw°, สูง°) ต้น → ปลาย ในแกนตัวหุ่น | yaw + = ขวา
        // มุมอื่นสร้างจากท่าต้นแบบสองท่าที่ใกล้สุด (SlashLine) — มุมตรงท่าต้นแบบ = ท่าเดิมเป๊ะ
        private static readonly Vector2[][] SlashPath =
        {
            new[] { new Vector2(0f, 50f), new Vector2(0f, -55f) },     // 0 ฟันลง (ลากลง) — เงื้อข้างหัวระดับหู (Vom Tag) ไม่ใช่ชูตรงเหนือหัว (หลังมือหันหน้า ดูจับกลับด้าน) | จบระดับเอว/เข่า
            new[] { new Vector2(-70f, 20f), new Vector2(65f, -15f) },  // 1 กวาดไปขวา (ลากขวา) — เฉียงลง ไม่ใช่ระนาบแนวนอนเป๊ะ
            new[] { new Vector2(70f, 20f), new Vector2(-65f, -15f) },  // 2 กวาดไปซ้าย (ลากซ้าย)
            new[] { new Vector2(0f, -50f), new Vector2(0f, 85f) },     // 3 ฟันขึ้น (ลากขึ้น)
        };
        // จังหวะ ช้า–เร็ว–ช้า (Weapon-Motion-Research.md): ง้าง → หยุดที่ยอด (ค้างคลิก = ชาร์จ) → ฟัน (เร่ง t³) → เลยเป้าแล้วเด้งกลับ → ค้าง | ทุกช่วง × slashWeight
        private const float SlashWindup = 0.4f, SlashApex = 0.06f, SlashStrike = 0.125f, SlashSettle = 0.15f, SlashLinger = 0.1f;
        private const float SlashOvershoot = 0.12f;           // เลยเป้าไปอีกกี่ส่วนของวงฟัน
        private float ElbowCocked => slashCockedReach;   // รัศมีมือตอนง้าง (ส่วนของแขน) — ยิ่งน้อยศอกยิ่งพับ
        private const float ElbowExtended = 0.98f;
        private const float SlashRecover = 0.9f;      // ฟันจบแล้วดึงดาบกลับท่าพักนานเท่านี้ (วิ × slashWeight) — ช้ากว่าทุกช่วงของการฟัน (ผู้ใช้ 2026-11-01: ไม่ต้องรีบ)
        /// <summary>ระยะที่ "มือ" เอื้อมถึงตอนแขนเหยียดในท่าฟัน (วัดจากไหล่)</summary>
        private static float HandReach(IKLimb arm) => arm.Reach * ElbowExtended;
        /// <summary>
        /// ระยะที่ "ปลายของที่ถือ" แตะถึงตอนแขนเหยียด (วัดจากไหล่) — เพดานของการเล็ง/ช่วยเล็ง
        /// มือเปล่า (ไม่มีของ) = เท่ากับระยะมือ | ผู้ใช้ 2026-11-01: เคยใช้ระยะมืออย่างเดียว (3.97) สั้นกว่าตัวหุ่น ชี้อะไรก็ "ไกลเกิน" หมด ท่าฟันเลยไม่ไปที่จุดที่คลิก
        /// </summary>
        private float TipReach(int i, IKLimb arm) => HandReach(arm) + BladeLength(i, arm);
        private const float ChargeBackswing = 0.15f, ChargeStrikeSpeedup = 0.3f, ChargeAutoRelease = 1.5f;
        private float ApexTime => SlashApex * slashWeight;
        private float StrikeTime => SlashStrike * slashWeight;
        private float SettleTime => SlashSettle * slashWeight;
        private float LingerTime => SlashLinger * slashWeight;
        private float RecoverTime => SlashRecover * slashWeight;
        private float StrikeDuration(int i) => StrikeTime * (1f - ChargeStrikeSpeedup * slash[i].charge);

        private static float LineAngle(Vector2 from, Vector2 to) => Mathf.Repeat(Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg, 360f);
        /// <summary>มุมแนวฟันของท่าต้นแบบ (0 ลง, 1 ขวา, 2 ซ้าย, 3 ขึ้น) — คอมโบ/จอยยังสั่งเป็นทิศ</summary>
        private static float KindAngle(int kind) => LineAngle(SlashPath[kind][0], SlashPath[kind][1]);

        /// <summary>
        /// มุมไหนก็ได้ → จุดต้น/ปลายวงฟัน (yaw°, สูง°): กลางวงกับครึ่งความยาวไล่ระหว่างท่าต้นแบบสองท่าข้างมุมนั้น แนวตามมุมจริง
        /// + จุดเล็ง (เลื่อนทั้งวง) | ชาร์จ = ง้างลึกขึ้น (เฉพาะจุดต้น)
        /// </summary>
        private static void SlashLine(float angle, Vector2 aim, float charge, out Vector2 start, out Vector2 end)
        {
            int lo = 0, hi = 0;
            float dLo = 361f, dHi = -361f;
            for (int k = 0; k < SlashPath.Length; k++)
            {
                float d = Mathf.DeltaAngle(KindAngle(k), angle); // + = มุมนี้อยู่ทวนเข็มจากท่า k
                if (d >= 0f && d < dLo) { dLo = d; lo = k; }
                if (d <= 0f && d > dHi) { dHi = d; hi = k; }
            }
            float f = dLo - dHi > 1e-3f ? dLo / (dLo - dHi) : 0f;
            Vector2 c = Vector2.Lerp((SlashPath[lo][0] + SlashPath[lo][1]) * 0.5f, (SlashPath[hi][0] + SlashPath[hi][1]) * 0.5f, f) + aim;
            float h = Mathf.Lerp(Vector2.Distance(SlashPath[lo][0], SlashPath[lo][1]), Vector2.Distance(SlashPath[hi][0], SlashPath[hi][1]), f) * 0.5f;
            Vector2 u = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            start = c - u * (h * (1f + ChargeBackswing * charge));
            end = c + u * h;
            start.y = Mathf.Clamp(start.y, -75f, 88f); // ไม่ง้างทะลุขั้วบน/ล่าง (ทิศ yaw กลับด้านตรงนั้น)
            end.y = Mathf.Clamp(end.y, -75f, 88f);
        }

        /// <summary>
        /// ทิศที่ลากเมาส์ → ทิศฟันจริง: ปัดเข้าช่องตาม slashAngleSnap (45 = 8 ทิศ) แล้วเลี่ยงทิศที่ฟันไม่ได้
        /// ปัดเป็นช่องแล้วแต่ละทิศคงที่ทุกครั้ง ตรวจทีละทิศ/แก้ทีละทิศได้ ไม่ใช่มุมใหม่ทุกครั้งที่ลาก
        /// </summary>
        private float SlashAngleOf(float drag, float side)
        {
            if (slashAngleSnap <= 0f) return ComfortAngle(drag, side); // 0 = อิสระแบบเดิม (ใช้ช่วงห้ามปัดมุม)
            // ปัดเข้าช่องตรงๆ ไม่ใช้ slashBlockedArc — ช่วงห้ามมีไว้กันมุมแปลกๆ ของระบบมุมอิสระ
            // พอเหลือ 8 ทิศตายตัว ผู้ใช้ควรได้ครบทั้ง 8 แล้วค่อยไล่ดูทีละทิศว่าทิศไหนท่าไม่สวย (ผู้ใช้ 2026-11-02)
            return Mathf.Repeat(Mathf.Round(drag / slashAngleSnap) * slashAngleSnap, 360f);
        }

        /// <summary>มุมที่แขนนี้ฟันได้สบาย: ตกช่วงห้าม (slashBlockedArc ของแขนขวา, แขนซ้ายกระจก) = ปัดไปขอบที่ใกล้สุด</summary>
        private float ComfortAngle(float angle, float side)
        {
            float a = side < 0f ? 180f - angle : angle;
            float d = Mathf.DeltaAngle(slashBlockedArc.x, a);
            if (slashBlockedArc.y > 0f && Mathf.Abs(d) < slashBlockedArc.y)
                a = slashBlockedArc.x + (d >= 0f ? 1f : -1f) * slashBlockedArc.y;
            return Mathf.Repeat(side < 0f ? 180f - a : a, 360f);
        }

        // ลากเลือกแนวฟัน → ง้าง (ค้างคลิกที่ยอด = ชาร์จ) → ปล่อย = ฟัน → เลยเป้า → ค้าง → กลับท่าพัก
        // ลากใหม่ระหว่างฟัน = จองท่าถัดไป ต่อทันทีที่ฟันถึงปลาย (ฟาดกลับ ง้างสั้นตามระยะที่ต้องไป) | คลิกเฉยๆ = คอมโบ
        // คืน true ขณะกำลังฟัน (dir/rate = เป้าและความเร็วตามเป้าของเฟรมนี้)
        private bool SlashStep(int i, IKLimb arm, IKInput input, out Vector3 dir, out float rate, out float radius)
        {
            dir = default; rate = follow; radius = ElbowExtended;
            ReadSlashInput(i, arm, input);
            if (slash[i].kind < 0) { EndSlash(i); return false; }

            float w = slash[i].windup, strikeAt = w + ApexTime;
            slash[i].t += Time.deltaTime;
            if (slash[i].hold && slash[i].t >= strikeAt) // ค้างคลิกที่ยอด = ชาร์จ | ค้างนานเกิน = ฟันออกเอง
            {
                slash[i].holdT += Time.deltaTime;
                slash[i].charge = Mathf.Clamp01(slash[i].holdT / Mathf.Max(0.01f, slashChargeTime * slashWeight));
                if (slash[i].holdT >= (slashChargeTime + ChargeAutoRelease) * slashWeight) slash[i].hold = false;
                else slash[i].t = strikeAt - 1e-4f;
            }
            float strike = StrikeDuration(i), settleAt = strikeAt + strike;
            if (slash[i].next >= 0 && slash[i].t >= settleAt + SettleTime) // ต่อท่าหลังเลยเป้าแล้วเด้งกลับ (แขนเหยียดสุดให้เห็นก่อน) — ปลายท่านี้คือท่าง้างของท่าถัดไป
            {
                LogSlashWrist(i);
                StartSlash(i, arm, slash[i].nextAngle, slash[i].nextAim, slash[i].nextHold && input.held);
                slash[i].next = cueNext = -1;
                w = slash[i].windup; strikeAt = w + ApexTime; strike = StrikeDuration(i); settleAt = strikeAt + strike;
            }

            float t = slash[i].t, recover = 0f;
            SlashLine(slash[i].angle, slash[i].aim, slash[i].charge, out Vector2 start, out Vector2 end);
            ClampSlashLine(arm.Side, ref start, ref end);
            Vector2 over = end + (end - start) * SlashOvershoot, a;
            bool wide = WideSweep(start, end, arm.Side);
            float cocked = wide ? ElbowExtended : ElbowCocked; // กวาดเข้าตัว = ง้างแขนเหยียด ไม่พับศอก
            if (t < w)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / w);
                a = Vector2.Lerp(slash[i].from, start, k);
                radius = Mathf.Lerp(slash[i].fromR, cocked, k); // ง้าง = ดึงมือเข้า งอศอก
                rate = 14f / slashWeight;
                slash[i].wideBlade = wide ? k : 0f;
            }
            else if (t < strikeAt) { a = start; radius = cocked; rate = slash[i].hold ? 8f : 20f; slash[i].wideBlade = wide ? 1f : 0f; } // หยุดโหลดที่ยอด (ชาร์จ = ค่อยๆ ง้างลึกตามเส้น)
            else if (t < settleAt)
            {
                float k = (t - strikeAt) / strike;
                a = Vector2.Lerp(start, over, k * k * k); // เร่งแบบ t³ — เร็วสุดตอนถึงเป้า ไม่ใช่ตอนเริ่ม
                radius = Mathf.Lerp(cocked, ElbowExtended, Mathf.Sqrt(k)); // ศอกเหยียดตั้งแต่ต้นวงฟัน — ดาบยื่นไปถึงเป้า
                slash[i].wideBlade = wide ? 1f - Mathf.SmoothStep(0f, 1f, k * 2f) : 0f; // ครึ่งแรกของวง ข้อมือปล่อยใบจากแนวแขนเข้าท่าฟันปกติ
                rate = 60f; // ตามเป้าเกือบทันที ไม่งั้นความหน่วงกินช่วงระเบิดแรง
            }
            else if (t < settleAt + SettleTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, (t - settleAt) / SettleTime);
                a = Vector2.Lerp(over, end, k); // เลยเป้าแล้วเด้งกลับ
                radius = Mathf.Lerp(ElbowExtended, 0.95f, k);
                rate = 25f / slashWeight;
            }
            else if (t < settleAt + SettleTime + LingerTime) { a = end; radius = 0.95f; rate = 25f / slashWeight; }
            else if (t < settleAt + SettleTime + LingerTime + RecoverTime)
            {
                // ฟันจบแล้ว "ค่อยๆ" ดึงดาบกลับท่าพัก ไม่รีบ (ผู้ใช้ 2026-11-01) — เดิมปล่อยให้ DriveWeaponArm ดีดกลับที่ 8/slashWeight
                // ลาก/คลิกใหม่ระหว่างนี้ = จองท่าถัดไป แล้วออกท่าทันที (ตัวเช็ค slashNext ด้านบนผ่านตั้งแต่จบช่วงเด้งกลับ)
                recover = Mathf.SmoothStep(0f, 1f, (t - (settleAt + SettleTime + LingerTime)) / RecoverTime);
                a = Vector2.Lerp(end, RestAngles(arm.Side), recover);
                radius = Mathf.Lerp(0.95f, WeaponRestRadius, recover);
                rate = 5f / slashWeight;
            }
            else { LogSlashWrist(i); EndSlash(i); return false; }
            if (t >= settleAt) slash[i].wideBlade = 0f;

            // กฎท่ามือ: ค่อยๆ มีผลตอนต้นท่า และค่อยๆ หมดผลตอนดึงกลับ — ขอบคมคือที่มาของการกระตุกหัว/ท้าย
            slash[i].poseWeight = (1f - recover) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / (0.25f * slashWeight)));
            slash[i].striking = t >= strikeAt && t < settleAt; // ช่วงสะบัดเท่านั้น (ง้าง/ชาร์จ/เด้งกลับ = ไม่ใช่การฟัน)
            DrawGuide(i, arm, start, over, t < strikeAt, cocked);
            dir = SlashDir(a);
            AimBlade(i, arm, start, end, a, t, w, strikeAt, settleAt, recover);
            return true;
        }

        private static Vector3 SlashDir(Vector2 a)
        {
            float yaw = a.x * Mathf.Deg2Rad, el = a.y * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(yaw) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(yaw) * Mathf.Cos(el));
        }

        // เส้นที่วาด = ทางของ "ปลายดาบ" (ผู้ใช้ 2026-10-30: เดิมเส้นคุมมือ ดาบตั้งขึ้นเพราะเดาทิศใบจากความเร็วแขน ฟันไม่ถึงศัตรู)
        // ใบชี้ออกตามแนวไหล่ → มือ ปลายดาบจึงวิ่งบนเส้นที่ระยะ แขน + ดาบ — มุมของวงฟันมาจาก "หัวไหล่" พาแขนไปตามเส้น
        // "ฟัน" ต่างจาก "ฟาด" ตรงที่ใบต้องหมุนเทียบแขนระหว่างวง (ผู้ใช้ 2026-11-01: ฟันลงแล้วดาบไม่หมุน เป็นฟาด)
        // ง้าง = ใบค้างไปข้างหลังตามเส้น ~55° → สะบัดผ่านเป้าจนใบนำไปข้างหน้า ~63° = ใบพลิกราว 118° ต่อหนึ่งการฟัน (แรงเหวี่ยงดึงปลายออก)
        // เคยลดเหลือ ±24° + ล็อกใบติดแขน แล้วดาบกับแขนกลายเป็นท่อนเดียว = ฟาด | ข้อมือกันไว้ที่ ClampWrist/LimitForearmRoll ตามเดิม
        /// <summary>
        /// แกนของวงฟันที่จุด a: radial = ไหล่ → มือ, tangent = ทิศที่ดาบกำลังวิ่งตามเส้น (ตั้งฉากกับ radial, อยู่ในระนาบวงฟัน)
        /// ใบดาบอยู่ในระนาบของสองแกนนี้เสมอ — ดาบจึงขนานไปกับแนวที่ฟัน ไม่แทงออกข้างระนาบ
        /// </summary>
        private bool SlashFrame(Vector2 a, Vector2 along, out Vector3 radial, out Vector3 tangent)
        {
            radial = transform.rotation * SlashDir(a);
            tangent = Vector3.ProjectOnPlane(transform.rotation * (SlashDir(a + along) - SlashDir(a - along)), radial);
            if (tangent.sqrMagnitude < 1e-8f) return false;
            tangent.Normalize();
            return true;
        }

        /// <summary>
        /// ทิศใบเมื่อสะบัดไปแล้ว tk (0 = ต้นวงฟัน, 1 = ผ่านเป้า): ค้างไปข้างหลังตามเส้น → สะบัดจนขนานกับเส้นพอดีตอนผ่านเป้า
        /// ใบหมุนเทียบแขนราว 145° ต่อการฟันหนึ่งครั้ง (นี่คือสิ่งที่ทำให้อ่านเป็น "ฟัน" ไม่ใช่ "ฟาด")
        /// </summary>
        private static Vector3 SlashBladeDir(Vector3 radial, Vector3 tangent, float tk) =>
            Vector3.Slerp((radial * 0.7f - tangent).normalized, tangent, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(tk)));

        private const float SlashBladeWhip = 1.6f; // ใบสะบัดเร็วกว่าแขนเท่านี้ — ขนานกับเส้นก่อนถึงปลายวงเล็กน้อย

        private void AimBlade(int i, IKLimb arm, Vector2 start, Vector2 end, Vector2 a, float t, float w, float strikeAt, float settleAt, float recover)
        {
            Vector2 along = (end - start).normalized;
            if (!SlashFrame(a, along, out Vector3 radial, out Vector3 tangent)) return;
            Vector3 cocked = SlashBladeDir(radial, tangent, 0f);
            Vector3 blade = t < w ? Vector3.Slerp(radial, cocked, Mathf.Clamp01(t / Mathf.Max(1e-4f, w) * 1.5f))  // ง้าง: ค่อยๆ ค้างใบไปข้างหลังตามเส้น
                          : t < strikeAt ? cocked
                          : t < settleAt ? SlashBladeDir(radial, tangent, (t - strikeAt) / Mathf.Max(1e-4f, settleAt - strikeAt) * SlashBladeWhip) // สะบัดจนขนานกับเส้น
                          : tangent;                                                                              // เลยเป้าแล้วใบยังนอนไปตามเส้น
            // ช่วงดึงกลับ: ใบไล่ไปหาท่าพัก | แนวฟันยังเก็บไว้ ให้ slashPoseWeight เป็นตัวจางอิทธิพลของกฎท่ามือแทน
            // (เคยล้างเป็นศูนย์ตรงนี้ → กฎท่ามือดับทันทีในเฟรมเดียว มือสะดุด — ผู้ใช้ 2026-11-02 "หลังฟันกระตุก")
            slash[i].blade = recover > 0f ? Vector3.Slerp(blade, WeaponRestBlade(arm.Side), recover) : blade;
            slash[i].tangent = tangent;
        }

        private void EndSlash(int i)
        {
            slash[i].End();
            HideGuide(i);
        }

        // คลิกซ้ายค้าง + ลากเกิน slashDragPixels = เริ่มท่า (มุมตามทิศที่ลากรวมตั้งแต่กด ลากต่อ = เล็งมุมใหม่ได้ระหว่างง้าง) ปล่อย = ฟัน
        // จุดเล็ง = ที่เคอร์เซอร์ชี้ตอนกด (ลากแล้วเคอร์เซอร์ขยับ) | คลิกเฉยๆ / จอย X,R1 = คอมโบ | จอยดีดสติ๊กขวา = ทิศนั้นทันที
        // dragKind: -1 = การกดครั้งนี้ยังไม่สั่งท่า, 0 = สั่งท่าที่กำลังง้าง (ลากต่อ = ปรับมุม), 1 = จองท่าถัดไปไว้
        private void ReadSlashInput(int i, IKLimb arm, IKInput input)
        {
            if (input.pressed)
            {
                slash[i].dragKind = -1;
                slash[i].dragAcc = Vector2.zero;
                slash[i].dragAim = CursorAim(i, arm, input.ray);
            }
            bool tap = input.attack || (input.released && slash[i].dragKind < 0 && slash[i].dragAcc.magnitude < slashDragPixels);
            if (input.padSlash >= 0) IssueSlash(i, arm, KindAngle(input.padSlash), AssistAim(i, arm), false);
            else if (tap) IssueSlash(i, arm, KindAngle(NextCombo(i)), AssistAim(i, arm), false);
            else if (input.held)
            {
                slash[i].dragAcc += input.lookDelta;
                if (slash[i].dragAcc.magnitude >= slashDragPixels)
                {
                    float angle = SlashAngleOf(Mathf.Atan2(slash[i].dragAcc.y, slash[i].dragAcc.x) * Mathf.Rad2Deg, arm.Side);
                    if (slash[i].dragKind < 0) slash[i].dragKind = IssueSlash(i, arm, angle, slash[i].dragAim, true) ? 0 : 1;
                    else if (slash[i].dragKind == 0 && slash[i].kind >= 0 && slash[i].hold && angle != slash[i].angle) // ยังไม่ปล่อย = เล็งมุมใหม่ได้
                    {
                        slash[i].angle = angle; cueAngle = angle;
                        // ท่ามือตามมุมใหม่ด้วย — เดิมค้างของมุมแรก ลากเฉียงแล้วช่องเปลี่ยน (0°→45°) ข้อมือต้องการผิดฝั่ง ~150° ชนเพดานจนส่าย (วัด 2026-11-02)
                        slash[i].palmGoal = SlashPalm(angle, slash[i].aim, arm.Side);
                    }
                    else if (slash[i].dragKind == 1 && slash[i].next >= 0) { slash[i].nextAngle = angle; cueNextAngle = angle; }
                }
            }
            if (!input.held)
            {
                slash[i].dragAcc = Vector2.zero;
                slash[i].hold = false; // ปล่อยคลิก = ฟันออก
                slash[i].nextHold = false;
            }
        }

        // ไม่มีท่าค้าง = เริ่มเลย (คืน true) | กำลังฟันอยู่ = จองเป็นท่าถัดไป (คืน false)
        private bool IssueSlash(int i, IKLimb arm, float angle, Vector2 aim, bool hold)
        {
            if (slash[i].kind < 0) { StartSlash(i, arm, angle, aim, hold); return true; }
            if (slash[i].next >= 0) return false; // จองไว้แล้วไม่ทับ
            slash[i].next = 0;
            slash[i].nextAngle = angle;
            slash[i].nextAim = aim;
            slash[i].nextHold = hold;
            cueNext = 0;
            cueNextAngle = angle;
            return false;
        }

        // คอมโบปุ่มเดียว: กวาดขวา → กวาดซ้าย (ฟาดกลับ) → ฟันลง | เว้นเกิน 1.2 วิ × slashWeight = เริ่มใหม่
        private static readonly int[] Combo = { 1, 2, 0 };
        private int NextCombo(int i)
        {
            if (Time.time - slash[i].comboTime > 1.2f * slashWeight) slash[i].comboStep = 0;
            slash[i].comboTime = Time.time;
            return Combo[slash[i].comboStep++ % Combo.Length];
        }

        private void StartSlash(int i, IKLimb arm, float angle, Vector2 aim, bool hold)
        {
            slash[i].kind = 0;
            slash[i].angle = angle;
            slash[i].aim = aim;
            slash[i].hold = hold;
            slash[i].holdT = 0f;
            slash[i].charge = 0f;
            cueKind = 0; // ลูกศรบน HUD ทันทีที่รับคำสั่ง — ผู้เล่นรู้ว่ากดติดก่อนท่าง้างจบ
            cueAngle = angle;
            cueTime = Time.time;
            slash[i].t = 0f;
            slash[i].edgeLock = float.NaN; // เริ่มท่าใหม่ = ล็อกทิศคมใหม่ (ต่อคอมโบก็นับเป็นคนละการฟัน)
            slash[i].palmGoal = SlashPalm(angle, aim, arm.Side);
            wield[i].lastRollRaw = float.NaN;   // ค่ามุมบิดแบบต่อเนื่องเริ่มนับใหม่ทุกท่า — ไม่งั้นสะสมข้ามการฟันจนไหลไปหลายร้อยองศา มือค้างติดขอบเพดานถาวร
            slash[i].ResetStats();
            Vector3 now = Quaternion.Inverse(transform.rotation) * (arm.target.position - arm.root.position).normalized;
            slash[i].from = new Vector2(Mathf.Atan2(now.x, now.z) * Mathf.Rad2Deg, Mathf.Asin(Mathf.Clamp(now.y, -1f, 1f)) * Mathf.Rad2Deg);
            slash[i].fromR = Vector3.Distance(arm.target.position, arm.root.position) / arm.Reach;
            slash[i].bladeLen = BladeLength(i, arm);
            SlashLine(angle, aim, 0f, out Vector2 start, out _);
            float far = Vector2.Distance(slash[i].from, start);
            slash[i].windup = Mathf.Lerp(0.08f, SlashWindup, Mathf.Clamp01(far / 120f)) * slashWeight; // ฟาดกลับ (ปลายท่าเก่า ≈ ต้นท่าใหม่) ง้างแทบไม่ต้อง
        }

        // ท่ามือของการฟันครั้งนี้: คิดจาก "กลางวงฟัน" ครั้งเดียวต่อมุม แล้วค้างไว้ทั้งท่า
        // (คิดใหม่ทุกเฟรมแล้วเป้าหมุนตามวง มือพลิกไปมาตลอดทาง — ผู้ใช้ 2026-11-02 เห็นชัดสุดที่ท่าเฉียงขึ้น)
        private Vector3 SlashPalm(float angle, Vector2 aim, float side)
        {
            SlashLine(angle, aim, 0f, out Vector2 ps, out Vector2 pe);
            Vector3 palm = SlashPlaneNormal(ps, pe, out Vector3 ptan);
            if (palm == Vector3.zero) return Vector3.down;
            // ฟันแนวนอน (เอียงไม่เกิน 30°) = คว่ำมือทั้งกวาดซ้ายและขวา (ผู้ใช้ 2026-11-03) — เดิมกวาดซ้ายได้หงายมือ
            // ฟันขึ้น = ท่ามือเดียวกับฟันลงในแนวเดียวกัน (ใช้คมหลัง) — ไม่กลับด้าน ไม่งั้นฝ่ามือต้องพลิก 180° เกินเพดานคว่ำ/หงาย ข้อมือค้างขอบแล้วพลิกไปมา (ผู้ใช้ 2026-11-02)
            if (Mathf.Abs(ptan.y) < 0.5f ? palm.y > 0f : ptan.y > 0.1f) palm = -palm;
            // ระนาบจริงใช้วงหลังตัดขอบ (ClampSlashLine) — เล็งซ้ายบนแล้ววงถูกตัดจนคนละระนาบ ฝ่ามือเกือบขนานแขน ข้อมือสะบัดข้ามฝั่ง (คลิปผู้ใช้ 2026-11-03)
            // ฝั่งของฝ่ามือยังเลือกจากวงก่อนตัด (ทิศที่ผู้เล่นลาก) ไม่งั้นวงที่ถูกตัดจนแบนจะสลับไปใช้กฎแนวนอน ฝ่ามือกลับด้าน
            ClampSlashLine(side, ref ps, ref pe);
            Vector3 real = SlashPlaneNormal(ps, pe, out _);
            if (real == Vector3.zero) return palm;
            return Vector3.Dot(real, palm) >= 0f ? real : -real;
        }

        // ทิศตั้งฉากกับระนาบวงฟันที่กลางวง (แกนตัวหุ่น) | ศูนย์ = หาไม่ได้
        private static Vector3 SlashPlaneNormal(Vector2 ps, Vector2 pe, out Vector3 tangent)
        {
            Vector2 pmid = (ps + pe) * 0.5f, palong = (pe - ps).normalized;
            Vector3 pradial = SlashDir(pmid);
            tangent = Vector3.ProjectOnPlane(SlashDir(pmid + palong) - SlashDir(pmid - palong), pradial);
            if (tangent.sqrMagnitude < 1e-8f) return Vector3.zero;
            tangent.Normalize();
            return Vector3.Cross(tangent, pradial).normalized;
        }

        // ไม่ง้างข้ามลำตัวไกลเกิน — แขนขวาเอื้อมไปซ้ายบนไกลๆ แล้วศอกชูอัดข้างหัว (ผู้ใช้ 2026-11-02)
        // มีผลเฉพาะตอนง้างสูง: ง้างต่ำ (ท่ากวาดข้าง) เอื้อมข้ามตัวได้เต็มที่เหมือนเดิม ไม่งั้นวงกวาดข้างหดไปด้วย
        // + เพดานความสูงของวงฟัน — มือขึ้นเหนือไหล่มากไปจะไปเบียดข้างหัว ท่าอัดและมือบิด (ผู้ใช้ 2026-11-02)
        private void ClampSlashLine(float side, ref Vector2 start, ref Vector2 end)
        {
            float acrossMax = Mathf.Lerp(90f, slashReachAcross, Mathf.InverseLerp(10f, 45f, start.y));
            if (side > 0f) start.x = Mathf.Max(start.x, -acrossMax);
            else start.x = Mathf.Min(start.x, acrossMax);
            start.y = Mathf.Min(start.y, slashTopAngle);
            end.y = Mathf.Min(end.y, slashTopAngle);
            // กวาดแนวนอนเข้าหาตัว = ง้างกางแขนออกข้างจนสุด (ผู้ใช้ 2026-11-03) — เดิมเริ่มที่ 70° ศอกพับ
            if (WideSweep(start, end, side)) start.x = side * Mathf.Max(start.x * side, SlashWideYaw);
        }

        private const float SlashWideYaw = 95f; // ง้างท่ากวาดเข้าตัว: แขนกางออกข้างกี่องศาจากหน้าตรง (90 = ตั้งฉากกับลำตัว)
        /// <summary>วงฟันนี้กวาดแนวนอน (เอียงไม่เกิน 30°) จากฝั่งแขนตัวเองเข้าหาตัวไหม</summary>
        private static bool WideSweep(Vector2 start, Vector2 end, float side)
        {
            Vector2 d = end - start;
            return Mathf.Abs(d.y) < Mathf.Abs(d.x) * 0.58f && d.x * side < 0f;
        }

        // จุดเล็งจากเคอร์เซอร์ (yaw°, สูง°) เทียบไหล่: ชี้โดนของ (ไม่ใช่ตัวเอง) ในระยะที่ปลายดาบแตะถึง = ฟันผ่านตรงนั้น | ไม่โดน/ไกลเกิน = จุดบนระนาบหน้าไหล่ที่ระยะนั้น
        private Vector2 CursorAim(int i, IKLimb arm, Ray ray)
        {
            Vector3 shoulder = arm.root.position, p;
            float reach = TipReach(i, arm) * Mathf.Max(1f, slashAssistRange); // เล็งได้ไกลสุดเท่าที่ดาบแตะถึง
            if (AimHit(ray, out RaycastHit hit) && (hit.point - shoulder).magnitude <= reach) p = hit.point;
            else
            {
                var plane = new Plane(transform.forward, shoulder + transform.forward * reach);
                p = plane.Raycast(ray, out float enter) ? ray.GetPoint(enter) : shoulder + transform.forward * reach;
            }
            Vector3 local = Quaternion.Inverse(transform.rotation) * (p - shoulder);
            float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float el = Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
            if (Mathf.Abs(yaw) > 100f) return AssistAim(i, arm); // ชี้ไปข้างหลังตัว = เล็งไม่ได้ ใช้ศัตรูใกล้สุดแทน
            return new Vector2(Mathf.Clamp(yaw, -slashAimRange.x, slashAimRange.x), Mathf.Clamp(el, -slashAimRange.y, slashAimRange.y));
        }

        // จุดแรกตามเคอร์เซอร์ที่ไม่ใช่ตัวเอง/ของในมือ (ศัตรู กล่อง พื้น)
        private bool AimHit(Ray ray, out RaycastHit best)
        {
            best = default;
            float nearest = float.MaxValue;
            int n = Physics.RaycastNonAlloc(ray, walkHits, 1000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < n; k++)
            {
                Collider c = walkHits[k].collider;
                if (walkHits[k].distance >= nearest || c.transform.IsChildOf(transform) || (robot && Robot.FromCollider(c) == robot)) continue;
                Rigidbody body = c.attachedRigidbody;
                if (body && System.Array.IndexOf(heldBody, body) >= 0) continue;
                best = walkHits[k];
                nearest = best.distance;
            }
            return nearest < float.MaxValue;
        }

        // ClosestPoint ใช้กับ MeshCollider ที่ไม่ convex ไม่ได้ (Unity เตือนทุกครั้ง เช่นพื้น/ตัวไคจู) — ใช้กล่องรอบแทน
        private static Vector3 Closest(Collider c, Vector3 p) => c is MeshCollider { convex: false } ? c.bounds.ClosestPoint(p) : c.ClosestPoint(p);

        private Vector2 AssistAim(int i, IKLimb arm) => new Vector2(SlashAssist(i, arm), 0f);

        // จุดจับ → ปลายฝั่งไกลของของที่ถือ (ด้ามอยู่ปลายหนึ่ง ปลายดาบอีกปลาย ≈ สองเท่าของจุดจับ → กลางของ)
        private float BladeLength(int i, IKLimb arm)
        {
            Rigidbody body = heldBody[i];
            Collider col = body ? body.GetComponentInChildren<Collider>() : null;
            if (!col) return 0f;
            Transform gp = GripPointOf(body, arm.GripCenter);
            Vector3 grip = gp ? gp.position : arm.GripCenter;
            // ปลายจริง = มุม bounds ที่ไกลสุดตามแนวใบ ไม่ใช่ระยะถึงกึ่งกลาง×2
            // (×2 ถูกเฉพาะตอนจุดจับอยู่ที่ก้นด้ามพอดี — ของจริงจับที่ 10% ของเล่ม วัด 2026-11-01 ขาดไป 0.80 ม. ทุกท่า)
            Vector3 dir = (col.bounds.center - grip).normalized;
            if (dir.sqrMagnitude < 0.5f) return Vector3.Distance(grip, col.bounds.center) * 2f;
            Vector3 c = col.bounds.center, e = col.bounds.extents;
            float far = 0f;
            for (int s = 0; s < 8; s++)
            {
                Vector3 corner = c + new Vector3((s & 1) == 0 ? -e.x : e.x, (s & 2) == 0 ? -e.y : e.y, (s & 4) == 0 ? -e.z : e.z);
                far = Mathf.Max(far, Vector3.Dot(corner - grip, dir));
            }
            return far;
        }

        // เส้นนำแนวฟัน (เฉพาะคนคุม): ระหว่างลาก/ง้าง = เส้นเต็ม | ฟันออกแล้ว = จางหาย
        private void DrawGuide(int i, IKLimb arm, Vector2 start, Vector2 over, bool aiming, float cocked)
        {
            if (!slashPreview || !IsMine(i)) return;
            LineRenderer line = slash[i].guide;
            if (!line)
            {
                var go = new GameObject($"{arm.name}_SlashGuide");
                line = slash[i].guide = go.AddComponent<LineRenderer>();
                line.material = weaponTrail ? weaponTrail : Resources.Load<Material>("WeaponTrail");
                line.positionCount = 24;
                line.useWorldSpace = true;
                line.numCapVertices = 4;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
            }
            float alpha = aiming ? 0.8f : Mathf.Max(0f, line.endColor.a - Time.deltaTime * 4f);
            line.enabled = alpha > 0.01f;
            if (!line.enabled) return;
            float width = arm.Reach * 0.05f;
            line.startWidth = width * 0.4f;
            line.endWidth = width;
            line.startColor = new Color(0.4f, 0.9f, 1f, alpha * 0.3f);
            line.endColor = new Color(0.4f, 0.9f, 1f, alpha);
            // ทางของ "ปลายดาบ" จริง = มือบนวงฟัน + ใบยื่นไปตามทิศใบที่จุดนั้น — ใช้สูตรเดียวกับ AimBlade
            // (เดิมวาดที่รัศมีคงที่ แขน + ความยาวดาบ ซึ่งถูกเฉพาะตอนใบต่อแนวแขนพอดี ไม่ตรงกับท่าจริงสักจังหวะ)
            // เดินตามเวลา: มุมของวงใช้ t³ (SlashStep) ระยะมือใช้ √t ส่วนใบใช้ SlashBladeWhip
            Vector2 alongLine = (over - start).normalized;
            for (int k = 0; k < line.positionCount; k++)
            {
                float tk = k / (line.positionCount - 1f);
                Vector2 a = Vector2.Lerp(start, over, tk * tk * tk);
                Vector3 hand = arm.root.position + transform.rotation * SlashDir(a) * (arm.Reach * Mathf.Lerp(cocked, ElbowExtended, Mathf.Sqrt(tk)));
                Vector3 tip = hand;
                // แขนคุมท่า: ข้อมือไม่หมุนใบตาม SlashBladeDir (ทำไม่ได้ในเพดานข้อมือ) — ใช้มุมใบจริงของมือตอนนี้แทน
                // วัด 2026-11-03: สูตรเดิมทำให้ปลายดาบจริงห่างเส้น 0.2–0.36 เท่าความยาวดาบ (ใบจริงค้าง ~50–80° จากแนวแขน ไม่ได้นอนตามแนวฟัน)
                if (SlashFrame(a, alongLine, out Vector3 radial, out Vector3 tangent))
                    tip += (slashArmDrives ? Quaternion.AngleAxis(slash[i].bladeAngle, Vector3.Cross(radial, tangent)) * radial
                                           : SlashBladeDir(radial, tangent, tk * SlashBladeWhip)) * slash[i].bladeLen;
                line.SetPosition(k, tip);
            }
        }

        private void HideGuide(int i)
        {
            if (slash != null && slash[i].guide) slash[i].guide.enabled = false;
        }

#if UNITY_EDITOR
        /// <summary>
        /// [ตรวจมือ — Editor เล่นคนเดียว] ใส่ของใส่มือแขนนี้ แล้วสั่งฟันทุก step° (ไม่ปัดมุมห้าม) — log มุมบิดแขนท่อนล่าง/ข้อมือที่ท่าต้องการ ทีละท่า
        /// เรียกจาก Play mode: StartCoroutine(DebugSlashSweep(0, ดาบ)) | ใช้ตั้ง edgeRollFollow / slashBlockedArc
        /// </summary>
        public System.Collections.IEnumerator DebugSlashSweep(int arm, Rigidbody weapon, float step = 15f)
        {
            IKLimb limb = limbs[arm];
            bool log = logSlashWrist;
            logSlashWrist = true;
            selected = arm;
            limb.Grab = true;
            if (heldBody[arm] == null)
            {
                weapon.transform.position = limb.GripCenter;
                Physics.SyncTransforms();
                grabWant[arm] = weapon; // ถืออยู่แล้วห้ามตั้ง — ค้างไว้แล้ว DriveWeaponArm ไม่ทำงาน (รอเอื้อม)
            }
            for (float t = 0f; heldBody[arm] == null && t < 3f; t += Time.deltaTime) yield return null;
            if (heldBody[arm] == null) { Debug.LogWarning("[SlashWrist] หยิบของไม่ได้"); logSlashWrist = log; yield break; }
            yield return new WaitForSeconds(0.5f);
            for (float angle = 0f; angle < 360f; angle += step)
            {
                while (slash[arm].kind >= 0) yield return null;
                StartSlash(arm, limb, angle, Vector2.zero, false);
                yield return null;
                while (slash[arm].kind >= 0) yield return null;
                yield return new WaitForSeconds(0.3f);
            }
            logSlashWrist = log;
            Debug.Log("[SlashWrist] ไล่ครบทุกมุมแล้ว");
        }
#endif

        // [ตรวจมือ] จบท่า → log มุมแขนท่อนล่าง/ข้อมือ ต่ำสุด-สูงสุดของท่านี้ (เปิด logSlashWrist)
        private void LogSlashWrist(int i)
        {
            if (!logSlashWrist || slash[i].flexRange.x > slash[i].flexRange.y) return;
            string roll = slash[i].rollRange.x > slash[i].rollRange.y ? "-" : $"{slash[i].rollRange.x:0}..{slash[i].rollRange.y:0}°";
            string twist = slash[i].twistRange.x > slash[i].twistRange.y ? "-" : $"{slash[i].twistRange.x:0}..{slash[i].twistRange.y:0}°";
            Debug.Log($"[SlashWrist] {limbs[i].name} มุม {slash[i].angle:0}° ชาร์จ {slash[i].charge:0.00} | คว่ำ/หงายที่ท่าต้องการ {twist} (จำกัด ±{forearmRollLimit:0}) | หน้าดาบบิด {roll} " +
                      $"| บิดเร็วสุด {slash[i].rollJump:0}°/เฟรม | งอข้อมือ {slash[i].flexRange.x:0}..{slash[i].flexRange.y:0}° | เอียง {slash[i].radialRange.x:0}..{slash[i].radialRange.y:0}° " +
                      $"| **คมเบี้ยวตอนสะบัด {(slash[i].edgeErr.x > slash[i].edgeErr.y ? "-" : $"{slash[i].edgeErr.x:0}..{slash[i].edgeErr.y:0}°")}** (0 = ฟัน, 90 = ฟาด) " +
                      $"| **ใบเทียบแนวฟัน {(slash[i].bladeArm.x > slash[i].bladeArm.y ? "-" : $"{slash[i].bladeArm.x:0}..{slash[i].bladeArm.y:0}°")}** (0 = ดาบขนานแนวที่ฟัน)");
        }

        // ศัตรู/เป้าที่ตีได้ ใกล้สุดด้านหน้า "ในระยะที่ปลายดาบแตะถึง" → มุม yaw (จำกัด ±50°) ให้ท่าฟันหันเข้าหา | ไม่มี = 0
        private float SlashAssist(int index, IKLimb arm)
        {
            if (slashAssistRange <= 0f) return 0f;
            Team mine = robot ? robot.GetTeam() : Team.None;
            int n = Physics.OverlapSphereNonAlloc(arm.root.position, TipReach(index, arm) * slashAssistRange, hitBuffer, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue, bias = 0f;
            for (int k = 0; k < n; k++)
            {
                Collider c = hitBuffer[k];
                if (Robot.FromCollider(c) == robot || c.transform.IsChildOf(transform)) continue;
                IDamageable target = c.GetComponentInParent<IDamageable>();
                if (target == null || !mine.IsHostileTo(target.GetTeam())) continue;
                Vector3 to = transform.InverseTransformPoint(c.bounds.center);
                float angle = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                float score = new Vector2(to.x, to.z).sqrMagnitude;
                if (Mathf.Abs(angle) > 80f || score >= best) continue;
                best = score;
                bias = Mathf.Clamp(angle, -50f, 50f);
            }
            return bias;
        }

        // มือที่ไม่มีใครคุม + อีกมือถืออาวุธที่มีจุดจับ → จับด้ามใต้มือหลัก (ฝั่งปลายด้าม ห่างหนึ่งความกว้างมือ) แบบถือดาบสองมือ
        // ทุกเครื่องคิดเอง (คิดจากมือหลักที่ซิงก์) — ภาพอย่างเดียว ของยังตามมือหลัก | คืน false = ไม่มีอะไรให้จับ ใช้ท่าการ์ดตามเดิม
        private bool SupportGrip(int i)
        {
            supportHolding[i] = false;
            IKLimb arm = limbs[i];
            for (int j = 0; j < limbs.Length; j++)
            {
                IKLimb main = limbs[j];
                if (j == i || !main.isArm || !main.Grab || main.Reaching || !main.Holding) continue;
                Transform gp = GripPointOf(main.Holding.attachedRigidbody, main.GripCenter);
                if (!gp) continue;
                Vector3 blade = (main.Holding.bounds.center - gp.position).normalized;
                float handLen = Vector3.Distance(main.tip.position, main.GripCenter) * 1.6f;
                Vector3 hold = main.GripCenter - blade * handLen; // ใต้มือหลัก ฝั่งปลายด้าม
                Vector3 goal = hold + (arm.tip.position - arm.GripCenter); // เป้าเป็นข้อมือ: ชดเชยระยะข้อมือ→กลางกำมือ
                bool reach = Vector3.Distance(arm.root.position, hold) <= arm.Reach * 0.9f;
                // เอื้อมไม่ถึง (ด้ามอยู่ฝั่งตรงข้ามตัว) = แขนว่างห้อยผ่อนข้างลำตัว ไม่ชูการ์ดหมัดข้างหัว (ผู้ตรวจ: ดูเป็นถือไม้ข้างหนึ่ง ต่อยมวยอีกข้าง)
                if (!reach) goal = arm.root.position + transform.rotation * (new Vector3(arm.Side * 0.2f, -0.85f, 0.35f).normalized * (arm.Reach * 0.8f));
                arm.target.position = Vector3.Lerp(arm.target.position, goal, 1f - Mathf.Exp(-follow * 1.5f * Time.deltaTime));
                arm.OrientHand(transform.rotation, arm.tip.position - arm.mid.position);
                arm.Grip = Mathf.MoveTowards(arm.Grip, reach ? 1f : 0.5f, gripSpeed * Time.deltaTime); // ห้อยผ่อน = มือกำครึ่งเดียว
                supportHolding[i] = reach;
                return true;
            }
            return false;
        }

        // ถืออาวุธ: ลำตัว **นำ** แขน (ทุกเครื่องคิดเองจากเป้ามือที่ซิงก์) — มองทิศที่มือ "กำลังจะไป" (ทิศตอนนี้ + ความเร็ว × เวลานำ)
        // ง้างไปหลัง = บิดง้างก่อนมือถึง, ฟาด = สะโพก/ลำตัวหมุนไปก่อนแขน | สับลงต่ำหน้า = โน้มหน้า, ยกเหนือหัว = เอนหลัง | ฟาดเร็ว = ตัวพุ่งเข้าหาการฟัน
        private Vector3 WeaponPose()
        {
            float yaw = 0f, pitch = 0f, lunge = 0f, crouch = 0f;
            for (int i = 0; i < limbs.Length; i++)
            {
                IKLimb limb = limbs[i];
                if (!limb.isArm || !limb.Grab || limb.Reaching) continue;
                // ทิศแขนวัดจากไหล่ "ท่าพัก" (แกนตัวหุ่น) ไม่ใช่ไหล่จริง — ไหล่จริงขยับตามลำตัวที่ฟังก์ชันนี้เป็นคนบิด = วงป้อนกลับ
                // แขนชี้ลงแล้ววงนี้ไวมาก ลำตัวบิด ±5°/เฟรม ไหล่กระโดด 10% ของแขน ศอกเหยียด/งอสลับ มือโยก (วัด 2026-11-03 ตอนจบฟันลง)
                Vector3 d = (transform.InverseTransformPoint(limb.target.position) - arms[i].shoulder).normalized;
                d = (d + wield[i].armDirVel * (0.1f * slashWeight)).normalized; // นำแขน ~0.1 วิ × ความหนัก
                lunge = Mathf.Max(lunge, wield[i].swing01 * Mathf.Clamp01(d.z + 0.3f) * limb.Reach * 0.06f);
                crouch = Mathf.Max(crouch, wield[i].swing01 * limb.Reach * 0.12f + Mathf.Clamp01(-d.y) * wield[i].swing01 * limb.Reach * 0.08f); // ฟาดเร็ว/สับลงต่ำ = ย่อมากขึ้น
                float raise = Mathf.InverseLerp(8f, 45f, Vector3.Angle(d, WeaponRestDir(limb.Side)));
                float flat = Mathf.Clamp01(new Vector2(d.x, d.z).magnitude * 2f); // มือเหนือหัวพอดี = ทิศซ้าย/ขวาไม่แน่นอน ไม่บิด (ไม่งั้นลำตัวสะบัดกลับด้าน)
                yaw += Mathf.Clamp(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -weaponTwist, weaponTwist) * raise * flat;
                pitch += -d.y * (d.y < 0f ? weaponLean : weaponLean * 0.5f) * raise + wield[i].swing01 * weaponLean * 0.4f; // ฟาดเร็ว = เอนเข้าหาการฟัน
            }
            weaponCrouchGoal = crouch;
            return new Vector3(yaw, pitch, lunge);
        }

        // รอยแสงปลายอาวุธ (ทุกเครื่องสร้างเอง ไม่ซิงก์): ติดที่ปลายของ (ด้านไกลจากมือ) — เปิดเฉพาะตอนปลายเร็วกว่าเกณฑ์ต่อย 3 เท่า
        private void UpdateTrail(int i, IKLimb limb)
        {
            Collider held = limb.Holding;
            TrailRenderer t = trails[i];
            if (t && (!limb.Grab || (held && t.transform.parent != held.transform))) { Destroy(t.gameObject); t = trails[i] = null; }
            Material trailMat = weaponTrail ? weaponTrail : defaultTrail ? defaultTrail : defaultTrail = Resources.Load<Material>("WeaponTrail"); // ไม่ได้ตั้งในฉาก = ค่าเริ่มต้นจาก Resources
            if (!held || !trailMat) { if (t) t.emitting = false; return; }
            if (!t)
            {
                Vector3 g = limb.GripCenter, c = held.bounds.center;
                Transform gp = GripPointOf(held.attachedRigidbody, g);
                Vector3 far = c + (c - (gp ? gp.position : Closest(held, g))); // ปลายฝั่งตรงข้ามจุดจับ กลางแนวของ (มือเยื้องจากแกนด้าม ใช้มือตรงๆ รอยเยื้องออกนอกของ)
                t = trails[i] = new GameObject("WeaponTrail").AddComponent<TrailRenderer>();
                t.transform.SetParent(held.transform, true);
                t.transform.position = far;
                t.sharedMaterial = trailMat;
                t.time = 0.25f;
                t.minVertexDistance = 0.2f;
                t.widthMultiplier = Vector3.Distance(g, far) * 0.5f;
                t.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                var fade = new Gradient();
                fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.85f, 0.5f), 1f) },
                             new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
                t.colorGradient = fade; // ขาวสว่างที่ปลาย จางเป็นส้มโปร่งใสตามหลัง
                t.emitting = false;
                lastTrailTip[i] = far;
                swingSoundAt[i] = Time.time; // เพิ่งหยิบ — ของวาร์ปเข้ามือ ความเร็วกระโดด ไม่ใช่การฟาด
                swingHit[i] = true;          // ไม่มีปะทะจนกว่าจะวืดจริง
            }
            Vector3 p = t.transform.position;
            float speed = (p - lastTrailTip[i]).magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
            // อยู่ในท่าฟัน = มีรอยเฉพาะช่วงสะบัด (ตอนง้าง/ชาร์จ/พลิกมือจัดท่า ปลายดาบก็เร็วพอจะติดเกณฑ์ แต่ไม่ใช่การฟัน)
            // ไม่ได้อยู่ในท่าฟัน (เหวี่ยงมือเอง/คอมโบ) = ใช้เกณฑ์ความเร็วเดิม
            bool striking = slash[i].kind >= 0 ? slash[i].striking : true;
            t.emitting = striking && speed > strikeMinSpeed * 3f;
            lastTrailTip[i] = p;
            // วืด + ปะทะเฉพาะช่วงฟาดจริง (ปลายเร็วเกิน 6 เท่าเกณฑ์ต่อย — วัด 2026-10-29: ง้าง ≤3, ฟาด 8–19) หนึ่งวืดต่อ 0.4 วิ ปะทะได้ครั้งเดียวในช่วงนั้น
            bool fast = speed > strikeMinSpeed * 6f;
            if (fast && Time.time - swingSoundAt[i] > 0.4f)
            {
                swingSoundAt[i] = Time.time;
                swingHit[i] = false;
                ImpactFeel.PlaySwing(p, Mathf.InverseLerp(strikeMinSpeed * 6f, strikeMinSpeed * 15f, speed));
            }
            if (t.emitting && !swingHit[i] && Time.time - swingSoundAt[i] < 0.4f) WeaponContact(i, limb, p, speed);
        }

        // ใบโดนของที่มี Rigidbody (ไม่ใช่ตัวเอง/ของที่ถือ/พื้น) ระหว่างฟาด → เสียงเหล็ก ประกายไฟ จอสั่น (ทุกเครื่องเล่นเอง — ดาเมจจริงอยู่ที่ ServerStrikes)
        private void WeaponContact(int i, IKLimb limb, Vector3 tip, float speed)
        {
            int n = Physics.OverlapCapsuleNonAlloc(limb.GripCenter, tip, strikeRadius * 0.5f, hitBuffer, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < n; k++)
            {
                Collider c = hitBuffer[k];
                Rigidbody rb = c.attachedRigidbody;
                // ของที่ถือเช็คจาก Holding (ทุกเครื่องมี) ไม่ใช่ heldBody (มีแค่ Server) — ไม่งั้น client ได้ยินใบโดนตัวเองทุกครั้งที่ฟาด
                if (rb == null || c.transform.IsChildOf(transform) || System.Array.Exists(limbs, l => l.Holding && l.Holding.attachedRigidbody == rb)) continue;
                swingHit[i] = true;
                Vector3 point = Closest(c, tip);
                Vector3 normal = tip - point;
                ImpactFeel.PlayHit(point, normal.sqrMagnitude > 1e-6f ? normal.normalized : -transform.forward, Mathf.Lerp(15f, 45f, Mathf.InverseLerp(strikeMinSpeed * 6f, strikeMinSpeed * 15f, speed)));
                return;
            }
        }

        // รอยแสงติดอยู่บนของที่ถือ (ไม่ใช่ลูกของหุ่น) — หุ่นล้ม/ชิ้นหลุด/หุ่นหายแล้ว UpdateTrail ไม่ได้รันต่อ ต้องลบเอง ไม่งั้นรอยค้างวาดตามของ
        private void ClearTrail(int i)
        {
            if (trails == null || !trails[i]) return;
            Destroy(trails[i].gameObject);
            trails[i] = null;
        }

        // จุดจับของชิ้นนี้: ลูกชื่อ GripPoint | ไม่มี + เป็นกล่องยาว (ไม้/ดาบ ยาว ≥ 2.5 เท่าด้านอื่น) = สร้างให้เองที่ปลายฝั่งใกล้ near (15% จากปลาย) แกน X ตามความยาว
        // ทุกเครื่องสร้างเองตอนเจอ ไม่ต้องตั้งในฉาก | ของไม่ยาว (กล่อง) = null ใช้วิธีจับผิวแบบเดิม
        private static Transform GripPointOf(Rigidbody rb, Vector3 near)
        {
            Transform gp = rb.transform.Find("GripPoint");
            if (gp || !rb.TryGetComponent(out BoxCollider box)) return gp;
            Vector3 size = Vector3.Scale(box.size, rb.transform.lossyScale);
            int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            if (size[axis] < Mathf.Max(size[(axis + 1) % 3], size[(axis + 2) % 3]) * 2.5f) return null;
            Vector3 along = Vector3.zero;
            along[axis] = 1f;
            float side = Mathf.Sign(Vector3.Dot(rb.transform.InverseTransformPoint(near) - box.center, along));
            gp = new GameObject("GripPoint").transform;
            gp.SetParent(rb.transform, false);
            gp.localPosition = box.center + along * (side * box.size[axis] * 0.35f);
            gp.localRotation = axis == 0 ? Quaternion.identity : axis == 1 ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(0f, -90f, 0f);
            return gp;
        }

        // ตัวของ (ทิศจากจุดจับ ในแกนจุดจับ) อยู่ฝั่งนิ้วก้อย = ต้องจับกลับด้าน — แบบคนถือไม้ ตัวไม้ออกทางนิ้วโป้ง จุดจับเดียวใช้ได้ทั้งมือซ้าย/ขวา
        private static bool GripFlipped(IKLimb limb, Vector3 bodyInPoint) => Vector3.Dot(bodyInPoint, limb.ThumbInHand) < 0f;

        // ด้ามพาดเฉียงในอุ้งมือ (ใบเอียงไปทางนิ้ว) เหมือนคนจับดาบ
        // 0 = ด้ามตั้งฉากกับทิศนิ้ว (ใบยื่นออกข้างกำปั้น เหมือนถือค้อน) | 90 = ด้ามไปทางเดียวกับนิ้ว (ใบต่อแนวแขน ข้อมือตรง)
        // ผู้ใช้ 2026-11-01: ค่า 38 เดิมทำให้ใบเบนจากแขน 50-90° ตลอด → ปลายดาบไม่เคยอยู่บนเส้นที่วาด และท่าที่คมนำก็เกินช่วงข้อมือ
        // (คอมเมนต์เดิมบันทึกไว้เองว่า "ใบจะต่อแนวแขนได้ต้องหักข้อมือ ~110° คนจริงได้ ~30–40°" — แก้ที่ท่าจับแทนการฝืนข้อมือ)
        [Tooltip("ด้ามพาดเฉียงในอุ้งมือกี่องศา: 0 = ตั้งฉากกับนิ้ว (ถือเหมือนค้อน ใบยื่นออกข้างกำปั้น) | 90 = ด้ามไปทางเดียวกับนิ้ว (ใบต่อแนวแขน ข้อมือตรง) — มีผลกับของยาวทุกชิ้นที่หยิบ")]
        [SerializeField, Range(0f, 90f)] private float gripDiagonal = 0f;

        // ท่าจุดจับเทียบแกนมือ: จับกลับด้านถ้าตัวของอยู่ฝั่งนิ้วก้อย แล้วเอียงด้ามเข้าหานิ้ว — TryGrab กับ AimWeapon ต้องใช้ตัวเดียวกัน
        private Quaternion GripInHand(IKLimb limb, Vector3 bodyInPoint)
        {
            Quaternion flip = GripFlip(limb, bodyInPoint);
            return Quaternion.AngleAxis(GripTilt(flip * bodyInPoint), Vector3.up) * flip;
        }

        // มุมเอียงของ gripDiagonal (รอบแกนฝ่ามือ) แบ่งสองส่วน: ข้อมือเอียงไม่เกิน GripWristTilt (AimWeapon) ที่เหลือด้ามพาดเฉียงในกำปั้น — ทิศใบเท่าเดิม
        // ผู้ใช้ 2026-11-02: ด้ามเฉียงในกำปั้นดูไม่เหมือนจับดาบ → ย้ายไปข้อมือทั้งหมด | 2026-11-03: ข้อมือหักค้าง 38° ดูเบี้ยว → แบ่งกัน
        private const float GripWristTilt = 15f;
        private float GripTilt(Vector3 bar) => -Mathf.Sign(bar.x) * gripDiagonal;
        private static float WristTilt(float tilt) => Mathf.Clamp(tilt, -GripWristTilt, GripWristTilt);
        private static Quaternion GripFlip(IKLimb limb, Vector3 bodyInPoint) =>
            GripFlipped(limb, bodyInPoint) ? Quaternion.AngleAxis(180f, Vector3.up) : Quaternion.identity;
        // ท่าด้ามในกำปั้นตอนหยิบจริง = เฉียงทั้งหมด ลบส่วนที่ข้อมือรับไป
        private Quaternion GripInFist(IKLimb limb, Vector3 bodyInPoint)
        {
            Quaternion flip = GripFlip(limb, bodyInPoint);
            float tilt = GripTilt(flip * bodyInPoint);
            return Quaternion.AngleAxis(tilt - WristTilt(tilt), Vector3.up) * flip;
        }

        // ข้อมือตอนถือของ (ทุกเครื่องคิดเองจากเป้ามือที่ซิงก์): ห้อย = ปลายอาวุธชี้ลงหน้า (ถือไม้พักข้างตัว) | ยกแขนขึ้น = อาวุธชี้ไปทางเดียวกับแขน
        // บิดข้อมือรอบแนวแขนก่อน (ธรรมชาติ) แล้วงอที่เหลือไม่เกิน weaponWristBend — ทิศอาวุธในมือมาจากจุดจับ ไม่ใช่ตัวของ (ของแกว่งตามช้ากว่ามือ อ่านจากตัวของแล้วข้อมือส่าย)
        private const float EdgeSettleTime = 0.03f; // ฟาดเร็วได้เท่านี้ (× slashWeight) แล้วค่อยล็อกหน้าไม้
        private const float EdgeRollSpeed = 900f;   // หน้าไม้บิดเร็วสุด (°/วิ) — เดิม 1800 หมุนได้ครึ่งรอบใน 0.1 วิ (ข้อมือคนบิดแรงสุด ~700–900°/วิ)
        private void AimWeapon(IKLimb limb)
        {
            int i = System.Array.IndexOf(limbs, limb);
            // ความเร็วฟาด (ทุกเครื่องคิดเอง) — ลำตัว (WeaponPose) กับมุมใบใช้ค่าเดียวกัน | คิดก่อนออกจากฟังก์ชัน (ถือกล่องไม่มีจุดจับก็ต้องอัปเดต ไม่งั้นลำตัวบิดค้างค่าเก่า)
            // เพดาน 8 rad/วิ — หยิบของใหม่ ทิศเก่าค้างจากรอบก่อน ความเร็วกระโดดแวบเดียว ลำตัวสะบัด
            Vector3 dBody = (transform.InverseTransformPoint(limb.target.position) - arms[i].shoulder).normalized; // วัดจากไหล่ท่าพัก ไม่ใช่ไหล่จริง — ดู WeaponPose
            if (Time.deltaTime > 0f && wield[i].lastArmDir != Vector3.zero)
                wield[i].armDirVel = Vector3.Lerp(wield[i].armDirVel, Vector3.ClampMagnitude((dBody - wield[i].lastArmDir) / Time.deltaTime, 8f), 1f - Mathf.Exp(-25f * Time.deltaTime));
            wield[i].lastArmDir = dBody;
            wield[i].swing01 = Mathf.Lerp(wield[i].swing01, Mathf.InverseLerp(1f, 5f, wield[i].armDirVel.magnitude), 1f - Mathf.Exp(-20f * Time.deltaTime));
            // คนอื่นคุมแขนนี้ = ท่ามือมาจาก LimbNetState แล้ว — ท่าฟันอยู่บนเครื่องเขาเท่านั้น เดาเองตรงนี้จะได้คนละท่า (ดาบ/ดาเมจบน Server เพี้ยน)
            if (Online && HasController(i) && !IsMine(i)) { wield[i].Forget(); return; }
            Vector3 along = limb.tip.position - limb.mid.position;
            limb.OrientHand(transform.rotation, along); // ข้อมือตรงแนวแขน
            Vector3 armDir = (limb.target.position - limb.root.position).normalized;
            if (!limb.Holding) { wield[i].Forget(); return; }
            Transform gp = GripPointOf(limb.Holding.attachedRigidbody, limb.GripCenter);
            if (!gp) return; // ไม่มีจุดจับ (เช่นกล่อง) = ข้อมือตรงแนวแขนตาม OrientHand
            Vector3 inHand = gp.InverseTransformDirection(limb.Holding.bounds.center - gp.position);
            if (inHand.sqrMagnitude < 1e-6f) return;
            float tilt = WristTilt(GripTilt(GripFlip(limb, inHand) * inHand));
            inHand = GripInHand(limb, inHand) * inHand; // คิดท่าแบบด้ามพาดเฉียงเหมือนเดิม แล้วย้ายความเฉียงไปที่ข้อมือก่อนจบ (ทิศใบไม่เปลี่ยน)
            float raise = Mathf.InverseLerp(8f, 45f, Vector3.Angle(armDir, transform.rotation * WeaponRestDir(limb.Side)));
            Vector3 restWant = WeaponRestBlade(limb.Side); // Pflug: ใบชี้ขึ้นไปข้างหน้า ~45°
            // ช้า/ง้าง = ใบตั้งขึ้นจากแขน (~90°, อ่านเป็นการถือดาบ) | ฟาดเร็ว = ใบต่อแนวแขน (แรงเหวี่ยงดึงใบออก) — ใบต่อแนวแขนตลอด = ดูเหมือนชี้นิ้ว
            Vector3 cocked = Vector3.ProjectOnPlane(transform.up - transform.forward * 0.3f, armDir);
            cocked = cocked.sqrMagnitude > 1e-4f ? cocked.normalized : -transform.forward;
            Vector3 swingWant = Vector3.Slerp(cocked, (armDir + transform.forward * 0.2f).normalized, wield[i].swing01);
            Vector3 want = Vector3.Slerp(restWant, swingWant, raise);
            // ท่าฟัน 360° (เครื่องที่คุมแขน) บอกทิศใบเอง — ปลายดาบวิ่งตามเส้นที่วาด
            // ผู้ใช้ 2026-11-01 รอบสอง: เคยบีบใบให้อยู่ในกรวยรอบแขนท่อนล่าง (slashBladeBend 25°) เพื่อกันข้อมือพลิก
            // → ใบเลยไม่หมุนเทียบแขนเลย ดาบกับแขนเป็นท่อนเดียวแกว่งลงมา = ฟาด ไม่ใช่ฟัน | ข้อมือมี ClampWrist/LimitForearmRoll คุมอยู่แล้ว
            Vector3 idleWant = want; // ทิศใบตอนไม่ได้ฟัน — ช่วงเข้า/ออกท่าฟันผสมกับค่านี้ ไม่ให้ข้อมือลอยไปที่อื่นก่อน
            if (slash[i].blade != Vector3.zero) want = slash[i].blade;
            Quaternion rot = limb.target.rotation;
            Vector3 now = limb.HandFrame(rot) * inHand;
            bool armDrives = slashArmDrives && slash[i].kind >= 0;
            if (armDrives)
            {
                // ช่วงเข้า/ออกท่า (slashPoseWeight < 1) บิดรอบแนวแขนเข้าหาท่าถือปกติตามสัดส่วน — วัด 2026-11-02 ฟันเฉียงขึ้น 45°:
                // ไม่มีส่วนนี้ ช่วงดึงกลับข้อมือลอยไปตามแขนถึง +88° แล้วพอจบท่า ท่าถือปกติดึงกลับ -11° = มือโยกไปกลับ ~250°
                float idle = 1f - slash[i].poseWeight;
                Vector3 ia = Vector3.ProjectOnPlane(now, along), ib = Vector3.ProjectOnPlane(idleWant, along);
                if (idle > 1e-3f && ia.sqrMagnitude > 1e-6f && ib.sqrMagnitude > 1e-6f)
                {
                    rot = Quaternion.AngleAxis(Vector3.SignedAngle(ia, ib, along) * idle, along) * rot;
                    limb.target.rotation = rot;
                }
                // ท่ากวาดเข้าตัว: ตอนง้างข้อมือหันใบออกไปทางเดียวกับแขนที่กาง (ผู้ใช้ 2026-11-03) — ClampWrist ด้านล่างจำกัดเท่าที่ข้อมือหักได้
                if (slash[i].wideBlade > 0.001f)
                {
                    Vector3 bladeNow = (limb.HandFrame(rot) * inHand).normalized;
                    Vector3 aim = Vector3.Slerp(bladeNow, armDir, slash[i].wideBlade);
                    rot = Quaternion.FromToRotation(bladeNow, aim) * rot;
                    limb.target.rotation = rot;
                }
                // ไหล่ + ศอกคุมท่า: ไม่สั่งทิศใบ ปล่อยข้อมือตรงแนวแขนตามที่ OrientHand ตั้งไว้ด้านบน
                // แล้ว LimitForearmRoll ด้านล่างบิดให้ฝ่ามือคว่ำ — ทิศใบจึงมาจากท่าแขนเป็นหลัก
                // + ข้อมือ "ช่วย" หันใบเข้าหาทิศใบของเส้นนำ (slashBlade — สูตรเดียวกับ DrawGuide) ได้ไม่เกิน slashWristAssist (ผู้ใช้ 2026-11-02)
                // ต่างจากของเดิมตรงที่เดิมสั่งให้ข้อมือไปให้ถึงทิศที่ต้องการเสมอ แล้วค่อยโดนเพดานตัด — อันนี้ขอแค่เท่าที่ช่วยได้
                // 2026-11-03: เดิมหันเข้าหาทิศที่ดาบวิ่ง (slashTangent) ซึ่งไม่ใช่ทิศที่เส้นนำวาดไว้ — วัดแล้วฟันลงห่างเส้นมากขึ้น 0.29→0.46 เท่าดาบ
                if (slashWristAssist > 0f && slash[i].blade != Vector3.zero)
                {
                    Vector3 bladeNow = (limb.HandFrame(rot) * inHand).normalized;
                    Vector3 aim = Vector3.RotateTowards(bladeNow, slash[i].blade.normalized, slashWristAssist * Mathf.Deg2Rad, 0f);
                    if (Vector3.Angle(bladeNow, aim) > 0.5f)
                    {
                        rot = Quaternion.FromToRotation(bladeNow, aim) * rot;
                        limb.target.rotation = rot;
                    }
                }
            }
            else if (slash[i].blade != Vector3.zero)
            {
                // ท่าฟัน: ท่ามือมาจาก "ท่าดาบ" โดยตรง ไม่ใช่ไล่หมุนมือเข้าหา (วัด 2026-11-01: ไม่มีอะไรแย่ลง กระตุกท่า 90° ดีขึ้น 1511→1222)
                // ดาบยาวกว่าแขน 1.6 เท่า — ทิศเพี้ยน 1° = ปลายเพี้ยน 11 ซม. การไล่หมุนแบบจำกัดองศาให้ความแม่นระดับนี้ไม่ได้
                rot = Quaternion.FromToRotation(now, want) * rot;
                // บิดรอบแกนด้าม (ไม่เปลี่ยนทิศใบ) ให้แนวนิ้วใกล้แนวแขนท่อนล่างที่สุด = ข้อมือตรงที่สุดเท่าที่ศอก/ไหล่พาไปได้
                // ผู้ใช้ 2026-11-01: เคยลองเปลี่ยนเป้าเป็น "แนวที่ดาบวิ่ง" (มือตั้ง + คมนำ) → ข้อมือหมุนรัว เพราะเป้าหมุนตลอดวงฟัน ถอยกลับ
                Vector3 hilt = (limb.HandFrame(rot) * inHand).normalized;
                Vector3 fingersNow = Vector3.ProjectOnPlane(limb.HandFrame(rot) * Vector3.forward, hilt);
                Vector3 foreWant = Vector3.ProjectOnPlane(along, hilt);
                if (fingersNow.sqrMagnitude > 1e-6f && foreWant.sqrMagnitude > 1e-6f)
                    rot = Quaternion.AngleAxis(Vector3.SignedAngle(fingersNow, foreWant, hilt), hilt) * rot;
                limb.target.rotation = rot;
            }
            else
            {
                Vector3 a = Vector3.ProjectOnPlane(now, along), b = Vector3.ProjectOnPlane(want, along);
                if (a.sqrMagnitude > 1e-6f && b.sqrMagnitude > 1e-6f) rot = Quaternion.AngleAxis(Vector3.SignedAngle(a, b, along), along) * rot;
                limb.target.rotation = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(limb.HandFrame(rot) * inHand, want), weaponWristBend) * rot;
            }

            // หน้าไม้ (ฝั่งสันหมัด = ทิศนิ้ว) หันไปทางที่ฟาด — ฟาดกลับ = บิดข้อมือรอบแกนของ พลิกหน้าไม้เอง (ทุกเครื่องคิดจากเป้ามือที่ซิงก์)
            Vector3 axis = limb.HandFrame(limb.target.rotation) * inHand.normalized;
            Vector3 sweep = Vector3.ProjectOnPlane(limb.target.position - wield[i].lastAimTarget, axis) / Mathf.Max(Time.deltaTime, 1e-4f);
            wield[i].lastAimTarget = limb.target.position;
            // เฉพาะช่วงฟาดเร็ว — ช่วงเงื้อ มือวิ่งขึ้น/ไปหลัง ถ้าหันหน้าไม้ตามด้วย ข้อมือพลิกกลับด้าน (ผู้ใช้: "ตรงนี้พลิกมือให้หน่อย" ตอนเงื้อเหนือหัว)
            if (slash[i].tangent != Vector3.zero)
            {
                // ท่าฟัน 360°: **หนึ่งการฟันไม่พลิกดาบ** (ผู้ใช้ 2026-11-01)
                // ทิศที่ดาบวิ่ง (slashTangent) หมุนไปเรื่อยตลอดวง ถ้าไล่คมตามมันทุกเฟรม ใบจะค่อยๆ พลิกทั้งท่า
                // จึงคำนวณครั้งเดียวตอนเริ่มท่า แล้วล็อกค้างไว้จนจบ — ใบเอียงเท่าไหร่ตอนเริ่ม ก็เท่านั้นจนจบ
                if (float.IsNaN(slash[i].edgeLock))
                {
                    Vector3 edge = Vector3.ProjectOnPlane(limb.HandFrame(limb.target.rotation) * Vector3.forward, axis);
                    Vector3 cut = Vector3.ProjectOnPlane(slash[i].tangent, axis);
                    if (edge.sqrMagnitude > 1e-6f && cut.sqrMagnitude > 1e-6f)
                    {
                        // ดาบสองคม: คมฝั่งไหนใกล้ท่ามือตอนนี้ใช้ฝั่งนั้น ไม่ต้องหมุนครึ่งรอบไปหาคมอีกฝั่ง
                        float face = Vector3.SignedAngle(edge, cut, axis);
                        float other = face > 0f ? face - 180f : face + 180f;
                        if (Mathf.Abs(Mathf.DeltaAngle(wield[i].edgeRoll, other)) < Mathf.Abs(Mathf.DeltaAngle(wield[i].edgeRoll, face))) face = other;
                        slash[i].edgeLock = Mathf.Clamp(face, -SlashEdgeTurn, SlashEdgeTurn);
                    }
                }
                // เปิดคว่ำมือ = ให้ LimitForearmRoll เป็นคนเดียวที่สั่งมุมหมุนรอบแกนนี้ ตัวล็อกคมหยุดทำงาน
                // (ผู้ใช้ 2026-11-02 "มือยังขยับแปลก" — สองตัวสั่งแกนเดียวกันคนละทิศ แล้วมีตัวหน่วงคั่น มือเลยส่าย)
                float edgeWant = slashHandPose != HandPose.ปิด ? 0f : slash[i].edgeLock;
                if (!float.IsNaN(edgeWant))
                    wield[i].edgeRoll = Mathf.MoveTowardsAngle(wield[i].edgeRoll, edgeWant, EdgeRollSpeed * 0.5f * Time.deltaTime);
                wield[i].edgeLatch = float.NaN;
            }
            else if (raise > 0.5f && wield[i].swing01 > 0.6f && sweep.magnitude > limb.Reach * 2f)
            {
                Vector3 edge = Vector3.ProjectOnPlane(limb.HandFrame(limb.target.rotation) * Vector3.forward, axis);
                // ล็อกหน้าไม้ตอนเริ่มฟาด แล้วตามทิศมือได้อีกไม่เกิน edgeRollFollow — ทิศมือหมุนไปตามวงโค้ง ถ้าตามเต็มที่ข้อมือปั่นเกือบรอบ (ทุกเครื่องคิดเองเหมือนกัน)
                float face = Vector3.SignedAngle(edge, sweep, axis);
                if (float.IsNaN(wield[i].edgeLatch)) wield[i].edgeLatchT = 0f;
                wield[i].edgeLatchT += Time.deltaTime;
                if (wield[i].edgeLatchT < EdgeSettleTime * slashWeight) wield[i].edgeLatch = face; // ช่วงแรกของการฟาด ทิศมือยังเลี้ยวอยู่ ตามไปก่อน
                float target = wield[i].edgeLatch + Mathf.Clamp(Mathf.DeltaAngle(wield[i].edgeLatch, face), -edgeRollFollow, edgeRollFollow);
                wield[i].edgeRoll = Mathf.MoveTowardsAngle(wield[i].edgeRoll, target, EdgeRollSpeed * Time.deltaTime);
            }
            else if (raise < 0.5f || wield[i].swing01 < 0.4f)
            {
                wield[i].edgeRoll = Mathf.MoveTowardsAngle(wield[i].edgeRoll, 0f, EdgeRollReturn * Time.deltaTime); // ช้า/เงื้อ/ดึงดาบกลับ = คืนท่าข้อมือปกติ
                wield[i].edgeLatch = float.NaN;
            }
            if (slash[i].kind >= 0) slash[i].rollRange = new Vector2(Mathf.Min(slash[i].rollRange.x, wield[i].edgeRoll), Mathf.Max(slash[i].rollRange.y, wield[i].edgeRoll));
            limb.target.rotation = Quaternion.AngleAxis(wield[i].edgeRoll, axis) * limb.target.rotation;

            LimitForearmRoll(limb, i, along);
            ClampWrist(limb, along, i);
            // ด้ามตรงในกำปั้น → ข้อมือเอียงรอบแกนฝ่ามือแทน (มือ × เอียง × ด้ามตรง = มือเดิม × ด้ามเฉียง)
            limb.target.rotation = Quaternion.AngleAxis(tilt, limb.HandFrame(limb.target.rotation) * Vector3.up) * limb.target.rotation;
            inHand = Quaternion.AngleAxis(-tilt, Vector3.up) * inHand; // ทิศด้ามในมือหลังเอียง — ตัววัดด้านล่างอ่านถูก
            if (slash[i].tangent != Vector3.zero)
            {
                Vector3 radial = (limb.target.position - limb.root.position).normalized, plane = Vector3.Cross(radial, slash[i].tangent);
                if (plane.sqrMagnitude > 1e-6f)
                    slash[i].bladeAngle = Vector3.SignedAngle(radial, Vector3.ProjectOnPlane(limb.HandFrame(limb.target.rotation) * inHand, plane), plane);
            }

            // กันกระตุก (ผู้ใช้: ดาบกระตุก — วัดได้บางเฟรมมือหมุนกระโดด 60–119° ในเฟรมเดียว ตอนใบเกือบขนานแนวแขน ทิศหมุนข้อมือคำนวณไม่นิ่ง)
            // ท่ามือสุดท้ายไล่ตามแบบนุ่ม และหมุนได้ไม่เกิน HandTurnSpeed — ดาบฟาดจริงเร็วสุด ~1000°/วิ ยังตามทัน
            if (wield[i].handRotValid)
            {
                Quaternion smooth = Quaternion.Slerp(wield[i].lastHandRot, limb.target.rotation, 1f - Mathf.Exp(-HandTurnSmoothing * Time.deltaTime));
                limb.target.rotation = Quaternion.RotateTowards(wield[i].lastHandRot, smooth, HandTurnSpeed * Time.deltaTime);
            }
            wield[i].lastHandRot = limb.target.rotation;
            wield[i].handRotValid = true;

            // [ตรวจคม] คมเบี้ยวจากแนวที่ดาบวิ่งกี่องศา วัดท่าสุดท้ายจริง (หลัง edgeRoll + LimitForearmRoll + ClampWrist + ตัวนุ่มมือ)
            // ดาบสองคม: ใช้ฝั่งที่ใกล้แนวกว่า → 0 = คมนำเต็ม (ฟัน), 90 = หน้าแบนเข้าใส่ (ฟาด)
            if (slash[i].striking && slash[i].tangent != Vector3.zero)
            {
                Vector3 ax = (limb.HandFrame(limb.target.rotation) * inHand).normalized;
                float bend = Vector3.Angle(ax, slash[i].tangent); // 0 = ดาบขนานกับแนวที่ฟันพอดี
                slash[i].bladeArm = new Vector2(Mathf.Min(slash[i].bladeArm.x, bend), Mathf.Max(slash[i].bladeArm.y, bend));
                Vector3 edgeNow = Vector3.ProjectOnPlane(limb.HandFrame(limb.target.rotation) * Vector3.forward, ax);
                Vector3 cutNow = Vector3.ProjectOnPlane(slash[i].tangent, ax);
                if (edgeNow.sqrMagnitude > 1e-6f && cutNow.sqrMagnitude > 1e-6f)
                {
                    float err = Mathf.Abs(Vector3.SignedAngle(edgeNow, cutNow, ax));
                    err = Mathf.Min(err, 180f - err);
                    slash[i].edgeErr = new Vector2(Mathf.Min(slash[i].edgeErr.x, err), Mathf.Max(slash[i].edgeErr.y, err));
                }
            }
        }

        private const float HandTurnSmoothing = 25f; // มือถืออาวุธไล่ท่าที่คำนวณได้ (1/วิ) — ~0.03 วิไปได้ครึ่งทาง
        private const float HandTurnSpeed = 1400f;   // มือถืออาวุธหมุนเร็วสุด (°/วิ)

        // แขนท่อนล่างคว่ำ/หงายมือไม่เกินคน (ผู้ใช้ 2026-10-30: ง้างแล้วข้อมือพลิกหงาย) — เดิมหมุนมือรอบแนวแขนได้ไม่จำกัดเพื่อให้ใบชี้ไปทางที่ต้องการ
        // ศูนย์ = ท่าจับมือ: นิ้วโป้งชี้ไปทางต้นแขน (ฝั่งที่ศอกพับเข้า) ตั้งฉากกับแขนท่อนล่าง — วัดจากตำแหน่งไหล่/ศอก/ข้อมือจริงทุกเฟรม ไม่สะสม
        private void LimitForearmRoll(IKLimb limb, int i, Vector3 along)
        {
            Vector3 thumbHand = limb.ThumbInHand;
            if (forearmRollLimit <= 0f || thumbHand.sqrMagnitude < 1e-6f) return; // โมเดลไม่มีนิ้วโป้ง = วัดไม่ได้
            // เพดานต้องห่างจาก 180° พอสมควร ไม่งั้นท่าที่ขอจะวนไปโผล่อีกฝั่งของขอบตลอด (แขนท่อนล่างคนหมุนได้ ~150-180° รวมทั้งช่วง)
            float rollLimit = Mathf.Min(forearmRollLimit * SlashWristSlack(i), 120f);
            Vector3 ax = along.normalized;
            Vector3 neutral = Vector3.ProjectOnPlane(limb.root.position - limb.mid.position, ax);
            // แขนเหยียดตรงวัดทิศนี้ไม่ได้ — เดิมใช้ค่าล่าสุดค้างไว้ พอศอกงอกลับค่ากระโดด ข้อมือหมุน -38°→+120° ใน 0.2 วิ
            // (วัด 2026-11-03 คลิปผู้ใช้: ฟันเฉียงขึ้นเล็งซ้ายบน จบแขนตรงแล้วใบหมุนเข้าหาหัวแล้วกลับ) → ใช้แกนบานพับศอก นิ่งแม้เหยียดสุด เหมือน IKLimb.Solve
            Vector3 hinge = Vector3.ProjectOnPlane(Vector3.Cross(limb.mid.rotation * limb.HingeAxis, ax), ax);
            if (neutral.sqrMagnitude > limb.Reach * limb.Reach * 0.01f)
            {
                wield[i].thumbNeutral = neutral.normalized;
                if (hinge.sqrMagnitude > 1e-6f) wield[i].neutralSide = Mathf.Sign(Vector3.Dot(neutral, hinge));
            }
            neutral = wield[i].neutralSide != 0f && hinge.sqrMagnitude > 1e-6f ? hinge * wield[i].neutralSide : Vector3.ProjectOnPlane(wield[i].thumbNeutral, ax);
            Vector3 thumb = Vector3.ProjectOnPlane(limb.HandFrame(limb.target.rotation) * thumbHand, ax);
            if (neutral.sqrMagnitude < 1e-6f || thumb.sqrMagnitude < 1e-6f) return;
            // ⚠️ SignedAngle คืนค่า ±180 เสมอ วัดใหม่ทุกเฟรมโดยไม่รู้ว่าเฟรมก่อนอยู่ตรงไหน
            // ท่าที่หมุนข้ามขอบจึงอ่านได้ +179 แล้วเฟรมถัดไป -179 (ต่างกันจริงแค่ 2° แต่ตัวเลขต่าง 358°)
            // ต้องต่อให้เป็นค่าต่อเนื่องก่อน ไม่งั้นทั้งการจำกัดและการหมุนแก้ด้านล่างจะสะบัดข้ามฝั่ง = ข้อมือพลิกทะลุตอนฟันจะจบ
            float raw = Vector3.SignedAngle(neutral, thumb, ax);
            float roll = float.IsNaN(wield[i].lastRollRaw) ? raw : wield[i].lastRollRaw + Mathf.DeltaAngle(wield[i].lastRollRaw, raw);
            wield[i].lastRollRaw = roll;
            if (slash[i].kind >= 0) slash[i].twistRange = new Vector2(Mathf.Min(slash[i].twistRange.x, roll), Mathf.Max(slash[i].twistRange.y, roll));
            // เกินเพดาน = ค้างที่ขอบฝั่งเดิม (ผู้ใช้: มือพลิกกระทันหัน) — มุมเป็นวงกลม ท่าต้องการ +170° → -170° ห่างกันแค่ 20°
            // ถ้าจำกัดตรงๆ จะกระโดด +90° → -90° ในเฟรมเดียว | แล้วบิดได้ไม่เร็วเกิน ForearmRollSpeed (ข้อมือคนหมุนแรงสุด ~700–900°/วิ)
            float last = wield[i].lastRoll;
            float clamped = Mathf.Abs(roll) <= rollLimit ? roll
                          : (float.IsNaN(last) ? roll : last) >= 0f ? rollLimit : -rollLimit;
            // ท่ามือตอนฟัน: บิดรอบแกนแขนท่อนล่างให้ด้านที่เลือกหันไปทางที่กำหนด แล้วตัดด้วยเพดาน
            // อ่านจากท่าที่กำลังคำนวณ (target.rotation) ไม่ใช่กระดูกของเฟรมก่อน | ไม่ต้องรู้เครื่องหมาย ใช้ได้ทั้งสองแขน
            // ใช้ตลอดทั้งท่ารวมช่วงง้าง — ถ้าใช้เฉพาะบางช่วง มือจะพลิกตอนสลับกฎ (ผู้ใช้: ตอนง้างมือพลิกแล้วหมุนออก)
            // กฎท่ามือ มีผลตามน้ำหนักที่ค่อยๆ ขึ้น/ลง (slashPoseWeight) — เปิดปิดทันทีแล้วมือสะดุดหัวท้าย
            if (slashHandPose != HandPose.ปิด && slash[i].kind >= 0 && slash[i].tangent != Vector3.zero && slash[i].poseWeight > 0.001f)
            {
                Quaternion handNow = limb.HandFrame(limb.target.rotation);
                Vector3 side = slashHandPose == HandPose.นิ้วโป้งขึ้น ? handNow * thumbHand : handNow * Vector3.up; // Y ของแกนมือ = ฝ่ามือ
                Vector3 goal;
                if (slashHandPose == HandPose.ตามแนวฟัน)
                {
                    // ตั้งฉากกับระนาบวงฟัน = ระนาบที่ดาบกำลังกวาดผ่าน
                    // ใช้ทิศที่ล็อกไว้ตอนเริ่มท่า (คิดจากกลางวงฟัน) — ไม่คิดใหม่ทุกเฟรม ไม่งั้นเป้าหมุนตามวง มือพลิกไปมา
                    // ข้างถูกบังคับโดยแกนมือ: คมนำไปตามทิศวิ่ง + ใบต่อแนวแขน → ฝ่ามือ = ทิศวิ่ง × แนวแขน (ไม่มีอะไรให้เลือก)
                    goal = slash[i].palmGoal != Vector3.zero ? transform.rotation * slash[i].palmGoal : Vector3.down;
                }
                else goal = slashHandPose == HandPose.ฝ่ามือเข้าหาตัว ? transform.rotation * new Vector3(-limb.Side, 0f, 0f)
                          : slashHandPose == HandPose.นิ้วโป้งขึ้น ? Vector3.up
                          : Vector3.down;
                Vector3 nowFlat = Vector3.ProjectOnPlane(side, ax), goalFlat = Vector3.ProjectOnPlane(goal, ax);
                if (nowFlat.sqrMagnitude > 1e-4f && goalFlat.sqrMagnitude > 1e-4f)
                {
                    // เลือกรอบที่ใกล้ท่าตอนนี้ — SignedAngle วนที่ ±180 พอต้องบิดเกินครึ่งรอบ เป้ากระโดดไปชนเพดานอีกฝั่ง
                    // (วัด 2026-11-03 คลิปผู้ใช้: ฟันเฉียงขึ้นเล็งซ้ายบน ข้อมือ -50°→+120° ใน 0.27 วิ ตอนค้างยอดง้าง)
                    float pose = roll + Vector3.SignedAngle(nowFlat, goalFlat, ax);
                    float near = float.IsNaN(last) ? roll : last;
                    pose = Mathf.Clamp(near + Mathf.DeltaAngle(near, pose), -rollLimit, rollLimit);
                    clamped = Mathf.Lerp(clamped, pose, slash[i].poseWeight); // ผสมตามน้ำหนัก ไม่ใช่ทับทั้งหมด
                }
            }
            if (!float.IsNaN(last))
            {
                // นุ่ม + เพดานความเร็ว — ทิศที่ท่าต้องการแกว่งรอบขอบเพดานเฟรมต่อเฟรม (วัดได้ -85 → -65 → -85 ใน 2 เฟรม = ดาบกระตุก)
                clamped = Mathf.Lerp(last, clamped, 1f - Mathf.Exp(-ForearmRollSmoothing * Time.deltaTime));
                clamped = Mathf.MoveTowards(last, clamped, ForearmRollSpeed * Time.deltaTime);
                if (slash[i].kind >= 0) slash[i].rollJump = Mathf.Max(slash[i].rollJump, Mathf.Abs(clamped - last));
            }
            wield[i].lastRoll = clamped;
            // roll กับ clamped อยู่ในสเกลต่อเนื่องเดียวกันแล้ว ลบกันตรงๆ ได้ (เดิมใช้ DeltaAngle ซึ่งพับที่ ±180 = ต้นเหตุของการสะบัด)
            float fix = clamped - roll;
            if (Mathf.Abs(fix) > 0.5f) limb.target.rotation = Quaternion.AngleAxis(fix, ax) * limb.target.rotation;
        }

        /// <summary>แขนนี้อยู่ในท่าฟันอยู่ไหม → เพดานข้อมือกว้างขึ้นกี่เท่า (ท่าพัก/ถือเฉยๆ = 1 เท่าเดิม)</summary>
        private float SlashWristSlack(int arm) =>
            arm >= 0 && arm < slash.Length && slash[arm].kind >= 0 ? Mathf.Max(1f, slashWristBoost) : 1f;

        private const float ForearmRollSpeed = 720f; // คว่ำ/หงายมือเร็วสุด (°/วิ)
        private const float ForearmRollSmoothing = 12f; // คว่ำ/หงายมือไล่ตามทิศที่ต้องการ (1/วิ) — ~0.08 วิไปได้ครึ่งทาง
        private const float EdgeRollReturn = 180f;   // คืนหน้าดาบเข้าท่าปกติ (°/วิ) — ผู้ใช้ 2026-11-01: ฟันจบแล้วค่อยๆ กลับ ไม่รีบ (เดิม 720)
        private const float SlashEdgeTurn = 90f;     // ท่าฟัน 360°: หมุนหน้าดาบให้คมเข้าแนวเส้นได้ไม่เกินนี้ (ผู้ใช้ 2026-11-01: ใบต้อง "แบ" ตรงกับที่วาด)
                                                     // ใบอยู่แนวแขนแล้ว หมุนรอบแกนนี้ = คว่ำ/หงายแขนท่อนล่าง (ธรรมชาติ) — LimitForearmRoll ±forearmRollLimit คุมอีกชั้น
                                                     // เดิม 30° เพราะกลางวงฟันใบเคยตั้งฉากกับแขน ทิศคมที่ต้องการแกว่งทั้งวง

        // ข้อมือไม่หักเกินมือคน (ผู้ตรวจมือ 2026-10-29: เดิมเอียงทางนิ้วก้อย 39–45° ค้างตอนฟาด, ฟันขึ้นตวัดทางนิ้วโป้ง 20–25°)
        // แยกมุมแขนท่อนล่างในแกนมือเป็น งอ/แอ่น (รอบแกนนิ้วโป้ง) กับ เอียงนิ้วโป้ง/นิ้วก้อย (รอบแกนฝ่ามือ) แล้วจำกัดทีละทิศตาม wristLimits
        private void ClampWrist(IKLimb limb, Vector3 along, int slashArm = -1)
        {
            Quaternion hand = limb.HandFrame(limb.target.rotation);
            Vector3 thumb = limb.ThumbInHand;
            float slack = SlashWristSlack(slashArm);
            Vector4 limit = wristLimits * slack;
            float bendMax = Mathf.Min(weaponWristBend * slack, 85f); // tan ระเบิดใกล้ 90°
            if (thumb.sqrMagnitude < 1e-6f) // โมเดลไม่มีกระดูกนิ้วโป้ง = แยกทิศไม่ได้ จำกัดรวมทุกทิศไว้ที่ bendMax แทน
            {
                Vector3 fingers = hand * Vector3.forward, fore = along.normalized;
                if (Vector3.Angle(fingers, fore) > bendMax)
                    limb.target.rotation = Quaternion.FromToRotation(fingers, Vector3.RotateTowards(fore, fingers, bendMax * Mathf.Deg2Rad, 0f)) * limb.target.rotation;
                return;
            }
            thumb = Vector3.ProjectOnPlane(thumb, Vector3.forward).normalized;
            Vector3 palm = Vector3.Cross(Vector3.forward, thumb); // ตั้งฉากทั้งนิ้วและนิ้วโป้ง (ฝั่งฝ่ามือหรือหลังมือตามแกนมือ)
            if (Vector3.Dot(palm, Vector3.up) < 0f) palm = -palm;   // ให้ชี้ทางฝ่ามือ (แกน Y ของแกนมือ)
            Vector3 f = Quaternion.Inverse(hand) * along.normalized; // แขนท่อนล่างในแกนมือ — ข้อมือตรง = (0,0,1)
            float z = Mathf.Max(f.z, 0.05f);
            float flex = Mathf.Atan2(-Vector3.Dot(f, palm), z) * Mathf.Rad2Deg;   // + = งอเข้าฝ่ามือ
            float radial = Mathf.Atan2(-Vector3.Dot(f, thumb), z) * Mathf.Rad2Deg; // + = เอียงทางนิ้วโป้ง
            if (slashArm >= 0 && slash[slashArm].kind >= 0) // [ตรวจมือ] ค่าที่ท่าต้องการก่อนจำกัด
            {
                slash[slashArm].flexRange = new Vector2(Mathf.Min(slash[slashArm].flexRange.x, flex), Mathf.Max(slash[slashArm].flexRange.y, flex));
                slash[slashArm].radialRange = new Vector2(Mathf.Min(slash[slashArm].radialRange.x, radial), Mathf.Max(slash[slashArm].radialRange.y, radial));
            }
            float flexC = Mathf.Clamp(flex, Mathf.Max(-85f, -limit.w), Mathf.Min(85f, limit.z));
            float radialC = Mathf.Clamp(radial, Mathf.Max(-85f, -limit.x), Mathf.Min(85f, limit.y));
            Vector3 fC = (Vector3.forward - palm * Mathf.Tan(flexC * Mathf.Deg2Rad) - thumb * Mathf.Tan(radialC * Mathf.Deg2Rad)).normalized;
            // แต่ละทิศผ่านแล้วแต่รวมกันยังเยอะ (งอ+เอียงพร้อมกัน ดูข้อมือหัก) — จำกัดมุมรวมด้วย weaponWristBend
            fC = Vector3.RotateTowards(Vector3.forward, fC, bendMax * Mathf.Deg2Rad, 0f);
            if (Vector3.Angle(fC, f) < 0.5f) return;
            // หมุนมือให้แขนท่อนล่าง (ทิศเดิมในโลก) อยู่ที่มุมที่จำกัดแล้วในแกนมือ
            limb.target.rotation = Quaternion.FromToRotation(hand * fC, along.normalized) * limb.target.rotation;
        }
    }
}

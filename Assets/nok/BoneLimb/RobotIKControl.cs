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
    /// <summary>
    /// ตัวกลางของหุ่น IK — หุ่นตัวเดียว คนละชิ้น
    /// แต่ละชิ้น = <see cref="IKLimb"/> (IK บานพับ), ข้อจำกัดข้อต่อ = <see cref="ShoulderLimit"/> / <see cref="HipLimit"/>,
    /// กล้อง = <see cref="OrbitCamera"/>, อินพุต = <see cref="IKInput"/>, ข้อมูล network = <see cref="LimbNetState"/>
    /// ไฟล์นี้ทำแค่: ใครคุมชิ้นไหน, แปลงเคอร์เซอร์เป็นเป้า, เดินตามเท้า, ซิงก์ และ UI
    ///
    /// แขน: มือตามเคอร์เซอร์ (ระนาบหน้าไหล่, Shift+ล้อ ดัน/ดึง), คลิกซ้ายค้าง = กำ
    /// ขา (แบบหุ่นเดิม): ชี้พื้น + คลิก = ก้าวเป็นเส้นโค้งไปตรงนั้น, ค้าง = ลอยตามเคอร์เซอร์ ปล่อย = วาง (ก้าวเฉียง = เลี้ยว, Shift = เตะสูงไปทาง ◈, F ค้าง = ย่อ / สองขาพร้อมกัน = ปักหลักกันล้ม) | ล้อ = ซูม | คลิกขวาค้าง = หันกล้อง
    ///
    /// ใครคุมชิ้นไหน = หน้าเลือกชิ้นส่วนเดิม (LimbSelection บน SelectPart) — หุ่นนี้มี Robot + RobotLimb 4 ช่องให้มันรู้จัก
    /// เข้าห้อง: AutoStartHost ในฉาก (เปิดฉากตรงๆ) หรือ Join code จากเมนู | ไม่มี NetworkManager = เล่นคนเดียว กด 1-4 สลับชิ้น
    /// เครื่องที่คุมชิ้นคำนวณเป้าเอง แล้วส่งให้ Server ตรวจ (เจ้าของ + ข้อจำกัดข้อต่อ) และกระจายผ่าน NetworkList
    /// ตัวหุ่น (เดินตามเท้า) คำนวณบน Server แล้วซิงก์ด้วย NetworkTransform
    /// </summary>
    public partial class RobotIKControl : NetworkBehaviour
    {
        [Tooltip("ลำดับ = LimbSlot: แขนซ้าย, แขนขวา, ขาซ้าย, ขาขวา")]
        [SerializeField] private IKLimb[] limbs = new IKLimb[4];
        [SerializeField] private GameObject model;
        [SerializeField] private Camera cam;
        [SerializeField] private OrbitCamera orbit = new OrbitCamera();

        [Header("การคุม")]
        [Tooltip("Shift + ล้อเมาส์: ดัน/ดึงมือ (สัดส่วนความยาวแขนต่อหน่วยล้อ)")]
        [SerializeField] private float depthSpeed = 0.001f;
        [Tooltip("เวลาเหวี่ยงเท้าหนึ่งก้าว (วินาที) — ยาวขึ้น = ดูหนักขึ้น (หุ่นเดิม stepSwingTime 0.6)")]
        [SerializeField] private float stepSwingTime = 0.6f;
        [Tooltip("ความสูงยอดโค้งของก้าว เป็นสัดส่วนของความยาวขา (หุ่นเดิม stepArcHeightRatio 0.2)")]
        [Range(0f, 0.5f)] [SerializeField] private float stepArcHeight = 0.2f;
        [Tooltip("มือ/เท้าไหลตามเคอร์เซอร์เร็วแค่ไหน (มาก = ติดเมาส์, น้อย = นุ่ม)")]
        [SerializeField] private float follow = 12f;
        [SerializeField] private float gripSpeed = 8f;

        [Header("ตัวหุ่น")]
        [Tooltip("ย่อตัวลงจากท่ายืนตรง — ขายาวแบบนี้ 0.15 ≈ เข่างอ 27°, 0.6 ≈ 54°")]
        [SerializeField] private float crouch = 0.15f;
        [Tooltip("คนคุมขาค้าง F = ย่อหลบ (ส่วนของความยาวขา) | สองขาค้างพร้อมกัน เท้าติดพื้น = ปักหลัก แรงกระแทกจากศัตรูล้มไม่ได้")]
        [SerializeField] private float braceCrouch = 0.25f;
        [SerializeField] private float bodyFollow = 3f;
        [SerializeField] private float remoteSmoothing = 15f;
        [Tooltip("กล่องข้อความมุมซ้ายบน: คุมชิ้นไหน/เลือด/วิธีเล่น/คำใบ้ตอนล้ม — ปิดไว้ (ผู้ใช้สั่งเอาออก 2026-10-30) เปิดไว้ตอนเทสถ้าอยากเห็นเลขเลือด")]
        [SerializeField] private bool showInfoBox = false;

        [Header("ข้อต่อแบบคน (องศา)")]
        [SerializeField] private ShoulderLimit shoulder = new ShoulderLimit();
        [SerializeField] private HipLimit hip = new HipLimit();

        [Header("ต่อสู้: การ์ด + หมัดชาร์จ")]
        [SerializeField] private IKCombat combat = new IKCombat();

        [Header("ความหนัก: ก้าว, น้ำหนักตัว, เอฟเฟกต์")]
        [SerializeField] private IKFeel feel = new IKFeel();

        [Header("ดาเมจหมัด (แบบ LimbStrike เดิม: ดาเมจ = ความเร็วพีคของกำปั้น × speedToDamage)")]
        [Tooltip("กำปั้นเร็วกว่านี้ (m/s) = กำลังต่อย — ช้ากว่านี้ชนอะไรก็ไม่มีดาเมจ")]
        [SerializeField] private float strikeMinSpeed = 8f;
        [Tooltip("ความเร็วพีค (m/s) × ค่านี้ = ดาเมจ — หุ่น IK กำปั้นช้ากว่า (หมัดแตะ ~18 m/s) ตั้ง 1.6 ให้ได้ ~30 ใกล้หมัดหุ่นเดิม (พีค 55 × 0.6 = 33)")]
        [SerializeField] private float strikeSpeedToDamage = 1.6f;
        [SerializeField] private float strikeMaxDamage = 60f;
        [Tooltip("รัศมีกำปั้นที่นับว่าโดน")]
        [SerializeField] private float strikeRadius = 1.2f;

        [Header("ถีบแบบบาทาลูบพักตร์ (กด Shift = ยกเข่าแล้วถีบฝ่าเท้าไปทาง ◈ — ดาเมจคิดแบบหมัด)")]
        [Tooltip("เวลายกเข่าก่อนถีบ (วินาที) — สั้น = กดแล้วออกเท้าไว")]
        [SerializeField] private float kickChamberTime = 0.25f;
        [Tooltip("ความเร็วเป้าเท้าตอนถีบ (m/s) × แรงที่เหลือตามเลือดขา — ปลายเท้าจริงเร็วกว่านี้ตอนเข่าเหยียด")]
        [SerializeField] private float kickThrustSpeed = 35f;
        [Tooltip("ความเร็วพีคของเท้า × ค่านี้ = ดาเมจถีบ (เพดาน strikeMaxDamage)")]
        [SerializeField] private float kickSpeedToDamage = 1.2f;
        [Tooltip("ยกเข่า: เท้าอยู่ต่ำกว่าสะโพกเท่านี้ (สัดส่วนความยาวขา) — น้อย = ยกเข่าสูง")]
        [SerializeField] private float kickChamberDrop = 0.3f;
        [Tooltip("ขาเหยียดชี้ขึ้นเหนือแนวราบกี่องศา — 25 = ถีบตรงไปข้างหน้าระดับหน้าคู่ต่อสู้ (บาทาลูบพักตร์), 55 = เตะสูงแบบคาราเต้")]
        [SerializeField] private float kickHighAngle = 25f;
        [Tooltip("ลำตัวเอนหนีเท้าที่ยกสูงสุดกี่องศา (เตะสูงแล้วตัวเอนถ่วงไว้)")]
        [SerializeField] private float kickLean = 30f;
        [Tooltip("ถีบได้แค่ข้างหน้า: ทิศ ◈ เบนจากหน้าหุ่นได้ไม่เกินกี่องศา (เล็งตามเคอร์เซอร์ได้ระหว่างยกเข่า แบบแขนที่เล็งได้ในกรอบข้อต่อ)")]
        [SerializeField] private float kickAimLimit = 25f;
        [Tooltip("ปลายเท้าเชิดขึ้นตอนถีบ (องศา) — ฝ่าเท้าหันเข้าเป้า")]
        [SerializeField] private float kickSolePitch = 70f;
        [Tooltip("ถีบสุดแล้วเท้าตกลงพื้นด้วยความเร่งนี้ (m/s²) — มาก = กระแทกลงไว")]
        [SerializeField] private float kickFallGravity = 40f;
        [Tooltip("แรงเตะที่เหลือตอนขาแทบไม่มีเลือด (หุ่นเดิม 0.3 → 100% ตามเลือด)")]
        [Range(0f, 1f)] [SerializeField] private float kickPowerAtZeroHealth = 0.3f;

        [Header("โดนตี")]
        [Tooltip("ลำตัวสะท้าน (ความเร็วองศา/วิ ต่อดาเมจ 1) — หงายไปหลัง + เอียงหนีฝั่งชิ้นที่โดน | ดาเมจ 60 ≈ เอน ~8°")]
        [SerializeField] private float hitFlinch = 1.6f;
        [Tooltip("ตัวถูกดันถอยหลัง (m/s ต่อดาเมจ 1) แล้วสปริงกลับกลางเท้า")]
        [SerializeField] private float hitShove = 0.08f;

        [Header("ท่าขา: เตะของ / กระทืบ / จังหวะเดิน (ผู้ใช้เลือกชุดที่ 1, 2026-10-30)")]
        [Tooltip("ถีบโดนของที่ขยับได้ (กล่อง/บาร์/ชิ้นส่วน) = ของพุ่งไปทางที่ถีบด้วยความเร็วนี้ (m/s) ÷ (1 + มวล/200)")]
        [SerializeField] private float kickPropSpeed = 40f;
        [Tooltip("ของที่พุ่งไปชนศัตรู = ดาเมจเท่านี้ (ครั้งเดียว)")]
        [SerializeField] private float kickPropDamage = 25f;
        [Tooltip("คลิกยกเท้าค้างอย่างน้อยกี่วินาทีแล้วปล่อย = กระทืบ (คลื่นกระแทก งัดของรอบเท้าลอยขึ้น)")]
        [SerializeField] private float stompHold = 0.4f;
        [Tooltip("รัศมีคลื่นกระแทก (ส่วนของความยาวขา)")]
        [SerializeField] private float stompRadius = 1.2f;
        [Tooltip("ของในวงกระทืบลอยขึ้นด้วยความเร็วนี้ (m/s) — ใกล้เท้า = สูงกว่า")]
        [SerializeField] private float stompLift = 20f;
        [Tooltip("ศัตรูในวงกระทืบโดนดาเมจเท่านี้ + กระเด็นนิดหน่อย")]
        [SerializeField] private float stompDamage = 10f;
        [Tooltip("ก้าวสลับซ้าย-ขวาห่างกัน (วินาที) อยู่ในช่วงนี้ + ก้าวยาวพอ = จังหวะติด (สะสมได้ถึง 3) → ก้าวเร็วขึ้น ตัวไหลตามเท้าแรงขึ้น")]
        [SerializeField] private Vector2 strideWindow = new Vector2(0.3f, 1.4f);
        [Tooltip("ต่อจังหวะ: ก้าวเร็วขึ้น/ตัวไหลแรงขึ้นกี่ส่วน (3 จังหวะ × 0.15 = เร็วขึ้น 45%)")]
        [SerializeField] private float strideBonus = 0.15f;

        [Header("ท่าแขน: การ์ด / ขว้าง / ประสานสองแขน / ล็อก (ผู้ใช้ขอครบ 7 ท่า 2026-10-30 — คลานตอนล้ม = ดึงมือที่มีอยู่แล้ว)")]
        [Tooltip("Shift ค้างที่แขน = ยกการ์ด: ดาเมจจากศัตรูที่แขนนั้นโดนเหลือเท่านี้ (ชิ้นอื่นโดนเต็ม) | สองแขนยกพร้อมกัน = แรงกระแทกล้มไม่ได้เหมือนปักหลัก | ล้มอยู่ = ยกไม่ได้")]
        [Range(0f, 1f)] [SerializeField] private float blockDamageScale = 0.2f;
        [Tooltip("ยกการ์ดไม่เกินกี่วินาทีก่อนโดนตี = ปัดป้อง (Parry): ไม่โดนเลย + ศัตรูเสียหลัก")]
        [SerializeField] private float parryWindow = 0.3f;
        [Tooltip("ศัตรูเสียหลัก (AI หยุด) นานกี่วินาที — จาก Parry / สองแขนฟาดพร้อมกัน | เสียหลักอยู่ = กด F ใส่ล็อกตัวได้")]
        [SerializeField] private float staggerTime = 1.5f;
        [Tooltip("ถือของอยู่กด F = ขว้างไปทางเคอร์เซอร์ทันที ด้วยความเร็วนี้ (m/s) ÷ (1 + มวล/200) และดาเมจตอนของที่ขว้างชนศัตรู")]
        [SerializeField] private float throwSpeed = 45f;
        [SerializeField] private float throwDamage = 30f;
        [Tooltip("สองแขนต่อย/ฟาดโดนเป้าเดียวกันห่างกันไม่เกินกี่วินาที = Double Impact (ดาเมจโบนัส + ศัตรูเสียหลัก)")]
        [SerializeField] private float doubleImpactWindow = 0.3f;
        [SerializeField] private float doubleImpactDamage = 30f;
        [Tooltip("ล็อกศัตรูที่เสียหลักได้นานสุดกี่วินาที (ระหว่างนั้น AI หยุด)")]
        [SerializeField] private float grappleTime = 2.5f;

        [Header("จับ (F = มือยึดของที่แตะ — ของตามมือ ของหนักหน่วงมือ)")]
        [Tooltip("เปิด = กด F ครั้งเดียวจับ กดอีกครั้งปล่อย | ปิด = ค้าง F ถึงจะจับ ปล่อย F = ปล่อย (แบบ Half-sword จริง)")]
        [SerializeField] private bool grabToggle = true;
        [Tooltip("รัศมีรอบกำปั้นที่จับของได้")]
        [SerializeField] private float grabRadius = 1.2f;
        [Tooltip("ของที่จับหนักเท่านี้ มือ/หมัดช้าลงครึ่งหนึ่ง (ช้า = 1 ÷ (1 + มวล ÷ ค่านี้)) — ของเบากว่านี้มือแทบไม่ช้า")]
        [SerializeField] private float grabHalfMass = 80f;
        [Tooltip("ช้าสุดกี่เท่าของปกติ — ของหนักมากมือยังขยับได้")]
        [SerializeField] private float grabMinSpeed = 0.15f;
        [Tooltip("ของที่แขนที่คุมอยู่เอื้อมถึงเรืองแสงสีนี้ (เฉพาะจอคนคุม) — กด F แล้วแขนเอื้อมไปหยิบชิ้นที่เรืองเอง")]
        [ColorUsage(false, true)] [SerializeField] private Color grabGlow = new Color(1f, 0.75f, 0.25f) * 0.6f;
        [Tooltip("เอื้อมไปหยิบนานสุดกี่วินาที — ไปไม่ถึง (ของขยับหนี/ข้อต่อพับไม่ได้) ก็เลิกเอื้อม กลับไปคุมตามปกติ")]
        [SerializeField] private float grabReachTime = 3f;
        [Tooltip("ถืออาวุธ + คลิกซ้ายค้าง: ลากเมาส์ 1 พิกเซล = แขนหมุนรอบไหล่กี่องศา")]
        [SerializeField] private float weaponSwingPerPixel = 0.18f;
        [Tooltip("ข้อมือถืออาวุธงอได้รวมกี่องศาจากแนวแขน (ทุกทิศรวมกัน — ใช้คู่กับ wristLimits)")]
        [SerializeField] private float weaponWristBend = 35f;
        [Tooltip("ข้อมือถืออาวุธ จำกัดแยกทิศตามมือคน (องศา): x = เอียงทางนิ้วก้อย (ulnar), y = เอียงทางนิ้วโป้ง (radial), z = งอเข้าฝ่ามือ (flex), w = แอ่นหลังมือ (extension) — ส่วนที่เกินให้แขนท่อนล่างบิดแทน")]
        [SerializeField] private Vector4 wristLimits = new Vector4(20f, 10f, 40f, 35f); // ผู้ตรวจมือรอบสอง: ช่วงสบายตอนออกแรง ไม่ใช่สุดช่วงข้อต่อ
        [Tooltip("ถืออาวุธ: ลำตัวบิดตามมือได้สุดกี่องศา (ง้างไปหลัง = บิดง้าง, ฟาดข้ามตัว = บิดตาม)")]
        [SerializeField] private float weaponTwist = 40f;
        [Tooltip("ถืออาวุธ: เปิด = ลากเมาส์คุมแขนอิสระ | ปิด (ค่าเริ่มต้น) = คลิกซ้ายค้าง + ลากสั้นๆ เลือกทิศฟัน เกมเล่นท่าให้ (ง่ายสำหรับคนเล่นไม่เป็น)")]
        [SerializeField] private bool weaponFreeAim;
        [Tooltip("ลากเมาส์กี่พิกเซลถึงนับเป็นการเลือกทิศฟัน")]
        [SerializeField] private float slashDragPixels = 30f;
        [Tooltip("ความหนักของท่าฟัน: 1 = ไว (ง้าง 0.28 / ฟัน 0.16 / ค้าง 0.2 วิ) | 2 = หุ่นพันตัน ทุกช่วงช้าลง 2 เท่า แขนตามเป้าหน่วงขึ้น")]
        [SerializeField] private float slashWeight = 2f;
        [Tooltip("ถืออาวุธ: เล็ง/หันท่าฟันเข้าหาศัตรูได้ไกลสุดเท่านี้ (เท่าของระยะที่ \"มือ\" เอื้อมถึงตอนแขนเหยียด) | 0 = ไม่หัน\n" +
                 "ผู้ใช้ 2026-11-01: เดิม 3 เท่าความยาวแขน = เล็งฟันของที่ไกลเกินกว่าแขนจะเอื้อมถึง (วัดในฉาก TestItem: แขน 4.05 → เล็งได้ถึง 12.2)")]
        [SerializeField] private float slashAssistRange = 1f;
        [Tooltip("ท่าฟัน 360°: แขนขวาฟันไปทิศในช่วงนี้ไม่ได้ (x = มุมกลาง°, y = ครึ่งความกว้าง°; 0 = ไปขวา, 90 = ขึ้น) — ลากเข้าช่วงนี้ = ปัดไปขอบที่ใกล้สุด | แขนซ้ายใช้กระจก (180 − มุม)\n" +
                 "ค่าเริ่มต้น = ฟันย้อนขึ้นข้ามลำตัว (ล่างซ้าย → บนขวา ของแขนขวา) ซึ่งต้องหงายมือเกินคนจริง")]
        [SerializeField] private Vector2 slashBlockedArc = new Vector2(45f, 15f); // ทดสอบไล่ทุก 30° หลังล็อกหน้าดาบ: 30°/60° ข้อมือใกล้ท่าเดิม 4 ทิศแล้ว เหลือกันไว้แค่แกนกลาง
        [Tooltip("ปัดทิศฟันเข้าช่องละกี่องศา — 45 = 8 ทิศ (ขึ้น ลง ซ้าย ขวา + เฉียง 4 มุม) | 90 = 4 ทิศ | 0 = อิสระ 360° แบบเดิม\n" +
                 "ผู้ใช้ 2026-11-02: มุมอิสระทำให้แต่ละการฟันไม่เหมือนกันสักครั้ง ตรวจและแก้ทีละท่าไม่ได้ — จำกัดทิศแล้วไล่แก้ทีละทิศได้\n" +
                 "ทิศที่ตกช่วงห้าม (slashBlockedArc) จะข้ามไปทิศถัดไปที่ฟันได้ ไม่ใช่ปัดไปขอบช่วง")]
        [SerializeField, Range(0f, 90f)] private float slashAngleSnap = 45f;
        [Tooltip("ท่าฟัน 360°: จุดที่เคอร์เซอร์ชี้ตอนกด = จุดที่ดาบฟันผ่าน เลื่อนวงฟันได้ไม่เกินนี้ (x = ซ้าย-ขวา°, y = ขึ้น-ลง°)")]
        [SerializeField] private Vector2 slashAimRange = new Vector2(45f, 30f);
        [Tooltip("ค้างคลิกที่ยอดง้างนานเท่านี้ (วิ × slashWeight) = ชาร์จเต็ม: ง้างลึกขึ้น 15% ฟันไวขึ้น 30% (ดาเมจตามความเร็วปลายดาบ)")]
        [SerializeField] private float slashChargeTime = 0.6f;
        [Tooltip("ระหว่างฟาดหนึ่งครั้ง หน้าดาบหันตามทิศมือได้อีกไม่เกินกี่องศาจากตอนเริ่มฟาด (ล็อกหน้าดาบ) — เดิมไม่จำกัด ฟันเฉียงย้อนแล้วหน้าดาบหมุนตามทิศมือเกือบรอบ (วัดได้ -164..135° ในท่าเดียว) ข้อมือปั่น\n" +
                 "คนจริงคมดาบนิ่งตลอดวงฟัน | ตอนเริ่มฟาดยังหันเต็มที่ตามทิศฟาดเหมือนเดิม")]
        [SerializeField] private float edgeRollFollow = 30f;
        [Tooltip("ถืออาวุธ: แขนท่อนล่างคว่ำ/หงายมือได้สุดกี่องศาจากท่าจับมือ (นิ้วโป้งชี้ไปทางต้นแขน) — เดิมไม่จำกัด ง้างแล้วมือหงายพลิกกลับด้าน\n" +
                 "เกิน = มือหยุดบิด ยอมให้ใบดาบเอียงจากทิศที่ท่าฟันต้องการแทน")]
        [SerializeField] private float forearmRollLimit = 90f; // วัด 2026-10-30: ยืนถือ -11°, ฟันลง -30..15°, ฟันขึ้นเดิมพลิกถึง ±177° (ภาพผู้ใช้), กวาดข้าง 100–147°
        [Tooltip("ตอนอยู่ในท่าฟัน ข้อมือ/แขนท่อนล่างหมุนได้กว้างกว่าตอนถือเฉยๆ กี่เท่า (คูณ wristLimits, weaponWristBend, forearmRollLimit) — 1 = เท่ากัน\n" +
                 "ผู้ใช้ 2026-11-01: ฟันตรงๆ แล้วดาบไม่ลงตรง เพราะท่าที่ถูกขอหมุนข้อมือ 80° คว่ำ/หงาย 180° แต่เพดานถือเฉยๆ ให้ 20°/85° เลยโดนตัดจนใบเบี้ยว\n" +
                 "ท่าพัก/ถือเฉยๆ ยังใช้เพดานเดิม ท่าจับจึงยังดูเป็นคน | ทิศคมล็อกต่อหนึ่งการฟันแล้ว เป้าไม่ขยับทุกเฟรม จึงไม่เกิดอาการข้อมือหมุนรัวแบบรอบก่อน")]
        [SerializeField, Range(1f, 3f)] private float slashWristBoost = 2f;
        /// <summary>ท่ามือระหว่างฟัน — บิดแขนท่อนล่างให้ด้านที่เลือกหันไปทางที่กำหนด (ทิศใบดาบเป็นผลตามมา ไม่ใช่ตัวกำหนด)</summary>
        public enum HandPose { ปิด, ฝ่ามือเข้าหาตัว, นิ้วโป้งขึ้น, ฝ่ามือคว่ำ, ตามแนวฟัน }
        [Tooltip("ท่ามือตอนฟัน — บิดแขนท่อนล่างเท่าที่เพดานข้อต่อยอม | ปิด = ปล่อยให้ท่าดาบกำหนดเอง\n" +
                 "ตามแนวฟัน = ฝ่ามือตั้งฉากกับระนาบที่ดาบกำลังกวาด → ฟันข้าง(ระนาบนอน) ได้คว่ำมือ, ฟันลง(ระนาบตั้ง) ได้ฝ่ามือเข้าหาตัว โดยอัตโนมัติ\n" +
                 "ผู้ใช้ 2026-11-02: \"มือมันควรหมุนตามทิศที่ดาบจะไป\" — สามตัวเลือกแรกเป็นทิศตายตัวในโลก ถูกทิศเดียวแล้วผิดทิศอื่น")]
        [SerializeField] private HandPose slashHandPose = HandPose.ตามแนวฟัน;
        [Tooltip("ตอนฟัน ให้ \"ไหล่ + ศอก\" เป็นคนคุมท่าทั้งหมด ข้อมือแค่ตรงแนวแขนและคว่ำนิ่ง — ทิศใบดาบเป็นผลจากท่าแขน ไม่ใช่สิ่งที่ไปสั่งข้อมือ\n" +
                 "ปิด = แบบเดิม (สั่งทิศใบก่อน แล้วบังคับข้อมือให้ได้ทิศนั้น แล้วโดนเพดานข้อต่อตัด)\n" +
                 "ผู้ใช้ 2026-11-02: ไหล่ปรับมุม ศอกเป็นจุดหมุนของวงฟัน แขนคนคว่ำลงอยู่แล้ว")]
        [SerializeField] private bool slashArmDrives = true;
        [Tooltip("ตอนฟัน ข้อมือช่วยหันปลายดาบเข้าหาทิศที่กำลังเคลื่อนที่ได้ไม่เกินกี่องศา (ใช้ร่วมกับ slashArmDrives) — 0 = ไม่ช่วยเลย ปล่อยตามท่าแขนล้วน\n" +
                 "ผู้ใช้ 2026-11-02: ท่าหลักมาจากไหล่+ศอก แล้วให้ข้อมือช่วยเก็บส่วนที่ยังไม่ตรง เช่นตอนฟันลง — ช่วยแบบมีเพดาน ไม่ใช่สั่งทั้งท่าแบบเดิม")]
        [SerializeField, Range(0f, 60f)] private float slashWristAssist = 30f;
        [Tooltip("จุดเริ่มท่าฟันง้างข้ามไปอีกฝั่งของลำตัวได้ไม่เกินกี่องศา (ยิ่งน้อยยิ่งไม่เอื้อมข้ามตัว) | 90 = ไม่จำกัด\n" +
                 "ผู้ใช้ 2026-11-02: ท่าเฉียงจากซ้ายบนมาขวาล่าง จุดเริ่มอยู่ซ้ายบนไกลไป แขนขวาต้องเอื้อมข้ามลำตัว ท่าอัด\n" +
                 "ท่าต้นแบบกวาดข้างเริ่มที่ 70° พอผสมเป็นท่าเฉียงจุดเริ่มเลยถูกดึงไปไกลตาม")]
        [SerializeField, Range(0f, 90f)] private float slashReachAcross = 40f;
        [Tooltip("วงฟันขึ้นได้สูงสุดกี่องศาเหนือแนวไหล่ — กันไม่ให้มือขึ้นไปเบียดข้างหัว (ท่าอัด มือบิดแปลก) | ลดค่านี้ = ผู้เล่นตีสูงไม่ได้มาก")]
        [SerializeField, Range(10f, 88f)] private float slashTopAngle = 35f;
        [Tooltip("ตอนง้าง มือถูกดึงเข้ามาที่กี่ส่วนของความยาวแขน — ยิ่งน้อยศอกยิ่งพับ ท่ายิ่งขด | ผู้ใช้ 2026-11-02: วัดได้ศอกพับ 91 องศา แขนขดอยู่หน้าหุ่น")]
        [SerializeField, Range(0.5f, 0.95f)] private float slashCockedReach = 0.85f;
        [Tooltip("เส้นนำแนวฟัน (เห็นเฉพาะคนคุม) ระหว่างลากเลือกมุม/ง้าง")]
        [SerializeField] private bool slashPreview = true;
        [Tooltip("[ตรวจมือ] log มุมบิดแขนท่อนล่าง/ข้อมือ ต่ำสุด-สูงสุดของทุกท่าฟัน — ใช้ตั้ง edgeRollFollow / slashBlockedArc")]
        [SerializeField] private bool logSlashWrist;
        [Tooltip("ถืออาวุธ: ลำตัวโน้มตามมือ (องศา) — สับลงต่ำ = โน้มหน้า, ยกเหนือหัว = เอนหลังครึ่งหนึ่ง")]
        [SerializeField] private float weaponLean = 18f;
        [Tooltip("รอยแสงที่ปลายอาวุธตอนฟาดเร็ว (ว่าง = ไม่มีรอย)")]
        [SerializeField] private Material weaponTrail;
        [Tooltip("มืออยู่ต่ำกว่าที่แขนเอื้อมถึง (หยิบของบนพื้น): ย่อตัวลงได้สุดกี่ส่วนของความยาวขา ส่วนที่เหลือก้ม")]
        [SerializeField] private float reachSquat = 0.6f;
        [Tooltip("ก้มตัวได้สุดกี่องศาตอนเอื้อมลงต่ำ")]
        [SerializeField] private float reachBend = 70f;
        [Tooltip("เป้ามือไกลเกินแขน: ตัวพุ่งไปทางนั้นได้สุดกี่ส่วนของความยาวขา (เท้าอยู่ที่เดิม) — เอื้อมหยิบได้ไกลกว่าหนึ่งช่วงแขน")]
        [SerializeField] private float reachLunge = 0.45f;

        [Header("ชิ้นส่วนหลุด (ดึงกลับ: R ค้าง แบบหุ่นเดิม)")]
        [SerializeField] private IKDetach detach = new IKDetach();

        [Header("ล้ม: ragdoll บนกระดูก + กฎล้มเดียวกับหุ่นเดิม")]
        [SerializeField] private IKRagdoll ragdoll = new IKRagdoll();
        // สองเท้ายกพร้อมกันได้ 0.3 วิก่อนล้ม (หุ่นเดิม 0.15) — ผู้ใช้ขอให้ล้มยากขึ้นนิดหนึ่ง
        // ต้องขยายทั้งสองกฎ: หุ่น IK สองเท้าลอย = ไม่มีเท้าสมดุลด้วย กฎ BothFeetOffBalance จะตัดที่ 0.15 ก่อน
        [SerializeField] private FallRules fallRules = new FallRules { bothSteppingGrace = 0.3f, bothOffBalanceGrace = 0.3f };

        // ขา (เฉพาะเครื่องที่คุมขา): ก้าวเป็นเส้นโค้งแบบหุ่นเดิม — จุดเริ่ม, จุดวางเท้าที่เล็ง, ความคืบหน้าแนวราบ/ความสูง
        private Vector3[] liftFrom, stepAim, footMark;   // footMark = จุดที่เท้าจะลงถ้าคลิกตอนนี้ (โชว์เป็น ◈)
        private float[] swingT, swingLift;
        private bool[] airborne;              // เท้ายกอยู่ (ทุกเครื่องดูจากเป้า) — ใช้จับจังหวะเท้าลง
        private Vector3 lastBodyPos, leanVel;
        private Vector3[] planted;            // ที่เท้าปักล่าสุด (ทุกเครื่อง) — ทิศก้าว = เท้าที่ลอยอยู่เทียบจุดนี้
        private Vector2 lean;
        private Vector3 bodyVel;
        private float yawVel;
        private static readonly Collider[] hitBuffer = new Collider[8];
        // ดาเมจหมัด (Server): ปลายแขนเฟรมก่อน, ความเร็วพีคของท่านี้, ท่านี้โดนไปแล้ว (หนึ่งท่าหนึ่งดาเมจ)
        private Vector3[] lastTip;
        private float[] strikePeak;
        private bool[] struck;
        private Rigidbody[] strikeWeapon;     // ของที่แขนถือตอนนับหมัดเฟรมก่อน — เปลี่ยน (หยิบ/ปล่อย) = จุดที่วัดกระโดดจากกำปั้นไปปลายของ ไม่นับเฟรมนั้น
        private bool[] tracked;            // เฟรมก่อนนับหมัดได้ไหม — เฟรมแรกหลังลุก/ต่อชิ้น/เริ่มเกม ปลายแขนกระโดด ไม่ใช่การต่อย
        // เตะ (เครื่องที่คุมขา): เวลายกเข่า (-1 = ไม่ได้ยก), ทิศเตะ, เท้าเทียบสะโพกตอนเริ่มท่า, ปลายท่า, ความเร็วเท้า
        private float[] kickCharge;
        private Vector3[] kickDir, kickFrom, kickEnd;
        private float[] kickSpeed;            // ถีบ/ชักกลับ = ความเร็วเท้า, ตก = ความเร็วตก
        private bool[] kickFall;              // ถีบสุดแล้วเท้ากำลังตกลงตรงนั้น
        // จับ (Server): ของที่ยึดอยู่ + ข้อต่อบนมือ + คู่ collider ที่ปิดการชนไว้ตอนจับ (คืนเฉพาะคู่ที่เราปิดเอง)
        private Rigidbody[] heldBody;
        private List<(Collider, Collider)>[] heldIgnored;
        private readonly List<(Collider held, Collider mine, float at)> restoreLater = new List<(Collider, Collider, float)>();
        // เอื้อมหยิบ + เรืองแสง (เครื่องที่คุมแขนเท่านั้น ไม่ซิงก์ — Server จับเองเมื่อกำปั้นถึงของ)
        private Rigidbody[] reachTarget;
        private float[] reachTimer;
        private Vector3[] shoulderRest;       // ไหล่ตอนยืนตรงในแกนตัวหุ่น — วัดว่ามืออยู่ต่ำกว่าที่เอื้อมถึงแค่ไหน (ไม่นับย่อ/ก้มที่ทำไปแล้ว)
        private float spineRestHeight;        // อก (จุดก้ม) ตอนยืนตรง เหนือตัวหุ่น
        private Collider[][] handColliders;   // collider ของ _Hit แต่ละชิ้น — เป็น trigger ตอนแขนกำลังเอื้อม (ลูกกลมมือไม่ดันของที่จะหยิบหนี)
        private SlashState[] slash;            // ท่าฟัน 360° ต่อแขน (เครื่องที่คุมเท่านั้น) — ดู SlashState.cs
        private WieldState[] wield;            // ข้อมือถืออาวุธต่อแขน (ทุกเครื่อง)
        private Vector3[] heldRelPos, heldVel;   // ของที่ถือ: ตำแหน่ง/หมุนเทียบมือ (เก็บตอนจับ) + ความเร็วล่าสุด (ปล่อยแล้วพุ่งต่อ)
        private Quaternion[] heldRelRot;
        private int cueKind = -1, cueNext = -1;  // เครื่องนี้: ลูกศรบอกท่าที่เพิ่งรับ / ท่าที่จองไว้ (HUD) — >= 0 = มี
        private float cueTime, cueAngle, cueNextAngle;
        // ท่าฟัน 360°: มุมแนวฟัน (0 = ไปขวา, 90 = ขึ้น ในแกน yaw/สูง), จุดเล็ง (yaw°, สูง°), ค้างชาร์จที่ยอด
        private static Material defaultTrail;
        private bool[] supportHolding;         // มือว่างกำลังจับด้ามช่วย (SupportGrip) — นิ้วกำรอบด้ามเหมือนมือหลัก
        private float weaponCrouch, weaponCrouchGoal; // ถืออาวุธ: สะโพกลดลง (เมตร) ตอนฟาด — เท้าเป็นเป้าในโลก เข่าเลยงอเอง
        private TrailRenderer[] trails;        // รอยแสงปลายอาวุธ (ทุกเครื่องสร้างเอง ไม่ซิงก์)
        private TrailRenderer[] skids;         // รอยลากบนพื้นตอนเท้าไถลตามลมพายุ (ทุกเครื่องสร้างเองจากเป้าเท้าที่ซิงก์)
        private Vector3[] lastFoot;
        private Vector3[] lastTrailTip;
        private HashSet<Rigidbody>[] kickedProps;  // ของที่ถีบไปแล้วในท่านี้ (Server) — ถีบหนึ่งครั้งพุ่งครั้งเดียว
        private readonly HashSet<Rigidbody> stompBodies = new HashSet<Rigidbody>();
        private readonly HashSet<IDamageable> stompTargets = new HashSet<IDamageable>();
        private float[] liftHold;               // คลิกยกเท้าค้างมานานเท่าไร (เครื่องที่คุมขา) — ถึง stompHold แล้วปล่อย = กระทืบ
        private int strideCombo, strideLeg = -1; // จังหวะเดิน (ทุกเครื่องคิดจากเป้าเท้าที่ซิงก์): กี่ก้าวสลับติดกัน / ขาที่ลงล่าสุด
        private float strideAt;
        private Vector3[] landedAt;             // จุดที่เท้าลงครั้งก่อน — วัดความยาวก้าว
        private float[] throwT;                 // เครื่องที่คุมแขน: ท่าขว้างเดินไปกี่วินาที (-1 = ไม่ได้ขว้าง) / ทิศที่จะปา
        private Vector3[] throwAim;
        private Rigidbody[] dropped;            // Server: ของที่แขนนี้เพิ่งปล่อย เมื่อไร — สัญญาณขว้างมาถึงทีหลังสัญญาณปล่อยมือ ก็ยังขว้างได้
        private float[] droppedAt;
        private bool[] wasBrace;                // แขน: เฟรมก่อนยกการ์ดอยู่ไหม — จับเวลาเริ่มยก (Parry)
        private float[] blockSince;
        private IDamageable[] lastHitTarget;    // Server: แขนนี้ต่อย/ฟาดโดนอะไรล่าสุด เมื่อไร — Double Impact
        private float[] lastHitAt;
        private EnemyController[] grappled;     // Server: แขนนี้ล็อกศัตรูตัวไหนอยู่ ถึงเมื่อไร
        private float[] grappleUntil;
        private readonly Dictionary<EnemyController, float> staggered = new Dictionary<EnemyController, float>(); // Server: ศัตรูเสียหลักถึงเมื่อไร
        private float[] swingSoundAt;          // เวลาเสียงวืดล่าสุด (กันวืดรัว)
        private bool[] swingHit;               // วืดนี้ปะทะไปแล้ว
        private IKCombat.Arm[] poseArms;       // arms ที่ตัดแขนถืออาวุธออก — BodyPose ของหมัดไม่นับแขนนี้ (WeaponPose คิดแทน)
        private Rigidbody[] grabWant;       // ชิ้นที่เอื้อมไปถึงแล้ว — ให้ TryGrab ยึดชิ้นนี้ก่อนชิ้นอื่นที่บังเอิญใกล้กำปั้นกว่า (เครื่องเดียวกับ Server เท่านั้น)
        private Rigidbody glowBody;
        private readonly List<(Renderer, Material[])> glowSaved = new List<(Renderer, Material[])>();
        private static readonly Collider[] grabBuffer = new Collider[32];
        private Ray grabAim;                  // เส้นจากกล้องผ่านเคอร์เซอร์เฟรมนี้ — ชิ้นที่เคอร์เซอร์ชี้ใกล้สุดคือชิ้นที่เรือง
        private bool hasGrabAim;
        private Transform[] slots;            // ช่อง RobotLimb (มี LimbHealth) — ตามปลายแขนขา ให้ศัตรูเล็งชิ้นที่ใกล้ได้ถูก

        private IKCombat.Arm[] arms;          // ช่องเดียวกับ limbs, ขา = null
        private Transform spine;
        private Quaternion spineRest;
        private Vector3 bodyPose;
        private Vector3 modelRest;
        // เป้าเล็งของแขนที่เครื่องนี้คุม — ใช้วาดเป้าเล็ง
        private int aimArm = -1;
        private Vector3 aimPoint;
        private bool aimOnTarget, aimInReach;

        private NetworkList<LimbNetState> states;
        private Rigidbody body;
        private Robot robot;
        private int selected;
        private Vector3 bodyPos;
        private Quaternion bodyRot;

        // ล้ม: Server ตัดสินแล้วซิงก์ | สะโพกประกาศก่อน fallen ให้ client ได้ท่าสุดท้ายก่อนลุก
        private NetworkVariable<Vector3> fallHipsPos = new NetworkVariable<Vector3>();
        private NetworkVariable<Quaternion> fallHipsRot = new NetworkVariable<Quaternion>(Quaternion.identity);
        private NetworkVariable<bool> fallen = new NetworkVariable<bool>();
        private NetworkVariable<int> standVotes = new NetworkVariable<int>(); // คนที่คุมชิ้นอยู่และกด Q แล้ว — โชว์บนจอ
        // ชิ้นส่วนหลุด: สถานะจริงอยู่ที่ LimbAttachment ของแต่ละช่อง (ซิงก์เอง) | บิตละชิ้น = กำลังถูกดึงกลับ (ทุกเครื่องเล่นภาพลอยกลับ)
        private NetworkVariable<byte> pullMask = new NetworkVariable<byte>();
        private byte offlinePull;
        private LimbAttachment[] attachments;
        private bool[] attachedNow, pullHeld;
        private float[] pullTimer;            // Server: ดึงมานานเท่าไร
        private int myPull = -1;              // ชิ้นที่เครื่องนี้กำลังกด R ดึง
        private float[] seat;                 // ต่อกลับแล้ว: ชิ้นเด้งแล้วยุบเข้าที่ 0..1 (1 = เสร็จ)
        private Collider[] robotColliders;
        private bool down;              // นอนอยู่ (ragdoll คุม) — ทุกเครื่อง
        private bool votedStand;        // เครื่องนี้กด Q ขอลุกแล้ว
        private readonly HashSet<ulong> standVoters = new HashSet<ulong>(); // Server: ใครกด Q แล้ว
        private float getUp = -1f;      // กำลังลุก 0..1, -1 = ไม่ได้ลุก
        private float getUpPrev;        // getUp เฟรมก่อน (-1 = เฟรมแรกของการลุก) — จับจังหวะมือ/เท้าลงพื้น
        private Vector3[] getUpPlant;   // จุดมือยันพื้นตอนลุก (โลก) — ตั้งเฟรมแรกจากไหล่ในท่าก้ม
        private float[] getUpArmT;      // Blend: เวลาไล่ท่าของแต่ละแขน
        private int getUpLead;          // แขนที่ยันพื้นก่อน = ข้างที่ไหล่แนบพื้นกว่าตอนนอน (ลำตัวบิดไปข้างนี้)
        private Vector3 getUpStepFrom, getUpStepTo; // เท้าหลัง: ท่าย่อ → ท่ายืน
        private const int RearLeg = 3;  // ขาขวาเป็นเท้าหลังตอนลุก
        private Vector3[] legRest;      // เท้าตอนยืนในแกนตัวหุ่น — วางเท้าใหม่ตอนลุก
        private Collider bodyCollider;
        private NetworkTransform netTransform;

        private bool Online => IsSpawned;
        private bool DrivesBody => !Online || IsServer;

        private void Awake()
        {
            states = new NetworkList<LimbNetState>();
            body = GetComponent<Rigidbody>();
            robot = GetComponent<Robot>();
            bodyPos = transform.position;
            bodyRot = transform.rotation;
            orbit.FaceYaw(bodyRot.eulerAngles.y);

            float ground = GroundY(transform.position);
            modelRest = model.transform.localPosition;
            foreach (IKLimb limb in limbs)
                limb.Init(transform, ground, limb.isArm ? shoulder : hip, model);

            arms = new IKCombat.Arm[limbs.Length];
            for (int i = 0; i < limbs.Length; i++)
                if (limbs[i].isArm)
                    arms[i] = new IKCombat.Arm
                    {
                        shoulder = transform.InverseTransformPoint(limbs[i].root.position),
                        desired = limbs[i].target.localPosition,
                        world = limbs[i].target.position,
                    };
            if (combat.Enabled)
                foreach (IKLimb limb in limbs)
                    if (limb.isArm) limb.PoleOverride = new Vector3(combat.elbowPole.x * limb.Side, combat.elbowPole.y, combat.elbowPole.z);
            liftFrom = new Vector3[limbs.Length];
            stepAim = new Vector3[limbs.Length];
            footMark = new Vector3[limbs.Length];
            for (int i = 0; i < limbs.Length; i++) footMark[i] = limbs[i].target.position;
            slash = new SlashState[limbs.Length];
            wield = new WieldState[limbs.Length];
            for (int i = 0; i < limbs.Length; i++) { slash[i] = new SlashState(); wield[i] = new WieldState(); }
            swingT = new float[limbs.Length];
            swingLift = new float[limbs.Length];
            kickCharge = new float[limbs.Length];
            for (int i = 0; i < limbs.Length; i++) kickCharge[i] = -1f;
            kickDir = new Vector3[limbs.Length];
            kickFrom = new Vector3[limbs.Length];
            kickEnd = new Vector3[limbs.Length];
            kickSpeed = new float[limbs.Length];
            kickFall = new bool[limbs.Length];
            heldBody = new Rigidbody[limbs.Length];
            heldIgnored = new List<(Collider, Collider)>[limbs.Length];
            for (int i = 0; i < limbs.Length; i++) heldIgnored[i] = new List<(Collider, Collider)>();
            reachTarget = new Rigidbody[limbs.Length];
            reachTimer = new float[limbs.Length];
            heldRelPos = new Vector3[limbs.Length];
            heldVel = new Vector3[limbs.Length];
            heldRelRot = new Quaternion[limbs.Length];
            supportHolding = new bool[limbs.Length];
            trails = new TrailRenderer[limbs.Length];
            skids = new TrailRenderer[limbs.Length];
            lastFoot = new Vector3[limbs.Length];
            lastTrailTip = new Vector3[limbs.Length];
            getUpPlant = new Vector3[limbs.Length];
            getUpArmT = new float[limbs.Length];
            liftHold = new float[limbs.Length];
            wasBrace = new bool[limbs.Length];
            throwT = new float[limbs.Length];
            for (int i = 0; i < limbs.Length; i++) throwT[i] = -1f;
            throwAim = new Vector3[limbs.Length];
            dropped = new Rigidbody[limbs.Length];
            droppedAt = new float[limbs.Length];
            blockSince = new float[limbs.Length];
            lastHitTarget = new IDamageable[limbs.Length];
            lastHitAt = new float[limbs.Length];
            grappled = new EnemyController[limbs.Length];
            grappleUntil = new float[limbs.Length];
            landedAt = new Vector3[limbs.Length];
            kickedProps = new HashSet<Rigidbody>[limbs.Length];
            for (int i = 0; i < limbs.Length; i++) { kickedProps[i] = new HashSet<Rigidbody>(); landedAt[i] = limbs[i].target.position; }
            swingSoundAt = new float[limbs.Length];
            swingHit = new bool[limbs.Length];
            poseArms = new IKCombat.Arm[limbs.Length];
            handColliders = new Collider[limbs.Length][];
            for (int i = 0; i < limbs.Length; i++)
                handColliders[i] = limbs[i].hit ? limbs[i].hit.GetComponentsInChildren<Collider>() : new Collider[0];
            grabWant = new Rigidbody[limbs.Length];
            airborne = new bool[limbs.Length];
            lastBodyPos = transform.position;
            planted = new Vector3[limbs.Length];
            for (int i = 0; i < limbs.Length; i++) planted[i] = limbs[i].target.position;
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                if (t.name.EndsWith("Spine1")) { spine = t; spineRest = t.localRotation; spineRestHeight = transform.InverseTransformPoint(t.position).y; break; }

            // IK บานพับของเราเองแทน TwoBoneIK (ตัวนั้นเลือกระนาบพับเอง ทำให้ศอก/เข่าพับผิดแกนจนทะลุโมเดล)
            var rigBuilder = model.GetComponent<UnityEngine.Animations.Rigging.RigBuilder>();
            if (rigBuilder) rigBuilder.enabled = false;
            // กรอบตัดการวาดของ skinned mesh ค้างท่า T-pose — IK ย้ายมือไปที่อื่นแล้วกล้องอยู่ด้านหน้า กรอบหลุดจอ มือหายทั้งที่อยู่ในจอ
            foreach (SkinnedMeshRenderer skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.updateWhenOffscreen = true;

            bodyCollider = GetComponent<Collider>();
            netTransform = GetComponent<NetworkTransform>();
            // เท้าเทียบกึ่งกลางเท้า (ตัวหุ่นยืนตรงกึ่งกลางเท้าเสมอ) ในแกนตัวหุ่น
            Vector3 feetMid = (limbs[2].target.position + limbs[3].target.position) * 0.5f;
            legRest = new Vector3[limbs.Length];
            for (int i = 0; i < limbs.Length; i++) legRest[i] = Quaternion.Inverse(transform.rotation) * (limbs[i].target.position - feetMid);
            shoulderRest = new Vector3[limbs.Length];
            for (int i = 0; i < limbs.Length; i++) shoulderRest[i] = transform.InverseTransformPoint(limbs[i].root.position);
            var ignore = new List<Collider>(GetComponents<Collider>());
            foreach (IKLimb limb in limbs)
                if (limb.hit) ignore.AddRange(limb.hit.GetComponentsInChildren<Collider>());
            ragdoll.Build(transform, model, limbs, ignore);
            robotColliders = ignore.ToArray();
            detach.Build(model, limbs.Length, robotColliders);
            attachedNow = new bool[limbs.Length];
            pullHeld = new bool[limbs.Length];
            pullTimer = new float[limbs.Length];
            seat = new float[limbs.Length];
            lastTip = new Vector3[limbs.Length];
            strikePeak = new float[limbs.Length];
            struck = new bool[limbs.Length];
            tracked = new bool[limbs.Length];
            strikeWeapon = new Rigidbody[limbs.Length];
            for (int i = 0; i < limbs.Length; i++) seat[i] = 1f;
            for (int i = 0; i < limbs.Length; i++) attachedNow[i] = true;
            if (robot) robot.KnockDownRequested += ServerKnockDown;
        }

        // RobotLimb หา LimbAttachment ของตัวเองใน Awake ของมัน — อ่านหลังทุก Awake จบ
        private void Start()
        {
            attachments = new LimbAttachment[limbs.Length];
            slots = new Transform[limbs.Length];
            hpSeen = new float[limbs.Length];
            hpHandlers = new System.Action[limbs.Length];
            for (int i = 0; i < limbs.Length; i++)
            {
                RobotLimb slot = robot ? robot.GetLimb((LimbSlot)i) : null;
                attachments[i] = slot ? slot.Attachment : null;
                slots[i] = slot ? slot.transform : null;
                if (!slot || !slot.Health) continue;
                // ลูกบอลรับหมัดลอยตามปลายแขน/ขา แต่ไม่ได้อยู่ใต้ช่อง RobotLimb — ต้องมีตัวส่งต่อบนลูกบอลเอง
                // ไม่งั้น DamageRouter หาเลือดของชิ้นไม่เจอ (PVP: ต่อยแขนศัตรูแล้วไม่เข้า)
                if (limbs[i].hit && !limbs[i].hit.TryGetComponent(out LimbHitRelay _))
                    limbs[i].hit.gameObject.AddComponent<LimbHitRelay>().Bind(slot.Health);
                int index = i;
                slot.Health.DamageScale = info => BlockScale(index, info); // แขนนี้ยกการ์ด = ชิ้นนี้โดนเบาลง (Server เท่านั้นที่หักเลือด)
                hpSeen[i] = slot.Health.Hp;
                hpHandlers[i] = () => OnLimbHealthChanged(index);
                slot.Health.HealthChanged += hpHandlers[i];
            }
        }

        private float[] hpSeen;
        private System.Action[] hpHandlers;

        // โดนตี (ทุกเครื่อง — เลือดซิงก์): ลำตัวสะท้านหงายไปหลัง เอียงหนีฝั่งชิ้นที่โดน + เครื่องที่คุมตัวดันตัวถอยหลัง แล้วสปริงกลับเอง
        // เลือดฟื้น (เพิ่มทีละนิด) / ต่อกลับเติมเต็ม = ไม่สะท้าน
        private void OnLimbHealthChanged(int i)
        {
            LimbHealth health = robot ? robot.GetLimb((LimbSlot)i)?.Health : null;
            if (!health) return;
            float drop = hpSeen[i] - health.Hp;
            hpSeen[i] = health.Hp;
            if (drop < 1f || down || getUp >= 0f) return;
            float k = Mathf.Min(drop, 120f);
            leanVel += new Vector3(-k * hitFlinch, limbs[i].Side * k * hitFlinch * 0.6f, 0f);
            if (DrivesBody && feel.bodyFrequency > 0f)
                bodyVel -= Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized * (k * hitShove);
        }

        public override void OnDestroy()
        {
            if (robot) robot.KnockDownRequested -= ServerKnockDown;
            for (int i = 0; slots != null && i < limbs.Length; i++)
            {
                LimbHealth health = robot ? robot.GetLimb((LimbSlot)i)?.Health : null;
                if (health) health.DamageScale = null;
                if (health && hpHandlers != null && hpHandlers[i] != null) health.HealthChanged -= hpHandlers[i];
            }
            SetGlow(null); // คืนวัสดุเดิมให้ของที่เรืองค้างอยู่
            for (int i = 0; slash != null && i < slash.Length; i++) if (slash[i].guide) Destroy(slash[i].guide.gameObject);
            for (int i = 0; i < limbs.Length; i++) ClearTrail(i);
            for (int i = 0; skids != null && i < skids.Length; i++) if (skids[i]) Destroy(skids[i].gameObject);
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            EnsureStates();
            fallen.OnValueChanged += OnFallenChanged;
            if (!IsServer && fallen.Value) SetDown(true); // เข้าห้องมาตอนหุ่นนอนอยู่
        }

        public override void OnNetworkDespawn() => fallen.OnValueChanged -= OnFallenChanged;

        private void OnFallenChanged(bool was, bool now)
        {
            if (!IsServer && now != down) SetDown(now);
        }

        // เติมใหม่ทุกครั้งที่ว่าง — พบว่ารายการถูกล้างตอนแมตช์เริ่ม (หลัง OnNetworkSpawn) แล้วหุ่นจะคุมไม่ได้
        private void EnsureStates()
        {
            if (!IsServer || states.Count >= limbs.Length) return;
            states.Clear();
            foreach (IKLimb limb in limbs) states.Add(LimbNetState.Capture(limb));
        }

        /// <summary>ใครคุมชิ้นนี้ — มาจากหน้าเลือกชิ้นส่วน (LimbSelection) | ฉากไม่มีหน้าเลือกชิ้น (ฉากทดสอบ) = Host คุมชิ้นที่กด 1-4 เหมือนเล่นคนเดียว</summary>
        private ulong ControllerOf(int index)
        {
            LimbSelection selection = LimbSelection.Current;
            if (selection == null && IsServer) return index == selected ? NetworkManager.LocalClientId : LimbSelection.NoClient;
            if (selection == null || robot == null || !robot.IsSpawned) return LimbSelection.NoClient;
            return selection.GetController(robot.NetworkObjectId, (LimbSlot)index);
        }

        private bool IsMine(int index) => Online ? ControllerOf(index) == NetworkManager.LocalClientId : index == selected;

        private bool HasController(int index) => Online ? ControllerOf(index) != LimbSelection.NoClient : index == selected;

        /// <summary>
        /// หุ่นตัวนี้เป็นของผู้เล่นเครื่องนี้ไหม — ฉาก PVP มีหุ่นสองตัวที่อ้างกล้องตัวเดียวกัน
        /// ถ้าไม่กั้น ทั้งสองตัวจะสั่งกล้องทุกเฟรม (ตัวที่คำนวณทีหลังชนะ) แล้วกล้องไปค้างที่หุ่นศัตรู
        /// ยังไม่จองชิ้น = ยึดตามหุ่นที่เลือกไว้ (เลือกหุ่น = เลือกทีม) | ยังไม่เลือกอะไรเลย = หุ่นตัวแรกถือกล้องไว้
        /// </summary>
        public bool IsLocalRobot
        {
            get
            {
                if (!Online) return true;
                for (int i = 0; i < limbs.Length; i++)
                    if (IsMine(i)) return true;

                LimbSelection selection = LimbSelection.Current;
                if (selection == null || robot == null) return true;

                Robot choice = selection.GetRobotChoice(NetworkManager.LocalClientId);
                if (choice != null) return choice == robot;

                foreach (Robot other in Robot.All)
                    if (other != null && other.isActiveAndEnabled) return other == robot;
                return true;
            }
        }

        // ---------------------------------------------------------------- อ่านอย่างเดียวสำหรับ HUD (RobotArenaHud)
        public Robot Robot => robot;
        public bool IsOnline => Online;
        public int LimbCount => limbs.Length;
        public ulong LimbController(int i) => ControllerOf(i);
        public bool IsLimbMine(int i) => IsMine(i);
        public bool IsLimbAttached(int i) => Attached(i);
        public float ReattachCooldown(int i) => attachments != null && attachments[i] ? attachments[i].RemainingReconnectCooldown : 0f;
        /// <summary>นอนอยู่ (ragdoll คุม) — ทุกเครื่อง</summary>
        public bool IsDown => down;
        public bool IsGettingUp => getUp >= 0f;
        public bool LegsReady => LegsAttached();
        public bool VotedStand => votedStand;
        public int StandVotes => Online ? standVotes.Value : 0;
        public int StandVotesNeeded => Online ? Controllers(out _) : 1;

        // ================================================================ ต่อเฟรม

        private void Update()
        {
            if (!IKInput.TryRead(cam, limbs.Length, out IKInput input)) return;
            if (Online) EnsureStates();
            if (Online && states.Count < limbs.Length) return; // client: ยังไม่ได้ซิงก์รอบแรก

            // ออนไลน์: ขยับได้เมื่อแมตช์เริ่ม (Host กด Start ในหน้าเลือกชิ้นส่วน)
            bool canAct = (!Online || MatchSession.Current == null || MatchSession.Current.IsPlaying) && !down && getUp < 0f;
            if (input.pressedSlot >= 0)
            {
                if (!Online || (IsServer && LimbSelection.Current == null)) selected = input.pressedSlot;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                // ออนไลน์: ขอสลับไปคุมชิ้นนั้น (ว่างอยู่เท่านั้น) — ตัวสลับของหุ่นเดิม (LimbControlBinder) ใช้ไม่ได้เพราะหุ่นนี้ไม่มีตัวควบคุมฟิสิกส์ให้ผูก
                else if (LimbSelection.Current != null && canAct) LimbSelection.Current.DebugCycleLimbRpc(input.pressedSlot);
#endif
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (input.knockDown && canAct)
            {
                if (Online) KnockDownRpc();
                else ServerKnockDown(FallReason.KnockedDown);
            }
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (input.detachTest && canAct)
                for (int i = 0; i < limbs.Length; i++)
                    if (IsMine(i))
                    {
                        if (Online) DetachTestRpc(i);
                        else if (attachments[i]) attachments[i].ServerDetach();
                        break;
                    }
#endif
            UpdateDetach(input);

            // ล้มแล้วนอนไปเรื่อยๆ จนทุกคนที่คุมชิ้นกด Q (เล่นคนเดียว = กดเองลุกเลย) — ขาหลุดอยู่ต้องดึงกลับก่อน
            if (down && input.standUp && !votedStand && (Online || LegsAttached()))
            {
                votedStand = true;
                if (Online) StandVoteRpc();
                else StandUp();
            }

            // กล้องหมุนด้วยคลิกขวาเท่านั้น ไม่หันตามหุ่นเอง (ทุกชิ้น) — หันตาม = จุดเล็งบนพื้นเลื่อน/ผู้เล่นคุมมุมเองไม่ได้
            if (input.looking) orbit.Look(input.lookDelta);
            grabAim = input.ray;
            hasGrabAim = cam;
            if (!input.shift) orbit.Zoom(input.scroll);              // ล้อเมาส์ = ซูม | Shift + ล้อ = ดัน/ดึงมือ
            float push = input.shift ? input.scroll * depthSpeed : 0f;

            aimArm = -1;
            float remoteT = 1f - Mathf.Exp(-remoteSmoothing * Time.deltaTime);
            for (int i = 0; i < limbs.Length; i++)
            {
                IKLimb limb = limbs[i];
                if (!Attached(i)) { CancelGrab(i); continue; } // หลุดอยู่: คุมไม่ได้ (ภาพ/ดึงกลับอยู่ใน UpdateDetach) — ต่อกลับแล้วไม่จับต่อเอง
                if (down)
                {
                    CancelGrab(i); // ล้ม = ปล่อย/เลิกเอื้อม ลุกแล้วไม่จับหรือเอื้อมต่อเอง (โหมดกดครั้งเดียว)
                    if (IsMine(i))
                    {
                        DriveDown(limb, input);
                        if (Online) SendLimbRpc(i, LimbNetState.Capture(limb));
                    }
                    else if (Online)
                    {
                        states[i].ApplyTo(limb, remoteT);
                        if (!HasController(i)) limb.Pull = false; // คนคุมหลุดเน็ตกลางคลิกค้าง — ไม่ให้ชิ้นนั้นดึงค้างไปถึงตอนลุก
                    }
                    continue;
                }
                // กำลังลุก: GetUpPose คุมเท้าคนเดียว — ระบบวางเท้า/ค่าที่ซิงก์มาแตะด้วย = เท้าถูกกดลงพื้นทุกเฟรมสลับกับยกขึ้น (ติด) เสียงก้าว+ฝุ่นรัวทุกเฟรม
                if (getUp >= 0f && !limb.isArm) continue;
                // ไม่ได้คุมชิ้นนี้ = ไม่จับ (สลับไปคุมชิ้นอื่นแล้วกลับมาไม่จับต่อเอง) — ออนไลน์ชิ้นที่คนอื่นคุมได้ค่าจริงจาก ApplyTo ด้านล่าง
                if (limb.isArm && !(IsMine(i) && canAct)) CancelGrab(i);
                if (!(IsMine(i) && canAct)) limb.Brace = false; // ปล่อยชิ้น/สลับชิ้นตอนค้างปุ่ม — ไม่ค้างท่า (ชิ้นที่คนอื่นคุม ApplyTo ข้างล่างใส่ค่าจริงทับ)
                if (IsMine(i) && canAct)
                {
                    if (limb.isArm)
                    {
                        // Shift ค้าง = ยกการ์ด (ไม่ได้ถือของ) — Server ลดดาเมจ/ปัดป้อง (BlockScale) จาก Brace ที่ซิงก์
                        // โหมดแขนเก่า Shift + ล้อ = ดันมือ ไม่ใช่การ์ด | ล้มอยู่/กำลังลุก = ตั้งการ์ดไม่ได้ | หมัดกำลังพุ่ง = รอหมัดจบก่อน (เดิมยกกลางหมัดได้ มือค้างข้างหน้า)
                        limb.Brace = combat.pointAndClick && input.shift && !limb.Grab && !reachTarget[i] && getUp < 0f && !Punching(i);
                        // จะจับ = จับอยู่ หรือกำลังเอื้อมไปหยิบ | กดครั้งเดียว: กดสลับ | กดค้าง: ค้าง = จะจับ
                        bool busy = limb.Grab || reachTarget[i];
                        bool want = grabToggle ? busy ^ input.grabPressed : input.grab;
                        if (throwT[i] >= 0f) want = true; // กำลังง้าง/เหวี่ยงขว้าง — กด F ซ้ำไม่นับ ปล่อยของตอนจบท่าเอง
                        else if (!want && limb.Grab && limb.Holding) // ถือของอยู่จริงแล้วกด F (กดค้าง: ปล่อย F) = เริ่มท่าขว้างไปทางเคอร์เซอร์ | มือเปล่า = แค่เลิกจับ
                        {
                            throwAim[i] = AimThrow(i, input.ray);
                            throwT[i] = 0f;
                            want = true;
                        }
                        if (!want) CancelGrab(i);
                        else if (!busy)
                        {
                            // เพิ่งกด: มีชิ้นที่เรืองอยู่ = เอื้อมไปหยิบชิ้นนั้น (ยังไม่เปิดจับ — ไม่งั้นมือแตะของอื่นระหว่างทางแล้วหยิบผิดชิ้น)
                            // ไม่มี = เปิดจับเลย มือแตะอะไรก็จับอันนั้น (แบบเดิม)
                            reachTarget[i] = GrabCandidate(i);
                            reachTimer[i] = 0f;
                            limb.Grab = reachTarget[i] == null;
                        }
                    }
                    if (limb.isArm && ReachForGrab(i)) { } // กำลังเอื้อมไปหยิบ — ไม่ต่อย/ไม่ตามเคอร์เซอร์จนกว่าจะถึง
                    else if (limb.isArm && limb.Grab) DriveWeaponArm(i, input); // ถือของ = โหมดอาวุธ ไม่ตั้งการ์ด
                    else if (limb.isArm && limb.Brace) DriveBlock(i); // ยกการ์ดบังหน้า ไม่ต่อย
                    else if (limb.isArm && combat.pointAndClick && cam) DriveCombatArm(i, input); // หมัดต้องจบท่าแม้กำลังหันกล้อง
                    else if (!limb.isArm) DriveLeg(i, input); // ก้าวต้องจบแม้กำลังหันกล้อง
                    else if (!input.looking && cam) DriveArm(limb, input.ray, push, input.held);
                    if (!limb.isArm) WindSlide(i);
                    if (Online) SendLimbRpc(i, LimbNetState.Capture(limb));
                }
                else if (limb.isArm && !HasController(i) && SupportGrip(i)) { } // อีกมือถืออาวุธอยู่ = มือนี้จับด้ามช่วย ไม่ตั้งการ์ดหมัด
                else if (limb.isArm && combat.idleGuard && !HasController(i))
                {
                    // ไม่มีใครคุม: ทุกเครื่องยกการ์ดเอง (ท่าการ์ดอยู่ในแกนตัวหุ่น ได้ค่าเดียวกันทุกเครื่อง)
                    combat.Idle(limb, arms[i], transform, follow, Time.deltaTime);
                    limb.Grip = Mathf.MoveTowards(limb.Grip, 1f, gripSpeed * Time.deltaTime);
                }
                else
                {
                    if (arms[i] != null) combat.Release(limb, arms[i]); // ไม่ให้หมัดที่ชาร์จค้างไว้พุ่งเองตอนได้คุมคืน
                    limb.Lifted = false; // ได้คุมคืนทีหลัง = เริ่มก้าวใหม่ ไม่ต่อโค้งเก่า
                    CancelKick(i);       // ชาร์จ/ถีบค้างไม่ไปต่อเอง (เครื่องอื่นได้ค่าจริงจาก ApplyTo ข้างล่าง)
                    if (limb.isArm && !Online) limb.Grip = Mathf.MoveTowards(limb.Grip, 0f, gripSpeed * Time.deltaTime);
                    if (Online) states[i].ApplyTo(limb, remoteT);
                    if (!HasController(i)) limb.Reaching = false; // คนคุมหลุด/สลับชิ้นกลางเอื้อม — ค่าเก่าที่ซิงก์ไว้ไม่ให้ตัวหุ่นค้างท่าย่อ/ก้ม
                    if (!limb.isArm && DrivesBody && !HasController(i))
                    {
                        // คนคุมปล่อยขากลางก้าว (สลับชิ้น/หลุดเน็ต) → วางเท้าลงตรงนั้น — ค้างกลางอากาศแล้วนับเป็นเท้ายกตลอด ยกอีกข้างเมื่อไรก็ล้ม
                        bool dropped = airborne[i];
                        if (dropped) limb.target.position = OnGround(limb, limb.target.position);
                        if ((PivotIdleFoot(limb) | WindSlide(i) || dropped) && Online) states[i] = LimbNetState.Capture(limb);
                    }
                }
            }

            // จังหวะยกการ์ด (ทุกเครื่องจาก Brace ที่ซิงก์ — Server ใช้ตัดสิน Parry)
            for (int i = 0; i < limbs.Length; i++)
            {
                if (limbs[i].isArm && limbs[i].Brace && !wasBrace[i]) blockSince[i] = Time.time;
                wasBrace[i] = limbs[i].Brace;
            }

            UpdateGrabGlow();
            if (getUp >= 0f) getUp = Mathf.Min(1f, getUp + Time.deltaTime / ragdoll.getUpTime); // จบใน LateUpdate หลังไล่ท่าเฟรมสุดท้าย

            if (!down)
            {
                DetectLandings(); // นอนอยู่: เป้าเท้าขึ้นลงไม่ใช่ก้าวเท้า (ไม่เล่นเสียงกระทืบ)
                if (DrivesBody) UpdateBody();
            }
        }

        private void FixedUpdate()
        {
            if (DrivesBody)
            {
                body.isKinematic = true; // Robot.SetSimulationEnabled ปลด kinematic ตอนเริ่มแมตช์ — ตัวนี้เดินด้วยสคริปต์
                body.MovePosition(bodyPos);
                body.MoveRotation(bodyRot);
            }
            ragdoll.Enforce(down, !DrivesBody);
            if (down)
            {
                // ทุกเครื่องออกแรงเอง: client เห็นแขนขาที่ตัวเองคุมขยับทันที | ตัวหุ่นเคลื่อนจริงตามฟิสิกส์ของ Server
                foreach (IKLimb limb in limbs)
                    if (limb.Pull) ragdoll.Pull(limb, limb.target.position);
                FixedDown();
            }
            else if (DrivesBody && getUp < 0f && (!Online || MatchSession.Current == null || MatchSession.Current.IsPlaying)) EvaluateFall();
            for (int i = 0; i < limbs.Length; i++)
            {
                IKLimb limb = limbs[i];
                if (!limb.hit) continue;
                // กำลังเอื้อมไปหยิบ (ซิงก์ ทุกเครื่องเห็นเท่ากัน) = มือทะลุของได้ — ไม่งั้นลูกกลมมือดันของหนีก่อนถึงระยะจับ หยิบพลาด
                foreach (Collider c in handColliders[i])
                    if (c.isTrigger != limb.Reaching) c.isTrigger = limb.Reaching;
                limb.hit.MovePosition(limb.tip.position);
                limb.hit.MoveRotation(limb.tip.rotation); // ของที่ถือ (ข้อต่ออยู่บนนี้) หมุนตามข้อมือ
            }
            if (DrivesBody) { ServerGrabs(); PlaceHeld(true); RestoreCollisions(); ServerStaggers(); }
        }

        private void LateUpdate()
        {
            if (down)
            {
                // ragdoll คุมกระดูก — ไม่แก้ IK, กล้องตามสะโพก (ตัวหุ่นยังอยู่ที่เดิมจนกว่าจะลุก)
                foreach (IKLimb limb in limbs) limb.ApplyFingers();
                for (int i = 0; i < limbs.Length; i++)
                {
                    ClearTrail(i);
                    // ช่อง RobotLimb ตามปลายแขนขาตอนนอนด้วย — ไม่งั้นค้างที่เดิม ศัตรูวัดระยะถึงชิ้นแล้ววืดทั้งที่ตีโดนตัว
                    if (slots != null && slots[i] && Attached(i)) slots[i].position = limbs[i].tip.position;
                }
                lastBodyPos = transform.position;
                Vector3 hips = ragdoll.Hips.transform.position;
                if (cam && IsLocalRobot) orbit.Apply(cam, new Vector3(hips.x, transform.position.y, hips.z), 0f, true);
                return;
            }
            if (getUp >= 0f)
            {
                ragdoll.RestoreRest();
                lastBodyPos = transform.position; // ตัวหุ่นเพิ่ง Teleport ไปที่สะโพก — ไม่ให้นับเป็นความเร็วแล้วลำตัวโน้มกระตุก
            }

            // ลำตัวบิด/โน้มตามหมัด + เอียงตามทิศที่ตัวเคลื่อน ก่อนแก้ IK (ไหล่ขยับตามลำตัว แล้ว IK พามือไปถึงเป้าเหมือนเดิม)
            UpdateLean();
            if (spine && (combat.Enabled || feel.leanPerSpeed > 0f))
            {
                // แขนยกการ์ดไม่นับเป็นหมัด — เดิมนับ มือการ์ดที่ยื่นไปหน้า = หมัดยื่น ลำตัวบิด/โน้มไปหน้า ไหล่ขยับ มือการ์ดยิ่งไปหน้า (วงป้อนกลับ 2026-11-04)
                for (int i = 0; i < limbs.Length; i++) poseArms[i] = limbs[i].Grab || limbs[i].Brace ? null : arms[i];
                Vector3 pose = (combat.Enabled ? combat.BodyPose(limbs, poseArms) : Vector3.zero) + WeaponPose();
                bodyPose = Vector3.Lerp(bodyPose, pose, 1f - Mathf.Exp(-combat.bodySmoothing * Time.deltaTime));
                weaponCrouch = Mathf.Lerp(weaponCrouch, weaponCrouchGoal, 1f - Mathf.Exp(-combat.bodySmoothing * Time.deltaTime));
                model.transform.localPosition = modelRest + Vector3.forward * bodyPose.z + Vector3.down * weaponCrouch; // ตัวพุ่งตามหมัด/ย่อตอนฟาด (เท้าเป็นเป้าในโลก จึงอยู่ที่เดิม เข่างอเอง)
                combat.torsoTurn = Quaternion.AngleAxis(bodyPose.x, Vector3.up) * Quaternion.AngleAxis(bodyPose.y + lean.x, Vector3.right)
                                 * Quaternion.AngleAxis(lean.y, Vector3.forward);
                spine.localRotation = spineRest;
                spine.rotation = Quaternion.AngleAxis(bodyPose.x, transform.up)
                               * Quaternion.AngleAxis(bodyPose.y + lean.x, transform.right)
                               * Quaternion.AngleAxis(lean.y, transform.forward) * spine.rotation;
            }
            if (getUp >= 0f) GetUpPose(getUp);
            for (int i = 0; i < limbs.Length; i++)
            {
                IKLimb limb = limbs[i];
                if (!Attached(i)) { ClearTrail(i); continue; } // ซ่อนอยู่ (กระดูกย่อ) — แก้ IK แล้วทิศเป็นศูนย์
                // ขา: เข่าชี้ตามทิศเท้า (ไม่ใช่ทิศลำตัว) — เท้าเฉียงแล้วขาไม่บิดที่ข้อเท้า
                if (limb.isArm && combat.Enabled) limb.PoleOverride = ArmPole(limb); // ทุกเครื่องเลือกเองจาก Brace ที่ซิงก์ — ท่าการ์ดเห็นเหมือนกัน
                limb.Solve(limb.isArm ? transform.rotation : Quaternion.Euler(0f, FootYaw(limb), 0f));
                limb.Holding = limb.isArm && ((limb.Grab && !limb.Reaching) || (supportHolding[i] && !HasController(i))) ? (heldBody[i] ? heldBody[i].GetComponentInChildren<Collider>() : InHand(limb)) : null;
                if (limb.isArm)
                {
                    if (combat.Enabled && limb.Brace && !limb.Grab) // ยกการ์ด: ฝ่ามือหันเข้าตัว หลังมือ/หลังแขนท่อนล่างหันออกรับหมัด
                        limb.OrientHand(transform.rotation, limb.tip.position - limb.mid.position, transform.rotation * Vector3.back);
                    else if (combat.Enabled && !limb.Grab) // กำปั้นตรงแนวแขน ฝ่ามือหันหน้า-เข้าใน (ตั้งการ์ด) / คว่ำลง (ตอนเหยียดหมัด)
                        limb.OrientHand(transform.rotation, limb.tip.position - limb.mid.position,
                                        transform.rotation * new Vector3(-limb.Side, -0.4f, 0f)); // ฝ่ามือหันเข้าหากัน สันหมัดชี้หน้า
                    else if (limb.Grab) AimWeapon(limb);
                    else limb.OrientHand(transform.rotation);
                    limb.tip.rotation = limb.target.rotation;
                }
                limb.ApplyFingers();
                if (limb.isArm) UpdateTrail(i, limb);
                else UpdateSkid(i, limb);
                if (slots != null && slots[i]) slots[i].position = limb.tip.position;
            }
            if (DrivesBody) { PlaceHeld(false); ServerStrikes(); }
            if (getUp >= 0f)
            {
                // ท่านอน → ท่ายันมือ เท่านั้น ที่เหลือ IK พาลุกเอง (มือ/เท้าปักพื้นจริง) | มือนำถึงพื้นก่อน อีกมือตามมา
                for (int i = 0; i < limbs.Length; i++)
                    getUpArmT[i] = Mathf.SmoothStep(0f, 1f, i == getUpLead ? getUp / GetUpLeadAt : Mathf.InverseLerp(GetUpLeadAt * 0.5f, GetUpPlantAt, getUp));
                ragdoll.Blend(Mathf.SmoothStep(0f, 1f, getUp / GetUpPlantAt), getUpArmT);
                getUpPrev = getUp;
                if (getUp >= 1f)
                {
                    getUp = -1f;
                    if (bodyCollider) bodyCollider.enabled = true;
                    fallRules.Reset(); // ลุกปุ๊บไม่ล้มซ้ำจากตัวจับเวลาเก่า
                }
            }
            if (cam && IsLocalRobot) orbit.Apply(cam, transform.position, ControlledArmSide());
        }

        // ================================================================ เคอร์เซอร์ → เป้า

        // มือไปตรงที่เคอร์เซอร์ชี้: ระนาบหันหากล้อง ห่างไหล่ไปข้างหน้า Depth แล้วจำกัดตามข้อต่อ
        private void DriveArm(IKLimb arm, Ray ray, float push, bool held)
        {
            arm.Depth = Mathf.Clamp(arm.Depth + push * arm.Reach, arm.Reach * 0.2f, arm.Reach);
            var plane = new Plane(cam.transform.forward, arm.root.position + transform.forward * arm.Depth);
            if (plane.Raycast(ray, out float enter))
            {
                // เลยขอบไหล่ → ติดขอบฝั่งที่แขนอยู่ ไม่กระโดดไปขอบอีกฝั่ง
                float yawNow = arm.Limits.AnglesOf(arm, transform.rotation, arm.target.position).x;
                Vector3 goal = arm.Limits.Limit(arm, transform.rotation, ray.GetPoint(enter), yawNow);
                arm.target.position = Vector3.Lerp(arm.target.position, goal, 1f - Mathf.Exp(-follow * GrabLag(arm) * Time.deltaTime));
            }
            arm.Grip = Mathf.MoveTowards(arm.Grip, held ? 1f : 0f, gripSpeed * Time.deltaTime);
        }

        // ท่าพักถืออาวุธ (Pflug): ด้ามหน้าสะโพก ศอกงอ ใบชี้ขึ้นไปข้างหน้า ~45° (AimWeapon)
        private static Vector3 WeaponRestDir(float side) => new Vector3(side * 0.2f, -0.75f, 0.6f).normalized;
        private const float WeaponRestRadius = 0.68f; // ศอกงอ — แขนเหยียดตอนพัก = ท่าแข็ง
        // ทิศใบดาบตอนพัก (Pflug: ใบชี้ขึ้นไปข้างหน้า ~45°) — ท่าพักกับช่วงดึงดาบกลับหลังฟันใช้ตัวเดียวกัน
        private Vector3 WeaponRestBlade(float side) => transform.rotation * new Vector3(side * 0.1f, 0.7f, 0.7f).normalized;
        /// <summary>ท่าพักเป็นมุม (yaw°, สูง°) เทียบตัวหุ่น — ปลายท่าฟันไล่กลับมาที่นี่</summary>
        private static Vector2 RestAngles(float side)
        {
            Vector3 d = WeaponRestDir(side);
            return new Vector2(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg);
        }

        // โหมดอาวุธ (ถือของ): ไม่ตั้งการ์ด/ไม่ชาร์จหมัด
        // ปล่อยเมาส์ = ถือเตรียมข้างหน้า-ข้างตัว (เห็นชัดในมุมกล้อง) | คลิกซ้ายค้าง + ลากเมาส์ = หมุนแขนรอบไหล่ตามทิศที่ลาก
        // ลากเร็ว = ฟาดเร็ว (ของหนักตามช้าลงตาม GrabLag) — ดาเมจจากความเร็วปลายของใน ServerStrikes, ปลายอาวุธหันตามแขน (AimWeapon)
        private void DriveWeaponArm(int index, IKInput input)
        {
            IKLimb arm = limbs[index];
            if (arms[index] != null) combat.Release(arm, arms[index]);
            arm.Grip = Mathf.MoveTowards(arm.Grip, 1f, gripSpeed * Time.deltaTime);
            if (throwT[index] >= 0f) { DriveThrow(index); return; }
            if (grabWant[index] != null) return;
            if (input.looking && slash[index].kind < 0) return;
            Vector3 shoulder = arm.root.position;
            Vector3 dir = WeaponRestDir(arm.Side);
            float radius = arm.Reach * WeaponRestRadius;
            // กลับท่าพักหลังฟันช้ากว่าตอนง้าง — ใช้ follow เต็ม แขนดีดกลับเร็วกว่าตอนฟัน (วัดได้ 11 เท่าเกณฑ์ต่อย) ดูเบา + วืดซ้ำ
            float rate = (weaponFreeAim ? follow : Mathf.Min(follow, 8f / slashWeight)) * GrabLag(arm);
            if (!weaponFreeAim && SlashStep(index, arm, input, out Vector3 slashDir, out float slashRate, out float slashRadius))
            {
                dir = slashDir; rate = slashRate; radius = arm.Reach * slashRadius;
            }
            else if (weaponFreeAim && input.held)
            {
                if (input.pressed) wield[index].swingDir = Quaternion.Inverse(transform.rotation) * (arm.target.position - shoulder).normalized; // เริ่มจากท่าตอนนี้ ไม่กระโดด
                // ลากเมาส์ = เลื่อนปลายแขนบนผิวทรงกลมรอบไหล่ ไปทางเดียวกับที่ลาก: ขึ้น = ไปทางหัว, ขวา = ไปทางขวาตัวหุ่น
                // แขนห้อย (ใกล้ขั้วล่าง) "ขึ้น" = ยกไปข้างหน้า — ไม่งั้นแขนที่ห้อยเอียงออกข้างนิดเดียวลากขึ้นแล้วกางออกข้างแทน
                Vector3 d = wield[index].swingDir;
                Vector3 up = Vector3.ProjectOnPlane(Vector3.up + Vector3.forward * (Mathf.Clamp01(-d.y) * 1.5f), d);
                up = up.sqrMagnitude > 1e-6f ? up.normalized : Vector3.forward;
                d = (d + (Vector3.Cross(up, d) * input.lookDelta.x + up * input.lookDelta.y) * (weaponSwingPerPixel * Mathf.Deg2Rad)).normalized;
                d.y = Mathf.Clamp(d.y, -0.96f, 0.96f); // ไม่ข้ามขั้วบน/ล่าง (ทิศ "ขวา" กลับด้านตรงนั้น)
                Vector3 flat = new Vector3(d.x, 0f, d.z);
                dir = (flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward) * Mathf.Sqrt(1f - d.y * d.y) + Vector3.up * d.y;
                radius = arm.Reach * 0.92f;
            }
            float yawNow = arm.Limits.AnglesOf(arm, transform.rotation, arm.target.position).x;
            Vector3 goal = arm.Limits.Limit(arm, transform.rotation, shoulder + transform.rotation * dir * radius, yawNow);
            wield[index].swingDir = Quaternion.Inverse(transform.rotation) * (goal - shoulder).normalized; // ติดข้อต่อ = ค้างที่ขอบ ลากกลับมาได้ทันที
            // ตามเป้าเป็นวงโค้งรอบไหล่ (ไม่ใช่เส้นตรง — ฟาดเร็วแล้วมือตัดเข้าหาไหล่ วงหด ดูอ่อน)
            arm.target.position = shoulder + Vector3.Slerp(arm.target.position - shoulder, goal - shoulder, 1f - Mathf.Exp(-rate * Time.deltaTime));
        }

        // โหมดต่อสู้: เคอร์เซอร์เล็งของใกล้ตัว (ตัวหุ่นอยู่ layer Ignore Raycast ยิงไม่โดนตัวเอง)
        // ไม่มีอะไรใกล้ = เล็งบนระนาบหน้าไหล่ระยะสุดแขน (ไม่งั้นหมัดพุ่งลงไปหาพื้นไกลๆ) | มือกำตลอด
        private void DriveCombatArm(int index, IKInput input)
        {
            IKLimb arm = limbs[index];
            if (!input.looking)
            {
                bool near = Physics.Raycast(input.ray, out RaycastHit hit, 1000f)
                            && Vector3.Distance(hit.point, arm.root.position) <= arm.Reach * 1.5f;
                var plane = new Plane(cam.transform.forward, arm.root.position + transform.forward * arm.Reach);
                if (near) aimPoint = hit.point;
                else if (plane.Raycast(input.ray, out float enter)) aimPoint = input.ray.GetPoint(enter);
                aimOnTarget = near && hit.rigidbody && !hit.rigidbody.isKinematic;
                aimInReach = !near || Vector3.Distance(hit.point, arm.root.position) <= arm.Reach;
            }
            aimArm = index;
            IKCombat.Arm a = arms[index];
            combat.Drive(arm, a, transform, aimPoint, input.held, follow, Time.deltaTime * GrabLag(arm)); // ถือของหนัก = ทุกจังหวะของแขนช้าลง
            arm.Grip = Mathf.MoveTowards(arm.Grip, 1f, gripSpeed * Time.deltaTime);
            if (feel.hitFeel && a.state == IKCombat.State.Punching && !a.hit) TryHit(arm, a);
        }

        // กำปั้นแตะของที่มี Rigidbody (ไม่นับตัวหุ่นเอง/พื้น) ครั้งแรกของหมัดนี้ → หยุดภาพ จอสั่น ประกายไฟ เสียง
        // เอฟเฟกต์เล่นเฉพาะเครื่องที่คุมแขน — แรงผลักของจริงมาจาก collider มือ (_Hit) ที่ server
        private void TryHit(IKLimb arm, IKCombat.Arm a)
        {
            Vector3 fist = arm.tip.position;
            int n = Physics.OverlapSphereNonAlloc(fist, feel.hitRadius, hitBuffer, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = hitBuffer[i];
                if (c.attachedRigidbody == null || c.transform.IsChildOf(transform)) continue;
                a.hit = true;
                Vector3 point = Closest(c, fist);
                Vector3 normal = fist - point;
                ImpactFeel.PlayHit(point, normal.sqrMagnitude > 1e-6f ? normal.normalized : -transform.forward, Mathf.Lerp(10f, 40f, a.charge));
                return;
            }
        }

        // ทุกเครื่อง: เป้าเท้าลงจากที่ยกไว้ถึงพื้น = เท้ากระแทก → เสียง ฝุ่น จอสั่น + ตัวยวบ (เครื่องที่คุมตัวหุ่น)
        private void DetectLandings()
        {
            for (int i = 0; i < limbs.Length; i++)
            {
                IKLimb leg = limbs[i];
                if (leg.isArm) continue;
                Vector3 p = leg.target.position;
                float ground = GroundY(p);
                bool up = p.y > ground + leg.RestHeight + 0.3f;
                if (!up) planted[i] = p;
                if (airborne[i] && !up)
                {
                    if (feel.footstepFeel) ImpactFeel.PlayFootstep(new Vector3(p.x, ground, p.z), 1f);
                    if (DrivesBody && feel.bodyFrequency > 0f) bodyVel.y -= feel.landingDip; // ยวบได้เฉพาะตอนตัวเป็นสปริง
                    CountStride(i, leg, p);
                }
                airborne[i] = up;
            }
            if (strideCombo > 0 && Time.time - strideAt > strideWindow.y) strideCombo = 0; // หยุดเดินนานเกิน = หมดจังหวะ
        }

        // จังหวะเดิน: เท้าลงสลับข้างกับก้าวก่อน ห่างกันอยู่ใน strideWindow และก้าวยาวเกิน 20% ของขา = ติดจังหวะ +1 (สูงสุด 3)
        // ขาเดิมซ้ำ/เร็วไป/ช้าไป/ก้าวสั้น = เริ่มนับใหม่ | ทุกเครื่องคิดเองจากเป้าเท้าที่ซิงก์ (ก้าวเร็วขึ้นที่เครื่องคนคุม, ตัวไหลแรงขึ้นที่ Server)
        private void CountStride(int i, IKLimb leg, Vector3 p)
        {
            float gap = Time.time - strideAt;
            bool good = strideLeg >= 0 && strideLeg != i && gap >= strideWindow.x && gap <= strideWindow.y
                        && Vector3.ProjectOnPlane(p - landedAt[i], Vector3.up).magnitude >= leg.Reach * 0.2f;
            strideCombo = good ? Mathf.Min(strideCombo + 1, 3) : 0;
            strideLeg = i;
            strideAt = Time.time;
            landedAt[i] = p;
        }

        // ทุกเครื่อง: ความเร็วตัวหุ่นจากตำแหน่งจริง (ซิงก์ด้วย NetworkTransform) → ลำตัวโน้มตามทิศเดินแบบสปริง ไม่แข็งทื่อ
        private void UpdateLean()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector3 v = transform.InverseTransformDirection((transform.position - lastBodyPos) / dt);
            lastBodyPos = transform.position;
            Vector3 goal = new Vector3(Mathf.Clamp(v.z * feel.leanPerSpeed, -feel.maxLean, feel.maxLean),
                                       Mathf.Clamp(-v.x * feel.leanPerSpeed, -feel.maxLean, feel.maxLean), 0f);
            // ถีบ: ลำตัวเอนหนีเท้าที่ยกสูง (แบบเตะสูงคาราเต้) — คิดจากเป้าเท้าที่ซิงก์ ทุกเครื่องได้ท่าเดียวกัน
            for (int i = 0; i < limbs.Length; i++)
            {
                IKLimb leg = limbs[i];
                if (leg.isArm || !leg.KickPose) continue;
                Vector3 d = transform.InverseTransformDirection(leg.target.position - leg.root.position);
                Vector2 away = new Vector2(d.x, d.z).normalized;
                float raise = kickLean * Mathf.Clamp01((leg.target.position.y - planted[i].y) / leg.Reach);
                goal.x -= away.y * raise; // ถีบไปหน้า = เอนหลัง
                goal.y += away.x * raise; // ถีบไปขวา = เอนซ้าย
            }
            // พายุทราย: เอนสู้ลม + โยกตามลมกระโชก — เดินสวนลมโยกมากขึ้น (ทุกเครื่องคิดเองจากลมในฉาก + ความเร็วตัวหุ่นที่ซิงก์)
            Sandstorm storm = Sandstorm.Current;
            if (storm && storm.Intensity > 0f)
            {
                Vector3 w = transform.InverseTransformDirection(storm.Wind);
                goal.x -= w.z * storm.lean; // ลมพัดจากหลัง = เอนหลังสู้ | ลมปะทะหน้า = เอนไปหน้า
                goal.y += w.x * storm.lean; // ลมพัดไปขวา = เอนซ้ายสู้
                Vector2 move = new Vector2(v.x, v.z), wind = new Vector2(w.x, w.z);
                float into = Mathf.Clamp01(move.magnitude / 3f) * Mathf.Clamp01(-Vector2.Dot(move.normalized, wind.normalized));
                float sway = storm.sway * wind.magnitude * (1f + into);
                goal.x += sway * (Mathf.PerlinNoise(Time.time * 1.1f, 3.1f) * 2f - 1f);
                goal.y += sway * (Mathf.PerlinNoise(Time.time * 0.9f, 7.7f) * 2f - 1f);
            }
            // ย่อแล้วมือยังไม่ถึงเป้าข้างล่าง = ก้มลงไปหา (คิดจากไหล่ตอนไม่ก้มเทียบตัวหุ่นตอนนี้ — ตัวหุ่น/เป้ามือซิงก์อยู่แล้ว ทุกเครื่องก้มเท่ากัน)
            // เฉพาะแขนที่กำลังเอื้อมไปหยิบ (Reaching ซิงก์) — หมัด/เคอร์เซอร์ไม่ทำให้ก้ม | + เป้าไกลเกินแขน = ก้มยื่นตัว: ไหล่ไปหน้า = อก→ไหล่ × sin θ
            // ก้มเอียงไปทางที่ของอยู่ (หน้า = ก้ม, ข้าง = เอียงตัว) — ของอยู่ข้างๆ ก้มไปหน้าอย่างเดียวไหล่ไม่ได้เข้าใกล้
            float stoop = 0f, chest = ChestToShoulder;
            Vector2 toward = Vector2.up;
            for (int i = 0; i < limbs.Length; i++)
            {
                if (!limbs[i].isArm || !limbs[i].Reaching || !Attached(i)) continue;
                Vector3 shoulder = transform.TransformPoint(shoulderRest[i]), target = limbs[i].target.position;
                float below = shoulder.y - target.y - limbs[i].Reach * 0.95f;
                float beyond = (target - shoulder).magnitude - limbs[i].Reach * 0.95f;
                float s = Mathf.Max(Mathf.Acos(Mathf.Clamp(1f - below / chest, -1f, 1f)), Mathf.Asin(Mathf.Clamp01(beyond / chest))) * Mathf.Rad2Deg; // ไหล่ลง = อก→ไหล่ × (1 − cos θ) | ไหล่ยื่น = × sin θ
                if (s <= stoop) continue;
                stoop = s;
                Vector3 d = transform.InverseTransformDirection(target - shoulder);
                toward = new Vector2(d.x, Mathf.Max(0f, d.z)); // ไม่ก้มไปข้างหลัง
                toward = toward.sqrMagnitude > 1e-4f ? toward.normalized : Vector2.up;
            }
            stoop = Mathf.Min(stoop, reachBend);
            goal.x += stoop * toward.y;  // ก้มไปหน้า
            goal.y -= stoop * toward.x;  // ของอยู่ขวา = เอียงขวา (lean.y บวก = เอียงซ้าย)
            Vector3 now = lean;
            IKFeel.Spring(ref now, ref leanVel, goal, 2f, 0.5f, dt);
            lean = now;
        }

        // คุมแขนในโหมดต่อสู้อยู่ → ฝั่งของแขน (-1/+1) ให้กล้องเลื่อนไปทางไหล่นั้น | ไม่ได้คุมแขน = 0
        private float ControlledArmSide()
        {
            if (!combat.pointAndClick) return 0f;
            for (int i = 0; i < limbs.Length; i++)
                if (limbs[i].isArm && IsMine(i)) return limbs[i].Side;
            return 0f;
        }

        // หุ่นเดิม (LegInput + LegController): เคอร์เซอร์ชี้พื้น = จุดวางเท้า (แนวราบจากสะโพกไม่เกิน 90% ของขา)
        // กดคลิก = ก้าวเป็นเส้นโค้งไปที่นั่น — คลิกสั้น = ก้าวครบโค้งแล้วปักเอง | กดค้าง = ลอยค้างที่ยอดโค้งตามเคอร์เซอร์ ปล่อยแล้วค่อยลง
        // แนวราบ ease in-out, ความสูง sin ¼ รอบ = ยกเร็วชะลอที่ยอด แล้วเร่งกระแทกพื้นเหมือนของหนัก | คลิกขวาหันกล้อง = จุดเล็งค้างไว้
        private void DriveLeg(int i, IKInput input)
        {
            IKLimb leg = limbs[i];
            // จุดเล็งตามเมาส์ทุกเฟรม (◈ บอกว่าคลิกแล้วเท้าจะลงตรงไหน) — คลิกขวาหันกล้อง = จุดเล็งค้าง (หุ่นเดิม UpdatePointAim)
            if (!input.looking && cam && FirstWalkable(input.ray, 1000f, out RaycastHit hit)) footMark[i] = ClampStep(leg, hit.point); // ชี้โดนมือตัวเอง/ศัตรู = ทะลุไปเล็งพื้นข้างหลัง
            leg.Brace = input.grab && !leg.Lifted && !leg.KickPose; // F ค้าง (เท้าติดพื้น) = ย่อ — ระหว่างก้าว/เตะกดไม่ติด
            if (leg.Brace) return; // ย่ออยู่ = เท้าปักแน่น ไม่ก้าว/ไม่เตะ
            // ส่งเป้าเฉพาะตอนกดค้าง (หุ่นเดิม) — ปล่อยแล้วเท้าลงตรงจุดสุดท้าย ไม่ไหลตามเมาส์
            if (DriveKick(i, leg, input, Mathf.Min(Time.deltaTime, 0.05f))) return;
            if (input.held) stepAim[i] = footMark[i];
            if (input.held && !leg.Lifted)
            {
                liftFrom[i] = leg.target.position;
                swingT[i] = 0f;
                swingLift[i] = 0f;
                liftHold[i] = 0f;
                leg.Lifted = true;
            }
            if (!leg.Lifted) return;

            float dt = Mathf.Min(Time.deltaTime, 0.05f); // เฟรมกระตุกยาวไม่กระโดดข้ามช่วงยก (เท้าจะไถลไปกับพื้น)
            if (input.held) liftHold[i] += dt;
            float swingTime = stepSwingTime / (1f + strideCombo * strideBonus); // เดินติดจังหวะ = ก้าวเร็วขึ้น
            swingT[i] = Mathf.MoveTowards(swingT[i], 1f, dt / swingTime);
            bool rising = input.held || swingT[i] < 0.5f;
            swingLift[i] = Mathf.MoveTowards(swingLift[i], rising ? 1f : 0f, dt / (swingTime * 0.5f));
            if (!input.held && swingT[i] >= 1f && swingLift[i] <= 0f)
            {
                // เท้าถึงพื้น: ปักตรงจุดที่เล็ง หันตามก้าว (DetectLandings เล่นเสียง/ฝุ่น/จอสั่น)
                leg.target.SetPositionAndRotation(OnGround(leg, stepAim[i]), Quaternion.Euler(0f, LandingYaw(i, leg), 0f) * leg.RestRotation);
                leg.Lifted = false;
                if (liftHold[i] >= stompHold) Stomp(i, leg.target.position); // ยกค้างนานแล้วปล่อย = กระทืบ
                liftHold[i] = 0f; // ก้าวนี้จบ — ถีบเสร็จแล้วเท้าลงทางนี้ต้องไม่กระทืบด้วยค่าเก่า
                return;
            }
            Vector3 flat = Vector3.Lerp(liftFrom[i], stepAim[i], Mathf.SmoothStep(0f, 1f, swingT[i]));
            leg.target.position = OnGround(leg, flat) + Vector3.up * (Mathf.Sin(swingLift[i] * Mathf.PI * 0.5f) * leg.Reach * stepArcHeight);
            // ลอยอยู่: เท้าค่อยๆ หมุนกลับชี้ทิศลำตัว (หุ่นเดิม) — หันตามก้าวตอนวางลง
            leg.target.rotation = Quaternion.Slerp(leg.target.rotation, transform.rotation * leg.RestRotation, 1f - Mathf.Exp(-feel.footTurnSpeed * dt));
        }

        // ถีบ (ผู้ใช้ 2026-10-28: เตะแนวพื้นแบบเดิมดูทำอะไรใครไม่ได้ → เปลี่ยนเป็นถีบ)
        // กด Shift (ผู้ใช้ 2026-10-28 "กดแล้วถีบเลย" + "เอาแบบบาทาลูบพักตร์"): ยกเข่า kickChamberTime วิ แล้วถีบขาเหยียดชี้ขึ้น kickHighAngle
        // ไปทาง ◈ เร็ว kickThrustSpeed × แรงที่เหลือของขา, ลำตัวเอนหนีเท้า (UpdateLean)
        // ยืดขาสุดแล้วเท้าตกลงตรงที่อยู่ (ผู้ใช้ขอ) — ใต้เท้ามีของสูงกว่าเท้า (ศัตรู/ตึก) = ชักกลับวางที่เดิม ไม่ขึ้นไปยืนบนนั้น
        // true = ท่าถีบคุมเท้าเฟรมนี้ (ไม่ก้าว)
        // ซิงก์: leg.KickPose = อยู่ในท่า (ตัวหุ่นไม่ไหลตามเท้านี้), leg.Kick = ช่วงพุ่งออก (Server นับดาเมจเฉพาะช่วงนี้)
        private bool DriveKick(int i, IKLimb leg, IKInput input, float dt)
        {
            Vector3 hip = leg.root.position;
            if (kickCharge[i] < 0f && !leg.KickPose)
            {
                if (!input.kick) return false;
                kickCharge[i] = 0f;
                liftHold[i] = 0f; // ถีบกลางก้าวที่ยกค้างอยู่ — ไม่นับเป็นกระทืบตอนเท้าลง
                kickDir[i] = KickAim(i, leg, hip);
                kickFrom[i] = leg.target.position - hip; // รวมความสูง — กดกลางก้าว (เท้าลอย) ยกเข่าต่อจากตรงนั้น ไม่วาร์ปลงพื้นก่อน
                leg.Lifted = leg.KickPose = true;
            }
            // จุดเริ่มท่า ยกเท่ายอดโค้งก้าว — ชักกลับมาตรงนี้แล้วต่อช่วงลงของก้าวได้ไม่กระตุก
            Vector3 home = OnGround(leg, hip + kickFrom[i]) + Vector3.up * (leg.Reach * stepArcHeight);
            // ปลายเท้าเชิดตามมุมขา ไม่เกิน 85° — เกินแล้วมุม yaw ของเท้ากลับด้าน เข่าชี้ผิดทาง
            Quaternion sole = Quaternion.AngleAxis(-Mathf.Min(kickSolePitch + kickHighAngle, 85f), Vector3.Cross(Vector3.up, kickDir[i]))
                            * Quaternion.LookRotation(kickDir[i]) * leg.RestRotation;
            if (kickCharge[i] < 0f && kickFall[i])
            {
                // ตก: เร่งลงตรงๆ จนถึงพื้น แล้วปักแบบก้าว (DetectLandings เล่นเสียง/ฝุ่น/จอสั่น) หันตามครึ่งมุมก้าวเหมือนก้าวปกติ
                kickSpeed[i] += kickFallGravity * dt;
                Vector3 land = OnGround(leg, ClampStep(leg, leg.target.position));
                Quaternion flat = Quaternion.Euler(0f, transform.eulerAngles.y + StepTurn(leg, planted[i], land), 0f) * leg.RestRotation;
                // เช็กถึงพื้นจากค่าที่คำนวณ ไม่อ่าน target กลับ — เขียน world แล้วอ่านกลับผ่าน parent ที่เอียงคลาดได้ ~1e-5
                // หุ่นยืนนิ่ง (ถีบแพนด้า) parent ไม่ขยับ = คลาดเท่าเดิมทุกเฟรม ไม่มีวันถึง ท่าเตะค้างตลอด
                float y = Mathf.Max(land.y, leg.target.position.y - kickSpeed[i] * dt);
                leg.target.position = new Vector3(land.x, y, land.z);
                leg.target.rotation = Quaternion.Slerp(leg.target.rotation, flat, 1f - Mathf.Exp(-12f * dt));
                if (y > land.y) return true;
                leg.target.rotation = flat;
                CancelKick(i);
                leg.Lifted = false;
                return true;
            }
            if (kickCharge[i] < 0f)
            {
                if (!leg.Kick) kickEnd[i] = home; // ชักกลับ: ตัวหุ่นขยับระหว่างนั้น — ตามจุดเริ่มท่า
                Vector3 p = Vector3.MoveTowards(leg.target.position, kickEnd[i], kickSpeed[i] * dt); // ถึงแล้ว = คืน kickEnd เป๊ะ
                leg.target.position = p;
                if (leg.Kick) leg.target.rotation = Quaternion.Slerp(leg.target.rotation, sole, 1f - Mathf.Exp(-20f * dt));
                if (p != kickEnd[i]) return true; // ค่าที่คำนวณ ไม่ใช่ target ที่อ่านกลับ (ดูหมายเหตุช่วงตกข้างบน)
                if (leg.Kick)
                {
                    leg.Kick = false;
                    kickFall[i] = OnGround(leg, ClampStep(leg, leg.target.position)).y < leg.target.position.y;
                    kickSpeed[i] = kickFall[i] ? 0f : kickSpeed[i] * 0.5f; // ตกเริ่มจากนิ่ง | ชักกลับช้าลงครึ่งหนึ่ง
                    return true;
                }
                CancelKick(i);
                liftFrom[i] = stepAim[i] = ClampStep(leg, leg.target.position);
                swingT[i] = swingLift[i] = 1f; // ช่วงลงของก้าว + เท้าค่อยๆ หมุนกลับราบ
                return true;
            }
            // ยกเข่า: เท้าจากพื้นขึ้นไปเก็บไว้หน้าตัวใน kickChamberTime วิ
            kickCharge[i] += dt;
            float t = Mathf.Clamp01(kickCharge[i] / kickChamberTime);
            if (t < 1f)
            {
                kickDir[i] = KickAim(i, leg, hip); // ระหว่างยกเข่ายังเล็งเบนได้ — ปล่อยเท้าแล้วล็อกทิศ
                Vector3 chamber = hip + kickDir[i] * (leg.Reach * 0.2f) - Vector3.up * (leg.Reach * kickChamberDrop);
                leg.target.position = Vector3.Lerp(hip + kickFrom[i], chamber, Mathf.SmoothStep(0f, 1f, t));
                leg.target.rotation = Quaternion.Slerp(Quaternion.LookRotation(kickDir[i]) * leg.RestRotation, sole, t * 0.5f);
                return true;
            }
            kickCharge[i] = -1f;
            kickSpeed[i] = kickThrustSpeed * KickHealthPower(i);
            // ยืดสุด (0.98 ของขา — เหยียดตรงเป๊ะ IK หาทิศเข่าไม่ได้) | มีของบนพื้นตรงหน้า = ถีบกดลงไปหากลางของ (ไม่ต่ำกว่า -50°) เตะเป็นกระสุน
            float e = kickHighAngle * Mathf.Deg2Rad;
            Rigidbody prop = PropToKick(leg, hip, kickDir[i]);
            if (prop)
            {
                Vector3 to = prop.worldCenterOfMass - hip;
                e = Mathf.Clamp(Mathf.Atan2(to.y, Vector3.ProjectOnPlane(to, Vector3.up).magnitude), -50f * Mathf.Deg2Rad, e);
            }
            kickEnd[i] = hip + (kickDir[i] * Mathf.Cos(e) + Vector3.up * Mathf.Sin(e)) * (leg.Reach * 0.98f);
            leg.Kick = true;
            return true;
        }

        // กระทืบ (เครื่องที่คุมขา): จอสั่นหนัก + ประกายไฟที่เท้า แล้วให้ Server ทำคลื่นกระแทก
        private void Stomp(int i, Vector3 foot)
        {
            Vector3 ground = new Vector3(foot.x, GroundY(foot), foot.z);
            ImpactFeel.PlayHit(ground, Vector3.up, 45f);
            if (Online && !IsServer) StompRpc(i, ground);
            else ServerStomp(i, ground);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void StompRpc(int i, Vector3 ground, RpcParams rpcParams = default)
        {
            if (i < 0 || i >= limbs.Length || limbs[i].isArm || ControllerOf(i) != rpcParams.Receive.SenderClientId) return;
            if (Vector3.Distance(ground, limbs[i].root.position) > limbs[i].Reach * 2f) return; // ไม่ใช่ใต้เท้าตัวเอง
            ServerStomp(i, ground);
        }

        // [SERVER] คลื่นกระแทกรอบเท้า: ของที่ขยับได้ลอยขึ้นระดับอก (ใกล้เท้า = สูงกว่า) เป็นกระสุนให้เพื่อนที่คุมแขนคว้า/ตีต่อ
        // ศัตรูในวง = ดาเมจเล็กน้อย + กระเด็น (นับเป้าละครั้ง)
        private void ServerStomp(int i, Vector3 ground)
        {
            float radius = limbs[i].Reach * stompRadius;
            Team team = robot ? robot.GetTeam() : Team.None;
            stompBodies.Clear();
            stompTargets.Clear();
            int n = Physics.OverlapSphereNonAlloc(ground, radius, hitBuffer, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < n; k++)
            {
                Collider c = hitBuffer[k];
                Robot other = Robot.FromCollider(c);
                if (other == robot) continue;
                Rigidbody rb = c.attachedRigidbody;
                if (!other && rb && !rb.isKinematic && System.Array.IndexOf(heldBody, rb) < 0 && stompBodies.Add(rb))
                {
                    Vector3 away = Vector3.ProjectOnPlane(rb.position - ground, Vector3.up);
                    float near = 1f - Mathf.Clamp01(away.magnitude / radius);
                    KickedProp.Launch(rb, Vector3.up * (stompLift * Mathf.Lerp(0.6f, 1f, near)) + away.normalized * (stompLift * 0.15f), kickPropDamage * 0.6f, team, robot);
                    continue;
                }
                IDamageable target = c.GetComponentInParent<IDamageable>();
                if (target == null || !stompTargets.Add(target)) continue;
                Vector3 dir = Vector3.ProjectOnPlane(Closest(c, ground) - ground, Vector3.up);
                DamageRouter.TryApply(target, new DamageInfo(stompDamage, Closest(c, ground), dir.sqrMagnitude > 1e-4f ? dir.normalized : transform.forward, DamageSource.Kick, team) { knockback = 6f });
            }
            Debug.Log($"[RobotIKControl] กระทืบ {limbs[i].name}: ของลอย {stompBodies.Count} ชิ้น, ศัตรูในวง {stompTargets.Count}");
        }

        // [SERVER] ถีบโดนของที่ขยับได้ (ไม่ใช่หุ่น/ของที่ถือ) = ของพุ่งไปทางที่ถีบ เป็นกระสุนชนศัตรูได้ — ท่าถีบหนึ่งครั้งพุ่งของละครั้ง
        private void KickProps(int i, Vector3 foot, Vector3 move)
        {
            Vector3 dir = (Vector3.ProjectOnPlane(move, Vector3.up).normalized + Vector3.up * 0.3f).normalized;
            Team team = robot ? robot.GetTeam() : Team.None;
            int n = Physics.OverlapSphereNonAlloc(foot, strikeRadius * 1.5f, hitBuffer, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < n; k++)
            {
                Collider c = hitBuffer[k];
                Rigidbody rb = c.attachedRigidbody;
                if (!rb || rb.isKinematic || Robot.FromCollider(c) || System.Array.IndexOf(heldBody, rb) >= 0 || !kickedProps[i].Add(rb)) continue;
                KickedProp.Launch(rb, dir * (kickPropSpeed / (1f + rb.mass / 200f)), kickPropDamage, team, robot);
                ImpactFeel.PlayHit(Closest(c, foot), -dir, 25f);
                Debug.Log($"[RobotIKControl] ถีบ {limbs[i].name} → '{rb.name}' พุ่ง {rb.linearVelocity.magnitude:F0} m/s");
            }
        }

        // ของที่ขยับได้ต่ำกว่าสะโพก อยู่ในระยะขาตามทิศถีบ (ไม่ใช่หุ่น/ของที่ถือ) ที่ใกล้แนวถีบที่สุด — ไม่ต้องเล็งเป๊ะ (คนเล่นไม่เป็นก็เตะโดน)
        private Rigidbody PropToKick(IKLimb leg, Vector3 hip, Vector3 dir)
        {
            Rigidbody best = null;
            float bestSide = float.MaxValue;
            // วงค้นหาอยู่ครึ่งล่างหน้าสะโพก (ของบนพื้นห่างสะโพกลงไปเกือบความยาวขา)
            int n = Physics.OverlapSphereNonAlloc(hip + dir * (leg.Reach * 0.6f) - Vector3.up * (leg.Reach * 0.5f), leg.Reach * 0.8f, hitBuffer, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < n; k++)
            {
                Rigidbody rb = hitBuffer[k].attachedRigidbody;
                if (!rb || rb.isKinematic || Robot.FromCollider(hitBuffer[k]) || System.Array.IndexOf(heldBody, rb) >= 0) continue;
                Vector3 to = rb.worldCenterOfMass - hip;
                float along = Vector3.Dot(to, dir);
                if (to.y > 0f || along < leg.Reach * 0.2f) continue; // เหนือสะโพก = ถีบสูงแบบเดิมโดนอยู่แล้ว | ใต้ตัว = เตะไม่ได้
                float side = Vector3.ProjectOnPlane(to - dir * along, Vector3.up).magnitude;
                if (side < bestSide) { bestSide = side; best = rb; }
            }
            return best;
        }

        // ================================================================ ท่าแขน

        // (1) ยกการ์ด: แขนท่อนล่างขวางหน้าอก/หน้า หลังแขนหันออก (ผู้ใช้ 2026-11-04) — มือไปกลางตัว ศอกชี้ออกข้าง (ArmPole) ไม่ต่อย
        private static readonly Vector3 GuardHand = new Vector3(-0.35f, 0.25f, 0.55f); // ×แขน จากไหล่ในแกนตัวหุ่น (x × ฝั่งแขน)
        private static readonly Vector3 GuardElbow = new Vector3(1f, -0.15f, 0.25f);    // ทิศศอกตอนยกการ์ด (x × ฝั่งแขน) — ออกข้าง แขนท่อนล่างเลยนอนขวาง
        private Vector3 ArmPole(IKLimb arm) => arm.Brace && !arm.Grab
            ? new Vector3(GuardElbow.x * arm.Side, GuardElbow.y, GuardElbow.z)
            : new Vector3(combat.elbowPole.x * arm.Side, combat.elbowPole.y, combat.elbowPole.z);

        private void DriveBlock(int i)
        {
            IKLimb arm = limbs[i];
            if (arms[i] != null) combat.Release(arm, arms[i]);
            Vector3 goal = arm.root.position + transform.rotation * (new Vector3(GuardHand.x * arm.Side, GuardHand.y, GuardHand.z) * arm.Reach);
            arm.target.position = Vector3.Lerp(arm.target.position, goal, 1f - Mathf.Exp(-follow * 1.5f * Time.deltaTime));
            arm.Grip = Mathf.MoveTowards(arm.Grip, 1f, gripSpeed * Time.deltaTime);
        }

        // (2) เครื่องที่คุมแขน: ทิศขว้าง = ไปจุดที่เคอร์เซอร์ชี้ (ไม่นับของใกล้มือ — ของในมือ/ตัวเองบังเคอร์เซอร์) + เชิดขึ้นนิดให้เป็นวิถีโค้ง
        private Vector3 AimThrow(int i, Ray ray)
        {
            Vector3 hand = limbs[i].GripCenter, aim = ray.GetPoint(300f);
            float nearest = 300f;
            foreach (RaycastHit h in Physics.RaycastAll(ray, 1000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (h.distance < nearest && (h.point - hand).magnitude > limbs[i].Reach) { nearest = h.distance; aim = h.point; }
            return ((aim - hand).normalized + Vector3.up * 0.15f).normalized;
        }

        // ท่าขว้าง (เครื่องที่คุมแขน — ทุกเครื่องเห็นจากเป้ามือที่ซิงก์): ง้างไปหลังเหนือไหล่ (ช้า หนัก) → เหวี่ยงไปทางที่ปา (เร่ง t² เร็วสุดตอนปล่อย)
        // → ปล่อยของตอนแขนเหยียดสุด | ลำตัวบิดตามเอง (WeaponPose อ่านความเร็วแขน) | จังหวะ × slashWeight เหมือนท่าฟัน
        private const float ThrowWindup = 0.3f, ThrowStrike = 0.12f;
        private void DriveThrow(int i)
        {
            IKLimb arm = limbs[i];
            float dt = Time.deltaTime, wind = ThrowWindup * slashWeight, strike = ThrowStrike * slashWeight;
            throwT[i] += dt;
            Vector3 d = throwAim[i], flat = Vector3.ProjectOnPlane(d, Vector3.up).normalized;
            Vector3 back = (-flat * 0.5f + Vector3.up * 0.8f + transform.right * (arm.Side * 0.3f)).normalized; // หลังไหล่ข้างเดียวกัน
            Vector3 front = (d + Vector3.up * 0.1f).normalized;
            Vector3 dir;
            float radius, rate;
            if (throwT[i] < wind) { dir = back; radius = 0.75f; rate = 14f / slashWeight; }
            else
            {
                float k = Mathf.Clamp01((throwT[i] - wind) / strike);
                dir = Vector3.Slerp(back, front, k * k);
                radius = Mathf.Lerp(0.75f, 0.97f, k); // ศอกเหยียดตอนท้าย = แรงสะบัด
                rate = 60f;
            }
            Vector3 shoulder = arm.root.position;
            arm.target.position = shoulder + Vector3.Slerp(arm.target.position - shoulder, dir * (arm.Reach * radius), 1f - Mathf.Exp(-rate * dt));
            if (throwT[i] < wind + strike) return;
            if (Online && !IsServer) ThrowRpc(i, d);
            else ServerThrow(i, d);
            CancelGrab(i); // จบท่า = มือว่าง (Server ปล่อยของแล้วใน ServerThrow — สัญญาณปล่อยมือที่ตามไปไม่ซ้ำ)
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ThrowRpc(int i, Vector3 dir, RpcParams rpcParams = default)
        {
            if (i < 0 || i >= limbs.Length || !limbs[i].isArm || ControllerOf(i) != rpcParams.Receive.SenderClientId) return;
            ServerThrow(i, dir.normalized);
        }

        // [SERVER] ขว้างของในมือ (หรือของที่มือนี้เพิ่งปล่อยไม่ถึง 0.3 วิ — สัญญาณปล่อยมือถึงก่อน) | อีกมือยังถืออยู่ = ไม่ขว้าง
        private void ServerThrow(int i, Vector3 dir)
        {
            Rigidbody rb = heldBody[i];
            if (rb) ReleaseGrab(i);
            else if (dropped[i] && Time.time - droppedAt[i] < 0.3f) rb = dropped[i];
            if (!rb || System.Array.IndexOf(heldBody, rb) >= 0) return;
            dropped[i] = null;
            KickedProp.Launch(rb, dir * (throwSpeed / (1f + rb.mass / 200f)), throwDamage, robot ? robot.GetTeam() : Team.None, robot);
            Debug.Log($"[RobotIKControl] {limbs[i].name} ขว้าง '{rb.name}' {rb.linearVelocity.magnitude:F0} m/s");
        }

        private bool Punching(int i) => arms[i] != null && arms[i].state == IKCombat.State.Punching; // ชาร์จอยู่ยกการ์ดได้ (ยกเลิกหมัด) — มือยังอยู่หลังท่าการ์ด

        private bool ArmGuarding(int i) => limbs[i].isArm && limbs[i].Brace && Attached(i) && !down && getUp < 0f;

        // สองแขนยกการ์ดพร้อมกัน = ตั้งรับเต็มตัว แรงกระแทกล้มไม่ได้ (เหมือนขาปักหลัก)
        private bool ArmsGuarding
        {
            get
            {
                int arms = 0, guarding = 0;
                for (int i = 0; i < limbs.Length; i++)
                    if (limbs[i].isArm && Attached(i)) { arms++; if (ArmGuarding(i)) guarding++; }
                return arms >= 2 && guarding == arms;
            }
        }

        // [SERVER] ตัวคูณดาเมจของชิ้น i (LimbHealth.DamageScale) — เฉพาะดาเมจจากศัตรู
        // ชิ้นนี้เป็นแขนที่ยกการ์ดอยู่ = เบาลงเหลือ blockDamageScale (ชิ้นอื่นโดนเต็ม — ผู้ใช้ 2026-11-04 เดิมยกแขนเดียวทุกชิ้นเบาลงหมด)
        // เพิ่งยกไม่เกิน parryWindow แล้วโดนตีธรรมดา = ปัดป้อง ไม่โดนเลย + ศัตรูเสียหลัก (ท่าไม้ตายปัดไม่ได้)
        // < 1 = กันได้ → LimbHealth เล่นเสียงหมัดกระทบเหล็ก (ImpactFeel.PlayBlock) แทนเสียงโดนตีปกติ ทุกเครื่อง
        private float BlockScale(int i, DamageInfo info)
        {
            if (info.source != DamageSource.EnemyMelee && info.source != DamageSource.EnemyUltimate) return 1f;
            if (!ArmGuarding(i)) return 1f;
            if (info.source == DamageSource.EnemyMelee && Time.time - blockSince[i] <= parryWindow)
            {
                EnemyController enemy = NearestEnemy(info.point, limbs[RearLeg].Reach * 3f);
                if (enemy) Stagger(enemy);
                Debug.Log($"[RobotIKControl] ปัดป้อง! {limbs[i].name} — {(enemy ? enemy.name + " เสียหลัก" : "ไม่เจอศัตรูใกล้จุดตี")}");
                return 0f;
            }
            return blockDamageScale;
        }

        private EnemyController NearestEnemy(Vector3 at, float radius)
        {
            EnemyController best = null;
            float bestSqr = float.MaxValue;
            int n = Physics.OverlapSphereNonAlloc(at, radius, hitBuffer, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            for (int k = 0; k < n; k++)
            {
                EnemyController e = hitBuffer[k].GetComponentInParent<EnemyController>();
                if (!e) continue;
                float sqr = (e.transform.position - at).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = e; }
            }
            return best;
        }

        // [SERVER] ศัตรูเสียหลัก: AI หยุดคิด (ServerBeginKnockback ของศัตรูเอง — ไม่ได้แก้โค้ดศัตรู) จนหมดเวลา | ระหว่างนี้แขนกด F ใส่ = ล็อกตัว
        private void Stagger(EnemyController enemy)
        {
            if (!staggered.ContainsKey(enemy)) enemy.ServerBeginKnockback();
            staggered[enemy] = Mathf.Max(staggered.TryGetValue(enemy, out float until) ? until : 0f, Time.time + staggerTime);
        }

        private readonly List<EnemyController> staggerDone = new List<EnemyController>();

        // [SERVER] ทุก FixedUpdate: ล็อกหมดเวลา/ศัตรูหาย = ปล่อย | เสียหลักหมดเวลา = AI กลับมาคิด
        private void ServerStaggers()
        {
            for (int i = 0; i < limbs.Length; i++)
                if (grappled[i] != null && (!grappled[i] || Time.time > grappleUntil[i])) EndGrapple(i);
            if (staggered.Count == 0) return;
            staggerDone.Clear();
            foreach (KeyValuePair<EnemyController, float> s in staggered)
                if (!s.Key || Time.time > s.Value) staggerDone.Add(s.Key);
            foreach (EnemyController e in staggerDone)
            {
                staggered.Remove(e);
                if (e) e.ServerEndKnockback();
            }
        }

        // (6) [SERVER] F ไม่โดนของ แต่กำปั้นอยู่ใกล้ศัตรูที่เสียหลัก = ล็อกตัวไว้ (AI หยุดต่อไปอีก grappleTime) ให้เพื่อนอัดท่าใหญ่ใส่
        private void TryGrapple(int i)
        {
            if (staggered.Count == 0) return;
            Vector3 g = limbs[i].GripCenter;
            int n = Physics.OverlapSphereNonAlloc(g, grabRadius * 2f, hitBuffer, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            for (int k = 0; k < n; k++)
            {
                EnemyController e = hitBuffer[k].GetComponentInParent<EnemyController>();
                if (!e || !staggered.ContainsKey(e)) continue;
                grappled[i] = e;
                grappleUntil[i] = Time.time + grappleTime;
                staggered[e] = Mathf.Max(staggered[e], grappleUntil[i]);
                Debug.Log($"[RobotIKControl] {limbs[i].name} ล็อก '{e.name}' {grappleTime:F1} วิ");
                return;
            }
        }

        // ปล่อยล็อก = ศัตรูได้คืนตัวทันที (ไม่รอเวลาเสียหลักที่ต่อไว้)
        private void EndGrapple(int i)
        {
            EnemyController e = grappled[i];
            grappled[i] = null;
            if (e && staggered.ContainsKey(e)) staggered[e] = Time.time;
        }

        // (4) [SERVER] แขนนี้เพิ่งต่อย/ฟาดเข้าเป้า: อีกแขนโดนเป้าเดียวกันไม่เกิน doubleImpactWindow วิ = Double Impact — ดาเมจโบนัส + กระเด็นแรง + ศัตรูเสียหลัก
        private void DoubleImpact(int i, Collider hit, DamageInfo info)
        {
            IDamageable target = hit.GetComponentInParent<IDamageable>();
            for (int j = 0; j < limbs.Length; j++)
            {
                if (j == i || !limbs[j].isArm || lastHitTarget[j] != target || Time.time - lastHitAt[j] > doubleImpactWindow) continue;
                lastHitTarget[j] = null;
                DamageRouter.TryApply(target, new DamageInfo(doubleImpactDamage, info.point, info.direction, info.source, info.attackerTeam) { knockback = 14f });
                EnemyController enemy = hit.GetComponentInParent<EnemyController>();
                if (enemy) Stagger(enemy);
                ImpactFeel.PlayHit(info.point, -info.direction, 60f);
                Debug.Log($"[RobotIKControl] Double Impact! {limbs[j].name} + {limbs[i].name} → '{hit.name}' โบนัส {doubleImpactDamage:F0}");
                return;
            }
            lastHitTarget[i] = target;
            lastHitAt[i] = Time.time;
        }

        // ถีบได้แค่ข้างหน้า: ทิศไป ◈ เบนจากหน้าหุ่นได้ไม่เกิน ±kickAimLimit | ◈ ใกล้สะโพกเกินไป = ตรงหน้า
        private Vector3 KickAim(int i, IKLimb leg, Vector3 hip)
        {
            Vector3 front = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 aim = Vector3.ProjectOnPlane(footMark[i] - hip, Vector3.up);
            if (aim.magnitude <= leg.Reach * 0.1f) return front;
            float yaw = Mathf.Clamp(Vector3.SignedAngle(front, aim, Vector3.up), -kickAimLimit, kickAimLimit);
            return Quaternion.AngleAxis(yaw, Vector3.up) * front;
        }

        // เลิกจับ + เลิกเอื้อม (ชิ้นหลุด/ล้ม/ไม่ได้คุม/กดปล่อย) — Server ปล่อยของเองเมื่อเห็น Grab = false
        private void CancelGrab(int i)
        {
            limbs[i].Grab = limbs[i].Reaching = false;
            reachTarget[i] = null;
            grabWant[i] = null;
            slash[i].kind = slash[i].next = cueNext = -1; // ปล่อยของ/สลับชิ้นกลางท่าฟัน — หยิบใหม่ทีหลังไม่ต่อท่าเก่า
            slash[i].hold = slash[i].nextHold = false;
            EndSlash(i);
            throwT[i] = -1f;
            slash[i].dragAcc = Vector2.zero;
        }

        private void CancelKick(int i)
        {
            limbs[i].Kick = limbs[i].KickPose = kickFall[i] = false;
            kickCharge[i] = -1f;
        }

        private float KickHealthPower(int i)
        {
            LimbHealth health = robot ? robot.GetLimb((LimbSlot)i)?.Health : null;
            return health ? Mathf.Lerp(kickPowerAtZeroHealth, 1f, Mathf.Clamp01(health.Hp / Mathf.Max(1f, health.StartingMaxHp))) : 1f;
        }

        // หุ่นเดิม (ClampPlantWithinReach): ตัดแค่ระยะแนวราบจากสะโพก ไม่ใช่ทรงกลมความยาวขา — ตัดทรงกลมแล้วเท้าโดนดึงมาใต้ตัวทุกก้าว
        // ระหว่างก้าวลำตัวไหลไปหาเท้า (UpdateBody) เท้าจึงเอื้อมถึง
        private static Vector3 ClampStep(IKLimb leg, Vector3 point)
        {
            Vector3 hip = leg.root.position;
            Vector3 flat = Vector3.ClampMagnitude(new Vector3(point.x - hip.x, 0f, point.z - hip.z), leg.Reach * 0.9f);
            return new Vector3(hip.x + flat.x, point.y, hip.z + flat.z);
        }

        private Vector3 OnGround(IKLimb leg, Vector3 p) => new Vector3(p.x, GroundY(p) + leg.RestHeight, p.z);

        // หุ่นเดิม (LegController.LandingYaw): เท้าลงหันตามทิศลำตัว + ครึ่งหนึ่งของมุมก้าว (ไม่เกิน maxTurnPerStep)
        // ก้าวสั้นกว่า 20% ของขา หรือถอยหลัง/เฉียงเกิน 110° = ทิศลำตัว | ตัวหันตามเท้าที่ปัก (UpdateBody) = เลี้ยว
        private float LandingYaw(int i, IKLimb leg) => transform.eulerAngles.y + StepTurn(leg, liftFrom[i], stepAim[i]);

        // ครึ่งหนึ่งของมุมก้าว (from → to) เทียบทิศหน้าหุ่นตอนนี้ ไม่เกิน maxTurnPerStep | ก้าวสั้น/ถอยหลัง/เฉียงเกิน 110° = 0
        private float StepTurn(IKLimb leg, Vector3 from, Vector3 to)
        {
            Vector3 step = Vector3.ProjectOnPlane(to - from, Vector3.up);
            float angle = Vector3.SignedAngle(transform.forward, step, Vector3.up);
            if (step.magnitude <= leg.Reach * 0.2f || Mathf.Abs(angle) > 110f) return 0f;
            return Mathf.Clamp(angle * 0.5f, -feel.maxTurnPerStep, feel.maxTurnPerStep);
        }

        // ================================================================ ตัวหุ่น

        // หุ่นเดิม (TorsoBalance): ตัวหุ่นไหลไปกลางระหว่างเท้าที่ปัก + เท้าที่กำลังก้าว (ถ่ายน้ำหนักไปข้างหน้าระหว่างก้าว
        // ไม่งั้นตัวโดนดึงกลับมาเหนือขาที่ยืน เท้าที่ก้าวเอื้อมได้นิดเดียว) → ก้าวเท้าทีละข้าง = เดิน
        private void UpdateBody()
        {
            Vector3 mid = Vector3.zero;
            int legs = 0;
            bool stepping = false;
            float floor = float.MaxValue;
            for (int i = 0; i < limbs.Length; i++)
            {
                IKLimb leg = limbs[i];
                if (leg.isArm) continue;
                mid += leg.KickPose ? planted[i] : leg.target.position; // ถีบ = ยืนขาเดียว ตัวไม่ไหลตามเท้าที่ถีบ
                legs++;
                stepping |= airborne[i] && !leg.KickPose;
                // ความสูงตัว = พื้นใต้เท้าที่ปักอยู่ข้างที่ต่ำสุด (เท้ายก/ถีบใช้จุดที่ปักล่าสุด) — ไม่ใช่พื้นใต้กลางตัว:
                // ยืนบนกล่องสองใบ กลางตัวเป็นร่อง ตัวเคยทรุดจนขาพับ | ยืนต่างระดับ ขาข้างสูงงอเอาแบบขึ้นบันได
                floor = Mathf.Min(floor, (airborne[i] || leg.KickPose ? planted[i].y : leg.target.position.y) - leg.RestHeight);
            }
            if (legs == 0) return;
            mid /= legs;

            // มือเอื้อมลงต่ำเกินแขน (หยิบของบนพื้น) = ย่อตัวลงตามไป ไม่เกิน reachSquat ของขา — ที่เหลือก้มเอา (UpdateLean)
            float need = 0f;
            for (int i = 0; i < limbs.Length; i++)
                if (limbs[i].isArm && limbs[i].Reaching && Attached(i))
                    need = Mathf.Max(need, floor - crouch + shoulderRest[i].y - limbs[i].target.position.y - limbs[i].Reach * 0.95f);
            float squat = Mathf.Min(need, limbs[2].Reach * reachSquat);
            // + ไกลเกินแขนตามแนวราบ = ตัวพุ่งไปทางเป้า (เท้าอยู่ที่เดิม) วัดจากไหล่ตอนยืนกลางเท้า ไม่ใช่ไหล่ที่พุ่งไปแล้ว — ไม่งั้นพุ่งแล้วระยะหด ค้างครึ่งทาง
            Vector3 lunge = Vector3.zero;
            for (int i = 0; i < limbs.Length; i++)
            {
                if (!limbs[i].isArm || !limbs[i].Reaching || !Attached(i)) continue;
                Vector3 shoulder = mid + bodyRot * new Vector3(shoulderRest[i].x, 0f, shoulderRest[i].z);
                Vector3 flat = Vector3.ProjectOnPlane(limbs[i].target.position - shoulder, Vector3.up);
                float excess = Mathf.Min(flat.magnitude - limbs[i].Reach * 0.9f, limbs[2].Reach * reachLunge);
                if (excess > lunge.magnitude) lunge = flat.normalized * excess;
            }
            float duck = 0f; // ขาค้าง F = ย่อหลบ (Server เห็นค่าที่ซิงก์ของทุกขา)
            for (int i = 0; i < limbs.Length; i++)
                if (!limbs[i].isArm && limbs[i].Brace && Attached(i)) duck = limbs[i].Reach * braceCrouch;
            Vector3 goal = new Vector3(mid.x, floor - crouch - Mathf.Max(squat, duck), mid.z) + lunge;
            // กางขากว้าง/ก้าวยาว: สะโพกสูงเท่ายืนตรงแล้วขายืดไม่ถึงพื้น (IK ยืดสุด เท้าลอยจากจุดปัก วัดได้ 0.55–1.1 ม.)
            // → ลดตัวลงจนทุกเท้าที่ปักอยู่เอื้อมถึง (สะโพกในแกนตัวหุ่นจาก shoulderRest ของขา, เผื่อ 3% ไม่ให้ขาเหยียดตึง)
            for (int i = 0; i < limbs.Length; i++)
            {
                IKLimb leg = limbs[i];
                if (leg.isArm || airborne[i] || leg.KickPose || !Attached(i)) continue;
                Vector3 hip = goal + bodyRot * shoulderRest[i];
                float reach = leg.Reach * 0.97f;
                float flat = Mathf.Min(Vector3.ProjectOnPlane(leg.target.position - hip, Vector3.up).magnitude, reach * 0.95f);
                goal.y = Mathf.Min(goal.y, leg.target.position.y + Mathf.Sqrt(reach * reach - flat * flat) - shoulderRest[i].y);
            }
            // หุ่นเดิม stepCenterMultiplier 2: ระหว่างก้าวดึงตัวแรงขึ้นสองเท่า (แรงสปริง ×2 = ความถี่ ×√2) ตัวไหลทันเท้า = ก้าวยาว
            float frequency = feel.bodyFrequency * (stepping ? 1.414f : 1f) * (1f + strideCombo * strideBonus); // เดินติดจังหวะ = ตัวพุ่งตามเท้าแรงขึ้น
            if (feel.bodyFrequency > 0f) IKFeel.Spring(ref bodyPos, ref bodyVel, goal, frequency, feel.bodyDamping, Time.deltaTime);
            else bodyPos = Vector3.Lerp(bodyPos, goal, 1f - Mathf.Exp(-bodyFollow * Time.deltaTime));

            // หุ่นเดิม (TorsoBalance heading): ตัวหันตามค่าเฉลี่ยทิศเท้าที่ปักอยู่ด้วยสปริง (ทิศเท้าซิงก์มากับเป้าเท้าทุกเครื่อง)
            // + ระหว่างก้าวตัวบิดไปทางที่เท้ากำลังจะลง (หุ่นเดิมเกิดจากแรงดึงสะโพกในฟิสิกส์ — วัดได้ ~25° ตอนก้าวเฉียง 60°)
            // เท้าจึงลงหันเพิ่มจากตัวที่บิดแล้ว (LandingYaw) = เลี้ยวตามจุดที่วางเท้า
            Vector3 facing = Vector3.zero;
            float twist = 0f;
            for (int i = 0; i < limbs.Length; i++)
            {
                if (limbs[i].isArm) continue;
                if (!airborne[i]) facing += Quaternion.Euler(0f, FootYaw(limbs[i]), 0f) * Vector3.forward;
                else twist += StepTurn(limbs[i], planted[i], limbs[i].target.position);
            }
            if (facing.sqrMagnitude < 1e-2f) return;
            float yaw = bodyRot.eulerAngles.y;
            Vector3 x = new Vector3(yaw, 0f, 0f), v = new Vector3(yawVel, 0f, 0f);
            Vector3 face = new Vector3(yaw + Mathf.DeltaAngle(yaw, Quaternion.LookRotation(facing).eulerAngles.y) + twist, 0f, 0f);
            IKFeel.Spring(ref x, ref v, face, feel.headingFrequency, feel.headingDamping, Time.deltaTime);
            yawVel = v.x;
            bodyRot = Quaternion.Euler(0f, x.x, 0f);
        }

        // ================================================================ ดาเมจ

        // หุ่นเดิม (LimbStrike): กำปั้นเร็วเกิน strikeMinSpeed = กำลังต่อย → แตะของที่รับดาเมจได้ = ดาเมจตามความเร็วพีคของท่า
        // หนึ่งท่าหนึ่งครั้ง (ช้าลงต่ำกว่าเกณฑ์ = จบท่า) | Server ดูกำปั้นที่แก้ IK แล้ว — แขนของ client มาจาก state ที่ซิงก์
        private void ServerStrikes()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            for (int i = 0; i < limbs.Length; i++)
            {
                IKLimb limb = limbs[i];
                // ถืออาวุธ: วัดที่ปลายของ (ด้านไกลจากมือ — ฟาดเป็นวง ปลายเร็วกว่ามือ) แทนกำปั้น
                Rigidbody weapon = limb.isArm ? heldBody[i] : null;
                if (weapon && System.Array.IndexOf(heldBody, weapon) < i) { tracked[i] = false; continue; } // ถือสองมือ = นับที่แขนแรกแขนเดียว ไม่งั้นฟาดครั้งเดียวโดนสองดาเมจ (ไม่ติดตาม = แขนแรกปล่อยแล้วเริ่มนับใหม่ ไม่ใช้ตำแหน่งเก่า)
                Collider weaponCol = weapon ? weapon.GetComponentInChildren<Collider>() : null;
                Vector3 grip = limb.GripCenter;
                Vector3 tip = weaponCol ? Closest(weaponCol, weaponCol.bounds.center + (weaponCol.bounds.center - grip).normalized * 100f) : limb.tip.position;
                Vector3 move = tip - lastTip[i];
                lastTip[i] = tip;
                bool wasTracked = tracked[i] && strikeWeapon[i] == weapon;
                strikeWeapon[i] = weapon;
                tracked[i] = Attached(i) && !down && getUp < 0f && (weaponCol || !limb.Grab); // จับมือเปล่า (ยังไม่ได้ของ) ไม่ต่อย
                float speed = move.magnitude / dt;
                // แขน: นับเฉพาะกำปั้นพุ่งออกจากไหล่ (ดึงหมัดกลับเร็วๆ ไม่ใช่การต่อย) | ขา: เฉพาะช่วงถีบ (ก้าวเดินไม่ใช่การเตะ) | อาวุธ: ฟาดทางไหนก็นับ
                bool striking = weaponCol || ((limb.isArm || limb.Kick) && Vector3.Dot(move, tip - limb.root.position) > 0f);
                if (!limb.isArm && limb.Kick && tracked[i] && striking && speed >= strikeMinSpeed) KickProps(i, tip, move);
                else if (!limb.isArm && !limb.Kick) kickedProps[i].Clear();
                if (!wasTracked || !tracked[i] || speed < strikeMinSpeed || !striking)
                {
                    strikePeak[i] = 0f;
                    struck[i] = false;
                    continue;
                }
                strikePeak[i] = Mathf.Max(strikePeak[i], speed);
                if (struck[i]) continue;

                int n = weaponCol
                    ? Physics.OverlapCapsuleNonAlloc(grip, tip, strikeRadius * 0.5f, hitBuffer, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) // ทั้งลำ มือ → ปลาย
                    : Physics.OverlapSphereNonAlloc(tip, strikeRadius, hitBuffer, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < n; k++)
                {
                    Collider c = hitBuffer[k];
                    if (Robot.FromCollider(c) == robot) continue;
                    if (c.attachedRigidbody && System.Array.IndexOf(heldBody, c.attachedRigidbody) >= 0) continue; // ของที่ตัวเองถือ
                    float damage = Mathf.Min(strikePeak[i] * (limb.isArm ? strikeSpeedToDamage : kickSpeedToDamage), strikeMaxDamage);
                    DamageSource source = weaponCol ? DamageSource.MeleeWeapon : limb.isArm ? DamageSource.Punch : DamageSource.Kick;
                    var info = new DamageInfo(damage, Closest(c, tip), move.normalized, source, robot ? robot.GetTeam() : Team.None)
                    {
                        knockback = Mathf.Lerp(2f, 12f, Mathf.Pow(Mathf.InverseLerp(strikeMinSpeed, 55f, strikePeak[i]), 2f)),
                    };
                    if (!DamageRouter.TryApply(c, info)) continue;
                    struck[i] = true;
                    Debug.Log($"[RobotIKControl] {(weaponCol ? $"อาวุธ '{weapon.name}'" : limb.isArm ? "หมัด" : "เตะ")} {limb.name} โดน '{c.name}' ความเร็วพีค {strikePeak[i]:F1} m/s ดาเมจ {damage:F1}");
                    if (limb.isArm) DoubleImpact(i, c, info);
                    break;
                }
            }
        }

        // ================================================================ จับ (Half-sword)

        // ของที่ยึดอยู่ → มือ/หมัดช้าลง: 1 ÷ (1 + มวล ÷ grabHalfMass) ไม่ต่ำกว่า grabMinSpeed (ใช้กับแขนที่เครื่องนี้คุม — มวลมาจาก Server จึงเห็นผลบน Host)
        private float GrabLag(IKLimb arm)
        {
            for (int i = 0; i < limbs.Length; i++)
                if (limbs[i] == arm)
                {
                    if (!heldBody[i]) return 1f;
                    int hands = 0; // สองมือจับชิ้นเดียวกัน = แบ่งน้ำหนักคนละครึ่ง เหวี่ยงได้เร็วขึ้น (ท่าแขน 3)
                    foreach (Rigidbody rb in heldBody) if (rb == heldBody[i]) hands++;
                    return Mathf.Max(grabMinSpeed, grabHalfMass / (grabHalfMass + heldBody[i].mass / hands));
                }
            return 1f;
        }

        // ของที่อยู่ในกำมือ (ทุกเครื่องหาเอง ไม่ต้องซิงก์ — ใช้แค่ให้นิ้วงอถึงผิว) | mesh เว้าใช้ ClosestPoint ไม่ได้ = ข้าม
        private Collider InHand(IKLimb limb)
        {
            Vector3 g = limb.GripCenter;
            int n = Physics.OverlapSphereNonAlloc(g, grabRadius, hitBuffer, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore); // ฟาดเร็ว ของตามมือช้ากว่ามือได้ถึงราว 1
            Collider best = null;
            float bestSqr = float.MaxValue;
            for (int k = 0; k < n; k++)
            {
                Collider c = hitBuffer[k];
                if (!c.attachedRigidbody || c.transform.IsChildOf(transform) || Robot.FromCollider(c) == robot || c is MeshCollider { convex: false }) continue;
                float sqr = (Closest(c, g) - g).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = c; }
            }
            return best;
        }

        // หยิบได้ = ของ dynamic | เล่นคนเดียว (Host เปิดไม่ขึ้น): ของออนไลน์ที่ NetworkRigidbody ล็อก kinematic ไว้เพราะยังไม่ spawn ก็หยิบได้ (ไม่มีเน็ต = เครื่องนี้เป็นเจ้าของ)
        private bool Grabbable(Rigidbody rb) =>
            !rb.isKinematic || System.Array.IndexOf(heldBody, rb) >= 0 // ถืออยู่ (kinematic ตามมือ — PlaceHeld) = มืออีกข้างจับเพิ่มได้
            || (!Online && rb.TryGetComponent(out NetworkObject net) && !net.IsSpawned && !rb.GetComponentInParent<Robot>()); // ตัวหุ่นอื่นไม่นับ — ปลดล็อกแล้วหุ่นพัง

        private float ChestToShoulder => Mathf.Max(0.1f, shoulderRest[0].y - spineRestHeight);

        // ของ dynamic ที่ใกล้กำปั้นที่สุดในระยะที่แขนเอื้อมถึง (ไหล่ → ความยาวแขน + ครึ่งรัศมีจับ) — ไม่นับตัวหุ่นเอง
        private Rigidbody GrabCandidate(int i)
        {
            IKLimb arm = limbs[i];
            Vector3 shoulder = arm.root.position, tip = arm.tip.position;
            // ระยะแนวราบจากไหล่ไม่เกินแขน + ก้มยื่นตัว — สูงต่ำไม่จำกัด (ของบนพื้นตัวย่อ/ก้มลงไปหยิบ)
            // เลือกชิ้นที่เคอร์เซอร์ชี้ใกล้สุด (มองที่ไหนหยิบที่นั่น) | ไม่มีกล้อง = ชิ้นที่ใกล้กำปั้นสุด
            // เรืองเฉพาะที่หยิบได้จริง (วัดในเกม แขนยาว 4.1 — ดู Quest) | แขน 1.15 เท่า + ตัวพุ่ง + ครึ่งรัศมีจับ
            float range = arm.Reach * 1.15f + limbs[2].Reach * reachLunge + grabRadius * 0.5f; // แขน + ตัวพุ่ง
            Vector3 ground = new Vector3(shoulder.x, GroundY(shoulder) - 1f, shoulder.z);
            int n = Physics.OverlapCapsuleNonAlloc(ground, shoulder, range, grabBuffer, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Rigidbody best = null;
            float bestSqr = float.MaxValue;
            for (int k = 0; k < n; k++)
            {
                Collider c = grabBuffer[k];
                Rigidbody rb = c.attachedRigidbody;
                if (rb == null || !Grabbable(rb) || rb.transform.IsChildOf(transform) || Robot.FromCollider(c) == robot) continue;
                Vector3 near = Closest(c, new Vector3(shoulder.x, Mathf.Clamp(c.bounds.center.y, c.bounds.min.y, c.bounds.max.y), shoulder.z));
                if (new Vector2(near.x - shoulder.x, near.z - shoulder.z).sqrMagnitude > range * range) continue;
                Vector3 from = tip;
                if (hasGrabAim) from = grabAim.origin + grabAim.direction * Mathf.Max(0f, Vector3.Dot(c.bounds.center - grabAim.origin, grabAim.direction));
                float sqr = (Closest(c, from) - from).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = rb; }
            }
            // ชิ้นของตัวเองที่หลุดอยู่ใกล้ๆ (หยิบมาฟาด) แข่งกับของอื่นด้วยเกณฑ์เดียวกัน — เคอร์เซอร์ชี้ใกล้อันไหนเอาอันนั้น
            Rigidbody part = NearDetached(shoulder, range, true, out Collider pc); // แนวราบจากไหล่เหมือนของอื่น — ชิ้นบนพื้นต่ำกว่ามือหลายเมตร หุ่นย่อลงไปหยิบเอง
            if (part)
            {
                Vector3 from = hasGrabAim ? grabAim.origin + grabAim.direction * Mathf.Max(0f, Vector3.Dot(pc.bounds.center - grabAim.origin, grabAim.direction)) : tip;
                if ((Closest(pc, from) - from).sqrMagnitude < bestSqr) best = part;
            }
            return best;
        }

        // (ท่าแขน 5) ชิ้นของหุ่นตัวเองที่หลุดอยู่บนพื้น — สำเนาอยู่ layer Ignore Raycast (เท้าไม่เหยียบ) overlap ปกติจึงไม่เห็น ต้องเช็คเอง
        private Rigidbody NearDetached(Vector3 from, float range, bool flat, out Collider touch)
        {
            touch = null;
            Rigidbody best = null;
            float bestSqr = range * range;
            for (int k = 0; k < limbs.Length; k++)
            {
                Rigidbody rb = detach.Prop(k);
                Collider c = rb ? rb.GetComponent<Collider>() : null;
                if (!c) continue;
                Vector3 d = Closest(c, from) - from;
                if (flat) d.y = 0f;
                float sqr = d.sqrMagnitude;
                if (sqr <= bestSqr) { bestSqr = sqr; best = rb; touch = c; }
            }
            return best;
        }

        // กด F แล้วแขนเอื้อมไปที่ของ (จุดบนผิวที่ใกล้กำปั้น ในกรอบข้อต่อไหล่) มือแบรอหยิบ — ถึงแล้ว = เปิดจับ (Server ยึดชิ้นที่กำปั้นแตะ)
        // หมดเวลา = เลิก (ไม่จับ) | กลับไปคุมตามปกติจากท่าที่มืออยู่ตอนนั้น (combat.Release จำท่าไว้ มือไม่กระโดดกลับการ์ด) | true = เฟรมนี้แขนกำลังเอื้อม
        private bool ReachForGrab(int i)
        {
            Rigidbody body = reachTarget[i];
            if (!body) { limbs[i].Reaching = false; return false; } // ของถูกทำลายกลางทาง
            IKLimb arm = limbs[i];
            Vector3 tip = arm.GripCenter; // วัดจากกลางกำปั้น (จุดที่ของจะแนบ) ไม่ใช่ข้อมือ
            Collider c = body.GetComponentInChildren<Collider>();
            Vector3 surface = c ? Closest(c, tip) : body.position;
            Transform gp = GripPointOf(body, tip);
            Vector3 point = gp ? gp.position : surface; // มีจุดจับ = เอื้อมไปทางจุดจับ (TryGrab หมุนของเข้ามือเอง)
            reachTimer[i] += Time.deltaTime;
            // ถึง = ผิวของเข้ารัศมีจับแล้ว (ลูกกลมชนของมือดันของออกก่อนเข้าใกล้กว่านี้ได้) — Server ยึดชิ้นในรัศมีเดียวกันนี้
            bool arrived = (surface - tip).magnitude <= grabRadius * 0.9f;
            if (arrived || reachTimer[i] >= grabReachTime)
            {
                reachTarget[i] = null;
                arm.Reaching = false;
                arm.Grab = arrived;
                grabWant[i] = arrived ? body : null;
                if (arms[i] != null) combat.Release(arm, arms[i]);
                return arrived;
            }
            // ทิศตามข้อต่อไหล่ แต่ระยะจริง — เป้าต่ำ/ไกลกว่าแขนเอื้อม = ตัวย่อ/ก้มลงไปหา (UpdateBody/UpdateLean อ่านจากเป้านี้)
            Vector3 dir = (arm.Limits.Limit(arm, transform.rotation, point) - arm.root.position).normalized;
            Vector3 goal = arm.root.position + dir * (point - arm.root.position).magnitude;
            arm.target.position = Vector3.Lerp(arm.target.position, goal, 1f - Mathf.Exp(-follow * Time.deltaTime));
            arm.Grip = Mathf.MoveTowards(arm.Grip, 0f, gripSpeed * Time.deltaTime); // แบมือเตรียมหยิบ
            arm.Reaching = true;
            if (arms[i] != null) combat.Release(arm, arms[i]);
            return true;
        }

        // เครื่องนี้: ชิ้นที่แขนที่คุมจะหยิบได้ (หรือกำลังเอื้อม/ถืออยู่) เรืองแสง — เห็นก่อนกดว่า F จะหยิบอะไร
        private void UpdateGrabGlow()
        {
            Rigidbody want = null;
            for (int i = 0; i < limbs.Length && !want; i++)
            {
                if (!limbs[i].isArm || !IsMine(i) || down || !Attached(i)) continue;
                want = heldBody[i] ? heldBody[i] : reachTarget[i] ? reachTarget[i] : limbs[i].Grab ? null : GrabCandidate(i);
            }
            SetGlow(want);
        }

        // เรืองด้วย emission บนวัสดุสำเนา (ของจริงไม่ถูกแก้) — เลิกเรือง = คืนวัสดุเดิม ทิ้งสำเนา
        private void SetGlow(Rigidbody body)
        {
            if (body == glowBody) return;
            foreach ((Renderer r, Material[] original) in glowSaved)
            {
                if (!r) continue;
                foreach (Material m in r.materials) Destroy(m);
                r.sharedMaterials = original;
            }
            glowSaved.Clear();
            glowBody = body;
            if (!body) return;
            foreach (Renderer r in body.GetComponentsInChildren<Renderer>())
            {
                glowSaved.Add((r, r.sharedMaterials));
                foreach (Material m in r.materials)
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", grabGlow);
                }
            }
        }

        // Server: ค้าง F = ยึดของ dynamic ที่ใกล้กำปั้นที่สุดด้วยข้อต่อแข็งบน collider มือ (_Hit ซึ่งตามกำปั้นทุก FixedUpdate)
        // ของตามมือไปทุกที่ที่มือไป — ต่อยออกไปก็พุ่งตามไป | ปล่อย F / ไม่มีคนคุม / ชิ้นหลุด / ตัวล้ม = ปล่อย
        // ของที่ถือไปอยู่ที่มือ (ตามท่าเทียบมือตอนจับ) — มือแรกที่ถือเป็นคนพา (ถือสองมือ มือที่สองแค่จับ) | physics = MovePosition (ดันของอื่นได้) ไม่ใช่ = ตั้งท่าตรงๆ ให้ภาพตรงมือทุกเฟรม
        private void PlaceHeld(bool physics)
        {
            for (int i = 0; i < limbs.Length; i++)
            {
                Rigidbody rb = heldBody[i];
                if (!rb || System.Array.IndexOf(heldBody, rb) != i) continue;
                Transform tip = limbs[i].tip;
                Vector3 pos = tip.position + tip.rotation * heldRelPos[i];
                Quaternion rot = tip.rotation * heldRelRot[i];
                if (!physics) { rb.transform.SetPositionAndRotation(pos, rot); continue; }
                heldVel[i] = (pos - rb.position) / Time.fixedDeltaTime;
                for (int j = i + 1; j < limbs.Length; j++) if (heldBody[j] == rb) heldVel[j] = heldVel[i]; // ถือสองมือ ปล่อยมือไหนทีหลังก็ขว้างด้วยความเร็วจริง
                rb.MovePosition(pos);
                rb.MoveRotation(rot);
            }
        }

        private void ServerGrabs()
        {
            for (int i = 0; i < limbs.Length; i++)
            {
                IKLimb limb = limbs[i];
                if (!limb.isArm || !limb.hit) continue;
                bool want = limb.Grab && HasController(i) && Attached(i) && !down && getUp < 0f;
                if (!want || heldBody[i] == null) ReleaseGrab(i); // ของถูกทำลายกลางจับก็เก็บกวาดที่นี่
                if (!want && grappled[i]) EndGrapple(i);
                if (want && heldBody[i] == null && !grappled[i]) TryGrab(i);
                if (want && heldBody[i] == null && !grappled[i]) TryGrapple(i); // ไม่มีของให้จับ = ลองล็อกศัตรูที่เสียหลัก
            }
        }

        private void TryGrab(int i)
        {
            IKLimb limb = limbs[i];
            Vector3 tip = limb.GripCenter;
            Rigidbody best = null;
            Collider touch = null;
            if (grabWant[i] != null && Grabbable(grabWant[i]))
            {
                best = grabWant[i];
                touch = best.GetComponentInChildren<Collider>();
            }
            else
            {
                int n = Physics.OverlapSphereNonAlloc(tip, grabRadius, hitBuffer, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float bestSqr = float.MaxValue;
                for (int k = 0; k < n; k++)
                {
                    Collider c = hitBuffer[k];
                    Rigidbody rb = c.attachedRigidbody;
                    if (rb == null || !Grabbable(rb) || rb.transform.IsChildOf(transform) || Robot.FromCollider(c) == robot) continue;
                    float sqr = (Closest(c, tip) - tip).sqrMagnitude;
                    if (sqr < bestSqr) { bestSqr = sqr; best = rb; touch = c; }
                }
                Rigidbody part = NearDetached(tip, grabRadius, false, out Collider pc); // ชิ้นที่หลุด ใกล้กำปั้นกว่า = เอาชิ้นนั้น
                if (part && (Closest(pc, tip) - tip).sqrMagnitude < bestSqr) { best = part; touch = pc; }
            }
            grabWant[i] = null;
            if (best == null || touch == null) return;

            // ข้อต่อบนมือ (kinematic) — ของตามมือแม้หนัก | ปิดการชนกับตัวหุ่นไม่ให้ตัวหุ่นดันของที่ถืออยู่จนสั่น
            if (best.isKinematic) best.isKinematic = false; // ของออนไลน์ที่ถูกล็อกตอนเล่นคนเดียว (ดู Grabbable) — จับแล้วเป็นฟิสิกส์ปกติ
            best.maxAngularVelocity = 60f; // ค่าเริ่มต้น 7 rad/วิ — ฟาดของยาวเร็วกว่านั้น ของหมุนตามมือไม่ทัน หลุดห่างมือ
            limb.Holding = touch;
            limb.Solve(transform.rotation);
            AimWeapon(limb);
            limb.tip.rotation = limb.target.rotation;
            limb.hit.transform.SetPositionAndRotation(limb.tip.position, limb.tip.rotation);
            limb.hit.position = limb.tip.position;
            limb.hit.rotation = limb.tip.rotation;

            Vector3 grip = limb.GripCenter;
            Quaternion hand = limb.HandFrame(limb.tip.rotation);
            Transform point = GripPointOf(best, tip);
            bool shared = System.Array.IndexOf(heldBody, best) >= 0; // อีกมือถืออยู่ = จับเพิ่มตรงที่แตะ ไม่ย้ายของออกจากมือนั้น
            if (point && !shared)
            {
                // มีจุดจับ: หมุนของให้แกนจุดจับตรงกับมือ — Z = ออกจากกำปั้น, Y = ทางฝ่ามือ, X = แนวด้าม
                hand *= GripInFist(limb, Quaternion.Inverse(point.rotation) * (touch.bounds.center - point.position)); // จับกลับด้าน + ด้ามเฉียงส่วนที่ข้อมือไม่ได้รับ (AimWeapon ท้ายฟังก์ชัน)
                // GripCenter = กลางวงที่นิ้วโอบตอนกำ (IKLimb.BakeGrip) — ด้ามวางตรงนั้นพอดี นิ้วงอรอบได้
                Quaternion rot = hand * Quaternion.Inverse(point.rotation) * best.rotation;
                Vector3 pos = grip + rot * (Quaternion.Inverse(best.rotation) * (best.position - point.position));
                best.transform.SetPositionAndRotation(pos, rot);
                best.position = pos;
                best.rotation = rot;
                best.linearVelocity = best.angularVelocity = Vector3.zero;
                Physics.SyncTransforms();
            }
            else if (!shared)
            {
                Vector3 hold = Closest(touch, touch.bounds.center - (hand * Vector3.forward) * 100f); // ไม่มีจุดจับ: ผิวด้านที่หันเข้าแขนแนบกำปั้น ตัวของยื่นออกหน้ามือ ไม่ทับแขน
                Vector3 pos = best.position + (grip - hold);
                best.transform.position = pos;
                best.position = pos;
                best.linearVelocity = best.angularVelocity = Vector3.zero;
                Physics.SyncTransforms();
            }
            heldBody[i] = best;
            // ถืออยู่ = ของตามมือตรงๆ (kinematic) ไม่ผ่านข้อต่อฟิสิกส์ — ข้อต่อตามช้ากว่ามือหนึ่งจังหวะ ฟาดเร็วแล้วของหลุดห่างมือ
            Quaternion inv = Quaternion.Inverse(limb.tip.rotation);
            heldRelPos[i] = inv * (best.position - limb.tip.position);
            heldRelRot[i] = inv * best.rotation;
            heldVel[i] = Vector3.zero;
            best.isKinematic = true;
            best.interpolation = RigidbodyInterpolation.None; // ตั้งท่าเองทุกเฟรมใน LateUpdate
            foreach (Collider held in best.GetComponentsInChildren<Collider>())
                foreach (Collider mine in robotColliders)
                    if (held && mine && mine.enabled && !Physics.GetIgnoreCollision(held, mine))
                    {
                        Physics.IgnoreCollision(held, mine);
                        heldIgnored[i].Add((held, mine));
                    }
            Debug.Log($"[RobotIKControl] {limb.name} จับ '{best.name}' มวล {best.mass:F0}");
        }

        private void ReleaseGrab(int i)
        {
            Rigidbody was = heldBody[i];
            heldBody[i] = null;
            if (was && System.Array.IndexOf(heldBody, was) < 0) // ไม่มีมือไหนถือแล้ว = กลับเป็นฟิสิกส์
            {
                was.isKinematic = false;
                was.interpolation = RigidbodyInterpolation.Interpolate;
                was.linearVelocity = Vector3.ClampMagnitude(heldVel[i] * 0.2f, 6f); // ปล่อย = วางลง ไม่ใช่ขว้าง — ปล่อยกลางฟาด (ปลายมือเร็ว ~200) ของปลิวไกล | ขว้าง = ServerThrow
                was.angularVelocity = Vector3.zero;
                dropped[i] = was;
                droppedAt[i] = Time.time;
                Debug.Log($"[RobotIKControl] {limbs[i].name} ปล่อย '{was.name}' ความเร็ว {was.linearVelocity.magnitude:F1}");
            }
            // อีกแขนยังถือชิ้นเดียวกัน (จับสองมือ) = ส่งคู่ที่ปิดการชนไว้ให้แขนนั้น ยังไม่คืน — ไม่งั้นของดันตัวหุ่นสั่นทั้งที่ยังถืออยู่
            for (int j = 0; was && j < limbs.Length; j++)
                if (heldBody[j] == was)
                {
                    heldIgnored[j].AddRange(heldIgnored[i]);
                    heldIgnored[i].Clear();
                    break;
                }
            // คืนการชนกับตัวหุ่นหลังของพ้นตัวไปแล้ว (0.6 วิ) — คืนทันทีตอนของยังชิดตัว = ตัวหุ่นดีดของเด้งออก
            foreach ((Collider held, Collider mine) in heldIgnored[i])
                restoreLater.Add((held, mine, Time.time + 0.6f));
            heldIgnored[i].Clear();
        }

        private void RestoreCollisions()
        {
            for (int k = restoreLater.Count - 1; k >= 0; k--)
            {
                (Collider held, Collider mine, float at) = restoreLater[k];
                if (Time.time < at) continue;
                restoreLater.RemoveAt(k);
                if (!held || !mine) continue;
                int j = System.Array.IndexOf(heldBody, held.attachedRigidbody);
                if (j >= 0) heldIgnored[j].Add((held, mine)); // หยิบกลับมาถือแล้ว = ยังไม่คืน ฝากไว้กับแขนที่ถือ คืนตอนปล่อยรอบหน้า
                else Physics.IgnoreCollision(held, mine, false);
            }
        }

        // ================================================================ ชิ้นส่วนหลุด

        private bool Attached(int i) => attachments == null || attachments[i] == null || attachments[i].IsAttached;

        private bool LegsAttached()
        {
            for (int i = 0; i < limbs.Length; i++)
                if (!limbs[i].isArm && !Attached(i)) return false;
            return true;
        }

        private bool Pulling(int i) => (((Online ? pullMask.Value : offlinePull) >> i) & 1) != 0;

        // ทุกเครื่อง: สถานะหลุด/ต่อเปลี่ยน → ภาพ | ชิ้นที่ถูกดึงลอยกลับ | R ค้าง → ขอดึง | Server นับเวลาดึงแล้วสั่งต่อ
        private void UpdateDetach(IKInput input)
        {
            if (attachments == null) return;
            for (int i = 0; i < limbs.Length; i++)
            {
                bool attached = Attached(i);
                if (attached != attachedNow[i]) { attachedNow[i] = attached; OnAttachChanged(i, attached); }
                // แขนอีกข้างถือชิ้นนี้อยู่ (ฟาดเป็นกระบอง) = ไม่แตะ — Tick คืนเป็นฟิสิกส์ทุกเฟรม ของหลุดมือไหลไป | ดึงกลับได้หลังปล่อยมือ
                Rigidbody part = detach.Prop(i);
                if (!part || System.Array.IndexOf(heldBody, part) < 0) detach.Tick(i, limbs[i], Pulling(i), Time.deltaTime);
                if (seat[i] < 1f)
                {
                    seat[i] = Mathf.Min(1f, seat[i] + Time.deltaTime / detach.seatTime);
                    float punch = 1f - seat[i];
                    limbs[i].root.localScale = Vector3.one * (1f + detach.seatPunch * punch * punch);
                }
            }

            int want = input.pull ? PullTarget() : -1;
            if (want != myPull)
            {
                if (myPull >= 0) RequestPull(myPull, false);
                if (want >= 0) RequestPull(want, true);
                myPull = want;
            }

            if (!DrivesBody) return;
            for (int i = 0; i < limbs.Length; i++)
            {
                bool active = pullHeld[i] && !Attached(i) && attachments[i] && attachments[i].RemainingReconnectCooldown <= 0f;
                pullTimer[i] = active ? pullTimer[i] + Time.deltaTime : 0f;
                if (active && pullTimer[i] >= detach.pullTime)
                {
                    attachments[i].ServerReattach();
                    pullHeld[i] = false;
                    active = false;
                    pullTimer[i] = 0f;
                }
                SetPullBit(i, active);
            }
        }

        // R ดึงชิ้นของตัวเองถ้าหลุด ไม่งั้นชิ้นที่หลุดและไม่มีใครคุม (หุ่นเดิม: Host เป็นเจ้าของชิ้นที่ไม่มีใครเลือก)
        private int PullTarget()
        {
            for (int i = 0; i < limbs.Length; i++) if (!Attached(i) && IsMine(i)) return i;
            for (int i = 0; i < limbs.Length; i++) if (!Attached(i) && !HasController(i)) return i;
            return -1;
        }

        private void RequestPull(int i, bool held)
        {
            if (Online) PullRpc(i, held);
            else pullHeld[i] = held;
        }

        private void SetPullBit(int i, bool on)
        {
            byte mask = Online ? pullMask.Value : offlinePull;
            byte next = on ? (byte)(mask | (1 << i)) : (byte)(mask & ~(1 << i));
            if (next == mask) return;
            if (Online) pullMask.Value = next;
            else offlinePull = next;
        }

        private void OnAttachChanged(int i, bool attached)
        {
            IKLimb limb = limbs[i];
            Vector3 outward = Vector3.ProjectOnPlane(limb.root.position - transform.position, Vector3.up).normalized;
            if (attached)
            {
                detach.Reattach(i, limb);
                ragdoll.SetFrozen(limb, false);
                if (limb.hit) limb.hit.detectCollisions = true;
                // เสียบเข้าล็อก: เสียงเหล็กหนัก + บูม + ประกายไฟ + หยุดภาพ + ซูม + จอสั่น, ลำตัวสะเทือน, ชิ้นเด้งแล้วยุบเข้าที่
                ImpactFeel.PlayHit(limb.root.position, outward, detach.reattachImpact);
                leanVel += new Vector3(8f, limb.Side * 14f, 0f);
                if (DrivesBody && feel.bodyFrequency > 0f) bodyVel.y -= feel.landingDip;
                seat[i] = 0f;
                return;
            }
            seat[i] = 1f; // หลุดซ้ำระหว่างเด้งเข้าที่ — ไม่ให้ตัวเด้งตั้ง scale ทับการซ่อน
            limb.Lifted = false;
            limb.Pull = false;
            CancelKick(i); // หลุดกลางท่าเตะ — ต่อกลับแล้วไม่ถีบต่อไปยังจุดเก่า
            if (arms[i] != null) combat.Release(limb, arms[i]);
            ImpactFeel.PlayHit(limb.root.position, outward, 30f);  // ประกายไฟ + จอสั่นที่ข้อต่อ
            // แขนหลุดกระเด็นไปหน้าตัว (ไม่ใช่ข้างตัวฝั่งเดียวกัน) — แขนอีกข้างเอื้อมข้ามลำตัวไม่ได้ (ข้อต่อไหล่) หยิบมาฟาดไม่ถึง | ขาหลุดออกข้างตามเดิม
            Vector3 fling = limb.isArm ? Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized + outward * 0.3f : outward;
            detach.Detach(i, limb, (fling + Vector3.up) * detach.flingSpeed);
            ragdoll.SetFrozen(limb, true);
            if (limb.hit) limb.hit.detectCollisions = false;   // มือ/เท้าที่หลุดไม่ดันของ
        }

        // ================================================================ ล้ม / ลุก

        // ปักหลัก: ขาทั้งสองต่ออยู่ ค้าง F และเท้าติดพื้นทั้งคู่ — คนคุมขาสองคนต้องกดพร้อมกัน (ทุกเครื่องคิดจาก Brace ที่ซิงก์)
        private bool Braced
        {
            get
            {
                int legs = 0, braced = 0;
                for (int i = 0; i < limbs.Length; i++)
                    if (!limbs[i].isArm && Attached(i)) { legs++; if (limbs[i].Brace && !airborne[i]) braced++; }
                return legs >= 2 && braced == legs;
            }
        }

        // กฎล้มเดียวกับหุ่นเดิม (FallRules): สองเท้ายกพร้อมกันนานเกิน grace = ล้ม | หุ่น IK ไม่มีกระโดด/มือจับ/แรงดึงให้กันล้ม
        private void EvaluateFall()
        {
            int legs = 0, grounded = 0;
            for (int i = 0; i < limbs.Length; i++)
                if (!limbs[i].isArm) { legs++; if (!airborne[i]) grounded++; }
            var balance = new BalanceInput
            {
                footCount = legs, groundedFeet = grounded, walkSteppingFeet = legs - grounded,
                balancedFeet = grounded, supportFoot = grounded > 0,
            };
            FallReason reason = fallRules.Evaluate(balance, Time.fixedDeltaTime);
            if (reason != FallReason.None) ServerKnockDown(reason);
        }

        /// <summary>[SERVER / เล่นคนเดียว] ล้ม — จากกฎล้ม, บอส (Robot.ServerKnockDown), ปุ่มทดสอบ B | ระหว่างลุกล้มซ้ำไม่ได้</summary>
        public void ServerKnockDown(FallReason reason)
        {
            if (!DrivesBody || down || getUp >= 0f) return;
            if (reason == FallReason.KnockedDown && (Braced || ArmsGuarding)) // แรงกระแทกจากข้างนอก (บอส/ทดสอบ B) — เท้าเองยกพร้อมกันยังล้มตามกติกาหลัก
            {
                Debug.Log($"[RobotIKControl] {(Braced ? "ปักหลักสองขา" : "ยกการ์ดสองแขน")} — กันล้มได้");
                for (int i = 0; i < limbs.Length; i++)
                    if (!limbs[i].isArm) ImpactFeel.PlayFootstep(new Vector3(limbs[i].target.position.x, GroundY(limbs[i].target.position), limbs[i].target.position.z), 1f, 0.6f);
                return;
            }
            Debug.Log($"[RobotIKControl] ล้ม: {reason}");
            standVoters.Clear();
            if (Online)
            {
                fallHipsPos.Value = ragdoll.Hips.position;
                fallHipsRot.Value = ragdoll.Hips.rotation;
                standVotes.Value = 0; // ไม่ให้จอโชว์ยอดโหวตของรอบล้มที่แล้ว
                fallen.Value = true;
            }
            SetDown(true);
        }

        // นอนอยู่: Server ประกาศท่าสะโพก + นับโหวตลุก (เช็กทุกรอบ: คนที่ยังไม่กดออกจากห้อง/ปล่อยชิ้น = ไม่ต้องรอเขาแล้ว)
        // client สะโพกตามที่ประกาศ
        private void FixedDown()
        {
            if (!DrivesBody)
            {
                ragdoll.FollowHips(fallHipsPos.Value, fallHipsRot.Value, 1f - Mathf.Exp(-remoteSmoothing * Time.fixedDeltaTime));
                return;
            }
            if (!Online) return; // เล่นคนเดียว: ลุกตอนกด Q ใน Update
            fallHipsPos.Value = ragdoll.Hips.position;
            fallHipsRot.Value = ragdoll.Hips.rotation;

            int controllers = Controllers(out int voted);
            standVotes.Value = voted;
            if (controllers > 0 && voted == controllers && LegsAttached()) StandUp(); // ไม่มีใครคุมชิ้นเลย = นอนต่อ | ขาหลุด = ดึงกลับก่อน
        }

        // คนที่คุมชิ้นของหุ่นนี้ (คนละชิ้นนับครั้งเดียว) และในนั้นกด Q แล้วกี่คน (voted รู้เฉพาะ Server)
        private int Controllers(out int voted)
        {
            int count = 0;
            voted = 0;
            for (int i = 0; i < limbs.Length; i++)
            {
                ulong c = ControllerOf(i);
                bool repeat = false;
                for (int j = 0; j < i; j++) repeat |= ControllerOf(j) == c;
                if (c == LimbSelection.NoClient || repeat) continue;
                count++;
                if (standVoters.Contains(c)) voted++;
            }
            return count;
        }

        // [SERVER / เล่นคนเดียว] ลุก — ทุกเครื่องเริ่มไล่ท่ากลับยืน
        private void StandUp()
        {
            if (Online) fallen.Value = false;
            SetDown(false);
        }

        // ทุกเครื่อง: เริ่มนอน (ragdoll คุม) / เริ่มลุก (ตัวหุ่นย้ายไปที่สะโพก, เท้าวางข้างตัว, ไล่ท่าจากท่านอนกลับเป็นท่ายืน)
        private void SetDown(bool value)
        {
            down = value;
            if (robot) robot.SetDownExternally(value); // ศัตรูรู้ว่านอนอยู่ (ท่ากระแทกพื้นโดนทุกชิ้น)
            votedStand = false;
            for (int i = 0; i < limbs.Length; i++) { limbs[i].Lifted = false; limbs[i].Pull = false; limbs[i].Brace = false; }
            if (value)
            {
                if (bodyCollider) bodyCollider.enabled = false; // กล่องลำตัวค้างที่เดิม ไม่ให้ขวางของ/ศัตรูตอนนอน
                Vector3 push = Vector3.ProjectOnPlane(bodyVel, Vector3.up);
                if (push.sqrMagnitude < 0.25f) push = -transform.forward; // ยืนนิ่ง = หงายหลัง
                ragdoll.Enforce(true, !DrivesBody);
                ragdoll.SetPhysics(true, push.normalized * ragdoll.topple);
                return;
            }

            Vector3 hips = DrivesBody ? ragdoll.Hips.position : fallHipsPos.Value;
            // หันตามท่านอน (ทุกเครื่องอ่านจากสะโพกที่ซิงก์): คว่ำ = ดันตัวขึ้นหันทางหัว | หงาย = ลุกนั่งหันทางเท้า — ไม่หมุนกลับทิศก่อนล้ม (ตัวบิดวนกลางท่า)
            Vector3 face = ragdoll.FaceDown(out Vector3 head) ? head : -head;
            Quaternion facing = face.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(face.normalized) : DrivesBody ? bodyRot : transform.rotation;
            getUpLead = limbs[0].root.position.y <= limbs[1].root.position.y ? 0 : 1; // ตะแคงไปข้างไหน ยันด้วยมือข้างนั้น
            ragdoll.BeginBlend();
            ragdoll.Enforce(false, false);
            ragdoll.SetPhysics(false, Vector3.zero);

            Vector3 stand = new Vector3(hips.x, 0f, hips.z);
            stand.y = GroundY(stand) - crouch;
            if (DrivesBody)
            {
                bodyPos = stand;
                bodyVel = Vector3.zero;
                bodyRot = facing;
                yawVel = 0f;
                body.position = stand;
                body.rotation = facing;
                if (netTransform && netTransform.IsSpawned) netTransform.Teleport(stand, facing, transform.localScale); // client ไม่ไถลจากจุดเดิม
                else transform.SetPositionAndRotation(stand, facing);
            }
            for (int i = 0; i < limbs.Length; i++)
            {
                IKLimb limb = limbs[i];
                if (limb.isArm)
                {
                    // ตอนนอนเป้าแขนถูกลากไปตามเคอร์เซอร์ — กลับท่าการ์ด/ท่าตั้งต้น แล้วสปริงมือเริ่มใหม่ที่ตัวหุ่นจุดใหม่
                    limb.target.localPosition = arms[i].desired;
                    combat.Release(limb, arms[i]);
                    if (Online && IsServer) states[i] = LimbNetState.Capture(limb);
                    continue;
                }
                Vector3 rest = stand + facing * new Vector3(legRest[i].x, 0f, legRest[i].z);
                Vector3 p = rest + facing * Vector3.forward * (limb.Reach * (i == RearLeg ? -0.45f : 0.25f)); // ท่าย่อ: เท้าเหลื่อมหน้า-หลัง ไม่ใช่ยืนตรงรอไว้แล้ว
                p.y = GroundY(p) + limb.RestHeight;
                if (i == RearLeg) // เท้าหลังก้าวขึ้นมาที่ท่ายืนตอนดันตัวขึ้น (GetUpPose)
                {
                    getUpStepFrom = p;
                    getUpStepTo = new Vector3(rest.x, GroundY(rest) + limb.RestHeight, rest.z);
                }
                limb.target.SetPositionAndRotation(p, facing * limb.RestRotation);
                airborne[i] = false;
                CancelKick(i);
                planted[i] = p;
                if (Online && IsServer) states[i] = LimbNetState.Capture(limb); // ขาที่ไม่มีใครคุมไม่ไหลกลับไปที่เดิม
            }
            getUp = 0f;
            getUpPrev = -1f;
        }

        // ลุกแบบหุ่นหนัก (ทีมค้นหา 2026-10-29) ทุกเครื่องคิดเองจากเวลา getUp 0..1:
        // ไล่ท่านอน → บิด/เอียงไปข้างหนึ่ง มือข้างนั้นยันพื้นข้างตัวก่อน อีกมือตามมาวางหน้าไหล่ (Blend ช่วงแรก) → แขนดันพื้น (ศอกเหยียด สะโพกยกนิด)
        // → ดันตัวขึ้นช้า-เร็ว-ช้า คลายบิด กำปั้นยกกลับการ์ดตอนสะโพกขึ้นราวครึ่ง ลำตัวตั้งทีหลังสะโพก → เท้าหลังก้าวขึ้นมา → ยวบแล้วนิ่ง
        // มือ/เท้าที่รับน้ำหนักปักจุดเดียวในโลก IK แก้ข้อต่อเอง (เดิมไล่มุมข้อต่อตรงจากท่านอน = ตัวลอยขึ้นเหมือนหุ่นเชิด)
        private const float GetUpLeadAt = 0.17f, GetUpPlantAt = 0.32f, GetUpRiseFrom = 0.44f, GetUpRiseTo = 0.86f, GetUpStepStart = 0.62f, GetUpStepEnd = 0.82f;
        private void GetUpPose(float t)
        {
            float leg = limbs[RearLeg].Reach;
            float press = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(GetUpPlantAt, GetUpRiseFrom, t)); // แขนดันพื้นก่อนลุกจริง
            float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(GetUpRiseFrom, GetUpRiseTo, t));
            float drop = leg * ((0.62f - 0.06f * press) * (1f - rise) + 0.05f * Bump(t, GetUpRiseTo, 0.97f));
            model.transform.localPosition = modelRest + Vector3.forward * bodyPose.z + Vector3.down * (weaponCrouch + drop);
            if (spine)
            {
                float side = limbs[getUpLead].Side;
                float twist = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(GetUpRiseFrom, 0.7f, t)); // บิด/เอียงไปข้างมือนำ คลายตอนดันขึ้น
                float pitch = (70f - 12f * press) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(GetUpRiseFrom + 0.05f, 0.92f, t)));
                spine.rotation = Quaternion.AngleAxis(side * 35f * twist, transform.up) * Quaternion.AngleAxis(-side * 15f * twist, transform.forward)
                               * Quaternion.AngleAxis(pitch, transform.right) * spine.rotation;
            }

            float plant = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.58f, 0.76f, t)); // ดันพื้นจนสะโพกขึ้นราวครึ่ง แล้วยกกลับการ์ด
            for (int i = 0; i < limbs.Length; i++)
            {
                IKLimb arm = limbs[i];
                if (!arm.isArm || !Attached(i)) continue;
                bool lead = i == getUpLead;
                if (getUpPrev < 0f) // เฟรมแรก: ไหล่อยู่ท่าบิด-ก้มแล้ว — มือนำลงข้างตัวฝั่งที่บิดไป, อีกมือหน้าไหล่
                {
                    Vector3 s = arm.root.position + (lead ? transform.right * (arm.Side * arm.Reach * 0.35f) + transform.forward * (arm.Reach * 0.1f)
                                                          : transform.forward * (arm.Reach * 0.3f));
                    getUpPlant[i] = new Vector3(s.x, GroundY(s), s.z);
                }
                arm.target.position = Vector3.Lerp(arm.target.parent.TransformPoint(arms[i].desired), getUpPlant[i] + Vector3.up * (arm.Lower * 0.15f), plant);
                if (Crossed(t, lead ? GetUpLeadAt : GetUpPlantAt)) ImpactFeel.PlayFootstep(getUpPlant[i], 0.7f, 0.25f); // ฝุ่นเท้าเต็มขนาดบังทั้งตัว
            }

            if (!Attached(RearLeg) || getUpPrev >= GetUpStepEnd) return; // ก้าวเสร็จแล้ว = เท้ากลับไปตามระบบเดินปกติ
            float u = Mathf.InverseLerp(GetUpStepStart, GetUpStepEnd, t);
            limbs[RearLeg].target.position = Vector3.Lerp(getUpStepFrom, getUpStepTo, Mathf.SmoothStep(0f, 1f, u)) + Vector3.up * (Mathf.Sin(Mathf.PI * u) * leg * 0.25f);
            if (Crossed(t, GetUpStepEnd)) // เสียง/ฝุ่นเท้าลง DetectLandings เล่นให้ครั้งเดียวเหมือนก้าวปกติ
            {
                planted[RearLeg] = getUpStepTo;
                if (Online && IsServer) states[RearLeg] = LimbNetState.Capture(limbs[RearLeg]); // เครื่องอื่นไม่เด้งเท้ากลับจุดก่อนก้าว (ค่าที่ซิงก์ไว้ตอนเริ่มลุก)
            }
        }

        private bool Crossed(float t, float at) => getUpPrev < at && t >= at;
        private static float Bump(float t, float from, float to) => t <= from || t >= to ? 0f : Mathf.Sin(Mathf.PI * (t - from) / (to - from));

        // นอนอยู่ (ทุกชิ้นเหมือนกัน): เคอร์เซอร์ = จุดที่มือ/เท้าเอื้อมไป บนระนาบผ่านไหล่/สะโพกที่หันหากล้อง ไม่เกินความยาวแขนขา
        // คลิกค้าง = ดึงจริง (แรงใน FixedUpdate) + กำมือ | ปล่อย = ห้อยตามฟิสิกส์ — ไว้ยันพื้น คลาน ปัดของ
        private void DriveDown(IKLimb limb, IKInput input)
        {
            if (!input.looking && cam)
            {
                var plane = new Plane(cam.transform.forward, limb.root.position);
                if (plane.Raycast(input.ray, out float enter))
                {
                    Vector3 goal = limb.root.position + Vector3.ClampMagnitude(input.ray.GetPoint(enter) - limb.root.position, limb.Reach);
                    limb.target.position = Vector3.Lerp(limb.target.position, goal, 1f - Mathf.Exp(-follow * Time.deltaTime));
                }
            }
            limb.Pull = input.held;
            if (limb.isArm) limb.Grip = Mathf.MoveTowards(limb.Grip, input.held ? 1f : 0f, gripSpeed * Time.deltaTime);
        }

        // พายุทราย: เท้าที่ปักอยู่ไถลตามลม แม้ไม่ก้าว — ตัวหุ่นไหลไปกลางเท้าเอง (UpdateBody)
        // เครื่องที่ถือเป้าเท้านั้นเป็นคนเลื่อน (คนคุม / Server สำหรับขาว่าง) แล้วซิงก์ทางเดิม | คืน true เมื่อเลื่อน
        private bool WindSlide(int i)
        {
            Sandstorm storm = Sandstorm.Current;
            IKLimb leg = limbs[i];
            if (!storm || storm.Intensity <= 0f || down || getUp >= 0f || airborne[i] || leg.Lifted || leg.KickPose || !Attached(i)) return false;
            float dig = leg.Brace ? 0.25f : 1f; // F ค้าง = ย่อปักหลัก ไถลน้อยลง
            leg.target.position = OnGround(leg, leg.target.position + storm.Wind * (storm.footSlide * dig * Time.deltaTime));
            return true;
        }

        // รอยลากใต้เท้าที่ไถล (ไม่ได้ยกก้าว แต่เป้าเท้าเลื่อน) — แอ่งทรายนอนราบบนพื้น ค่อยๆ จางเอง (แบบภาพตัวอย่างผู้ใช้ 2026-11-05)
        // วัสดุ SandSkid = โหมดคูณสี: ทำให้พื้นจริงข้างใต้เข้มลงเป็นแอ่งนุ่ม (T_SandDrag) — พื้นเป็น shader การ์ตูน
        // วัสดุที่คิดแสงเอง (Lit) แรเงาคนละแบบ เห็นเป็นกรอบเหลี่ยม/ดูลอย | คูณสีได้สีและแสงของพื้นเองทั้งหมด
        private void UpdateSkid(int i, IKLimb leg)
        {
            Vector3 foot = leg.target.position;
            float speed = (foot - lastFoot[i]).magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
            lastFoot[i] = foot;
            bool sliding = !airborne[i] && !leg.Lifted && !down && speed > 0.05f && speed < leg.Reach; // เร็วเกินแขนขา = ซิงก์กระโดด/วาร์ป ไม่ใช่ไถล
            TrailRenderer t = skids[i];
            if (!t)
            {
                Sandstorm storm = Sandstorm.Current;
                if (!sliding || !storm || !storm.skidMaterial) return;
                t = skids[i] = new GameObject($"{leg.name}_Skid").AddComponent<TrailRenderer>();
                t.sharedMaterial = storm.skidMaterial;
                t.alignment = LineAlignment.TransformZ;
                t.transform.rotation = Quaternion.LookRotation(Vector3.up); // แกน Z ชี้ขึ้น = แถบนอนบนพื้น
                t.time = 20f;
                t.minVertexDistance = leg.Reach * 0.05f;
                t.widthMultiplier = leg.Reach * 0.4f; // กว้างกว่าฝ่าเท้า (ขอบจางนุ่มกินความกว้าง) — แคบแล้วดูเป็นเส้น ไม่ใช่รอยเท้าลาก
                t.textureMode = LineTextureMode.Tile; // ความไม่สม่ำเสมอของทรายต่อกันตามระยะที่ลาก ไม่ยืดตามความยาวรอย
                t.textureScale = new Vector2(1f / (leg.Reach * 2f), 1f);
                t.numCapVertices = 0; // หัวมนดึงขอบ texture มาโผล่เป็นรูปตัว D สีอ่อน — ปลายรอยใช้ alpha จางแทน
                // หัวรอย (ใต้เท้า) ชัด → ท้ายรอยจางกลืนพื้น (ทรายพัดกลบ)
                var fade = new Gradient();
                fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                             new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
                t.colorGradient = fade;
                t.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                t.receiveShadows = false;
            }
            t.transform.position = new Vector3(foot.x, GroundY(foot) + 0.02f, foot.z);
            t.emitting = sliding;
        }

        // ขาที่ไม่มีคนคุม (Server/เล่นคนเดียว): เท้าที่ปักค่อยๆ บิดตามทิศลำตัว — ตัวหันตามค่าเฉลี่ยเท้า ถ้าเท้านี้นิ่ง
        // ตัวหันได้แค่ครึ่งเดียวแล้วติด | คืน true เมื่อขยับ (หยุดเมื่อต่างไม่ถึง 0.5° ไม่ส่ง state ทุกเฟรม)
        private bool PivotIdleFoot(IKLimb leg)
        {
            float delta = Mathf.DeltaAngle(FootYaw(leg), transform.eulerAngles.y);
            if (feel.idleFootPivot <= 0f || leg.Lifted || Mathf.Abs(delta) < 0.5f) return false;
            leg.target.rotation = Quaternion.Euler(0f, delta * (1f - Mathf.Exp(-feel.idleFootPivot * Time.deltaTime)), 0f) * leg.target.rotation;
            return true;
        }

        // ทิศเท้าในโลก = มุมหมุนของเป้าเท้าเทียบท่าตั้งต้น
        private static float FootYaw(IKLimb leg) => (leg.target.rotation * Quaternion.Inverse(leg.RestRotation)).eulerAngles.y;

        // หุ่นและ collider มือ/เท้าต้องอยู่ layer Ignore Raycast ไม่งั้นยิงโดนตัวเองแล้วตัวลอยขึ้นเรื่อยๆ
        // พื้นที่เท้าเหยียบได้ใต้จุด p — เดิมเอาอะไรก็ได้ที่ ray โดนก่อน เท้าเลยขึ้นไปยืนบนมือ/แขนตัวเองหรือบนตัวไคจู
        private float GroundY(Vector3 p) =>
            FirstWalkable(new Ray(p + Vector3.up * 100f, Vector3.down), 300f, out RaycastHit hit) ? hit.point.y : 0f;

        private static readonly RaycastHit[] walkHits = new RaycastHit[32];

        // จุดแรกตามแนว ray ที่เหยียบได้ (ข้ามตัวเอง/ของที่ถือ/ศัตรู — ยิงทะลุไปเจอพื้นข้างหลังแทน)
        private bool FirstWalkable(Ray ray, float distance, out RaycastHit best)
        {
            best = default;
            float nearest = float.MaxValue;
            int n = Physics.RaycastNonAlloc(ray, walkHits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < n; k++)
                if (walkHits[k].distance < nearest && Walkable(walkHits[k].collider)) { best = walkHits[k]; nearest = walkHits[k].distance; }
            return nearest < float.MaxValue;
        }

        // เหยียบไม่ได้: ตัวหุ่นเอง (กระดูก/ragdoll), มือ-เท้า _Hit, ของที่มือถืออยู่, ศัตรู | กล่อง/ที่สูงในฉากยังขึ้นไปยืนได้ตามเดิม
        private bool Walkable(Collider c)
        {
            if (c.transform.IsChildOf(transform) || (robot && Robot.FromCollider(c) == robot)) return false;
            Rigidbody body = c.attachedRigidbody;
            if (body)
                for (int i = 0; i < limbs.Length; i++)
                    if (body == limbs[i].hit || (heldBody != null && body == heldBody[i])) return false;
            return c.GetComponentInParent<EnemyController>() == null;
        }

        // ================================================================ network

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Everyone)]
        private void SendLimbRpc(int index, LimbNetState s, RpcParams rpcParams = default)
        {
            if (index < 0 || index >= limbs.Length || ControllerOf(index) != rpcParams.Receive.SenderClientId) return;
            if (MatchSession.Current != null && !MatchSession.Current.IsPlaying) return;

            // Server ตรวจซ้ำ — ไม่เชื่อเป้าจาก client ตรงๆ
            IKLimb limb = limbs[index];
            Vector3 world = limb.isArm ? transform.TransformPoint(s.pos) : s.pos;
            // นอนอยู่: ไม่เกินความยาวแขนขาจากโคน | แขนตอนยืน: ข้อจำกัดข้อต่อ
            // ขา (หุ่นเดิม SetFootTargetRpc ServerReachMargin 1.5): ก้าวยาวเป้าอยู่ไกลกว่าขาจริงได้ ตัวไหลตามไปเอง —
            // ตัดที่ความยาวขาพอดีแล้วเท้าเครื่องอื่นลอยค้าง และ Server นับเป็นเท้ายก (กฎสองเท้ายก = ล้ม)
            float reach = down ? limb.Reach : limb.Reach * 1.5f;
            // แขนตอนยืน: ทิศตามข้อต่อ ระยะไม่เกิน 2.5 เท่าแขน (เอื้อมหยิบของบนพื้น = เป้าต่ำกว่าแขน ตัวย่อ/ก้มตามเป้านี้)
            world = down || !limb.isArm ? limb.root.position + Vector3.ClampMagnitude(world - limb.root.position, reach)
                                        : limb.root.position + (limb.Limits.Limit(limb, transform.rotation, world) - limb.root.position).normalized
                                          * Mathf.Min((world - limb.root.position).magnitude, limb.Reach * 2.5f);
            s.pos = limb.isArm ? transform.InverseTransformPoint(world) : world;
            s.grip = Mathf.Clamp01(s.grip);
            states[index] = s;
        }

        // ปุ่มทดสอบ B — RPC ต้องมีทุก build (ตาราง RPC ตรงกันทุกเครื่อง) แต่ build จริงไม่ทำอะไร กัน client แก้เกมสั่งล้มรัวๆ
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void KnockDownRpc(RpcParams rpcParams = default)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            for (int i = 0; i < limbs.Length; i++)
                if (ControllerOf(i) == rpcParams.Receive.SenderClientId)
                {
                    ServerKnockDown(FallReason.KnockedDown);
                    return;
                }
#endif
        }

        // ดึงได้: ชิ้นที่ตัวเองคุม หรือชิ้นที่ไม่มีใครคุม
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void PullRpc(int index, bool held, RpcParams rpcParams = default)
        {
            if (index < 0 || index >= limbs.Length) return;
            ulong controller = ControllerOf(index);
            if (controller != LimbSelection.NoClient && controller != rpcParams.Receive.SenderClientId) return;
            pullHeld[index] = held;
        }

        // ปุ่มทดสอบ X — RPC ต้องมีทุก build (ตาราง RPC ตรงกันทุกเครื่อง) แต่ build จริงไม่ทำอะไร
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void DetachTestRpc(int index, RpcParams rpcParams = default)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (index >= 0 && index < limbs.Length && ControllerOf(index) == rpcParams.Receive.SenderClientId && attachments[index])
                attachments[index].ServerDetach();
#endif
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void StandVoteRpc(RpcParams rpcParams = default)
        {
            if (down) standVoters.Add(rpcParams.Receive.SenderClientId); // นับเฉพาะคนที่คุมชิ้นอยู่ (FixedDown กรอง)
        }

        // ================================================================ UI

        // ลูกศรทิศฟันกลางล่างจอ (หมุนตามมุมฟัน): ใหญ่ = ท่าที่เพิ่งรับ (จางใน 0.5 วิ), เล็กข้างๆ = ท่าที่จองต่อไว้ — บอกผู้เล่นว่ากดติดก่อนท่าง้างจบ
        private GUIStyle cueStyle;
        private void DrawSlashCue()
        {
            float a = 1f - (Time.time - cueTime) / 0.5f;
            if (cueKind < 0 || (a <= 0f && cueNext < 0)) return;
            cueStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            Color old = GUI.color;
            cueStyle.fontSize = 110;
            GUI.color = new Color(1f, 0.85f, 0.4f, Mathf.Clamp01(a));
            CueArrow(new Rect(Screen.width * 0.5f - 80f, Screen.height * 0.62f, 160f, 160f), cueAngle);
            if (cueNext >= 0)
            {
                cueStyle.fontSize = 56;
                GUI.color = new Color(1f, 1f, 1f, 0.85f);
                CueArrow(new Rect(Screen.width * 0.5f + 70f, Screen.height * 0.66f, 90f, 90f), cueNextAngle);
            }
            GUI.color = old;
        }

        // "↑" หมุนตามเข็มบนจอ: มุม 90° (ขึ้น) = ไม่หมุน, 0° (ขวา) = หมุน 90°
        private void CueArrow(Rect r, float angle)
        {
            Matrix4x4 m = GUI.matrix;
            GUIUtility.RotateAroundPivot(90f - angle, r.center);
            GUI.Label(r, "↑", cueStyle);
            GUI.matrix = m;
        }

        private void OnGUI()
        {
            GUI.skin.label.fontSize = 18;
            if (Online && (MatchSession.Current == null || !MatchSession.Current.IsPlaying)) return; // หน้าเลือกชิ้นส่วนแสดงอยู่

            DrawSlashCue();
            if (showInfoBox) DrawInfoBox();
            if (aimArm >= 0 && cam) DrawReticle();
            if (!down && cam) DrawFootMarker();
        }

        private void DrawInfoBox()
        {
            GUILayout.BeginArea(new Rect(10, 10, 900, 280), GUI.skin.box);
            if (!Online) GUILayout.Label($"เล่นคนเดียว: {limbs[selected].name} (กด 1-4 สลับ)");
            else
            {
                string mine = "ไม่ได้คุมชิ้นไหน";
                for (int i = 0; i < limbs.Length; i++)
                    if (IsMine(i)) mine = limbs[i].name;
                GUILayout.Label($"คุณคุม: {mine}");
            }
            if (robot)
            {
                var hp = new System.Text.StringBuilder("เลือด: ");
                for (int i = 0; i < limbs.Length; i++)
                {
                    LimbHealth health = robot.GetLimb((LimbSlot)i)?.Health;
                    if (health) hp.Append($"{limbs[i].name} {Mathf.CeilToInt(health.Hp)}/{Mathf.CeilToInt(health.MaxHp)}{(Attached(i) ? "" : " (หลุด)")}   ");
                }
                if (hp.Length > "เลือด: ".Length) GUILayout.Label(hp.ToString());
                for (int i = 0; i < limbs.Length; i++)
                    if (heldBody[i]) GUILayout.Label($"{limbs[i].name} จับ: {heldBody[i].name} (มวล {heldBody[i].mass:F0})");
            }
            for (int i = 0; i < limbs.Length; i++)
                if (IsMine(i) && !Attached(i))
                {
                    float wait = attachments[i] ? attachments[i].RemainingReconnectCooldown : 0f;
                    GUILayout.Label(wait > 0f ? $"{limbs[i].name} หลุด! รอ {wait:F1} วิ แล้วกด R ค้างดึงกลับ" : $"{limbs[i].name} หลุด! กด R ค้างดึงกลับ");
                }
            if (down && !LegsAttached()) GUILayout.Label("ขาหลุดอยู่ — ดึงขากลับ (R ค้าง) ก่อนถึงจะลุกได้");
            if (!down && strideCombo > 0) GUILayout.Label($"จังหวะเดิน x{strideCombo} — ก้าวสลับซ้าย-ขวาต่อไปเร็วขึ้น");
            for (int i = 0; i < limbs.Length; i++)
                if (IsMine(i) && !limbs[i].isArm && limbs[i].Lifted && liftHold[i] >= stompHold) GUILayout.Label("ปล่อยคลิก = กระทืบ! (ของรอบเท้าลอยขึ้น)");
            if (!down && Braced) GUILayout.Label("ปักหลักสองขา — แรงกระแทกล้มไม่ได้");
            else if (!down) for (int i = 0; i < limbs.Length; i++)
                if (!limbs[i].isArm && limbs[i].Brace) { GUILayout.Label($"{limbs[i].name} ย่อ — อีกขากด F ค้างด้วย = ปักหลักกันล้ม"); break; }
            if (down)
            {
                int need = Online ? Controllers(out _) : 1;
                GUILayout.Label(!Online ? "หุ่นล้ม! คลิกค้าง = ดึงแขน/ขาไปที่เคอร์เซอร์ (มือแตะพื้น = ลากตัวไป) | กด Q เพื่อลุก"
                    : votedStand ? $"หุ่นล้ม! คลิกค้าง = ดึงแขน/ขา | รอเพื่อนกด Q ({standVotes.Value}/{need})"
                    : $"หุ่นล้ม! คลิกค้าง = ดึงแขน/ขาไปที่เคอร์เซอร์ | ทุกคนกด Q เพื่อลุก ({standVotes.Value}/{need})");
            }
            string armHelp = combat.pointAndClick
                ? "แขน: เคอร์เซอร์เล็ง, คลิกซ้ายค้าง = ชาร์จ ปล่อย = ต่อย"
                : "แขน: มือตามเคอร์เซอร์, Shift+ล้อ = ดัน/ดึงมือ, คลิกซ้ายค้าง = กำ";
            armHelp += grabToggle ? ", F = จับของ (กดครั้งเดียว) / ถืออยู่กด F = ขว้างไปทางเคอร์เซอร์" : ", F ค้าง = จับของที่แตะมือ";
            armHelp += ", Shift ค้าง = การ์ด (กดทันก่อนโดน = ปัดป้อง), F ใส่ศัตรูเสียหลัก = ล็อก, สองแขนโดนพร้อมกัน = Double Impact";
            armHelp += weaponFreeAim ? " | ถือของ: คลิกซ้ายค้าง + ลากเมาส์ = ฟาด ปล่อย = ห้อยแขน" : " | ถือของ: คลิกซ้าย = คอมโบ, ชี้จุดที่จะฟันผ่าน → กดค้าง + ลากไปทิศไหนก็ได้ = แนวฟัน (ค้างไว้ = ชาร์จ) ปล่อย = ฟัน (ลากระหว่างฟัน = ฟาดต่อ) | จอย: X/R1 = คอมโบ, ดีดสติ๊กขวา = ทิศฟัน";
            GUILayout.Label($"{armHelp} | ขา: ชี้พื้น + คลิก = ก้าวไปตรงนั้น (ค้าง = เท้าลอยตามเคอร์เซอร์ ปล่อย = วาง, ก้าวเฉียง = เลี้ยว, ยกค้าง ≥{stompHold:0.#} วิ แล้วปล่อย = กระทืบ, Shift = ถีบ (โดนของ = ของพุ่ง), F ค้าง = ย่อ / สองขาพร้อมกัน = ปักหลัก) | ล้อ = ซูม | คลิกขวาค้าง = หันกล้อง | R ค้าง = ดึงชิ้นที่หลุดกลับ | B = ทดสอบล้ม | X = ทดสอบหลุด");
            GUILayout.EndArea();
        }

        private GUIStyle reticleStyle, markerStyle, reachStyle;

        // หุ่นเดิม (LegInput.OnGUI): ◈ ที่จุดที่เท้าจะลง (ตัดที่ระยะเอื้อมแล้ว อาจต่างจากเคอร์เซอร์) + ระยะเป็น % ของที่ก้าวได้ไกลสุด
        private void DrawFootMarker()
        {
            int i = 0;
            while (i < limbs.Length && (limbs[i].isArm || !IsMine(i))) i++;
            if (i == limbs.Length) return;
            Vector3 mark = limbs[i].Lifted ? stepAim[i] : footMark[i];
            Vector3 screen = cam.WorldToScreenPoint(new Vector3(mark.x, GroundY(mark), mark.z));
            if (screen.z <= 0f) return;

            markerStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            reachStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            float x = screen.x, y = Screen.height - screen.y;
            Vector3 hip = limbs[i].root.position;
            float reach = new Vector2(mark.x - hip.x, mark.z - hip.z).magnitude / (limbs[i].Reach * 0.9f);
            string pct = $"{Mathf.RoundToInt(Mathf.Clamp01(reach) * 100f)}%";

            Color old = GUI.color, shadow = new Color(0f, 0f, 0f, 0.6f), tint = new Color(0.5f, 0.85f, 1f);
            GUI.color = shadow;
            GUI.Label(new Rect(x - 13f, y - 13f, 30f, 30f), "◈", markerStyle);
            GUI.Label(new Rect(x - 29f, y + 13f, 60f, 20f), pct, reachStyle);
            GUI.color = tint;
            GUI.Label(new Rect(x - 14f, y - 14f, 30f, 30f), "◈", markerStyle);
            GUI.Label(new Rect(x - 30f, y + 12f, 60f, 20f), pct, reachStyle);
            GUI.color = old;
        }

        // ◎ ที่จุดเล็ง: แดง = เล็งโดนของที่ต่อยได้, ส้ม = ของที่เล็งอยู่ไกลเกินแขน, ขาว = ปกติ + แถบชาร์จใต้เป้า
        private void DrawReticle()
        {
            Vector3 sp = cam.WorldToScreenPoint(aimPoint);
            if (sp.z <= 0f) return;
            IKCombat.Arm a = arms[aimArm];

            reticleStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 40, alignment = TextAnchor.MiddleCenter };
            reticleStyle.normal.textColor = !aimInReach ? new Color(1f, 0.55f, 0f) : aimOnTarget ? Color.red : Color.white;
            var r = new Rect(sp.x - 30f, Screen.height - sp.y - 30f, 60f, 60f);
            GUI.Label(r, "◎", reticleStyle);

            if (a.state != IKCombat.State.Charging) return;
            float c = combat.Charge01(a);
            GUI.Box(new Rect(r.x, r.yMax, 60f, 10f), GUIContent.none);
            Color old = GUI.color;
            GUI.color = c >= 1f ? Color.red : Color.yellow;
            GUI.DrawTexture(new Rect(r.x + 1f, r.yMax + 1f, 58f * c, 8f), Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}

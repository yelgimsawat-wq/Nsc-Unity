using System;
using Nsc.Combat;
using Nsc.Robots;
using UnityEngine;

namespace Nsc.Limbs
{
    public enum KickState : byte { Idle, Charging, Kicking, Recovering }

    /// <summary>
    /// Charge Kick (กด Shift ค้างเพื่อชาร์จ ปล่อยเพื่อเตะ) — ลอจิกฝั่ง server ของการเตะหนึ่งครั้ง
    /// ง้างขาถอยหลังตามระดับพลังระหว่างชาร์จ แล้วสะบัดไปตามทิศที่เล็งด้วยสปริงแบบเดียวกับแขน + แรงเร่ง F = ma
    ///
    /// ระหว่างง้าง/เตะ ท่านี้เป็น "เจ้าของเท้า" — LegController หยุดสปริงก้าวเดินและตัวล็อกยืนของมันเอง
    /// </summary>
    [Serializable]
    public class KickSkill : IStrikeSource
    {
        [Header("Charge Kick")]
        [Min(0.05f)] public float chargeTime = 1f;
        [Range(0f, 1f)] public float minimumCharge = 0.12f;
        // จูนสำหรับหุ่นสเกลจริง (maxLegLength 14) — เตะเบาสุดแรงกว่าหมัดเบา / ชาร์จเต็มแรงกว่าหมัดสุด
        [Min(0f)] public float minSpeed = 30f;
        [Min(0f)] public float maxSpeed = 70f;
        [Tooltip("แรงเร่งตอนดีดออก — เท้าถึงความเร็วสูงสุดใน ~0.08 วิ = จังหวะสะบัดคม")]
        [Min(0f)] public float acceleration = 1100f;
        [Min(0.05f)] public float duration = 0.3f;
        [Tooltip("ระยะที่ตัดจบท่า — ตั้งให้จบตอนพีคพอดีขณะขายังอยู่ในกรวยที่เอื้อมถึง")]
        [Min(0.1f)] public float reach = 6f;
        [Min(0f)] public float maxTorsoUpwardSpeed = 0.75f;

        [Header("Kick Drive (สปริงแบบเดียวกับแขน)")]
        [Min(0.1f)] public float moveSpeed = 25f;
        [Tooltip("ตัวหลักที่คุมความ 'แข็งเป็นหุ่นยนต์' — แข็งไปให้ลดเป็น 8 หรือ 6 ก่อน")]
        [Min(0.1f)] public float damper = 10f;
        [Tooltip("ระยะจากสะโพกถึงเป้าที่สปริงวิ่งไล่ ต้องไกลกว่าความยาวขา สปริงจะได้ไม่ผ่อนแรงกลางทาง")]
        [Min(1f)] public float targetReach = 18f;

        [Header("Wind-Up (ง้างขาระหว่างชาร์จ)")]
        public bool enableWindup = true;
        [Tooltip("ระยะถอยหลังสูงสุดตอนชาร์จเต็ม (เมตร)")]
        [Min(0f)] public float windupPullBack = 8f;
        [Min(0.1f)] public float windupMoveSpeed = 9f;
        [Min(0.1f)] public float windupDamper = 8f;
        [Tooltip("เพดานความเร็วตอนง้าง — ช้ากว่าตอนเตะมาก ให้อีกฝ่ายอ่านท่าออกทัน")]
        [Min(0.1f)] public float maxWindupSpeed = 18f;

        [Header("Return To Movement")]
        [Min(0f)] public float recoveryDuration = 0.18f;
        [Min(0f)] public float recoveryDamping = 14f;

        [Header("Damaged Leg Power")]
        [Tooltip("แรงเตะที่เหลือเมื่อขาแทบไม่มีเลือด — ไล่จากค่านี้ถึง 100% ตามเลือด / เพดานเลือดตอนเกิด")]
        [Range(0f, 1f)] public float minimumPowerAtZeroHealth = 0.3f;

        [Header("Debug")]
        [Tooltip("log สรุปทุกครั้งที่เตะจบ (ความเร็วพีค/ดาเมจที่จะได้/โดนเป้าไหม)")]
        public bool debugLog = true;

        [NonSerialized] private KickState state;
        [NonSerialized] private LegController leg;
        [NonSerialized] private LimbStrike strike;
        [NonSerialized] private CollisionDetectionMode originalCollisionMode;
        [NonSerialized] private float chargeStartedAt;
        [NonSerialized] private float actionTimer;
        [NonSerialized] private float activeSpeed;
        [NonSerialized] private Vector3 startPosition;
        [NonSerialized] private Vector3 direction;
        [NonSerialized] private bool hasHit;
        [NonSerialized] private float peakSpeed;
        // สะโพกเก็บเป็นพิกัดลำตัว — บน prefab จุดหมุนขาซ้ายถูก parent ไว้ใต้ต้นขาอีกข้าง อ่านสดจะแกว่ง
        [NonSerialized] private Vector3 hipLocalOffset;
        [NonSerialized] private float releaseFootDrop;
        [NonSerialized] private Vector3 windupDirection;
        [NonSerialized] private Vector3 windupStartOffset;
        [NonSerialized] private float windupFootY;

        public KickState State => state;
        public bool IsMotionActive => state == KickState.Kicking || state == KickState.Recovering;

        /// <summary>ช่วงที่ท่าเตะเป็นเจ้าของเท้า — รวมตอนง้างด้วย (สปริงก้าวเดินจะลากเท้ากลับจนท่าง้างไม่เกิด)</summary>
        public bool IsControllingFoot => IsMotionActive || (enableWindup && state == KickState.Charging);

        private Rigidbody Foot => leg.Body;

        public void Init(LegController owner, LimbStrike limbStrike)
        {
            leg = owner;
            strike = limbStrike;
            // เท้าบน prefab เป็น Discrete — ที่ 70 m/s ทะลุ collider บางๆ ได้ ต้องสลับเป็น CCD ตอนเตะ
            if (owner.Body != null) originalCollisionMode = owner.Body.collisionDetectionMode;
        }

        /// <summary>แรงที่เหลือตามเลือดของขา 0.3..1 — ขาที่บาดเจ็บเตะเบาลง</summary>
        public float HealthPowerMultiplier
        {
            get
            {
                LimbHealth health = leg != null && leg.Limb != null ? leg.Limb.Health : null;
                if (health == null) return 1f;
                float ratio = Mathf.Clamp01(health.Hp / Mathf.Max(1f, health.StartingMaxHp));
                return Mathf.Lerp(minimumPowerAtZeroHealth, 1f, ratio);
            }
        }

        // ================================================================
        //  IStrikeSource
        // ================================================================

        public bool CanDealDamage() => state == KickState.Kicking && !hasHit;
        public float PeakSpeed() => peakSpeed;
        public DamageSource Source() => DamageSource.Kick;

        public void ResolveStrike(bool landed)
        {
            if (landed)
            {
                if (!CanDealDamage()) return;
                hasHit = true;
                BeginRecovery();
            }
            else if (state == KickState.Kicking)
            {
                BeginRecovery();
            }
        }

        // ================================================================
        //  วงจรท่าเตะ (server)
        // ================================================================

        /// <summary>เริ่มชาร์จ — ทิศง้าง = ตรงข้ามทิศเล็ง ล็อกไว้ตั้งแต่เริ่ม (ท่ายืนถูกคอมมิตแล้ว)</summary>
        public void BeginCharge(Vector3 aimDirection)
        {
            if (state != KickState.Idle) return;

            state = KickState.Charging;
            chargeStartedAt = Time.time;
            peakSpeed = 0f;
            hasHit = false;
            CaptureHipAnchor();

            aimDirection.y = 0f;
            if (!aimDirection.IsValid() || aimDirection.sqrMagnitude < 0.001f) aimDirection = FlatPivotForward();
            windupDirection = aimDirection.sqrMagnitude > 0.001f ? aimDirection.normalized : Vector3.forward;

            // จุดตั้งต้นของการง้าง = ตำแหน่งเท้าตอนนี้เทียบสะโพก (แนวราบ) — charge = 0 คือไม่ขยับเลย
            Vector3 startOffset = Foot.position - HipAnchor();
            startOffset.y = 0f;
            windupStartOffset = startOffset;
            windupFootY = Foot.position.y;
        }

        /// <summary>ปล่อย Shift — เตะไปตามทิศที่เล็ง ด้วยความเร็วตามระดับชาร์จ × แรงที่เหลือของขา</summary>
        public void Release(Vector3 requestedDirection)
        {
            if (state != KickState.Charging)
            {
                Reset();
                return;
            }

            float charge = Mathf.Max(minimumCharge, ChargeAt(Time.time));

            requestedDirection.y = 0f;
            if (!requestedDirection.IsValid() || requestedDirection.sqrMagnitude < 0.001f)
                requestedDirection = FlatPivotForward();
            requestedDirection.Normalize();

            activeSpeed = Mathf.Lerp(minSpeed, maxSpeed, charge) * HealthPowerMultiplier;
            direction = requestedDirection;
            startPosition = Foot.position;
            actionTimer = duration;
            peakSpeed = 0f;
            hasHit = false;
            state = KickState.Kicking;

            Foot.isKinematic = false;
            // เก็บสะโพกใหม่ ณ วินาทีปล่อย — ท่าง้างเพิ่งย้ายลำตัว/ขาไปจากตอนเริ่มชาร์จ
            CaptureHipAnchor();
            // ยืนอยู่ก็เตะระดับพื้น / ค้างกลางก้าว (เท้าลอย) ก็เตะสูงขึ้นตามนั้น
            releaseFootDrop = HipAnchor().y - Foot.position.y;
            Foot.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        public void CancelCharge()
        {
            if (state == KickState.Charging) Reset();
        }

        public void Reset()
        {
            state = KickState.Idle;
            actionTimer = 0f;
            activeSpeed = 0f;
            startPosition = Vector3.zero;
            peakSpeed = 0f;
            hasHit = false;

            // คืนโหมดชนที่ปลายช่วง recovery — เท้ายังวิ่งเร็วตลอดช่วงหน่วง คืนเร็วไปก็ยังทะลุได้
            if (leg != null && Foot != null) Foot.collisionDetectionMode = originalCollisionMode;
        }

        public void FixedTick(float dt)
        {
            switch (state)
            {
                case KickState.Charging:
                    DriveWindup();
                    break;

                case KickState.Kicking:
                {
                    actionTimer -= dt;
                    Foot.isKinematic = false;

                    // เป้าไกลเกินความยาวขา สปริงจึงไม่ผ่อนแรงกลางทาง / แกน Y ล็อกที่ระดับเท้าตอนปล่อย
                    Vector3 hip = HipAnchor();
                    Vector3 target = hip + direction * targetReach;
                    target.y = hip.y - releaseFootDrop;

                    // สปริงแบบแขน — ห้ามเขียน linearVelocity ตรงๆ (ท่อนขาถูก "ลาก" แทน "เหวี่ยง" = แข็งเป็นหุ่นยนต์)
                    Vector3 velocityTarget = Vector3.ClampMagnitude((target - Foot.position) * moveSpeed, activeSpeed);
                    Vector3 force = (velocityTarget - Foot.linearVelocity) * damper;

                    // เครื่องยนต์ F = ma — แนวราบล้วน (เคยใส่แรงเงยแล้วหุ่นกระโดดทั้งตัว)
                    if (Vector3.Dot(Foot.linearVelocity, direction) < activeSpeed)
                        force += direction * acceleration;

                    Foot.AddForce(force, ForceMode.Acceleration);
                    BraceTorsoAgainstJump();

                    peakSpeed = Mathf.Max(peakSpeed, Mathf.Max(0f, Vector3.Dot(Foot.linearVelocity, direction)));
                    float forwardDistance = Vector3.Dot(Foot.position - startPosition, direction);
                    if (actionTimer <= 0f || forwardDistance >= reach) BeginRecovery();
                    break;
                }

                case KickState.Recovering:
                    actionTimer -= dt;
                    Foot.isKinematic = false;
                    Foot.AddForce(-Foot.linearVelocity * recoveryDamping, ForceMode.Acceleration);
                    // แรงหน่วงที่กดเท้าลงสะท้อนขึ้นลำตัวผ่านโซ่ข้อต่อ — ต้องกันตอนนี้ด้วย
                    BraceTorsoAgainstJump();
                    if (actionTimer <= 0f) Reset();
                    break;
            }
        }

        private void BeginRecovery()
        {
            // จุดนี้เป็นทางผ่านเดียวของการเตะจบทุกแบบ — เตะวืดก็โชว์ ไม่งั้นวัดแรงเตะจริงไม่ได้
            if (debugLog)
            {
                string damage = strike == null
                    ? "?"
                    : strike.PreviewDamage(peakSpeed) <= 0f
                        ? $"0 (พีคต่ำกว่าเกณฑ์ {strike.MinVelocityThreshold})"
                        : $"{strike.PreviewDamage(peakSpeed):F1}";
                Debug.Log($"🦵 [{leg.name}] เตะจบ | Peak: {peakSpeed:F1} m/s | สั่งไป: {activeSpeed:F1} m/s | " +
                          $"ดาเมจถ้าเข้าเป้า: {damage} | โดนเป้า: {(hasHit ? "✅" : "❌ วืด")}");
            }

            state = KickState.Recovering;
            actionTimer = recoveryDuration;
        }

        /// <summary>
        /// ง้างขาระหว่างชาร์จ — ดึงเท้าถอยหลังตามระดับพลัง แนวราบล้วน (คัดลอกความสูงเท้าตอนเริ่มชาร์จ)
        /// กฎการล้มตัดสินจากแกน Y ของเท้า การถอยแนวราบจึงไม่ทำให้ล้มแม้แต่ tick เดียว
        /// </summary>
        private void DriveWindup()
        {
            if (!enableWindup) return;
            Foot.isKinematic = false;

            float charge = ChargeAt(Time.time);
            Vector3 target = HipAnchor() + windupStartOffset - windupDirection * (windupPullBack * charge);
            target.y = windupFootY;

            Vector3 velocityTarget = Vector3.ClampMagnitude((target - Foot.position) * windupMoveSpeed, maxWindupSpeed);
            Vector3 force = (velocityTarget - Foot.linearVelocity) * windupDamper;

            // แค่ "ค้างเท้าไว้" ไม่ใช่ดีดขึ้น — หักล้างแรงโน้มถ่วง ไม่งั้นเท้าตกระหว่างง้าง
            if (Foot.useGravity) force -= Physics.gravity;
            Foot.AddForce(force, ForceMode.Acceleration);
        }

        private float ChargeAt(float time) =>
            Mathf.Clamp01(Mathf.Max(0f, time - chargeStartedAt) / Mathf.Max(0.05f, chargeTime));

        private void BraceTorsoAgainstJump()
        {
            Rigidbody torso = leg.TorsoBody;
            if (torso == null) return;

            Vector3 velocity = torso.linearVelocity;
            if (velocity.y <= maxTorsoUpwardSpeed) return;

            velocity.y = maxTorsoUpwardSpeed;
            torso.linearVelocity = velocity;
        }

        /// <summary>สะโพกปัจจุบันจาก offset ในพิกัดลำตัว — ลำตัวหมุน/เคลื่อนแล้วท่าเตะตามไปด้วย</summary>
        private Vector3 HipAnchor()
        {
            Rigidbody torso = leg.TorsoBody;
            if (torso != null) return torso.position + torso.rotation * hipLocalOffset;
            return leg.Pivot != null ? leg.Pivot.position : Foot.position;
        }

        private void CaptureHipAnchor()
        {
            Vector3 hipNow = leg.Pivot != null ? leg.Pivot.position : Foot.position;
            Rigidbody torso = leg.TorsoBody;
            hipLocalOffset = torso != null ? Quaternion.Inverse(torso.rotation) * (hipNow - torso.position) : Vector3.zero;
        }

        private Vector3 FlatPivotForward()
        {
            Vector3 forward = leg.Pivot != null ? leg.Pivot.forward : leg.transform.forward;
            forward.y = 0f;
            return forward;
        }
    }
}

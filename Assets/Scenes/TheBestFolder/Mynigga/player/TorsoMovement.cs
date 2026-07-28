using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class TorsoMovement : NetworkBehaviour
{
    public enum TorsoState { Standing, Falling, Ragdoll }

    [Header("Network State")]
    public NetworkVariable<TorsoState> currentState = new NetworkVariable<TorsoState>(TorsoState.Standing);

    [Header("Fake Hover & Posture")]
    public float targetTorsoHeight = 1.6f;
    public float heightSpringForce = 300f;
    public float heightDamper      = 30f;
    public float uprightSpring     = 800f;
    public float uprightDamper     = 60f;
    public float autoCenterGravityForce = 250f;

    [Header("Balance Constraints")]
    public float maxBalanceAngle = 55f;
    public float fallGracePeriod = 1.0f;
    [Tooltip("กันเฟรมเดียวหลอกล้ม: ต้องเห็น 'ทั้งสองเท้าไม่สมดุลพร้อมกัน' ค้างนานกว่านี้ก่อนถึงจะล้มจริง\n" +
             "สำคัญมากในโหมดหลายผู้เล่น (ขาซ้าย-ขวาคุมคนละคน) ที่จังหวะ step อาจซ้อนกันสั้นๆจากดีเลย์เครือข่าย")]
    public float bothFeetUnbalancedGrace = 0.15f;
    private float _bothUnbalancedTimer = 0f;

    [Header("Ragdoll Recovery")]
    [Tooltip("แรงดึงลำตัวให้ตั้งตรงขณะกดลุก (ช่วยให้ไม่ลอยขึ้นแล้วยังล้มอยู่)")]
    public float recoveryUprightTorque = 600f;
    [Tooltip("สัดส่วนความสูงที่ต้องถึงก่อน snap กลับเป็น Standing (0.5 = ครึ่งความสูงปกติ)")]
    public float recoveryHeightThreshold = 0.5f;
    private float _tiltTimer = 0f;

    [Header("Break Force System")]
    public float maxTorsoStress = 500f;
    [HideInInspector] public float currentStress = 0f;
    public float stressDecayRate = 100f;

    [Header("Continuous Recovery")]
    public float continuousRecoveryForce = 800f;
    [Tooltip("ความเร็วขาขึ้นสูงสุดที่ยอมให้แรงลุกดันต่อ (m/s)\n" +
             "ตัวกำลังพุ่งขึ้นเร็วกว่านี้แล้วแรง Q จะหยุดอัด — กันหุ่นพุ่งขึ้นฟ้าตอนหลายคนกดพร้อมกัน")]
    public float maxRecoveryUpVelocity = 12f;
    private float _lastRecoveryTick = -1f;

    [Header("Jump System (Co-op)")]
    [Tooltip("ระยะเวลาหลังกระโดดที่การตัดสินล้มจากสถานะเท้าถูก 'พักไว้' — ลอยเพราะกระโดด ≠ กำลังล้ม\n" +
             "หมดเวลาแล้วยังไม่แตะพื้น ค่อยกลับมานับล้มตามปกติ")]
    public float jumpGraceDuration = 1.2f;
    [Tooltip("หน้าต่างเวลาที่สองขากด Space ห่างกันแล้วยังนับเป็น 'กระโดดพร้อมกัน' (Co-op Jump)")]
    public float coopJumpWindow = 0.3f;
    [Tooltip("แรงดันลำตัวขึ้นตอนกระโดดขาเดียว (hop เตี้ยๆ)")]
    public float soloJumpForce = 250f;
    [Tooltip("แรงโบนัสเพิ่มให้ตอนขาที่สองกดทันใน window — รวมกับ solo กลายเป็นกระโดดเต็มแรง")]
    public float coopJumpBonusForce = 450f;
    [Tooltip("เพดานความเร็วขาขึ้นจากการกระโดด (m/s) — แรงซ้อนกี่ทางก็ไม่ทะลุลิมิต\n" +
             "หลักการเดียวกับ maxRecoveryUpVelocity ที่ใช้แก้ปัญหา 4 คนกด Q พร้อมกัน")]
    public float maxJumpUpVelocity = 10f;

    [Header("Landing Assist")]
    [Tooltip("ระยะเวลาช่วยพยุงหลังลงพื้นจากการกระโดด — กันเด้ง/ยุบจน stress เกินแล้วล้มทั้งที่โดดสำเร็จ")]
    public float landingAssistDuration = 0.4f;
    [Tooltip("แรงหน่วงความเร็วลำตัวช่วงพยุงลงพื้น")]
    public float landingAssistDamping = 4f;

    private float _jumpGraceTimer = 0f;
    private float _landingAssistTimer = 0f;
    private PlayerFootForRobot _pendingJumpFoot = null;
    private float _pendingJumpTime = -999f;
    // ช่วงต้นของ grace ที่ยังไม่เช็ก landing — แรงกระโดดเพิ่งเริ่มออกฤทธิ์ ความเร็วยังไม่ขึ้น
    private const float LANDING_CHECK_DELAY = 0.15f;
    // สัดส่วนของ fallGracePeriod ที่ใช้เป็นเกณฑ์ "เอียงค้างนานเกิน" ก่อนตัดสินล้ม
    private const float TILT_FALL_GRACE_RATIO = 0.5f;

    [Header("References")]
    public Rigidbody torsoRb;
    public Transform groundRaycastOrigin;
    public LayerMask groundLayer;
    public ConfigurableJoint[] hipJoints;

    [HideInInspector] public float armPullIntensity = 0f;
    public float minCenterForceMultiplier = 0.15f;

    private readonly HashSet<PlayerFootForRobot> _attachedFeet = new HashSet<PlayerFootForRobot>();
    private readonly HashSet<PlayerHandMovement> _attachedHands = new HashSet<PlayerHandMovement>();
    private float _balanceLossTimer = 0f;
    // [Gameplay Fix G10] Cache เพื่อ update hipJoint เฉพาะตอน state เปลี่ยน
    private TorsoState _lastHipJointState = (TorsoState)(-1);

    [Header("Test Features")]
    [Tooltip("ติ๊กถูก = เปิดโหมดทดสอบ: ใช้เท้าข้างเดียวยันพื้นก็ดีดกลับมายืนได้ทันที | เอาติ๊กออก = ใช้ระบบคำนวณความสูงรวมแบบเดิม")]
    public bool testSingleFootRecovery = true;

    // ให้เท้าแต่ละข้างมองเห็นกันได้ (ใช้ตั้งค่า ignore การชนระหว่างสองขา)
    public IReadOnlyCollection<PlayerFootForRobot> AttachedFeet => _attachedFeet;

    public void RegisterFoot(PlayerFootForRobot foot) { if (foot != null) _attachedFeet.Add(foot); }
    public void UnregisterFoot(PlayerFootForRobot foot) { if (foot != null) _attachedFeet.Remove(foot); }
    public void RegisterHand(PlayerHandMovement hand) { if (hand != null) _attachedHands.Add(hand); }
    public void UnregisterHand(PlayerHandMovement hand) { if (hand != null) _attachedHands.Remove(hand); }

    public bool HasSupportingHandGrab
    {
        get
        {
            _attachedHands.RemoveWhere(h => h == null);
            foreach (PlayerHandMovement hand in _attachedHands)
                if (hand.HasSupportingGrab) return true;
            return false;
        }
    }

    void FixedUpdate()
    {
        if (!IsServer) return;

        _attachedFeet.RemoveWhere(f => f == null);
        _attachedHands.RemoveWhere(h => h == null);
        currentStress = Mathf.Max(0f, currentStress - stressDecayRate * Time.fixedDeltaTime);

        // 🦘 [Jump System] นับถอยหลัง grace + ตรวจจับ "ลงพื้นแล้ว" เพื่อสลับเข้าช่วงพยุง
        if (_jumpGraceTimer > 0f)
        {
            _jumpGraceTimer -= Time.fixedDeltaTime;

            // ลงพื้นแล้ว (ความเร็วขาขึ้นหมด + มีเท้าแตะพื้น) → จบ grace เริ่ม landing assist
            // ⚠️ เริ่มเช็คหลังพ้นช่วงต้นของ grace เท่านั้น — เฟรมแรกๆ แรงกระโดดยังไม่ทัน
            // ออกฤทธิ์ (ความเร็วยัง ~0 + เท้ายังแตะพื้น) เช็คทันทีจะโดนตัดสินว่า "ลงแล้ว"
            // ตั้งแต่ยังไม่ทันลอย grace โดนตัดทิ้งทั้งที่เพิ่งกดกระโดด
            float graceElapsed = jumpGraceDuration - _jumpGraceTimer;
            if (graceElapsed >= LANDING_CHECK_DELAY && torsoRb != null &&
                torsoRb.linearVelocity.y <= 0.5f && AnyFootGrounded())
            {
                _jumpGraceTimer = 0f;
                _landingAssistTimer = landingAssistDuration;
            }
        }
        else if (_landingAssistTimer > 0f)
        {
            _landingAssistTimer -= Time.fixedDeltaTime;
            // หน่วงความเร็วลำตัวช่วงสั้นๆ หลังลงพื้น — กันเด้งกลับ/ไถลจนเสียสมดุล
            if (torsoRb != null)
                torsoRb.AddForce(-torsoRb.linearVelocity * landingAssistDamping, ForceMode.Acceleration);
        }

        // คำนวณครั้งเดียวต่อ tick — property นี้วนลิสต์มือ + RemoveWhere ทุกครั้งที่ถูกเรียก
        bool supportedByHand = HasSupportingHandGrab;

        if (!supportedByHand && currentStress >= maxTorsoStress && currentState.Value == TorsoState.Standing)
            currentState.Value = TorsoState.Falling;

        // [Gameplay Fix G10] Set hipJoint เฉพาะตอน state เปลี่ยน ไม่ใช่ทุก FixedUpdate
        // ป้องกัน Physics solver disruption และ Stun-lock loop
        TorsoState currentVal = currentState.Value;
        if (currentVal != _lastHipJointState)
        {
            _lastHipJointState = currentVal;
            UnlockHipJoints();
        }

        switch (currentState.Value)
        {
            case TorsoState.Standing:
                HandleFakeHoverAndPosture(supportedByHand);
                break;
            case TorsoState.Falling:
                currentState.Value = TorsoState.Ragdoll;
                break;
            case TorsoState.Ragdoll:
                // ✅ [ตามดีไซน์] ล้มแล้วต้องกด Q เท่านั้นถึงลุกได้ — ไม่มีลุกอัตโนมัติ
                // (Auto Recovery เดิมอัดแรงทุก tick จนหุ่นลอยค้างฟ้าเป็นบอลลูน → ถอดทิ้งแล้ว)
                break;
        }
    }

    private void HandleFakeHoverAndPosture(bool supportedByHand)
    {
        if (torsoRb == null) return;

        // นับสถานะเท้าก่อน — ใช้ทั้งเงื่อนไขเอียงและเงื่อนไขล้มจากเท้าด้านล่าง
        int groundedCount = 0;
        int balancedCount = 0;
        Vector3 avgFootPos = Vector3.zero;

        foreach (var foot in _attachedFeet)
        {
            if (foot == null) continue;
            if (foot.IsBalanced) balancedCount++;
            if (!foot.IsGrounded()) continue;
            // ✅ [Bug Fix] เท้าที่ footRb หายห้ามนับ — เดิมบวก Vector3.zero เข้า average
            // ทำให้จุดศูนย์ถ่วงถูกลากไปหา (0,0,0) ของโลก ตัวหุ่นไหลผิดทิศ
            if (foot.footRb == null) continue;
            groundedCount++;
            avgFootPos += foot.footRb.position;
        }

        // ✅ [Design Rule] "การล้มเกิดจากเผลอก้าวสองขาพร้อมกันเท่านั้น"
        // เช็กเอียงเกินองศาจึงทำงานเฉพาะตอน 'ไม่มีเท้าแตะพื้นเลย' (เช่นคะมำกลางอากาศ)
        // — ตราบใดที่มีเท้าปักพื้นอยู่ ระบบ Tether ในขา (legStretchSpring) เป็นคนรั้ง
        // ลำตัวไม่ให้ถูกลากจนเสียท่า แทนการจับล้มทิ้ง
        float tiltAngle = Vector3.Angle(torsoRb.transform.up, Vector3.up);
        if (!supportedByHand && groundedCount == 0 && tiltAngle > maxBalanceAngle)
        {
            _tiltTimer += Time.fixedDeltaTime;
            if (_tiltTimer >= fallGracePeriod * TILT_FALL_GRACE_RATIO)
            {
                _tiltTimer = 0f;
                currentState.Value = TorsoState.Falling;
                return;
            }
        }
        else
        {
            _tiltTimer = Mathf.Max(0f, _tiltTimer - Time.fixedDeltaTime * 2f);
        }

        // 🦘 [Jump Grace] ช่วงกระโดด/เพิ่งลงพื้น: พักการตัดสินล้มจาก "สถานะเท้า" ไว้ก่อน
        // (เท้าลอยเพราะกระโดด ≠ กำลังล้ม)
        bool jumpProtected = _jumpGraceTimer > 0f || _landingAssistTimer > 0f;

        int footCount = _attachedFeet.Count;
        // ✅ [Multiplayer Debounce] เดิมเช็คนี้ล้มทันทีไม่มี grace เลย (ต่างจากเงื่อนไขอื่นด้านล่าง)
        // ในโหมดหลายผู้เล่น (ขาซ้าย-ขวาคุมคนละคน) จังหวะ isStepping ของสองคนมีโอกาสซ้อนกันแค่เฟรมเดียว
        // จากดีเลย์เครือข่าย ทำให้ตัวล้มทั้งที่จริงๆยังยืนอยู่ได้ (ร่วมกับ fix IsBalanced ในไฟล์ขา)
        // ✅ [Tighter Fix] เพิ่ม groundedCount == 0 เข้าไปด้วย — เดิม balancedCount==0 ทริกเกอร์ได้จาก
        // flag ล้วนๆ (เช่น isJumping ตั้งไปแล้วแต่เท้ายังไม่ทันลอยจริง) ตอนนี้ต้อง "เท้าทั้งคู่ลอยจริง"
        // เท่านั้นถึงจะนับ ตัดโอกาส false-positive จาก flag ที่ไม่ตรงกับสถานะฟิสิกส์จริงทิ้งไปเลย
        if (!supportedByHand && !jumpProtected && footCount >= 2 && balancedCount == 0 && groundedCount == 0)
        {
            _bothUnbalancedTimer += Time.fixedDeltaTime;
            if (_bothUnbalancedTimer >= bothFeetUnbalancedGrace)
            {
                _bothUnbalancedTimer = 0f;
                currentState.Value = TorsoState.Falling;
                return;
            }
        }
        else
        {
            _bothUnbalancedTimer = Mathf.Max(0f, _bothUnbalancedTimer - Time.fixedDeltaTime * 2f);
        }

        if (!supportedByHand && !jumpProtected && footCount > 0 && groundedCount == 0)
        {
            _balanceLossTimer += Time.fixedDeltaTime;
            if (_balanceLossTimer >= fallGracePeriod)
            {
                currentState.Value = TorsoState.Falling;
                return;
            }
        }
        else
        {
            _balanceLossTimer = Mathf.Max(0f, _balanceLossTimer - Time.fixedDeltaTime * 2f);
            if (groundedCount > 0)
            {
                avgFootPos /= groundedCount;
                Vector3 flatError = new Vector3(avgFootPos.x - torsoRb.position.x, 0f, avgFootPos.z - torsoRb.position.z);
                torsoRb.AddForce(flatError * (autoCenterGravityForce * Mathf.Lerp(1f, minCenterForceMultiplier, armPullIntensity)), ForceMode.Acceleration);
            }
        }

        // ✅ [Fly-away Fix] ชดเชยแรงโน้มถ่วง "เฉพาะตอนมีพื้นในระยะ" เท่านั้น
        // เดิมใส่ -gravity ตลอดเวลา → หุ่นที่ถูกดีดพ้นระยะ raycast จะลอยค้างฟ้าไม่ตกลงมา
        // ใหม่: หลุดพ้นพื้นเมื่อไหร่ แรงโน้มถ่วงกลับมาดึงลงตามธรรมชาติทันที
        // 🦘 [Jump Fix] ปิด hover spring ระหว่างช่วงกระโดด (grace) — เดิมสปริงความสูง
        // เห็นตัวลอยสูงกว่า targetTorsoHeight ก็ออกแรงกดกลับลง + damper หน่วงความเร็วขาขึ้น
        // แรงกระโดดเลยโดนตัดทิ้งเกือบหมดตั้งแต่เฟรมแรกๆ → ตอนนี้ปล่อยให้ลอยตามวิถีธรรมชาติ
        // แล้วสปริงค่อยกลับมาทำงานตอนลงพื้น (ช่วง landing assist ช่วยรับอยู่แล้ว)
        if (_jumpGraceTimer <= 0f &&
            groundRaycastOrigin != null && Physics.Raycast(groundRaycastOrigin.position, Vector3.down, out RaycastHit hit, targetTorsoHeight * 2f, groundLayer))
        {
            torsoRb.AddForce(-Physics.gravity, ForceMode.Acceleration);

            float heightError = targetTorsoHeight - (groundRaycastOrigin.position.y - hit.point.y);
            torsoRb.AddForce(Vector3.up * ((heightError * heightSpringForce) - (torsoRb.linearVelocity.y * heightDamper)), ForceMode.Acceleration);
        }

        Quaternion targetRot = Quaternion.Euler(0f, torsoRb.rotation.eulerAngles.y, 0f);
        Quaternion deltaRot  = targetRot * Quaternion.Inverse(torsoRb.rotation);
        deltaRot.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;

        if (Mathf.Abs(angle) > 0.01f)
            torsoRb.AddTorque((axis * (angle * uprightSpring)) - (torsoRb.angularVelocity * uprightDamper), ForceMode.Acceleration);
    }

    public void ApplyContinuousRecoveryForce(Vector3 forcePosition, float strengthMultiplier = 1f)
    {
        if (torsoRb == null) return;
        if (currentState.Value != TorsoState.Ragdoll && currentState.Value != TorsoState.Falling) return;

        // ✅ [4-Player Fix] กันแรงลุกซ้อนกัน — เดิม 2 เท้า + 2 มือ + auto-recovery
        // เรียกฟังก์ชันนี้พร้อมกันได้ 5 ทาง แรงคูณ 5 เท่า → หุ่นพุ่งขึ้นฟ้า
        // จำกัดให้แรงลุกทำงานครั้งเดียวต่อ physics tick ไม่ว่ากี่คนจะกด Q
        if (Mathf.Approximately(_lastRecoveryTick, Time.fixedTime)) return;
        _lastRecoveryTick = Time.fixedTime;

        // ✅ ตัวกำลังพุ่งขึ้นเร็วอยู่แล้ว → หยุดอัดเพิ่ม กันสะสมความเร็วจนทะยาน
        if (torsoRb.linearVelocity.y > maxRecoveryUpVelocity) return;

        // [Audit Fix] ยกเลิกการใช้ ragdollRecoveryDelay บล็อกการกด Q
        // ถ้าผู้เล่นสามารถกด Q จนเท้าดันพื้นได้แล้ว (ระบบ IsGrounded ผ่าน) ควรให้สิทธิ์ลุกขึ้นทันที!

        // [Fix-3] เพิ่มแรงงัดลำตัว (Torso Recovery Boost)
        // ดันสะโพกให้พุ่งขึ้นตรงๆ อย่างรุนแรง เพื่อให้หุ่นงัดตัวเองขึ้นมาตั้งไข่ได้ชัวร์ๆ ทันทีที่กดปุ่ม Q
        float massiveRecoveryBoost = continuousRecoveryForce * strengthMultiplier * 2.5f;
        torsoRb.AddForceAtPosition(Vector3.up * massiveRecoveryBoost, forcePosition, ForceMode.Acceleration);
        
        // แถมแรงเสริมดันกลางลำตัวตรงๆ อีกแรง เพื่อให้ดีดตัวพ้นพื้นได้ง่ายขึ้น
        torsoRb.AddForce(Vector3.up * (continuousRecoveryForce * strengthMultiplier), ForceMode.Acceleration);

        // [Recovery Fix] เพิ่ม Upright Torque ระหว่าง recovery
        // ป้องกันตัวลอยขึ้นแต่ยังเอียงอยู่ → ทำให้ height threshold ไม่ผ่านและล้มลงซ้ำ
        Quaternion targetRot = Quaternion.Euler(0f, torsoRb.rotation.eulerAngles.y, 0f);
        Quaternion deltaRot  = targetRot * Quaternion.Inverse(torsoRb.rotation);
        deltaRot.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        if (Mathf.Abs(angle) > 0.01f)
            torsoRb.AddTorque(axis * (angle * recoveryUprightTorque), ForceMode.Acceleration);

        // ตรวจว่าสูงพอแล้วหรือยัง → snap กลับเป็น Standing
        if (groundRaycastOrigin != null && Physics.Raycast(groundRaycastOrigin.position, Vector3.down, out RaycastHit hit, targetTorsoHeight * 2f, groundLayer))
        {
            float currentHeight = groundRaycastOrigin.position.y - hit.point.y;
            // [Audit Fix] Implement testSingleFootRecovery
            if (testSingleFootRecovery || currentHeight >= targetTorsoHeight * recoveryHeightThreshold)
            {
                _balanceLossTimer    = 0f;
                _tiltTimer           = 0f; // กันลุกปุ๊บโดนตัดสินว่าเอียงค้างแล้วล้มซ้ำ
                _bothUnbalancedTimer = 0f; // reset ให้ครบชุดเดียวกับ timer ล้มตัวอื่น
                currentState.Value = TorsoState.Standing;
            }
        }
    }

    // ================================================================
    //  🦘 Jump System — เรียกจาก ApplyJumpRpc ของเท้า (ฝั่ง server เท่านั้น)
    // ================================================================

    /// <summary>
    /// เท้าข้างหนึ่งเพิ่งกระโดด — torso เป็นคนตัดสินใจว่าเป็น hop ขาเดียว
    /// หรือ Co-op Jump (สองขากดภายใน coopJumpWindow = ได้แรงโบนัสเพิ่ม)
    /// แรงทั้งหมดถูกคุมด้วยเพดาน maxJumpUpVelocity — ไม่มีทางซ้อนสะสมจนพุ่งฟ้า
    /// </summary>
    public void NotifyFootJump(PlayerFootForRobot foot)
    {
        if (torsoRb == null || currentState.Value != TorsoState.Standing) return;

        // เข้าช่วง grace: การตัดสินล้มจากสถานะเท้าถูกพักไว้จนกว่าจะลงพื้น/หมดเวลา
        _jumpGraceTimer = jumpGraceDuration;
        _landingAssistTimer = 0f;

        bool isCoopJump = _pendingJumpFoot != null && _pendingJumpFoot != foot &&
                          (Time.fixedTime - _pendingJumpTime) <= coopJumpWindow;

        if (isCoopJump)
        {
            _pendingJumpFoot = null; // ใช้คู่นี้ไปแล้ว กันขาที่สามมาต่อคอมโบ (เผื่ออนาคต)
            ApplyJumpBoost(coopJumpBonusForce);
            Debug.Log("[Server] 🚀 Co-op Jump! สองขากดพร้อมกัน — ได้แรงโบนัส");
        }
        else
        {
            _pendingJumpFoot = foot;
            _pendingJumpTime = Time.fixedTime;
            ApplyJumpBoost(soloJumpForce);
        }
    }

    private void ApplyJumpBoost(float force)
    {
        // ✅ เพดานจริง ไม่ใช่แค่ประตู — เดิมเช็ก "ยังไม่ถึงเพดาน" แล้วอัดแรงเต็มก้อน
        // Co-op Jump เลยทะลุเพดานได้ (5 m/s จากขาแรก + โบนัสเต็มจากขาสอง = ~14 ทั้งที่เพดาน 10)
        // ใหม่: คิดเป็น Δv แล้ว clamp ตาม headroom ที่เหลือ — ความเร็วสุดท้ายไม่เกินเพดานเสมอ
        float headroom = maxJumpUpVelocity - torsoRb.linearVelocity.y;
        if (headroom <= 0f) return;

        float deltaV = Mathf.Min(force * Time.fixedDeltaTime, headroom);
        torsoRb.AddForce(Vector3.up * deltaV, ForceMode.VelocityChange);
    }

    private bool AnyFootGrounded()
    {
        foreach (var foot in _attachedFeet)
            if (foot != null && foot.IsGrounded()) return true;
        return false;
    }

    /// <summary>
    /// เรียกจาก RespawnManager (ฝั่ง server) หลัง teleport ร่างกลับเช็คพอยต์
    /// ล้าง stress/timer ทั้งหมดแล้วตั้งกลับเป็นท่ายืน — limb ถูกจัดท่ายืนให้แล้วโดย RespawnManager
    /// </summary>
    public void ResetForRespawn()
    {
        if (!IsServer) return;

        currentStress        = 0f;
        _balanceLossTimer    = 0f;
        _tiltTimer           = 0f;
        _bothUnbalancedTimer = 0f;
        _jumpGraceTimer      = 0f;
        _landingAssistTimer  = 0f;
        _pendingJumpFoot     = null;

        if (torsoRb != null && !torsoRb.isKinematic)
        {
            torsoRb.linearVelocity  = Vector3.zero;
            torsoRb.angularVelocity = Vector3.zero;
        }

        currentState.Value = TorsoState.Standing;
    }

    [Rpc(SendTo.Server)]
    public void ApplyRecoveryForceRpc(Vector3 forcePosition)
    {
        // 🛡️ [Server Validation] ห้ามเชื่อพิกัดจาก client — จุดออกแรงยิ่งไกลตัว
        // ยิ่งได้ torque แรง (AddForceAtPosition) ส่งพิกัดไกลๆ/NaN มาปั่นหุ่นได้
        if (torsoRb == null || !forcePosition.IsValid()) return;

        const float MAX_FORCE_POINT_RADIUS = 3f;
        Vector3 offset = forcePosition - torsoRb.position;
        if (offset.sqrMagnitude > MAX_FORCE_POINT_RADIUS * MAX_FORCE_POINT_RADIUS)
            forcePosition = torsoRb.position + offset.normalized * MAX_FORCE_POINT_RADIUS;

        ApplyContinuousRecoveryForce(forcePosition);
    }
    public void AddStress(float amount) => currentStress = Mathf.Min(currentStress + amount, maxTorsoStress * 1.5f);
    public int RegisteredFootCount => _attachedFeet.Count;

    // [Audit Fix] ปลดล็อก hipJoints เป็น Free เสมอ ไม่ใช้ Locked เพื่อไม่ให้แช่แข็งผิดท่า
    // อาศัยพลังจาก Angular Drives (Spring) ของข้อต่อดึงกลับให้ตรงแทนอย่างเป็นธรรมชาติ
    // (เดิมชื่อ UpdateHipJointMotion(state) แต่ไม่เคยใช้ state — ทุกสถานะตั้งค่าเดียวกัน
    // เปลี่ยนชื่อให้ตรงพฤติกรรมจริง จะได้ไม่หลอกคนอ่านว่ามี logic per-state)
    private void UnlockHipJoints()
    {
        if (hipJoints == null || hipJoints.Length == 0) return;

        foreach (var hip in hipJoints)
        {
            if (hip == null) continue;
            hip.angularXMotion = ConfigurableJointMotion.Free;
            hip.angularYMotion = ConfigurableJointMotion.Free;
            hip.angularZMotion = ConfigurableJointMotion.Free;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Transform origin = groundRaycastOrigin != null ? groundRaycastOrigin : transform;
        
        // Target Torso Height (สีส้ม)
        Gizmos.color = new Color(1f, 0.5f, 0f); // Orange
        Vector3 start = origin.position;
        Vector3 end = start + Vector3.down * targetTorsoHeight;
        
        Gizmos.DrawLine(start, end);
        // วาดแผ่นสี่เหลี่ยมบางๆ ที่ปลายเส้น เพื่อให้เห็นระดับความสูงที่เอวจะพยายามลอยอยู่
        Gizmos.DrawWireCube(end, new Vector3(1f, 0.05f, 1f));

        // Threshold การลุกยืน (สีเหลืองอ่อน)
        Gizmos.color = Color.yellow;
        Vector3 thresholdEnd = start + Vector3.down * (targetTorsoHeight * recoveryHeightThreshold);
        Gizmos.DrawWireCube(thresholdEnd, new Vector3(0.5f, 0.05f, 0.5f));
    }
}

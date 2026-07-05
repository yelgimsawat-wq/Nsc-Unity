using UnityEngine;
using Unity.Netcode;

public class PlayerHandMovement : NetworkBehaviour
{
    public enum HandState { Attached, Detached }

    [Header("Network State")]
    public NetworkVariable<HandState> currentState = new NetworkVariable<HandState>(HandState.Attached);

    [Header("References")]
    public TorsoMovement torso;
    public Rigidbody handRb;
    public Transform pivotPoint;
    public Camera playerCamera;
    
    [Header("Offsets (ปรับจุดศูนย์กลางได้อิสระ)")]
    public Vector3 pivotOffset = Vector3.zero;
    public Vector3 grabOffset = Vector3.zero;

    public Vector3 PivotPosition => pivotPoint != null ? pivotPoint.TransformPoint(pivotOffset) : transform.position;
    public Vector3 GrabPosition => handRb != null ? handRb.transform.TransformPoint(grabOffset) : transform.position;

    [Header("Movement & IK Tuning")]
    public float maxArmLength = 1.8f;
    public float handMoveSpeed = 25f;
    public float handDamper = 15f;
    public float planeYOffsetSpeed = 3f;
    public float grabRadius = 0.5f;
    public float grabBreakForce = 10000f; // แรงฉีกขาดเมื่อดึงของหนักเกิน
    public float torsoPullForce = 60f;
    [Tooltip("แรงที่ดึงตัวเมื่อจับ Kinematic Object (ใช้ปีนป่าย)")]
    public float kinematicPullForce = 150f;
    public float detachedMoveSpeed = 20f;
    public LayerMask grabLayer;
    public LayerMask groundLayer;

    [Header("Smoothing (Anti-Jitter)")]
    [Tooltip("เวลาที่มือใช้ไล่ถึงเป้าหมาย (SmoothDamp) — สั้น = ตอบสนองไว/คม แนะนำ 0.03–0.05")]
    public float smoothTime = 0.04f;

    [Header("Stability (Anti-Overshoot)")]
    [Tooltip("ระยะจากเป้าหมายที่เริ่มเบรกความเร็วมือ กันพุ่งเลยเป้าแล้วดีดกลับ")]
    public float brakeDistance = 0.5f;
    [Tooltip("ความแรงเบรกเมื่อเข้าใกล้เป้าหมาย (Acceleration)")]
    public float brakeDamping = 8f;
    [Tooltip("ชดเชยแรงโน้มถ่วงของมือ — สปริงไม่ต้องแบกน้ำหนักมือค้างไว้ ทำให้ยืดถึงเป้าจริง ไม่ตกสั้น")]
    public bool compensateGravity = true;
    [Tooltip("เพดานความเร็วเป้าหมายของสปริง (m/s)\n" +
             "กันสปริงออกแรงมหาศาลตอนเป้าอยู่ไกล ซึ่งทำให้โซ่ข้อต่อแขนสั่น/ระเบิด")]
    public float maxHandVelocity = 40f;
    [Tooltip("ปิดการชนระหว่างมือกับชิ้นส่วนหุ่นตัวเอง\n" +
             "ตัดอาการสั่นจากมือครูดลำตัว/แขน/มืออีกข้างตอนเล็งไปมา")]
    public bool ignoreSelfCollision = true;

    // เพดานความเร็วชั่วคราว — คลาสลูกเซ็ตตอนต่อย (0 = ใช้ maxHandVelocity ปกติ)
    protected float velocityCapOverride = 0f;

    // ระยะยืดพิเศษชั่วคราว — คลาสลูก (PlayerHandCombat) เซ็ตตอนปล่อยหมัด
    // เพื่อขยาย reach limit จริงๆ แทนการหลอกเป้าหมายที่โดน clamp ทิ้ง
    protected float extraReach = 0f;

    [Header("Arm Reach Limits")]
    [Tooltip("สัดส่วนแรงแนวราบ (XZ) ที่ส่งไปยังลำตัวเมื่อแขนยืดสุด\n" +
             "0 = ดันได้แค่แนวตั้ง (ลุกหรือยืนไม่ได้ใช้มือโกง)\n" +
             "1 = ดันได้เต็มทุกทิศ (เหมือนเดิม)\n" +
             "แนะนำ 0.15–0.25 = รู้สึกว่ามือดึงบ้างแต่ไม่โกงได้")]
    [Range(0f, 1f)]
    public float torsoPullHorizontalScale = 0.2f;
    [Tooltip("ความเร็วสูงสุดของลำตัวในแนวราบที่แรงดึงจะเริ่มลดลง (m/s)\n" +
             "ถ้าตัววิ่งเร็วกว่านี้แล้ว แรงดึงแนวราบจะ = 0 กันสะสม momentum")]
    public float maxAllowedHorizontalBoost = 3f;

    [Header("Mouse Range (World Space)")]
    public float mouseReachX = 3f;
    public float mouseReachY = 3f;
    public float mouseReachDepth = 3f;

    protected float currentDepthOffset = 0f; // ระยะเข้า-ออกตามแนวกล้อง (W/S)
    protected bool isGrabbing = false;
    protected Rigidbody grabbedObject;
    protected FixedJoint grabJoint;

    protected Vector3 targetHandPosition;
    protected Vector3 smoothedHandTarget;
    protected bool smoothedHandInitialized = false;
    protected Vector3 _smoothVelocityRef = Vector3.zero; // ความเร็วสำหรับ SmoothDamp

    protected bool localGrabToggle = false;

    protected Vector3 lastSentTarget;
    protected const float RPC_SEND_THRESHOLD = 0.05f;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // ✅ [Rest Pose] กันแขนสะบัดไปหา (0,0,0) ในเฟรมแรก:
        // ใช้ตำแหน่งจริงของมือตอน spawn (ท่าขนาบลำตัวตาม prefab) เป็นเป้าหมายเริ่มต้น
        // แขนจึงนิ่งอยู่ข้างลำตัวตั้งแต่เฟรมแรก จนกว่า Owner จะเริ่มขยับเมาส์จริง
        Vector3 restPose = handRb != null
            ? handRb.position
            : PivotPosition + Vector3.down * (maxArmLength * 0.5f);

        targetHandPosition = restPose;
        smoothedHandTarget = restPose;
        _smoothVelocityRef = Vector3.zero;
        smoothedHandInitialized = true;
        lastSentTarget = restPose;

        if (IsServer && handRb != null)
        {
            // ✅ [Solver Boost] โซ่ข้อต่อแขนแก้สมการยากกว่า rigidbody เดี่ยว
            // เพิ่มรอบ solver เฉพาะมือ → ข้อต่อนิ่งขึ้นตอนรับแรงสูง (ต่อย/เหวี่ยงเร็ว)
            handRb.solverIterations = 12;
            handRb.solverVelocityIterations = 4;

            // ✅ [No Self-Collision] มือไม่ชนชิ้นส่วนหุ่นตัวเอง
            // มือครูดลำตัว/ขา/มืออีกข้าง = แหล่งแรงสั่นและหมัดสะดุดที่ใหญ่ที่สุด
            if (ignoreSelfCollision)
            {
                Collider[] handCols = handRb.GetComponentsInChildren<Collider>();
                Collider[] bodyCols = transform.root.GetComponentsInChildren<Collider>(true);
                foreach (var hc in handCols)
                    foreach (var bc in bodyCols)
                        if (hc != bc) Physics.IgnoreCollision(hc, bc, true);
            }
        }
    }

    protected virtual void Update()
    {
        if (!IsOwner || playerCamera == null) return;
        HandleInput();
    }

    protected virtual void FixedUpdate()
    {
        if (!IsServer) return;
        SmoothHandTarget();

        if (currentState.Value == HandState.Attached)
        {
            PerformArmMovement();
        }
    }

    private void SmoothHandTarget()
    {
        // SmoothDamp: คม/แม่น — smoothTime สั้น ตอบสนองไว มีเบรกในตัวกัน overshoot
        if (!smoothedHandInitialized)
        {
            smoothedHandTarget = targetHandPosition;
            _smoothVelocityRef = Vector3.zero;
            smoothedHandInitialized = true;
        }
        else
        {
            smoothedHandTarget = Vector3.SmoothDamp(
                smoothedHandTarget, targetHandPosition, ref _smoothVelocityRef, smoothTime, Mathf.Infinity, Time.fixedDeltaTime);
        }
    }

    private Vector2 GetNormalizedMousePosition()
    {
        return new Vector2(
            (Mathf.Clamp(Input.mousePosition.x, 0, Screen.width) / Screen.width) * 2f - 1f,
            (Mathf.Clamp(Input.mousePosition.y, 0, Screen.height) / Screen.height) * 2f - 1f
        );
    }

    // ── Screen Basis (ล็อกเฉพาะ Yaw ของกล้อง) ─────────────────────────
    // ใช้ทิศแนวราบของกล้องอย่างเดียว: มุมก้ม/เงย (pitch) ของกล้อง
    // จะไม่มีผลต่อพิกัดมืออีกต่อไป → ตัด Drift/Jitter จากมุมกล้องทิ้งทั้งหมด
    // เมาส์ขึ้น = มือขึ้นแนวดิ่งโลกเป๊ะๆ, เมาส์ขวา = มือไปขวาของจอในแนวราบ
    private void GetScreenBasis(out Vector3 screenRight, out Vector3 screenUp, out Vector3 depthDir)
    {
        Vector3 flatForward = Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up);
        if (flatForward.sqrMagnitude < 0.0001f) // กล้องมองดิ่งพอดี ใช้แกน up ของกล้องแทน
            flatForward = Vector3.ProjectOnPlane(playerCamera.transform.up, Vector3.up);

        depthDir    = flatForward.normalized;
        screenRight = Vector3.Cross(Vector3.up, depthDir); // ตั้งฉากกันเสมอ ไม่สะสม error
        screenUp    = Vector3.up;
    }

    protected virtual void HandleInput()
    {
        if (currentState.Value != HandState.Attached) return;

        // ✅ [Depth] W/S ปรับเฉพาะระยะลึกเข้า-ออก (แยกขาดจากแกน XY โดยสิ้นเชิง)
        if (Input.GetKey(KeyCode.W)) currentDepthOffset += planeYOffsetSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.S)) currentDepthOffset -= planeYOffsetSpeed * Time.deltaTime;
        currentDepthOffset = Mathf.Clamp(currentDepthOffset, -mouseReachDepth, mouseReachDepth);

        // ✅ [Screen-to-World 1:1] เมาส์ normalized [-1,1] × ระยะเอื้อม = พิกัดโลกโดยตรง
        // ไม่มี Virtual Plane, ไม่มี Raycast, ไม่พึ่ง camRight/camUp ที่เอียงตาม pitch
        Vector2 mouseNorm = GetNormalizedMousePosition();
        GetScreenBasis(out Vector3 screenRight, out Vector3 screenUp, out Vector3 depthDir);

        Vector3 newTarget = PivotPosition
            + screenRight * (mouseNorm.x * mouseReachX)  // ซ้าย-ขวา ตามจอ
            + screenUp    * (mouseNorm.y * mouseReachY)  // ขึ้น-ลง แนวดิ่งโลก
            + depthDir    * currentDepthOffset;          // ลึกเข้า-ออก (W/S เท่านั้น)

        // ✅ กันมือทะลุพื้น (คงไว้เหมือนเดิม)
        if (Physics.Raycast(newTarget + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f, groundLayer))
            if (newTarget.y < hit.point.y) newTarget.y = hit.point.y;

        if (Vector3.Distance(lastSentTarget, newTarget) > RPC_SEND_THRESHOLD)
        { lastSentTarget = newTarget; UpdateHandTargetRpc(newTarget); }

        // ✅ กด F ครั้งเดียวเพื่อจับ / กดอีกครั้งเพื่อปล่อย (Toggle)
        if (Input.GetKeyDown(KeyCode.F))
        {
            localGrabToggle = !localGrabToggle;

            if (localGrabToggle) 
                TryGrabRpc();
            else 
                ReleaseGrabRpc();
        }

        if (torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll && Input.GetKeyDown(KeyCode.Q))
            ApplyHandRecoveryRpc();
    }
    
    [Rpc(SendTo.Server)] private void UpdateHandTargetRpc(Vector3 target) { targetHandPosition = target; }
    [Rpc(SendTo.Server)] private void ApplyHandRecoveryRpc() { torso.ApplyContinuousRecoveryForce(PivotPosition); }

    [Rpc(SendTo.Server)]
    private void TryGrabRpc()
    {
        isGrabbing = true;
        Collider[] hits = Physics.OverlapSphere(GrabPosition, grabRadius, grabLayer);
        foreach (var h in hits)
        {
            Rigidbody rb = h.attachedRigidbody;
            if (rb == null) continue;
            grabbedObject = rb;

            // ✅ จับได้ทั้ง Kinematic และ Dynamic
            grabJoint = handRb.gameObject.AddComponent<FixedJoint>();
            grabJoint.connectedBody = rb;
            grabJoint.breakForce = grabBreakForce;
            grabJoint.breakTorque = grabBreakForce;

            // ปิดการชนระหว่างของที่ถูกจับกับลำตัวหุ่น เพื่อป้องกันบั๊กบินขึ้นฟ้า
            IgnoreCollisionWithTorso(grabbedObject, true);

            break;
        }
    }

    [Rpc(SendTo.Server)]
    private void ReleaseGrabRpc()
    {
        isGrabbing = false;
        if (grabJoint != null) Destroy(grabJoint);
        
        if (grabbedObject != null)
        {
            IgnoreCollisionWithTorso(grabbedObject, false);
            grabbedObject = null;
        }
    }

    private void IgnoreCollisionWithTorso(Rigidbody targetRb, bool ignore)
    {
        if (targetRb == null || torso == null) return;
        Collider[] targetCols = targetRb.GetComponentsInChildren<Collider>();
        Collider[] torsoCols = torso.GetComponentsInChildren<Collider>();
        foreach (var tc in torsoCols)
        {
            foreach (var gc in targetCols)
            {
                Physics.IgnoreCollision(tc, gc, ignore);
            }
        }
    }

    void OnJointBreak(float breakForce)
    {
        Debug.Log($"Hand joint broke due to massive force: {breakForce}");
        isGrabbing = false;
        
        if (grabbedObject != null)
        {
            IgnoreCollisionWithTorso(grabbedObject, false);
            grabbedObject = null;
        }
        
        if (IsServer)
        {
            ForceReleaseGrabClientRpc();
        }
    }

    [Rpc(SendTo.Owner)]
    private void ForceReleaseGrabClientRpc()
    {
        localGrabToggle = false;
    }

    protected virtual void PerformArmMovement()
    {
        Vector3 dirFromPivot = smoothedHandTarget - PivotPosition;
        float currentDistance = dirFromPivot.magnitude;
        Vector3 physicsTarget = smoothedHandTarget;

        // ✅ ระยะเอื้อมจริง = ค่าฐาน + ระยะพิเศษชั่วคราว (เช่นตอนปล่อยหมัด)
        float reachLimit = maxArmLength + extraReach;

        if (isGrabbing && grabbedObject != null && grabbedObject.isKinematic)
        {
            // ปีนป่าย
            Vector3 climbPullDir = physicsTarget - GrabPosition;
            torso.torsoRb.AddForce(climbPullDir * kinematicPullForce, ForceMode.Acceleration);

            float stressThisFrame = kinematicPullForce * Time.fixedDeltaTime * Mathf.Clamp01(currentDistance / maxArmLength);
            torso.AddStress(stressThisFrame);
            torso.armPullIntensity = Mathf.Clamp01(currentDistance / maxArmLength);
        }
        else
        {
            torso.armPullIntensity = 0f;

            if (currentDistance < 0.05f)
            {
                physicsTarget = PivotPosition + (dirFromPivot.normalized * 0.05f);
            }
            else if (currentDistance > reachLimit)
            {
                // ✅ [Soft Clamp] เดิม clamp แข็งที่ผิวทรงกลม → เป้าหมายฟิสิกส์กระตุกทันทีที่ชนขอบ
                // ทำให้สปริง "เด้งกลับ" ตอนยืดสุดแขน
                // ใหม่: ระยะส่วนเกินถูกบีบแบบ asymptotic (ยอมเกินได้สูงสุด ~0.4m แบบนุ่มนวล)
                float excess = currentDistance - reachLimit;
                float softExcess = excess / (1f + excess * 2.5f);
                physicsTarget = PivotPosition + (dirFromPivot / currentDistance) * (reachLimit + softExcess);

                Vector3 pullDir = dirFromPivot / currentDistance;

                // ── [Anti Hand-Skating] ────────────────────────────────────────
                // แยกแรงดึงออกเป็น แนวตั้ง (Y) และ แนวราบ (XZ)
                // แรงแนวราบถูก scale ลงตาม torsoPullHorizontalScale
                // และถูกลดเพิ่มเติมถ้าตัวกำลังเคลื่อนที่เร็วอยู่แล้วในทิศเดียวกัน
                Vector3 pullVertical   = new Vector3(0f, pullDir.y, 0f);
                Vector3 pullHorizontal = new Vector3(pullDir.x, 0f, pullDir.z);

                // วัดความเร็วตัวในแนวราบ
                Vector3 bodyHorizVel = torso.torsoRb.linearVelocity;
                bodyHorizVel.y = 0f;

                // ถ้าตัววิ่งในทิศเดียวกับแรงดึงอยู่แล้ว → ลดแรงแนวราบลง
                float velAlongPull = Vector3.Dot(bodyHorizVel, pullHorizontal.normalized);
                float horizScale   = torsoPullHorizontalScale *
                                     Mathf.Clamp01(1f - velAlongPull / Mathf.Max(maxAllowedHorizontalBoost, 0.1f));

                // [Audit Fix] ป้องกันไม่ให้แขนกดตัวเองจมพื้นเวลาล้มหรือกำลังพยายามลุก
                bool isRagdollOrFalling = torso.currentState.Value == TorsoMovement.TorsoState.Ragdoll || torso.currentState.Value == TorsoMovement.TorsoState.Falling;
                if (pullVertical.y < 0f && isRagdollOrFalling)
                {
                    pullVertical.y *= 0.1f; // ลดแรงกดลง 90%
                }

                Vector3 cappedPullDir = pullVertical + pullHorizontal * horizScale;
                torso.torsoRb.AddForceAtPosition(cappedPullDir * torsoPullForce, PivotPosition, ForceMode.Acceleration);
                // ──────────────────────────────────────────────────────────────

                float stressThisFrame = torsoPullForce * Time.fixedDeltaTime * 0.5f;
                torso.AddStress(stressThisFrame);
            }

            // ── Spring + Stability ─────────────────────────────────────────
            Vector3 toTarget = physicsTarget - handRb.position;

            // ✅ [Velocity Cap] จำกัดความเร็วที่สปริงเรียกร้อง — เป้าไกลแค่ไหน
            // สปริงก็ขอความเร็วได้ไม่เกินเพดาน → แรงในโซ่ข้อต่อไม่ระเบิด ไม่สั่น
            float velCap = velocityCapOverride > 0f ? velocityCapOverride : maxHandVelocity;
            Vector3 velocityTarget = Vector3.ClampMagnitude(toTarget * handMoveSpeed, velCap);
            Vector3 force = (velocityTarget - handRb.linearVelocity) * handDamper;

            // ✅ [Gravity Compensation] ตัวการหลักที่ "ยื้อ" แขนไว้:
            // สปริงต้องเหลือระยะ error ค้างไว้เพื่อสร้างแรงต้านน้ำหนักมือ
            // → มือจึงหยุดสั้นกว่าเป้าเสมอ (sag) ชดเชยทิ้งให้สปริงยืดถึงเป้าเต็มระยะ
            if (compensateGravity && handRb.useGravity)
                force -= Physics.gravity;

            // ✅ [Proximity Brake] ใกล้เป้าแล้วเบรกความเร็วเพิ่มแบบ quadratic
            // → มีแรงเหวี่ยงเต็มที่ตอนไกล แต่หยุดนิ่งแม่นยำตอนถึง กัน Overshoot/ดีดกลับ
            float proximity = 1f - Mathf.Clamp01(toTarget.magnitude / Mathf.Max(brakeDistance, 0.01f));
            if (proximity > 0f)
                force -= handRb.linearVelocity * (brakeDamping * proximity * proximity);

            handRb.AddForce(force, ForceMode.Acceleration);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (pivotPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(PivotPosition, maxArmLength);
        }

        if (handRb != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(GrabPosition, grabRadius);
        }
    }
}
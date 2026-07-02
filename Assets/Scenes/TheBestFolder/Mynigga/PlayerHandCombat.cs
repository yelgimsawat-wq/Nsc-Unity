using UnityEngine;
using Unity.Netcode;

public class PlayerHandCombat : PlayerHandMovement
{
    public enum CombatState { Idle, Charging, Punching, Recovering }

    [Header("Combat Settings")]
    public NetworkVariable<CombatState> currentCombatState = new NetworkVariable<CombatState>(CombatState.Idle);
    
    [Tooltip("ความเร็วหมัดตอนปล่อย (คูณจากความเร็วปกติ)")]
    public float punchSpeedMultiplier = 4f;
    [Tooltip("แรงต้านตอนต่อย (ยิ่งน้อยหมัดยิ่งพุ่งทะลวง)")]
    public float punchDamper = 2f; 
    [Tooltip("ระยะเวลาที่หมัดพุ่งก่อนเข้าสู่โหมด Recovery")]
    public float punchDuration = 0.2f;

    [Header("Recovery Blend (Anti-Jitter)")]
    [Tooltip("ระยะเวลา (วินาที) ที่ค่า speed/damper จะค่อย ๆ lerp กลับสู่ค่าปกติหลังต่อยเสร็จ")]
    public float recoveryBlendDuration = 0.25f;

    private float punchTimer = 0f;
    private float recoveryTimer = 0f;

    private float originalHandSpeed;
    private float originalHandDamper;

    // ค่า speed/damper ที่ใช้จริงในเฟรมนี้ (lerp ระหว่างค่าต่อย → ค่าปกติ)
    private float activeHandSpeed;
    private float activeHandDamper;

    [Tooltip("ระยะยืดพิเศษตอนปล่อยหมัด ยิ่งเยอะยิ่งพุ่งไกล")]
    public float punchExtraReach = 5f;

    private void Start()
    {
        // เก็บค่าดั้งเดิมจากคลาสแม่เอาไว้
        originalHandSpeed = handMoveSpeed;
        originalHandDamper = handDamper;

        activeHandSpeed = originalHandSpeed;
        activeHandDamper = originalHandDamper;
    }

    protected override void Update()
    {
        base.Update(); // ให้คลาสแม่ทำงานปกติ

        if (!IsOwner || currentState.Value != HandState.Attached) return;

        HandleCombatInput();
    }

    private void HandleCombatInput()
    {
        // 🖱️ กดคลิกซ้าย = เข้าสู่โหมดชาร์จ
        if (Input.GetMouseButtonDown(0))
        {
            ChangeCombatStateRpc(CombatState.Charging);
        }
        // 🖱️ ปล่อยคลิกซ้าย = ปล่อยหมัด!
        else if (Input.GetMouseButtonUp(0) && currentCombatState.Value == CombatState.Charging)
        {
            ChangeCombatStateRpc(CombatState.Punching);
        }
    }

    [Rpc(SendTo.Server)]
    private void ChangeCombatStateRpc(CombatState newState)
    {
        currentCombatState.Value = newState;
        if (newState == CombatState.Punching)
        {
            punchTimer = punchDuration;
        }
        else if (newState == CombatState.Recovering)
        {
            recoveryTimer = 0f;
        }
    }

    protected override void FixedUpdate()
    {
        base.FixedUpdate(); // ให้คลาสแม่รันฟิสิกส์ปกติ

        if (!IsServer) return;

        if (currentCombatState.Value == CombatState.Punching)
        {
            punchTimer -= Time.fixedDeltaTime;
            if (punchTimer <= 0f)
            {
                // ✅ เข้าสู่ Recovery mode แทนที่จะ snap กลับทันที
                currentCombatState.Value = CombatState.Recovering;
                recoveryTimer = 0f;
            }
        }
        else if (currentCombatState.Value == CombatState.Recovering)
        {
            recoveryTimer += Time.fixedDeltaTime;
            if (recoveryTimer >= recoveryBlendDuration)
            {
                currentCombatState.Value = CombatState.Idle;
            }
        }
    }

    // ⭐ เขียนทับการขยับแขนเพื่อใส่แรงต่อย และ lerp ค่ากลับอย่าง smooth
    protected override void PerformArmMovement()
    {
        switch (currentCombatState.Value)
        {
            case CombatState.Punching:
            {
                // 1. บันทึกเป้าหมายเดิมไว้ก่อน
                Vector3 originalTarget = smoothedHandTarget;

                // 2. คำนวณทิศทางที่มือควรมุ่งไป
                Vector3 punchDirection = (smoothedHandTarget - pivotPoint.position).normalized;
                if (punchDirection == Vector3.zero)
                    punchDirection = playerCamera.transform.forward;

                // 3. หลอกเป้าหมายฟิสิกส์ให้พุ่งทะลวงออกไปไกลขึ้น!
                smoothedHandTarget = originalTarget + (punchDirection * punchExtraReach);

                // 4. เร่งสปีดและลดความหนืด (snap ทันทีตอนต่อย)
                handMoveSpeed = originalHandSpeed * punchSpeedMultiplier;
                handDamper = punchDamper;
                activeHandSpeed = handMoveSpeed;
                activeHandDamper = handDamper;

                // 5. รันฟิสิกส์คลาสแม่ด้วยเป้าหมายใหม่
                base.PerformArmMovement();

                // 6. คืนค่ากลับเพื่อไม่ให้คลาสแม่รวน
                smoothedHandTarget = originalTarget;
                handMoveSpeed = originalHandSpeed;
                handDamper = originalHandDamper;
                break;
            }

            case CombatState.Recovering:
            {
                // ✅ Blend Tree แบบ Physics: ค่อย ๆ lerp speed/damper กลับเป็นค่าปกติ
                float t = recoveryTimer / Mathf.Max(recoveryBlendDuration, 0.001f);
                float smooth = Mathf.SmoothStep(0f, 1f, t); // curve ให้ natural มากขึ้น

                activeHandSpeed  = Mathf.Lerp(originalHandSpeed * punchSpeedMultiplier, originalHandSpeed, smooth);
                activeHandDamper = Mathf.Lerp(punchDamper, originalHandDamper, smooth);

                handMoveSpeed = activeHandSpeed;
                handDamper    = activeHandDamper;

                base.PerformArmMovement();

                // คืนค่าปกติหลังจากรันฟิสิกส์
                handMoveSpeed = originalHandSpeed;
                handDamper    = originalHandDamper;
                break;
            }

            case CombatState.Charging:
            {
                // หนืดๆ ตอนชาร์จ
                handMoveSpeed = originalHandSpeed * 0.3f;
                base.PerformArmMovement();
                handMoveSpeed = originalHandSpeed;
                break;
            }

            default: // Idle
            {
                base.PerformArmMovement();
                break;
            }
        }
    }
}
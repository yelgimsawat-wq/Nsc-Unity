using UnityEngine;
using Unity.Netcode;

// สืบทอดมาจาก PlayerHandMovement
public class PlayerHandCombat : PlayerHandMovement
{
    public enum CombatState { Idle, Charging, Punching }

    [Header("Combat Settings")]
    public NetworkVariable<CombatState> currentCombatState = new NetworkVariable<CombatState>(CombatState.Idle);
    
    [Tooltip("ความเร็วหมัดตอนปล่อย (คูณจากความเร็วปกติ)")]
    public float punchSpeedMultiplier = 4f;
    [Tooltip("แรงต้านตอนต่อย (ยิ่งน้อยหมัดยิ่งพุ่งทะลวง)")]
    public float punchDamper = 2f; 
    [Tooltip("ระยะเวลาที่หมัดพุ่งก่อนกลับสู่โหมดปกติ")]
    public float punchDuration = 0.2f;

    private float punchTimer = 0f;
    private float originalHandSpeed;
    private float originalHandDamper;

    [Tooltip("ระยะยืดพิเศษตอนปล่อยหมัด ยิ่งเยอะยิ่งพุ่งไกล")]
    public float punchExtraReach = 5f;

    private void Start()
    {
        // เก็บค่าดั้งเดิมจากคลาสแม่เอาไว้
        originalHandSpeed = handMoveSpeed;
        originalHandDamper = handDamper;
    }

    protected override void Update()
    {
        base.Update(); // ให้คลาสแม่ทำงานปกติ (ขยับตามเมาส์, หยิบของ ฯลฯ)

        if (!IsOwner || currentState.Value != HandState.Attached) return;

        HandleCombatInput();
    }

    private void HandleCombatInput()
    {
        // 🖱️ กดคลิกขวาค้าง = เข้าสู่โหมดชาร์จ
        if (Input.GetMouseButtonDown(1))
        {
            ChangeCombatStateRpc(CombatState.Charging);
        }
        // 🖱️ ปล่อยคลิกขวา = ปล่อยหมัด!
        else if (Input.GetMouseButtonUp(1) && currentCombatState.Value == CombatState.Charging)
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
            punchTimer = punchDuration; // ตั้งเวลาการพุ่งของหมัด
        }
    }

    protected override void FixedUpdate()
    {
        base.FixedUpdate(); // ให้คลาสแม่รันฟิสิกส์ปกติ

        if (!IsServer) return;

        // นับเวลาถอยหลังตอนต่อย เพื่อกลับสู่สภาวะปกติ
        if (currentCombatState.Value == CombatState.Punching)
        {
            punchTimer -= Time.fixedDeltaTime;
            if (punchTimer <= 0f)
            {
                ChangeCombatStateRpc(CombatState.Idle);
            }
        }
    }

    // ⭐ หัวใจหลัก: เขียนทับการขยับแขนเพื่อใส่แรงต่อย
    protected override void PerformArmMovement()
    {
        if (currentCombatState.Value == CombatState.Punching)
        {
            // 1. บันทึกเป้าหมายเดิมไว้ก่อน
            Vector3 originalTarget = smoothedHandTarget;

            // 2. คำนวณทิศทางที่มือควรมุ่งไป (ชี้ไปทางเมาส์)
            Vector3 punchDirection = (smoothedHandTarget - pivotPoint.position).normalized;
            
            // ถ้าเป้าหมายอยู่ใกล้ตัวมาก (เช่นเมาส์อยู่ตรงกลางจอพอดี) ให้พุ่งไปข้างหน้าตามกล้องแทน
            if (punchDirection == Vector3.zero) 
                punchDirection = playerCamera.transform.forward;

            // 3. หลอกเป้าหมายฟิสิกส์ให้พุ่งทะลวงออกไปไกลขึ้น!
            smoothedHandTarget = originalTarget + (punchDirection * punchExtraReach);

            // 4. เร่งสปีดและลดความหนืด
            handMoveSpeed = originalHandSpeed * punchSpeedMultiplier;
            handDamper = punchDamper; 

            // 5. รันฟิสิกส์คลาสแม่ด้วยเป้าหมายใหม่ที่ไกลกว่าเดิม
            base.PerformArmMovement(); 

            // 6. คืนค่ากลับเมื่อคำนวณเฟรมนี้เสร็จ (เพื่อไม่ให้คลาสแม่รวน)
            smoothedHandTarget = originalTarget;
            handMoveSpeed = originalHandSpeed;
            handDamper = originalHandDamper;
        }
        else if (currentCombatState.Value == CombatState.Charging)
        {
            handMoveSpeed = originalHandSpeed * 0.3f;
            base.PerformArmMovement();
            handMoveSpeed = originalHandSpeed;
        }
        else
        {
            base.PerformArmMovement();
        }
    }
}
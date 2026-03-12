using UnityEngine;
using UnityEngine.Animations.Rigging;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// ระบบสมบูรณ์: ง้างแขน (Charge) + เล็ง IK ตามเมาส์ + ตอกหมัด (Punch)
/// 
/// การทำงาน:
///   กดค้าง → เล่นท่าง้าง (IK ลดลง ให้แอนิเมชั่นดึงแขน) → IK ค่อยๆ เพิ่มขึ้นตามเวลาชาร์จ
///   ปล่อย  → ตอกหมัดตามทิศเมาส์ (IK ปิดชั่วคราวให้ Animator คุมเต็ม)
///   จบท่า  → กลับ Idle + IK เปิดใหม่
/// </summary>
[RequireComponent(typeof(Animator))]
public class ArmPunchIKController : MonoBehaviour
{
    [Header("IK Settings")]
    [Tooltip("ลาก Transform ของ HandTarget มาใส่")]
    public Transform handTarget;
    public float aimHeight = 1.0f;

    [Header("Rig (Animation Rigging)")]
    [Tooltip("ลาก Rig Component ของ ArmRig มาใส่ เพื่อควบคุม IK weight")]
    public Rig armRig;
    [Tooltip("ความเร็วในการเปลี่ยน Rig Weight (ยิ่งสูง ยิ่งสลับเร็ว)")]
    public float rigBlendSpeed = 12f;

    [Header("Animator Control")]
    public string isChargingParam = "IsCharging";
    public string punchPowParam = "PunchPower";   // *** ต้องตรงกับชื่อใน Animator Controller ***
    public string punchTriggerParam = "Punch";

    [Header("Charge Settings")]
    public float maxChargeTime = 1.0f;

    [Header("IK Weight During Charge")]
    [Tooltip("IK weight ตอนเริ่มง้าง (ค่าต่ำ = แอนิเมชั่นดึงแขนได้มาก)")]
    [Range(0f, 1f)]
    public float chargeStartIKWeight = 0.15f;
    [Tooltip("IK weight ตอนง้างเต็ม (ค่าสูง = มือเริ่มตามเมาส์มากขึ้น)")]
    [Range(0f, 1f)]
    public float chargeEndIKWeight = 0.5f;

    // ---- Private ----
    private Animator animator;
    private Camera mainCam;

    private bool isCharging = false;
    private float currentPunchPow = 0f;
    private float targetRigWeight = 1f;

    // ตำแหน่งเป้าหมายของ HandTarget (คำนวณจากเมาส์)
    private Vector3 targetWorldPosition;

    private void Start()
    {
        animator = GetComponent<Animator>();
        mainCam = Camera.main;

        if (mainCam == null)
            Debug.LogError("[ArmPunchIKController] ไม่พบ Camera.main!");
        if (handTarget == null)
            Debug.LogError("[ArmPunchIKController] ยังไม่ได้ลาก HandTarget มาใส่!");
        if (armRig == null)
            Debug.LogWarning("[ArmPunchIKController] ไม่ได้ลาก armRig มาใส่ — จะไม่สามารถควบคุม IK weight ได้");
    }

    private void Update()
    {
        // 1. คำนวณตำแหน่งเมาส์ในโลก 3D
        UpdateMouseWorldPosition();

        // 2. รับ Input
        bool pressDown = false;
        bool isHeld = false;
        bool pressUp = false;

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            pressDown = Mouse.current.leftButton.wasPressedThisFrame;
            isHeld = Mouse.current.leftButton.isPressed;
            pressUp = Mouse.current.leftButton.wasReleasedThisFrame;
        }
#else
        pressDown = Input.GetMouseButtonDown(0);
        isHeld = Input.GetMouseButton(0);
        pressUp = Input.GetMouseButtonUp(0);
#endif

        // 3. เริ่มกด → เข้าโหมดง้าง
        if (pressDown && !isCharging)
        {
            isCharging = true;
            currentPunchPow = 0f;
            animator.SetBool(isChargingParam, true);
            animator.SetFloat(punchPowParam, 0f);

            // *** ลด IK weight ให้แอนิเมชั่นง้างดึงแขนถอยหลังได้ ***
            targetRigWeight = chargeStartIKWeight;
        }

        // 4. ระหว่างง้าง → เพิ่มค่า PunchPow + ค่อยๆ เพิ่ม IK weight
        if (isCharging && isHeld)
        {
            currentPunchPow += Time.deltaTime / maxChargeTime;
            currentPunchPow = Mathf.Clamp01(currentPunchPow);
            animator.SetFloat(punchPowParam, currentPunchPow);

            // IK weight เพิ่มขึ้นตาม charge progress
            // เริ่มต้น chargeStartIKWeight → ค่อยๆ ไปถึง chargeEndIKWeight
            targetRigWeight = Mathf.Lerp(chargeStartIKWeight, chargeEndIKWeight, currentPunchPow);
        }

        // 5. ปล่อยคลิก → ตอกหมัด
        if ((pressUp || (!isHeld && isCharging)) && isCharging)
        {
            isCharging = false;

            // ส่งค่าพลังชกก่อนสั่ง Trigger เพื่อให้ Animator ใช้ค่าล่าสุด
            animator.SetFloat(punchPowParam, currentPunchPow);
            animator.SetTrigger(punchTriggerParam);
            animator.SetBool(isChargingParam, false);

            // *** ลด IK weight ชั่วคราว ให้ Animator คุมท่าชกเต็มที่ ***
            targetRigWeight = 0f;

            currentPunchPow = 0f;
        }

        // 6. อัปเดต Rig Weight (สลับ IK ↔ Animator อย่างนุ่มนวล)
        UpdateRigWeight();
    }

    /// <summary>
    /// เลื่อน HandTarget ไปที่ตำแหน่งเมาส์ใน LateUpdate
    /// เพื่อให้ IK override ตำแหน่งมือจาก Animator ได้เสมอ
    /// </summary>
    private void LateUpdate()
    {
        if (handTarget != null)
        {
            handTarget.position = targetWorldPosition;
        }
    }

    /// <summary>
    /// คำนวณตำแหน่งเมาส์ในโลก 3D
    /// </summary>
    private void UpdateMouseWorldPosition()
    {
        if (mainCam == null || handTarget == null) return;

        Vector2 mousePos = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            mousePos = Mouse.current.position.ReadValue();
        }
#else
        mousePos = Input.mousePosition;
#endif

        Ray ray = mainCam.ScreenPointToRay(mousePos);
        Plane groundPlane = new Plane(Vector3.up, new Vector3(0, aimHeight, 0));

        if (groundPlane.Raycast(ray, out float distance))
        {
            targetWorldPosition = ray.GetPoint(distance);
        }
    }

    /// <summary>
    /// สลับ IK weight อย่างนุ่มนวล
    /// - ง้างอยู่ = IK ลดลง (แอนิเมชั่นง้างดึงแขน) → ค่อยๆ เพิ่ม
    /// - ตอกหมัด = IK ปิดชั่วคราว (Animator คุม)
    /// - กลับ Idle = IK เปิดกลับมา
    /// </summary>
    private void UpdateRigWeight()
    {
        if (armRig == null) return;

        // ถ้าไม่ได้ง้าง และ Animator กลับ Idle แล้ว → คืน IK
        if (!isCharging)
        {
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            bool isInIdle = stateInfo.IsName("Idle");
            bool isTransitioning = animator.IsInTransition(0);

            if (isInIdle && !isTransitioning)
            {
                targetRigWeight = 1f;
            }
        }

        armRig.weight = Mathf.Lerp(armRig.weight, targetRigWeight, Time.deltaTime * rigBlendSpeed);
    }
}

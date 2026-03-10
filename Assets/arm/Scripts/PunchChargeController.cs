using UnityEngine;
using UnityEngine.Animations.Rigging; // สำหรับคุม IK

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// ระบบควบคุมการชาร์จต่อยแบบสมบูรณ์
/// ป้องกันปัญหากล้ามเนื้อตีกันกับ IK และกันบัค Animation ข้ามเฟรม
/// </summary>
[RequireComponent(typeof(Animator))]
public class PunchChargeController : MonoBehaviour
{
    [Header("Charge Settings")]
    public float maxChargeTime = 1.0f;
    public float minPunchPower = 0.2f;
    public float maxPunchPower = 1.0f;

    [Header("IK Settings (Animation Rigging)")]
    [Tooltip("ลาก Component 'Rig' (ArmRig) มาใส่ช่องนี้")]
    public Rig armRig;
    [Tooltip("ความเร็วในการสลับภาพระหว่าง IK กับ Animator")]
    public float rigBlendSpeed = 15f; 

    [Header("Animator Parameters")]
    public string isChargingParam = "IsCharging";
    public string punchTriggerParam = "Punch";
    public string punchPowerParam = "PunchPower";

    private Animator animator;
    private int isChargingHash;
    private int punchTriggerHash;
    private int punchPowerHash;

    private bool isCharging = false;
    private float currentChargeTime = 0f;
    private float targetRigWeight = 1f;

    private void Start()
    {
        animator = GetComponent<Animator>();

        if (animator == null)
            Debug.LogError("[PunchChargeController] ไม่พบ Animator Component!");
        if (armRig == null)
            Debug.LogWarning("[PunchChargeController] ไม่ได้ลาก 'armRig' มาใส่ หรือคุณอาจจะลบมันทิ้งไปแล้ว");

        isChargingHash = Animator.StringToHash(isChargingParam);
        punchTriggerHash = Animator.StringToHash(punchTriggerParam);
        punchPowerHash = Animator.StringToHash(punchPowerParam);
    }

    private void Update()
    {
        HandleInput();
        ProcessCharging();
        UpdateRigWeight();
    }

    /// <summary>
    /// สลับคิว IK ให้เนียนกริบ ไม่กระตุกหรือแย่งกับ Animator
    /// </summary>
    private void UpdateRigWeight()
    {
        if (armRig == null) return;

        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        bool isTransitioning = animator.IsInTransition(0);

        // วิธีนี้ชัวร์ที่สุด: 
        // ถ้ากำลังชาร์จอยู่ หรือ Animator เล่นท่าอื่นที่ไม่ใช่ชื่อ Idle หรือกำลังหมุนเส้นต่อลูกศรอยู่ (Transition) = เราจะยึดสิทธิ์คุมแขนคืนให้ Animator เต็มที่!
        if (isCharging || !stateInfo.IsName("Idle") || isTransitioning)
        {
            targetRigWeight = 0f;
        }
        else
        {
            // ถ้าหลุดเงื่อนไขพวกนั้น แปลว่าลงมายืนนิ่งๆ ท่า Idle สนิทแล้ว ให้คืนสิทธิ์ IK เล็งเมาส์รัวๆ
            targetRigWeight = 1f;
        }

        armRig.weight = Mathf.Lerp(armRig.weight, targetRigWeight, Time.deltaTime * rigBlendSpeed);
    }

    private void HandleInput()
    {
        bool wasPressedThisFrame = false;
        bool isHeldDown = false;
        bool wasReleasedThisFrame = false;

#if ENABLE_INPUT_SYSTEM
        if (UnityEngine.InputSystem.Mouse.current != null)
        {
            wasPressedThisFrame = UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame;
            isHeldDown = UnityEngine.InputSystem.Mouse.current.leftButton.isPressed;
            wasReleasedThisFrame = UnityEngine.InputSystem.Mouse.current.leftButton.wasReleasedThisFrame;
        }
#else
        wasPressedThisFrame = Input.GetMouseButtonDown(0);
        isHeldDown = Input.GetMouseButton(0);
        wasReleasedThisFrame = Input.GetMouseButtonUp(0);
#endif

        if (wasPressedThisFrame && !isCharging)
            StartCharging();

        if (isCharging && !isHeldDown) 
            ReleasePunch();

        if (wasReleasedThisFrame && isCharging)
            ReleasePunch();
    }

    private void StartCharging()
    {
        isCharging = true;
        currentChargeTime = 0f;

        animator.SetBool(isChargingHash, true);
        animator.SetFloat(punchPowerHash, 0f);
    }

    private void ProcessCharging()
    {
        if (!isCharging) return;

        currentChargeTime += Time.deltaTime;
        currentChargeTime = Mathf.Clamp(currentChargeTime, 0f, maxChargeTime);

        float chargePercent = currentChargeTime / maxChargeTime;
        float currentPower = Mathf.Lerp(minPunchPower, maxPunchPower, chargePercent);
        animator.SetFloat(punchPowerHash, currentPower);
    }

    private void ReleasePunch()
    {
        isCharging = false;
        
        float finalChargePercent = currentChargeTime / maxChargeTime;
        float finalPower = Mathf.Lerp(minPunchPower, maxPunchPower, finalChargePercent);

        // เตะคำสั่งชกออกไปก่อน 
        animator.SetTrigger(punchTriggerHash);
        animator.SetFloat(punchPowerHash, finalPower);
        
        // ค่อยดึงสถานะ IsCharging ลงทีหลัง เพื่อป้องกัน Animator ทรยศวิ่งกลับ Idle ทันที
        animator.SetBool(isChargingHash, false);

        currentChargeTime = 0f;
    }
}

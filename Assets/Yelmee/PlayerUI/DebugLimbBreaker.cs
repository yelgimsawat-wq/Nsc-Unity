using Nsc.Robots;
using UnityEngine;

/// <summary>
/// ปุ่มทดสอบ UI: กดเลขบังคับชิ้นส่วนหลุดทันที (ไม่สนเลือด)
///   1 = แขนซ้าย, 2 = แขนขวา, 3 = ขาซ้าย, 4 = ขาขวา
/// ทำงานเฉพาะใน Editor และ Development Build — build ที่ปล่อยจริงไม่มีช่องโกงนี้
/// และ client สั่งหลุดได้เฉพาะชิ้นที่ตัวเองเป็นเจ้าของ
/// </summary>
public class DebugLimbBreaker : MonoBehaviour
{
    [SerializeField] private LocalRobotBinder binder;

    [Tooltip("ปิดได้แม้ใน Editor")]
    [SerializeField] private bool enableDebugKeys = true;

    [Header("Keys")]
    [SerializeField] private KeyCode leftArmKey = KeyCode.Alpha1;
    [SerializeField] private KeyCode rightArmKey = KeyCode.Alpha2;
    [SerializeField] private KeyCode leftLegKey = KeyCode.Alpha3;
    [SerializeField] private KeyCode rightLegKey = KeyCode.Alpha4;

    private void Awake()
    {
        if (binder == null)
            binder = GetComponentInParent<LocalRobotBinder>();
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void Update()
    {
        if (!enableDebugKeys || binder == null || !binder.IsBound)
            return;

        if (Input.GetKeyDown(leftArmKey)) Break(LimbSlot.LeftArm);
        if (Input.GetKeyDown(rightArmKey)) Break(LimbSlot.RightArm);
        if (Input.GetKeyDown(leftLegKey)) Break(LimbSlot.LeftLeg);
        if (Input.GetKeyDown(rightLegKey)) Break(LimbSlot.RightLeg);
    }

    private void Break(LimbSlot slot)
    {
        RobotLimb limb = binder.GetLimb(slot);
        if (limb != null && limb.Attachment != null)
            limb.Attachment.DebugRequestBreak();
    }
#endif
}

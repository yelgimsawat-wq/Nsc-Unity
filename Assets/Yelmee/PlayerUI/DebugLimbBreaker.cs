using UnityEngine;

/// <summary>
/// ปุ่มทดสอบ UI: กดเลขบังคับชิ้นส่วนหลุดทันที (ข้าม flag กันหลุด / เลือดไม่เกี่ยว)
///   1 = แขนซ้าย, 2 = แขนขวา, 3 = ขาซ้าย, 4 = ขาขวา
/// กดได้จากทุกเครื่อง (client ส่งคำขอไป Server ให้อัตโนมัติ)
/// ⚠️ เอาติ๊ก enableDebugKeys ออกก่อนปล่อยเกมจริง
/// </summary>
public class DebugLimbBreaker : MonoBehaviour
{
    [SerializeField] private LocalRobotBinder binder;

    [Tooltip("ปิดตัวนี้ก่อน build ปล่อยเกมจริง")]
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

    private void Update()
    {
        if (!enableDebugKeys || binder == null || !binder.IsBound)
            return;

        if (Input.GetKeyDown(leftArmKey)) Break(binder.LeftArmJoint);
        if (Input.GetKeyDown(rightArmKey)) Break(binder.RightArmJoint);
        if (Input.GetKeyDown(leftLegKey)) Break(binder.LeftLegJoint);
        if (Input.GetKeyDown(rightLegKey)) Break(binder.RightLegJoint);
    }

    private static void Break(JointPullAndReconnect joint)
    {
        if (joint != null)
            joint.DebugRequestBreak();
    }
}

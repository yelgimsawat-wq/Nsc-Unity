using UnityEngine;

/// <summary>
/// ระบบขยับแขน/ขาด้วย "กระดูก" (IK แบบ FABRIK) แทนการใช้ฟิสิกส์ล้วนๆ
///
/// วิธีทำงาน:
/// - ปลายกระดูก (มือ/เท้า) จะพยายามไปให้ถึงเป้า (target) ทุกเฟรม โดยหมุนกระดูกทั้งเส้น
/// - รองรับกระดูกกี่ท่อนก็ได้ (ขา 3 ท่อน, แขน 4 ท่อน)
/// - มี Pole (จุดบอกทิศเข่า/ศอก) กันข้อพับผิดด้าน
/// - ต่อกับระบบเดิมได้ทันที: ลาก Following ที่ปลายแขนมาใส่ช่อง linkedFollowing
///   แล้ว LobbyManager จะกำหนด targetPoint ให้เหมือนเดิม (ไม่ต้องแก้โค้ดเก่า)
///
/// รันบนทุกเครื่อง (ไม่ต้อง sync กระดูก) เพราะตำแหน่งเป้า (ตัวผู้เล่น) ถูก sync อยู่แล้ว
/// </summary>
[DefaultExecutionOrder(100)] // รันหลัง Animator และหลังสคริปต์ที่ขยับเป้า
public class BoneLimbIK : MonoBehaviour
{
    [Header("Bone Chain (เรียงจากโคน -> ปลาย)")]
    [Tooltip("กระดูกเรียงจากไหล่/สะโพก ไปจนถึงปลายมือ/ปลายเท้า (อย่างน้อย 2 ชิ้น)")]
    public Transform[] bones;

    [Header("Target")]
    [Tooltip("เป้าที่ปลายกระดูกจะวิ่งไปหา (ถ้าใส่ linkedFollowing จะใช้ targetPoint ของมันแทน)")]
    public Transform target;
    [Tooltip("ใช้ targetPoint จาก Following เดิม (LobbyManager เป็นคนตั้งค่าให้ตอนเริ่มเกม)")]
    public Following linkedFollowing;
    [Tooltip("จุดบอกทิศทางให้ข้อพับ (เข่าชี้ไปข้างหน้า / ศอกชี้ไปข้างหลัง)")]
    public Transform pole;

    [Header("Solver")]
    [Range(0f, 1f)] public float weight = 1f;
    [Range(1, 30)] public int iterations = 10;
    public float tolerance = 0.01f;
    [Tooltip("ความไวในการตามเป้า (0 = ตามทันที, ยิ่งน้อยยิ่งหน่วงเหมือนหุ่นหนักๆ)")]
    public float followSharpness = 12f;
    [Tooltip("รีเซ็ตกระดูกกลับท่าเริ่มต้นก่อนคำนวณทุกเฟรม (ปิดไว้ถ้ามี Animator เล่นท่าอยู่)")]
    public bool resetPoseEachFrame = true;

    [Header("Tip Rotation (ไม่บังคับ)")]
    [Tooltip("ให้ปลายกระดูก (มือ/เท้า) หมุนตามเป้าด้วย")]
    public bool alignTipRotation = false;
    public Vector3 tipRotationOffset = Vector3.zero;

    [Header("Physics Bridge")]
    [Tooltip("บังคับ Rigidbody ที่อยู่บนกระดูกให้เป็น Kinematic เสมอ (LobbyManager จะปลด Kinematic ทุกชิ้นตอนเริ่มเกม)")]
    public bool keepChainKinematic = true;
    [Tooltip("ไม่ให้ Collider ของแขน/ขาชนกับตัวหุ่นเอง (กันหุ่นผลักตัวเองกระเด็น)")]
    public bool ignoreCollisionWithBody = true;

    [Header("Debug")]
    public bool drawGizmos = true;

    // ---- runtime ----
    private Vector3[] positions;
    private float[] lengths;
    private Quaternion[] bindLocalRotations;
    private Quaternion[] startRotations;
    private Rigidbody[] chainBodies;
    private Vector3 smoothedTarget;
    private bool hasSmoothedTarget;

    public float ChainLength { get; private set; }
    public Transform Tip => IsValid ? bones[bones.Length - 1] : null;
    public bool IsValid => bones != null && bones.Length >= 2 && System.Array.TrueForAll(bones, b => b != null);

    /// <summary>เป้าที่ใช้จริงตอนนี้ (Following.targetPoint มาก่อน)</summary>
    public Transform ActiveTarget
    {
        get
        {
            if (linkedFollowing != null && linkedFollowing.targetPoint != null) return linkedFollowing.targetPoint;
            return target;
        }
    }

    private void Reset()
    {
        // ถ้ามี Animator คุมกระดูกอยู่ อย่ารีเซ็ตท่าทุกเฟรม ไม่งั้นจะทับอนิเมชั่น
        resetPoseEachFrame = GetComponentInParent<Animator>() == null;
    }

    private void Awake()
    {
        Initialize();
    }

    private void Start()
    {
        if (ignoreCollisionWithBody) IgnoreBodyCollisions();
    }

    /// <summary>เรียกซ้ำได้ถ้าเปลี่ยนรายการกระดูกตอนรันเกม</summary>
    public void Initialize()
    {
        if (!IsValid)
        {
            Debug.LogWarning($"[BoneLimbIK] '{name}' ต้องมีกระดูกอย่างน้อย 2 ชิ้น และห้ามมีช่องว่าง", this);
            enabled = false;
            return;
        }

        int n = bones.Length;
        positions = new Vector3[n];
        lengths = new float[n - 1];
        bindLocalRotations = new Quaternion[n];
        startRotations = new Quaternion[n];
        for (int i = 0; i < n; i++) bindLocalRotations[i] = bones[i].localRotation;

        chainBodies = new Rigidbody[n];
        for (int i = 0; i < n; i++) chainBodies[i] = bones[i].GetComponent<Rigidbody>();

        hasSmoothedTarget = false;
    }

    private void IgnoreBodyCollisions()
    {
        if (!IsValid || bones[0].parent == null) return;

        Rigidbody body = bones[0].parent.GetComponentInParent<Rigidbody>();
        if (body == null) return;

        Collider[] bodyColliders = body.GetComponentsInChildren<Collider>(true);
        foreach (Transform bone in bones)
        {
            foreach (Collider limbCol in bone.GetComponents<Collider>())
            {
                foreach (Collider bodyCol in bodyColliders)
                {
                    if (bodyCol != limbCol) Physics.IgnoreCollision(limbCol, bodyCol, true);
                }
            }
        }
    }

    private void FixedUpdate()
    {
        if (positions != null && keepChainKinematic) EnforceKinematic();
    }

    private void LateUpdate()
    {
        if (positions == null) return;

        if (keepChainKinematic) EnforceKinematic();

        Transform t = ActiveTarget;
        if (t == null || weight <= 0f) return;

        // ทำให้เป้านุ่มขึ้น (หุ่นใหญ่ไม่ควรสะบัดเร็วเกิน)
        if (!hasSmoothedTarget || followSharpness <= 0f)
        {
            smoothedTarget = t.position;
            hasSmoothedTarget = true;
        }
        else
        {
            float k = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            smoothedTarget = Vector3.Lerp(smoothedTarget, t.position, k);
        }

        Solve(smoothedTarget, t);
    }

    private void EnforceKinematic()
    {
        for (int i = 0; i < chainBodies.Length; i++)
        {
            Rigidbody rb = chainBodies[i];
            if (rb != null && !rb.isKinematic)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }
    }

    private void Solve(Vector3 goal, Transform goalTransform)
    {
        int n = bones.Length;

        if (resetPoseEachFrame)
        {
            for (int i = 0; i < n; i++) bones[i].localRotation = bindLocalRotations[i];
        }

        for (int i = 0; i < n; i++)
        {
            positions[i] = bones[i].position;
            startRotations[i] = bones[i].rotation;
        }

        ChainLength = 0f;
        for (int i = 0; i < n - 1; i++)
        {
            lengths[i] = Vector3.Distance(positions[i], positions[i + 1]);
            ChainLength += lengths[i];
        }
        if (ChainLength < 1e-5f) return;

        Vector3 root = positions[0];
        Vector3 toGoal = goal - root;

        if (toGoal.sqrMagnitude >= ChainLength * ChainLength)
        {
            // เอื้อมไม่ถึง: ยืดตรงไปทางเป้า
            Vector3 dir = toGoal.normalized;
            for (int i = 1; i < n; i++) positions[i] = positions[i - 1] + dir * lengths[i - 1];
        }
        else
        {
            for (int iter = 0; iter < iterations; iter++)
            {
                // Backward: เริ่มจากปลายที่เป้า ไล่กลับมาหาโคน
                positions[n - 1] = goal;
                for (int i = n - 2; i >= 0; i--)
                    positions[i] = positions[i + 1] + SafeDir(positions[i] - positions[i + 1], startRotations[i] * Vector3.up) * lengths[i];

                // Forward: ยึดโคนกลับที่เดิม ไล่ไปหาปลาย
                positions[0] = root;
                for (int i = 1; i < n; i++)
                    positions[i] = positions[i - 1] + SafeDir(positions[i] - positions[i - 1], startRotations[i - 1] * Vector3.up) * lengths[i - 1];

                if ((positions[n - 1] - goal).sqrMagnitude < tolerance * tolerance) break;
            }

            if (pole != null) ApplyPole();
        }

        // แปลงตำแหน่งข้อต่อกลับเป็นการหมุนกระดูก (ไล่จากโคน เพราะลูกจะขยับตามพ่อ)
        for (int i = 0; i < n - 1; i++)
        {
            Vector3 currentDir = bones[i + 1].position - bones[i].position;
            Vector3 wantedDir = positions[i + 1] - positions[i];
            if (currentDir.sqrMagnitude < 1e-10f || wantedDir.sqrMagnitude < 1e-10f) continue;

            Quaternion solved = Quaternion.FromToRotation(currentDir, wantedDir) * bones[i].rotation;
            bones[i].rotation = weight >= 1f ? solved : Quaternion.Slerp(startRotations[i], solved, weight);
        }

        if (alignTipRotation && goalTransform != null)
        {
            Transform tip = bones[n - 1];
            Quaternion wanted = goalTransform.rotation * Quaternion.Euler(tipRotationOffset);
            tip.rotation = Quaternion.Slerp(tip.rotation, wanted, weight);
        }
    }

    // หมุนข้อต่อกลาง (เข่า/ศอก) ให้หันไปทาง pole
    private void ApplyPole()
    {
        Vector3 polePos = pole.position;
        for (int i = 1; i < positions.Length - 1; i++)
        {
            Vector3 a = positions[i - 1];
            Vector3 c = positions[i + 1];
            Vector3 axis = c - a;
            if (axis.sqrMagnitude < 1e-10f) continue;
            axis.Normalize();

            Vector3 projPole = Vector3.ProjectOnPlane(polePos - a, axis);
            Vector3 projBone = Vector3.ProjectOnPlane(positions[i] - a, axis);
            if (projPole.sqrMagnitude < 1e-10f || projBone.sqrMagnitude < 1e-10f) continue;

            float angle = Vector3.SignedAngle(projBone, projPole, axis);
            positions[i] = Quaternion.AngleAxis(angle, axis) * (positions[i] - a) + a;
        }
    }

    private static Vector3 SafeDir(Vector3 v, Vector3 fallback)
    {
        float m = v.magnitude;
        return m > 1e-6f ? v / m : fallback;
    }

    /// <summary>ไล่หากระดูกจาก bones[0] ลงไปตามลูกคนแรกจนสุด (หยุดที่ชิ้น "_end" ชิ้นแรก)</summary>
    [ContextMenu("Auto Fill Chain From First Bone")]
    public void AutoFillFromRoot()
    {
        Transform root = (bones != null && bones.Length > 0 && bones[0] != null) ? bones[0] : transform;
        bones = BuildChain(root);
    }

    public static Transform[] BuildChain(Transform root)
    {
        var list = new System.Collections.Generic.List<Transform> { root };
        Transform current = root;
        while (current.childCount > 0 && !current.name.EndsWith("_end"))
        {
            Transform next = null;
            // เลือกลูกที่เป็นกระดูกต่อ (ข้าม mesh/collider ลูกที่ไม่มีลูกต่อ ถ้ามีตัวเลือกอื่น)
            for (int i = 0; i < current.childCount; i++)
            {
                Transform c = current.GetChild(i);
                if (c.GetComponent<Renderer>() != null) continue;
                next = c;
                break;
            }
            if (next == null) break;
            list.Add(next);
            current = next;
        }
        return list.ToArray();
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos || !IsValid) return;

        Gizmos.color = Color.cyan;
        for (int i = 0; i < bones.Length - 1; i++)
        {
            Gizmos.DrawLine(bones[i].position, bones[i + 1].position);
            Gizmos.DrawWireSphere(bones[i].position, 0.03f * HandleScale());
        }

        Transform t = ActiveTarget;
        if (t != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(t.position, 0.08f * HandleScale());
            Gizmos.DrawLine(bones[bones.Length - 1].position, t.position);
        }
        if (pole != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireCube(pole.position, Vector3.one * 0.08f * HandleScale());
            Gizmos.DrawLine(bones[bones.Length / 2].position, pole.position);
        }
    }

    private float HandleScale()
    {
        float len = 0f;
        for (int i = 0; i < bones.Length - 1; i++) len += Vector3.Distance(bones[i].position, bones[i + 1].position);
        return Mathf.Max(len, 0.5f);
    }
}

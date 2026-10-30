using UnityEngine;

/// <summary>
/// หุ่นโชว์หน้าเมนู (RobotNSC_Rigged — โมเดลเดียวกับหุ่น IK ในเกม)
/// - ตอนเริ่ม: วัดขนาดหุ่นโชว์ตัวเก่า (ชิ้น FBX แยก ปิดไว้ในฉาก) แล้ววางหุ่นใหม่ให้ยืนที่เดิม สูงเท่าเดิม หันหากล้อง
///   — ชิ้นเก่า pivot เพี้ยนกันคนละที่ คำนวณตำแหน่งเองในฉากไม่ได้ จึงวัดจากกรอบ mesh จริงตอนเล่น
/// - โมเดลไม่มีท่ายืน (bind pose = T-pose) → หมุนต้นแขนลงข้างตัว ศอกงอนิด
/// - หายใจ: ตัวยุบขึ้นลงเบาๆ
/// </summary>
public class MenuRobotDisplay : MonoBehaviour
{
    [Tooltip("โมเดลหุ่นใหม่ (ลูกของ GameObject นี้)")]
    [SerializeField] private Transform model;
    [Tooltip("หุ่นโชว์ตัวเก่า (ปิดอยู่) — ใช้วัดตำแหน่ง/ความสูงที่จะวางแทน | ว่าง = ใช้ตำแหน่ง/สเกลที่วางไว้ในฉากตามเดิม")]
    [SerializeField] private Transform fitTo;
    [Tooltip("สูงกว่าหุ่นเก่ากี่เท่า (1 = เท่ากัน)")]
    [SerializeField] private float heightScale = 1f;

    [Header("ท่ายืน")]
    [Tooltip("ต้นแขนกางออกจากลำตัวกี่ส่วน (0 = แนบตัว)")]
    [SerializeField] private float armSpread = 0.3f;
    [Tooltip("ศอกงอไปหน้ากี่ส่วน")]
    [SerializeField] private float elbowBend = 0.35f;

    [Header("ตาเรืองแสง")]
    [Tooltip("วัสดุตาเดิมของโมเดล (eye.mat) — ช่องไหนใช้วัสดุนี้ สลับเป็น Eye Glow")]
    [SerializeField] private Material eyeSource;
    [Tooltip("วัสดุ Unlit สีเกิน 1 (HDR) — Bloom ของเมนูทำให้เรือง (Toon shader ของหุ่นไม่มีช่อง Emission)")]
    [SerializeField] private Material eyeGlow;
    [ColorUsage(false, true)] [SerializeField] private Color glowColor = new Color(6f, 3.6f, 0.6f);
    [Tooltip("ความสว่างต่ำสุดตอนกะพริบ (สัดส่วนของ Glow Color)")]
    [Range(0f, 1f)] [SerializeField] private float glowMin = 0.55f;
    [SerializeField] private float glowPulseSpeed = 2.2f;

    [Header("หายใจ")]
    [SerializeField] private float breatheHeight = 0.006f; // สัดส่วนความสูงหุ่น
    [SerializeField] private float breatheSpeed = 1.6f;

    private Vector3 basePos;
    private float height = 1f;
    private Material glow;
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    private void Start()
    {
        if (!model) return;
        Camera cam = Camera.main;
        Bounds old = default;
        bool fit = fitTo && TryMeshBounds(fitTo, out old);
        Vector3 spot = fit ? old.center : transform.position;
        if (cam) // หันหากล้อง — หน้าหุ่น = -Z ของโมเดล (หุ่น IK ก็หมุนโมเดล 180°)
        {
            Vector3 toCam = Vector3.ProjectOnPlane(cam.transform.position - spot, Vector3.up);
            if (toCam.sqrMagnitude > 1e-4f) model.rotation = Quaternion.LookRotation(-toCam.normalized);
        }
        PoseArms();

        if (fit && TryRendererBounds(model, out Bounds now) && now.size.y > 1e-4f)
        {
            // สเกลให้สูงเท่าหุ่นเก่า แล้ววางเท้ากลางกรอบเก่า
            transform.localScale *= old.size.y * heightScale / now.size.y;
            TryRendererBounds(model, out now);
            Vector3 foot = new Vector3(now.center.x, now.min.y, now.center.z);
            transform.position += new Vector3(old.center.x, old.min.y, old.center.z) - foot;
        }
        if (TryRendererBounds(model, out Bounds b)) height = b.size.y;
        basePos = transform.position;
        SwapEyes();
    }

    private void Update()
    {
        if (!model) return;
        transform.position = basePos + Vector3.up * (Mathf.Sin(Time.time * breatheSpeed) * breatheHeight * height);
        if (glow) // ตาเต้นช้าๆ เหมือนระบบกำลังทำงาน
            glow.SetColor(BaseColor, glowColor * Mathf.Lerp(glowMin, 1f, 0.5f + 0.5f * Mathf.Sin(Time.time * glowPulseSpeed)));
    }

    private void OnDestroy()
    {
        if (glow) Destroy(glow);
    }

    // ช่องวัสดุตา → วัสดุเรือง (สำเนาเดียวใช้ร่วมทุกช่อง เปลี่ยนสีทีเดียวเต้นพร้อมกัน)
    private void SwapEyes()
    {
        if (!eyeGlow) return;
        glow = new Material(eyeGlow);
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] && (mats[i] == eyeSource || (!eyeSource && mats[i].name.StartsWith("eye")))) { mats[i] = glow; changed = true; }
            if (changed) r.sharedMaterials = mats;
        }
    }

    // T-pose → แขนลงข้างตัว: หมุนต้นแขนให้แนวไหล่→ศอกชี้ลง (กางนิด) แล้วศอก→มือชี้ลงเฉียงไปหน้า — คิดในโลก ไม่ต้องรู้แกนกระดูก
    private void PoseArms()
    {
        Transform hips = Bone("Hips");
        foreach (string side in new[] { "Left", "Right" })
        {
            Transform arm = Bone(side + "Arm"), fore = Bone(side + "ForeArm"), hand = Bone(side + "Hand");
            if (!arm || !fore) continue;
            Vector3 outward = Vector3.ProjectOnPlane(arm.position - (hips ? hips.position : model.position), Vector3.up).normalized;
            Vector3 forward = -model.forward; // หน้าหุ่น
            arm.rotation = Quaternion.FromToRotation(fore.position - arm.position, (Vector3.down + outward * armSpread).normalized) * arm.rotation;
            if (hand) fore.rotation = Quaternion.FromToRotation(hand.position - fore.position, (Vector3.down + forward * elbowBend + outward * armSpread * 0.5f).normalized) * fore.rotation;
        }
    }

    private Transform Bone(string name)
    {
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            if (t.name == name || t.name.EndsWith(":" + name)) return t;
        return null;
    }

    private static bool TryRendererBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            if (r is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true; // กรอบตามท่าที่หมุนแขนแล้ว
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    // หุ่นเก่าปิดอยู่ (Renderer.bounds ใช้ไม่ได้) → กรอบ mesh × ตำแหน่งจริงของแต่ละชิ้น
    private static bool TryMeshBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (MeshFilter f in root.GetComponentsInChildren<MeshFilter>(true))
            if (f.sharedMesh) Add(f.transform.localToWorldMatrix, f.sharedMesh.bounds, ref bounds, ref any);
        foreach (SkinnedMeshRenderer s in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (s.sharedMesh) Add(s.transform.localToWorldMatrix, s.sharedMesh.bounds, ref bounds, ref any);
        return any;
    }

    private static void Add(Matrix4x4 m, Bounds local, ref Bounds bounds, ref bool any)
    {
        for (int i = 0; i < 8; i++)
        {
            Vector3 c = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            Vector3 w = m.MultiplyPoint3x4(c);
            if (!any) { bounds = new Bounds(w, Vector3.zero); any = true; }
            else bounds.Encapsulate(w);
        }
    }
}

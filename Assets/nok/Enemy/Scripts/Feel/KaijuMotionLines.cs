using UnityEngine;

namespace NscGame.Enemy
{
    /// <summary>
    /// เส้นความเร็วแบบการ์ตูนตามมือ/เท้าแพนด้าตอนออกท่าโจมตี (Q-20261028-kaiju-enemy-feel)
    /// มือ/เท้าที่วิ่งเร็วเกินเกณฑ์ → เส้นขาว 3 เส้นลากตามทางที่เพิ่งผ่าน (หางเรียว หัวหนา) แล้วหดหายตอนช้าลง
    /// ใช้กับทุกท่าใน Attack Layer อัตโนมัติ (ต่อย ต่อยรัว กระโดดทุบ ทุบพื้น) ยกเว้นท่ากลิ้งที่มีเส้นหมุนของตัวเอง
    /// เป็นภาพล้วน ทุกเครื่องทำเองจากท่าที่เล่นอยู่
    /// </summary>
    [DefaultExecutionOrder(200)] // หลัง Animator + KaijuPoseFix ตำแหน่งกระดูกนิ่งแล้ว
    [RequireComponent(typeof(Animator))]
    public class KaijuMotionLines : MonoBehaviour
    {
        [SerializeField] private Material lineMaterial;
        [SerializeField] private Color lineColor = Color.white;
        [SerializeField] private int attackLayer = 2;
        [Tooltip("ท่าที่ไม่ต้องมีเส้น (มีเอฟเฟกต์ของตัวเองแล้ว)")]
        [SerializeField] private string[] skipStates = { "Attack_Roll" };
        [Tooltip("ความเร็วขั้นต่ำที่เริ่มมีเส้น = ระยะสะโพก→หัว × ค่านี้ ต่อวินาที (ตัวใหญ่เกณฑ์ใหญ่ตาม)")]
        [SerializeField] private float minSpeed = 5f;
        [Tooltip("เส้นยาวย้อนหลังกี่จุด (เก็บจุดทุก 1/30 วิ ไม่ขึ้นกับ FPS)")]
        [SerializeField] private int trailFrames = 7;
        [Tooltip("ความหนา = ระยะสะโพก→หัว × ค่านี้")]
        [SerializeField] private float width = 0.08f;
        [Tooltip("ระยะห่างระหว่างเส้นคู่ข้าง = ระยะสะโพก→หัว × ค่านี้")]
        [SerializeField] private float spacing = 0.18f;

        private class Trail
        {
            public Transform bone;
            public Vector3[] history;
            public Vector3 last;
            public float sinceSample;
            public int count;
            public float strength;
            public LineRenderer[] lines;
        }

        private const float SampleInterval = 1f / 30f;

        private Animator animator;
        private Transform hips, head;
        private Trail[] trails;
        private int[] skipHashes;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            skipHashes = System.Array.ConvertAll(skipStates, Animator.StringToHash);
            Material mat = lineMaterial != null ? lineMaterial : new Material(Shader.Find("Sprites/Default"));
            HumanBodyBones[] bones = { HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };
            trails = new Trail[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                var t = new Trail { bone = animator.GetBoneTransform(bones[i]), history = new Vector3[Mathf.Max(3, trailFrames)], lines = new LineRenderer[3] };
                for (int j = 0; j < t.lines.Length; j++) t.lines[j] = MakeLine(mat, j == 1);
                trails[i] = t;
            }
        }

        private LineRenderer MakeLine(Material mat, bool center)
        {
            var go = new GameObject("MotionLine");
            go.transform.SetParent(transform, false); // ไปด้วยกับตัว ถูกลบพร้อมตัว
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = mat;
            lr.useWorldSpace = true;
            lr.numCapVertices = 3;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.startColor = lr.endColor = lineColor;
            // หาง (จุดแรก = ตำแหน่งเก่าสุด) เรียวเป็นศูนย์ หัว (ใกล้มือ) หนา — เส้นข้างสั้นกว่าเส้นกลาง
            lr.widthCurve = center
                ? new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0.7f))
                : new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.4f, 0f), new Keyframe(0.85f, 0.7f), new Keyframe(1f, 0.4f));
            lr.enabled = false;
            return lr;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || hips == null) return;
            float size = head ? Vector3.Distance(hips.position, head.position) : 3f;
            bool attacking = animator.enabled && Attacking();
            Camera cam = Camera.main;

            foreach (Trail t in trails)
            {
                if (t.bone == null) continue;
                Vector3 now = t.bone.position;
                float speed = t.count > 0 ? Vector3.Distance(now, t.last) / dt : 0f;
                t.last = now;
                // เลื่อนประวัติตามเวลา (ช่อง 0 = ใหม่สุด) หัวเส้นติดมือทุกเฟรม
                t.sinceSample += dt;
                if (t.count == 0 || t.sinceSample >= SampleInterval)
                {
                    for (int k = t.history.Length - 1; k > 0; k--) t.history[k] = t.history[k - 1];
                    t.count = Mathf.Min(t.count + 1, t.history.Length);
                    t.sinceSample = 0f;
                }
                t.history[0] = now;

                float target = attacking ? Mathf.Clamp01((speed - size * minSpeed) / (size * minSpeed)) : 0f;
                // ขึ้นไว ลงไว: เส้นโผล่ทันทีตอนเหวี่ยง แล้วหดหายเมื่อช้าลง
                t.strength = Mathf.MoveTowards(t.strength, target, dt * (target > t.strength ? 20f : 8f));
                Draw(t, size, cam);
            }
        }

        private void Draw(Trail t, float size, Camera cam)
        {
            bool show = t.strength > 0.02f && t.count >= 3;
            foreach (LineRenderer lr in t.lines) lr.enabled = show;
            if (!show) return;

            Vector3 motion = t.history[0] - t.history[t.count - 1];
            Vector3 view = cam ? cam.transform.forward : Vector3.forward;
            Vector3 side = Vector3.Cross(motion, view);
            side = side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.up;

            for (int j = 0; j < t.lines.Length; j++)
            {
                LineRenderer lr = t.lines[j];
                lr.positionCount = t.count;
                Vector3 offset = side * ((j - 1) * spacing * size);
                for (int k = 0; k < t.count; k++)
                    lr.SetPosition(k, t.history[t.count - 1 - k] + offset * (k / (t.count - 1f))); // เส้นข้างบานออกทางหัว
                lr.widthMultiplier = size * width * t.strength * (j == 1 ? 1f : 0.7f);
            }
        }

        private bool Attacking()
        {
            if (animator.layerCount <= attackLayer) return false;
            AnimatorStateInfo s = animator.IsInTransition(attackLayer) ? animator.GetNextAnimatorStateInfo(attackLayer) : animator.GetCurrentAnimatorStateInfo(attackLayer);
            if (s.IsName("Empty")) return false;
            foreach (int h in skipHashes) if (s.shortNameHash == h) return false;
            return true;
        }
    }
}

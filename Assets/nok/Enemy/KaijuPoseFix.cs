using UnityEngine;

namespace NscGame.Enemy
{
    /// <summary>
    /// ปรับท่าหลังอนิเมชันเล่นจบในแต่ละเฟรม (เป็นแค่ภาพ ทำทุกเครื่อง, หลัง EnemyBodySway ที่ส่ายตัวใน LateUpdate)
    /// - เงยอก/คอ/หัว: ท่า Mutant ของ Mixamo หลังค่อมคอยื่น พอใส่แพนด้าหัวโตแล้วหน้ามุดลง | องศาบวก = เงยขึ้น
    /// - กันจมพื้น: ท่าย่อ/ต่อย/ลงพื้นของ Mixamo ทำเท้ามุดพื้นสำหรับสัดส่วนแพนด้า → ยกโมเดลขึ้นพอให้นิ้วเท้าอยู่เหนือพื้น
    ///   ยกอย่างเดียว ไม่ดึงลง (ตอนกระโดดยังลอยได้)
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(Animator))]
    public class KaijuPoseFix : MonoBehaviour
    {
        [SerializeField] private float chestLift = 5f;
        [SerializeField] private float neckLift = 15f;
        [SerializeField] private float headLift = 20f;

        [Header("กันจมพื้น")]
        [SerializeField] private bool keepFeetAboveGround = true;
        [Tooltip("โมเดลที่ยกขึ้นลง (ลูกที่มีกระดูก) — ว่าง = ลูกตัวแรก")]
        [SerializeField] private Transform model;
        [SerializeField] private float liftSmoothing = 25f;
        [Tooltip("layer ท่าขาอย่างเดียว (mask แค่ขา): ระหว่างเล่นท่าใน layer นี้ ดึงตัวลงให้เท้าแตะพื้นด้วย — ท่างอเข่าแต่สะโพกยังสูงเท่าท่ายืน ตัวจะลอย")]
        [SerializeField] private string legsOnlyLayer = "Hit Layer";

        private Animator animator;
        private Transform chest, neck, head;
        private Transform[] feet;
        private Collider[] ownColliders;
        private Vector3 modelRest;
        private float toeAboveSole, lift;
        private int legsLayer;
        private float flatFeet;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest);
            neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            feet = new[]
            {
                animator.GetBoneTransform(HumanBodyBones.LeftFoot), animator.GetBoneTransform(HumanBodyBones.RightFoot),
                animator.GetBoneTransform(HumanBodyBones.LeftToes), animator.GetBoneTransform(HumanBodyBones.RightToes),
            };
            if (model == null && transform.childCount > 0) model = transform.GetChild(0);
            if (model) modelRest = model.localPosition;
            ownColliders = GetComponentsInChildren<Collider>(true);
            legsLayer = animator.GetLayerIndex(legsOnlyLayer);

            // ท่าตั้งต้น: กระดูกเท้าต่ำสุดสูงกว่าพื้นรองเท้า (vertex ต่ำสุด) เท่าไร
            var smr = GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr)
            {
                var mesh = new Mesh();
                smr.BakeMesh(mesh, true);
                float sole = float.MaxValue;
                foreach (Vector3 v in mesh.vertices) sole = Mathf.Min(sole, smr.transform.TransformPoint(v).y);
                toeAboveSole = Mathf.Max(0f, LowestFoot() - sole);
                Destroy(mesh);
            }
        }

        private void LateUpdate()
        {
            if (!animator.enabled) return; // ตายแล้ว ragdoll คุม

            Lift(chest, chestLift);
            Lift(neck, neckLift);
            Lift(head, headLift);
            if (keepFeetAboveGround) KeepFeetAboveGround();
        }

        private void Lift(Transform bone, float degrees)
        {
            if (bone && degrees != 0f) bone.rotation = Quaternion.AngleAxis(-degrees, transform.right) * bone.rotation;
        }

        private void KeepFeetAboveGround()
        {
            if (!model || !TryGroundY(out float ground)) return;
            float raw = LowestFoot() - lift;                       // ความสูงเท้าถ้าไม่ได้ยก
            float needed = ground + toeAboveSole - raw;
            if (!PlayingLegsOnly()) needed = Mathf.Max(0f, needed); // ปกติยกอย่างเดียว (กระโดดยังลอยได้)
            // จมต้องยกทันที (ไม่งั้นเห็นเท้ามุดแวบหนึ่ง) ส่วนอย่างอื่นค่อยๆ ไหล
            lift = needed > Mathf.Max(lift, 0f) ? needed : Mathf.Lerp(lift, needed, 1f - Mathf.Exp(-liftSmoothing * Time.deltaTime));
            model.localPosition = modelRest + Vector3.up * (lift / Mathf.Max(transform.lossyScale.y, 1e-4f));
        }

        // ท่าขาอย่างเดียว: สะโพกมาจากท่าเดิม (Mutant โน้มตัว) แต่ขามาจากท่าที่ยืนตรง → เท้าเอียงตามสะโพกจนจิกปลายเท้า
        // ตั้งเท้าให้ราบ (ตัดก้ม/เอียงออก เหลือแค่ทิศหัน) แล้วกันจมพื้นด้านบนจะดึงตัวลงให้เท้าแตะเต็มฝ่า
        // ต้องเปิด IK Pass ที่ layer นั้นใน Animator Controller
        private void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != legsLayer) return;
            flatFeet = Mathf.MoveTowards(flatFeet, PlayingLegsOnly() ? 1f : 0f, Time.deltaTime * 6f);
            foreach (AvatarIKGoal goal in new[] { AvatarIKGoal.LeftFoot, AvatarIKGoal.RightFoot })
            {
                Vector3 forward = Vector3.ProjectOnPlane(animator.GetIKRotation(goal) * Vector3.forward, Vector3.up);
                if (forward.sqrMagnitude < 1e-4f) continue;
                animator.SetIKRotationWeight(goal, flatFeet);
                animator.SetIKRotation(goal, Quaternion.LookRotation(forward, Vector3.up));
            }
        }

        private bool PlayingLegsOnly() =>
            legsLayer >= 0 && (animator.IsInTransition(legsLayer) || !animator.GetCurrentAnimatorStateInfo(legsLayer).IsName("Empty"));

        private float LowestFoot()
        {
            float y = float.MaxValue;
            foreach (Transform f in feet) if (f) y = Mathf.Min(y, f.position.y);
            return y == float.MaxValue ? transform.position.y : y;
        }

        // พื้นใต้ตัว (ข้าม collider ของตัวเอง)
        private bool TryGroundY(out float y)
        {
            y = 0f;
            float best = float.MaxValue;
            foreach (RaycastHit hit in Physics.RaycastAll(transform.position + Vector3.up * 10f, Vector3.down, 40f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                // พื้นจริงไม่มี Rigidbody — ข้ามชิ้นหุ่น/ของที่กองอยู่ ไม่งั้นแพนด้าลอยขึ้นไปเหยียบ
                if (System.Array.IndexOf(ownColliders, hit.collider) >= 0 || hit.collider.attachedRigidbody != null || hit.distance >= best) continue;
                best = hit.distance;
                y = hit.point.y;
            }
            return best < float.MaxValue;
        }
    }
}

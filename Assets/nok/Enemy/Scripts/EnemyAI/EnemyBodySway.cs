// =============================================================================
//  EnemyBodySway.cs
//  Client-visual procedural sway layered on top of the Animator pose.
//  Not a real Rigidbody ragdoll — a damped spring applied to bone rotations
//  in LateUpdate (after Animator evaluates), so it never fights NavMeshAgent
//  or the animation state machine. Runs identically on every client (and the
//  host) with zero network traffic; inputs are things already replicated
//  (Animator "Speed"/"IsRolling" params, and the hit-direction/force this
//  script receives locally from EnemyHealth's ClientRpc).
//
//  Setup in Unity (one-time):
//  ─────────────────────────────────────────────────────────────────────────
//  1. Add this component next to Animator on the Enemy prefab
//  2. Assign Spine / Chest / Head bones (Optional: L/R Upper Arm)
//  3. Leave a bone empty to skip it — nothing else changes
// =============================================================================

using UnityEngine;

namespace NscGame.Enemy
{
    [RequireComponent(typeof(Animator))]
    public class EnemyBodySway : MonoBehaviour
    {
        #region Inspector Fields

        [Header("Bone References (leave empty to skip)")]
        [SerializeField] private Transform spineBone;
        [SerializeField] private Transform chestBone;
        [SerializeField] private Transform headBone;
        [SerializeField] private Transform leftUpperArmBone;
        [SerializeField] private Transform rightUpperArmBone;

        [Header("Idle/Walk Wobble (\"ตัวย้วย\")")]
        [Tooltip("ความถี่ของการโยกตัว (รอบ/วินาที)")]
        [SerializeField] private float wobbleFrequency = 1.4f;

        [Tooltip("มุมโยกสูงสุดตอนเดินปกติ (องศา)")]
        [SerializeField] private float wobbleAmplitude = 4f;

        [Tooltip("ตัวคูณมุมโยกตอนกลิ้ง/พุ่งเข้าใส่ (แรงกว่าเดินปกติ)")]
        [SerializeField] private float rollWobbleMultiplier = 2.2f;

        [Header("Hit Lean (โดนต่อยแล้วเอียง)")]
        [Tooltip("แปลงแรงน็อคแบ็คเป็นมุมเอียง (ยิ่งมากยิ่งเอียงแรง)")]
        [SerializeField] private float hitImpulseMultiplier = 18f;

        [Tooltip("มุมเอียงสูงสุดที่ยอมให้เกิด กันไม่ให้บิดจนตัวทะลุ/ดูแปลก")]
        [SerializeField] private float maxLeanAngle = 35f;

        [Header("Spring Feel")]
        [Tooltip("ความ 'แข็ง' ของสปริง ยิ่งมากยิ่งดึงกลับตัวตรงเร็ว")]
        [SerializeField] private float stiffness = 90f;

        [Tooltip("ความหน่วง ยิ่งมากยิ่งหยุดสั่นเร็ว (กันสั่นค้าง)")]
        [SerializeField] private float damping = 6f;

        [Tooltip("สัดส่วนที่ Spine เอียงตาม Chest (0-1) ให้ตัวโค้งเป็นช่วงๆ แทนที่จะหักงอจุดเดียว")]
        [SerializeField, Range(0f, 1f)] private float spineFollowShare = 0.4f;

        [Tooltip("สัดส่วน/ความหน่วงที่หัวตามลำตัว (whip lag เบาๆ)")]
        [SerializeField, Range(0f, 1f)] private float headFollowShare = 0.65f;

        [SerializeField] private float headStiffness = 60f;
        [SerializeField] private float headDamping = 5f;

        [Header("Arm Sway")]
        [SerializeField] private float armSwayAmplitude = 6f;
        [SerializeField] private float armSwayFrequency = 1.8f;

        #endregion

        #region Private Fields

        private Animator animator;

        // สปริงหลักที่ Chest — pitch (X) = ก้ม/แหงน, roll (Z) = เอียงข้าง
        private Vector3 chestOffsetEuler;
        private Vector3 chestAngularVel;

        // สปริงรอง — หัวตาม Chest แบบหน่วงเวลา (whip lag)
        private Vector3 headOffsetEuler;
        private Vector3 headAngularVel;

        private float noiseSeedPitch;
        private float noiseSeedRoll;

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int IsRollingHash = Animator.StringToHash("IsRolling");
        private static readonly int IsDeadHash = Animator.StringToHash("IsDead");

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            animator = GetComponent<Animator>();

            // Seed สุ่มต่ออินสแตนซ์ กันศัตรูหลายตัวโยกซิงค์กันเป๊ะๆ จนดูเป็นแพทเทิร์น
            noiseSeedPitch = Random.Range(0f, 100f);
            noiseSeedRoll = Random.Range(0f, 100f);
        }

        private void LateUpdate()
        {
            // Animator ปิดตอน ragdoll ตาย (EnemyRagdoll.EnableRagdoll) — ห้ามเขียนทับ
            // localRotation ที่ physics engine คุมอยู่ ไม่งั้นกระตุกสู้กัน
            if (animator == null || !animator.enabled) return;
            if (animator.GetBool(IsDeadHash)) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            UpdateWobbleTarget(out float pitchTarget, out float rollTarget);

            SpringTowards(ref chestOffsetEuler, ref chestAngularVel,
                new Vector3(pitchTarget, 0f, rollTarget), stiffness, damping, dt);
            ClampAxes(ref chestOffsetEuler, maxLeanAngle);

            Vector3 headTarget = chestOffsetEuler * headFollowShare;
            SpringTowards(ref headOffsetEuler, ref headAngularVel, headTarget, headStiffness, headDamping, dt);
            ClampAxes(ref headOffsetEuler, maxLeanAngle);

            ApplyBoneOffsets();
        }

        #endregion

        #region Public API

        /// <summary>
        /// เตะสปริงให้เอียงตามแรง/ทิศที่โดน แล้วปล่อยให้มันดึงตัวกลับมายืนเอง
        /// เรียกจากฝั่ง Client เท่านั้น (EnemyHealth.PlayHitEffectsClientRpc) — ไม่มีการ sync สถานะนี้
        /// </summary>
        /// <param name="worldPushDirection">ทิศทางที่แรงกระแทกผลักตัว (เดียวกับที่ใช้คำนวณ knockback)</param>
        /// <param name="force">ขนาดแรง ใช้สเกลเดียวกับ knockback force</param>
        public void ApplyHitImpulse(Vector3 worldPushDirection, float force)
        {
            if (worldPushDirection.sqrMagnitude < 0.0001f || force <= 0f) return;

            Vector3 localDir = transform.InverseTransformDirection(worldPushDirection.normalized);

            // โดนดันไปข้างหน้า (local.z > 0) → ลำตัวแหงนไปด้านหลัง (pitch ลบ)
            // โดนดันไปด้านข้าง (local.x) → ลำตัวเอียงข้าง (roll)
            float pitchKick = -localDir.z * force * hitImpulseMultiplier;
            float rollKick = localDir.x * force * hitImpulseMultiplier;

            chestAngularVel += new Vector3(pitchKick, 0f, rollKick);
        }

        #endregion

        #region Private Helpers

        private void UpdateWobbleTarget(out float pitchTarget, out float rollTarget)
        {
            float speed = animator.GetFloat(SpeedHash);
            bool isRolling = animator.GetBool(IsRollingHash);

            // Speed พารามิเตอร์วิ่ง 0 (นิ่ง/โจมตี) → 0.5 (เดิน) ตาม EnemyController
            float speedFactor = Mathf.InverseLerp(0f, 0.5f, speed);
            float amplitude = wobbleAmplitude * speedFactor * (isRolling ? rollWobbleMultiplier : 1f);

            float t = Time.time * wobbleFrequency;
            pitchTarget = (Mathf.PerlinNoise(t, noiseSeedPitch) - 0.5f) * 2f * amplitude;
            rollTarget = (Mathf.PerlinNoise(t, noiseSeedRoll) - 0.5f) * 2f * amplitude;
        }

        private static void SpringTowards(ref Vector3 offset, ref Vector3 velocity, Vector3 target,
            float springStiffness, float springDamping, float dt)
        {
            Vector3 diff = target - offset;
            velocity += diff * springStiffness * dt;
            velocity *= Mathf.Clamp01(1f - springDamping * dt);
            offset += velocity * dt;
        }

        private static void ClampAxes(ref Vector3 euler, float maxAngle)
        {
            euler.x = Mathf.Clamp(euler.x, -maxAngle, maxAngle);
            euler.z = Mathf.Clamp(euler.z, -maxAngle, maxAngle);
        }

        private void ApplyBoneOffsets()
        {
            if (spineBone != null)
                spineBone.localRotation *= Quaternion.Euler(chestOffsetEuler * spineFollowShare);

            if (chestBone != null)
                chestBone.localRotation *= Quaternion.Euler(chestOffsetEuler);

            if (headBone != null)
                headBone.localRotation *= Quaternion.Euler(headOffsetEuler);

            if (leftUpperArmBone == null && rightUpperArmBone == null) return;

            float speed = animator.GetFloat(SpeedHash);
            float speedFactor = Mathf.InverseLerp(0f, 0.5f, speed);
            float armAngle = Mathf.Sin(Time.time * armSwayFrequency) * armSwayAmplitude * speedFactor;

            if (leftUpperArmBone != null)
                leftUpperArmBone.localRotation *= Quaternion.Euler(0f, 0f, armAngle);

            if (rightUpperArmBone != null)
                rightUpperArmBone.localRotation *= Quaternion.Euler(0f, 0f, -armAngle);
        }

        #endregion
    }
}

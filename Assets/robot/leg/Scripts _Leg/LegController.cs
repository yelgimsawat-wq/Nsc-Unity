using UnityEngine;

namespace NscUnity.Movement
{
    /// <summary>
    /// Handles the movement, animation scrubbing, and physics for a single robot leg.
    /// Supports manual animation control based on input duration.
    /// </summary>
    public class LegController : MonoBehaviour
    {
        public enum AnimationMode { SingleStateScrub, BlendTreeParameter }

        [Header("Animation Settings")]
        [SerializeField] private Animator animator;
        [SerializeField] private AnimationMode animationMode = AnimationMode.SingleStateScrub;
        [SerializeField] private string animationStateName = "Leg_CockPush";
        [SerializeField] private string blendTreeParameter = "LegCycle"; // Useful for separate clips in a Blend Tree
        [SerializeField] private float cockDuration = 1.0f;
        
        [Header("Speed Multipliers (4 Phases)")]
        [SerializeField] private float phase1_RaiseSpeed = 1.5f;     // จาก 0 ไป 1 (ยกขาม)
        [SerializeField] private float phase2_LowerSpeed = 0.8f;     // จาก 1 คืน 0 (วางขาม)
        [SerializeField] private float phase3_PushSpeed = 1.2f;      // จาก 0 ไป -1 (ดันพื้นม)
        [SerializeField] private float phase4_PullSpeed = 1.0f;      // จาก -1 คืน 0 (ดึงกลับม)

        [Header("Rotation Settings")]
        [SerializeField] private float rotationSpeed = 60f; // ความเร็วในการหมุนขา (องศา/วินาที)ม
        [SerializeField] private float rotationLimit = 180f; // Hardware safety limit (smart limits managed by RobotController)
        [SerializeField] private float baseXRotation = 0f; // Base X rotation to keep leg model upright
        
        private float targetValue = 0f;
        private float currentYaw = 0f; // เก็บค่าองศาการหมุนรวมม
        private float normalizedTime = 0f;

        public float GetTargetValue()
        {
            return targetValue;
        }

        public float GetNormalizedTime()
        {
            return normalizedTime;
        }

        public void SetTargetValue(float value)
        {
            targetValue = value;
        }

        public bool IsMovingAnimation()
        {
            // เช็คว่าขากำลังอยู่ในช่วงขยับไปยังเป้าหมายหรือไม่ม
            return Mathf.Abs(normalizedTime - targetValue) > 0.001f;
        }

        public void RotateLeg(float direction)
        {
            // คำนวณองศาใหม่และทำการ Clamp ให้อยู่ในระยะที่จำกัดม
            currentYaw += direction * rotationSpeed * Time.deltaTime;
            currentYaw = Mathf.Clamp(currentYaw, -rotationLimit, rotationLimit);

            ApplyRotation();
        }

        public void AdjustRotation(float delta)
        {
            // ใช้สำหรับหักลบองศาออกเมื่อตัวหุ่นหมุนตามขาแล้ว (Draining Rotation)ม
            currentYaw += delta;
            currentYaw = Mathf.Clamp(currentYaw, -rotationLimit, rotationLimit);
            ApplyRotation();
        }

        /// <summary>
        /// Applies the combined base rotation + yaw using proper quaternion math.
        /// </summary>
        private void ApplyRotation()
        {
            transform.localRotation = Quaternion.Euler(0f, currentYaw, 0f);
        }

        /// <summary>
        /// Returns the world-space horizontal direction this leg is facing,
        /// based on the parent's forward rotated by the leg's yaw.
        /// </summary>
        public Vector3 GetWorldForward()
        {
            Transform parentTransform = transform.parent;
            Vector3 baseForward = (parentTransform != null) ? parentTransform.forward : Vector3.forward;
            baseForward.y = 0f;
            if (baseForward.sqrMagnitude < 0.001f) baseForward = Vector3.forward;
            baseForward.Normalize();
            return Quaternion.AngleAxis(currentYaw, Vector3.up) * baseForward;
        }

        public float GetCurrentYaw()
        {
            return currentYaw;
        }

        /// <summary>
        /// Force-sets the yaw value. Used by RobotController to enforce world-space rotation limits.
        /// </summary>
        public void SetYaw(float yaw)
        {
            currentYaw = Mathf.Clamp(yaw, -rotationLimit, rotationLimit);
            ApplyRotation();
        }

        private void Update()
        {
            HandleAnimationScrubbing();
        }

        private void HandleAnimationScrubbing()
        {
            if (animator == null) return;

            // ตรวจเช็คชื่อพารามิเตอร์ (Safety Check)
            bool hasParam = false;
            foreach (var p in animator.parameters) { if (p.name == blendTreeParameter) { hasParam = true; break; } }
            if (!hasParam) return;

            // แยกการคำนวณความเร็วเป็น 4 เฟสแบบไดนามิกตามทิศทางที่เปลี่ยนไปจริงครับม
            float currentSpeedMultiplier = 1f;
            float movementDirection = Mathf.Sign(targetValue - normalizedTime);

            if (normalizedTime >= -0.01f) // โซนขาอยู่ด้านหน้า (0 ถึง 1)
            {
                if (movementDirection > 0) // กำลังขยับขึ้นม
                    currentSpeedMultiplier = phase1_RaiseSpeed;
                else // กำลังขยับลงมาที่ 0ม
                    currentSpeedMultiplier = phase2_LowerSpeed;
            }
            else // โซนขาอยู่ด้านหลัง (-1 ถึง 0)
            {
                if (movementDirection < 0) // กำลังขยับไปท่าดันพื้นม
                    currentSpeedMultiplier = phase3_PushSpeed;
                else // กำลังขยับกลับมาที่ 0ม
                    currentSpeedMultiplier = phase4_PullSpeed;
            }

            float finalSpeed = (1f / Mathf.Max(0.01f, cockDuration)) * currentSpeedMultiplier;

            normalizedTime = Mathf.MoveTowards(normalizedTime, targetValue, Time.deltaTime * finalSpeed);

            // Animation Control
            if (animationMode == AnimationMode.SingleStateScrub)
            {
                float scrubValue = (normalizedTime + 1f) / 2f;
                animator.Play(animationStateName, 0, scrubValue);
                animator.speed = 0;
            }
            else
            {
                bool isPlaying = false;
                for (int i = 0; i < animator.layerCount; i++)
                {
                    if (animator.GetCurrentAnimatorStateInfo(i).IsName(animationStateName))
                    {
                        isPlaying = true;
                        break;
                    }
                }

                if (!isPlaying && !string.IsNullOrEmpty(animationStateName)) 
                {
                    animator.Play(animationStateName);
                }
                
                animator.SetFloat(blendTreeParameter, normalizedTime);
            }
        }
    }
}

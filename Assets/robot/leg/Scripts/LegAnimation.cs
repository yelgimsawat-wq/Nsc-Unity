using UnityEngine;

namespace NscUnity.Movement
{
    /// <summary>
    /// Handles simple animation playback for a robot leg.
    /// No scrubbing, just playing states.
    /// </summary>
    public class LegAnimation : MonoBehaviour
    {
        [Header("Animation Settings")]
        [SerializeField] private Animator animator;
        [SerializeField] private string animationStateName = "Leg_Movement"; // ชื่อ State ที่มี Blend Tree หรือคลิปม
        [SerializeField] private string idleStateName = "Idle"; // ชื่อ State ตอนหยุดนิ่งม
        [SerializeField] private string blendTreeParameter = "LegCycle";
        [SerializeField] private bool useBlendTree = true;

        private void Start()
        {
            if (animator != null) animator.speed = 1f; // มั่นใจว่าแอนิเมชันไม่หยุดนิ่งม
        }

        public void SetInput(bool isPressed)
        {
            if (animator == null) return;

            string targetState = isPressed ? animationStateName : idleStateName;

            // ตรวจสอบว่ากำลังเล่น State นี้อยู่แล้วหรือไม่ เพื่อไม่ให้มันเริ่มใหม่ทุกเฟรมครับม
            bool isAlreadyPlaying = animator.GetCurrentAnimatorStateInfo(0).IsName(targetState);

            if (useBlendTree)
            {
                float targetValue = isPressed ? 1f : 0f;
                animator.SetFloat(blendTreeParameter, targetValue);
                
                if (!isAlreadyPlaying)
                {
                    animator.CrossFade(animationStateName, 0.1f);
                }
            }
            else
            {
                if (!isAlreadyPlaying)
                {
                    animator.CrossFade(targetState, 0.1f);
                }
            }
        }
    }
}

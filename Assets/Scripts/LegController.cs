using UnityEngine;

namespace NscUnity.Movement
{
    /// <summary>
    /// Handles the movement, animation scrubbing, and physics for a single robot leg.
    /// Supports manual animation control based on input duration.
    /// </summary>
    public class LegController : MonoBehaviour
    {
        [Header("Animation Settings")]
        [SerializeField] private Animator animator;
        [SerializeField] private string animationStateName = "Leg_CockPush"; // Combined animation cock + push
        [SerializeField] private float cockDuration = 1.0f; // Time to reach max cock height
        
        [Header("Physics Settings")]
        [SerializeField] private Rigidbody bodyRigidbody;
        [SerializeField] private float pushForce = 500f;
        [SerializeField] private Vector3 pushDirection = Vector3.forward;
        
        private float currentCockTime = 0f;
        private bool isInputHeld = false;
        
        // State tracking for networking synchronization
        public float CockNormalizedTime => Mathf.Clamp01(currentCockTime / cockDuration);

        private void Start()
        {
            if (animator == null) animator = GetComponent<Animator>();
            // Ensure the animator doesn't play automatically
            if (animator != null) animator.speed = 0;
        }

        /// <summary>
        /// Call this from the RobotController/Input source.
        /// </summary>
        /// <param name="isHeld">True if the control button is being pressed.</param>
        public void SetInput(bool isHeld)
        {
            isInputHeld = isHeld;
        }

        private void Update()
        {
            HandleAnimationScrubbing();
        }

        private void FixedUpdate()
        {
            HandlePhysicsPushing();
        }

        private void HandleAnimationScrubbing()
        {
            if (animator == null) return;

            // Increment or decrement based on input
            if (isInputHeld)
            {
                currentCockTime = Mathf.MoveTowards(currentCockTime, cockDuration, Time.deltaTime);
            }
            else
            {
                currentCockTime = Mathf.MoveTowards(currentCockTime, 0f, Time.deltaTime * 2f); // Return faster
            }

            // Manually scrub the animation
            // Using 0-0.5 for Cocking, 0.5-1.0 for Pushing (Conceptual mapping)
            float normalizedTime = CockNormalizedTime;
            animator.Play(animationStateName, 0, normalizedTime);
        }

        private void HandlePhysicsPushing()
        {
            if (bodyRigidbody == null) return;

            // In this specific mechanical design:
            // When THIS leg is cocking (input held), the PARTNER leg should be pushing.
            // Or if THIS leg is releasing, it might be the push stroke.
            
            // Logic: High currentCockTime means leg is lifted.
            // When releasing (returning to ground), that's when the "push" happens if it's the dominant leg.
            // Alternatively, the user request says: "While one leg is cocking, the other leg pushes".
            // So this method might be called by the partner leg or based on partner state.
        }

        /// <summary>
        /// External push command to be called by RobotController when coordinating legs.
        /// </summary>
        public void ApplyPushForce()
        {
            if (bodyRigidbody == null) return;
            
            // Apply force to the body
            bodyRigidbody.AddForce(transform.TransformDirection(pushDirection) * pushForce, ForceMode.Force);
        }
    }
}

using UnityEngine;
using UnityEngine.InputSystem;

namespace NscUnity.Movement
{
    /// <summary>
    /// Coordinates the movement of two legs using a step-cycle state machine.
    /// 
    /// Key behaviors:
    /// - Prevents push-exploit by requiring alternating steps
    /// - Resets step cycle when both legs return to neutral stance
    /// - Body rotates in real-time as lifted legs rotate (not just during push)
    /// - Rotation limits are relative to stored body direction at lift time (prevents turning exploit)
    /// - Movement direction = average of both leg forward vectors
    /// - Designed for future online multiplayer
    /// </summary>
    public class RobotController : MonoBehaviour
    {
        // --- Step-Cycle State Machine ---
        private enum StepPhase
        {
            WaitingForLift,  // No leg lifted yet — waiting for either leg to lift
            WaitingForPush,  // One leg is lifted — waiting for the OTHER leg to push
            Pushing,         // Push detected — continuously moving while push is active
            StepComplete     // Step done — waiting for both legs to return to idle
        }

        [Header("Leg Configuration")]
        [SerializeField] private LegController leftLeg;
        [SerializeField] private LegController rightLeg;
        [SerializeField] private Transform rootTransform; // The robot body that moves

        [Header("Movement Settings")]
        [SerializeField] private float walkSpeed = 2f;         // Continuous movement speed
        [SerializeField] private float bodyRotationSpeed = 5f; // How fast body rotates to face average direction
        [SerializeField] private bool invertMovement = false;

        [Header("Rotation Limits")]
        [Tooltip("Max degrees a lifted leg can deviate from the body direction at the moment it was lifted")]
        [SerializeField] private float maxLegDeviation = 35f;

        [Header("Balance Settings")]
        [Tooltip("Max yaw difference between legs before falling over")]
        [SerializeField] private float maxLegAngleDifference = 70f;
        [SerializeField] private float fallTiltAngle = 60f;
        [SerializeField] private float fallSpeed = 2f;

        [Header("Input Action References — Left Leg")]
        [SerializeField] private InputActionProperty leftLegLiftAction;
        [SerializeField] private InputActionProperty leftLegPushAction;
        [SerializeField] private InputActionProperty leftLegRotateLeftAction;
        [SerializeField] private InputActionProperty leftLegRotateRightAction;

        [Header("Input Action References — Right Leg")]
        [SerializeField] private InputActionProperty rightLegLiftAction;
        [SerializeField] private InputActionProperty rightLegPushAction;
        [SerializeField] private InputActionProperty rightLegRotateLeftAction;
        [SerializeField] private InputActionProperty rightLegRotateRightAction;

        // --- State Machine ---
        private StepPhase currentPhase = StepPhase.WaitingForLift;
        private bool liftedLegIsLeft;
        private bool lastStepLiftedLeft;
        private bool isFirstStep = true;

        // --- Fall State ---
        [SerializeField] private bool isFallen = false;
        private Quaternion targetFallRotation;

        // --- Lift Reference Tracking (turning exploit prevention) ---
        private bool leftWasLifted = false;
        private bool rightWasLifted = false;
        private float leftLiftReferenceYaw;   // Body world yaw when left leg was lifted
        private float rightLiftReferenceYaw;  // Body world yaw when right leg was lifted

        // --- Thresholds ---
        private const float LIFT_THRESHOLD = 0.1f;
        private const float PUSH_THRESHOLD = -0.1f;
        private const float IDLE_THRESHOLD = 0.15f;

        // ========================================================
        // Enable / Disable Input Actions
        // ========================================================

        private void OnEnable()
        {
            EnableAction(leftLegLiftAction);
            EnableAction(leftLegPushAction);
            EnableAction(leftLegRotateLeftAction);
            EnableAction(leftLegRotateRightAction);
            EnableAction(rightLegLiftAction);
            EnableAction(rightLegPushAction);
            EnableAction(rightLegRotateLeftAction);
            EnableAction(rightLegRotateRightAction);
        }

        private void OnDisable()
        {
            DisableAction(leftLegLiftAction);
            DisableAction(leftLegPushAction);
            DisableAction(leftLegRotateLeftAction);
            DisableAction(leftLegRotateRightAction);
            DisableAction(rightLegLiftAction);
            DisableAction(rightLegPushAction);
            DisableAction(rightLegRotateLeftAction);
            DisableAction(rightLegRotateRightAction);
        }

        private static void EnableAction(InputActionProperty prop)
        {
            if (prop.action != null) prop.action.Enable();
        }

        private static void DisableAction(InputActionProperty prop)
        {
            if (prop.action != null) prop.action.Disable();
        }

        // ========================================================
        // Update Loop
        // ========================================================

        private void Update()
        {
            if (!isFallen)
            {
                HandleLegInput();
                UpdateLiftReferences();
                HandleLegRotation();
                UpdateBodyRotation();
                CheckIdleReset();
                UpdateStepStateMachine();
                CheckBalance();
            }
            else
            {
                if (rootTransform != null)
                {
                    rootTransform.localRotation = Quaternion.Slerp(
                        rootTransform.localRotation,
                        targetFallRotation,
                        Time.deltaTime * fallSpeed
                    );
                }
            }
        }

        // ========================================================
        // Input Handling (Network-Ready)
        // ========================================================

        private void HandleLegInput()
        {
            if (leftLeg == null || rightLeg == null) return;
            if (leftLegLiftAction.action == null || rightLegLiftAction.action == null) return;

            bool holdLiftL = leftLegLiftAction.action.IsPressed();
            bool holdPushL = leftLegPushAction.action != null && leftLegPushAction.action.IsPressed();
            bool holdLiftR = rightLegLiftAction.action.IsPressed();
            bool holdPushR = rightLegPushAction.action != null && rightLegPushAction.action.IsPressed();

            float leftNorm = leftLeg.GetNormalizedTime();
            float rightNorm = rightLeg.GetNormalizedTime();

            // DEFAULT: Stop in place (target = current progress)
            float leftT = leftNorm;
            float rightT = rightNorm;

            // 1. Left Leg Logic
            if (holdLiftL)
            {
                // Alternating Rule: Can only lift if it's Left's turn AND Right is on ground
                bool isLeftTurn = isFirstStep || !lastStepLiftedLeft;
                if (isLeftTurn && rightNorm <= LIFT_THRESHOLD) leftT = 1f;
            }
            else if (holdPushL)
            {
                leftT = -1f;
            }

            // 2. Right Leg Logic
            if (holdLiftR)
            {
                // Alternating Rule: Can only lift if it's Right's turn AND Left is on ground
                bool isRightTurn = isFirstStep || lastStepLiftedLeft;
                if (isRightTurn && leftNorm <= LIFT_THRESHOLD) rightT = 1f;
            }
            else if (holdPushR)
            {
                rightT = -1f;
            }

            leftLeg.SetTargetValue(leftT);
            rightLeg.SetTargetValue(rightT);
        }

        // ========================================================
        // Lift Reference Tracking (Turning Exploit Prevention)
        // ========================================================

        /// <summary>
        /// Detects when legs transition between lifted/grounded and stores/clears
        /// the body direction reference used for rotation clamping.
        /// </summary>
        private void UpdateLiftReferences()
        {
            if (leftLeg == null || rightLeg == null || rootTransform == null) return;

            bool leftLifted = leftLeg.GetNormalizedTime() > LIFT_THRESHOLD;
            bool rightLifted = rightLeg.GetNormalizedTime() > LIFT_THRESHOLD;

            // Left leg just lifted → store reference body direction
            if (leftLifted && !leftWasLifted)
            {
                leftLiftReferenceYaw = rootTransform.eulerAngles.y;
            }

            // Right leg just lifted → store reference body direction
            if (rightLifted && !rightWasLifted)
            {
                rightLiftReferenceYaw = rootTransform.eulerAngles.y;
            }

            // Update tracking flags
            leftWasLifted = leftLifted;
            rightWasLifted = rightLifted;
        }

        // ========================================================
        // Leg Rotation with World-Space Clamping
        // ========================================================

        private void HandleLegRotation()
        {
            if (leftLeg == null || rightLeg == null) return;

            // Strict Manual Rotation: Only for the side that is officially lifted according to the state machine
            if (currentPhase == StepPhase.WaitingForPush || currentPhase == StepPhase.Pushing)
            {
                if (liftedLegIsLeft && leftWasLifted)
                {
                    float rotateVal = 0f;
                    if (leftLegRotateLeftAction.action != null && leftLegRotateLeftAction.action.IsPressed()) rotateVal -= 1f;
                    if (leftLegRotateRightAction.action != null && leftLegRotateRightAction.action.IsPressed()) rotateVal += 1f;

                    if (rotateVal != 0f)
                    {
                        leftLeg.RotateLeg(rotateVal);
                        ClampLiftedLegRotation(leftLeg, leftLiftReferenceYaw);
                    }
                }
                else if (!liftedLegIsLeft && rightWasLifted)
                {
                    float rotateVal = 0f;
                    if (rightLegRotateLeftAction.action != null && rightLegRotateLeftAction.action.IsPressed()) rotateVal -= 1f;
                    if (rightLegRotateRightAction.action != null && rightLegRotateRightAction.action.IsPressed()) rotateVal += 1f;

                    if (rotateVal != 0f)
                    {
                        rightLeg.RotateLeg(rotateVal);
                        ClampLiftedLegRotation(rightLeg, rightLiftReferenceYaw);
                    }
                }
            }
        }

        /// <summary>
        /// Clamps a lifted leg's rotation so its world-space direction stays within
        /// ±maxLegDeviation of the body direction stored at lift time.
        /// This prevents the infinite turning exploit.
        /// </summary>
        private void ClampLiftedLegRotation(LegController leg, float referenceBodyYaw)
        {
            if (rootTransform == null) return;

            float currentBodyYaw = rootTransform.eulerAngles.y;
            float legWorldYaw = currentBodyYaw + leg.GetCurrentYaw();

            // How far the leg's world direction is from the stored reference
            float deltaFromReference = Mathf.DeltaAngle(referenceBodyYaw, legWorldYaw);

            if (Mathf.Abs(deltaFromReference) > maxLegDeviation)
            {
                float clampedDelta = Mathf.Clamp(deltaFromReference, -maxLegDeviation, maxLegDeviation);
                float clampedWorldYaw = referenceBodyYaw + clampedDelta;
                float newLocalYaw = Mathf.DeltaAngle(currentBodyYaw, clampedWorldYaw);
                leg.SetYaw(newLocalYaw);
            }
        }

        // ========================================================
        // Body Rotation (Runs EVERY frame, not just during push)
        // ========================================================

        /// <summary>
        /// Rotates the body to face the average direction of both legs every frame.
        /// This makes the character turn in real-time as the lifted leg rotates,
        /// without waiting for a push.
        /// </summary>
        private void UpdateBodyRotation()
        {
            if (rootTransform == null || leftLeg == null || rightLeg == null) return;

            Vector3 leftFwd = leftLeg.GetWorldForward();
            Vector3 rightFwd = rightLeg.GetWorldForward();
            Vector3 averageDir = (leftFwd + rightFwd).normalized;

            averageDir.y = 0f;
            if (averageDir.sqrMagnitude < 0.001f) return;
            averageDir.Normalize();

            Quaternion previousRotation = rootTransform.rotation;
            Quaternion targetRotation = Quaternion.LookRotation(averageDir, Vector3.up);
            rootTransform.rotation = Quaternion.Slerp(previousRotation, targetRotation, Time.deltaTime * bodyRotationSpeed);

            // Calculate how much the body actually rotated this frame
            float appliedYawDelta = rootTransform.rotation.eulerAngles.y - previousRotation.eulerAngles.y;
            if (appliedYawDelta > 180f) appliedYawDelta -= 360f;
            if (appliedYawDelta < -180f) appliedYawDelta += 360f;

            // STIFF LEGS: Only drain yaw from LIFTED legs so grounded legs turn with the body
            if (Mathf.Abs(appliedYawDelta) > 0.01f)
            {
                if (leftWasLifted) leftLeg.AdjustRotation(-appliedYawDelta);
                if (rightWasLifted) rightLeg.AdjustRotation(-appliedYawDelta);
            }
        }

        // ========================================================
        // Idle Reset (Fixes step memory bug)
        // ========================================================

        /// <summary>
        /// When both legs return to a neutral standing position, reset the step cycle.
        /// This allows either leg to start the next step, fixing the bug where
        /// the system remembered which leg pushed last even when standing still.
        /// </summary>
        private void CheckIdleReset()
        {
            // Don't reset during an active push
            if (currentPhase == StepPhase.Pushing) return;
            if (leftLeg == null || rightLeg == null) return;

            float leftNorm = leftLeg.GetNormalizedTime();
            float rightNorm = rightLeg.GetNormalizedTime();

            bool bothIdle = Mathf.Abs(leftNorm) < IDLE_THRESHOLD && Mathf.Abs(rightNorm) < IDLE_THRESHOLD;

            if (bothIdle)
            {
                isFirstStep = true;
                currentPhase = StepPhase.WaitingForLift;
            }
        }

        // ========================================================
        // Step-Cycle State Machine (Anti-Exploit)
        // ========================================================

        private void UpdateStepStateMachine()
        {
            if (leftLeg == null || rightLeg == null || rootTransform == null) return;

            float leftNorm = leftLeg.GetNormalizedTime();
            float rightNorm = rightLeg.GetNormalizedTime();

            switch (currentPhase)
            {
                case StepPhase.WaitingForLift:
                    HandleWaitingForLift(leftNorm, rightNorm);
                    break;

                case StepPhase.WaitingForPush:
                    HandleWaitingForPush(leftNorm, rightNorm);
                    break;

                case StepPhase.Pushing:
                    HandlePushing(leftNorm, rightNorm);
                    break;

                case StepPhase.StepComplete:
                    HandleStepComplete(leftNorm, rightNorm);
                    break;
            }
        }

        private void HandleWaitingForLift(float leftNorm, float rightNorm)
        {
            bool leftLifted = leftNorm > LIFT_THRESHOLD;
            bool rightLifted = rightNorm > LIFT_THRESHOLD;

            if (leftLifted && !rightLifted)
            {
                if (isFirstStep || lastStepLiftedLeft == false)
                {
                    liftedLegIsLeft = true;
                    currentPhase = StepPhase.WaitingForPush;
                }
            }
            else if (rightLifted && !leftLifted)
            {
                if (isFirstStep || lastStepLiftedLeft == true)
                {
                    liftedLegIsLeft = false;
                    currentPhase = StepPhase.WaitingForPush;
                }
            }
        }

        private void HandleWaitingForPush(float leftNorm, float rightNorm)
        {
            if (liftedLegIsLeft)
            {
                bool rightStartedPushing = rightLeg.GetTargetValue() == -1f && rightLeg.IsMovingAnimation();
                bool leftStillLifted = leftNorm > LIFT_THRESHOLD;

                if (rightStartedPushing && leftStillLifted)
                {
                    currentPhase = StepPhase.Pushing;
                }

                if (!leftStillLifted && rightNorm > PUSH_THRESHOLD)
                {
                    currentPhase = StepPhase.WaitingForLift;
                }
            }
            else
            {
                bool leftStartedPushing = leftLeg.GetTargetValue() == -1f && leftLeg.IsMovingAnimation();
                bool rightStillLifted = rightNorm > LIFT_THRESHOLD;

                if (leftStartedPushing && rightStillLifted)
                {
                    currentPhase = StepPhase.Pushing;
                }

                if (!rightStillLifted && leftNorm > PUSH_THRESHOLD)
                {
                    currentPhase = StepPhase.WaitingForLift;
                }
            }
        }

        private void HandlePushing(float leftNorm, float rightNorm)
        {
            LegController pushLeg = liftedLegIsLeft ? rightLeg : leftLeg;
            float liftedNorm = liftedLegIsLeft ? leftNorm : rightNorm;

            bool pushLegStillActive = pushLeg.GetTargetValue() == -1f && pushLeg.IsMovingAnimation();
            bool liftedLegStillUp = liftedNorm > LIFT_THRESHOLD;

            if (pushLegStillActive && liftedLegStillUp)
            {
                // Continuously move while pushing
                ApplyContinuousMovement();
            }
            else if (liftedLegStillUp)
            {
                // Multi-push support: if leg is still up but push finished, allow another push
                currentPhase = StepPhase.WaitingForPush;
            }
            else
            {
                CompleteStep();
            }
        }

        private void HandleStepComplete(float leftNorm, float rightNorm)
        {
            // CheckIdleReset() handles the transition back to WaitingForLift
            // This phase just blocks new steps until both legs are idle
        }

        // ========================================================
        // Movement (Position Only)
        // ========================================================

        /// <summary>
        /// Moves the robot forward along the averaged leg direction.
        /// Body rotation is NOT handled here — it's in UpdateBodyRotation() which runs every frame.
        /// </summary>
        private void ApplyContinuousMovement()
        {
            if (rootTransform == null) return;

            Vector3 leftFwd = leftLeg.GetWorldForward();
            Vector3 rightFwd = rightLeg.GetWorldForward();
            Vector3 averageDir = (leftFwd + rightFwd).normalized;

            averageDir.y = 0f;
            if (averageDir.sqrMagnitude < 0.001f) averageDir = rootTransform.forward;
            averageDir.Normalize();

            if (invertMovement) averageDir = -averageDir;

            rootTransform.position += averageDir * walkSpeed * Time.deltaTime;
        }

        private void CompleteStep()
        {
            lastStepLiftedLeft = liftedLegIsLeft;
            isFirstStep = false;
            currentPhase = StepPhase.StepComplete;
        }

        // ========================================================
        // Balance / Fall Detection
        // ========================================================

        private void CheckBalance()
        {
            if (leftLeg == null || rightLeg == null) return;

            float leftNorm = leftLeg.GetNormalizedTime();
            float rightNorm = rightLeg.GetNormalizedTime();

            bool leftOnGround = leftNorm <= LIFT_THRESHOLD;
            bool rightOnGround = rightNorm <= LIFT_THRESHOLD;

            // 1. Both legs lifted = fall
            if (!leftOnGround && !rightOnGround)
            {
                TriggerFall("No legs on ground! (Both Lifted)");
                return;
            }

            // 2. Both legs pushing deep = fall
            if (leftNorm < -0.3f && rightNorm < -0.3f)
            {
                TriggerFall("Both legs pushing too deep! (Dual-Push < -0.3)");
                return;
            }

            // 3. Leg spread too wide = fall
            float angleDiff = Mathf.Abs(leftLeg.GetCurrentYaw() - rightLeg.GetCurrentYaw());
            if (angleDiff > maxLegAngleDifference)
            {
                TriggerFall($"Leg angle difference too large: {angleDiff:F1}° > {maxLegAngleDifference}°");
                return;
            }
        }

        private void TriggerFall(string reason)
        {
            if (isFallen) return;
            isFallen = true;
            Debug.LogError($"Robot has Fallen! Reason: {reason}");

            if (rootTransform != null)
            {
                targetFallRotation = rootTransform.localRotation * Quaternion.Euler(Vector3.right * fallTiltAngle);
            }
        }

        // ========================================================
        // Utility
        // ========================================================

        [ContextMenu("Reset Robot")]
        public void ResetRobot()
        {
            isFallen = false;
            isFirstStep = true;
            currentPhase = StepPhase.WaitingForLift;
            leftWasLifted = false;
            rightWasLifted = false;

            if (rootTransform != null)
            {
                rootTransform.localRotation = Quaternion.identity;
            }
            Debug.Log("Robot Reset.");
        }

        // ========================================================
        // Networking Prepared Methods
        // ========================================================

        public void SetLeftLegTarget(float normalizedTarget)
        {
            if (leftLeg != null) leftLeg.SetTargetValue(normalizedTarget);
        }

        public void SetRightLegTarget(float normalizedTarget)
        {
            if (rightLeg != null) rightLeg.SetTargetValue(normalizedTarget);
        }

        public void RotateLeftLeg(float direction)
        {
            if (leftLeg != null && leftWasLifted)
            {
                leftLeg.RotateLeg(direction);
                ClampLiftedLegRotation(leftLeg, leftLiftReferenceYaw);
            }
        }

        public void RotateRightLeg(float direction)
        {
            if (rightLeg != null && rightWasLifted)
            {
                rightLeg.RotateLeg(direction);
                ClampLiftedLegRotation(rightLeg, rightLiftReferenceYaw);
            }
        }

        /// <summary>
        /// Sync the full leg state from the server to clients.
        /// </summary>
        public void SyncLegState(float leftNormalized, float rightNormalized)
        {
            // This is where remote synchronization would happen
        }
    }
}

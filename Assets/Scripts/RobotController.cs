using UnityEngine;
using UnityEngine.InputSystem;

namespace NscUnity.Movement
{
    /// <summary>
    /// Coordinates the movement of multiple legs and handles player input.
    /// Manages the logic of one leg cocking while the other pushes.
    /// </summary>
    public class RobotController : MonoBehaviour
    {
        [Header("Leg Configuration")]
        [SerializeField] private LegController leftLeg;
        [SerializeField] private LegController rightLeg;
        
        [Header("Input Action References")]
        [SerializeField] private InputActionProperty leftLegAction;
        [SerializeField] private InputActionProperty rightLegAction;

        private void Update()
        {
            HandleLegInput();
        }

        private void HandleLegInput()
        {
            // Read input for both legs
            bool leftHeld = leftLegAction.action.IsPressed();
            bool rightHeld = rightLegAction.action.IsPressed();

            // Update Left Leg
            if (leftLeg != null)
            {
                leftLeg.SetInput(leftHeld);
                
                // Coordination Logic: If left is cocking (lifting), right should push
                if (leftHeld && rightLeg != null)
                {
                    rightLeg.ApplyPushForce();
                }
            }

            // Update Right Leg
            if (rightLeg != null)
            {
                rightLeg.SetInput(rightHeld);
                
                // Coordination Logic: If right is cocking (lifting), left should push
                if (rightHeld && leftLeg != null)
                {
                    leftLeg.ApplyPushForce();
                }
            }
        }

        // --- Networking Prepared Methods ---
        
        /// <summary>
        /// This would be called by a NetworkBehaviour on the Server to sync state to clients.
        /// Or used with NetworkVariables in Netcode for GameObjects.
        /// </summary>
        public void SyncLegState(float leftNormalized, float rightNormalized)
        {
            // This is where remote synchronization would happen
        }
    }
}

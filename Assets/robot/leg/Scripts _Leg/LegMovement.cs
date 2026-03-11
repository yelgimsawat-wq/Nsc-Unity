using UnityEngine;

namespace NscUnity.Movement
{
    /// <summary>
    /// Handles simple transform-based movement for a robot leg.
    /// No physics (Rigidbody) used for actual displacement.
    /// </summary>
    public class LegMovement : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 2f;
        [SerializeField] private Vector3 moveDirection = Vector3.forward;

        private bool shouldMove = false;

        public void SetMoving(bool state)
        {
            shouldMove = state;
        }

        private void Update()
        {
            if (shouldMove)
            {
                // เลื่อนตำแหน่งไปข้างหน้าโดยตรงม
                transform.Translate(moveDirection * moveSpeed * Time.deltaTime, Space.Self);
            }
        }
    }
}

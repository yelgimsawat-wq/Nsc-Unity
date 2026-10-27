using System;
using UnityEngine;

namespace Nsc.BoneLimb
{
    /// <summary>กล้องหมุนรอบตัวหุ่น (คลิกขวาค้าง) + ซูม (ล้อเมาส์)</summary>
    [Serializable]
    public class OrbitCamera
    {
        public float distance = 36f;
        [Tooltip("ระยะกล้องใกล้สุด/ไกลสุด")]
        public Vector2 distanceRange = new Vector2(12f, 90f);
        public float lookHeight = 8f;
        public float lookSensitivity = 0.2f;
        public float zoomSpeed = 0.02f;

        private float yaw, pitch = 15f;

        public void FaceYaw(float degrees) => yaw = degrees;

        public void Look(Vector2 mouseDelta)
        {
            yaw += mouseDelta.x * lookSensitivity;
            pitch = Mathf.Clamp(pitch - mouseDelta.y * lookSensitivity, -10f, 70f);
        }

        public void Zoom(float scroll) =>
            distance = Mathf.Clamp(distance - scroll * zoomSpeed, distanceRange.x, distanceRange.y);

        public void Apply(Camera cam, Vector3 pivot)
        {
            Vector3 look = pivot + Vector3.up * lookHeight;
            cam.transform.position = look + Quaternion.Euler(pitch, yaw, 0f) * new Vector3(0f, 0f, -distance);
            cam.transform.LookAt(look);
        }
    }
}

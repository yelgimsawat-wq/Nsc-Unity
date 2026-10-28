using System;
using Nsc.Feel;
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
        [Tooltip("กล้องค่อยๆ หมุนกลับไปอยู่หลังหุ่นเมื่อไม่ได้คลิกขวาหันกล้อง (มาก = ตามไว, 0 = ปิด)")]
        public float followBehind;

        [Header("มุมตอนคุมแขน (แบบหุ่นเดิม: สูงขึ้น ใกล้ขึ้น เลื่อนไปทางไหล่ที่คุม)")]
        public bool armView;
        public float armPitch = 18f;
        public float armDistance = 6f;
        public float armLookHeight = 11.5f;
        public float armSideShift = 2.5f;
        public float reframeSpeed = 6f;

        [Tooltip("มุมก้มตอนเริ่ม (หุ่นเดิมตอนคุมขา = 55 มองเห็นพื้นรอบเท้า)")]
        public float startPitch = 15f;

        private float yaw, pitch = 15f;
        private float baseFov = -1f;
        private float armBlend, armSideNow;

        public void FaceYaw(float degrees)
        {
            yaw = degrees;
            pitch = startPitch;
        }

        public void Follow(float bodyYaw, float dt)
        {
            if (followBehind > 0f) yaw = Mathf.LerpAngle(yaw, bodyYaw, 1f - Mathf.Exp(-followBehind * dt));
        }

        public void Look(Vector2 mouseDelta)
        {
            yaw += mouseDelta.x * lookSensitivity;
            pitch = Mathf.Clamp(pitch - mouseDelta.y * lookSensitivity, -10f, 70f);
        }

        public void Zoom(float scroll) =>
            distance = Mathf.Clamp(distance - scroll * zoomSpeed, distanceRange.x, distanceRange.y);

        /// <param name="armSide">คุมแขนอยู่: -1 ซ้าย / +1 ขวา, ไม่ได้คุมแขน = 0</param>
        public void Apply(Camera cam, Vector3 pivot, float armSide = 0f)
        {
            if (CameraShake.IsFrozen) return; // หยุดภาพตอนหมัดโดน — กล้องค้างแวบหนึ่ง
            float t = 1f - Mathf.Exp(-reframeSpeed * Time.deltaTime);
            armBlend = Mathf.Lerp(armBlend, armView && armSide != 0f ? 1f : 0f, t);
            if (armSide != 0f) armSideNow = Mathf.Lerp(armSideNow, armSide, t);

            // คุมแขน: มุมก้มแขนบวกส่วนที่ผู้เล่นหันเองจากมุมเริ่ม (คลิกขวายังหันได้)
            Quaternion view = Quaternion.Euler(Mathf.Clamp(pitch + (armPitch - startPitch) * armBlend, -10f, 80f), yaw, 0f);
            Vector3 look = pivot + Vector3.up * Mathf.Lerp(lookHeight, armLookHeight, armBlend)
                         + Quaternion.Euler(0f, yaw, 0f) * Vector3.right * (armSideNow * armSideShift * armBlend);
            cam.transform.position = look + view * new Vector3(0f, 0f, -Mathf.Lerp(distance, armDistance, armBlend));
            cam.transform.LookAt(look);

            // จอสั่นจากเท้ายักษ์/หมัด (CameraShake ของ Nsc.Feel) + ซูมเข้าแวบหนึ่งตอนหมัดหนัก
            CameraShake.Sample(pivot, out Vector3 positionOffset, out Vector3 rotationOffset);
            cam.transform.position += cam.transform.rotation * positionOffset;
            cam.transform.rotation *= Quaternion.Euler(rotationOffset);
            if (baseFov < 0f) baseFov = cam.fieldOfView;
            cam.fieldOfView = baseFov - CameraShake.FovOffset;
        }
    }
}

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
        [Tooltip("ล้อเมาส์ซูม: ตัวคูณระยะกล้องน้อยสุด/มากสุด — ใช้กับทุกมุม (ขา/แขน/ตอนล้ม) 1 = ระยะที่ตั้งไว้")]
        public Vector2 zoomRange = new Vector2(0.4f, 3f);
        [Tooltip("ล้อเมาส์ 1 คลิกเข้า/ออกกี่ส่วน (0.1 = 10%)")]
        public float zoomStep = 0.1f;
        public float lookHeight = 8f;
        public float lookSensitivity = 0.2f;
        [Tooltip("หุ่นเดิม (PlayerCam followDeadZone): ตัวหุ่นขยับไม่เกินระยะนี้กล้องอยู่นิ่ง — ตัวโยกตอนก้าวไม่ลากกล้อง (0 = ตามตลอด)")]
        public float followDeadZone = 2f;

        [Header("มุมตอนคุมแขน (แบบหุ่นเดิม: สูงขึ้น ใกล้ขึ้น เลื่อนไปทางไหล่ที่คุม)")]
        public bool armView;
        public float armPitch = 18f;
        public float armDistance = 6f;
        public float armLookHeight = 11.5f;
        public float armSideShift = 2.5f;
        public float reframeSpeed = 6f;

        [Tooltip("มุมก้มตอนเริ่ม (หุ่นเดิมตอนคุมขา = 55 มองเห็นพื้นรอบเท้า)")]
        public float startPitch = 15f;

        [Header("มุมตอนหุ่นล้ม (มองลงมาที่ตัวหุ่นที่นอนบนพื้น)")]
        public float downPitch = 45f;
        public float downDistance = 16f;
        public float downLookHeight = 2f;

        private float yaw, pitch = 15f;
        private float zoom = 1f;
        private float baseFov = -1f;
        private float armBlend, armSideNow, downBlend;
        private Vector3 deadZoneAnchor;
        private bool anchorSet;

        public void FaceYaw(float degrees)
        {
            yaw = degrees;
            pitch = startPitch;
        }

        // หุ่นเดิม (PlayerCam.FollowWithDeadZone): จุดมองขยับเมื่อตัวหุ่นออกนอกวงนิ่ง แล้วเลื่อนแค่ให้ตัวอยู่ขอบวง
        private Vector3 FollowWithDeadZone(Vector3 target)
        {
            if (!anchorSet || followDeadZone <= 0f) { deadZoneAnchor = target; anchorSet = true; return target; }
            Vector3 offset = target - deadZoneAnchor;
            float excess = offset.magnitude - followDeadZone;
            if (excess > 0f) deadZoneAnchor += offset.normalized * excess;
            return deadZoneAnchor;
        }

        public void Look(Vector2 mouseDelta)
        {
            yaw += mouseDelta.x * lookSensitivity;
            pitch = Mathf.Clamp(pitch - mouseDelta.y * lookSensitivity, -10f, 70f);
        }

        // ซูมเป็นตัวคูณ ไม่ใช่ปรับ distance ตรงๆ — มุมแขน/ตอนล้มใช้ระยะของตัวเอง ปรับ distance แล้วซูมไม่ติด
        // Input System: ล้อ 1 คลิก = 120
        public void Zoom(float scroll) =>
            zoom = Mathf.Clamp(zoom * Mathf.Pow(1f - zoomStep, scroll / 120f), zoomRange.x, zoomRange.y);

        /// <param name="armSide">คุมแขนอยู่: -1 ซ้าย / +1 ขวา, ไม่ได้คุมแขน = 0</param>
        /// <param name="down">หุ่นนอนอยู่ — เลิกมุมแขน (ระดับไหล่ตอนยืน = มองกลางอากาศ) ค่อยๆ ก้มลงมองตัวหุ่นบนพื้น</param>
        public void Apply(Camera cam, Vector3 pivot, float armSide = 0f, bool down = false)
        {
            if (CameraShake.IsFrozen) return; // หยุดภาพตอนหมัดโดน — กล้องค้างแวบหนึ่ง
            pivot = FollowWithDeadZone(pivot);
            float t = 1f - Mathf.Exp(-reframeSpeed * Time.deltaTime);
            armBlend = Mathf.Lerp(armBlend, armView && armSide != 0f && !down ? 1f : 0f, t);
            downBlend = Mathf.Lerp(downBlend, down ? 1f : 0f, t);
            if (armSide != 0f) armSideNow = Mathf.Lerp(armSideNow, armSide, t);

            // คุมแขน/นอน: มุมก้มของท่านั้นบวกส่วนที่ผู้เล่นหันเองจากมุมเริ่ม (คลิกขวายังหันได้)
            float tilt = pitch + (armPitch - startPitch) * armBlend + (downPitch - startPitch) * downBlend;
            Quaternion view = Quaternion.Euler(Mathf.Clamp(tilt, -10f, 80f), yaw, 0f);
            Vector3 look = pivot + Vector3.up * Mathf.Lerp(Mathf.Lerp(lookHeight, armLookHeight, armBlend), downLookHeight, downBlend)
                         + Quaternion.Euler(0f, yaw, 0f) * Vector3.right * (armSideNow * armSideShift * armBlend);
            float dist = Mathf.Lerp(Mathf.Lerp(distance, armDistance, armBlend), downDistance, downBlend) * zoom;
            cam.transform.position = look + view * new Vector3(0f, 0f, -dist);
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

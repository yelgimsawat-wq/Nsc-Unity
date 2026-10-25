using System.Collections.Generic;
using UnityEngine;

namespace Nsc.Feel
{
    /// <summary>
    /// แรงสั่นของกล้องในเครื่องนี้ — ใครก็ส่งแรงกระแทกเข้ามาได้ (ก้าวเท้า หมัด บอส) PlayerCam เป็นคนอ่านไปใช้
    /// แรงลดตามระยะจากจุดที่กล้องตาม และจางลงตามเวลา ใช้เวลาจริง (unscaled) ทั้งหมด
    /// "ค้างกล้อง" (Freeze) คือ hit-stop ฝั่งภาพ: กล้องหยุดตามเป้าแวบหนึ่ง แล้วค่อยสั่น — ไม่แตะ Time.timeScale
    /// (เกมนี้ห้ามแตะ timeScale: เคยทำให้ physics ค้าง และใน multiplayer จะหยุดฟิสิกส์ของทุกคนบน host)
    /// </summary>
    public static class CameraShake
    {
        private struct Impulse
        {
            public Vector3 position;
            public float strength;
            public float start;
            public float duration;
            public float seed;
        }

        private static readonly List<Impulse> impulses = new List<Impulse>();
        private static float freezeUntil;
        private static float fovKick;
        private static float fovKickStart;
        private const float FovKickDuration = 0.25f;

        public static bool IsFrozen => Time.unscaledTime < freezeUntil;

        public static void Add(Vector3 worldPosition, float strength, float duration)
        {
            if (strength <= 0f || duration <= 0f) return;
            if (impulses.Count > 32) impulses.RemoveAt(0);
            impulses.Add(new Impulse
            {
                position = worldPosition,
                strength = strength,
                start = Time.unscaledTime,
                duration = duration,
                seed = Random.value * 100f
            });
        }

        public static void Freeze(float seconds)
        {
            if (seconds > 0f) freezeUntil = Mathf.Max(freezeUntil, Time.unscaledTime + seconds);
        }

        public static void KickFov(float degrees)
        {
            if (degrees <= fovKick * CurrentKickEnvelope()) return; // แรงกว่าเท่านั้นถึงทับได้
            fovKick = degrees;
            fovKickStart = Time.unscaledTime;
        }

        /// <summary>FOV ที่ต้องลบออกตอนนี้ (ซูมเข้าแล้วคลายออก)</summary>
        public static float FovOffset => fovKick * CurrentKickEnvelope();

        private static float CurrentKickEnvelope()
        {
            float t = (Time.unscaledTime - fovKickStart) / FovKickDuration;
            return t >= 1f ? 0f : 1f - t * t;
        }

        /// <summary>รวมแรงสั่นทั้งหมดที่ตำแหน่งนี้ → ระยะเลื่อน (แกนกล้อง) กับมุมหมุน (องศา)</summary>
        public static void Sample(Vector3 listener, out Vector3 positionOffset, out Vector3 rotationOffset)
        {
            positionOffset = Vector3.zero;
            rotationOffset = Vector3.zero;

            FeelConfig config = FeelConfig.Instance;
            float maxPos = config != null ? config.shakePosition : 0.6f;
            float maxRot = config != null ? config.shakeRotation : 1.4f;
            float frequency = config != null ? config.shakeFrequency : 22f;
            float falloff = config != null ? Mathf.Max(1f, config.shakeFalloffDistance) : 45f;

            float now = Time.unscaledTime;
            float total = 0f;
            for (int i = impulses.Count - 1; i >= 0; i--)
            {
                Impulse impulse = impulses[i];
                float t = (now - impulse.start) / impulse.duration;
                if (t >= 1f) { impulses.RemoveAt(i); continue; }

                float distance = Vector3.Distance(listener, impulse.position) / falloff;
                float envelope = (1f - t) * (1f - t);
                float amount = impulse.strength * envelope / (1f + distance * distance);
                total += amount;

                float phase = (now - impulse.start) * frequency + impulse.seed;
                positionOffset += new Vector3(Noise(phase, 0f), Noise(phase, 1f), Noise(phase, 2f) * 0.3f) * amount;
                rotationOffset += new Vector3(Noise(phase, 3f), Noise(phase, 4f), Noise(phase, 5f) * 0.5f) * amount;
            }

            // หลายแรงซ้อนกันห้ามสั่นจนจอเละ — เพดานเท่ากับแรงเต็ม 1.5 เท่า
            float scale = total > 1.5f ? 1.5f / total : 1f;
            positionOffset *= maxPos * scale;
            rotationOffset *= maxRot * scale;
        }

        private static float Noise(float x, float channel) => Mathf.PerlinNoise(x, channel * 7.31f) * 2f - 1f;
    }
}

using UnityEngine;

namespace Nsc.Feel
{
    /// <summary>
    /// ค่าจูน "ฟีลหุ่นยักษ์" ทั้งหมดในไฟล์เดียว — วางไว้ที่ Resources/FeelConfig ใช้ได้ทุกฉากโดยไม่ต้องผูกกับ prefab
    /// ปรับใน Inspector ระหว่างกด Play ได้เลย (ScriptableObject ค่าที่แก้ตอน Play จะค้างอยู่)
    /// </summary>
    [CreateAssetMenu(menuName = "NSC/Feel Config", fileName = "FeelConfig")]
    public class FeelConfig : ScriptableObject
    {
        private static FeelConfig instance;
        private static bool searched;

        public static FeelConfig Instance
        {
            get
            {
                if (!searched)
                {
                    searched = true;
                    instance = Resources.Load<FeelConfig>("FeelConfig");
                }
                return instance;
            }
        }

        [Header("กล้องสั่น (ค่าที่ strength = 1 ที่ระยะ 0)")]
        [Tooltip("เลื่อนกล้องสูงสุด (เมตร)")]
        public float shakePosition = 0.6f;
        [Tooltip("หมุนกล้องสูงสุด (องศา)")]
        public float shakeRotation = 1.4f;
        [Tooltip("ความถี่ของการสั่น (ครั้ง/วินาที)")]
        public float shakeFrequency = 22f;
        [Tooltip("ระยะที่ความแรงเหลือครึ่งหนึ่ง (เมตร) — หุ่นกว้าง ~35 ม. ต้องไกลพอให้ก้าวตัวเองยังสั่น")]
        public float shakeFalloffDistance = 45f;

        [Header("ก้าวเท้า (ตอนเท้าปักพื้น)")]
        public AudioClip[] footstepClips;
        [Range(0f, 1f)] public float footstepVolume = 0.9f;
        public Vector2 footstepPitch = new Vector2(0.62f, 0.74f);
        public float footstepShake = 0.45f;
        public float footstepShakeDuration = 0.32f;
        [Tooltip("ความเร็วเท้าลงพื้นที่นับเป็นก้าวเต็มแรง (m/s)")]
        public float footstepFullSpeed = 12f;
        [Tooltip("ก้าวเบาสุดยังได้แรงเท่านี้ — กันก้าวช้าแล้วเงียบ")]
        [Range(0f, 1f)] public float footstepMinStrength = 0.45f;
        public GameObject dustPrefab;
        public float dustScale = 6f;

        [Header("ปะทะ (หมัด เตะ ดาบ / บอสตีหุ่น)")]
        [Tooltip("ดาเมจตั้งแต่ค่านี้ = ปะทะหนัก")]
        public float heavyHitDamage = 30f;
        [Tooltip("เสียงหลักตอนปะทะ — ควรเป็นเหล็กกระแทก ไม่ใช่เสียงหมัดคน (ฟังเป็นการ์ตูน)")]
        public AudioClip[] lightHitClips;
        public AudioClip[] heavyHitClips;
        [Range(0f, 1f)] public float hitVolume = 1f;
        public Vector2 hitPitch = new Vector2(0.8f, 0.95f);
        [Tooltip("ปะทะหนักใช้ pitch ต่ำกว่า ให้เสียงทุ้มหนัก")]
        public Vector2 heavyHitPitch = new Vector2(0.55f, 0.68f);
        [Tooltip("เสียงทุ้มซ้อนใต้ปะทะหนัก (เช่นระเบิดลด pitch) — ว่าง = ไม่ซ้อน")]
        public AudioClip[] heavyHitBoomClips;
        [Range(0f, 1f)] public float heavyHitBoomVolume = 0.45f;
        public Vector2 heavyHitBoomPitch = new Vector2(0.5f, 0.6f);
        [Tooltip("กล้องค้างนิ่งก่อนสั่น (วินาที) — hit-stop ฝั่งกล้อง ไม่แตะ Time.timeScale")]
        public float lightHitFreeze = 0.045f;
        public float heavyHitFreeze = 0.085f;
        public float lightHitShake = 0.7f;
        public float heavyHitShake = 1.25f;
        public float hitShakeDuration = 0.4f;
        [Tooltip("ซูมเข้าแวบหนึ่ง (องศา FOV)")]
        public float lightFovKick = 1.5f;
        public float heavyFovKick = 4f;
        public Material sparkMaterial;
        public int lightSparkCount = 18;
        public int heavySparkCount = 40;
        public float sparkSpeed = 32f;
        public float sparkSize = 0.7f;

        [Header("ฟาดอาวุธ (เสียงวืดตอนเริ่มฟาดเร็ว)")]
        public AudioClip[] swingClips;
        [Range(0f, 1f)] public float swingVolume = 0.8f;
        [Tooltip("pitch ต่ำ = ของใหญ่ผ่าลม (วืดเสียงสูงฟังเป็นไม้เล็ก)")]
        public Vector2 swingPitch = new Vector2(0.5f, 0.65f);

        [Header("เสียง 3D")]
        [Tooltip("ระยะที่ยังดังเต็ม (เมตร) — ของยักษ์ต้องได้ยินจากไกล")]
        public float audioMinDistance = 30f;
        public float audioMaxDistance = 450f;
        [Range(0f, 1f)] public float audioSpatialBlend = 0.65f;
    }
}

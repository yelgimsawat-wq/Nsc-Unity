using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Nsc.BoneLimb
{
    /// <summary>
    /// พายุทราย: บรรยากาศครึ้ม (หมอก/แสง), ทรายปลิว, เสียงลม และ "ลม" ที่หุ่น IK อ่าน (RobotIKControl: เท้าไถล/ตัวเอน/รอยลาก)
    /// อีเวนต์ (bossEvent): พายุเริ่มตอนบอสเฟสสอง (เลือด ≤ bossPhaseHp) สงบเมื่อบอสตาย — อ่านเลือดบอสที่ซิงก์อยู่แล้ว ทุกเครื่องพร้อมกัน
    /// ทิศลมคงที่ ความแรงค่อยๆ ขึ้น/ลง (fadeTime) + ลมกระโชก | ปิด component / ติ๊ก active ออก = ไม่มีพายุ บรรยากาศกลับเป็นของฉากเดิม
    /// </summary>
    public class Sandstorm : MonoBehaviour
    {
        public static Sandstorm Current { get; private set; }

        [Header("ลม")]
        [Tooltip("ทิศที่ลมพัดไป (องศารอบแกนตั้ง, 0 = แกน +Z ของโลก)")]
        public float windYaw = 60f;
        [Tooltip("เท้าที่ปักอยู่ไถลตามลมกี่ m/s ตอนพายุเต็มแรง — ตัวหุ่นไหลตามเท้าเอง แม้ไม่ก้าว")]
        public float footSlide = 0.6f;
        [Tooltip("ตัวเอนสู้ลมกี่องศา")]
        public float lean = 4f;
        [Tooltip("ตัวโยกตามลมกระโชกกี่องศา — เดินสวนลมโยกมากขึ้นสูงสุด 2 เท่า")]
        public float sway = 2f;
        [Tooltip("ลมกระโชก: แรงลมขึ้นลงได้เท่านี้ (0 = ลมนิ่งคงที่)")]
        [Range(0f, 1f)] public float gust = 0.35f;
        [Tooltip("เปิด = พายุทั้งระบบ (ปิด = ไม่มีพายุเลย)")]
        public bool active = true;
        [Tooltip("พายุก่อตัว/สงบภายในกี่วินาที")]
        public float fadeTime = 6f;

        [Header("อีเวนต์: เริ่มตอนบอสเฟสสอง")]
        [Tooltip("เปิด = พายุเริ่มเมื่อบอสเข้าเฟสสอง แล้วสงบเมื่อบอสตาย | ปิด = มีพายุตลอด (ไว้ทดสอบ)")]
        public bool bossEvent = true;
        [Tooltip("บอสที่ดู — ว่าง = หาในฉากเอง (EnemyHealth ตัวแรกที่เจอ บอสเกิดทีหลังก็เจอ)")]
        public NscGame.Enemy.EnemyHealth boss;
        [Tooltip("เลือดบอสเหลือเท่านี้ (สัดส่วน) = เฟสสอง — แพนด้า: ตรงกับ enrageHpRatio ของ KaijuSpecials (คลั่ง)")]
        [Range(0f, 1f)] public float bossPhaseHp = 0.5f;

        [Header("บรรยากาศ")]
        public Color fogColor = new Color(0.6f, 0.47f, 0.32f);
        public float fogStart = 30f, fogEnd = 700f;
        [Tooltip("แสงอาทิตย์หรี่ลงกี่ส่วน")]
        [Range(0f, 1f)] public float lightDim = 0.3f;
        public Color ambient = new Color(0.5f, 0.41f, 0.3f);
        [Tooltip("ปรับสีภาพทั้งจอ (post-process) — shader การ์ตูนของฉากไม่รับหมอก ความครึ้มเลยมาจากตรงนี้เป็นหลัก")]
        public Color tint = new Color(1f, 0.82f, 0.6f);
        [Range(-100f, 0f)] public float desaturate = -35f;
        public float exposure = -0.3f;
        [Range(0f, 1f)] public float vignette = 0.35f;
        [Tooltip("หมอกฝุ่นทั้งจอ: ยกส่วนมืดขึ้นเป็นสีนี้ (แทนหมอกที่ shader ฉากไม่รับ)")]
        public Color haze = new Color(0.75f, 0.6f, 0.42f);
        [Tooltip("ยกส่วนมืดขึ้นกี่ส่วน — เกิน ~0.06 ภาพจะซีดแบนทั้งจอ")]
        [Range(0f, 0.2f)] public float hazeLift = 0.03f;
        [Tooltip("ลดคอนทราสต์ = ภาพฟุ้งเหมือนมองผ่านฝุ่น")]
        [Range(-100f, 0f)] public float hazeContrast = -12f;

        [Header("ทรายปลิว (prefab เอฟเฟกต์ — SandHaze / SandWisps / SandDrift)")]
        [Tooltip("ความเร็วลมที่พาทราย/ฝุ่น (m/s) — แต่ละชั้นคูณด้วย wind ของตัวเอง")]
        public float sandSpeed = 55f;
        [Tooltip("ทรายม้วนวนออกนอกแนวลม (× ความเร็วลม) — 0 = พุ่งเป็นเส้นตรงทั้งหมด")]
        [Range(0f, 1f)] public float swirl = 0.4f;
        [Tooltip("ทิศที่ทรายพัดส่ายไปมาตามลมกระโชกกี่องศา (ภาพอย่างเดียว ไม่เปลี่ยนแรงดันหุ่น)")]
        public float swirlYaw = 25f;
        [Tooltip("เอฟเฟกต์ที่พัดตามลม ตามกล้อง (เกิดฝั่งต้นลม) ความหนาแน่นตามความแรงพายุ")]
        public Effect[] effects;

        [System.Serializable]
        public struct Effect
        {
            public ParticleSystem prefab;
            [Tooltip("ขยายกี่เท่า (หุ่นสูง ~20 ม.)")]
            public float scale;
            [Tooltip("ความหนาแน่นคูณจากค่าเดิมของ prefab")]
            public float density;
            [Tooltip("ลมพาไปเร็วแค่ไหน (× sandSpeed) — กลุ่มฝุ่นช้า สายทรายเร็ว")]
            public float wind;
            [Tooltip("จุดเกิดอยู่ต้นลมจากกล้องกี่เมตร — ให้ของพัดผ่านกล้องพอดีในช่วงอายุ")]
            public float upwind;
        }

        [Header("เสียง")]
        public AudioClip windClip;
        [Range(0f, 1f)] public float volume = 0.75f;

        [Header("รอยลากเท้า")]
        public Material skidMaterial;

        /// <summary>ความแรงตอนนี้ 0..1 (ก่อตัว/สงบ × ลมกระโชก)</summary>
        public float Intensity { get; private set; }
        public Vector3 WindDir => Quaternion.Euler(0f, windYaw, 0f) * Vector3.forward;
        /// <summary>ทิศลม × ความแรง (แนวราบ)</summary>
        public Vector3 Wind => WindDir * Intensity;

        private float level;
        private ParticleSystem[] live;
        private float[] liveRate;
        private AudioSource audioSource;
        private Light sun;
        private float sunIntensity;
        private bool fog;
        private FogMode fogMode;
        private Color fogColor0, sky0, equator0, ground0;
        private float fogStart0, fogEnd0;
        private AmbientMode ambientMode0;
        private Volume grade;
        private VolumeProfile profile;

        private void OnEnable()
        {
            Current = this;
            fog = RenderSettings.fog; fogMode = RenderSettings.fogMode; fogColor0 = RenderSettings.fogColor;
            fogStart0 = RenderSettings.fogStartDistance; fogEnd0 = RenderSettings.fogEndDistance;
            ambientMode0 = RenderSettings.ambientMode;
            sky0 = RenderSettings.ambientSkyColor; equator0 = RenderSettings.ambientEquatorColor; ground0 = RenderSettings.ambientGroundColor;
            sun = RenderSettings.sun;
            if (!sun)
                foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { sun = l; break; }
            if (sun) sunIntensity = sun.intensity;
            if (!grade)
            {
                // Volume ของพายุซ้อนบน Global Volume ของฉาก น้ำหนัก = ความแรงพายุ (ค่อยๆ ครึ้ม/ค่อยๆ หาย)
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                ColorAdjustments color = profile.Add<ColorAdjustments>();
                color.colorFilter.Override(tint);
                color.saturation.Override(desaturate);
                color.postExposure.Override(exposure);
                color.contrast.Override(hazeContrast);
                LiftGammaGain lift = profile.Add<LiftGammaGain>();
                lift.lift.Override(new Vector4(haze.r, haze.g, haze.b, hazeLift));
                Vignette edge = profile.Add<Vignette>();
                edge.intensity.Override(vignette);
                edge.color.Override(new Color(0.25f, 0.17f, 0.08f));
                grade = gameObject.AddComponent<Volume>();
                grade.isGlobal = true;
                grade.priority = 10f;
                grade.sharedProfile = profile;
                grade.weight = 0f;
            }
            if (live == null) SpawnEffects();
            if (!audioSource && windClip)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.clip = windClip;
                audioSource.loop = true;
                audioSource.spatialBlend = 0f; // พายุอยู่รอบตัว ไม่มีต้นเสียง
                audioSource.volume = 0f;
                audioSource.Play();
            }
        }

        private void OnDisable()
        {
            if (Current == this) Current = null;
            RenderSettings.fog = fog; RenderSettings.fogMode = fogMode; RenderSettings.fogColor = fogColor0;
            RenderSettings.fogStartDistance = fogStart0; RenderSettings.fogEndDistance = fogEnd0;
            RenderSettings.ambientMode = ambientMode0;
            RenderSettings.ambientSkyColor = sky0; RenderSettings.ambientEquatorColor = equator0; RenderSettings.ambientGroundColor = ground0;
            if (sun) sun.intensity = sunIntensity;
            if (audioSource) audioSource.volume = 0f;
            if (grade) grade.weight = 0f;
            level = Intensity = 0f;
        }

        private void OnDestroy()
        {
            if (profile) Destroy(profile);
        }

        private void Update()
        {
            bool on = active && (!bossEvent || BossPhaseTwo());
            level = Mathf.MoveTowards(level, on ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, fadeTime));
            float g = Mathf.PerlinNoise(Time.time * 0.25f, 0.37f); // ลมกระโชกช้าๆ หลายวินาทีต่อลูก
            Intensity = level * Mathf.Lerp(1f - gust, 1f, g);

            RenderSettings.fog = fog || level > 0f;
            if (level > 0f) RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Color.Lerp(fogColor0, fogColor, level);
            RenderSettings.fogStartDistance = Mathf.Lerp(fog ? fogStart0 : fogEnd, fogStart, level);
            RenderSettings.fogEndDistance = Mathf.Lerp(fog ? fogEnd0 : fogEnd * 4f, fogEnd, level);
            if (level > 0f) RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(sky0, ambient, level);
            RenderSettings.ambientEquatorColor = Color.Lerp(equator0, ambient, level);
            RenderSettings.ambientGroundColor = Color.Lerp(ground0, ambient * 0.8f, level);
            if (sun) sun.intensity = sunIntensity * (1f - lightDim * level);
            if (grade) grade.weight = level;

            for (int i = 0; live != null && i < live.Length; i++)
                if (live[i])
                {
                    ParticleSystem.EmissionModule e = live[i].emission;
                    e.rateOverTimeMultiplier = liveRate[i] * Intensity;
                    SetFlow(live[i], effects[i]);
                }
            if (audioSource)
            {
                audioSource.volume = volume * Mathf.Lerp(0.6f, 1f, g) * level * GameAudio.GetBusVolume(AudioBus.Ambient);
                audioSource.pitch = Mathf.Lerp(0.85f, 1.05f, g);
            }
        }

        private void LateUpdate()
        {
            // เอฟเฟกต์เกิดคลุมจุดบนพื้นที่กล้องมองอยู่ (ไม่ใช่แค่รอบหุ่น) เยื้องไปฝั่งต้นลม แล้วพัดผ่าน
            // จำลองในโลก — ย้ายจุดเกิดตามกล้องไม่ลากของที่ปลิวอยู่ไปด้วย | ความสูง = ตัว Sandstorm (วางไว้ระดับพื้น)
            Camera cam = Camera.main;
            if (!cam || live == null) return;
            Transform c = cam.transform;
            float ground = transform.position.y;
            Vector3 look = c.position;
            if (c.forward.y < -0.05f) look += c.forward * Mathf.Min((ground - c.position.y) / c.forward.y, 60f);
            for (int i = 0; i < live.Length; i++)
            {
                if (!live[i]) continue;
                Vector3 p = look - WindDir * effects[i].upwind;
                p.y = ground;
                live[i].transform.position = p;
            }
        }

        // บอสเฟสสอง: อ่านเลือดบอส (NetworkVariable ทุกเครื่องเห็นค่าเดียวกัน) — พายุเริ่มพร้อมกันทุกเครื่องโดยไม่ต้องซิงก์เพิ่ม
        // สำคัญเพราะพายุดันหุ่นจริง (เท้าไถล) | เข้าเฟสสองแล้วค้างไว้ (เลือดเด้งขึ้นไม่ยกเลิก) จนบอสตาย = สงบ
        private bool phaseTwo;
        private float nextBossSearch;
        private bool BossPhaseTwo()
        {
            if (!boss && Time.time >= nextBossSearch)
            {
                boss = FindAnyObjectByType<NscGame.Enemy.EnemyHealth>(); // บอสเกิดผ่าน network ทีหลังฉาก — หาใหม่ทุกวินาที
                nextBossSearch = Time.time + 1f;
            }
            if (!boss) return false;
            float hp = boss.GetHpPercent();
            if (hp <= 0f) return false;
            if (hp <= bossPhaseHp) phaseTwo = true;
            return phaseTwo;
        }

        // เอฟเฟกต์จาก prefab: ขยายตามสเกลหุ่น หันไปทางลม จำลองในโลก + ลมพาไป | เริ่มที่ความหนาแน่น 0 (Update คุมตามความแรง)
        // ทิศที่ทรายพัด = ทิศลมส่ายช้าๆ ±swirlYaw (Perlin) ทุกเม็ดเลี้ยวตามพร้อมกันแบบลมเปลี่ยนทาง + ขึ้นลงเล็กน้อย
        private void SetFlow(ParticleSystem ps, Effect fx)
        {
            float yaw = (Mathf.PerlinNoise(Time.time * 0.12f, 5.3f) * 2f - 1f) * swirlYaw;
            Vector3 v = Quaternion.Euler(0f, yaw, 0f) * WindDir * (sandSpeed * fx.wind);
            ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
            vel.x = new ParticleSystem.MinMaxCurve(v.x * 0.8f, v.x * 1.2f);
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0.05f * v.magnitude); // ลอยขึ้นนิดๆ ไม่จมพื้น
            vel.z = new ParticleSystem.MinMaxCurve(v.z * 0.8f, v.z * 1.2f);
        }

        private void SpawnEffects()
        {
            live = new ParticleSystem[effects?.Length ?? 0];
            liveRate = new float[live.Length];
            for (int i = 0; i < live.Length; i++)
            {
                Effect fx = effects[i];
                if (!fx.prefab) continue;
                ParticleSystem ps = Instantiate(fx.prefab, transform);
                ps.transform.rotation = Quaternion.LookRotation(WindDir) * fx.prefab.transform.rotation;
                ps.transform.localScale = fx.prefab.transform.localScale * fx.scale;
                ParticleSystem.MainModule main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy; // ขนาดอนุภาค/พื้นที่เกิดขยายตามสเกล
                main.startSpeedMultiplier *= fx.scale;                  // จำลองในโลก ความเร็วไม่ขยายเอง
                main.maxParticles = Mathf.CeilToInt(main.maxParticles * Mathf.Max(1f, fx.density));
                ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                // ม้วนวน: noise ความถี่ต่ำ = แต่ละเม็ดเลี้ยวโค้งคนละทาง (เส้นยืดหันตามทิศที่เลี้ยว) ไม่พุ่งเป็นเส้นตรงเรียงกัน
                float speed = sandSpeed * fx.wind;
                ParticleSystem.NoiseModule noise = ps.noise;
                noise.enabled = swirl > 0f;
                noise.damping = false;
                noise.separateAxes = true;                          // วนแนวราบเป็นหลัก แนวตั้งนิดเดียว (ทรายเรี่ยพื้นไม่มุดดิน)
                noise.strengthX = speed * swirl;
                noise.strengthY = speed * swirl * 0.15f;
                noise.strengthZ = speed * swirl;
                noise.frequency = 0.04f;
                noise.scrollSpeed = 0.35f;
                noise.octaveCount = 1;
                noise.quality = ParticleSystemNoiseQuality.Medium;
                SetFlow(ps, fx);
                ParticleSystem.EmissionModule emission = ps.emission;
                liveRate[i] = emission.rateOverTimeMultiplier * fx.density;
                emission.rateOverTimeMultiplier = 0f;
                ps.Play();
                live[i] = ps;
            }
        }
    }
}

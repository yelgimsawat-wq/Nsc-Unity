using UnityEngine;

namespace Nsc.Feel
{
    /// <summary>
    /// เล่นเอฟเฟกต์ "ของหนัก" ในเครื่องนี้: เสียง ฝุ่น/ประกายไฟ กล้องสั่น — ไม่แตะเกมเพลย์หรือดาเมจเลย
    /// เรียกจาก RPC ที่ server ส่งมา (LimbController) ทุกเครื่องจึงเห็นเหมือนกัน
    /// </summary>
    public static class ImpactFeel
    {
        private static Texture2D softDot;

        /// <summary>เท้ายักษ์ปักพื้น — strength 0..1 ตามความเร็วตอนลง</summary>
        public static void PlayFootstep(Vector3 position, float strength)
        {
            FeelConfig config = FeelConfig.Instance;
            if (config == null || Application.isBatchMode) return;

            strength = Mathf.Clamp01(strength);
            CameraShake.Add(position, config.footstepShake * strength, config.footstepShakeDuration);
            PlayClip(config, config.footstepClips, position, config.footstepVolume * Mathf.Lerp(0.65f, 1f, strength), config.footstepPitch);
            SpawnDust(config, position, strength);
        }

        /// <summary>หมัด/เตะ/ดาบเข้าเป้า หรือบอสตีโดนหุ่น — surfaceNormal ชี้ออกจากผิวที่โดน (ทางที่ประกายไฟกระเด็น)</summary>
        public static void PlayHit(Vector3 position, Vector3 surfaceNormal, float damage)
        {
            FeelConfig config = FeelConfig.Instance;
            if (config == null || Application.isBatchMode) return;

            bool heavy = damage >= config.heavyHitDamage;
            CameraShake.Freeze(heavy ? config.heavyHitFreeze : config.lightHitFreeze);
            CameraShake.Add(position, heavy ? config.heavyHitShake : config.lightHitShake, config.hitShakeDuration);
            CameraShake.KickFov(heavy ? config.heavyFovKick : config.lightFovKick);

            AudioClip[] clips = heavy && config.heavyHitClips != null && config.heavyHitClips.Length > 0
                ? config.heavyHitClips : config.lightHitClips;
            PlayClip(config, clips, position, config.hitVolume, heavy ? config.heavyHitPitch : config.hitPitch);
            if (heavy) PlayClip(config, config.heavyHitBoomClips, position, config.heavyHitBoomVolume, config.heavyHitBoomPitch);
            SpawnSparks(config, position, surfaceNormal, heavy ? config.heavySparkCount : config.lightSparkCount, heavy);
        }

        /// <summary>อาวุธเริ่มฟาดเร็ว — strength 0..1 ตามความเร็วปลาย</summary>
        public static void PlaySwing(Vector3 position, float strength)
        {
            FeelConfig config = FeelConfig.Instance;
            if (config == null || Application.isBatchMode) return;
            PlayClip(config, config.swingClips, position, config.swingVolume * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(strength)), config.swingPitch);
        }

        // ================================================================

        private static void PlayClip(FeelConfig config, AudioClip[] clips, Vector3 position, float volume, Vector2 pitch)
        {
            if (clips == null || clips.Length == 0) return;
            AudioClip clip = clips[Random.Range(0, clips.Length)];
            if (clip == null) return;

            var go = new GameObject("FeelAudio");
            go.transform.position = position;
            AudioSource source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = volume * GameAudio.GetBusVolume(AudioBus.Sfx);
            source.pitch = Random.Range(pitch.x, pitch.y);
            source.spatialBlend = config.audioSpatialBlend;
            source.minDistance = config.audioMinDistance;
            source.maxDistance = config.audioMaxDistance;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.dopplerLevel = 0f;
            source.Play();
            Object.Destroy(go, clip.length / Mathf.Max(0.1f, source.pitch) + 0.1f);
        }

        private static void SpawnDust(FeelConfig config, Vector3 position, float strength)
        {
            if (config.dustPrefab == null) return;

            // สองก้อนซ้ายขวา ให้ดูเป็นฝุ่นกระจายรอบเท้า ไม่ใช่ควันจุดเดียว
            Vector3 side = Random.insideUnitSphere; side.y = 0f;
            side = side.sqrMagnitude > 0.001f ? side.normalized : Vector3.right;
            for (int i = 0; i < 2; i++)
            {
                Vector3 at = position + side * (i == 0 ? 2.5f : -2.5f);
                GameObject dust = Object.Instantiate(config.dustPrefab, at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                dust.transform.localScale = Vector3.one * config.dustScale * Mathf.Lerp(0.7f, 1f, strength);
                float life = 0f;
                foreach (ParticleSystem ps in dust.GetComponentsInChildren<ParticleSystem>())
                {
                    ParticleSystem.MainModule main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    main.loop = false;
                    life = Mathf.Max(life, main.duration + main.startLifetime.constantMax);
                    ps.Play();
                }
                Object.Destroy(dust, Mathf.Max(1f, life));
            }
        }

        private static void SpawnSparks(FeelConfig config, Vector3 position, Vector3 normal, int count, bool heavy)
        {
            if (count <= 0) return;
            if (normal.sqrMagnitude < 0.0001f) normal = Vector3.up;

            var go = new GameObject("HitSparks");
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(normal));
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, heavy ? 0.5f : 0.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(config.sparkSpeed * 0.35f, config.sparkSpeed * (heavy ? 1.3f : 1f));
            main.startSize = new ParticleSystem.MinMaxCurve(config.sparkSize * 0.4f, config.sparkSize);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.55f), new Color(1f, 0.5f, 0.12f));
            main.gravityModifier = 3f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count + 4;
            main.stopAction = ParticleSystemStopAction.Destroy;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = heavy ? 55f : 40f;
            shape.radius = 0.4f;

            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.35f, 0.05f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.05f;
            renderer.lengthScale = 1.2f;
            if (config.sparkMaterial != null) renderer.sharedMaterial = config.sparkMaterial;

            ps.Play();

            // แสงวาบที่จุดปะทะ — อนุภาคใหญ่ก้อนเดียว อายุสั้นมาก
            if (config.sparkMaterial != null)
            {
                var flashGo = new GameObject("HitFlash");
                flashGo.transform.position = position;
                ParticleSystem flash = flashGo.AddComponent<ParticleSystem>();
                flash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ParticleSystem.MainModule fm = flash.main;
                fm.duration = 0.1f;
                fm.loop = false;
                fm.startLifetime = 0.08f;
                fm.startSpeed = 0f;
                fm.startSize = config.sparkSize * (heavy ? 14f : 8f);
                fm.startColor = new Color(1f, 0.85f, 0.6f, 0.9f);
                fm.maxParticles = 1;
                fm.stopAction = ParticleSystemStopAction.Destroy;
                ParticleSystem.EmissionModule fe = flash.emission;
                fe.rateOverTime = 0f;
                fe.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
                ParticleSystem.ShapeModule fs = flash.shape;
                fs.enabled = false;
                flashGo.GetComponent<ParticleSystemRenderer>().sharedMaterial = config.sparkMaterial;
                flash.Play();
            }
        }
    }
}

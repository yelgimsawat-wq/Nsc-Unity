using Nsc.Robots;
using UnityEngine;

namespace NscGame.Enemy
{
    /// <summary>
    /// ของใช้ร่วมของสคริปต์แพนด้าไคจู (KaijuFeel / KaijuSpecials) — เดิมแต่ละสคริปต์มีสำเนาของตัวเอง
    /// หาพื้น, รูปเตือนบนพื้น, เสียงเริ่มกลางไฟล์ — เป็นภาพ/เสียงล้วน ไม่แตะเกมเพลย์หรือเน็ต
    /// </summary>
    public static class KaijuUtil
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        // ใช้ซ้ำทุกครั้งที่หาพื้น (RaycastAll สร้าง array ใหม่ทุกครั้ง — หนามวงหนึ่งเรียกหลายสิบที)
        private static readonly RaycastHit[] hits = new RaycastHit[32];

        public static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        public static float FlatDistance(Vector3 a, Vector3 b) => Flat(a - b).magnitude;

        /// <summary>ความสูงพื้นจริงใต้จุด — ข้ามตัวเอง (ไม่งั้นวงเตือนลอยบนหัว), ของที่มี Rigidbody และชิ้นหุ่น | ไม่เจอ = fallbackY</summary>
        public static float GroundY(Vector3 at, Transform self, float fallbackY)
        {
            int n = Physics.RaycastNonAlloc(at + Vector3.up * 30f, Vector3.down, hits, 80f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue, y = fallbackY;
            for (int i = 0; i < n; i++)
            {
                Collider c = hits[i].collider;
                if (hits[i].distance >= best || c.transform.IsChildOf(self) || c.attachedRigidbody != null || c.GetComponentInParent<Robot>() != null) continue;
                best = hits[i].distance;
                y = hits[i].point.y;
            }
            return y;
        }

        /// <summary>รูปเตือนบนพื้น (กล่อง/จานแบน) ไม่มี collider</summary>
        public static Transform MakeWarningShape(PrimitiveType type, Material material, Color color, string name = "SpecialWarning")
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>()); // ลบทันที — Destroy รอจบเฟรม ระหว่างนั้นการหาพื้นชนรูปตัวเองแล้วลอย
            SetupWarningRenderer(go.GetComponent<MeshRenderer>(), material, color);
            return go.transform;
        }

        /// <summary>สีผ่าน MaterialPropertyBlock (ไม่สร้าง material ใหม่ต่อชิ้น) ไม่ทำเงา</summary>
        public static void SetupWarningRenderer(MeshRenderer renderer, Material material, Color color)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor(BaseColor, color);
            renderer.SetPropertyBlock(mpb);
        }

        /// <summary>
        /// เล่นเสียงเริ่มกลางไฟล์ (ข้ามช่วงเงียบต้นไฟล์ ให้ตรงจังหวะ) ผ่านช่องเสียงแยกของเจ้าของ — PlayOneShot เริ่มกลางไฟล์ไม่ได้
        /// ช่องเสียงสร้างครั้งแรกที่ใช้ ใช้ 3D/2D ตาม AudioSource หลักของตัว
        /// </summary>
        public static void PlayFrom(GameObject owner, ref AudioSource source, AudioClip clip, float start, float volume = 1f, float pitch = 1f)
        {
            if (clip == null) return;
            if (source == null)
            {
                AudioSource main = owner.GetComponent<AudioSource>(); // ตัวแรก = ตัวหลักที่ตั้งไว้ใน prefab
                source = owner.AddComponent<AudioSource>();
                source.playOnAwake = false;
                if (main != null) source.spatialBlend = main.spatialBlend;
            }
            source.clip = clip;
            source.pitch = pitch;
            source.volume = volume;
            source.time = Mathf.Min(start, clip.length - 0.01f);
            source.Play();
        }
    }
}

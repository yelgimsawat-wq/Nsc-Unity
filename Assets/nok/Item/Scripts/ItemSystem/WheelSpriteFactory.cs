using UnityEngine;

namespace NscUnity.Items
{
    /// <summary>
    /// สร้าง Sprite ของวงล้อขึ้นมาเองตอนรันไทม์ (วงแหวน / วงกลมทึบ / ฉากมืดขอบจอ)
    /// ทำแบบนี้เพื่อให้ระบบวงล้อใช้งานได้ทันทีโดยไม่ต้องเตรียมไฟล์ภาพหรือ Prefab UI เลย
    /// </summary>
    internal static class WheelSpriteFactory
    {
        private const int TextureSize = 512;

        private static Sprite ring;
        private static Sprite disc;
        private static Sprite vignette;

        /// <summary>วงแหวน (โดนัท) ใช้เป็นชิ้นส่วนของวงล้อ คู่กับ Image type = Filled / Radial360</summary>
        public static Sprite Ring
        {
            get
            {
                if (ring == null) ring = CreateRing("WheelRing", TextureSize, 0.52f);
                return ring;
            }
        }

        /// <summary>วงกลมทึบ ใช้เป็นพื้นหลังไอคอนและวงกลางจอ</summary>
        public static Sprite Disc
        {
            get
            {
                if (disc == null) disc = CreateRing("WheelDisc", 256, 0f);
                return disc;
            }
        }

        /// <summary>ไล่สีมืดจากขอบจอเข้ากลาง ใช้หรี่ฉากตอนเปิดวงล้อ</summary>
        public static Sprite Vignette
        {
            get
            {
                if (vignette == null) vignette = CreateVignette("WheelVignette", 256);
                return vignette;
            }
        }

        private static Sprite CreateRing(string spriteName, int size, float innerRatio)
        {
            Texture2D texture = NewTexture(spriteName, size);

            float center = (size - 1) * 0.5f;
            float outer = center - 1f;
            float inner = outer * innerRatio;
            const float feather = 1.5f; // ความกว้างของขอบนุ่ม (กัน jagged)

            Color32[] pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                float dy = y - center;
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);

                    float outerFade = Mathf.Clamp01((outer - distance) / feather);
                    float innerFade = innerRatio <= 0f ? 1f : Mathf.Clamp01((distance - inner) / feather);

                    byte alpha = (byte)(Mathf.Clamp01(outerFade * innerFade) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            return Finish(texture, pixels, size);
        }

        private static Sprite CreateVignette(string spriteName, int size)
        {
            Texture2D texture = NewTexture(spriteName, size);

            float center = (size - 1) * 0.5f;
            float maxDistance = center;

            Color32[] pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                float dy = y - center;
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float t = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / maxDistance);

                    // กลางจอมืดน้อย ขอบจอมืดเต็ม
                    float alpha = Mathf.Lerp(0.45f, 1f, Mathf.SmoothStep(0f, 1f, t));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            return Finish(texture, pixels, size);
        }

        private static Texture2D NewTexture(string textureName, int size)
        {
            return new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = textureName,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private static Sprite Finish(Texture2D texture, Color32[] pixels, int size)
        {
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);

            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}

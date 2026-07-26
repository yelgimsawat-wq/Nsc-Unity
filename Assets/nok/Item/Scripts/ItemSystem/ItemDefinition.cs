using UnityEngine;

namespace NscUnity.Items
{
    /// <summary>
    /// ข้อมูลกลางของไอเทม 1 ชิ้น (ปืน, ดาบ, ระเบิด ฯลฯ)
    /// สร้างไฟล์ได้จากเมนู: Assets > Create > Nsc > Item Definition
    /// </summary>
    [CreateAssetMenu(fileName = "NewItem", menuName = "Nsc/Item Definition")]
    public class ItemDefinition : ScriptableObject
    {
        [Header("ข้อมูลแสดงผล")]
        public string displayName = "ไอเทมใหม่";

        [TextArea(2, 4)]
        public string description;

        [Tooltip("ไอคอนที่จะโชว์ในวงล้อตอนกด Tab (Texture Type ต้องเป็น Sprite)")]
        public Sprite icon;

        [Tooltip("สีประจำไอเทม ใช้เน้นช่องในวงล้อ")]
        public Color accentColor = new Color(1f, 0.85f, 0.45f);

        [Header("Prefab")]
        [Tooltip("โมเดลตอนถืออยู่ในมือ (แนะนำให้มีสคริปต์ที่สืบทอดจาก HeldItem แปะไว้)")]
        public GameObject heldPrefab;

        [Tooltip("โมเดลตอนวางอยู่บนพื้น ใช้ตอนโยนทิ้ง (ถ้าเว้นว่างจะโยนทิ้งไม่ได้)")]
        public GameObject worldPrefab;

        [Header("ตำแหน่งตอนถือในมือ (อิงกับ Hold Point)")]
        public Vector3 holdPositionOffset = Vector3.zero;
        public Vector3 holdRotationOffset = Vector3.zero;
        public Vector3 holdScale = Vector3.one;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
    }
}

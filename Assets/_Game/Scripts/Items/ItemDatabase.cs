using System.Collections.Generic;
using UnityEngine;

namespace NscUnity.Items
{
    /// <summary>
    /// รายชื่อไอเทมทั้งหมดในเกม เรียงเป็นลำดับคงที่ — ใช้แปลง ItemDefinition เป็นเลข index (และแปลงกลับ)
    /// จำเป็นสำหรับเล่นออนไลน์ เพราะ Netcode ส่ง ScriptableObject ข้ามเครือข่ายตรงๆ ไม่ได้
    /// ต้องส่งเป็นตัวเลขแทน แล้วให้แต่ละเครื่องเปิดไฟล์นี้ (ไฟล์เดียวกันทุกเครื่อง) มาแปลงกลับเป็นไอเทมจริง
    ///
    /// สำคัญ: ทุกเครื่อง (Host/Client) ต้องใช้ไฟล์ .asset เดียวกัน และห้ามสลับลำดับไอเทมในลิสต์
    /// ถ้าลำดับต่างกัน index จะชี้ผิดไอเทมไปคนละชิ้นระหว่างเครื่อง
    /// </summary>
    [CreateAssetMenu(fileName = "ItemDatabase", menuName = "Nsc/Item Database")]
    public class ItemDatabase : ScriptableObject
    {
        [Tooltip("ใส่ ItemDefinition ทุกชิ้นในเกมที่นี่ ห้ามลบ/สลับลำดับหลังจากเริ่มใช้งานจริงแล้ว จะทำให้เซฟเก่า/เครื่องอื่นชี้ผิดไอเทม เพิ่มไอเทมใหม่ให้ต่อท้ายลิสต์เท่านั้น")]
        [SerializeField] private List<ItemDefinition> items = new List<ItemDefinition>();

        private Dictionary<ItemDefinition, int> indexLookup;

        public int Count => items.Count;

        private void BuildLookupIfNeeded()
        {
            if (indexLookup != null) return;

            indexLookup = new Dictionary<ItemDefinition, int>();
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null) indexLookup[items[i]] = i;
            }
        }

        /// <summary>แปลง ItemDefinition เป็นเลข index สำหรับส่งผ่านเครือข่าย คืน -1 ถ้าไม่ได้อยู่ในฐานข้อมูล</summary>
        public int GetIndex(ItemDefinition item)
        {
            if (item == null) return -1;

            BuildLookupIfNeeded();
            return indexLookup.TryGetValue(item, out int index) ? index : -1;
        }

        /// <summary>แปลงเลข index (ที่ได้รับจากเครือข่าย) กลับเป็น ItemDefinition คืน null ถ้า index ไม่ถูกต้อง</summary>
        public ItemDefinition GetByIndex(int index)
        {
            return index >= 0 && index < items.Count ? items[index] : null;
        }
    }
}

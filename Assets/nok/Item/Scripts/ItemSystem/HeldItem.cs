using UnityEngine;

namespace NscUnity.Items
{
    /// <summary>
    /// คลาสฐานของ "ไอเทมที่กำลังถืออยู่ในมือ"
    /// แปะสคริปต์ที่สืบทอดจากคลาสนี้ไว้บน heldPrefab ของ ItemDefinition
    /// แล้ว override เมธอดที่ต้องการ (เช่น GunHeldItem override OnUseStart เพื่อยิง)
    /// </summary>
    public class HeldItem : MonoBehaviour
    {
        /// <summary>ข้อมูลไอเทมที่ spawn ตัวนี้ขึ้นมา</summary>
        public ItemDefinition Definition { get; private set; }

        /// <summary>มือที่กำลังถือไอเทมชิ้นนี้อยู่</summary>
        public HandItemHolder Holder { get; private set; }

        internal void Bind(ItemDefinition definition, HandItemHolder holder)
        {
            Definition = definition;
            Holder = holder;
        }

        /// <summary>เรียกทันทีหลังไอเทมถูก spawn เข้ามือ</summary>
        public virtual void OnEquipped() { }

        /// <summary>เรียกก่อนไอเทมถูกถอดออกจากมือ (ก่อนโดน Destroy)</summary>
        public virtual void OnUnequipped() { }

        /// <summary>กดปุ่มใช้งาน (ค่าเริ่มต้นคือคลิกซ้าย) เฟรมแรก</summary>
        public virtual void OnUseStart() { }

        /// <summary>กดปุ่มใช้งานค้างไว้ เรียกทุกเฟรม</summary>
        public virtual void OnUseHold(float deltaTime) { }

        /// <summary>ปล่อยปุ่มใช้งาน</summary>
        public virtual void OnUseEnd() { }
    }
}

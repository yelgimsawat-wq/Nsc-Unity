using UnityEngine;

namespace NscUnity.Items
{
    /// <summary>
    /// คลาสฐานของ "ไอเทมที่กำลังถืออยู่ในมือ"
    /// แปะสคริปต์ที่สืบทอดจากคลาสนี้ไว้บน heldPrefab ของ ItemDefinition แล้ว override เมธอดที่ต้องการ
    ///
    /// มีสองชั้น:
    ///   • OnUse* — ฝั่งเจ้าของเท่านั้น (input + เอฟเฟกต์ในเครื่อง เช่นลูกบอลชาร์จที่ปากกระบอก)
    ///   • ServerOnUse* — ฝั่ง server เท่านั้น (คิดผลจริง) ผ่านโปรโตคอลกลางของ HandItemHolder
    ///     ไอเทมไหนต้องการโปรโตคอลพิเศษ (ชาร์จ, ยิงต่อเนื่อง) เขียนในไอเทมเอง ไม่ต้องแก้ HandItemHolder
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

        /// <summary>เรียกทันทีหลังไอเทมถูก spawn เข้ามือ (ทุกเครื่อง)</summary>
        public virtual void OnEquipped() { }

        /// <summary>เรียกก่อนไอเทมถูกถอดออกจากมือ (ก่อนโดน Destroy)</summary>
        public virtual void OnUnequipped() { }

        /// <summary>[เจ้าของ] กดปุ่มใช้งานเฟรมแรก</summary>
        public virtual void OnUseStart() { }

        /// <summary>[เจ้าของ] กดปุ่มใช้งานค้างไว้ เรียกทุกเฟรม</summary>
        public virtual void OnUseHold(float deltaTime) { }

        /// <summary>[เจ้าของ] ปล่อยปุ่มใช้งาน</summary>
        public virtual void OnUseEnd() { }

        /// <summary>[เจ้าของ] การใช้งานถูกยกเลิกกลางคัน (เปิด UI ทับ / แมตช์ยังไม่เริ่มหรือจบแล้ว)</summary>
        public virtual void OnUseCancelled() { }

        /// <summary>[SERVER] เจ้าของเริ่มใช้งาน (ผ่าน HandItemHolder.RequestUseStart)</summary>
        public virtual void ServerOnUseStart() { }

        /// <summary>[SERVER] เจ้าของปล่อย — heldSeconds วัดบน server ไม่เชื่อค่าจาก client</summary>
        public virtual void ServerOnUseRelease(float heldSeconds) { }

        /// <summary>[ทุกเครื่อง] server ประกาศผลการยิงหนึ่งนัด — เล่นภาพ/เสียงให้ตรงกันทุกจอ</summary>
        public virtual void OnShot(FireData data) { }
    }
}

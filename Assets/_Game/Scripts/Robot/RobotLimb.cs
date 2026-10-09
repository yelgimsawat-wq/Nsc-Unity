using System;
using Nsc.Limbs;
using UnityEngine;

namespace Nsc.Robots
{
    /// <summary>
    /// แขนหรือขาหนึ่งชิ้นของหุ่น — วางบน GameObject ของมือ/เท้า (ที่เดียวกับตัวควบคุม)
    /// เป็นตัวกลางระหว่าง "เลือด" กับ "การต่ออยู่" ของชิ้นนี้ จะได้ไม่ต้องรู้จักกันเอง:
    ///   เลือดหมด → สั่งให้หลุด / ต่อกลับ → เติมเลือดเต็มตามเพดาน
    /// แล้วแจ้ง Robot ให้ลำตัวรู้ (ขาหลุด = ล้ม)
    ///
    /// ตัวควบคุมอ่าน limb.IsAttached แทนการเก็บสำเนาสถานะไว้เอง (เดิมมีสองแหล่งความจริง)
    /// </summary>
    [DisallowMultipleComponent]
    public class RobotLimb : MonoBehaviour
    {
        [SerializeField] private LimbSlot slot;

        private LimbAttachment attachment;
        private LimbHealth health;

        public LimbSlot Slot => slot;
        public Robot Owner { get; private set; }
        public LimbController Controller { get; private set; }
        public LimbAttachment Attachment => attachment;
        public LimbHealth Health => health;

        /// <summary>ชิ้นนี้ต่ออยู่กับตัวไหม — ไม่มีระบบหลุด (prefab ฐานที่ไม่มี LimbAttachment) = ต่ออยู่เสมอ</summary>
        public bool IsAttached => attachment == null || attachment.IsAttached;

        /// <summary>ยิงบนทุกเครื่องเมื่อชิ้นนี้หลุดหรือต่อกลับ</summary>
        public event Action<RobotLimb> StateChanged;

        private void Awake()
        {
            Controller = GetComponent<LimbController>();
            attachment = GetComponent<LimbAttachment>();
            health = GetComponent<LimbHealth>();

            if (health != null) health.Depleted += OnHealthDepleted;
            if (attachment != null) attachment.AttachmentChanged += OnAttachmentChanged;
        }

        private void OnDestroy()
        {
            if (health != null) health.Depleted -= OnHealthDepleted;
            if (attachment != null) attachment.AttachmentChanged -= OnAttachmentChanged;
        }

        /// <summary>Robot เรียกตอน Awake — ชิ้นส่วนรู้ว่าตัวเองเป็นของหุ่นตัวไหน</summary>
        internal void BindOwner(Robot robot) => Owner = robot;

        /// <summary>[SERVER] เลือดหมด → ชิ้นหลุด</summary>
        private void OnHealthDepleted()
        {
            if (attachment != null) attachment.ServerDetach();
        }

        private void OnAttachmentChanged(bool attached)
        {
            // ✅ ต่อชิ้นกลับ (กด R ดึงคืน) → เลือดเต็ม "ตามเพดานปัจจุบัน" ซึ่งลดลงทุกครั้งที่หลุด
            if (attached && health != null) health.ServerRefill();

            StateChanged?.Invoke(this);
            if (Owner != null) Owner.OnLimbStateChanged(this);
        }
    }
}

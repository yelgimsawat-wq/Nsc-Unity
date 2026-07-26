using Unity.Netcode;
using UnityEngine;

namespace NscUnity.Items
{
    /// <summary>
    /// ข้อมูลนัดยิง 1 ครั้ง ส่งผ่านเครือข่ายจากเจ้าของไปให้ทุกเครื่อง (รวมตัวเอง) — ดูที่ HandItemHolder.FireRpc
    /// ค่าถูกคำนวณจบแล้วที่ฝั่งเจ้าของก่อนส่ง (เชื่อ client เรื่องเลข ไม่ validate ซ้ำฝั่ง Server —
    /// พอสำหรับเกมนี้ที่ยังไม่มีระบบกันโกง เหมือนที่ PvpDamageSender ก็เชื่อค่าจาก client อยู่แล้ว)
    /// </summary>
    public struct FireData : INetworkSerializable
    {
        public Vector3 origin;
        public Vector3 direction;
        public float speed;
        public float damage;
        public float knockback;
        public float maxDistance;
        public float hitRadius;
        public float visualSize;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref origin);
            serializer.SerializeValue(ref direction);
            serializer.SerializeValue(ref speed);
            serializer.SerializeValue(ref damage);
            serializer.SerializeValue(ref knockback);
            serializer.SerializeValue(ref maxDistance);
            serializer.SerializeValue(ref hitRadius);
            serializer.SerializeValue(ref visualSize);
        }
    }

    /// <summary>
    /// "มือ" ของตัวละคร — หน้าที่หลักคือ spawn / ทำลาย โมเดลไอเทมที่จุด Hold Point
    /// และส่งต่อ input ปุ่มใช้งานไปให้ HeldItem ที่ถืออยู่
    /// แปะไว้ที่ GameObject ของผู้เล่น แล้วลากกระดูกมือ (หรือ Empty ที่เป็นลูกของกระดูกมือ) มาใส่ holdPoint
    ///
    /// ออนไลน์: Hold() ทำงานบนทุกเครื่อง (เพื่อให้ทุกคนเห็นว่าผู้เล่นคนนี้ถืออะไรอยู่ — ขับเคลื่อนโดย
    /// PlayerInventory.EquippedIndex ที่ซิงค์อยู่แล้ว) ส่วนการรับ Input (คลิกใช้ไอเทม) อ่านเฉพาะฝั่งเจ้าของ
    /// (IsOwner) เท่านั้น แล้วส่งผลการยิงไปให้ทุกเครื่องผ่าน FireRpc เพื่อให้เห็นเอฟเฟกต์ตรงกัน
    /// </summary>
    public class HandItemHolder : NetworkBehaviour
    {
        [Header("จุดยึดไอเทม")]
        [Tooltip("Empty GameObject ที่เป็นลูกของกระดูกมือ — ไอเทมจะถูก spawn เป็นลูกของจุดนี้")]
        [SerializeField] private Transform holdPoint;

        [Header("ปุ่มใช้งานไอเทม")]
        [SerializeField] private bool handleUseInput = true;
        [Tooltip("0 = คลิกซ้าย, 1 = คลิกขวา, 2 = คลิกกลาง\n" +
                 "โปรเจกต์นี้คลิกซ้ายค้าง = เดิน (PlayerFootForRobot) และคลิกขวาค้าง = หมุนกล้อง " +
                 "ทั้งสองปุ่มชนแน่ๆ ห้ามใช้ — ค่าเริ่มต้นเลยเป็นคลิกกลาง (2)")]
        [SerializeField] private int useMouseButton = 2;

        [Header("สคริปต์ที่ต้องปิดตอนถือไอเทม")]
        [Tooltip("ปกติปล่อยว่างไว้ได้ — โปรเจกต์นี้ต่อยด้วย PlayerHandCombat (Shift) ซึ่งไม่ชนกับการถือไอเทม " +
                 "(ไอเทมแค่เกาะเป็นลูกของมือ ไม่กันการต่อย) ใส่เฉพาะถ้ามีสคริปต์อื่นที่อยากปิดตอนมือไม่ว่าง")]
        [SerializeField] private Behaviour[] disableWhileHolding;

        /// <summary>ไอเทมที่ถืออยู่ตอนนี้ (null = มือเปล่า) — เป็นอินสแตนซ์ local ของเครื่องนี้เอง</summary>
        public HeldItem Current { get; private set; }

        /// <summary>ข้อมูลไอเทมที่ถืออยู่ตอนนี้ (null = มือเปล่า)</summary>
        public ItemDefinition CurrentDefinition { get; private set; }

        public bool IsHoldingSomething => CurrentDefinition != null;

        public Transform HoldPoint => holdPoint;

        private void Awake()
        {
            if (holdPoint == null)
            {
                Debug.LogWarning($"[HandItemHolder] ยังไม่ได้ลาก Hold Point มาใส่บน '{name}' — ไอเทมจะไม่โผล่ในมือ", this);
            }

            ApplyDisabledBehaviours();
        }

        /// <summary>สั่งให้ถือไอเทมชิ้นใหม่ (ส่ง null = ปล่อยมือเปล่า) — ทำงานบนทุกเครื่อง ไม่ใช่แค่เจ้าของ</summary>
        public void Hold(ItemDefinition definition)
        {
            ClearCurrent();

            CurrentDefinition = definition;

            if (definition != null && holdPoint != null)
            {
                if (definition.heldPrefab != null)
                {
                    GameObject instance = Instantiate(definition.heldPrefab, holdPoint);
                    instance.transform.localPosition = definition.holdPositionOffset;
                    instance.transform.localRotation = Quaternion.Euler(definition.holdRotationOffset);
                    instance.transform.localScale = definition.holdScale;

                    HeldItem held = instance.GetComponent<HeldItem>();
                    if (held == null) held = instance.AddComponent<HeldItem>();

                    held.Bind(definition, this);
                    Current = held;
                    held.OnEquipped();
                }
                else
                {
                    Debug.LogWarning($"[HandItemHolder] ไอเทม '{definition.DisplayName}' ยังไม่ได้ใส่ Held Prefab", this);
                }
            }

            ApplyDisabledBehaviours();
        }

        private void ClearCurrent()
        {
            if (Current != null)
            {
                Current.OnUnequipped();
                Destroy(Current.gameObject);
                Current = null;
            }
            else if (holdPoint != null)
            {
                // เผื่อกรณี prefab ไม่มี HeldItem แต่ถูก spawn ค้างไว้
                for (int i = holdPoint.childCount - 1; i >= 0; i--)
                {
                    Destroy(holdPoint.GetChild(i).gameObject);
                }
            }

            CurrentDefinition = null;
        }

        private void ApplyDisabledBehaviours()
        {
            if (disableWhileHolding == null) return;

            // สคริปต์อื่นในเครื่องเดียวกันเท่านั้นที่ควรถูกปิด/เปิดตาม — ผู้เล่นระยะไกลไม่ควรมีสคริปต์เหล่านี้ทำงานอยู่แล้ว
            if (!IsOwner) return;

            bool holding = IsHoldingSomething;
            foreach (Behaviour behaviour in disableWhileHolding)
            {
                if (behaviour != null) behaviour.enabled = !holding;
            }
        }

        private void Update()
        {
            // อ่าน Input จริงเฉพาะฝั่งเจ้าของเท่านั้น — เครื่องอื่นเห็นแค่ผลลัพธ์ผ่านสถานะที่ซิงค์มา ไม่ประมวลผล Input ของคนอื่น
            if (!IsOwner) return;
            if (!handleUseInput || Current == null) return;

            // ตอนวงล้อเลือกไอเทมเปิดอยู่ ห้ามยิง/ใช้ไอเทม
            if (ItemWheelUI.IsAnyOpen) return;

            if (InputCompat.GetMouseButtonDown(useMouseButton)) Current.OnUseStart();
            if (InputCompat.GetMouseButton(useMouseButton)) Current.OnUseHold(Time.deltaTime);
            if (InputCompat.GetMouseButtonUp(useMouseButton)) Current.OnUseEnd();
        }

        // ==========================================================
        // ยิงของ — ให้ทุกเครื่องเห็นผลตรงกัน (ดู ChargeGunHeldItem.OnUseEnd/OnUseHold ที่เรียกเมธอดนี้)
        // ==========================================================

        /// <summary>
        /// ขอยิงออกไป — เรียกจากฝั่งเจ้าของเท่านั้น (HeldItem คำนวณค่าทั้งหมดเสร็จแล้วก่อนเรียก)
        /// ส่งต่อไปให้ทุกเครื่องรวมตัวเองผ่าน Rpc เพื่อให้เห็นกระสุน/เอฟเฟกต์ตรงกัน
        /// ดาเมจจริงจะถูกคิดเฉพาะฝั่ง Server เท่านั้น (ดู Projectile.ApplyDamage ที่เช็ค IsServer เองอยู่แล้ว)
        /// </summary>
        public void RequestFire(FireData data)
        {
            if (!IsOwner) return;
            FireRpc(data);
        }

        [Rpc(SendTo.Everyone)]
        private void FireRpc(FireData data)
        {
            (Current as ChargeGunHeldItem)?.ExecuteFire(data);
        }

        private void OnDrawGizmosSelected()
        {
            if (holdPoint == null) return;

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(holdPoint.position, 0.06f);
            Gizmos.DrawRay(holdPoint.position, holdPoint.forward * 0.3f);
        }
    }
}

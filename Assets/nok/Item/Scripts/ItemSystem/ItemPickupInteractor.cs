using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace NscUnity.Items
{
    /// <summary>
    /// ตัวตรวจจับไอเทมรอบตัวผู้เล่น + รับปุ่ม E เพื่อเก็บของ (F ถูก PlayerHandMovement จองไว้ทำ "จับ/ปล่อยวัตถุทางฟิสิกส์" อยู่แล้ว)
    /// แปะไว้ที่ GameObject ของผู้เล่น (ตัวเดียวกับ PlayerInventory)
    ///
    /// ออนไลน์: ตรวจจับ/แสดงป้าย "กด E" เป็น local prediction ล้วนๆ (ไม่กระทบผลจริง) ทำงานเฉพาะฝั่งเจ้าของ
    /// ส่วนการเก็บของจริงต้องยิง Rpc ไปให้ Server ตัดสิน — กันไม่ให้สองคนแย่งเก็บของชิ้นเดียวกันพร้อมกันได้
    /// </summary>
    public class ItemPickupInteractor : NetworkBehaviour
    {
        [Header("การเชื่อมต่อ")]
        [SerializeField] private PlayerInventory inventory;

        [Tooltip("จุดศูนย์กลางของการตรวจจับ (ถ้าเว้นว่างจะใช้ตัวผู้เล่นเอง)")]
        [SerializeField] private Transform detectionOrigin;

        [Header("ระยะตรวจจับ")]
        [SerializeField] private float pickupRange = 2.5f;

        [Tooltip("มุมสูงสุดจากทิศที่ผู้เล่นหันหน้า (180 = เก็บได้รอบตัว)")]
        [SerializeField, Range(15f, 180f)] private float maxPickupAngle = 180f;

        [Header("ปุ่ม")]
        [Tooltip("ใช้ E เพราะ F ถูก PlayerHandMovement จองไว้ทำ \"จับ/ปล่อยวัตถุทางฟิสิกส์\" อยู่แล้ว")]
        [SerializeField] private KeyCode pickupKey = KeyCode.E;
        [SerializeField] private KeyCode dropKey = KeyCode.G;

        [Header("พฤติกรรม")]
        [Tooltip("เก็บของแล้วหยิบขึ้นมือเลย")]
        [SerializeField] private bool equipOnPickup = true;

        [Header("Debug")]
        [Tooltip("พิมพ์ log ทุกขั้นตอนตอนกดเก็บของ — เปิดไว้ตอนหาสาเหตุว่าทำไมเก็บไม่ได้")]
        [SerializeField] private bool debugLog = true;

        /// <summary>ปุ่มเก็บของจริงตอนนี้ — ให้ PickupPromptUI อ่านค่านี้แทนการเขียนตัวอักษรตายตัว จะได้ไม่เพี้ยนเวลาเปลี่ยนปุ่ม</summary>
        public KeyCode PickupKey => pickupKey;

        /// <summary>ไอเทมที่ใกล้ที่สุดตอนนี้ (null = ไม่มีอะไรให้เก็บ) ใช้ผูกกับ UI ป้ายบอก "กด E"</summary>
        public WorldItem Focused { get; private set; }

        /// <summary>ยิงเมื่อไอเทมเป้าหมายเปลี่ยน — เอาไปโชว์/ซ่อนป้าย "[E] เก็บ ..."</summary>
        public event Action<WorldItem> OnFocusChanged;

        private Transform Origin => detectionOrigin != null ? detectionOrigin : transform;

        private void Awake()
        {
            // ปกติทั้งชุด (กระเป๋า/มือ/ตัวเก็บของ) อยู่บนแขนข้างเดียวกันหมด เพราะผู้เล่น 1 คน = แขน 1 ข้าง
            // เผื่อไว้หา parent ด้วย เผื่อใครย้ายไปวางโครงสร้างอื่น
            if (inventory == null) inventory = GetComponent<PlayerInventory>();
            if (inventory == null) inventory = GetComponentInParent<PlayerInventory>();

            if (inventory == null)
            {
                Debug.LogError("[ItemPickupInteractor] หา PlayerInventory ไม่เจอ (เช็คทั้งตัวเองและ parent แล้ว) — เก็บของไม่ได้", this);
                enabled = false;
            }
        }

        private void Update()
        {
            // ตรวจจับ/รับปุ่มเฉพาะฝั่งเจ้าของ — ผู้เล่นระยะไกลไม่ควรมีใครมากดปุ่มแทนเราได้
            if (!IsOwner) return;

            // ตอนวงล้อเลือกไอเทมเปิดอยู่ ไม่ต้องรับปุ่มเก็บ/ทิ้งของ
            if (ItemWheelUI.IsAnyOpen) return;

            RefreshFocus();

            if (Focused != null && InputCompat.GetKeyDown(pickupKey))
            {
                RequestPickup(Focused);
            }

            if (InputCompat.GetKeyDown(dropKey))
            {
                Transform origin = Origin;
                inventory.RequestDrop(origin.position + origin.forward * 1f + Vector3.up * 0.5f, origin.forward);
            }
        }

        /// <summary>หาไอเทมที่ "น่าเก็บที่สุด" รอบตัว — ใกล้ที่สุดและอยู่ในมุมที่หันหน้าไป (local prediction ไว้โชว์ป้ายเฉยๆ)</summary>
        private void RefreshFocus()
        {
            Transform origin = Origin;
            Vector3 originPosition = origin.position;

            WorldItem best = null;
            float bestSqrDistance = float.MaxValue;
            float sqrRange = pickupRange * pickupRange;
            float cosLimit = Mathf.Cos(maxPickupAngle * Mathf.Deg2Rad);

            // วนจากลิสต์ไอเทมที่ลงทะเบียนไว้ (มีไม่กี่ชิ้น) แทนการยิง Physics.OverlapSphere ทุกเฟรม
            // — ในแมพใหญ่ๆ การยิง physics แล้ว GetComponentInParent กับ collider ทุกชิ้นทำให้เกมหน่วงหนักมาก
            List<WorldItem> items = WorldItem.Active;
            for (int i = 0; i < items.Count; i++)
            {
                WorldItem item = items[i];
                if (item == null || item.Definition == null) continue;

                Vector3 toItem = item.transform.position - originPosition;
                float sqrDistance = toItem.sqrMagnitude;
                if (sqrDistance > sqrRange || sqrDistance >= bestSqrDistance) continue;

                if (maxPickupAngle < 180f && sqrDistance > 0.0001f)
                {
                    Vector3 flat = new Vector3(toItem.x, 0f, toItem.z).normalized;
                    if (Vector3.Dot(origin.forward, flat) < cosLimit) continue;
                }

                bestSqrDistance = sqrDistance;
                best = item;
            }

            if (best != Focused)
            {
                Focused = best;
                OnFocusChanged?.Invoke(best);
            }
        }

        /// <summary>ขอเก็บไอเทมชิ้นนี้ — ยิงไปให้ Server ตัดสิน (กันสองคนแย่งเก็บของชิ้นเดียวกันพร้อมกัน)</summary>
        private void RequestPickup(WorldItem item)
        {
            if (item == null) return;

            if (!item.TryGetComponent(out NetworkObject netObj))
            {
                Debug.LogError($"[ItemPickup] '{item.name}' ไม่มี NetworkObject — เก็บออนไลน์ไม่ได้ ต้องเพิ่ม NetworkObject ใน World Prefab ก่อน", item);
                return;
            }

            if (!netObj.IsSpawned)
            {
                Debug.LogError($"[ItemPickup] '{item.name}' มี NetworkObject แต่ยังไม่ถูก Spawn — " +
                               "ของที่วางไว้ในฉากตั้งแต่แรกต้องให้ Server สั่ง Spawn ก่อนถึงจะเก็บได้ " +
                               "(ใช้ Tools ▸ NSC ▸ Item System ▸ Validate Setup ดูรายละเอียด)", item);
                return;
            }

            if (debugLog) Debug.Log($"[ItemPickup] ส่งคำขอเก็บ '{item.PromptText}' ไปที่ Server...", this);

            PickupRpc(new NetworkObjectReference(netObj));
        }

        [Rpc(SendTo.Server)]
        private void PickupRpc(NetworkObjectReference itemRef)
        {
            // ระหว่างที่ Rpc นี้เดินทางมา อาจมีอีกคนเก็บของชิ้นนี้ไปก่อนแล้ว (Despawn ไปแล้ว) — TryGet จะคืน false ให้เอง
            if (!itemRef.TryGet(out NetworkObject netObj) || netObj == null)
            {
                if (debugLog) Debug.Log("[ItemPickup] ของชิ้นนี้ถูกคนอื่นเก็บไปก่อนแล้ว", this);
                return;
            }

            WorldItem item = netObj.GetComponent<WorldItem>();
            if (item == null || item.Definition == null)
            {
                Debug.LogWarning($"[ItemPickup] '{netObj.name}' ไม่มี WorldItem หรือยังไม่ได้ใส่ Item Definition — เก็บไม่ได้", netObj);
                return;
            }

            if (!inventory.TryAddServerSide(item.Definition, out int index))
            {
                // TryAddServerSide จะ log error เองถ้าไอเทมไม่ได้อยู่ใน Database — เหลือแค่เคสกระเป๋าเต็ม
                Debug.LogWarning($"[ItemPickup] เก็บ '{item.Definition.DisplayName}' ไม่สำเร็จ — กระเป๋าเต็มหรือไอเทมไม่ได้อยู่ใน Item Database", this);
                return;
            }

            if (debugLog) Debug.Log($"[ItemPickup] ✅ เก็บ '{item.Definition.DisplayName}' ลงช่อง {index} เรียบร้อย", this);

            if (equipOnPickup) inventory.RequestEquip(index);

            item.ConsumeFromWorld(); // เล่นเอฟเฟกต์ตอนเก็บ (ถ้ามี) แล้ว Despawn ให้ทุกเครื่องเห็นของหายพร้อมกัน
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.35f);
            Gizmos.DrawWireSphere(Origin.position, pickupRange);
        }
    }
}

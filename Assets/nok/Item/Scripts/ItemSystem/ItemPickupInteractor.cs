using System;
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

        [Tooltip("Layer ของไอเทมในฉาก แนะนำให้สร้าง Layer ชื่อ 'Item' แล้วเลือกเฉพาะอันนั้น")]
        [SerializeField] private LayerMask itemLayers = ~0;

        [Tooltip("มุมสูงสุดจากทิศที่ผู้เล่นหันหน้า (180 = เก็บได้รอบตัว)")]
        [SerializeField, Range(15f, 180f)] private float maxPickupAngle = 180f;

        [Header("ปุ่ม")]
        [Tooltip("ใช้ E เพราะ F ถูก PlayerHandMovement จองไว้ทำ \"จับ/ปล่อยวัตถุทางฟิสิกส์\" อยู่แล้ว")]
        [SerializeField] private KeyCode pickupKey = KeyCode.E;
        [SerializeField] private KeyCode dropKey = KeyCode.G;

        [Header("พฤติกรรม")]
        [Tooltip("เก็บของแล้วหยิบขึ้นมือเลย")]
        [SerializeField] private bool equipOnPickup = true;

        /// <summary>ปุ่มเก็บของจริงตอนนี้ — ให้ PickupPromptUI อ่านค่านี้แทนการเขียนตัวอักษรตายตัว จะได้ไม่เพี้ยนเวลาเปลี่ยนปุ่ม</summary>
        public KeyCode PickupKey => pickupKey;

        /// <summary>ไอเทมที่ใกล้ที่สุดตอนนี้ (null = ไม่มีอะไรให้เก็บ) ใช้ผูกกับ UI ป้ายบอก "กด E"</summary>
        public WorldItem Focused { get; private set; }

        /// <summary>ยิงเมื่อไอเทมเป้าหมายเปลี่ยน — เอาไปโชว์/ซ่อนป้าย "[E] เก็บ ..."</summary>
        public event Action<WorldItem> OnFocusChanged;

        private static readonly Collider[] OverlapBuffer = new Collider[32];

        private Transform Origin => detectionOrigin != null ? detectionOrigin : transform;

        private void Awake()
        {
            if (inventory == null) inventory = GetComponent<PlayerInventory>();

            if (inventory == null)
            {
                Debug.LogError("[ItemPickupInteractor] หา PlayerInventory ไม่เจอ — เก็บของไม่ได้", this);
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
            int count = Physics.OverlapSphereNonAlloc(origin.position, pickupRange, OverlapBuffer, itemLayers, QueryTriggerInteraction.Collide);

            WorldItem best = null;
            float bestDistance = float.MaxValue;
            float cosLimit = Mathf.Cos(maxPickupAngle * Mathf.Deg2Rad);

            for (int i = 0; i < count; i++)
            {
                Collider hit = OverlapBuffer[i];
                if (hit == null) continue;

                WorldItem item = hit.GetComponentInParent<WorldItem>();
                if (item == null || item.Definition == null) continue;

                Vector3 toItem = item.transform.position - origin.position;
                float distance = toItem.magnitude;
                if (distance > pickupRange) continue;

                if (maxPickupAngle < 180f && distance > 0.01f)
                {
                    Vector3 flat = new Vector3(toItem.x, 0f, toItem.z).normalized;
                    if (Vector3.Dot(origin.forward, flat) < cosLimit) continue;
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = item;
                }
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
                Debug.LogError($"[ItemPickupInteractor] '{item.name}' ไม่มี NetworkObject — เก็บออนไลน์ไม่ได้ ต้องเพิ่ม NetworkObject ใน World Prefab ก่อน", item);
                return;
            }

            PickupRpc(new NetworkObjectReference(netObj));
        }

        [Rpc(SendTo.Server)]
        private void PickupRpc(NetworkObjectReference itemRef)
        {
            // ระหว่างที่ Rpc นี้เดินทางมา อาจมีอีกคนเก็บของชิ้นนี้ไปก่อนแล้ว (Despawn ไปแล้ว) — TryGet จะคืน false ให้เอง
            if (!itemRef.TryGet(out NetworkObject netObj) || netObj == null) return;

            WorldItem item = netObj.GetComponent<WorldItem>();
            if (item == null || item.Definition == null) return;

            if (!inventory.TryAddServerSide(item.Definition, out int index))
            {
                return; // กระเป๋าเต็ม — เงียบไว้ก่อน (เพิ่ม feedback ให้ผู้เล่นทีหลังได้ถ้าต้องการ)
            }

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

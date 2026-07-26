using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace NscUnity.Items
{
    /// <summary>
    /// กระเป๋าไอเทมของผู้เล่น แบบซิงค์ผ่านเครือข่าย — ทุกเครื่องเห็นตรงกันว่าใครถืออะไรอยู่ในช่องไหน
    /// เป็น "แหล่งความจริงเดียว" ของระบบ ส่วน HandItemHolder กับ ItemWheelUI แค่ฟัง event จากที่นี่
    ///
    /// สถานะช่องเก็บของ/ช่องที่ถืออยู่เก็บเป็น NetworkList/NetworkVariable ที่เขียนได้เฉพาะ Server เท่านั้น
    /// ฝั่ง Client เรียกได้แค่เมธอด Request* ซึ่งจะยิง Rpc ไปให้ Server เป็นคนตัดสินและแก้สถานะจริง
    /// ต้องแปะอยู่บน GameObject ที่มี NetworkObject (ปกติคือตัวราก Player Prefab)
    /// </summary>
    public class PlayerInventory : NetworkBehaviour
    {
        [Header("ช่องเก็บของ")]
        [Tooltip("จำนวนช่องในวงล้อ (แนะนำ 4-8 ช่อง)")]
        [SerializeField, Range(2, 12)] private int slotCount = 6;

        [Tooltip("ไอเทมที่มีติดตัวตั้งแต่เริ่มเกม")]
        [SerializeField] private List<ItemDefinition> startingItems = new List<ItemDefinition>();

        [Header("ฐานข้อมูลไอเทม")]
        [Tooltip("ต้องเป็นไฟล์ .asset เดียวกันทุกเครื่อง (Host/Client) ไม่งั้นซิงค์ผิดไอเทม")]
        [SerializeField] private ItemDatabase itemDatabase;

        [Header("การเชื่อมต่อ")]
        [SerializeField] private HandItemHolder hand;

        [Tooltip("เริ่มเกมแล้วหยิบไอเทมช่องแรกขึ้นมือเลยหรือไม่")]
        [SerializeField] private bool equipFirstItemOnStart = true;

        // -1 = ช่องว่าง เก็บเป็น index เข้า ItemDatabase แทนการอ้าง ScriptableObject ตรงๆ (ส่งผ่านเครือข่ายไม่ได้)
        private readonly NetworkList<int> slotItemIndices = new NetworkList<int>();

        private readonly NetworkVariable<int> equippedIndex = new NetworkVariable<int>(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>ช่องที่กำลังถืออยู่ (-1 = มือเปล่า) — ค่าเดียวกันทุกเครื่อง</summary>
        public int EquippedIndex => equippedIndex.Value;

        public int SlotCount => slotItemIndices.Count;

        public ItemDefinition EquippedItem => IsValidIndex(equippedIndex.Value) ? GetSlot(equippedIndex.Value) : null;

        /// <summary>ยิงเมื่อมีไอเทมเข้า/ออกจากช่องใดก็ตาม (ทุกเครื่อง)</summary>
        public event Action OnSlotsChanged;

        /// <summary>ยิงเมื่อเปลี่ยนช่องที่ถืออยู่ (ส่ง index ใหม่, -1 = มือเปล่า) (ทุกเครื่อง)</summary>
        public event Action<int> OnEquippedChanged;

        private void Awake()
        {
            if (hand == null) hand = GetComponentInChildren<HandItemHolder>();

            if (itemDatabase == null)
            {
                Debug.LogError("[PlayerInventory] ยังไม่ได้ลาก Item Database มาใส่ — ซิงค์ไอเทมออนไลน์ไม่ได้เลย", this);
            }
        }

        public override void OnNetworkSpawn()
        {
            slotItemIndices.OnListChanged += HandleSlotsChanged;
            equippedIndex.OnValueChanged += HandleEquippedChanged;

            if (IsServer)
            {
                while (slotItemIndices.Count < Mathf.Max(2, slotCount)) slotItemIndices.Add(-1);

                foreach (ItemDefinition item in startingItems)
                {
                    if (item != null) TryAddServerSide(item, out _);
                }

                if (equipFirstItemOnStart && equippedIndex.Value < 0)
                {
                    int first = FindFirstFilledSlot();
                    if (first >= 0) EquipServerSide(first);
                }
            }

            // เผื่อค่าถูกซิงค์มาถึงเครื่องนี้ก่อนจะทัน subscribe event — รีเฟรชมือให้ตรงกับสถานะปัจจุบันทันที
            RefreshHandFromEquipped();
        }

        public override void OnNetworkDespawn()
        {
            slotItemIndices.OnListChanged -= HandleSlotsChanged;
            equippedIndex.OnValueChanged -= HandleEquippedChanged;
        }

        private void HandleSlotsChanged(NetworkListEvent<int> _) => OnSlotsChanged?.Invoke();

        private void HandleEquippedChanged(int previous, int current)
        {
            RefreshHandFromEquipped();
            OnEquippedChanged?.Invoke(current);
        }

        private void RefreshHandFromEquipped()
        {
            // ทำงานบนทุกเครื่อง (ไม่ใช่แค่เจ้าของ) เพื่อให้ทุกคนเห็นว่าผู้เล่นคนนี้ถือของอะไรอยู่จริง
            if (hand != null) hand.Hold(EquippedItem);
        }

        public bool IsValidIndex(int index) => index >= 0 && index < slotItemIndices.Count;

        public ItemDefinition GetSlot(int index)
        {
            if (!IsValidIndex(index)) return null;
            return itemDatabase != null ? itemDatabase.GetByIndex(slotItemIndices[index]) : null;
        }

        public bool Contains(ItemDefinition item)
        {
            if (item == null || itemDatabase == null) return false;

            int dbIndex = itemDatabase.GetIndex(item);
            if (dbIndex < 0) return false;

            for (int i = 0; i < slotItemIndices.Count; i++)
            {
                if (slotItemIndices[i] == dbIndex) return true;
            }
            return false;
        }

        // ==========================================================
        // เรียกจาก Client — ขอเปลี่ยนสถานะ ยิง Rpc ไปให้ Server ตัดสิน
        // ==========================================================

        /// <summary>ขอสลับไอเทมที่ถือ (-1 หรือช่องว่าง = มือเปล่า) เรียกได้ทั้งจาก Client และ Server</summary>
        public void RequestEquip(int index)
        {
            if (IsServer) EquipServerSide(index);
            else EquipRpc(index);
        }

        /// <summary>ขอเลื่อนไปช่องที่มีของถัดไป (direction = +1 / -1) ใช้กับล้อเมาส์ได้</summary>
        public void RequestEquipRelative(int direction)
        {
            if (IsServer) EquipRelativeServerSide(direction);
            else EquipRelativeRpc(direction);
        }

        /// <summary>ขอโยนไอเทมที่ถืออยู่ทิ้งลงพื้น</summary>
        public void RequestDrop(Vector3 dropPosition, Vector3 dropDirection)
        {
            if (IsServer) DropEquippedServerSide(dropPosition, dropDirection);
            else DropRpc(dropPosition, dropDirection);
        }

        [Rpc(SendTo.Server)]
        private void EquipRpc(int index) => EquipServerSide(index);

        [Rpc(SendTo.Server)]
        private void EquipRelativeRpc(int direction) => EquipRelativeServerSide(direction);

        [Rpc(SendTo.Server)]
        private void DropRpc(Vector3 dropPosition, Vector3 dropDirection) => DropEquippedServerSide(dropPosition, dropDirection);

        // ==========================================================
        // ตรรกะจริง — รันบน Server เท่านั้น (ItemPickupInteractor เรียก TryAddServerSide จากใน Rpc ของมันเองได้ตรงๆ)
        // ==========================================================

        /// <summary>ใส่ไอเทมลงช่องว่างช่องแรก ต้องเรียกจาก Server เท่านั้น คืน false ถ้ากระเป๋าเต็มหรือไม่ได้อยู่ในฐานข้อมูล</summary>
        public bool TryAddServerSide(ItemDefinition item, out int index)
        {
            index = -1;
            if (!IsServer || item == null || itemDatabase == null) return false;

            int dbIndex = itemDatabase.GetIndex(item);
            if (dbIndex < 0)
            {
                Debug.LogError($"[PlayerInventory] '{item.DisplayName}' ไม่ได้อยู่ใน Item Database — เพิ่มลงฐานข้อมูลก่อนถึงจะเก็บ/ถือออนไลน์ได้", this);
                return false;
            }

            for (int i = 0; i < slotItemIndices.Count; i++)
            {
                if (slotItemIndices[i] < 0)
                {
                    slotItemIndices[i] = dbIndex;
                    index = i;
                    return true;
                }
            }

            return false;
        }

        /// <summary>เอาไอเทมออกจากช่อง ต้องเรียกจาก Server เท่านั้น คืนไอเทมที่ถูกเอาออก (null ถ้าช่องว่างอยู่แล้ว)</summary>
        public ItemDefinition RemoveAtServerSide(int index)
        {
            if (!IsServer || !IsValidIndex(index)) return null;

            int dbIndex = slotItemIndices[index];
            if (dbIndex < 0) return null;

            slotItemIndices[index] = -1;

            if (equippedIndex.Value == index) EquipServerSide(-1);

            return itemDatabase != null ? itemDatabase.GetByIndex(dbIndex) : null;
        }

        private void EquipServerSide(int index)
        {
            if (!IsServer) return;
            if (index >= slotItemIndices.Count) return;
            if (index >= 0 && slotItemIndices[index] < 0) index = -1;
            if (index < 0) index = -1;

            equippedIndex.Value = index;
        }

        private void EquipRelativeServerSide(int direction)
        {
            if (!IsServer || slotItemIndices.Count == 0 || direction == 0) return;

            int start = equippedIndex.Value < 0 ? 0 : equippedIndex.Value;
            for (int step = 1; step <= slotItemIndices.Count; step++)
            {
                int candidate = ((start + direction * step) % slotItemIndices.Count + slotItemIndices.Count) % slotItemIndices.Count;
                if (slotItemIndices[candidate] >= 0)
                {
                    EquipServerSide(candidate);
                    return;
                }
            }
        }

        private void DropEquippedServerSide(Vector3 dropPosition, Vector3 dropDirection)
        {
            if (!IsServer) return;

            ItemDefinition item = EquippedItem;
            if (item == null) return;

            if (item.worldPrefab == null)
            {
                Debug.LogWarning($"[PlayerInventory] ไอเทม '{item.DisplayName}' ไม่มี World Prefab เลยโยนทิ้งไม่ได้", this);
                return;
            }

            int index = equippedIndex.Value;
            GameObject dropped = Instantiate(item.worldPrefab, dropPosition, Quaternion.LookRotation(FlattenOrForward(dropDirection)));

            WorldItem worldItem = dropped.GetComponent<WorldItem>();
            if (worldItem == null) worldItem = dropped.AddComponent<WorldItem>();
            worldItem.SetDefinition(item);

            if (dropped.TryGetComponent(out Rigidbody body))
            {
                body.linearVelocity = dropDirection.normalized * 3f + Vector3.up * 2f;
            }

            if (dropped.TryGetComponent(out NetworkObject netObj))
            {
                netObj.Spawn();
            }
            else
            {
                Debug.LogError($"[PlayerInventory] World Prefab ของ '{item.DisplayName}' ไม่มี NetworkObject — โยนทิ้งแล้วเครื่องอื่นจะไม่เห็น ต้องเพิ่ม NetworkObject ใน Prefab ก่อน", this);
            }

            RemoveAtServerSide(index);
        }

        private static Vector3 FlattenOrForward(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude < 0.0001f ? Vector3.forward : direction.normalized;
        }

        private int FindFirstFilledSlot()
        {
            for (int i = 0; i < slotItemIndices.Count; i++)
            {
                if (slotItemIndices[i] >= 0) return i;
            }
            return -1;
        }
    }
}

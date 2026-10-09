using Nsc.Combat;
using Nsc.Match;
using Unity.Netcode;
using UnityEngine;

namespace NscUnity.Items
{
    /// <summary>
    /// ผลการยิงหนึ่งนัดที่ server คำนวณแล้วประกาศให้ทุกเครื่อง
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

        /// <summary>ทีมของคนยิง — DamageRouter ใช้กันยิงเพื่อนร่วมทีม (None = ไม่มีทีม)</summary>
        public Team shooterTeam;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref origin);
            serializer.SerializeValue(ref direction);
            serializer.SerializeValue(ref speed);
            serializer.SerializeValue(ref damage);
            serializer.SerializeValue(ref knockback);
            serializer.SerializeValue(ref maxDistance);
            serializer.SerializeValue(ref hitRadius);
            serializer.SerializeValue(ref shooterTeam);
            serializer.SerializeValue(ref visualSize);
        }
    }

    /// <summary>
    /// "มือ" ของตัวละคร — spawn / ทำลาย โมเดลไอเทมที่ Hold Point และส่งต่อ input ปุ่มใช้งานไปให้ HeldItem
    ///
    /// ออนไลน์: Hold() ทำงานบนทุกเครื่อง (ขับโดย PlayerInventory.EquippedIndex ที่ซิงค์อยู่แล้ว)
    /// input อ่านเฉพาะฝั่งเจ้าของ แล้วข้ามไป server ด้วยโปรโตคอลกลางชุดเดียว (เริ่ม/ปล่อย/ยกเลิก)
    /// ส่งแค่ "จังหวะกด" ข้ามเครือข่าย — ค่าความแรงของนัดคำนวณบน server เสมอ
    /// มือไม่รู้จักไอเทมชนิดไหนเป็นพิเศษ: โปรโตคอลชาร์จอยู่ใน ChargeGunHeldItem เอง
    /// </summary>
    public class HandItemHolder : NetworkBehaviour
    {
        [Header("จุดยึดไอเทม")]
        [Tooltip("Empty GameObject ที่เป็นลูกของกระดูกมือ — ไอเทมจะถูก spawn เป็นลูกของจุดนี้")]
        [SerializeField] private Transform holdPoint;

        [Header("ปุ่มใช้งานไอเทม")]
        [SerializeField] private bool handleUseInput = true;
        [Tooltip("0 = คลิกซ้าย, 1 = คลิกขวา, 2 = คลิกกลาง\n" +
                 "คลิกซ้าย = ก้าวเดิน/ต่อย และคลิกขวาค้าง = หมุนกล้อง ทั้งสองปุ่มชนแน่ๆ — ค่าเริ่มต้นจึงเป็นคลิกกลาง")]
        [SerializeField] private int useMouseButton = 2;

        [Header("สคริปต์ที่ต้องปิดตอนถือไอเทม")]
        [Tooltip("ปกติปล่อยว่างได้ — ใส่เฉพาะถ้ามีสคริปต์อื่นที่อยากปิดตอนมือไม่ว่าง")]
        [SerializeField] private Behaviour[] disableWhileHolding;

        /// <summary>ไอเทมที่ถืออยู่ตอนนี้ (null = มือเปล่า) — อินสแตนซ์ local ของเครื่องนี้เอง</summary>
        public HeldItem Current { get; private set; }

        /// <summary>ข้อมูลไอเทมที่ถืออยู่ตอนนี้ (null = มือเปล่า)</summary>
        public ItemDefinition CurrentDefinition { get; private set; }

        public bool IsHoldingSomething => CurrentDefinition != null;

        public Transform HoldPoint => holdPoint;

        // server เท่านั้น — การใช้งานที่กำลังดำเนินอยู่และ cooldown (เก็บที่มือ สลับอาวุธแล้ว cooldown ไม่รีเซ็ต)
        private HeldItem serverUsingItem;
        private ulong serverUsingOwner;
        private double serverUseStartedAt;
        private double serverNextUseAt;

        private void Awake()
        {
            if (holdPoint == null)
                Debug.LogWarning($"[HandItemHolder] ยังไม่ได้ลาก Hold Point มาใส่บน '{name}' — ไอเทมจะไม่โผล่ในมือ", this);

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
            serverUsingItem = null;
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
                    Destroy(holdPoint.GetChild(i).gameObject);
            }

            CurrentDefinition = null;
        }

        private void ApplyDisabledBehaviours()
        {
            // ผู้เล่นระยะไกลไม่ควรมีสคริปต์เหล่านี้ทำงานอยู่แล้ว — ปิด/เปิดเฉพาะของเครื่องเจ้าของ
            if (disableWhileHolding == null || !IsOwner) return;

            bool holding = IsHoldingSomething;
            foreach (Behaviour behaviour in disableWhileHolding)
                if (behaviour != null) behaviour.enabled = !holding;
        }

        private void Update()
        {
            // อ่าน input จริงเฉพาะฝั่งเจ้าของ — เครื่องอื่นเห็นแค่ผลลัพธ์ที่ซิงค์มา
            if (!IsOwner || !handleUseInput || Current == null) return;

            // วงล้อเลือกไอเทมเปิดอยู่ / แมตช์ยังไม่เริ่มหรือจบแล้ว → ห้ามใช้ไอเทม
            if (UiFocus.IsCaptured || !GameplayGate.CanAct)
            {
                Current.OnUseCancelled();
                return;
            }

            if (InputCompat.GetMouseButtonDown(useMouseButton)) Current.OnUseStart();
            if (InputCompat.GetMouseButton(useMouseButton)) Current.OnUseHold(Time.deltaTime);
            if (InputCompat.GetMouseButtonUp(useMouseButton)) Current.OnUseEnd();
        }

        // ==========================================================
        //  โปรโตคอลกลาง: เจ้าของส่งแค่จังหวะกด — ค่าทุกอย่างคำนวณบน server
        // ==========================================================

        public void RequestUseStart()
        {
            if (IsSpawned && IsOwner) UseStartRpc();
        }

        public void RequestUseRelease()
        {
            if (IsSpawned && IsOwner) UseReleaseRpc();
        }

        public void RequestUseCancel()
        {
            if (IsSpawned && IsOwner) UseCancelRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void UseStartRpc()
        {
            if (serverUsingOwner != OwnerClientId) serverUsingItem = null;
            if (!GameplayGate.CanAct || serverUsingItem != null || Current == null) return;

            double now = NetworkManager.ServerTime.Time;
            if (now < serverNextUseAt) return;

            serverUsingItem = Current;
            serverUsingOwner = OwnerClientId;
            serverUseStartedAt = now;
            Current.ServerOnUseStart();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void UseReleaseRpc()
        {
            HeldItem item = serverUsingItem;
            serverUsingItem = null;
            if (!GameplayGate.CanAct || item == null || item != Current || serverUsingOwner != OwnerClientId) return;

            item.ServerOnUseRelease((float)(NetworkManager.ServerTime.Time - serverUseStartedAt));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void UseCancelRpc() => serverUsingItem = null;

        /// <summary>[SERVER] ไอเทมยิงนัดหนึ่งออกไป — ตั้ง cooldown ที่มือแล้วประกาศให้ทุกเครื่องเล่นภาพตรงกัน</summary>
        public void ServerBroadcastShot(FireData data, float cooldownSeconds)
        {
            if (!IsServer) return;
            serverNextUseAt = NetworkManager.ServerTime.Time + Mathf.Max(0f, cooldownSeconds);
            ShotRpc(data);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        private void ShotRpc(FireData data)
        {
            if (Current != null) Current.OnShot(data);
        }

        public override void OnNetworkDespawn()
        {
            serverUsingItem = null;
            serverNextUseAt = 0;
            base.OnNetworkDespawn();
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

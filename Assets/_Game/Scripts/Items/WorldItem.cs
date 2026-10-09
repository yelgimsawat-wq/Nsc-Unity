using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace NscUnity.Items
{
    /// <summary>
    /// ไอเทมที่วางอยู่ในฉาก รอให้ผู้เล่นเดินไปกด E เก็บ
    /// ต้องมี Collider อยู่บน GameObject นี้ (หรือลูกของมัน) เพื่อให้ตัวตรวจจับมองเห็น
    /// ออนไลน์: ต้องมี NetworkObject แปะอยู่ด้วย (เพิ่มใน Prefab เอง) ไม่งั้นเก็บออนไลน์ไม่ได้ —
    /// ItemPickupInteractor จะแจ้ง error ให้เองถ้าลืม
    /// </summary>
    public class WorldItem : MonoBehaviour
    {
        [Header("ไอเทมชิ้นนี้คืออะไร")]
        [SerializeField] private ItemDefinition definition;

        [Header("เอฟเฟกต์ตอนวางอยู่บนพื้น")]
        [SerializeField] private bool spinIdle = true;
        [SerializeField] private float spinSpeed = 45f;
        [SerializeField] private float bobHeight = 0.12f;
        [SerializeField] private float bobSpeed = 2f;

        [Header("ตอนถูกเก็บ")]
        [Tooltip("เอฟเฟกต์ที่จะเล่นตรงจุดที่ไอเทมหายไป (ใส่หรือไม่ใส่ก็ได้)")]
        [SerializeField] private GameObject pickupEffect;

        private Vector3 startLocalPosition;
        private float bobOffset;

        /// <summary>
        /// ไอเทมทุกชิ้นที่ยัง active อยู่ในฉาก — ItemPickupInteractor วนเช็คจากลิสต์นี้แทนการยิง
        /// Physics.OverlapSphere ทุกเฟรม (ในแมพใหญ่ๆ การยิง physics + GetComponentInParent
        /// กับ collider ทุกชิ้นทุกเฟรมทำให้เกมหน่วงหนัก ทั้งที่ของในฉากมีไม่กี่ชิ้น)
        /// </summary>
        public static readonly List<WorldItem> Active = new List<WorldItem>();

        public ItemDefinition Definition => definition;

        public string PromptText => definition != null ? definition.DisplayName : name;

        private void Awake()
        {
            startLocalPosition = transform.localPosition;
            // สุ่มเฟสการลอย เพื่อไม่ให้ไอเทมทุกชิ้นในฉากลอยขึ้นลงพร้อมกันเป๊ะ
            bobOffset = Random.value * Mathf.PI * 2f;

            if (definition == null)
            {
                Debug.LogWarning($"[WorldItem] '{name}' ยังไม่ได้ใส่ Item Definition — เก็บขึ้นมือไม่ได้", this);
            }
        }

        private void OnEnable() => Active.Add(this);

        private void OnDisable() => Active.Remove(this);

        private void Update()
        {
            if (!spinIdle) return;

            transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);

            if (bobHeight > 0f)
            {
                float y = Mathf.Sin(Time.time * bobSpeed + bobOffset) * bobHeight;
                transform.localPosition = startLocalPosition + Vector3.up * y;
            }
        }

        public void SetDefinition(ItemDefinition value)
        {
            definition = value;
            startLocalPosition = transform.localPosition;
        }

        /// <summary>เรียกโดย ItemPickupInteractor (ฝั่ง Server เท่านั้น) หลังไอเทมเข้ากระเป๋าเรียบร้อยแล้ว</summary>
        public void ConsumeFromWorld()
        {
            if (pickupEffect != null)
            {
                Instantiate(pickupEffect, transform.position, Quaternion.identity);
            }

            if (TryGetComponent(out NetworkObject networkObject) && networkObject.IsSpawned)
            {
                // Netcode จะเตือนว่า "Destroying in-scene network objects..." ถ้าของชิ้นนี้วางไว้ในฉากตั้งแต่แรก
                // — เป็นคำเตือนเชิงแนะนำเฉยๆ เพราะของแบบนั้น spawn กลับมาใหม่ไม่ได้
                // สำหรับไอเทมที่เก็บแล้วหายถาวร นี่คือพฤติกรรมที่ต้องการอยู่แล้ว ปล่อยผ่านได้
                networkObject.Despawn(true); // true = ทำลาย GameObject ให้ทุกเครื่องเห็นของหายพร้อมกัน
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }
}

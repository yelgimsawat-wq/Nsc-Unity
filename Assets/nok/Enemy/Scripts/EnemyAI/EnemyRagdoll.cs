// =============================================================================
//  EnemyRagdoll.cs
//  จัดการการสลับระหว่าง Animator (ปกติ) ↔ Ragdoll Physics (ตาย)
//
//  วิธีใช้:
//  1. Add script นี้ไปที่ Enemy Prefab (root)
//  2. ตั้งค่า Ragdoll ใน Inspector ก่อน (ดู Setup ด้านล่าง)
//  3. EnemyController.ServerDie() จะเรียก ClientRpc → EnableRagdoll()
//
//  SETUP (ทำครั้งเดียวใน Unity):
//  ─────────────────────────────────────────────────────────────────────────
//  1. เลือก Enemy Prefab
//  2. เมนู: GameObject → 3D Object → Ragdoll...
//  3. ลาก Bone ใส่ช่อง Root, Hips, Spine, Head, LeftArm, RightArm, LeftLeg, RightLeg
//  4. กด Create → Unity สร้าง Rigidbody + Collider + CharacterJoint อัตโนมัติ
//  5. Script นี้จะ disable Rigidbody ทั้งหมดตอน Awake และเปิดเมื่อตาย
// =============================================================================

using UnityEngine;
using Unity.Netcode;

namespace NscGame.Enemy
{
    /// <summary>
    /// ทำงานบนทุก Client (ไม่ต้อง IsServer)
    /// เพราะ Ragdoll เป็นแค่ Visual Effect ฝั่ง Client
    /// </summary>
    public class EnemyRagdoll : NetworkBehaviour
    {
        // ─────────────────────────────────────────
        //  Inspector
        // ─────────────────────────────────────────

        [Header("Ragdoll Settings")]
        [Tooltip("แรงที่ใช้ผลักร่างเมื่อตาย (ถ้าไม่ต้องการให้ล้มตรงๆ)")]
        [SerializeField] private float deathImpulseForce = 3f;

        [Tooltip("ทิศทาง Force เมื่อตาย (world space) — ถ้าปล่อยว่างจะล้มตรงลง")]
        [SerializeField] private Vector3 deathForceDirection = Vector3.zero;

        [Tooltip("Fade out ร่างหลังจาก (วินาที) — 0 = ไม่ Fade")]
        [SerializeField] private float disappearDelay = 5f;

        // ─────────────────────────────────────────
        //  Private
        // ─────────────────────────────────────────

        private Animator       animator;
        private Rigidbody[]    ragdollBodies;   // Rigidbody ทุกชิ้นของกระดูก
        private Collider[]     ragdollColliders;
        private Collider       mainCollider;     // Capsule Collider หลักของ Enemy root

        private bool           isRagdollActive = false;

        // ─────────────────────────────────────────
        //  Unity Lifecycle
        // ─────────────────────────────────────────

        private void Awake()
        {
            animator         = GetComponent<Animator>();
            mainCollider     = GetComponent<Collider>(); // CapsuleCollider บน root

            // เก็บ Rigidbody และ Collider ทุกชิ้นของกระดูก
            // (ไม่รวม root ของตัว Enemy เอง)
            ragdollBodies    = GetComponentsInChildren<Rigidbody>();
            ragdollColliders = GetComponentsInChildren<Collider>();

            // ปิด Ragdoll ทั้งหมดตอนเริ่ม
            SetRagdollActive(false);
        }

        // ─────────────────────────────────────────
        //  Public API — เรียกจาก EnemyController
        // ─────────────────────────────────────────

        /// <summary>
        ///  เปิด Ragdoll พร้อม optional Force (เช่น ถูกเตะกระเด็น)
        ///  เรียกได้จาก ClientRpc ใน EnemyController
        /// </summary>
        public void EnableRagdoll(Vector3 impactForce = default)
        {
            if (isRagdollActive) return;
            isRagdollActive = true;

            // ปิด Animator — หยุด Animation ทันที
            animator.enabled = false;

            // ปิด Main Collider ของ root (ไม่งั้นจะชนกัน)
            if (mainCollider != null)
                mainCollider.enabled = false;

            // เปิด Ragdoll Physics
            SetRagdollActive(true);

            // ใส่แรงผลัก (ถ้ามี)
            Vector3 force = impactForce != default
                ? impactForce
                : deathForceDirection.normalized * deathImpulseForce;

            if (force != Vector3.zero)
            {
                // ใส่ force ที่ Rigidbody ของ Hips (กลางร่าง) เพื่อผลักทั้งตัว
                Rigidbody hipsRb = ragdollBodies.Length > 0 ? ragdollBodies[0] : null;
                if (hipsRb != null)
                    hipsRb.AddForce(force, ForceMode.Impulse);
            }

            // เริ่ม Fade / Disappear
            if (disappearDelay > 0f)
                Invoke(nameof(StartDisappear), disappearDelay);
        }

        /// <summary>ปิด Ragdoll และคืนการควบคุมให้ Animator (ถ้าต้องการ Get Up)</summary>
        public void DisableRagdoll()
        {
            if (!isRagdollActive) return;
            isRagdollActive = false;

            SetRagdollActive(false);
            animator.enabled = true;

            if (mainCollider != null)
                mainCollider.enabled = true;
        }

        // ─────────────────────────────────────────
        //  Private Helpers
        // ─────────────────────────────────────────

        /// <summary>Toggle Rigidbody.isKinematic และ Collider.enabled บนทุก Bone</summary>
        private void SetRagdollActive(bool active)
        {
            foreach (Rigidbody rb in ragdollBodies)
            {
                rb.isKinematic = !active;   // active=true → ปิด kinematic → ฟิสิกส์ทำงาน
                rb.useGravity  = active;
            }

            foreach (Collider col in ragdollColliders)
            {
                // ไม่แตะ main collider ของ root
                if (col == mainCollider) continue;
                col.enabled = active;
            }
        }

        private void StartDisappear()
        {
            StartCoroutine(FadeAndDestroy());
        }

        private System.Collections.IEnumerator FadeAndDestroy()
        {
            // รวบรวม Renderer ทั้งหมด
            Renderer[] renderers = GetComponentsInChildren<Renderer>();

            // Cache materials
            Material[][] mats = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
                mats[i] = renderers[i].materials;

            float elapsed = 0f;
            float fadeDuration = 2f;

            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                float alpha = Mathf.Lerp(1f, 0f, elapsed / fadeDuration);

                for (int i = 0; i < renderers.Length; i++)
                {
                    foreach (Material mat in mats[i])
                    {
                        // ต้องใช้ Shader ที่รองรับ Transparency (URP: Lit + Alpha Clipping)
                        if (mat.HasProperty("_BaseColor"))
                        {
                            Color c = mat.GetColor("_BaseColor");
                            c.a = alpha;
                            mat.SetColor("_BaseColor", c);
                        }
                    }
                }

                yield return null;
            }

            // Despawn บน Server — destroy ทุก Client อัตโนมัติ
            if (IsServer && TryGetComponent<NetworkObject>(out var netObj))
                netObj.Despawn();
            else if (!IsServer)
                Destroy(gameObject);
        }
    }
}

// =============================================================================
//  EnemyHealth.cs
//  จัดการ HP ของศัตรู + รับ Knockback เมื่อโดนผู้เล่นตี
//
//  วิธีใช้:
//  ─────────────────────────────────────────────────────────────────────────
//  1. Add script นี้ไปที่ Enemy Prefab (root)
//  2. จาก script ของผู้เล่น (ฝั่ง Attack) ให้เรียก:
//
//     EnemyHealth enemyHp = hitCollider.GetComponentInParent<EnemyHealth>();
//     if (enemyHp != null)
//         enemyHp.ServerTakeHit(damage, hitDirection, knockbackForce);
//
//  หมายเหตุ: hitDirection = ทิศที่ผู้เล่นหน้าอยู่ (transform.forward ของผู้เล่น)
// =============================================================================

using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using NscGame.Enemy;

namespace NscGame.Enemy
{
    [RequireComponent(typeof(EnemyController))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyHealth : NetworkBehaviour
    {
        // ─────────────────────────────────────────
        //  Inspector
        // ─────────────────────────────────────────

        [Header("HP Settings")]
        [SerializeField] private float maxHp = 100f;

        [Header("Knockback Settings")]
        [Tooltip("ระยะเวลาที่ศัตรูถูกผลัก (วินาที)")]
        [SerializeField] private float knockbackDuration = 0.3f;

        [Tooltip("แรงผลักสูงสุด (ปรับตาม feel ที่ต้องการ)")]
        [SerializeField] private float knockbackMultiplier = 1.0f;

        [Tooltip("ถ้า HP เหลือน้อยกว่า % นี้ knockback จะแรงขึ้น (สร้าง feel ว่าศัตรูอ่อนแล้ว)")]
        [SerializeField, Range(0f, 1f)] private float weakKnockbackThreshold = 0.3f;

        [Tooltip("knockback เพิ่มขึ้นอีกเท่าไหร่ตอน HP น้อย")]
        [SerializeField] private float weakKnockbackBonus = 0.5f;

        [Header("Hit Reaction VFX/SFX")]
        [SerializeField] private GameObject hitVfxPrefab;
        [SerializeField] private AudioClip  sfxHit;
        [SerializeField] private AudioSource audioSource;

        // ─────────────────────────────────────────
        //  NetworkVariables
        // ─────────────────────────────────────────

        /// <summary>HP ปัจจุบัน — ทุก Client อ่านได้ เพื่อแสดง HP Bar</summary>
        public NetworkVariable<float> CurrentHp = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        // ─────────────────────────────────────────
        //  Private
        // ─────────────────────────────────────────

        private EnemyController controller;
        private NavMeshAgent    agent;
        private bool            isDead = false;

        // ─────────────────────────────────────────
        //  Unity Lifecycle
        // ─────────────────────────────────────────

        private void Awake()
        {
            controller  = GetComponent<EnemyController>();
            agent       = GetComponent<NavMeshAgent>();
            audioSource = GetComponent<AudioSource>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsServer)
                CurrentHp.Value = maxHp;
        }

        // ─────────────────────────────────────────
        //  SERVER: รับ Damage + Knockback
        //  เรียกจาก script ของผู้เล่นเมื่อโจมตีโดน
        // ─────────────────────────────────────────

        /// <summary>
        ///  [SERVER ONLY] เรียกจาก Player Attack Script
        ///  hitDirection = ทิศที่ผู้เล่นหน้าอยู่ (transform.forward ของผู้เล่น)
        ///  knockbackForce = แรงผลัก (แนะนำ 3-8)
        /// </summary>
        public void ServerTakeHit(float damage, Vector3 hitDirection, float knockbackForce)
        {
            if (!IsServer || isDead) return;

            // ── 1. ลด HP ──────────────────────────────────────────────────
            CurrentHp.Value = Mathf.Max(0, CurrentHp.Value - damage);

            // ── 2. คำนวณ knockback ─────────────────────────────────────────
            // ถ้า HP น้อย (อ่อนแล้ว) knockback แรงขึ้น
            float hpRatio    = CurrentHp.Value / maxHp;
            float bonusForce = (hpRatio < weakKnockbackThreshold) ? weakKnockbackBonus : 0f;
            float totalForce = knockbackForce * knockbackMultiplier + bonusForce;

            // ทิศทาง = ทิศที่ผู้เล่นหน้าอยู่ + ผลักขึ้นเล็กน้อย
            Vector3 knockDir = hitDirection.normalized;
            knockDir.y = 0.15f;  // ผลักขึ้นนิดนึง ทำให้ดูกระเด็น
            knockDir.Normalize();

            // ── 3. บอกทุก Client แสดง Hit Reaction ─────────────────────────
            Vector3 impactPos = transform.position + Vector3.up * 0.8f;
            PlayHitEffectsClientRpc(impactPos);

            // ── 4. Apply Knockback บน Server + sync ไป Client ───────────────
            StartCoroutine(ServerKnockbackRoutine(knockDir * totalForce));

            // ── 5. ตรวจ Death ─────────────────────────────────────────────
            if (CurrentHp.Value <= 0)
                ServerDie();
        }

        // ─────────────────────────────────────────
        //  SERVER: Knockback Coroutine
        // ─────────────────────────────────────────

        private IEnumerator ServerKnockbackRoutine(Vector3 force)
        {
            // หยุด AI ชั่วคราวให้ EnemyController รู้ว่ากำลังโดนผลัก
            controller.ServerBeginKnockback();

            // ปิด NavMeshAgent ชั่วคราว ใช้ transform เลื่อนเอง
            agent.isStopped = true;
            agent.enabled   = false;

            float elapsed = 0f;
            while (elapsed < knockbackDuration)
            {
                // ลด force ตาม easing curve (แรงช่วงแรก ค่อยๆ ช้าลง)
                float t          = elapsed / knockbackDuration;
                float eased      = 1f - t * t;              // Ease Out Quad
                Vector3 movement = force * eased * Time.deltaTime;

                transform.position += movement;

                elapsed += Time.deltaTime;
                yield return null;
            }

            // คืนการควบคุมให้ NavMeshAgent
            agent.enabled   = true;
            agent.isStopped = false;

            controller.ServerEndKnockback();
        }

        // ─────────────────────────────────────────
        //  SERVER: Death
        // ─────────────────────────────────────────

        private void ServerDie()
        {
            isDead = true;
            controller.ServerDie();     // บอก EnemyController ให้ตาย
        }

        // ─────────────────────────────────────────
        //  CLIENT RPC — Hit Effects บนทุก Client
        // ─────────────────────────────────────────

        /// <summary>Spawn VFX + SFX เมื่อโดนตีบนทุก Client</summary>
        [ClientRpc]
        private void PlayHitEffectsClientRpc(Vector3 impactPosition)
        {
            // VFX
            if (hitVfxPrefab != null)
            {
                GameObject vfx = Instantiate(hitVfxPrefab, impactPosition, Quaternion.identity);
                ParticleSystem ps = vfx.GetComponentInChildren<ParticleSystem>();
                Destroy(vfx, ps != null ? ps.main.duration + 1f : 2f);
            }

            // SFX
            if (sfxHit != null && audioSource != null)
                audioSource.PlayOneShot(sfxHit);
        }

        // ─────────────────────────────────────────
        //  Public Helpers
        // ─────────────────────────────────────────

        public float GetHpPercent()  => CurrentHp.Value / maxHp;
        public bool  IsDead()        => isDead;
        public float GetMaxHp()      => maxHp;

        /// <summary>
        /// ฟังก์ชันสำหรับทดสอบการตายจาก Inspector
        /// </summary>
        [ContextMenu("Test Kill (Kill immediately)")]
        public void TestKill()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("You can only kill the enemy during Play Mode!");
                return;
            }

            if (IsSpawned)
            {
                if (IsServer)
                {
                    ServerTakeHit(CurrentHp.Value, Vector3.forward, 5f);
                }
                else
                {
                    Debug.LogWarning("Test Kill must be executed on the Server/Host!");
                }
            }
            else
            {
                // ถ้ายังไม่ได้ต่อเน็ตเวิร์ก/ยังไม่ได้ Spawn (เช่น กด Play ทดสอบในซีนเดี่ยวๆ)
                isDead = true;
                if (controller != null)
                {
                    controller.ServerDie();
                }
            }
        }
    }

#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(EnemyHealth))]
    public class EnemyHealthEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EnemyHealth enemyHealth = (EnemyHealth)target;

            GUILayout.Space(15);
            
            // ปุ่มสีแดงสำหรับทดสอบการตาย
            GUI.backgroundColor = Color.red;
            if (GUILayout.Button("Kill Enemy (Test Death)", GUILayout.Height(30)))
            {
                enemyHealth.TestKill();
            }
            GUI.backgroundColor = Color.white;
        }
    }
#endif
}

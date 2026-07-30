// =============================================================================
//  EnemyUltimate.cs
//  Server-authoritative "BlackHole" ultimate, fired once the boss drops to a
//  HP threshold (50% by default).
//
//  Sequence:
//  ─────────────────────────────────────────────────────────────────────────
//   1. Back-jump   — hops straight backwards away from the player in an arc,
//                    landing no further than the boss's own walk range so it
//                    can always close the distance again afterwards.
//   2. Charge      — Armature_BlackHoleChage + blast(Chage) parented to the
//                    muzzle. The aim direction is LOCKED the moment the charge
//                    starts and the boss stops turning, so the charge doubles
//                    as a telegraph the player can simply walk out of.
//   3. Fire        — Armature_BlackHoleShot, charge VFX swapped for a moving
//                    blast(Shoot) that flies dead straight (see
//                    BlackHoleProjectile) and flattens the buildings it hits.
//
//  Animator requirements (Attack layer, alongside the melee triggers):
//    Trigger "BlackHoleCharge" → state playing Armature_BlackHoleChage
//    Trigger "BlackHoleShot"   → state playing Armature_BlackHoleShot
// =============================================================================

using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;

namespace NscGame.Enemy
{
    [RequireComponent(typeof(EnemyController))]
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Animator))]
    public class EnemyUltimate : NetworkBehaviour
    {
        #region Inspector Fields

        [Header("Trigger")]
        [Tooltip("ยิงท่าไม้ตายเมื่อเลือดเหลือถึงสัดส่วนนี้ (0.5 = 50%)")]
        [Range(0.05f, 0.95f)]
        [SerializeField] private float triggerHpPercent = 0.5f;

        [Tooltip("ใช้ท่าไม้ตายได้กี่ครั้งต่อหนึ่งชีวิต (1 = ครั้งเดียว)")]
        [SerializeField] private int maxUses = 1;

        [Header("Back-jump (กระโดดถอยหลัง)")]
        [Tooltip("ระยะที่อยากถอยเพิ่มจากระยะปัจจุบัน (เมตร)")]
        [SerializeField] private float backjumpDistance = 14f;

        [Tooltip("เพดานระยะหลังลงพื้น = Walk Threshold ของ EnemyController × ค่านี้\nกันไม่ให้ถอยไกลเกินระยะเดินของตัวเอง")]
        [Range(0.3f, 1f)]
        [SerializeField] private float maxRangeAfterJump = 0.9f;

        [SerializeField] private float jumpDuration = 0.7f;

        [Tooltip("ความสูงของส่วนโค้งตอนกระโดด (เมตร)")]
        [SerializeField] private float jumpArcHeight = 9f;

        [Header("Charge")]
        [Tooltip("ชาจนานเท่าไหร่ก่อนยิง — ช่วงนี้คือเวลาที่ผู้เล่นใช้เดินหลบออกจากแนวยิง\n" +
                 "ถ้าแก้ค่านี้ ให้ตั้ง speed ของ state 'Armature_BlackHoleChage' = 5.792 ÷ ค่านี้\n" +
                 "เพื่อให้อนิเมชันชาจยาวพอดีกับเวลาจริง (คลิปยาว 5.792 วิ)")]
        [SerializeField] private float chargeDuration = 5.5f;

        [Tooltip("หน่วงหลังเริ่มอนิเมชันยิง ก่อนพาร์ทิเคิลจะพุ่งออกไป")]
        [SerializeField] private float fireDelay = 0.35f;

        [Tooltip("หน่วงหลังยิงเสร็จก่อนคืน AI ให้เดินต่อ")]
        [SerializeField] private float recoverDelay = 1.2f;

        [Header("Muzzle / VFX")]
        [Tooltip("จุดกำเนิดลูกกระสุน (ปล่อยว่าง = ใช้ตัวบอส + Muzzle Fallback Height)")]
        [SerializeField] private Transform muzzle;

        [Tooltip("ถ้าไม่ได้ตั้ง Muzzle ให้ยิงจากความสูงนี้เหนือเท้าบอส")]
        [SerializeField] private float muzzleFallbackHeight = 16f;

        [Tooltip("blast(Chage) — พาร์ทิเคิลตอนชาจ (เกาะไปกับ Muzzle)")]
        [SerializeField] private GameObject chargeVfxPrefab;

        [Tooltip("blast(Shoot) — พาร์ทิเคิลที่ยิงออกไป (โค้ดขยับตำแหน่งให้ ไม่แตะทิศ/ความเร็ว)")]
        [SerializeField] private GameObject shotVfxPrefab;

        [Header("VFX Size")]
        [Tooltip("ขนาดลูกบอลชาจ = กี่เท่าของขนาดที่วาดไว้ในพรีแฟบ")]
        [Range(0.2f, 60f)]
        [SerializeField] private float chargeVfxScale = 4f;

        [Tooltip("ขนาดลูกกระสุนที่ยิงออกไป = กี่เท่าของขนาดที่วาดไว้ในพรีแฟบ\n" +
                 "ตั้งให้ใกล้ Damage Radius จะอ่านง่ายที่สุด (radius 22 ≈ scale 5)")]
        [Range(0.2f, 60f)]
        [SerializeField] private float shotVfxScale = 5f;

        [Header("Projectile (ทะลุผ่าน ไม่ระเบิดหยุด)")]
        [SerializeField] private float projectileSpeed = 55f;

        [Tooltip("แรงดึงลงของลูกกระสุน — 0 = บินตรงขนานพื้น (แนะนำ เพราะกระสุนทะลุผ่าน\n" +
                 "ถ้าใส่แรงตกมันจะมุดลงใต้พื้นตอนปลายเส้นทาง)")]
        [SerializeField] private float projectileGravity = 0f;

        [Tooltip("ลูกกระสุนบินนานเท่าไหร่ก่อนสลาย (วินาที) — ระยะทาง = ค่านี้ × Projectile Speed")]
        [SerializeField] private float projectileLifetime = 4.5f;

        [Tooltip("รัศมีแนวทำลายรอบเส้นทางบิน — ตึกที่อยู่ในทรงกระบอกนี้พังทั้งแนว")]
        [SerializeField] private float damageRadius = 22f;

        [Tooltip("ดาเมจที่ส่งให้ตึก (explobuilding เลือดเริ่มต้น 100 — ใส่เกินไว้ให้พังแน่)")]
        [SerializeField] private float buildingDamage = 500f;

        [Tooltip("ดาเมจที่ลงชิ้นส่วนหุ่นผู้เล่นถ้าโดนระเบิด")]
        [SerializeField] private float playerDamage = 40f;

        [Tooltip("เลเยอร์ที่ลูกกระสุนกวาดหาเป้า (ตึก/รถ = grabLayer, ชิ้นส่วนหุ่น = RobotParts)\n" +
                 "ของที่ไม่มี IHittable จะถูกข้ามอยู่แล้ว เสาไฟ/ต้นไม้จึงไม่มีผล")]
        [SerializeField] private LayerMask sweepMask = ~0;

        [Tooltip("เปิด = เล็งขนานพื้นเสมอ (แนะนำ — ผู้เล่นอ่านแนวยิงง่าย เดินหลบข้างได้)")]
        [SerializeField] private bool aimFlat = true;

        [Header("Audio")]
        [SerializeField] private AudioClip sfxCharge;
        [SerializeField] private AudioClip sfxFire;
        [SerializeField] private AudioSource audioSource;

        #endregion

        #region Network Variables

        private NetworkVariable<UltimatePhase> netPhase = new NetworkVariable<UltimatePhase>(
            UltimatePhase.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        #endregion

        #region Private Fields

        private EnemyController controller;
        private NavMeshAgent agent;
        private Animator animator;

        private int usesLeft;
        private bool ultimateQueued;
        private bool ultimateRunning;

        /// <summary>Locked at charge start — the shot never re-aims after this.</summary>
        private Vector3 lockedAimDirection = Vector3.forward;

        private GameObject activeChargeVfx;
        private BlackHoleProjectile activeProjectile;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            controller = GetComponent<EnemyController>();
            agent = GetComponent<NavMeshAgent>();
            animator = GetComponent<Animator>();

            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();

            usesLeft = Mathf.Max(0, maxUses);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netPhase.OnValueChanged += OnPhaseChanged;

            if (IsServer)
                StartCoroutine(ServerWatchForCue());
        }

        public override void OnNetworkDespawn()
        {
            netPhase.OnValueChanged -= OnPhaseChanged;
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            CleanupChargeVfx();
            if (activeProjectile != null)
                Destroy(activeProjectile.gameObject);
            base.OnDestroy();
        }

        #endregion

        #region Public API

        /// <summary>
        /// [SERVER] Called by EnemyHealth once HP crosses the threshold. Only queues
        /// the ultimate — the actual start waits for a safe moment (not mid-knockback,
        /// agent on the NavMesh, not dead) so the back-jump never fires from a bad state.
        /// </summary>
        public void ServerRequestUltimate()
        {
            if (!IsServer) return;
            if (usesLeft <= 0 || ultimateRunning || ultimateQueued) return;

            ultimateQueued = true;
            Debug.Log($"[EnemyUltimate] 🕳️ คิวท่าไม้ตายไว้แล้ว (เลือดถึง {triggerHpPercent * 100f:F0}%)");
        }

        public float TriggerHpPercent => triggerHpPercent;
        public bool IsRunning => ultimateRunning;

        #endregion

        #region Server - Orchestration

        private IEnumerator ServerWatchForCue()
        {
            while (true)
            {
                yield return null;

                if (!ultimateQueued || ultimateRunning) continue;
                if (controller.CurrentState == EnemyState.Dead) { ultimateQueued = false; continue; }

                // Wait out knockback and any NavMesh hiccup before committing.
                if (controller.ServerIsKnockedBack) continue;
                if (agent == null || !agent.enabled || !agent.isOnNavMesh) continue;

                ultimateQueued = false;
                yield return ServerUltimateRoutine();
            }
        }

        private IEnumerator ServerUltimateRoutine()
        {
            ultimateRunning = true;
            usesLeft--;
            controller.ServerBeginUltimate();

            Transform target = controller.PlayerTarget;

            // ---------- 1) back-jump ----------
            netPhase.Value = UltimatePhase.Backjump;
            yield return ServerBackjumpRoutine(target);

            if (controller.CurrentState == EnemyState.Dead) { ServerFinish(); yield break; }

            // ---------- 2) charge (aim locks here) ----------
            target = controller.PlayerTarget; // re-read: the player moved during the jump
            lockedAimDirection = ComputeAimDirection(target);
            controller.ServerRotationLocked = true;

            // Face the locked direction exactly once, then freeze.
            Vector3 flatAim = new Vector3(lockedAimDirection.x, 0f, lockedAimDirection.z);
            if (flatAim.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(flatAim, Vector3.up);

            netPhase.Value = UltimatePhase.Charging;
            PlayChargeClientRpc();

            yield return new WaitForSeconds(chargeDuration);

            if (controller.CurrentState == EnemyState.Dead) { ServerFinish(); yield break; }

            // ---------- 3) fire ----------
            netPhase.Value = UltimatePhase.Firing;
            PlayShotClientRpc();

            yield return new WaitForSeconds(fireDelay);

            if (controller.CurrentState == EnemyState.Dead) { ServerFinish(); yield break; }

            Vector3 origin = MuzzlePosition();
            FireProjectileClientRpc(origin, lockedAimDirection);
            // A dedicated server gets no ClientRpc callback, so it needs its own
            // authoritative copy. On a host the RPC already made one.
            if (!IsHost)
                SpawnProjectileLocal(origin, lockedAimDirection, authoritative: true);

            // The shot outlives the ultimate — it keeps ploughing through the city on
            // its own while the boss recovers and walks back in.

            yield return new WaitForSeconds(recoverDelay);

            ServerFinish();
        }

        private void ServerFinish()
        {
            controller.ServerRotationLocked = false;
            netPhase.Value = UltimatePhase.None;
            ResetUltimateTriggersClientRpc();
            controller.ServerEndUltimate();
            ultimateRunning = false;
        }

        /// <summary>
        /// Hops backwards in an arc. The landing spot is clamped so the boss ends up
        /// no further from the player than its own walk range — otherwise it would
        /// jump out of its detection range and go idle instead of resuming the fight.
        /// </summary>
        private IEnumerator ServerBackjumpRoutine(Transform target)
        {
            Vector3 start = transform.position;
            Vector3 away = -transform.forward;

            if (target != null)
            {
                Vector3 d = transform.position - target.position;
                d.y = 0f;
                if (d.sqrMagnitude > 0.0001f) away = d.normalized;
            }

            // ⚠️ transform.position ของบอสไม่ได้อยู่ที่เท้า — NavMeshAgent ยกโมเดลขึ้นเท่ากับ
            // baseOffset × scale (บอสตัวนี้ = 1.67 × 9.58 ≈ 16 หน่วย) ถ้า SamplePosition
            // จากความสูงนั้นด้วยรัศมีสั้นๆ จะหา NavMesh ไม่เจอเลยแม้จะยืนอยู่บนนั้นจริง
            float agentLift = Mathf.Abs(agent != null ? agent.baseOffset * transform.lossyScale.y : 0f);
            float sampleRadius = Mathf.Max(12f, agentLift * 1.5f + 12f);

            if (!NavMesh.SamplePosition(start, out NavMeshHit startHit, sampleRadius, NavMesh.AllAreas))
            {
                Debug.LogWarning($"[EnemyUltimate] ⚠️ บอสไม่ได้อยู่บน NavMesh (sample {sampleRadius:F0}) — ข้ามการกระโดดถอย", this);
                yield break;
            }

            // Keep whatever vertical offset the agent was already rendering at, so the
            // boss does not sink or pop when we hand control back.
            Vector3 groundStart = startHit.position;
            float yOffset = start.y - groundStart.y;

            Vector3 flatTarget = Vector3.zero;
            float ceiling = float.MaxValue;
            Vector3 landing;

            if (target != null)
            {
                // Everything is measured on the NavMesh plane, not at the boss's pivot height.
                flatTarget = new Vector3(target.position.x, groundStart.y, target.position.z);
                float currentDist = Vector3.Distance(groundStart, flatTarget);

                // Two ceilings: the walk range is the hard requirement (hop further and the
                // boss falls out of its own detection range and just stands there), and the
                // roll range is the gameplay one — landing inside it means the boss dashes
                // straight back in instead of plodding across half the city.
                ceiling = Mathf.Min(controller.WalkThreshold * maxRangeAfterJump, controller.RollTriggerDistance);

                float desired = Mathf.Min(currentDist + backjumpDistance, ceiling);
                desired = Mathf.Max(desired, currentDist); // never hop *towards* the player

                landing = flatTarget + away * desired;
            }
            else
            {
                landing = groundStart + away * backjumpDistance;
            }

            // Walk the candidate inwards until it lands on the NavMesh — the city is full of
            // buildings and the island edge is a hard boundary now. SamplePosition snaps to
            // the nearest mesh point, which can sit further out than we asked for, so the
            // resulting distance is re-checked against the ceiling before accepting it.
            Vector3 landOnMesh = groundStart;
            bool found = false;
            float startDist = target != null ? Vector3.Distance(groundStart, flatTarget) : 0f;

            for (int i = 0; i <= 8; i++)
            {
                Vector3 candidate = Vector3.Lerp(groundStart, landing, 1f - i * 0.12f);
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
                    continue;

                if (target != null)
                {
                    float d = Vector3.Distance(new Vector3(hit.position.x, groundStart.y, hit.position.z), flatTarget);
                    // Allow it only if it respects the ceiling, or if the boss was already
                    // beyond that ceiling to begin with (then anything ≤ where it started is fine).
                    if (d > ceiling && d > startDist) continue;
                }

                landOnMesh = hit.position;
                found = true;
                break;
            }
            if (!found)
            {
                Debug.LogWarning("[EnemyUltimate] ⚠️ หาจุดลงพื้นบน NavMesh ที่ยังอยู่ในระยะไม่ได้ — ข้ามการกระโดดถอย", this);
                yield break;
            }

            Vector3 from = start;
            Vector3 to = landOnMesh + Vector3.up * yOffset;

            bool agentWasEnabled = agent.enabled;
            if (agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
            agent.velocity = Vector3.zero;
            agent.enabled = false;

            float elapsed = 0f;
            float dur = Mathf.Max(0.05f, jumpDuration);
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / dur);

                Vector3 pos = Vector3.Lerp(from, to, t);
                pos.y += Mathf.Sin(t * Mathf.PI) * jumpArcHeight; // parabola-ish arc
                transform.position = pos;

                yield return null;
            }

            transform.position = to;

            // ตายกลางอากาศ (ผู้เล่นต่อยตายทันตอนกระโดด) → ServerDie ปิด agent ไว้แล้ว
            // เปิดกลับตรงนี้จะกลายเป็นศพยืนเดินได้
            if (controller.CurrentState == EnemyState.Dead) yield break;

            if (agentWasEnabled)
            {
                agent.enabled = true;
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit finalHit, sampleRadius, NavMesh.AllAreas))
                    agent.Warp(finalHit.position);
                if (agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
            }

            float finalDist = target != null ? HorizontalDistance(transform.position, target.position) : -1f;
            Debug.Log($"[EnemyUltimate] 🦘 กระโดดถอยลง {transform.position} — ห่างผู้เล่น {finalDist:F1} " +
                      $"(เพดาน {ceiling:F1} | walk {controller.WalkThreshold:F1} roll {controller.RollTriggerDistance:F1})");
        }

        private Vector3 ComputeAimDirection(Transform target)
        {
            Vector3 dir = transform.forward;

            if (target != null)
            {
                Vector3 origin = MuzzlePosition();
                dir = target.position - origin;
                if (aimFlat) dir.y = 0f;
            }
            else if (aimFlat)
            {
                dir.y = 0f;
            }

            return dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;
        }

        private Vector3 MuzzlePosition()
        {
            return muzzle != null ? muzzle.position : transform.position + Vector3.up * muzzleFallbackHeight;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }

        #endregion

        #region Client RPC - Visuals

        [ClientRpc]
        private void PlayChargeClientRpc()
        {
            if (animator != null)
            {
                // ล้าง trigger ท่าปกติที่อาจค้างอยู่ในคิว — ไม่งั้น AnyState ของ Attack layer
                // จะเด้งท่าต่อย/เตะขึ้นมาทับอนิเมชันชาจกลางท่า
                animator.ResetTrigger(EnemyAnimParam.LightPunch);
                animator.ResetTrigger(EnemyAnimParam.BarragePunch);
                animator.ResetTrigger(EnemyAnimParam.Kick);

                animator.ResetTrigger(EnemyAnimParam.BlackHoleShot);
                animator.SetTrigger(EnemyAnimParam.BlackHoleCharge);
            }

            CleanupChargeVfx();

            if (chargeVfxPrefab != null)
            {
                Transform anchor = muzzle != null ? muzzle : transform;
                activeChargeVfx = Instantiate(chargeVfxPrefab, MuzzlePosition(), anchor.rotation, anchor);
                ApplyVfxScale(activeChargeVfx, chargeVfxScale);
            }

            PlaySfx(sfxCharge);
        }

        [ClientRpc]
        private void PlayShotClientRpc()
        {
            if (animator != null)
            {
                animator.ResetTrigger(EnemyAnimParam.BlackHoleCharge);
                animator.SetTrigger(EnemyAnimParam.BlackHoleShot);
            }

            PlaySfx(sfxFire);
        }

        [ClientRpc]
        private void FireProjectileClientRpc(Vector3 origin, Vector3 direction)
        {
            // The charge orb becomes the shot — kill it exactly when the shot appears.
            CleanupChargeVfx();
            SpawnProjectileLocal(origin, direction, authoritative: IsServer);
        }

        [ClientRpc]
        private void ResetUltimateTriggersClientRpc()
        {
            if (animator == null) return;

            // Same stuck-trigger hazard EnemyCombat guards against: a trigger that no
            // transition consumed stays queued and fires the state later out of nowhere.
            animator.ResetTrigger(EnemyAnimParam.BlackHoleCharge);
            animator.ResetTrigger(EnemyAnimParam.BlackHoleShot);
        }

        #endregion

        #region Projectile Plumbing

        private void SpawnProjectileLocal(Vector3 origin, Vector3 direction, bool authoritative)
        {
            if (shotVfxPrefab == null)
            {
                if (authoritative)
                    Debug.LogWarning("[EnemyUltimate] ⚠️ ยังไม่ได้ใส่ Shot Vfx Prefab (blast(Shoot)) — ท่าไม้ตายจะไม่มีลูกกระสุน", this);
                return;
            }

            GameObject go = Instantiate(shotVfxPrefab, origin, Quaternion.LookRotation(direction, Vector3.up));
            ApplyVfxScale(go, shotVfxScale);

            BlackHoleProjectile proj = go.GetComponent<BlackHoleProjectile>();
            if (proj == null) proj = go.AddComponent<BlackHoleProjectile>();

            proj.Launch(
                origin,
                direction,
                projectileSpeed,
                projectileGravity,
                projectileLifetime,
                damageRadius,
                buildingDamage,
                playerDamage,
                sweepMask,
                transform,
                authoritative);

            activeProjectile = proj;
        }

        /// <summary>
        /// Resizes a spawned VFX to <paramref name="worldScale"/>× its authored size.
        ///
        /// ⚠️ This forces scalingMode = Hierarchy on every emitter. It has to: both blast
        /// prefabs author every system as scalingMode = Local, which makes them ignore
        /// parent scale outright, so setting a scale alone would do nothing at all (that
        /// is why the charge orb still rendered tiny even parented under the 9.58× boss).
        /// So the scalingMode you set in the prefab is NOT respected — size comes from the
        /// two fields above. Child scales are untouched, so the authored proportions
        /// between the sub-emitters survive.
        /// </summary>
        private static void ApplyVfxScale(GameObject vfx, float worldScale)
        {
            if (vfx == null) return;

            foreach (ParticleSystem ps in vfx.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }

            // Cancel out the parent's scale so the Inspector value means a plain world-size
            // multiplier, whether the effect is parented to the muzzle or free in the world.
            Transform parent = vfx.transform.parent;
            float parentScale = parent != null ? parent.lossyScale.x : 1f;
            if (Mathf.Approximately(parentScale, 0f)) parentScale = 1f;

            vfx.transform.localScale = Vector3.one * (Mathf.Max(0.01f, worldScale) / parentScale);
        }

        private void CleanupChargeVfx()
        {
            if (activeChargeVfx == null) return;

            foreach (ParticleSystem ps in activeChargeVfx.GetComponentsInChildren<ParticleSystem>(true))
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            Destroy(activeChargeVfx);
            activeChargeVfx = null;
        }

        private void PlaySfx(AudioClip clip)
        {
            if (clip == null || audioSource == null) return;
            audioSource.PlayOneShot(clip);
        }

        #endregion

        #region Client - Phase Callback

        private void OnPhaseChanged(UltimatePhase previous, UltimatePhase current)
        {
            // Late joiners and anyone who missed an RPC still get the charge VFX torn
            // down when the boss leaves the ultimate.
            if (current == UltimatePhase.None)
                CleanupChargeVfx();
        }

        #endregion

        #region Debug Gizmos

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Vector3 origin = Application.isPlaying
                ? MuzzlePosition()
                : (muzzle != null ? muzzle.position : transform.position + Vector3.up * muzzleFallbackHeight);

            // Draw the destruction corridor the shot ploughs through the city.
            float range = projectileSpeed * projectileLifetime;
            Vector3 end = origin + transform.forward * range;

            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(origin, end);

            Gizmos.color = new Color(1f, 0.4f, 0f, 0.6f);
            Gizmos.DrawWireSphere(origin, damageRadius);
            Gizmos.DrawWireSphere(end, damageRadius);
        }
#endif

        #endregion
    }
}

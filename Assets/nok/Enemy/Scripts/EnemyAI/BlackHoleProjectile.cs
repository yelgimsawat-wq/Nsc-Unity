// =============================================================================
//  BlackHoleProjectile.cs
//  The travelling half of the boss's BlackHole ultimate.
//
//  It PIERCES: nothing stops it. It ploughs straight through the city for its
//  whole lifetime, flattening every building inside a corridor around its flight
//  path, then simply fades out. There is no impact explosion.
//
//  Why this is NOT a NetworkBehaviour:
//  ─────────────────────────────────────────────────────────────────────────
//  The shot never homes and nothing deflects it, so its whole flight is a closed
//  form curve. Every machine spawns its own copy from the same (origin,
//  direction, speed) and they stay in sync for free — no RPCs at all, not even
//  for the cleanup, because the lifetime is identical everywhere. Only the
//  server-side copy applies damage.
//
//  Position is evaluated ANALYTICALLY from elapsed time rather than integrated
//  step by step — an incremental integration would drift apart between machines
//  running at different frame rates.
//
//  The visual prefab (blast(Shoot)) is a stationary effect whose particles all
//  simulate in Local space, so translating this transform carries them along.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace NscGame.Enemy
{
    public class BlackHoleProjectile : MonoBehaviour
    {
        #region Launch Config

        private Vector3 origin;
        private Vector3 direction = Vector3.forward;
        private float speed = 55f;
        private float gravity = 0f;
        private float maxLifetime = 4.5f;
        private float damageRadius = 22f;
        private float buildingDamage = 500f;
        private float playerDamage = 120f;
        private LayerMask sweepMask;
        private Transform ignoreRoot;

        /// <summary>Server-side copy applies damage; client copies are pure visuals.</summary>
        private bool authoritative;

        private float aliveTime;
        private bool finished;

        /// <summary>Each victim is damaged once, no matter how many frames it stays in the corridor.</summary>
        private readonly HashSet<Object> alreadyDamaged = new HashSet<Object>();

        private int buildingsDestroyed;
        private int partsHit;
        private int partsDetached;

        /// <summary>The knockdown is one per shot — the torso only needs telling once.</summary>
        private bool torsoKnockedDown;

        #endregion

        /// <summary>
        /// Arms the projectile. <paramref name="authoritative"/> must be true only on
        /// the server, otherwise the same building would be damaged once per machine.
        /// </summary>
        public void Launch(
            Vector3 startPosition,
            Vector3 fireDirection,
            float speed,
            float gravity,
            float maxLifetime,
            float damageRadius,
            float buildingDamage,
            float playerDamage,
            LayerMask sweepMask,
            Transform ignoreRoot,
            bool authoritative)
        {
            transform.position = startPosition;

            this.origin = startPosition;
            this.direction = fireDirection.sqrMagnitude > 0.0001f ? fireDirection.normalized : Vector3.forward;
            this.speed = Mathf.Max(1f, speed);
            this.gravity = Mathf.Max(0f, gravity);
            this.maxLifetime = Mathf.Max(0.5f, maxLifetime);
            this.damageRadius = Mathf.Max(1f, damageRadius);
            this.buildingDamage = buildingDamage;
            this.playerDamage = playerDamage;
            this.sweepMask = sweepMask;
            this.ignoreRoot = ignoreRoot;
            this.authoritative = authoritative;

            // Aim the prefab's +Z down the flight path. Orientation of the artwork itself
            // is the prefab's business — nothing here rotates or rescales it.
            transform.rotation = Quaternion.LookRotation(this.direction, Vector3.up);

            // The prefab carries a SplineAnimate left over from the timeline version.
            // Leaving it on would drive the transform every frame and fight our own
            // movement, so it has to go before the first Update runs.
            foreach (var spline in GetComponentsInChildren<UnityEngine.Splines.SplineAnimate>(true))
                spline.enabled = false;
        }

        /// <summary>Closed-form ballistic position — identical on every machine for a given t.</summary>
        private Vector3 PositionAt(float t)
        {
            return origin + direction * (speed * t) + Vector3.down * (0.5f * gravity * t * t);
        }

        private void Update()
        {
            if (finished) return;

            Vector3 from = transform.position;

            aliveTime += Time.deltaTime;
            Vector3 to = PositionAt(aliveTime);

            Vector3 delta = to - from;
            float step = delta.magnitude;

            if (authoritative && step > 0.0001f)
                DamageAlong(from, to);

            transform.position = to;
            if (step > 0.0001f)
                transform.rotation = Quaternion.LookRotation(delta / step, Vector3.up);

            if (aliveTime >= maxLifetime)
            {
                if (authoritative)
                {
                    Debug.Log($"[BlackHole] 🌀 ลูกกระสุนทะลุจบเส้นทาง {Vector3.Distance(origin, transform.position):F0} m " +
                              $"— ตึกพัง {buildingsDestroyed} หลัง, ชิ้นส่วนหุ่นโดน {partsHit} ชิ้น " +
                              $"(หลุด {partsDetached} ชิ้น, ล้ม {(torsoKnockedDown ? "ใช่" : "ไม่")})");
                }
                StopAndCleanup(2f);
            }
        }

        /// <summary>
        /// [SERVER ONLY] Sweeps a capsule over the segment travelled this frame and
        /// damages everything inside it. A capsule (not a sphere at the end point)
        /// matters because at 55 m/s the shot covers ~1 m per frame at 60fps but far
        /// more on a hitching frame — a point test would tunnel past whole buildings.
        /// </summary>
        private void DamageAlong(Vector3 from, Vector3 to)
        {
            Collider[] overlaps = Physics.OverlapCapsule(
                from, to, damageRadius, sweepMask, QueryTriggerInteraction.Ignore);

            foreach (Collider col in overlaps)
            {
                if (col == null) continue;
                if (ignoreRoot != null && col.transform.IsChildOf(ignoreRoot)) continue;

                IHittable hittable = col.GetComponentInParent<IHittable>();
                if (hittable == null) continue;

                Object key = (Object)hittable;
                if (!alreadyDamaged.Add(key)) continue;

                if (hittable is explobuilding)
                {
                    hittable.ServerTakeDamage(buildingDamage, AttackType.BlackHole, direction);
                    buildingsDestroyed++;
                }
                else
                {
                    hittable.ServerTakeDamage(playerDamage, AttackType.BlackHole, direction);
                    partsHit++;

                    // This is the ultimate: whatever it touches comes off, and the robot
                    // goes down. No HP threshold involved — a graze is enough.
                    if (hittable is RobotHealth part)
                    {
                        part.ServerBreakPart();
                        partsDetached++;
                        KnockDownRobot(part.transform);
                    }
                }
            }
        }

        /// <summary>
        /// [SERVER ONLY] Puts the robot on the floor. Going through Falling rather than
        /// straight to Ragdoll is deliberate: TorsoMovement's own FixedUpdate promotes
        /// Falling → Ragdoll, so this follows the same path a normal loss of balance takes
        /// and cannot skip whatever that transition sets up.
        /// </summary>
        private void KnockDownRobot(Transform hitPart)
        {
            if (torsoKnockedDown || hitPart == null) return;

            TorsoMovement torso = hitPart.root.GetComponentInChildren<TorsoMovement>();
            if (torso == null) return;

            torsoKnockedDown = true;   // found it — don't search the hierarchy again this shot

            if (torso.currentState.Value == TorsoMovement.TorsoState.Standing)
                torso.currentState.Value = TorsoMovement.TorsoState.Falling;
        }

        /// <summary>Stop emitting and fade out, then remove the object.</summary>
        public void StopAndCleanup(float destroyDelay)
        {
            if (finished) return;
            finished = true;

            foreach (ParticleSystem ps in GetComponentsInChildren<ParticleSystem>(true))
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            Destroy(gameObject, Mathf.Max(0f, destroyDelay));
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.6f, 0.2f, 1f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, damageRadius);
        }
#endif
    }
}

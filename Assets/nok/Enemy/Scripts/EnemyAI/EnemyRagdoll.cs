// =============================================================================
//  EnemyRagdoll.cs
//  Manages Animator ↔ Ragdoll Physics transition on death
//
//  Setup in Unity (one-time):
//  ─────────────────────────────────────────────────────────────────────────
//  1. Select Enemy Prefab
//  2. GameObject → 3D Object → Ragdoll...
//  3. Assign bones (Root, Hips, Spine, Head, Arms, Legs)
//  4. Click Create → Unity auto-generates Rigidbody + Collider + CharacterJoint
//  5. This script disables all Rigidbodies on Awake, enables on death
// =============================================================================

using System.Collections;
using UnityEngine;
using Unity.Netcode;

namespace NscGame.Enemy
{
    /// <summary>
    /// Client-side visual effect for death ragdoll
    /// Works on all clients, no server authority needed
    /// </summary>
    public class EnemyRagdoll : NetworkBehaviour
    {
        #region Inspector Fields

        [Header("Ragdoll Settings")]
        [Tooltip("Impulse force applied on death")]
        [SerializeField] private float deathImpulseForce = 3f;

        [Tooltip("Force direction on death (world space). Zero = no force")]
        [SerializeField] private Vector3 deathForceDirection = Vector3.zero;

        [Tooltip("Delay before fading out body (0 = no fade)")]
        [SerializeField] private float disappearDelay = 5f;

        #endregion

        #region Private Fields

        private Animator animator;
        private Rigidbody[] ragdollBodies;
        private Collider[] ragdollColliders;
        private Collider mainCollider;
        private bool isRagdollActive = false;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            animator = GetComponent<Animator>();
            mainCollider = GetComponent<Collider>();

            ragdollBodies = GetComponentsInChildren<Rigidbody>();
            ragdollColliders = GetComponentsInChildren<Collider>();

            SetRagdollActive(false);
        }

        #endregion

        #region Public API

        /// <summary>
        /// Enable ragdoll physics with optional impact force
        /// Called from EnemyController via ClientRpc
        /// </summary>
        /// <param name="impactForce">Directional force to apply. Zero = use default death force</param>
        public void EnableRagdoll(Vector3 impactForce = default)
        {
            if (isRagdollActive) return;
            isRagdollActive = true;

            animator.enabled = false;

            if (mainCollider != null)
                mainCollider.enabled = false;

            SetRagdollActive(true);

            // Apply death force to ragdoll
            Vector3 force = impactForce != default
                ? impactForce
                : deathForceDirection.normalized * deathImpulseForce;

            if (force != Vector3.zero)
            {
                Rigidbody hipsRb = FindHipsRigidbody();
                if (hipsRb != null)
                    hipsRb.AddForce(force, ForceMode.Impulse);
            }

            if (disappearDelay > 0f)
                Invoke(nameof(StartDisappear), disappearDelay);
        }

        /// <summary>Disable ragdoll and return control to Animator (for get-up animations)</summary>
        public void DisableRagdoll()
        {
            if (!isRagdollActive) return;
            isRagdollActive = false;

            SetRagdollActive(false);
            animator.enabled = true;

            if (mainCollider != null)
                mainCollider.enabled = true;
        }

        #endregion

        #region Private Helpers

        /// <summary>Toggle physics on all ragdoll bones</summary>
        private void SetRagdollActive(bool active)
        {
            foreach (Rigidbody rb in ragdollBodies)
            {
                rb.isKinematic = !active;
                rb.useGravity = active;
            }

            foreach (Collider col in ragdollColliders)
            {
                if (col == mainCollider) continue;
                col.enabled = active;
            }
        }

        /// <summary>Find Hips rigidbody by name (common ragdoll bone)</summary>
        private Rigidbody FindHipsRigidbody()
        {
            foreach (Rigidbody rb in ragdollBodies)
            {
                if (rb.name.ToLower().Contains("hips") || rb.name.ToLower().Contains("pelvis"))
                    return rb;
            }

            // Fallback to first rigidbody if Hips not found
            return ragdollBodies.Length > 0 ? ragdollBodies[0] : null;
        }

        private void StartDisappear()
        {
            StartCoroutine(FadeAndDestroy());
        }

        private IEnumerator FadeAndDestroy()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            Material[][] materials = new Material[renderers.Length][];

            for (int i = 0; i < renderers.Length; i++)
                materials[i] = renderers[i].materials;

            const float fadeDuration = 2f;
            float elapsed = 0f;

            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                float alpha = Mathf.Lerp(1f, 0f, elapsed / fadeDuration);

                for (int i = 0; i < renderers.Length; i++)
                {
                    foreach (Material mat in materials[i])
                    {
                        // Note: Requires URP shader with Transparent surface type
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

            // Network despawn if on server, otherwise local destroy
            if (IsServer && TryGetComponent<NetworkObject>(out var netObj))
                netObj.Despawn();
            else if (!IsServer)
                Destroy(gameObject);
        }

        #endregion
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;

namespace NscGame.Enemy
{
    /// <summary>
    /// EnemyHealthUI - Handles the rendering and animation of the Enemy/Boss Health Bar.
    /// Positioned at the top-center of the screen.
    /// Features:
    /// - Smooth health value lerping.
    /// - Red/Yellow damage catch-up bar (secondary bar) for premium visual feedback.
    /// - Auto-finds active EnemyHealth in the scene.
    /// - Fade-in when boss is active, fade-out on death.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class EnemyHealthUI : MonoBehaviour
    {
        [Header("Target Enemy")]
        [Tooltip("Leave empty to automatically find the EnemyHealth component in the scene.")]
        [SerializeField] private EnemyHealth targetEnemy;

        [Header("UI References")]
        [SerializeField] private Slider mainHealthSlider;
        [SerializeField] private Slider catchUpHealthSlider;
        [SerializeField] private TextMeshProUGUI bossNameText;
        [SerializeField] private TextMeshProUGUI hpValueText;

        [Header("Visual Settings")]
        [SerializeField] private string defaultBossName = "BOSS";
        [SerializeField] private float fadeDuration = 0.5f;
        [SerializeField] private float smoothSpeed = 10f;
        [SerializeField] private float catchUpDelay = 0.5f;
        [SerializeField] private float catchUpSpeed = 5f;

        private CanvasGroup canvasGroup;
        private float targetHpPercent = 0f;
        private float currentHpPercent = 0f;
        private float catchUpHpPercent = 0f;
        private float lastDamageTime = 0f;
        private bool isBossActive = false;

        private LobbyManager lobby;
        private float nextLobbySearchTime;

        private void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            // Start invisible
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        private void Start()
        {
            if (targetEnemy == null)
            {
                FindEnemyHealth();
            }

            if (targetEnemy != null && ShouldShow())
            {
                InitializeUI();
            }
        }

        private bool ShouldShow()
        {
            if (lobby == null && Time.unscaledTime >= nextLobbySearchTime)
            {
                nextLobbySearchTime = Time.unscaledTime + 1f;
                lobby = FindFirstObjectByType<LobbyManager>(FindObjectsInactive.Include);
            }

            // scene นี้ไม่มี lobby (เข้าเกมตรงๆ) → ไม่มีอะไรต้องรอ
            if (lobby == null)
                return true;

            // ไม่มี NetworkManager เลย = เทส offline ล้วนๆ → โชว์
            // มี NetworkManager แต่ยังไม่ host/join = ยังอยู่หน้าเมนู → ซ่อนรอ
            if (Unity.Netcode.NetworkManager.Singleton == null)
                return true;
            if (!Unity.Netcode.NetworkManager.Singleton.IsListening)
                return false;

            return lobby.GameStarted;
        }

        private void FindEnemyHealth()
        {
            targetEnemy = FindFirstObjectByType<EnemyHealth>();
        }

        private void InitializeUI()
        {
            float initialHp = targetEnemy.CurrentHp.Value;
            float maxHp = targetEnemy.GetMaxHp();
            float initialPercent = maxHp > 0 ? initialHp / maxHp : 0f;

            currentHpPercent = initialPercent;
            catchUpHpPercent = initialPercent;
            targetHpPercent = initialPercent;

            if (mainHealthSlider != null) mainHealthSlider.value = initialPercent;
            if (catchUpHealthSlider != null) catchUpHealthSlider.value = initialPercent;

            if (bossNameText != null) bossNameText.text = defaultBossName;
            UpdateHpText(initialHp, maxHp);

            isBossActive = true;
            StopAllCoroutines();
            StartCoroutine(FadeCanvasGroup(1f));
        }

        private void Update()
        {
            // Check if game has started
            if (!ShouldShow())
            {
                if (isBossActive)
                {
                    isBossActive = false;
                    StopAllCoroutines();
                    StartCoroutine(FadeCanvasGroup(0f));
                }
                return;
            }

            // Try to find enemy if not assigned yet
            if (targetEnemy == null)
            {
                FindEnemyHealth();
                if (targetEnemy != null)
                {
                    InitializeUI();
                }
                else
                {
                    if (isBossActive)
                    {
                        isBossActive = false;
                        StopAllCoroutines();
                        StartCoroutine(FadeCanvasGroup(0f));
                    }
                    return;
                }
            }

            // If we have an enemy but UI is not marked active yet (e.g. game just started)
            if (!isBossActive && !targetEnemy.IsDead())
            {
                InitializeUI();
            }

            // Sync values from EnemyHealth
            float currentHp = targetEnemy.CurrentHp.Value;
            float maxHp = targetEnemy.GetMaxHp();
            float hpPercent = maxHp > 0 ? currentHp / maxHp : 0f;

            // Detect taking damage to trigger catch-up delay
            if (hpPercent < targetHpPercent)
            {
                lastDamageTime = Time.time;
            }

            targetHpPercent = hpPercent;

            // Interpolate main health bar
            currentHpPercent = Mathf.Lerp(currentHpPercent, targetHpPercent, Time.deltaTime * smoothSpeed);
            if (mainHealthSlider != null)
            {
                mainHealthSlider.value = currentHpPercent;
            }

            // Catch-up bar lag effect
            if (Time.time > lastDamageTime + catchUpDelay)
            {
                catchUpHpPercent = Mathf.Lerp(catchUpHpPercent, targetHpPercent, Time.deltaTime * catchUpSpeed);
            }
            if (catchUpHealthSlider != null)
            {
                catchUpHealthSlider.value = catchUpHpPercent;
            }

            // Update text values
            UpdateHpText(currentHp, maxHp);

            // Manage visibility based on alive/dead state
            if (targetEnemy.IsDead() && isBossActive)
            {
                isBossActive = false;
                StopAllCoroutines();
                StartCoroutine(FadeCanvasGroup(0f));
            }
        }

        private void UpdateHpText(float current, float max)
        {
            if (hpValueText != null)
            {
                hpValueText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";
            }
        }

        private IEnumerator FadeCanvasGroup(float targetAlpha)
        {
            float startAlpha = canvasGroup.alpha;
            float elapsed = 0f;

            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / fadeDuration);
                yield return null;
            }

            canvasGroup.alpha = targetAlpha;
            canvasGroup.blocksRaycasts = targetAlpha > 0.5f;
            canvasGroup.interactable = targetAlpha > 0.5f;
        }

        /// <summary>
        /// Manually set target enemy (e.g. if spawned dynamically at runtime)
        /// </summary>
        public void SetTargetEnemy(EnemyHealth enemy)
        {
            targetEnemy = enemy;
            if (targetEnemy != null)
            {
                InitializeUI();
            }
        }
    }
}

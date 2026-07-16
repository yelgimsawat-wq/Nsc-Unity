using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// TutorialManager.cs
/// Displays skippable tutorial hints in the bottom-right corner of the screen.
/// Activated by LobbyManager.StartTutorial() when the host presses the Start button.
///
/// Setup (Unity Inspector):
///   1. Create a new UI Canvas (Screen Space - Overlay, Sort Order high enough to be on top)
///   2. Attach this component to a new empty GameObject inside the Canvas
///   3. Create a Panel child → assign to tutorialPanel
///   4. Create a TextMeshProUGUI child inside the panel → assign to stepText
///   5. Create a TextMeshProUGUI child inside the panel → assign to counterText
///   6. Create a Button child inside the panel → assign to skipButton
/// </summary>
public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance { get; private set; }

    // ============================================================
    //  Inspector References
    // ============================================================
    [Header("UI References")]
    [Tooltip("Root panel that contains all tutorial UI elements")]
    [SerializeField] private CanvasGroup tutorialPanel;

    [Tooltip("Main text that shows the current tutorial hint")]
    [SerializeField] private TextMeshProUGUI stepText;

    [Tooltip("Small text showing e.g. '1 / 6'")]
    [SerializeField] private TextMeshProUGUI counterText;

    [Tooltip("The skip / next button")]
    [SerializeField] private Button skipButton;

    [Tooltip("Label on the skip button")]
    [SerializeField] private TextMeshProUGUI skipButtonText;

    [Tooltip("Optional button to toggle the tutorial panel open/closed")]
    [SerializeField] private Button toggleButton;

    [Tooltip("Optional button inside the tutorial panel to close/hide it")]
    [SerializeField] private Button closeButton;

    // ============================================================
    //  Tuning
    // ============================================================
    [Header("Timing")]
    [Tooltip("Fade in/out duration for the panel")]
    [SerializeField] private float fadeDuration = 0.35f;

    [Tooltip("Pause (seconds) before the very first step appears after game starts")]
    [SerializeField] private float startDelay = 1.5f;

    [Header("Keyboard Controls")]
    [Tooltip("Key to toggle visibility of the tutorial panel")]
    [SerializeField] private KeyCode toggleKey = KeyCode.H;

    // ============================================================
    //  Tutorial Steps  (edit freely)
    // ============================================================
    public enum LimbRole { Unknown, Arm, Leg }

    // Set by LobbyManager (or auto-detect) before StartTutorial() is called
    [HideInInspector] public LimbRole playerRole = LimbRole.Unknown;

    // ---- SHARED steps (camera, shown to everyone) ----
    private static readonly string[] SharedSteps = new string[]
    {
        "Welcome!\nThis is a physics-based robot brawler.\nEach player controls one limb of a shared robot.",
        "CAMERA  •  Hold Right Mouse Button and drag\nto orbit the camera around your limb.",
        "CAMERA  •  Scroll the Mouse Wheel\nto zoom in and out.",
    };

    // ---- ARM-specific steps ----
    private static readonly string[] ArmSteps = new string[]
    {
        "ARM — AIM  •  Move your mouse to aim your hand.",
        "ARM — PUNCH  •  Hold Left Shift to punch.",
        "ARM — HEIGHT  •  Hold W to raise your hand up.\nHold S to push your hand down.",
        "ARM — GRAB  •  Press F near an object to grab it.\nPress F again to release the grab.",
        "ARM — RECOVERY  •  If the torso falls over,\npress Q to push yourself back up.",
        "You're ready!\nWork with your teammates — or don't.\nGood luck!",
    };

    // ---- LEG-specific steps ----
    private static readonly string[] LegSteps = new string[]
    {
        "LEG — BALANCE  •  Move your mouse to shift\nyour robot's weight and steer its walk.",
        "LEG — KICK  •  Hold Left Mouse Button\nto lift and swing your leg at enemies.",
        "LEG — HEIGHT  •  While holding Left Mouse Button,\npress W to raise your foot or S to lower it.",
        "LEG — WARNING  •  Do NOT lift both legs at the same time!\nThe robot will lose balance and fall.",
        "LEG — JUMP  •  Press Space while grounded\nto jump. Release Left Mouse Button first.",
        "LEG — RECOVERY  •  If the torso falls,\nplace your foot on the ground and hold Q to stand up.",
        "You're ready!\nKick hard and keep the robot balanced.\nGood luck!",
    };

    // Built at runtime from the above arrays
    private string[] _tutorialSteps;

    // ============================================================
    //  Private State
    // ============================================================
    private int   _currentStep  = 0;
    private bool  _isRunning    = false;
    private Coroutine _startDelayCoroutine;

    // ============================================================
    //  Unity Lifecycle
    // ============================================================
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Start hidden
        if (tutorialPanel != null)
        {
            tutorialPanel.alpha          = 0f;
            tutorialPanel.interactable   = false;
            tutorialPanel.blocksRaycasts = false;
        }

        if (skipButton != null)
            skipButton.onClick.AddListener(OnSkipClicked);

        if (toggleButton != null)
            toggleButton.onClick.AddListener(ToggleTutorial);

        if (closeButton != null)
            closeButton.onClick.AddListener(CloseTutorial);
    }

    // ============================================================
    //  Public API — called by LobbyManager
    // ============================================================

    /// <summary>
    /// Starts the tutorial sequence.  Call this from LobbyManager after the
    /// game begins (i.e. at the end of AssignLimbsThenUnfreeze).
    /// </summary>
    public void StartTutorial()
    {
        if (_startDelayCoroutine != null)
            StopCoroutine(_startDelayCoroutine);

        _startDelayCoroutine = StartCoroutine(RunTutorialDelayed());
    }

    /// <summary>
    /// Tell the tutorial which limb role this player has so the correct
    /// control hints are shown.  Call before StartTutorial().
    /// </summary>
    public void SetRole(LimbRole role)
    {
        playerRole = role;
    }

    /// <summary>Immediately hides and stops the tutorial.</summary>
    public void StopTutorial()
    {
        if (_startDelayCoroutine != null)
            StopCoroutine(_startDelayCoroutine);
        _isRunning = false;
        HidePanel();
    }

    /// <summary>
    /// Toggles the tutorial panel. If not running, it starts it.
    /// If running, it toggles visibility.
    /// </summary>
    public void ToggleTutorial()
    {
        if (!_isRunning)
        {
            StartTutorial();
            return;
        }

        if (tutorialPanel != null)
        {
            bool isVisible = tutorialPanel.alpha > 0.5f;
            if (isVisible)
            {
                HidePanel();
            }
            else
            {
                ShowPanel();
            }
        }
    }

    /// <summary>Closes (hides) the tutorial panel.</summary>
    public void CloseTutorial()
    {
        HidePanel();
    }

    // ============================================================
    //  Update loop for Key detection
    // ============================================================
    private void Update()
    {
        // Toggle panel with toggleKey (default H)
        if (Input.GetKeyDown(toggleKey))
        {
            ToggleTutorial();
        }

        // Support K key to advance when panel is open
        if (_isRunning && tutorialPanel != null && tutorialPanel.alpha > 0.5f && Input.GetKeyDown(KeyCode.K))
        {
            OnSkipClicked();
        }
    }

    // ============================================================
    //  Internal Initialization
    // ============================================================
    private IEnumerator RunTutorialDelayed()
    {
        _isRunning    = true;
        _currentStep  = 0;

        // Build the step list based on the player's limb role
        string[] roleSteps = playerRole == LimbRole.Leg ? LegSteps : ArmSteps;
        var combined = new System.Collections.Generic.List<string>(SharedSteps);
        combined.AddRange(roleSteps);
        _tutorialSteps = combined.ToArray();

        yield return new WaitForSeconds(startDelay);

        ShowPanel();
        RefreshUI();
    }

    // ============================================================
    //  Button Callback
    // ============================================================
    private void OnSkipClicked()
    {
        if (!_isRunning) return;

        _currentStep++;

        if (_currentStep >= _tutorialSteps.Length)
        {
            HidePanel();
            _isRunning = false;
        }
        else
        {
            RefreshUI();
        }
    }

    // ============================================================
    //  UI Helpers
    // ============================================================
    private void RefreshUI()
    {
        if (_tutorialSteps == null || _tutorialSteps.Length == 0) return;
        if (stepText    != null) stepText.text    = _tutorialSteps[_currentStep];
        if (counterText != null) counterText.text = $"{_currentStep + 1} / {_tutorialSteps.Length}";

        bool isLast = _currentStep >= _tutorialSteps.Length - 1;
        if (skipButtonText != null)
            skipButtonText.text = isLast ? "Got it! (K)" : "Skip (K)  ›";
    }

    private void ShowPanel()
    {
        if (tutorialPanel == null) return;
        tutorialPanel.interactable   = true;
        tutorialPanel.blocksRaycasts = true;
        tutorialPanel.DOFade(1f, fadeDuration).SetEase(Ease.OutCubic);
    }

    private void HidePanel()
    {
        if (tutorialPanel == null) return;
        tutorialPanel.DOFade(0f, fadeDuration)
            .SetEase(Ease.InCubic)
            .OnComplete(() =>
            {
                tutorialPanel.interactable   = false;
                tutorialPanel.blocksRaycasts = false;
            });
    }
}

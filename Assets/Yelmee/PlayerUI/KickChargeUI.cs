using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Segmented Charge Kick meter shown only to the player who owns a leg.
/// The visible maximum shrinks with that leg's HP, matching its real kick power.
/// </summary>
public class KickChargeUI : MonoBehaviour
{
    [SerializeField] private LocalRobotBinder binder;

    [Tooltip("All meter visuals. Hidden when this player does not own a leg.")]
    [SerializeField] private GameObject content;

    [SerializeField] private Image[] segments;

    [Header("Colors")]
    [SerializeField] private Color filledColor = new Color(1f, 0.72f, 0.2f, 1f);
    [SerializeField] private Color emptyColor = new Color(1f, 1f, 1f, 0.16f);

    private PlayerLegCombat ownedLeg;
    private int litSegments = -1;

    private void Awake()
    {
        if (binder == null)
            binder = GetComponentInParent<LocalRobotBinder>();
    }

    private void OnEnable()
    {
        if (binder == null)
            return;

        binder.OnBound += HandleBound;

        if (binder.IsBound)
            HandleBound();
    }

    private void OnDisable()
    {
        if (binder != null)
            binder.OnBound -= HandleBound;
    }

    private void HandleBound()
    {
        ownedLeg = binder.OwnedLeg;

        // Arm players do not need the Charge Kick meter.
        if (content != null)
            content.SetActive(ownedLeg != null);

        litSegments = -1;
        ApplyCharge(0f);
    }

    private void Update()
    {
        if (ownedLeg == null)
            return;

        ApplyCharge(ownedLeg.EffectiveNormalizedKickCharge);
    }

    private void ApplyCharge(float normalizedCharge)
    {
        if (segments == null || segments.Length == 0)
            return;

        int lit = normalizedCharge <= 0f
            ? 0
            : Mathf.Clamp(Mathf.CeilToInt(normalizedCharge * segments.Length), 0, segments.Length);

        if (lit == litSegments)
            return;

        litSegments = lit;

        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] != null)
                segments[i].color = i < lit ? filledColor : emptyColor;
        }
    }
}

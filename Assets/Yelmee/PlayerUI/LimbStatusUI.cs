using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// การ์ดสถานะแขนขา 1 ใบ (ขวาล่าง มี 4 ใบ: L-ARM / R-ARM / L-LEG / R-LEG)
/// เขียว = ต่ออยู่, แดง + กระพริบ = หลุด — สถานะ sync จาก Server เห็นตรงกันทุกคน
/// ป้ายชิ้นที่ "เราคุม" เป็นสีฟ้าให้รู้ว่าใบไหนของเรา
/// </summary>
public class LimbStatusUI : MonoBehaviour
{
    public enum LimbSlot { LeftArm = 0, RightArm = 1, LeftLeg = 2, RightLeg = 3 }

    [SerializeField] private LocalRobotBinder binder;
    [SerializeField] private LimbSlot slot;

    [Header("Visuals")]
    [SerializeField] private Image borderImage;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image statusDot;
    [SerializeField] private TMP_Text labelText;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Attached Colors")]
    [SerializeField] private Color attachedBorder = new Color(1f, 1f, 1f, 0.15f);
    [SerializeField] private Color attachedBackground = new Color(0.05f, 0.09f, 0.13f, 0.72f);
    [SerializeField] private Color attachedDot = new Color(0.3f, 0.9f, 0.45f, 1f);
    [SerializeField] private Color attachedLabel = new Color(0.75f, 0.82f, 0.88f, 0.9f);

    [Header("Detached Colors")]
    [SerializeField] private Color detachedBorder = new Color(1f, 0.25f, 0.2f, 0.9f);
    [SerializeField] private Color detachedBackground = new Color(0.25f, 0.03f, 0.05f, 0.85f);
    [SerializeField] private Color detachedDot = new Color(1f, 0.25f, 0.2f, 1f);
    [SerializeField] private Color detachedLabel = new Color(1f, 0.6f, 0.58f, 1f);

    [Header("Owned Highlight")]
    [Tooltip("สีป้ายของชิ้นที่ผู้เล่นคนนี้คุม (ตอนต่ออยู่) ให้รู้ว่าการ์ดใบไหนของเรา")]
    [SerializeField] private Color ownedLabel = new Color(0.35f, 0.85f, 1f, 1f);

    private JointPullAndReconnect joint;
    private Tween pulseTween;

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

        DetachJoint();
        StopPulse();
    }

    private void HandleBound()
    {
        DetachJoint();

        joint = JointForSlot();
        if (joint != null)
            joint.OnConnectionStateChanged += OnConnectionChanged;

        Apply(joint == null || joint.IsConnected);
    }

    private JointPullAndReconnect JointForSlot()
    {
        if (binder == null)
            return null;

        return slot switch
        {
            LimbSlot.LeftArm => binder.LeftArmJoint,
            LimbSlot.RightArm => binder.RightArmJoint,
            LimbSlot.LeftLeg => binder.LeftLegJoint,
            LimbSlot.RightLeg => binder.RightLegJoint,
            _ => null
        };
    }

    private void DetachJoint()
    {
        if (joint != null)
            joint.OnConnectionStateChanged -= OnConnectionChanged;

        joint = null;
    }

    private void OnConnectionChanged(bool connected)
    {
        Apply(connected);
    }

    private void Apply(bool connected)
    {
        bool isOwnLimb = binder != null && joint != null && binder.OwnedJoint == joint;

        if (borderImage != null)
            borderImage.color = connected ? attachedBorder : detachedBorder;
        if (backgroundImage != null)
            backgroundImage.color = connected ? attachedBackground : detachedBackground;
        if (statusDot != null)
            statusDot.color = connected ? attachedDot : detachedDot;
        if (labelText != null)
            labelText.color = connected
                ? (isOwnLimb ? ownedLabel : attachedLabel)
                : detachedLabel;

        if (connected)
            StopPulse();
        else
            StartPulse();
    }

    private void StartPulse()
    {
        StopPulse();

        if (canvasGroup == null)
            return;

        canvasGroup.alpha = 1f;
        pulseTween = canvasGroup
            .DOFade(0.45f, 0.5f)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo)
            .SetUpdate(true);
    }

    private void StopPulse()
    {
        pulseTween?.Kill();
        pulseTween = null;

        if (canvasGroup != null)
            canvasGroup.alpha = 1f;
    }

    private void OnDestroy()
    {
        StopPulse();
    }
}

using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// กล่องข้อความกลางล่าง — บอกสถานะและปุ่มแก้:
///   Priority 1: ตัวล้ม (Ragdoll/Falling) → "กด [Q] ค้าง · พยุงตัวขึ้น" (เห็นทุกคน ช่วยกันกดลุกได้)
///   Priority 2: ชิ้นส่วน "ที่เราคุม" หลุดและพร้อมดึง → "กด [R] ค้าง · ดึงชิ้นส่วนกลับ"
///               (เห็นเฉพาะเจ้าของชิ้นที่หลุด — เพื่อนร่วมหุ่นไม่เห็น)
/// </summary>
public class StatusPromptUI : MonoBehaviour
{
    private enum PromptMode { Hidden, Recovery, Pull }

    [SerializeField] private LocalRobotBinder binder;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TMP_Text keyText;
    [SerializeField] private TMP_Text instructionText;
    [SerializeField] private float fadeDuration = 0.2f;

    [Header("Messages")]
    [SerializeField] private string recoveryKey = "Q";
    [SerializeField] private string recoveryInstruction = "· GET UP";
    [SerializeField] private string pullInstruction = "· PULL LIMB BACK";

    private PromptMode mode = PromptMode.Hidden;
    private Tween fadeTween;

    private void Awake()
    {
        if (binder == null)
            binder = GetComponentInParent<LocalRobotBinder>();

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }

    private void Update()
    {
        // Poll ทุกเฟรม — สถานะพร้อมดึง (cooldown หมด) ไม่มี event ให้ฟัง
        PromptMode desired = ComputeMode();
        if (desired == mode)
            return;

        mode = desired;

        switch (mode)
        {
            case PromptMode.Recovery:
                SetContent(recoveryKey, recoveryInstruction);
                FadeTo(1f);
                break;

            case PromptMode.Pull:
                JointPullAndReconnect joint = binder != null ? binder.OwnedJoint : null;
                SetContent(joint != null ? joint.PullKeyLabel : "R", pullInstruction);
                FadeTo(1f);
                break;

            default:
                FadeTo(0f);
                break;
        }
    }

    private PromptMode ComputeMode()
    {
        if (binder == null || !binder.IsBound)
            return PromptMode.Hidden;

        // Priority 1: ชิ้นที่ "เราคุม" หลุดและพร้อมดึง — เห็นเฉพาะเจ้าของ
        // มาก่อนเรื่องล้ม เพราะขาหลุดจะสั่งล้มอัตโนมัติ และการดึงชิ้นกลับ
        // ทำได้ระหว่างนอน (ต้องดึงก่อนค่อยลุก ไม่งั้นลุกไปตัวก็ลอยเพราะขาไม่ครบ)
        JointPullAndReconnect ownedJoint = binder.OwnedJoint;
        if (ownedJoint != null && ownedJoint.IsPullReady)
            return PromptMode.Pull;

        // Priority 2: ตัวล้ม — ทุกคนเห็นและช่วยกด Q ลุกได้
        TorsoMovement torso = binder.Torso;
        if (torso != null)
        {
            TorsoMovement.TorsoState state = torso.currentState.Value;
            if (state == TorsoMovement.TorsoState.Ragdoll ||
                state == TorsoMovement.TorsoState.Falling)
            {
                return PromptMode.Recovery;
            }
        }

        return PromptMode.Hidden;
    }

    private void SetContent(string key, string instruction)
    {
        if (keyText != null)
            keyText.text = key;

        if (instructionText != null)
            instructionText.text = instruction;
    }

    private void FadeTo(float targetAlpha)
    {
        if (canvasGroup == null)
            return;

        fadeTween?.Kill();
        fadeTween = canvasGroup
            .DOFade(targetAlpha, fadeDuration)
            .SetEase(Ease.OutCubic)
            .SetUpdate(true);
    }

    private void OnDestroy()
    {
        fadeTween?.Kill();
    }
}

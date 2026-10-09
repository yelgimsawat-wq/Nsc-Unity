using System;
using System.Collections.Generic;
using Nsc.Limbs;
using Nsc.Match;
using Nsc.Robots;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// หัวใจของ Player HUD: บอก widget ทุกตัวว่า "หุ่นของเรา" คือตัวไหน และเราคุมชิ้นไหน
///
/// ข้อมูลมาจาก LimbControlBinder (ผูกตอนแมตช์เริ่มตามที่จองในลอบบี้) — ไม่ต้องเดาจาก ownership
/// หรือชื่อ GameObject แล้ว (Host เป็นเจ้าของชิ้นที่ไม่มีใครเลือก เดาจาก ownership จะได้หุ่น/ชิ้นผิด)
/// ฉากเทสที่ไม่มีลอบบี้: ใช้หุ่นตัวแรก และชิ้นแรกที่เครื่องนี้เป็นเจ้าของ
/// </summary>
public class LocalRobotBinder : MonoBehaviour
{
    [SerializeField, Min(0.1f)]
    [Tooltip("ฉากที่ไม่มีลอบบี้: ความถี่ในการลองหาหุ่นของเรา (วินาที)")]
    private float bindRetryInterval = 0.5f;

    /// <summary>ยิงทุกครั้งที่ bind สำเร็จ (รวมการ bind ใหม่หลังต่อกลับ)</summary>
    public event Action OnBound;

    public bool IsBound { get; private set; }
    public Robot Robot { get; private set; }
    public Transform RobotRoot => Robot != null ? Robot.Root : null;
    public TorsoBalance Torso => Robot != null ? Robot.Torso : null;

    /// <summary>ชิ้นที่ผู้เล่นคนนี้คุม — null ถ้าไม่ได้คุมชิ้นไหน (คนดู/Host ที่ไม่ได้เลือก)</summary>
    public RobotLimb OwnedLimb { get; private set; }

    /// <summary>แขนที่เราคุม — null ถ้าคุมขา</summary>
    public ArmController OwnedHand { get; private set; }

    /// <summary>ขาที่เราคุม (input ฝั่งเรา มีเกจชาร์จเตะ) — null ถ้าคุมแขน</summary>
    public LegInput OwnedLeg { get; private set; }

    public LimbHealth OwnedLimbHealth => OwnedHealths.Count > 0 ? OwnedHealths[0] : null;

    /// <summary>
    /// เลือดของ "ทุกชิ้นที่เราคุม" — เล่นจริงคุมชิ้นเดียว แต่ตอนเทสคนเดียว (Host คุมครบ 4 ชิ้น)
    /// HUD ต้องเห็นดาเมจของทุกชิ้น ชิ้นที่เลือกเล่นอยู่ต้นลิสต์เสมอ
    /// </summary>
    public readonly List<LimbHealth> OwnedHealths = new List<LimbHealth>();

    private LimbControlBinder controlBinder;
    private float nextFallbackAttempt;

    private void Start()
    {
        controlBinder = LimbControlBinder.Current;
        if (controlBinder == null) return;

        controlBinder.Bound += OnControlBound;
        if (controlBinder.IsBound) OnControlBound(controlBinder.LocalRobot, controlBinder.LocalSlot);
    }

    private void OnDestroy()
    {
        if (controlBinder != null) controlBinder.Bound -= OnControlBound;
    }

    private void Update()
    {
        // ฉากที่มีลอบบี้ รอ LimbControlBinder อย่างเดียว
        if (controlBinder != null || IsBound || Time.unscaledTime < nextFallbackAttempt) return;
        nextFallbackAttempt = Time.unscaledTime + bindRetryInterval;
        TryBindFallback();
    }

    private void OnControlBound(Robot robot, LimbSlot slot) => Bind(robot, robot.GetLimb(slot));

    /// <summary>ฉากเทส: หุ่นตัวแรก / ชิ้นแรกที่เครื่องนี้เป็นเจ้าของ (ไม่ต่อ network = ชิ้นแรกที่มี)</summary>
    private void TryBindFallback()
    {
        bool networkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        foreach (Robot robot in Robot.All)
        {
            foreach (LimbSlot slot in LimbSlots.All)
            {
                RobotLimb limb = robot.GetLimb(slot);
                if (limb == null || limb.Controller == null) continue;
                if (networkActive && (!limb.Controller.IsSpawned || !limb.Controller.IsOwner)) continue;

                Bind(robot, limb);
                return;
            }
        }
    }

    private void Bind(Robot robot, RobotLimb ownedLimb)
    {
        if (robot == null) return;

        Robot = robot;
        OwnedLimb = ownedLimb;
        OwnedHand = ownedLimb != null ? ownedLimb.Controller as ArmController : null;
        OwnedLeg = ownedLimb != null ? ownedLimb.GetComponent<LegInput>() : null;

        OwnedHealths.Clear();
        AddHealth(ownedLimb);
        foreach (LimbSlot slot in LimbSlots.All)
        {
            RobotLimb limb = robot.GetLimb(slot);
            if (limb != null && limb.Controller != null && (!limb.Controller.IsSpawned || limb.Controller.IsOwner))
                AddHealth(limb);
        }

        // ไม่ได้คุมชิ้นไหนเลย (Host ที่เป็นแค่คนเปิดห้อง) — ใช้แขนซ้ายให้มีข้อมูลโชว์
        if (OwnedHealths.Count == 0) AddHealth(robot.GetLimb(LimbSlot.LeftArm));

        IsBound = true;
        Debug.Log($"[PlayerHUD] bound to '{(robot.Root != null ? robot.Root.name : robot.name)}' | " +
                  $"owned limb: {(ownedLimb != null ? ownedLimb.Slot.ToString() : "none")}", this);
        OnBound?.Invoke();
    }

    private void AddHealth(RobotLimb limb)
    {
        if (limb != null && limb.Health != null && !OwnedHealths.Contains(limb.Health))
            OwnedHealths.Add(limb.Health);
    }

    public RobotLimb GetLimb(LimbSlot slot) => Robot != null ? Robot.GetLimb(slot) : null;

    /// <summary>ชื่อย่อของชิ้นส่วนบน HUD (L-ARM / R-ARM / L-LEG / R-LEG)</summary>
    public string GetLimbLabel(Component limbComponent)
    {
        RobotLimb limb = limbComponent != null ? limbComponent.GetComponent<RobotLimb>() : null;
        return limb != null && limb.Owner == Robot ? limb.Slot.ShortLabel() : string.Empty;
    }
}

using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerFootForRobot), typeof(Rigidbody))]
public class PlayerLegCombat : NetworkBehaviour
{
    public enum LegActionState : byte
    {
        Idle,
        Charging,
        Kicking,
        Recovering
    }

    [Header("Input")]
    [SerializeField] private KeyCode kickKey = KeyCode.LeftShift;

    [Header("Charge Kick")]
    [Min(0.05f)]
    [SerializeField] private float fullChargeDuration = 1f;
    [Range(0f, 1f)]
    [SerializeField] private float minimumCharge = 0.12f;
    // เดิม 8-24 m/s / accel 120 — ตัวเลขนี้จูนมาสำหรับหุ่นสเกลเล็ก (maxLegLength ~1.5)
    // แต่หุ่นจริงถูกสเกลใหญ่ขึ้น ~9 เท่า (maxLegLength = 14) โดยไม่มีใครมาปรับแรงเตะตาม
    // เทียบกับหมัด (maxPunchSpeed=55, punchAcceleration=400 ใน PlayerHandCombat) แล้วเตะเบากว่ามาก
    // ค่าใหม่: เตะเบาสุดยังแรงกว่าหมัดเบาๆ / ชาร์จเต็มแรงกว่าหมัดสุด (รางวัลของการรอชาร์จ)
    [Min(0f)]
    [SerializeField] private float minimumKickSpeed = 30f;
    [Min(0f)]
    [SerializeField] private float maximumKickSpeed = 70f;
    [Min(0f)]
    [SerializeField] private float kickAcceleration = 350f;
    [Min(0.05f)]
    [SerializeField] private float kickDuration = 0.3f;
    // ต้องพอให้เท้าเร่งถึงความเร็วสูงสุดได้ก่อนโดนตัดจบด้วยระยะ (ดูคอมเมนต์ด้านบน)
    [Min(0.1f)]
    [SerializeField] private float kickReach = 10f;
    [Min(0f)]
    [SerializeField] private float maxTorsoUpwardSpeedDuringKick = 0.75f;

    [Header("Return To Movement")]
    [Min(0f)]
    [SerializeField] private float recoveryDuration = 0.18f;
    // สปีคสูงสุดขึ้นเกือบ 3 เท่า — ต้องหน่วงแรงขึ้นตามเพื่อไม่ให้เท้าค้างไถลหลังเตะจบ
    [Min(0f)]
    [SerializeField] private float recoveryDamping = 14f;

    [Header("Damaged Leg Power")]
    [Tooltip("Kick power retained when this leg has almost no HP. The actual power scales from this value to 100% using current HP / original MaxHp.")]
    [Range(0f, 1f)]
    [SerializeField] private float minimumPowerAtZeroHealth = 0.3f;

    [Header("Debug")]
    [Tooltip("โชว์ log สรุปทุกครั้งที่เตะจบ (ความเร็วพีค/ดาเมจที่จะได้/โดนเป้าไหม) ไว้เช็คจูนค่า\n" +
             "สไตล์เดียวกับ debugPunchLog ของหมัด — เตะวืดก็ยังโชว์ ไม่งั้นวัดแรงเตะจริงไม่ได้เลย")]
    [SerializeField] private bool debugKickLog = true;

    public readonly NetworkVariable<LegActionState> currentAction = new NetworkVariable<LegActionState>(
        LegActionState.Idle,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public LegActionState CurrentAction => currentAction.Value;
    public bool IsCharging => localCharging || currentAction.Value == LegActionState.Charging;
    public bool IsKickMotionActive =>
        currentAction.Value == LegActionState.Kicking ||
        currentAction.Value == LegActionState.Recovering;
    public bool CanDealDamage =>
        currentAction.Value == LegActionState.Kicking &&
        !hasHitThisKick;
    public float PeakKickSpeed { get; private set; }

    public float HealthPowerMultiplier
    {
        get
        {
            if (legHealth == null)
                return 1f;

            float originalMaxHp = Mathf.Max(1f, legHealth.MaxHp);
            float hpRatio = Mathf.Clamp01(legHealth.currentHp.Value / originalMaxHp);
            return Mathf.Lerp(minimumPowerAtZeroHealth, 1f, hpRatio);
        }
    }

    public float NormalizedKickCharge
    {
        get
        {
            if (!localCharging)
                return 0f;

            return Mathf.Clamp01((Time.unscaledTime - localChargeStartedAt) /
                                 Mathf.Max(0.05f, fullChargeDuration));
        }
    }

    // The HUD shows real output, not just held time. A damaged leg therefore
    // cannot fill the meter as high as a healthy leg.
    public float EffectiveNormalizedKickCharge =>
        NormalizedKickCharge * HealthPowerMultiplier;

    private PlayerFootForRobot footController;
    private Rigidbody footRb;
    private RobotHealth legHealth;
    private JointPullAndReconnect reconnect;
    private LobbyManager limbSelectionLobby;
    private PhysicsDamageSender damageSender;

    private bool localCharging;
    private bool localLiftArmed;
    private bool kickNeedsMouseRelease;
    private float localChargeStartedAt;

    private float serverChargeStartedAt;
    private float actionTimer;
    private float activeKickSpeed;
    private Vector3 kickStartPosition;
    private Vector3 kickDirection;
    private bool hasHitThisKick;

    private void Awake()
    {
        footController = GetComponent<PlayerFootForRobot>();
        footRb = GetComponent<Rigidbody>();
        legHealth = GetComponent<RobotHealth>();
        reconnect = GetComponent<JointPullAndReconnect>();
        damageSender = GetComponent<PhysicsDamageSender>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
            ResetServerState();
    }

    public override void OnNetworkDespawn()
    {
        localCharging = false;
        localLiftArmed = false;
        kickNeedsMouseRelease = false;
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!IsOwner || footController == null)
            return;

        if (!CanReadCombatInputForThisLimb())
        {
            CancelLocalCharge();
            localLiftArmed = false;
            kickNeedsMouseRelease = false;
            return;
        }

        bool holdingLift = Input.GetMouseButton(0);
        if (!holdingLift)
        {
            if (localCharging)
                CancelLocalCharge();

            localLiftArmed = false;
            kickNeedsMouseRelease = false;
            return;
        }

        // Left Mouse must be pressed first. The first frame only starts the normal
        // foot lift/step; Shift becomes valid from a following frame onward.
        if (Input.GetMouseButtonDown(0))
        {
            localLiftArmed = false;
            return;
        }

        if (footController.isStepping)
            localLiftArmed = true;

        if (kickNeedsMouseRelease || !localLiftArmed)
            return;

        bool canUseKickInput = CanUseKickInputLocally();

        if (!localCharging && canUseKickInput && Input.GetKeyDown(kickKey))
        {
            localCharging = true;
            localChargeStartedAt = Time.unscaledTime;
            BeginChargeRpc();
        }

        if (!localCharging)
            return;

        if (!canUseKickInput)
        {
            CancelLocalCharge();
            return;
        }

        if (Input.GetKeyUp(kickKey))
        {
            Vector3 aimDirection = footController.GetKickAimDirection();
            localCharging = false;
            kickNeedsMouseRelease = true;
            ReleaseKickRpc(aimDirection);
        }
    }

    private bool CanReadCombatInputForThisLimb()
    {
        NetworkManager manager = NetworkManager.Singleton;

        // Scenes without a limb-selection lobby keep the existing ownership rule.
        if (manager == null || !manager.IsListening)
            return true;

        if (limbSelectionLobby == null)
        {
            limbSelectionLobby = LobbyManager.Instance;
            if (limbSelectionLobby == null)
            {
                limbSelectionLobby = FindFirstObjectByType<LobbyManager>(
                    FindObjectsInactive.Include);
            }
        }

        if (limbSelectionLobby == null)
            return true;

        return limbSelectionLobby.IsLimbSelectedByClient(
            gameObject,
            manager.LocalClientId);
    }

    private void FixedUpdate()
    {
        if (!IsServer || footController == null || footRb == null)
            return;

        if (!CanRemainInCurrentAction())
        {
            ResetServerState();
            return;
        }

        switch (currentAction.Value)
        {
            case LegActionState.Charging:
                break;

            case LegActionState.Kicking:
                actionTimer -= Time.fixedDeltaTime;
                footRb.isKinematic = false;

                Vector3 desiredVelocity = kickDirection * activeKickSpeed;
                footRb.linearVelocity = Vector3.MoveTowards(
                    footRb.linearVelocity,
                    desiredVelocity,
                    kickAcceleration * Time.fixedDeltaTime);

                BraceTorsoAgainstJump();

                float forwardSpeed = Mathf.Max(0f, Vector3.Dot(footRb.linearVelocity, kickDirection));
                PeakKickSpeed = Mathf.Max(PeakKickSpeed, forwardSpeed);
                float forwardDistance = Vector3.Dot(footRb.position - kickStartPosition, kickDirection);

                if (actionTimer <= 0f || forwardDistance >= kickReach)
                    BeginRecovery();
                break;

            case LegActionState.Recovering:
                actionTimer -= Time.fixedDeltaTime;
                footRb.isKinematic = false;
                footRb.AddForce(-footRb.linearVelocity * recoveryDamping, ForceMode.Acceleration);

                if (actionTimer <= 0f)
                    ResetServerState();
                break;
        }
    }

    private bool CanUseKickInputLocally()
    {
        if (GameFlowManager.GameEnded)
            return false;
        if (footController.useVirtualCursor && Cursor.lockState != CursorLockMode.Locked)
            return false;
        if (footController.currentState.Value != PlayerFootForRobot.FootState.Attached)
            return false;
        if (footController.torso != null &&
            footController.torso.currentState.Value != TorsoMovement.TorsoState.Standing)
            return false;
        if (!Input.GetMouseButton(0) || !footController.isStepping || footController.isJumping)
            return false;
        if (reconnect != null && !reconnect.IsConnected)
            return false;

        bool actionAcceptsInput =
            currentAction.Value == LegActionState.Idle ||
            (localCharging && currentAction.Value == LegActionState.Charging);

        return actionAcceptsInput;
    }

    private bool CanStartKickOnServer()
    {
        if (footController.currentState.Value != PlayerFootForRobot.FootState.Attached)
            return false;
        if (footController.torso != null &&
            footController.torso.currentState.Value != TorsoMovement.TorsoState.Standing)
            return false;
        if (footController.isJumping)
            return false;
        if (reconnect != null && !reconnect.IsConnected)
            return false;

        return true;
    }

    private bool CanRemainInCurrentAction()
    {
        if (currentAction.Value == LegActionState.Idle)
            return true;
        if (footController.currentState.Value != PlayerFootForRobot.FootState.Attached)
            return false;
        if (reconnect != null && !reconnect.IsConnected)
            return false;
        if (footController.torso != null &&
            footController.torso.currentState.Value != TorsoMovement.TorsoState.Standing)
            return false;

        return true;
    }

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable)]
    private void BeginChargeRpc()
    {
        if (currentAction.Value != LegActionState.Idle || !CanStartKickOnServer())
            return;

        // Local input already requires Left Mouse + a raised stepping foot.
        // Mark it raised server-side too so cross-component RPC ordering cannot
        // reject a valid kick on a higher-latency client.
        footController.isStepping = true;
        currentAction.Value = LegActionState.Charging;
        serverChargeStartedAt = Time.time;
        PeakKickSpeed = 0f;
        hasHitThisKick = false;
    }

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable)]
    private void ReleaseKickRpc(Vector3 requestedDirection)
    {
        if (currentAction.Value != LegActionState.Charging || !CanRemainInCurrentAction())
        {
            ResetServerState();
            return;
        }

        float heldDuration = Mathf.Max(0f, Time.time - serverChargeStartedAt);
        float charge = Mathf.Clamp01(heldDuration / Mathf.Max(0.05f, fullChargeDuration));
        charge = Mathf.Max(minimumCharge, charge);

        if (!IsFinite(requestedDirection) || requestedDirection.sqrMagnitude < 0.001f)
            requestedDirection = footController.GetKickAimDirection();

        requestedDirection.y = 0f;
        if (requestedDirection.sqrMagnitude < 0.001f)
            requestedDirection = footController.pivotPoint != null
                ? footController.pivotPoint.forward
                : transform.forward;
        requestedDirection.y = 0f;
        requestedDirection.Normalize();

        float chargedSpeed = Mathf.Lerp(minimumKickSpeed, maximumKickSpeed, charge);
        activeKickSpeed = chargedSpeed * HealthPowerMultiplier;
        kickDirection = requestedDirection;
        kickStartPosition = footRb.position;
        actionTimer = kickDuration;
        PeakKickSpeed = 0f;
        hasHitThisKick = false;
        currentAction.Value = LegActionState.Kicking;

        footRb.isKinematic = false;
    }

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable)]
    private void CancelChargeRpc()
    {
        if (currentAction.Value == LegActionState.Charging)
            ResetServerState();
    }

    private void CancelLocalCharge()
    {
        if (!localCharging)
            return;

        localCharging = false;
        CancelChargeRpc();
    }

    private void BraceTorsoAgainstJump()
    {
        if (footController.torso == null || footController.torso.torsoRb == null)
            return;

        Rigidbody torsoRb = footController.torso.torsoRb;
        Vector3 torsoVelocity = torsoRb.linearVelocity;
        if (torsoVelocity.y <= maxTorsoUpwardSpeedDuringKick)
            return;

        torsoVelocity.y = maxTorsoUpwardSpeedDuringKick;
        torsoRb.linearVelocity = torsoVelocity;
    }

    private void BeginRecovery()
    {
        // สรุปผลทุกครั้งที่เตะจบ — จุดนี้เป็นทางผ่านเดียวของทั้งเตะครบเวลา/ครบระยะ/ชนโดน/ชนกำแพง
        // เตะวืดก็ต้องโชว์ ไม่งั้นไม่มีทางรู้ว่าเท้าเร่งได้จริงกี่ m/s (log ชนโดนอยู่ที่ตัว DamageSender)
        if (debugKickLog)
        {
            string damageText = "?";
            if (damageSender != null)
            {
                damageText = PeakKickSpeed < damageSender.minVelocityThreshold
                    ? $"0 (พีคต่ำกว่าเกณฑ์ {damageSender.minVelocityThreshold})"
                    : $"{Mathf.Min(PeakKickSpeed * damageSender.kickSpeedToDamage, damageSender.maxDamagePerHit):F1}";
            }

            Debug.Log(
                $"🦵 [{gameObject.name}] เตะจบ | Peak: {PeakKickSpeed:F1} m/s | " +
                $"สั่งไป: {activeKickSpeed:F1} m/s | ดาเมจถ้าเข้าเป้า: {damageText} | " +
                $"โดนเป้า: {(hasHitThisKick ? "✅" : "❌ วืด")}");
        }

        currentAction.Value = LegActionState.Recovering;
        actionTimer = recoveryDuration;
    }

    public void NotifyKickImpact()
    {
        if (!IsServer || !CanDealDamage)
            return;

        hasHitThisKick = true;
        BeginRecovery();
    }

    public void NotifyKickBlocked()
    {
        if (!IsServer || currentAction.Value != LegActionState.Kicking)
            return;

        BeginRecovery();
    }

    public void ResetForRespawn()
    {
        localCharging = false;
        localLiftArmed = false;
        kickNeedsMouseRelease = false;

        if (IsServer)
            ResetServerState();
    }

    private void ResetServerState()
    {
        if (IsServer)
            currentAction.Value = LegActionState.Idle;

        actionTimer = 0f;
        activeKickSpeed = 0f;
        kickStartPosition = Vector3.zero;
        PeakKickSpeed = 0f;
        hasHitThisKick = false;
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}

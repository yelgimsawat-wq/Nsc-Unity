using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// หัวใจของ Player HUD: หา "หุ่นของเรา" จากแขนขาที่ client นี้เป็นเจ้าของ (ชิ้นเดียวพอ)
/// แล้ว expose reference ให้ widget ทุกตัวใช้ร่วมกัน
///
/// กติกาสำคัญ (จากดีไซน์เกม):
/// - หุ่น 1 ตัวถูกแบ่งคุมหลายคน — LobbyManager โอน ownership แขนขาทีละชิ้นให้คนละ client
///   ห้าม assume ว่าผู้เล่นเป็นเจ้าของครบทุกชิ้น
/// - ชิ้นส่วนที่หลุดอาจถูกย้าย parent ออกนอก RobotContainer
///   จึงจับคู่ชิ้นกับหุ่นผ่าน joint.targetBody (จุดที่มันต่ออยู่ ซึ่งไม่เคยหลุดจากหุ่น)
/// - รองรับทดสอบคนเดียวแบบไม่ต่อ network (ไม่มีอะไร spawn → ใช้หุ่นตัวแรกที่เจอ)
/// </summary>
public class LocalRobotBinder : MonoBehaviour
{
    [SerializeField, Min(0.1f)]
    [Tooltip("ความถี่ในการลองหาหุ่นของเรา (วินาที) จนกว่าจะเจอครบ — ownership ถูกโอนหลัง spawn")]
    private float bindRetryInterval = 0.5f;

    /// <summary>ยิงทุกครั้งที่ bind สำเร็จ (รวมถึงตอน rebind หลัง ownership เปลี่ยน)</summary>
    public event Action OnBound;

    public bool IsBound { get; private set; }
    public Transform RobotRoot { get; private set; }
    public TorsoMovement Torso { get; private set; }

    public JointPullAndReconnect LeftArmJoint { get; private set; }
    public JointPullAndReconnect RightArmJoint { get; private set; }
    public JointPullAndReconnect LeftLegJoint { get; private set; }
    public JointPullAndReconnect RightLegJoint { get; private set; }

    public PlayerHandCombat LeftHand { get; private set; }
    public PlayerHandCombat RightHand { get; private set; }

    /// <summary>มือที่เราคุม — null ถ้าผู้เล่นคนนี้คุมขา</summary>
    public PlayerHandCombat OwnedHand { get; private set; }

    /// <summary>ชิ้นส่วนชิ้นแรกที่เราเป็นเจ้าของ (แขนซ้าย → แขนขวา → ขาซ้าย → ขาขวา)</summary>
    public JointPullAndReconnect OwnedJoint { get; private set; }

    /// <summary>เลือดของชิ้นแรกที่เราคุม (สะดวกสำหรับ widget ที่ต้องการชิ้นเดียว)</summary>
    public RobotHealth OwnedLimbHealth { get; private set; }

    /// <summary>
    /// เลือดของ "ทุกชิ้นที่เราคุม" — ตอนเล่นจริงคุมคนละชิ้นจะมีตัวเดียว
    /// แต่ตอนเทสคนเดียว (Host คุมครบ 4 ชิ้น) HUL ต้องเห็นดาเมจของทุกชิ้น ไม่ใช่แค่แขนซ้าย
    /// </summary>
    public readonly List<RobotHealth> OwnedHealths = new List<RobotHealth>();

    private NetworkBehaviour anchor;
    private LobbyManager lobby;
    private float nextBindAttemptTime;
    private float nextDiagnosticTime;

    private void Update()
    {
        if (Time.unscaledTime >= nextBindAttemptTime && NeedsRebind())
            TryBind();
    }

    private bool NeedsRebind()
    {
        if (!IsBound || anchor == null || Torso == null ||
            LeftArmJoint == null || RightArmJoint == null ||
            LeftLegJoint == null || RightLegJoint == null)
        {
            return true;
        }

        // bind ไว้ตอน offline แล้ว network เพิ่งเปิด — ต้องหาหุ่นตัวที่ spawn จริงใหม่
        bool networkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (networkActive && !anchor.IsSpawned)
            return true;

        // ผู้เล่นเปลี่ยนชิ้นที่เลือกในลอบบี้ (หรือเพิ่งเลือกหลัง HUD bind ไปแล้ว)
        // → rebind เพื่ออัปเดตชิ้นที่คุม ไม่งั้นแถบพลังหมัด/prompt จะอิงชิ้นเก่า
        int selectedIndex = GetLobbySelectedIndex();
        if (selectedIndex >= 0 && JointForLimbIndex(selectedIndex) != OwnedJoint)
            return true;

        // LobbyManager โอน ownership หลัง spawn ได้ — anchor ที่ไม่ใช่ของเราแล้วต้องหาใหม่
        return anchor.IsSpawned && !anchor.IsOwner;
    }

    /// <summary>ชิ้นที่ client นี้เลือกใน lobby (0-3) หรือ -1 ถ้าไม่มีข้อมูล</summary>
    private int GetLobbySelectedIndex()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            return -1;

        if (lobby == null)
            lobby = FindFirstObjectByType<LobbyManager>(FindObjectsInactive.Include);

        if (lobby == null)
            return -1;

        return lobby.GetSelectedLimbIndex(NetworkManager.Singleton.LocalClientId);
    }

    private JointPullAndReconnect JointForLimbIndex(int index)
    {
        return index switch
        {
            0 => LeftArmJoint,
            1 => RightArmJoint,
            2 => LeftLegJoint,
            3 => RightLegJoint,
            _ => null
        };
    }

    /// <summary>ชื่อย่อของชิ้นส่วนไว้โชว์บน HUD (L-ARM / R-ARM / L-LEG / R-LEG)</summary>
    public string GetLimbLabel(Component limbComponent)
    {
        if (limbComponent == null)
            return string.Empty;

        JointPullAndReconnect joint = limbComponent.GetComponent<JointPullAndReconnect>();
        if (joint == null)
            return string.Empty;

        if (joint == LeftArmJoint) return "L-ARM";
        if (joint == RightArmJoint) return "R-ARM";
        if (joint == LeftLegJoint) return "L-LEG";
        if (joint == RightLegJoint) return "R-LEG";
        return string.Empty;
    }

    private void TryBind()
    {
        nextBindAttemptTime = Time.unscaledTime + bindRetryInterval;

        PlayerHandCombat[] allCombats = FindObjectsByType<PlayerHandCombat>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        JointPullAndReconnect[] allJoints = FindObjectsByType<JointPullAndReconnect>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        // 1) anchor = อะไรก็ได้ของหุ่นที่ "เรา" เป็นเจ้าของสักชิ้น
        NetworkBehaviour localAnchor = FindLocalOwner(allCombats);
        if (localAnchor == null)
            localAnchor = FindLocalOwner(allJoints);

        // 2) Host/ผู้เล่นที่ไม่ได้เป็นเจ้าของแขนขาเลย: ยึดหุ่นจาก Torso (Server เป็นเจ้าของ)
        if (localAnchor == null)
        {
            TorsoMovement[] allTorsos = FindObjectsByType<TorsoMovement>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            localAnchor = FindLocalOwner(allTorsos);
        }

        // 3) โหมดทดสอบไม่ต่อ network เท่านั้น (NetworkManager ยังไม่เปิด):
        //    ใช้หุ่นตัวแรกที่ยัง active — กัน bind หุ่นสำรองที่ถูกปิดไว้ใน scene
        //    (ถ้า network เปิดอยู่ ต้องรอ ownership จริง ไม่งั้นจะไป bind หุ่นของคนอื่น)
        bool networkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (localAnchor == null && !networkActive)
        {
            localAnchor = FirstActive(allCombats);
            if (localAnchor == null)
                localAnchor = FirstActive(allJoints);
        }

        if (localAnchor == null)
        {
            LogDiagnostic($"no locally owned NetworkBehaviour (combats={allCombats.Length}, joints={allJoints.Length})");
            return;
        }

        Transform robotRoot = ResolveRobotRoot(localAnchor);
        TorsoMovement torso = robotRoot.GetComponentInChildren<TorsoMovement>(true);

        JointPullAndReconnect leftArm = null;
        JointPullAndReconnect rightArm = null;
        JointPullAndReconnect leftLeg = null;
        JointPullAndReconnect rightLeg = null;

        foreach (JointPullAndReconnect joint in allJoints)
        {
            if (joint == null || ResolveRobotRoot(joint) != robotRoot)
                continue;

            bool isArm = joint.GetComponent<PlayerHandCombat>() != null;
            bool isLeg = joint.GetComponent<PlayerFootForRobot>() != null;

            if (isArm && IsLeftName(joint.name)) leftArm = joint;
            else if (isArm && IsRightName(joint.name)) rightArm = joint;
            else if (isLeg && IsLeftName(joint.name)) leftLeg = joint;
            else if (isLeg && IsRightName(joint.name)) rightLeg = joint;
        }

        if (torso == null || leftArm == null || rightArm == null ||
            leftLeg == null || rightLeg == null)
        {
            LogDiagnostic(
                $"incomplete robot root '{robotRoot.name}' " +
                $"(torso={torso != null}, leftArm={leftArm != null}, rightArm={rightArm != null}, " +
                $"leftLeg={leftLeg != null}, rightLeg={rightLeg != null})");
            return;
        }

        RobotRoot = robotRoot;
        Torso = torso;
        LeftArmJoint = leftArm;
        RightArmJoint = rightArm;
        LeftLegJoint = leftLeg;
        RightLegJoint = rightLeg;
        LeftHand = leftArm.GetComponent<PlayerHandCombat>();
        RightHand = rightArm.GetComponent<PlayerHandCombat>();

        // ชิ้นที่ "เราคุมจริง" ยึดตามที่เลือกใน lobby ก่อน — Host เป็นเจ้าของชิ้นที่
        // ไม่มีใครเลือกโดย default ถ้าดูจาก ownership อย่างเดียวจะนับผิดว่าคุมมือ
        // ทั้งที่เลือกขา / ไม่มีข้อมูล lobby (เทสไม่ผ่านลอบบี้) ค่อย fallback เป็น ownership
        OwnedJoint = JointForLimbIndex(GetLobbySelectedIndex());
        if (OwnedJoint == null)
            OwnedJoint = FirstLocallyOwned(leftArm, rightArm, leftLeg, rightLeg);
        OwnedHand = OwnedJoint != null ? OwnedJoint.GetComponent<PlayerHandCombat>() : null;

        // รวมเลือดของทุกชิ้นที่เราคุม (เทสคนเดียว = ครบ 4, เล่นจริง = 1 ชิ้น)
        // ชิ้นที่เลือกเล่นต้องอยู่ต้นลิสต์ — วงแหวนเลือกโชว์ตัวแรกเมื่อเลือดเท่ากัน
        // จะได้เห็นชื่อชิ้นตัวเองตอนเลือดเต็ม ไม่ใช่ L-ARM ตลอด
        OwnedHealths.Clear();
        CollectOwnedHealth(OwnedJoint);
        CollectOwnedHealth(leftArm);
        CollectOwnedHealth(rightArm);
        CollectOwnedHealth(leftLeg);
        CollectOwnedHealth(rightLeg);

        // ไม่ได้คุมชิ้นไหนเลย (เช่น host ที่เป็นแค่คนเปิดห้อง) — ใช้แขนซ้ายให้มีข้อมูลโชว์
        if (OwnedHealths.Count == 0)
        {
            RobotHealth fallbackHealth = leftArm.GetComponent<RobotHealth>();
            if (fallbackHealth != null)
                OwnedHealths.Add(fallbackHealth);
        }

        OwnedLimbHealth = OwnedHealths.Count > 0 ? OwnedHealths[0] : null;

        anchor = localAnchor;
        IsBound = true;

        Debug.Log(
            $"[PlayerHUD] bound to '{robotRoot.name}' | owned limb: " +
            $"{(OwnedJoint != null ? OwnedJoint.name : "none")} | anchor: {localAnchor.name}",
            this);

        OnBound?.Invoke();
    }

    /// <summary>
    /// หา root ของหุ่นจาก component ใดๆ — ชิ้นที่หลุด/ถูกย้าย parent ยังโยงกลับถึงหุ่นได้
    /// ผ่าน targetBody (rigidbody ปลายทางที่ joint ต่ออยู่ ซึ่งไม่เคยหลุดจากตัวหุ่น)
    /// </summary>
    private static Transform ResolveRobotRoot(NetworkBehaviour behaviour)
    {
        JointPullAndReconnect joint = behaviour.GetComponent<JointPullAndReconnect>();
        if (joint != null && joint.targetBody != null)
            return joint.targetBody.transform.root;

        return behaviour.transform.root;
    }

    private static NetworkBehaviour FindLocalOwner<T>(T[] behaviours)
        where T : NetworkBehaviour
    {
        foreach (T behaviour in behaviours)
        {
            if (behaviour != null && behaviour.IsSpawned && behaviour.IsOwner)
                return behaviour;
        }

        return null;
    }

    private static JointPullAndReconnect FirstLocallyOwned(params JointPullAndReconnect[] joints)
    {
        foreach (JointPullAndReconnect joint in joints)
        {
            // ยังไม่ spawn (ทดสอบไม่ต่อ network) = ถือว่าเป็นของเรา
            if (joint != null && (!joint.IsSpawned || joint.IsOwner))
                return joint;
        }

        return null;
    }

    private void CollectOwnedHealth(JointPullAndReconnect joint)
    {
        if (joint == null || (joint.IsSpawned && !joint.IsOwner))
            return;

        RobotHealth health = joint.GetComponent<RobotHealth>();
        if (health != null && !OwnedHealths.Contains(health))
            OwnedHealths.Add(health);
    }

    private static NetworkBehaviour FirstActive<T>(T[] behaviours)
        where T : NetworkBehaviour
    {
        foreach (T behaviour in behaviours)
        {
            if (behaviour != null && behaviour.gameObject.activeInHierarchy)
                return behaviour;
        }

        return null;
    }

    private static bool IsLeftName(string objectName)
    {
        string normalized = objectName.Replace('-', '_').Replace(' ', '_');
        return normalized.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0 ||
               normalized.EndsWith("_L", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRightName(string objectName)
    {
        string normalized = objectName.Replace('-', '_').Replace(' ', '_');
        return normalized.IndexOf("right", StringComparison.OrdinalIgnoreCase) >= 0 ||
               normalized.EndsWith("_R", StringComparison.OrdinalIgnoreCase);
    }

    private void LogDiagnostic(string reason)
    {
        if (Time.unscaledTime < nextDiagnosticTime)
            return;

        nextDiagnosticTime = Time.unscaledTime + 5f;
        Debug.LogWarning($"[PlayerHUD] waiting to bind: {reason}.", this);
    }
}

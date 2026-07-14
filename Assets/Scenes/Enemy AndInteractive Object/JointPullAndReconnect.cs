using UnityEngine;
using Unity.Netcode;
using System;

[RequireComponent(typeof(Rigidbody))]
public class JointPullAndReconnect : NetworkBehaviour
{
    [Header("Target & Pull Settings")]
    public Rigidbody targetBody;
    public float pullSpeed = 20f;
    public float pullDamper = 10f;
    [Tooltip("เพดานความเร็วตอนถูกดึงกลับ (m/s) — กันชิ้นที่อยู่ไกลถูกสปริงเหวี่ยงจนพุ่งเลยเป้า")]
    public float maxPullSpeed = 25f;
    public float reconnectDistance = 0.4f;

    [Header("Cooldown Settings")]
    [Tooltip("เวลาที่ต้องรอ (วินาที) ก่อนจะดึงชิ้นส่วนกลับมาได้")]
    public float reconnectCooldown = 3.0f;

    [Header("Controls (For Testing)")]
    public KeyCode pullKey = KeyCode.R;

    [Header("Current Status (Read Only)")]
    [SerializeField] private bool isConnected = true;
    [SerializeField] private bool isPulling = false;
    [SerializeField] private float currentCooldown = 0f;

    // ===== Public API สำหรับ HUD =====
    public bool IsConnected => isConnected;
    public bool IsPullReady =>
        !isConnected &&
        currentCooldown <= 0f &&
        targetBody != null;
    public float RemainingReconnectCooldown => Mathf.Max(0f, currentCooldown);
    public string PullKeyLabel => pullKey.ToString();

    /// <summary>กำลังถูกดึงกลับอยู่ไหม (ให้ controller ของแขนขาหยุดขับแรงสู้กับแรงดึง)</summary>
    public bool IsBeingPulled => isPulling;

    /// <summary>ยิงบนทุกเครื่องเมื่อสถานะหลุด/ต่อกลับเปลี่ยน (true = ต่ออยู่)</summary>
    public event Action<bool> OnConnectionStateChanged;

    // ✅ สถานะเชื่อมต่อถูกตัดสินบน Server เท่านั้น แล้ว sync ให้ทุกเครื่องผ่านตัวนี้
    // (เดิมเป็น bool ธรรมดา เครื่อง Client ไม่มีทางรู้ว่าชิ้นส่วนหลุด
    //  → HUD ไม่อัปเดต และกด R ดึงชิ้นส่วนกลับไม่ได้เลยถ้าไม่ใช่ Host)
    private readonly NetworkVariable<bool> netIsConnected = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ✅ รองรับทดสอบคนเดียวแบบไม่ต่อ network: ถ้ายังไม่ spawn ให้เครื่องนี้มีสิทธิ์เต็ม
    private bool HasLocalAuthority => !IsSpawned || IsOwner;
    private bool HasServerAuthority => !IsSpawned || IsServer;

    private Rigidbody myRb;

    // NscGame Integration
    private PlayerFootForRobot footController;
    private PlayerHandMovement handController;

    private Vector3 localPositionOffset;
    private Quaternion localRotationOffset;
    private SavedConfigurableJointSettings savedSettings;

    [Header("Object with Joint")]
    public GameObject Joinobject; // วัตถุเป้าหมายที่คุณเพิ่มเข้ามา
    private ConfigurableJoint currentJoint;

    private void Awake()
    {
        // ถ้าไม่ได้ลากใส่อะไรไว้ ให้ใช้วัตถุที่สคริปต์นี้แปะอยู่แทน
        if (Joinobject == null) Joinobject = gameObject;

        myRb = Joinobject.GetComponent<Rigidbody>();

        // หา Controller จากวัตถุหลัก
        footController = GetComponent<PlayerFootForRobot>();
        handController = GetComponent<PlayerHandMovement>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        currentJoint = Joinobject.GetComponent<ConfigurableJoint>();
        if (currentJoint != null)
        {
            CaptureJointSetup(currentJoint);
        }

        netIsConnected.OnValueChanged += OnNetConnectionChanged;

        if (IsServer)
        {
            netIsConnected.Value = isConnected;
        }
        else
        {
            // ผู้เล่นที่เข้าห้องช้า: ดึงสถานะล่าสุดจาก Server มาใช้ทันที
            ApplyConnectionState(netIsConnected.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        netIsConnected.OnValueChanged -= OnNetConnectionChanged;
        base.OnNetworkDespawn();
    }

    private void Start()
    {
        // ✅ ทดสอบคนเดียวแบบไม่ต่อ network: OnNetworkSpawn ไม่ถูกเรียก เลย capture ตรงนี้แทน
        if (!IsSpawned && currentJoint == null)
        {
            currentJoint = Joinobject.GetComponent<ConfigurableJoint>();
            if (currentJoint != null)
            {
                CaptureJointSetup(currentJoint);
            }
        }
    }

    public void CaptureJointSetup(ConfigurableJoint joint)
    {
        if (joint == null || joint.connectedBody == null) return;

        targetBody = joint.connectedBody;

        // 🚨 อิงตำแหน่งจาก Joinobject.transform เท่านั้น เพื่อความเป๊ะ
        localPositionOffset = targetBody.transform.InverseTransformPoint(Joinobject.transform.position);
        localRotationOffset = Quaternion.Inverse(targetBody.transform.rotation) * Joinobject.transform.rotation;

        savedSettings = new SavedConfigurableJointSettings(joint);

        SetConnectionState(true);
        isPulling = false;
        currentCooldown = 0f;
        currentJoint = joint;
    }

    private void OnJointBreak(float breakForce)
    {
        HandleDisconnection();
    }

    public void ForceBreakJoint()
    {
        // การหลุดเป็นเรื่องของ Server เท่านั้น — Client รอรับผลผ่าน netIsConnected
        if (IsSpawned && !IsServer) return;

        // Infinity only prevents a physics break. Respect the controller's
        // unbreakable rule for explicit damage/gameplay detach requests too.
        if (handController != null && handController.preventGrabBreakWhileRagdoll)
            return;
        if (footController != null && footController.makeLegJointsUnbreakable)
            return;

        if (!isConnected || currentJoint == null) return;

        Destroy(currentJoint);
        currentJoint = null;
        HandleDisconnection();
    }

    private void HandleDisconnection()
    {
        // OnJointBreak อาจยิงจากฟิสิกส์ local บนเครื่อง Client ด้วย
        // แต่สถานะจริงให้ Server ตัดสินคนเดียว แล้ว sync กลับมา
        if (IsSpawned && !IsServer)
            return;

        if (!isConnected)
            return;

        SetConnectionState(false);
        SetControllerStateServer(false);

        // ✅ ขาหลุด = ยืนต่อไม่ได้ — สั่งล้ม (Falling → Ragdoll) ทันที
        // ไม่งั้นระบบ hover ยังพยุงตัวลอยค้างกลางอากาศ แล้วขาที่หลุด
        // เอื้อมกลับมาต่อไม่ถึงตำแหน่ง socket ที่ลอยอยู่
        if (footController != null && footController.torso != null)
        {
            TorsoMovement torso = footController.torso;
            if (torso.currentState.Value == TorsoMovement.TorsoState.Standing)
                torso.currentState.Value = TorsoMovement.TorsoState.Falling;
        }
    }

    private void Update()
    {
        // นับ cooldown บนทุกเครื่อง ให้ HUD ฝั่ง Client รู้ว่าพร้อมดึงเมื่อไหร่
        // (จุดเริ่มนับถูก sync พร้อมกันทุกเครื่องใน ApplyConnectionState)
        if (currentCooldown > 0f)
        {
            currentCooldown -= Time.deltaTime;
        }

        if (!HasLocalAuthority) return;

        if (!isConnected && targetBody != null)
        {
            if (currentCooldown <= 0f && Input.GetKey(pullKey))
            {
                if (!isPulling)
                    SetPulling(true);
            }
            else
            {
                if (isPulling)
                    SetPulling(false);
            }
        }
    }

    private void SetPulling(bool state)
    {
        isPulling = state;

        // ยิง RPC ได้เฉพาะตอนต่อ network — โหมดทดสอบคนเดียวใช้ค่า local ตรงๆ
        if (IsSpawned)
            SetPullingStateRpc(state);
    }

    private void FixedUpdate()
    {
        if (!HasServerAuthority) return;

        if (!isPulling || isConnected || targetBody == null) return;

        Vector3 targetSocketWorldPos = targetBody.transform.TransformPoint(localPositionOffset);
        float distanceToSocket = Vector3.Distance(Joinobject.transform.position, targetSocketWorldPos);

        if (distanceToSocket <= reconnectDistance)
        {
            ReconnectSystem(targetSocketWorldPos);
            return;
        }

        Vector3 velocityTarget = (targetSocketWorldPos - myRb.position) * pullSpeed;
        // ชิ้นอยู่ไกล = สปริงคำนวณความเร็วมหาศาลจนพุ่งเลยเป้าแล้วแกว่ง — จำกัดเพดานไว้
        velocityTarget = Vector3.ClampMagnitude(velocityTarget, maxPullSpeed);
        Vector3 force = (velocityTarget - myRb.linearVelocity) * pullDamper;
        myRb.AddForce(force, ForceMode.Acceleration);
    }

    private void ReconnectSystem(Vector3 targetSocketWorldPos)
    {
        isPulling = false;

        myRb.isKinematic = true;
        myRb.linearVelocity = Vector3.zero;
        myRb.angularVelocity = Vector3.zero;

        // 🚨 วาร์ป Joinobject ไปยังตำแหน่งที่ถูกต้อง
        Joinobject.transform.position = targetSocketWorldPos;
        Joinobject.transform.rotation = targetBody.transform.rotation * localRotationOffset;

        myRb.isKinematic = false;

        currentJoint = Joinobject.AddComponent<ConfigurableJoint>();
        savedSettings.ApplyTo(currentJoint, targetBody);

        SetConnectionState(true);
        SetControllerStateServer(true);
    }

    private void SetConnectionState(bool connected)
    {
        if (IsSpawned)
        {
            // เขียนผ่าน NetworkVariable แล้วให้ callback เป็นคน apply เหมือนกันทุกเครื่อง
            // Client ธรรมดาห้ามเขียน — รอรับจาก Server อย่างเดียว
            if (IsServer)
                netIsConnected.Value = connected;
            return;
        }

        ApplyConnectionState(connected);
    }

    private void OnNetConnectionChanged(bool previousValue, bool newValue)
    {
        ApplyConnectionState(newValue);
    }

    private void ApplyConnectionState(bool connected)
    {
        if (isConnected == connected)
            return;

        isConnected = connected;

        if (connected)
        {
            currentCooldown = 0f;
            isPulling = false;
        }
        else
        {
            currentCooldown = reconnectCooldown;
        }

        // ฝั่ง Client จัดการ ConfigurableJoint ในเครื่องตัวเองให้ตรงกับ Server
        // (ถ้าปล่อย joint เก่าค้างไว้ มันจะดึงชิ้นส่วนสู้กับตำแหน่งที่ sync มา)
        if (IsSpawned && !IsServer)
        {
            if (!connected)
            {
                if (currentJoint != null)
                {
                    Destroy(currentJoint);
                    currentJoint = null;
                }
            }
            else if (currentJoint == null && targetBody != null)
            {
                currentJoint = Joinobject.AddComponent<ConfigurableJoint>();
                savedSettings.ApplyTo(currentJoint, targetBody);
            }
        }

        OnConnectionStateChanged?.Invoke(connected);
    }

    // ================================================================
    //  Debug: บังคับชิ้นหลุดสำหรับทดสอบ UI (ข้าม flag กันหลุดทุกตัว)
    // ================================================================

    /// <summary>เรียกจากปุ่มทดสอบ — ใช้ได้จากทุกเครื่อง (client จะส่งคำขอไป Server ให้เอง)</summary>
    public void DebugRequestBreak()
    {
        if (!IsSpawned || IsServer)
            DebugForceBreak();
        else
            DebugRequestBreakRpc();
    }

    [Rpc(SendTo.Server)]
    private void DebugRequestBreakRpc() { DebugForceBreak(); }

    private void DebugForceBreak()
    {
        if (IsSpawned && !IsServer) return;
        if (!isConnected) return;

        if (currentJoint != null)
        {
            Destroy(currentJoint);
            currentJoint = null;
        }

        HandleDisconnection();
        Debug.Log($"[Debug] force-broke limb '{name}'", this);
    }

    [Rpc(SendTo.Server)]
    private void SetPullingStateRpc(bool state) { isPulling = state; }

    private void SetControllerStateServer(bool isAttached)
    {
        if (footController != null)
            footController.currentState.Value = isAttached ? PlayerFootForRobot.FootState.Attached : PlayerFootForRobot.FootState.Detached;

        if (handController != null)
            handController.currentState.Value = isAttached ? PlayerHandMovement.HandState.Attached : PlayerHandMovement.HandState.Detached;
    }

    public void StartPullingExternal()
    {
        if (!isConnected && HasLocalAuthority && currentCooldown <= 0f)
            SetPulling(true);
    }

    public void StopPullingExternal()
    {
        if (HasLocalAuthority)
            SetPulling(false);
    }
}

// ==========================================
// 🚨 อัปเดต Struct: เพิ่ม Axis และ AutoAnchor ป้องกันข้อต่อเบี้ยว
// ==========================================
public struct SavedConfigurableJointSettings
{
    public Vector3 anchor;
    public Vector3 connectedAnchor;
    public bool autoConfigureConnectedAnchor; // ✅ โคตรสำคัญ ป้องกัน Unity คาดเดา Anchor เอง
    public Vector3 axis;                      // ✅ แกนหมุนหลัก
    public Vector3 secondaryAxis;             // ✅ แกนหมุนรอง
    public float massScale;
    public float connectedMassScale;

    public ConfigurableJointMotion xMotion, yMotion, zMotion, angularXMotion, angularYMotion, angularZMotion;
    public SoftJointLimit linearLimit, lowAngularXLimit, highAngularXLimit, angularYLimit, angularZLimit;
    public JointDrive xDrive, yDrive, zDrive, angularXDrive, angularYZDrive, slerpDrive;
    public RotationDriveMode rotationDriveMode;
    public float breakForce, breakTorque;
    public bool enableCollision;

    public SavedConfigurableJointSettings(ConfigurableJoint source)
    {
        anchor = source.anchor;
        connectedAnchor = source.connectedAnchor;
        autoConfigureConnectedAnchor = source.autoConfigureConnectedAnchor;
        axis = source.axis;
        secondaryAxis = source.secondaryAxis;
        massScale = source.massScale;
        connectedMassScale = source.connectedMassScale;

        xMotion = source.xMotion; yMotion = source.yMotion; zMotion = source.zMotion;
        angularXMotion = source.angularXMotion; angularYMotion = source.angularYMotion; angularZMotion = source.angularZMotion;
        linearLimit = source.linearLimit; lowAngularXLimit = source.lowAngularXLimit; highAngularXLimit = source.highAngularXLimit;
        angularYLimit = source.angularYLimit; angularZLimit = source.angularZLimit;
        xDrive = source.xDrive; yDrive = source.yDrive; zDrive = source.zDrive;
        angularXDrive = source.angularXDrive; angularYZDrive = source.angularYZDrive; slerpDrive = source.slerpDrive;
        rotationDriveMode = source.rotationDriveMode; breakForce = source.breakForce; breakTorque = source.breakTorque;
        enableCollision = source.enableCollision;
    }

    public void ApplyTo(ConfigurableJoint target, Rigidbody connectedBody)
    {
        target.connectedBody = connectedBody;

        // 🚨 ต้องปิด Auto Configure ก่อนยัด Anchor ไม่งั้น Unity จะเขียนทับ
        target.autoConfigureConnectedAnchor = false;

        target.anchor = anchor;
        target.connectedAnchor = connectedAnchor;
        target.axis = axis;
        target.secondaryAxis = secondaryAxis;
        target.massScale = massScale;
        target.connectedMassScale = connectedMassScale;

        // คืนค่า Auto ให้เหมือนต้นฉบับ
        target.autoConfigureConnectedAnchor = autoConfigureConnectedAnchor;

        target.xMotion = xMotion; target.yMotion = yMotion; target.zMotion = zMotion;
        target.angularXMotion = angularXMotion; target.angularYMotion = angularYMotion; target.angularZMotion = angularZMotion;
        target.linearLimit = linearLimit; target.lowAngularXLimit = lowAngularXLimit; target.highAngularXLimit = highAngularXLimit;
        target.angularYLimit = angularYLimit; target.angularZLimit = angularZLimit;
        target.xDrive = xDrive; target.yDrive = yDrive; target.zDrive = zDrive;
        target.angularXDrive = angularXDrive; target.angularYZDrive = angularYZDrive; target.slerpDrive = slerpDrive;
        target.rotationDriveMode = rotationDriveMode; target.breakForce = breakForce; target.breakTorque = breakTorque;
        target.enableCollision = enableCollision;
    }
}

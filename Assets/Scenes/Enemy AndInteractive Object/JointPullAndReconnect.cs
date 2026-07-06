using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
public class JointPullAndReconnect : NetworkBehaviour
{
    [Header("Target & Pull Settings")]
    public Rigidbody targetBody;
    public float pullSpeed = 20f;
    public float pullDamper = 10f;
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
    }

    public void CaptureJointSetup(ConfigurableJoint joint)
    {
        if (joint == null || joint.connectedBody == null) return;

        targetBody = joint.connectedBody;

        // 🚨 อิงตำแหน่งจาก Joinobject.transform เท่านั้น เพื่อความเป๊ะ
        localPositionOffset = targetBody.transform.InverseTransformPoint(Joinobject.transform.position);
        localRotationOffset = Quaternion.Inverse(targetBody.transform.rotation) * Joinobject.transform.rotation;

        savedSettings = new SavedConfigurableJointSettings(joint);

        isConnected = true;
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
        if (!isConnected || currentJoint == null) return;

        Destroy(currentJoint);
        currentJoint = null;
        HandleDisconnection();
    }

    private void HandleDisconnection()
    {
        isConnected = false;
        currentCooldown = reconnectCooldown;

        if (IsOwner)
        {
            SetControllerStateRpc(false);
        }
    }

    private void Update()
    {
        if (!IsOwner) return;

        if (currentCooldown > 0f)
        {
            currentCooldown -= Time.deltaTime;
        }

        if (!isConnected && targetBody != null)
        {
            if (currentCooldown <= 0f && Input.GetKey(pullKey))
            {
                if (!isPulling)
                {
                    isPulling = true;
                    SetPullingStateRpc(true);
                }
            }
            else
            {
                if (isPulling)
                {
                    isPulling = false;
                    SetPullingStateRpc(false);
                }
            }
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        if (!isPulling || isConnected || targetBody == null) return;

        Vector3 targetSocketWorldPos = targetBody.transform.TransformPoint(localPositionOffset);
        float distanceToSocket = Vector3.Distance(Joinobject.transform.position, targetSocketWorldPos);

        if (distanceToSocket <= reconnectDistance)
        {
            ReconnectSystem(targetSocketWorldPos);
            return;
        }

        Vector3 velocityTarget = (targetSocketWorldPos - myRb.position) * pullSpeed;
        Vector3 force = (velocityTarget - myRb.linearVelocity) * pullDamper;
        myRb.AddForce(force, ForceMode.Acceleration);
    }

    private void ReconnectSystem(Vector3 targetSocketWorldPos)
    {
        isPulling = false;
        SetPullingStateRpc(false);

        myRb.isKinematic = true;
        myRb.linearVelocity = Vector3.zero;
        myRb.angularVelocity = Vector3.zero;

        // 🚨 วาร์ป Joinobject ไปยังตำแหน่งที่ถูกต้อง
        Joinobject.transform.position = targetSocketWorldPos;
        Joinobject.transform.rotation = targetBody.transform.rotation * localRotationOffset;

        myRb.isKinematic = false;

        currentJoint = Joinobject.AddComponent<ConfigurableJoint>();
        savedSettings.ApplyTo(currentJoint, targetBody);

        isConnected = true;
        SetControllerStateServer(true);
    }

    [Rpc(SendTo.Server)]
    private void SetPullingStateRpc(bool state) { isPulling = state; }

    [Rpc(SendTo.Server)]
    private void SetControllerStateRpc(bool isAttached) { SetControllerStateServer(isAttached); }

    private void SetControllerStateServer(bool isAttached)
    {
        if (footController != null)
            footController.currentState.Value = isAttached ? PlayerFootForRobot.FootState.Attached : PlayerFootForRobot.FootState.Detached;

        if (handController != null)
            handController.currentState.Value = isAttached ? PlayerHandMovement.HandState.Attached : PlayerHandMovement.HandState.Detached;
    }

    public void StartPullingExternal()
    {
        if (!isConnected && IsOwner && currentCooldown <= 0f)
        {
            isPulling = true;
            SetPullingStateRpc(true);
        }
    }

    public void StopPullingExternal()
    {
        if (IsOwner)
        {
            isPulling = false;
            SetPullingStateRpc(false);
        }
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
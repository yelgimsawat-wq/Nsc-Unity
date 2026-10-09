using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

namespace Nsc.Robots
{
    /// <summary>
    /// การต่ออยู่ของแขนขาหนึ่งชิ้น: ทำลาย/สร้าง ConfigurableJoint ใหม่ และฟิสิกส์ตอนดึงกลับ (กด R)
    /// เดิมชื่อ JointPullAndReconnect (GUID เดิม)
    ///
    /// หน้าที่เหลือแค่สถานะการต่อ + ข้อต่อ + แรงดึงกลับ — ผลที่ตามมาของการหลุดอยู่ที่อื่น:
    ///   RobotLimb (เลือด ↔ การต่อ), Robot (ขาหลุด → ลำตัวล้ม), ตัวควบคุม (อ่าน IsAttached)
    /// ปุ่ม R ย้ายไปอยู่ที่ ArmInput/LegInput — ตัวนี้รับแค่ RequestPullBackRpc จากเจ้าของ
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class LimbAttachment : NetworkBehaviour
    {
        [Header("Socket & Pull")]
        [Tooltip("Rigidbody ที่ชิ้นนี้ต่ออยู่ (ลำตัว) — ว่างไว้ได้ จะอ่านจาก connectedBody ของข้อต่อตอนเริ่ม")]
        [FormerlySerializedAs("targetBody")]
        [SerializeField] private Rigidbody socket;
        [SerializeField] private float pullSpeed = 20f;
        [SerializeField] private float pullDamper = 10f;
        [Tooltip("เพดานความเร็วตอนถูกดึงกลับ (m/s) — กันชิ้นที่อยู่ไกลถูกสปริงเหวี่ยงจนพุ่งเลยเป้า")]
        [SerializeField] private float maxPullSpeed = 25f;
        [SerializeField] private float reconnectDistance = 0.4f;

        [Tooltip("เวลาที่ต้องรอ (วินาที) หลังหลุดก่อนจะดึงกลับมาได้")]
        [SerializeField] private float reconnectCooldown = 3.0f;

        [Header("Object with Joint")]
        [Tooltip("GameObject ที่มี ConfigurableJoint ต่อกับลำตัว (ปกติคือท่อนบนของแขน/ขา) — ว่าง = ตัวนี้เอง")]
        [FormerlySerializedAs("Joinobject")]
        [SerializeField] private GameObject jointObject;

        // สถานะการต่อถูกตัดสินบน server เท่านั้น แล้ว sync ให้ทุกเครื่อง
        private readonly NetworkVariable<bool> isAttached = new NetworkVariable<bool>(
            true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private Rigidbody jointBody;
        private ConfigurableJoint joint;
        private SavedConfigurableJointSettings savedSettings;
        private Vector3 localPositionOffset;
        private Quaternion localRotationOffset;
        private bool attachedLocal = true;   // ค่าที่ apply แล้วบนเครื่องนี้ (กันยิงอีเวนต์ซ้ำ)
        private bool isPulling;
        private float cooldownRemaining;

        public bool IsAttached => attachedLocal;
        public bool IsBeingPulled => isPulling;
        public bool IsPullReady => !attachedLocal && cooldownRemaining <= 0f && socket != null;
        public float RemainingReconnectCooldown => Mathf.Max(0f, cooldownRemaining);
        public Rigidbody Socket => socket;

        /// <summary>ยิงบนทุกเครื่องเมื่อหลุด (false) หรือต่อกลับ (true)</summary>
        public event Action<bool> AttachmentChanged;

        private bool HasServerAuthority => !IsSpawned || IsServer;

        private void Awake()
        {
            if (jointObject == null) jointObject = gameObject;
            jointBody = jointObject.GetComponent<Rigidbody>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            CaptureJoint();
            isAttached.OnValueChanged += OnAttachedValueChanged;

            // ผู้เล่นที่เข้าห้องช้า: ใช้สถานะล่าสุดจาก server ทันที
            if (!IsServer) Apply(isAttached.Value);
        }

        public override void OnNetworkDespawn()
        {
            isAttached.OnValueChanged -= OnAttachedValueChanged;
            base.OnNetworkDespawn();
        }

        private void Start()
        {
            // ทดสอบคนเดียวแบบไม่ต่อ network: OnNetworkSpawn ไม่ถูกเรียก
            if (!IsSpawned && joint == null) CaptureJoint();
        }

        private void CaptureJoint()
        {
            ConfigurableJoint current = jointObject.GetComponent<ConfigurableJoint>();
            if (current == null || current.connectedBody == null) return;

            joint = current;
            socket = current.connectedBody;
            // อิงตำแหน่งจาก jointObject เท่านั้น เพื่อความเป๊ะตอนวาร์ปกลับเข้า socket
            localPositionOffset = socket.transform.InverseTransformPoint(jointObject.transform.position);
            localRotationOffset = Quaternion.Inverse(socket.transform.rotation) * jointObject.transform.rotation;
            savedSettings = new SavedConfigurableJointSettings(current);
        }

        private void Update()
        {
            // นับ cooldown บนทุกเครื่อง ให้ HUD ฝั่ง client รู้ว่าพร้อมดึงเมื่อไหร่
            if (cooldownRemaining > 0f) cooldownRemaining -= Time.deltaTime;
        }

        // ================================================================
        //  หลุด
        // ================================================================

        private void OnJointBreak(float breakForce) => ServerDetach();

        /// <summary>
        /// [SERVER] ทำให้ชิ้นนี้หลุด — การหลุดจากดาเมจเป็นเกมเพลย์ ต้องหลุดได้เสมอ
        /// (ธงกันข้อต่อแตกจากฟิสิกส์ของตัวควบคุมไม่เกี่ยวกับทางนี้)
        /// </summary>
        public void ServerDetach()
        {
            if (!HasServerAuthority || !attachedLocal) return;

            if (joint != null)
            {
                Destroy(joint);
                joint = null;
            }

            SetAttached(false);
        }

        // ================================================================
        //  ดึงกลับ (กด R ค้าง)
        // ================================================================

        /// <summary>เจ้าของชิ้นนี้กด/ปล่อยปุ่มดึงกลับ — server ตัดสินว่าดึงได้ไหม</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        public void RequestPullBackRpc(bool held) => ServerSetPulling(held);

        /// <summary>ใช้ตอนเล่นแบบไม่ต่อ network (Rpc เรียกไม่ได้ก่อน spawn)</summary>
        public void RequestPullBack(bool held)
        {
            if (IsSpawned) RequestPullBackRpc(held);
            else ServerSetPulling(held);
        }

        private void ServerSetPulling(bool held)
        {
            isPulling = held && !attachedLocal && cooldownRemaining <= 0f && socket != null;
        }

        private void FixedUpdate()
        {
            if (!HasServerAuthority || !isPulling || attachedLocal || socket == null) return;

            Vector3 socketPosition = socket.transform.TransformPoint(localPositionOffset);
            if (Vector3.Distance(jointObject.transform.position, socketPosition) <= reconnectDistance)
            {
                ServerReconnect(socketPosition);
                return;
            }

            // ชิ้นอยู่ไกล = สปริงคำนวณความเร็วมหาศาลจนพุ่งเลยเป้าแล้วแกว่ง — จำกัดเพดานไว้
            Vector3 velocityTarget = Vector3.ClampMagnitude((socketPosition - jointBody.position) * pullSpeed, maxPullSpeed);
            jointBody.AddForce((velocityTarget - jointBody.linearVelocity) * pullDamper, ForceMode.Acceleration);
        }

        private void ServerReconnect(Vector3 socketPosition)
        {
            isPulling = false;

            jointBody.isKinematic = true;
            jointBody.linearVelocity = Vector3.zero;
            jointBody.angularVelocity = Vector3.zero;

            jointObject.transform.position = socketPosition;
            jointObject.transform.rotation = socket.transform.rotation * localRotationOffset;

            jointBody.isKinematic = false;

            joint = jointObject.AddComponent<ConfigurableJoint>();
            savedSettings.ApplyTo(joint, socket);

            SetAttached(true);
        }

        // ================================================================
        //  sync สถานะ
        // ================================================================

        private void SetAttached(bool attached)
        {
            // เขียนผ่าน NetworkVariable แล้วให้ callback apply เหมือนกันทุกเครื่อง
            if (IsSpawned)
            {
                if (IsServer) isAttached.Value = attached;
                return;
            }

            Apply(attached);
        }

        private void OnAttachedValueChanged(bool previous, bool current) => Apply(current);

        private void Apply(bool attached)
        {
            if (attachedLocal == attached) return;
            attachedLocal = attached;

            cooldownRemaining = attached ? 0f : reconnectCooldown;
            if (attached) isPulling = false;

            // ฝั่ง client จัดการข้อต่อในเครื่องตัวเองให้ตรงกับ server
            // (ถ้าปล่อยข้อต่อเก่าค้างไว้ มันจะดึงชิ้นส่วนสู้กับตำแหน่งที่ sync มา)
            if (IsSpawned && !IsServer)
            {
                if (!attached && joint != null)
                {
                    Destroy(joint);
                    joint = null;
                }
                else if (attached && joint == null && socket != null)
                {
                    joint = jointObject.AddComponent<ConfigurableJoint>();
                    savedSettings.ApplyTo(joint, socket);
                }
            }

            AttachmentChanged?.Invoke(attached);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // ================================================================
        //  Debug: บังคับชิ้นหลุดสำหรับทดสอบ UI — มีเฉพาะใน Editor/development build
        //  (เดิมเป็น server RPC ที่ client ไหนก็เรียกได้ และติดไปกับ build จริง = ช่องโกง)
        // ================================================================

        public void DebugRequestBreak()
        {
            if (!IsSpawned || IsServer) ServerDetach();
            else DebugRequestBreakRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void DebugRequestBreakRpc() => ServerDetach();
#endif
    }

    /// <summary>
    /// ค่าตั้งของ ConfigurableJoint ที่ต้องจำไว้สร้างข้อต่อใหม่ตอนต่อกลับ
    /// ต้องปิด autoConfigureConnectedAnchor ก่อนยัด anchor ไม่งั้น Unity เดา anchor เองจนข้อต่อเบี้ยว
    /// </summary>
    public struct SavedConfigurableJointSettings
    {
        public Vector3 anchor, connectedAnchor, axis, secondaryAxis;
        public bool autoConfigureConnectedAnchor;
        public float massScale, connectedMassScale;
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
            rotationDriveMode = source.rotationDriveMode;
            breakForce = source.breakForce; breakTorque = source.breakTorque;
            enableCollision = source.enableCollision;
        }

        public void ApplyTo(ConfigurableJoint target, Rigidbody connectedBody)
        {
            target.connectedBody = connectedBody;
            target.autoConfigureConnectedAnchor = false;
            target.anchor = anchor;
            target.connectedAnchor = connectedAnchor;
            target.axis = axis;
            target.secondaryAxis = secondaryAxis;
            target.massScale = massScale;
            target.connectedMassScale = connectedMassScale;
            target.autoConfigureConnectedAnchor = autoConfigureConnectedAnchor;
            target.xMotion = xMotion; target.yMotion = yMotion; target.zMotion = zMotion;
            target.angularXMotion = angularXMotion; target.angularYMotion = angularYMotion; target.angularZMotion = angularZMotion;
            target.linearLimit = linearLimit; target.lowAngularXLimit = lowAngularXLimit; target.highAngularXLimit = highAngularXLimit;
            target.angularYLimit = angularYLimit; target.angularZLimit = angularZLimit;
            target.xDrive = xDrive; target.yDrive = yDrive; target.zDrive = zDrive;
            target.angularXDrive = angularXDrive; target.angularYZDrive = angularYZDrive; target.slerpDrive = slerpDrive;
            target.rotationDriveMode = rotationDriveMode;
            target.breakForce = breakForce; target.breakTorque = breakTorque;
            target.enableCollision = enableCollision;
        }
    }
}

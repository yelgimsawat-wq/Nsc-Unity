using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// วางสคริปต์นี้ไว้บน NetworkObject ตัวเดียวในซีน (เช่น GameManager)
/// ทำหน้าที่เก็บตำแหน่งเช็คพอยต์ล่าสุด และรีเซ็ต "ร่างกาย" (Body root + limbs)
/// กลับไปที่เช็คพอยต์เมื่อร่างตก/ตาย
///
/// แก้ไข 3 จุดจากเวอร์ชันก่อนหน้าที่ทำให้ร่างค้างลอยกลางอากาศหลัง respawn:
///   1) ใช้ NetworkTransform.Teleport() แทนการ set transform ตรงๆ (กัน interpolation ดึงกลับ)
///   2) limb ที่ owner เป็น client อื่น จะสั่งผ่าน ClientRpc ให้ owner teleport ของตัวเอง
///      แทนที่จะให้ server เขียนทับ (ซึ่งไม่มีผลถ้า NetworkTransform เป็น owner-authoritative)
///   3) ปลด connectedBody ของ joint ชั่วคราวระหว่าง teleport แล้วค่อยต่อกลับ
///      (Joint ไม่มี .enabled ให้ใช้เหมือน Behaviour อื่นๆ) กัน physics solver
///      พยายามแก้ constraint ระยะไกลด้วยแรงมหาศาลในเฟรมเดียว
///
/// สมมติฐานโครงสร้าง:
/// - มี GameObject ชื่อ "Body" เป็น root, มี Rigidbody หลัก (อาจเป็น kinematic หรือไม่ก็ได้)
/// - ใต้ Body มี limb ต่างๆ (ArmL, ArmR, LegL, LegR) แต่ละอันมี Rigidbody ของตัวเอง
///   เชื่อมกันด้วย ConfigurableJoint/CharacterJoint
/// - แต่ละ limb อาจมี NetworkObject ของตัวเอง (owner-authoritative) หรือไม่มีก็ได้
///   ถ้าไม่มี NetworkObject แยก จะถือว่า server เป็นคนคุม (server-authoritative) ตรงๆ
/// </summary>
public class RespawnManager : NetworkBehaviour
{
    public static RespawnManager Instance { get; private set; }

    [Header("References")]
    [Tooltip("Root object ของร่างกายทั้งหมด (Body)")]
    [SerializeField] private Transform bodyRoot;

    [Tooltip("Rigidbody ของทุกส่วน (แขน/ขา/ลำตัว) ที่ต้องรีเซ็ต velocity ตอน respawn")]
    [SerializeField] private Rigidbody[] limbRigidbodies;

    [Tooltip("จุดเกิดเริ่มต้น ถ้ายังไม่เคยเหยียบเช็คพอยต์ไหนเลย")]
    [SerializeField] private Transform defaultSpawnPoint;

    [Tooltip("กัน respawn ซ้ำรัวๆ — ร่างตกเหวทั้งตัว แขนขาทยอยแตะ death zone ห่างกันไม่กี่เฟรม\n" +
             "ถ้าไม่กันไว้ RespawnBody จะถูกเรียกซ้ำ 3-5 ครั้งติด (joint ปลด/ต่อวนซ้ำ)")]
    [SerializeField] private float respawnCooldown = 1f;
    private float _lastRespawnTime = -999f;

    // แคชไว้ตอน Awake เพื่อไม่ต้อง GetComponent ซ้ำทุกครั้งที่ respawn
    private NetworkTransform bodyRootNetTransform;
    private NetworkTransform[] limbNetTransforms;   // null ได้ถ้า limb ไม่มี NetworkTransform เป็นของตัวเอง
    private Joint[] allJoints;                       // ทุก joint ใต้ bodyRoot (รวม limb ทั้งหมด)
    private Rigidbody[] jointConnectedBodies;         // connectedBody เดิมของแต่ละ joint (Joint ไม่มี .enabled ให้ใช้)

    // ✅ [Gameplay Reset] สคริปต์เกมเพลย์ที่ต้องรีเซ็ต state หลัง teleport
    // ไม่งั้นเท้าจะ MovePosition กลับไปจุดปักเก่าที่ก้นเหว / มือลาก joint ที่ยังจับกำแพงเดิม
    private TorsoMovement torsoMovement;
    private PlayerFootForRobot[] feet;
    private PlayerHandMovement[] hands;

    // เก็บตำแหน่ง/หมุนของแต่ละ limb แบบ "สัมพัทธ์กับ bodyRoot" ตอนเริ่มเกม
    // ใช้คำนวณตำแหน่งเป้าหมายตอน teleport แทนที่จะปล่อยให้ joint ดึงเอง (ซึ่งพังถ้าระยะไกล)
    private Vector3[] limbLocalPosOffset;
    private Quaternion[] limbLocalRotOffset;

    // เก็บ checkpoint ปัจจุบันแบบ sync ให้ทุก client รู้ตรงกัน (เผื่อ UI แสดงผล)
    private NetworkVariable<Vector3> currentCheckpointPos = new NetworkVariable<Vector3>();
    private NetworkVariable<Quaternion> currentCheckpointRot = new NetworkVariable<Quaternion>();

    private void Awake()
    {
        Instance = this;
        CacheReferences();
    }

    private void CacheReferences()
    {
        bodyRootNetTransform = bodyRoot != null ? bodyRoot.GetComponent<NetworkTransform>() : null;

        // สคริปต์เกมเพลย์ทั้งหมดใต้ร่าง — เอาไว้รีเซ็ต state หลัง respawn
        if (bodyRoot != null)
        {
            torsoMovement = bodyRoot.GetComponentInChildren<TorsoMovement>(true);
            feet = bodyRoot.GetComponentsInChildren<PlayerFootForRobot>(true);
            hands = bodyRoot.GetComponentsInChildren<PlayerHandMovement>(true);
        }
        else
        {
            feet = new PlayerFootForRobot[0];
            hands = new PlayerHandMovement[0];
        }

        int n = limbRigidbodies?.Length ?? 0;
        limbNetTransforms = new NetworkTransform[n];
        limbLocalPosOffset = new Vector3[n];
        limbLocalRotOffset = new Quaternion[n];

        for (int i = 0; i < n; i++)
        {
            var rb = limbRigidbodies[i];
            if (rb == null) continue;

            limbNetTransforms[i] = rb.GetComponent<NetworkTransform>();

            // เก็บ offset สัมพัทธ์กับ bodyRoot ตอนเริ่มเกม (ตอนร่างอยู่ในท่ายืน/ท่าเริ่มต้นที่ถูกต้อง)
            if (bodyRoot != null)
            {
                limbLocalPosOffset[i] = bodyRoot.InverseTransformPoint(rb.position);
                limbLocalRotOffset[i] = Quaternion.Inverse(bodyRoot.rotation) * rb.rotation;
            }
        }

        // เก็บ joint ทุกตัวใต้ bodyRoot (รวมลูกทั้งหมด) เพื่อปลด/ต่อ connectedBody ตอน teleport
        // (Joint สืบทอดจาก Component ตรงๆ ไม่ใช่ Behaviour เลยไม่มี .enabled ให้ปิด/เปิดแบบ Collider/Renderer)
        if (bodyRoot != null)
        {
            allJoints = bodyRoot.GetComponentsInChildren<Joint>(includeInactive: true);
        }
        else
        {
            allJoints = new Joint[0];
        }

        jointConnectedBodies = new Rigidbody[allJoints.Length];
        for (int i = 0; i < allJoints.Length; i++)
        {
            if (allJoints[i] != null)
                jointConnectedBodies[i] = allJoints[i].connectedBody;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer && defaultSpawnPoint != null)
        {
            currentCheckpointPos.Value = defaultSpawnPoint.position;
            currentCheckpointRot.Value = defaultSpawnPoint.rotation;
        }
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    /// <summary>
    /// เรียกจาก CheckpointZone เมื่อ server ตรวจพบว่าร่างเดินผ่านจุดเซฟใหม่
    /// </summary>
    public void SetCheckpoint(Vector3 pos, Quaternion rot)
    {
        if (!IsServer) return;
        currentCheckpointPos.Value = pos;
        currentCheckpointRot.Value = rot;
    }

    /// <summary>
    /// เรียกจาก FallDeathZone (หรือระบบตรวจ HP อื่นๆ) เมื่อร่างต้อง respawn
    /// รีเซ็ตตำแหน่ง + หยุดความเร็วทุก limb เพื่อไม่ให้ร่างยังพุ่งต่อหลัง teleport
    /// </summary>
    public void RespawnBody()
    {
        if (!IsServer) return;

        // 🛡️ [Debounce] ร่างตกเหวทั้งตัว = หลาย limb แตะ death zone ห่างกันไม่กี่เฟรม
        // กันไม่ให้ teleport + ปลด/ต่อ joint วนซ้ำรัวๆ
        if (Time.time - _lastRespawnTime < respawnCooldown) return;
        _lastRespawnTime = Time.time;

        Vector3 pos = currentCheckpointPos.Value;
        Quaternion rot = currentCheckpointRot.Value;

        // 0) ปล่อย grab ของมือทุกข้างก่อน — FixedJoint ที่จับกำแพงถูกสร้าง runtime
        //    ไม่อยู่ใน allJoints ที่ cache ไว้ ถ้าไม่ปล่อย มือจะวาร์ปไปพร้อม joint
        //    ที่ยังเกาะกำแพงเดิม → constraint ระเบิด
        if (hands != null)
            foreach (var hand in hands)
                if (hand != null) hand.ServerReleaseGrab();

        // 1) ปลด connectedBody ของ joint ทั้งหมดก่อน ป้องกัน physics solver พยายามแก้ constraint
        //    ระยะไกลด้วยแรงมหาศาลในเฟรมเดียว (อาการ "เด้ง/ค้าง" หลัง teleport)
        DetachJoints();

        // 2) ย้าย root ก่อน — ใช้ Teleport() ถ้ามี NetworkTransform เพื่อบอกระบบว่านี่คือ
        //    การกระโดดตำแหน่งทันที ไม่ใช่การเคลื่อนที่ปกติที่ต้อง interpolate
        TeleportTransform(bodyRoot, bodyRootNetTransform, pos, rot);

        // 3) Server teleport ทุก limb เองหมด — โปรเจกต์นี้ limb เป็น server-authoritative
        //    (ฟิสิกส์รันบน server, NetworkTransform sync ลง client / ChangeOwnership แค่บอกว่า
        //    ใครคุม input) เวอร์ชันก่อนส่ง RPC ให้ owner teleport เอง ซึ่งใช้ไม่ได้:
        //    client ไม่มี authority บน NetworkTransform → Teleport ฝั่งนั้นถูกปัดทิ้ง
        //    ผลคือแขนขาผู้เล่นค้างที่จุดตายในขณะที่ลำตัววาร์ปไปแล้ว
        for (int i = 0; i < limbRigidbodies.Length; i++)
        {
            var rb = limbRigidbodies[i];
            if (rb == null) continue;

            // คำนวณตำแหน่งเป้าหมายจาก offset ที่เก็บไว้ตอน Awake แทนที่จะปล่อยให้ joint ดึงเอง
            Vector3 targetPos = pos + rot * limbLocalPosOffset[i];
            Quaternion targetRot = rot * limbLocalRotOffset[i];

            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            TeleportTransform(rb.transform, limbNetTransforms[i], targetPos, targetRot);
        }

        // 4) ต่อ connectedBody กลับหลังจากทุกชิ้นอยู่ในตำแหน่งที่ถูกต้องแล้ว
        //    การ set connectedBody ใหม่ทำให้ joint คำนวณ anchor จากตำแหน่งปัจจุบัน แทนที่จะจำ
        //    ตำแหน่งเก่าไว้แล้วพยายามดึงกลับด้วยแรงมหาศาล
        ReattachJoints();

        // 5) รีเซ็ต gameplay state — สำคัญมาก:
        //    เท้า: plantedPosition เก่าชี้จุดตกเหว ถ้าไม่ล้าง PerformStandingPhysics
        //          จะ MovePosition ลากเท้ากลับไปก้นเหวในเฟรมถัดไปทันที
        //    torso: ล้าง stress/timer ทั้งหมด แล้วตั้งกลับเป็นท่ายืน (limb ถูกจัดท่ายืนให้แล้ว)
        if (feet != null)
            foreach (var foot in feet)
                if (foot != null) foot.ResetForRespawn();

        if (hands != null)
            foreach (var hand in hands)
                if (hand != null) hand.ResetForRespawn();

        if (torsoMovement != null) torsoMovement.ResetForRespawn();

        RespawnFeedbackClientRpc();
    }

    /// <summary>
    /// ปลด connectedBody ของทุก joint ชั่วคราว (แทนการ disable ที่ Joint ไม่รองรับ)
    /// ทำให้ joint หยุดพยายามยึดตำแหน่งเดิมไว้ระหว่าง teleport
    /// </summary>
    private void DetachJoints()
    {
        if (allJoints == null) return;
        foreach (var j in allJoints)
        {
            if (j != null) j.connectedBody = null;
        }
    }

    /// <summary>
    /// ต่อ connectedBody ของทุก joint กลับเป็นค่าเดิมหลัง teleport เสร็จ
    /// การ set ใหม่ทำให้ joint คำนวณ anchor จากตำแหน่งปัจจุบันแทนที่จะจำค่าเก่าไว้
    /// </summary>
    private void ReattachJoints()
    {
        if (allJoints == null) return;
        for (int i = 0; i < allJoints.Length; i++)
        {
            if (allJoints[i] != null)
                allJoints[i].connectedBody = jointConnectedBodies[i];
        }
    }

    /// <summary>
    /// Teleport แบบไม่ให้ NetworkTransform พยายาม interpolate — ถ้ามี NetworkTransform
    /// ใช้ Teleport() ตรงๆ (ต้องเรียกฝั่งที่มี authority เท่านั้น), ถ้าไม่มีก็ set transform ปกติ
    /// </summary>
    private static void TeleportTransform(Transform t, NetworkTransform netTransform,
        Vector3 pos, Quaternion rot)
    {
        if (t == null) return;

        if (netTransform != null)
        {
            netTransform.Teleport(pos, rot, t.localScale);
        }
        else
        {
            t.SetPositionAndRotation(pos, rot);
        }
    }

    // ให้ client เล่นเอฟเฟกต์ตอน respawn เช่น fade กล้อง / เสียง
    [ClientRpc]
    private void RespawnFeedbackClientRpc()
    {
        Debug.Log("ร่างกาย respawn กลับไปที่เช็คพอยต์แล้ว");
        // TODO: เรียก UI fade, particle, sound ที่นี่
    }
}
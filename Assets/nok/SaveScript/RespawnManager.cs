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

    // แคชไว้ตอน Awake เพื่อไม่ต้อง GetComponent ซ้ำทุกครั้งที่ respawn
    private NetworkTransform bodyRootNetTransform;
    private NetworkTransform[] limbNetTransforms;   // null ได้ถ้า limb ไม่มี NetworkTransform เป็นของตัวเอง
    private NetworkObject[] limbNetworkObjects;      // null ได้ถ้า limb ไม่มี NetworkObject แยกจาก root
    private Joint[] allJoints;                       // ทุก joint ใต้ bodyRoot (รวม limb ทั้งหมด)
    private Rigidbody[] jointConnectedBodies;         // connectedBody เดิมของแต่ละ joint (Joint ไม่มี .enabled ให้ใช้)

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

        int n = limbRigidbodies?.Length ?? 0;
        limbNetTransforms = new NetworkTransform[n];
        limbNetworkObjects = new NetworkObject[n];
        limbLocalPosOffset = new Vector3[n];
        limbLocalRotOffset = new Quaternion[n];

        for (int i = 0; i < n; i++)
        {
            var rb = limbRigidbodies[i];
            if (rb == null) continue;

            limbNetTransforms[i] = rb.GetComponent<NetworkTransform>();
            limbNetworkObjects[i] = rb.GetComponent<NetworkObject>();

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
        if (IsServer && defaultSpawnPoint != null)
        {
            currentCheckpointPos.Value = defaultSpawnPoint.position;
            currentCheckpointRot.Value = defaultSpawnPoint.rotation;
        }
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

        Vector3 pos = currentCheckpointPos.Value;
        Quaternion rot = currentCheckpointRot.Value;

        // 1) ปลด connectedBody ของ joint ทั้งหมดก่อน ป้องกัน physics solver พยายามแก้ constraint
        //    ระยะไกลด้วยแรงมหาศาลในเฟรมเดียว (อาการ "เด้ง/ค้าง" หลัง teleport)
        DetachJoints();

        // 2) ย้าย root ก่อน — ใช้ Teleport() ถ้ามี NetworkTransform เพื่อบอกระบบว่านี่คือ
        //    การกระโดดตำแหน่งทันที ไม่ใช่การเคลื่อนที่ปกติที่ต้อง interpolate
        TeleportTransform(bodyRoot, bodyRootNetTransform, pos, rot);

        var limbTeleportRpcTargets = new Dictionary<ulong, List<int>>();

        for (int i = 0; i < limbRigidbodies.Length; i++)
        {
            var rb = limbRigidbodies[i];
            if (rb == null) continue;

            // คำนวณตำแหน่งเป้าหมายจาก offset ที่เก็บไว้ตอน Awake แทนที่จะปล่อยให้ joint ดึงเอง
            Vector3 targetPos = pos + rot * limbLocalPosOffset[i];
            Quaternion targetRot = rot * limbLocalRotOffset[i];

            var netObj = limbNetworkObjects[i];

            // 3) ถ้า limb นี้มี NetworkObject แยกและ owner ไม่ใช่ server (host) เอง
            //    server เขียนทับ Rigidbody ตรงๆ จะไม่มีผล เพราะ owner client ยัง simulate
            //    ต่อแล้วส่งค่ากลับมาทับ — ต้องสั่งให้ owner เป็นคน teleport ของตัวเองผ่าน RPC
            if (netObj != null && !netObj.IsOwnedByServer)
            {
                ulong ownerId = netObj.OwnerClientId;
                if (!limbTeleportRpcTargets.TryGetValue(ownerId, out var indices))
                {
                    indices = new List<int>();
                    limbTeleportRpcTargets[ownerId] = indices;
                }
                indices.Add(i);
            }
            else
            {
                // server-authoritative (หรือไม่มี NetworkObject แยก) — teleport ตรงนี้ได้เลย
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                TeleportTransform(rb.transform, limbNetTransforms[i], targetPos, targetRot);
            }
        }

        // ส่ง RPC แยกตาม owner client แต่ละคน พร้อมตำแหน่งเป้าหมายของ limb ที่เขาคุมอยู่
        foreach (var kvp in limbTeleportRpcTargets)
        {
            ulong ownerId = kvp.Key;
            List<int> indices = kvp.Value;

            var limbIndices = indices.ToArray();
            var targetPositions = new Vector3[limbIndices.Length];
            var targetRotations = new Quaternion[limbIndices.Length];

            for (int k = 0; k < limbIndices.Length; k++)
            {
                int i = limbIndices[k];
                targetPositions[k] = pos + rot * limbLocalPosOffset[i];
                targetRotations[k] = rot * limbLocalRotOffset[i];
            }

            var rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { ownerId } }
            };

            TeleportLimbsClientRpc(limbIndices, targetPositions, targetRotations, rpcParams);
        }

        // 4) ต่อ connectedBody กลับหลังจากทุกชิ้นอยู่ในตำแหน่งที่ถูกต้องแล้ว
        //    การ set connectedBody ใหม่ทำให้ joint คำนวณ anchor จากตำแหน่งปัจจุบัน แทนที่จะจำ
        //    ตำแหน่งเก่าไว้แล้วพยายามดึงกลับด้วยแรงมหาศาล (สำหรับ limb ที่ต้องรอ RPC ไปถึง owner ก่อน
        //    ให้หน่วง 1 เฟรมทาง physics ได้ — joint ยืดหยุ่นพอที่จะรับช่วงสั้นๆ นี้)
        ReattachJoints();

        RespawnFeedbackClientRpc();
    }

    /// <summary>
    /// เรียกที่ตัว owner client ของ limb แต่ละอัน ให้ teleport Rigidbody ของตัวเอง
    /// เพราะถ้า NetworkTransform เป็น owner-authoritative แล้ว server สั่งตรงจะไม่มีผล
    /// </summary>
    [ClientRpc]
    private void TeleportLimbsClientRpc(int[] limbIndices, Vector3[] targetPositions,
        Quaternion[] targetRotations, ClientRpcParams rpcParams = default)
    {
        for (int k = 0; k < limbIndices.Length; k++)
        {
            int i = limbIndices[k];
            if (i < 0 || i >= limbRigidbodies.Length) continue;

            var rb = limbRigidbodies[i];
            if (rb == null) continue;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            TeleportTransform(rb.transform, limbNetTransforms[i], targetPositions[k], targetRotations[k]);
        }
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
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// วางสคริปต์นี้ไว้บน NetworkObject ตัวเดียวในซีน (เช่น GameManager)
/// ทำหน้าที่เก็บตำแหน่งเช็คพอยต์ล่าสุด และรีเซ็ต "ร่างกาย" (Body root + limbs)
/// กลับไปที่เช็คพอยต์เมื่อร่างตก/ตาย
///
/// สมมติฐานโครงสร้าง:
/// - มี GameObject ชื่อ "Body" เป็น root, มี Rigidbody หลัก (อาจเป็น kinematic หรือไม่ก็ได้)
/// - ใต้ Body มี limb ต่างๆ (ArmL, ArmR, LegL, LegR) แต่ละอันมี Rigidbody ของตัวเอง
///   เชื่อมกันด้วย ConfigurableJoint/CharacterJoint
/// - แต่ละ limb ควบคุมโดยผู้เล่นคนละคน ผ่าน NetworkObject ที่ตัวเองเป็นเจ้าของ (owner)
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

    // เก็บ checkpoint ปัจจุบันแบบ sync ให้ทุก client รู้ตรงกัน (เผื่อ UI แสดงผล)
    private NetworkVariable<Vector3> currentCheckpointPos = new NetworkVariable<Vector3>();
    private NetworkVariable<Quaternion> currentCheckpointRot = new NetworkVariable<Quaternion>();

    private void Awake()
    {
        Instance = this;
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

        // ย้าย root ก่อน (ถ้า bodyRoot เป็น NetworkObject ที่มี NetworkTransform
        // การเปลี่ยนค่านี้บน server จะ sync ไปทุก client อัตโนมัติ)
        bodyRoot.SetPositionAndRotation(pos, rot);

        foreach (var rb in limbRigidbodies)
        {
            if (rb == null) continue;

            // ปิดความเร็ว/แรงหมุนค้างไว้ทั้งหมด ป้องกันร่างยังกระเด็นหลัง teleport
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            // ถ้า limb มี offset จาก root คงที่ (joint จะดึงกลับเข้าที่เอง)
            // ไม่จำเป็นต้อง set ตำแหน่งแต่ละ limb ตรงๆ เพราะ joint จะทำให้เอง
            // แต่ถ้า joint หลุด/พังตอนตก แนะนำ reset local position ด้วย:
            // rb.transform.localPosition = originalLocalPosition[rb];
        }

        RespawnFeedbackClientRpc();
    }

    // ให้ client เล่นเอฟเฟกต์ตอน respawn เช่น fade กล้อง / เสียง
    [ClientRpc]
    private void RespawnFeedbackClientRpc()
    {
        Debug.Log("ร่างกาย respawn กลับไปที่เช็คพอยต์แล้ว");
        // TODO: เรียก UI fade, particle, sound ที่นี่
    }
}
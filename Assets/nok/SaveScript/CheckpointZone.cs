using UnityEngine;

/// <summary>
/// วางบน GameObject ที่มี Collider (isTrigger = true) เป็นจุดเซฟในด่าน
/// ให้ส่วนไหนของร่างกาย (แขน/ขา) เดินผ่านก็ได้ ไม่จำเป็นต้องเป็น "เท้า" เท่านั้น
/// แต่ถ้าต้องการ "ต้องเหยียบด้วยขา/เท้าเท่านั้น" ให้เช็ค tag เพิ่ม (ดูคอมเมนต์ด้านล่าง)
/// </summary>
[RequireComponent(typeof(Collider))]
public class CheckpointZone : MonoBehaviour
{
    [Tooltip("ถ้าต้องการให้เฉพาะ 'เท้า/ขา' เหยียบถึงจะนับ ให้ตั้ง tag limb เป็น Foot แล้วใส่ตรงนี้")]
    [SerializeField] private string requiredTag = ""; // เว้นว่าง = รับทุกส่วนของร่างกาย

    [Tooltip("จุด teleport กลับเมื่อ respawn (ปกติตั้งตำแหน่งเดียวกับ zone หรือสูงกว่าพื้นเล็กน้อย)")]
    [SerializeField] private Transform respawnPoint;

    private bool alreadyTriggered = false;

    private void OnTriggerEnter(Collider other)
    {
        // ให้ logic นี้ทำงานเฉพาะฝั่ง server เท่านั้น (server-authoritative)
        // ถ้าใช้ Netcode for GameObjects, NetworkManager.Singleton.IsServer จะ true เฉพาะบนเครื่อง server/host
        if (NetworkCheck.IsServerOrHost() == false) return;

        // ✅ ต้องเป็นชิ้นส่วนของหุ่นผู้เล่นเท่านั้น — เดิม tag ว่าง = รับทุก collider
        // ทำให้ Enemy AI เดินผ่านจุดเซฟแล้ว checkpoint ถูกตั้งทั้งที่ผู้เล่นยังไม่เคยไปถึง
        if (!RobotBodyCheck.IsRobotBodyPart(other)) return;

        // ตัวกรองเสริม (ถ้าตั้ง tag ไว้): เช่นให้เฉพาะเท้าเหยียบเท่านั้น
        if (!string.IsNullOrEmpty(requiredTag) && !other.CompareTag(requiredTag))
            return;

        // ป้องกันการ trigger ซ้ำรัวๆ ถ้าต้องการให้เซฟได้ครั้งเดียวต่อจุด
        // (เอาออกได้ถ้าต้องการให้เดินผ่านซ้ำแล้วอัปเดตตำแหน่งใหม่ได้เรื่อยๆ)
        if (alreadyTriggered) return;

        if (RespawnManager.Instance == null)
        {
            Debug.LogWarning("[CheckpointZone] RespawnManager ไม่อยู่ในฉาก — checkpoint ไม่ถูกบันทึก");
            return;
        }

        Transform point = respawnPoint != null ? respawnPoint : transform;
        RespawnManager.Instance.SetCheckpoint(point.position, point.rotation);

        alreadyTriggered = true; // ลบบรรทัดนี้ถ้าอยากให้ trigger ซ้ำได้
        Debug.Log($"Checkpoint set: {point.position}");
    }
}
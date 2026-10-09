using Nsc.Robots;
using UnityEngine;

namespace Nsc.Respawn
{
    /// <summary>
    /// จุดเซฟในด่าน (Collider แบบ isTrigger) — ชิ้นส่วนไหนของหุ่นผ่านก็นับ
    /// ตั้ง requiredTag ถ้าอยากให้นับเฉพาะชิ้นที่ติด tag นั้น (เช่นเท้า)
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CheckpointZone : MonoBehaviour
    {
        [Tooltip("ถ้าต้องการให้เฉพาะ 'เท้า/ขา' เหยียบถึงจะนับ ให้ตั้ง tag ของชิ้นนั้นแล้วใส่ตรงนี้ (ว่าง = ทุกชิ้น)")]
        [SerializeField] private string requiredTag = "";

        [Tooltip("จุด teleport กลับเมื่อ respawn (ว่าง = ตำแหน่งของ zone นี้)")]
        [SerializeField] private Transform respawnPoint;

        private bool triggered;

        private void OnTriggerEnter(Collider other)
        {
            if (triggered || !NetworkCheck.IsServerOrHost()) return;

            // ต้องเป็นชิ้นส่วนของหุ่นเท่านั้น — กันบอสเดินผ่านจุดเซฟแล้ว checkpoint ถูกตั้งก่อนผู้เล่นไปถึง
            if (Robot.FromCollider(other) == null) return;
            if (!string.IsNullOrEmpty(requiredTag) && !other.CompareTag(requiredTag)) return;

            if (RespawnManager.Instance == null)
            {
                Debug.LogWarning("[CheckpointZone] RespawnManager ไม่อยู่ในฉาก — checkpoint ไม่ถูกบันทึก");
                return;
            }

            Transform point = respawnPoint != null ? respawnPoint : transform;
            RespawnManager.Instance.SetCheckpoint(new Pose(point.position, point.rotation));
            triggered = true; // เซฟได้ครั้งเดียวต่อจุด
            Debug.Log($"Checkpoint set: {point.position}");
        }
    }
}

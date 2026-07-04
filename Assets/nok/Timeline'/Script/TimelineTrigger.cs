using Unity.Netcode;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// ออปเจคที่ต้อง "เหยียบ" (หรือส่วนไหนของร่างกายสัมผัสก็ได้ ตาม requiredTag)
/// แล้วสั่งเล่น Timeline (PlayableDirector) พร้อมกันทุกเครื่อง
///
/// วางสคริปต์นี้บน GameObject ที่มี:
/// - Collider (isTrigger = true)
/// - NetworkObject (จำเป็น เพราะต้องยิง ClientRpc)
/// - PlayableDirector อ้างอิง Timeline ที่ต้องการเล่น
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class TimelineTrigger : NetworkBehaviour
{
    [SerializeField] private PlayableDirector director;
    [SerializeField] private string requiredTag = "BodyPart"; // ปรับเป็น "Foot" ถ้าต้องการให้เหยียบด้วยขาเท่านั้น
    [SerializeField] private bool playOnce = true;

    private bool hasPlayed = false;

    private void OnTriggerEnter(Collider other)
    {
        // ให้ตัดสินใจที่ server เท่านั้น กัน client สั่งเล่นซ้ำไม่ตรงกัน
        if (!IsServer) return;

        if (!string.IsNullOrEmpty(requiredTag) && !other.CompareTag(requiredTag))
            return;

        if (playOnce && hasPlayed) return;

        hasPlayed = true;
        PlayTimelineClientRpc();
    }

    [ClientRpc]
    private void PlayTimelineClientRpc()
    {
        // ทำงานทั้งบน server และทุก client ทำให้ทุกคนเห็น Timeline เล่นพร้อมกัน
        if (director != null)
        {
            director.time = 0;
            director.Play();
        }
    }

    // เผื่อกรณีอยากรีเซ็ต trigger นี้ให้เล่นซ้ำได้ (เช่น ปุ่มกด toggle)
    public void ResetTrigger()
    {
        if (!IsServer) return;
        hasPlayed = false;
    }
}
using UnityEngine;

/// <summary>
/// วางเป็น Collider (isTrigger) ขนาดใหญ่คลุมพื้นที่ใต้แมพทั้งหมด
/// เมื่อส่วนใดของร่างกาย (แขน/ขา/ลำตัว) ตกเข้ามาแตะ ให้ respawn ทั้งร่างกลับไปเช็คพอยต์ล่าสุด
///
/// ทางเลือก: จะเช็คด้วย tag "BodyPart" (ต้องตั้ง tag ให้ Rigidbody ของ limb ทุกอันไว้ก่อน)
/// เพื่อกันไม่ให้ของอื่น เช่น ก้อนหินที่ตกในด่าน ไป trigger respawn โดยไม่ได้ตั้งใจ
/// </summary>
[RequireComponent(typeof(Collider))]
public class FallDeathZone : MonoBehaviour
{
    [SerializeField] private string bodyPartTag = "BodyPart";

    private void OnTriggerEnter(Collider other)
    {
        if (NetworkCheck.IsServerOrHost() == false) return;

        if (!other.CompareTag(bodyPartTag)) return;

        Debug.Log($"{other.name} ตกออกนอกแมพ -> respawn ร่างกาย");
        RespawnManager.Instance.RespawnBody();
    }
}
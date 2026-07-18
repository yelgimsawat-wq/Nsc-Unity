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
    private void OnTriggerEnter(Collider other)
    {
        if (NetworkCheck.IsServerOrHost() == false) return;

        // ✅ เช็กจาก component จริงแทน tag — เดิมใช้ tag "BodyPart" ซึ่งถ้าลืมตั้งบน limb
        // แม้ชิ้นเดียว ชิ้นนั้นจะตกทะลุโลกเงียบๆ ไม่มี respawn (แถม tag ที่ไม่ได้สร้างใน
        // TagManager จะ log error ทุกครั้งที่ CompareTag ด้วย)
        if (!RobotBodyCheck.IsRobotBodyPart(other)) return;

        if (RespawnManager.Instance == null)
        {
            Debug.LogWarning("[FallDeathZone] RespawnManager ไม่อยู่ในฉาก — respawn ไม่ทำงาน");
            return;
        }

        Debug.Log($"{other.name} ตกออกนอกแมพ -> respawn ร่างกาย");
        RespawnManager.Instance.RespawnBody();
    }
}
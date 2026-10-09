using Nsc.Robots;
using UnityEngine;

namespace Nsc.Respawn
{
    /// <summary>
    /// Collider (isTrigger) ขนาดใหญ่ใต้แมพ — ชิ้นส่วนใดของหุ่นตกมาแตะ ให้หุ่นทั้งตัว respawn ที่เช็คพอยต์ล่าสุด
    /// เช็คจาก component จริง (Robot) ไม่พึ่ง tag — ลืมตั้ง tag ชิ้นเดียวแล้วชิ้นนั้นตกทะลุโลกเงียบๆ
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class FallDeathZone : MonoBehaviour
    {
        private void OnTriggerEnter(Collider other)
        {
            if (!NetworkCheck.IsServerOrHost()) return;

            Robot robot = Robot.FromCollider(other);
            if (robot == null) return;

            if (RespawnManager.Instance == null)
            {
                Debug.LogWarning("[FallDeathZone] RespawnManager ไม่อยู่ในฉาก — respawn ไม่ทำงาน");
                return;
            }

            Debug.Log($"{other.name} ตกออกนอกแมพ -> respawn ร่างกาย");
            RespawnManager.Instance.RespawnBody(robot);
        }
    }
}

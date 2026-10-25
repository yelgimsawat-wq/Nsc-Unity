using UnityEngine;

namespace Nsc.Combat
{
    /// <summary>
    /// ช่องส่งเอฟเฟกต์ปะทะจาก server ไปทุกเครื่อง — ชิ้นส่วนหุ่น (LimbController) เป็นคน implement
    /// Combat จึงไม่ต้องรู้จักชั้น Limbs (LimbStrike / LimbHealth หาตัวนี้จาก GameObject ของตัวเอง)
    /// </summary>
    public interface IImpactFeedback
    {
        /// <summary>[SERVER] surfaceNormal ชี้ออกจากผิวที่โดน</summary>
        void ServerBroadcastHit(Vector3 point, Vector3 surfaceNormal, float damage);
    }
}

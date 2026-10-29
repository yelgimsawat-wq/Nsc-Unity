using System;
using Unity.Netcode;
using UnityEngine;

namespace Nsc.BoneLimb
{
    /// <summary>เป้าของแขน/ขาหนึ่งข้างที่ซิงก์ผ่าน network: แขน = ตำแหน่งในแกนตัวหุ่น, ขา = ตำแหน่ง/มุมในโลก</summary>
    public struct LimbNetState : INetworkSerializable, IEquatable<LimbNetState>
    {
        public Vector3 pos;
        public Quaternion rot;
        public float grip;
        public bool pull;   // หุ่นนอนอยู่: คลิกค้างดึงชิ้นนี้ไปที่เป้า
        public bool grab;     // แขนค้างปุ่มจับ — Server ยึดของที่แตะมือ
        public bool reaching; // แขนกำลังเอื้อมไปหยิบ — ตัวหุ่นย่อ/ก้ม/พุ่งตาม
        public bool kickPose; // ขาอยู่ในท่าถีบ — Server ไม่ให้ตัวไหลตามเท้านี้
        public bool kick;     // ช่วงพุ่งถีบออก — Server นับดาเมจ
        public bool brace;    // ขาค้าง F — ย่อ / สองขา = ปักหลักกันล้ม

        public static LimbNetState Capture(IKLimb limb) => new LimbNetState
        {
            pos = limb.isArm ? limb.target.localPosition : limb.target.position,
            rot = limb.target.rotation,
            grip = limb.Grip,
            pull = limb.Pull,
            grab = limb.Grab,
            reaching = limb.Reaching,
            kickPose = limb.KickPose,
            kick = limb.Kick,
            brace = limb.Brace,
        };

        /// <summary>เครื่องที่ไม่ได้คุมชิ้นนี้: ไหลเป้าไปหาค่าที่ซิงก์มา</summary>
        public void ApplyTo(IKLimb limb, float t)
        {
            if (limb.isArm) limb.target.localPosition = Vector3.Lerp(limb.target.localPosition, pos, t);
            else limb.target.SetPositionAndRotation(Vector3.Lerp(limb.target.position, pos, t), Quaternion.Slerp(limb.target.rotation, rot, t));
            limb.Grip = grip;
            limb.Pull = pull;
            limb.Grab = grab;
            limb.Reaching = reaching;
            limb.KickPose = kickPose;
            limb.Kick = kick;
            limb.Brace = brace;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref pos);
            s.SerializeValue(ref rot);
            s.SerializeValue(ref grip);
            s.SerializeValue(ref pull);
            s.SerializeValue(ref grab);
            s.SerializeValue(ref reaching);
            s.SerializeValue(ref kickPose);
            s.SerializeValue(ref kick);
            s.SerializeValue(ref brace);
        }

        public bool Equals(LimbNetState o) => pos == o.pos && rot == o.rot && grip == o.grip && pull == o.pull && grab == o.grab && reaching == o.reaching && kickPose == o.kickPose && kick == o.kick && brace == o.brace;
    }
}

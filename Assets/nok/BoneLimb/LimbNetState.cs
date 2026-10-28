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
        public bool kickPose; // ขาอยู่ในท่าถีบ — Server ไม่ให้ตัวไหลตามเท้านี้
        public bool kick;     // ช่วงพุ่งถีบออก — Server นับดาเมจ

        public static LimbNetState Capture(IKLimb limb) => new LimbNetState
        {
            pos = limb.isArm ? limb.target.localPosition : limb.target.position,
            rot = limb.target.rotation,
            grip = limb.Grip,
            pull = limb.Pull,
            kickPose = limb.KickPose,
            kick = limb.Kick,
        };

        /// <summary>เครื่องที่ไม่ได้คุมชิ้นนี้: ไหลเป้าไปหาค่าที่ซิงก์มา</summary>
        public void ApplyTo(IKLimb limb, float t)
        {
            if (limb.isArm) limb.target.localPosition = Vector3.Lerp(limb.target.localPosition, pos, t);
            else limb.target.SetPositionAndRotation(Vector3.Lerp(limb.target.position, pos, t), Quaternion.Slerp(limb.target.rotation, rot, t));
            limb.Grip = grip;
            limb.Pull = pull;
            limb.KickPose = kickPose;
            limb.Kick = kick;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref pos);
            s.SerializeValue(ref rot);
            s.SerializeValue(ref grip);
            s.SerializeValue(ref pull);
            s.SerializeValue(ref kickPose);
            s.SerializeValue(ref kick);
        }

        public bool Equals(LimbNetState o) => pos == o.pos && rot == o.rot && grip == o.grip && pull == o.pull && kickPose == o.kickPose && kick == o.kick;
    }
}

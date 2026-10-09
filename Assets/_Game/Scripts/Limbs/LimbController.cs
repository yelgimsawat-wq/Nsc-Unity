using System;
using System.Collections.Generic;
using Nsc.Robots;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

namespace Nsc.Limbs
{
    /// <summary>
    /// ฐานของตัวขับฟิสิกส์แขน/ขาฝั่ง server — ของที่แขนกับขาใช้ร่วมกัน:
    /// rigidbody ปลายแขนขา, จุดหมุน (ไหล่/สะโพก), สปริงไล่เป้า, การเดินโซ่ข้อต่อ, การรีเซ็ตตอน respawn
    ///
    /// กติกาของชั้นนี้: owner ส่งแค่ "เจตนา" ผ่าน RPC (InvokePermission.Owner เท่านั้น)
    /// server ตรวจค่าแล้วขับฟิสิกส์เอง — client ไม่เคยแตะ rigidbody ของหุ่น
    /// ลำดับชั้นลึกหนึ่งระดับ ห้ามสืบทอดต่อจาก ArmController/LegController
    /// </summary>
    [RequireComponent(typeof(RobotLimb))]
    public abstract class LimbController : NetworkBehaviour
    {
        [Header("References")]
        [FormerlySerializedAs("handRb"), FormerlySerializedAs("footRb")]
        [SerializeField] protected Rigidbody body;

        [FormerlySerializedAs("pivotPoint")]
        [SerializeField] protected Transform pivot;

        protected RobotLimb limb;

        public RobotLimb Limb => limb;
        public Rigidbody Body => body;
        public Transform Pivot => pivot;

        /// <summary>ลำตัวของหุ่นที่ชิ้นนี้สังกัด</summary>
        protected TorsoBalance Torso => limb != null && limb.Owner != null ? limb.Owner.Torso : null;

        /// <summary>ลำตัวล้มอยู่ไหม — แขนขาเปลี่ยนโหมดฟิสิกส์ตามนี้</summary>
        protected bool IsTorsoDown => Torso != null && Torso.IsRagdoll;

        /// <summary>ยิงฝั่ง owner เมื่อ server สั่งล้างจุดเล็ง (หลัง respawn) — ตัว *Input ฟังแล้วรีเซ็ตตัวเอง</summary>
        public event Action AimResetRequested;

        protected virtual void Awake()
        {
            limb = GetComponent<RobotLimb>();
        }

        /// <summary>[SERVER] หลัง teleport กลับเช็คพอยต์ — ล้างเป้าเก่าที่ยังชี้จุดตก</summary>
        public abstract void ServerResetForRespawn();

        /// <summary>แรงสปริงที่พา body ไปหาเป้า: ความเร็วที่ต้องการ (มีเพดาน) ลบความเร็วจริง คูณ damper</summary>
        protected Vector3 SpringForce(Vector3 target, float speed, float damper, float maxSpeed = float.PositiveInfinity)
        {
            Vector3 velocityTarget = Vector3.ClampMagnitude((target - body.position) * speed, maxSpeed);
            return (velocityTarget - body.linearVelocity) * damper;
        }

        protected void DriveToward(Vector3 target, float speed, float damper, float maxSpeed = float.PositiveInfinity)
        {
            body.AddForce(SpringForce(target, speed, damper, maxSpeed), ForceMode.Acceleration);
        }

        /// <summary>
        /// rigidbody ทั้งโซ่ของแขนขาชิ้นนี้ ตั้งแต่ปลาย (มือ/เท้า) ไล่ตามข้อต่อจนถึงก่อนลำตัว
        /// skipJoint = ข้อต่อที่ห้ามใช้เดินต่อ (FixedJoint ตอนจับของชี้ไปหาของที่จับ ไม่ใช่ลำตัว)
        /// </summary>
        protected List<Rigidbody> CollectChainBodies(Joint skipJoint = null)
        {
            var chain = new List<Rigidbody>();
            Rigidbody stop = Torso != null ? Torso.Body : null;
            Rigidbody current = body;
            int safety = 0;

            while (current != null && current != stop && safety++ < 8)
            {
                chain.Add(current);

                Rigidbody next = null;
                foreach (Joint joint in current.GetComponents<Joint>())
                {
                    if (joint == null || joint == skipJoint || joint.connectedBody == null) continue;
                    next = joint.connectedBody;
                    break;
                }
                current = next;
            }
            return chain;
        }

        /// <summary>ให้ owner ล้างจุดเล็ง/สถานะปุ่มในเครื่องตัวเอง — ไม่งั้นเป้าเก่าถูกส่งกลับมาทับในเฟรมถัดไป</summary>
        [Rpc(SendTo.Owner, Delivery = RpcDelivery.Reliable)]
        protected void ResetAimRpc() => AimResetRequested?.Invoke();

        protected void RequestOwnerAimReset()
        {
            if (IsSpawned) ResetAimRpc();
            else AimResetRequested?.Invoke();
        }
    }
}

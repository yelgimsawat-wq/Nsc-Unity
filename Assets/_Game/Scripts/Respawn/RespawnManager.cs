using Nsc.Robots;
using Unity.Netcode;
using UnityEngine;

namespace Nsc.Respawn
{
    /// <summary>
    /// เก็บเช็คพอยต์ล่าสุดของฉาก แล้วสั่งหุ่นเกิดใหม่ที่นั่นเมื่อตกแมพ — วางบน NetworkObject ตัวเดียวในซีน
    /// ไม่รู้จักคลาสข้างในหุ่นแล้ว: การย้ายทุกชิ้นและการรีเซ็ต state เป็นหน้าที่ของ Robot.ServerResetForRespawn
    /// </summary>
    public class RespawnManager : NetworkBehaviour
    {
        public static RespawnManager Instance { get; private set; }

        [Tooltip("หุ่นที่ต้อง respawn — ว่างไว้ = หุ่นตัวแรกในฉาก (ฉากพาร์คัวร์มีหุ่นตัวเดียว)")]
        [SerializeField] private Robot robot;

        [Tooltip("จุดเกิดเริ่มต้น ถ้ายังไม่เคยเหยียบเช็คพอยต์ไหนเลย")]
        [SerializeField] private Transform defaultSpawnPoint;

        [Tooltip("กัน respawn ซ้ำรัวๆ — ร่างตกเหวทั้งตัว แขนขาทยอยแตะ death zone ห่างกันไม่กี่เฟรม")]
        [SerializeField] private float respawnCooldown = 1f;

        // sync ให้ทุกเครื่องรู้ตรงกัน (เผื่อ UI แสดงผล)
        private readonly NetworkVariable<Vector3> checkpoint = new NetworkVariable<Vector3>();
        private readonly NetworkVariable<Quaternion> checkpointRotation = new NetworkVariable<Quaternion>(Quaternion.identity);

        private float lastRespawnTime = -999f;

        public Pose Checkpoint => new Pose(checkpoint.Value, checkpointRotation.Value);

        private void Awake() => Instance = this;

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer && defaultSpawnPoint != null)
                SetCheckpoint(new Pose(defaultSpawnPoint.position, defaultSpawnPoint.rotation));
        }

        /// <summary>[SERVER] CheckpointZone เรียกเมื่อหุ่นเดินผ่านจุดเซฟใหม่</summary>
        public void SetCheckpoint(Pose point)
        {
            if (!IsServer) return;
            checkpoint.Value = point.position;
            checkpointRotation.Value = point.rotation;
        }

        /// <summary>[SERVER] FallDeathZone เรียกเมื่อชิ้นส่วนของหุ่นตกออกนอกแมพ</summary>
        public void RespawnBody(Robot fallen = null)
        {
            if (!IsServer) return;

            // 🛡️ [Debounce] ร่างตกเหวทั้งตัว = หลายชิ้นแตะ death zone ห่างกันไม่กี่เฟรม
            if (Time.time - lastRespawnTime < respawnCooldown) return;
            lastRespawnTime = Time.time;

            Robot target = fallen != null ? fallen : robot;
            if (target == null && Robot.All.Count > 0) target = Robot.All[0];
            if (target == null)
            {
                Debug.LogWarning("[RespawnManager] ไม่มีหุ่นในฉากให้ respawn", this);
                return;
            }

            target.ServerResetForRespawn(Checkpoint);
            RespawnFeedbackRpc();
        }

        // ให้ client เล่นเอฟเฟกต์ตอน respawn (fade กล้อง / เสียง)
        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        private void RespawnFeedbackRpc()
        {
            Debug.Log("ร่างกาย respawn กลับไปที่เช็คพอยต์แล้ว");
        }
    }
}

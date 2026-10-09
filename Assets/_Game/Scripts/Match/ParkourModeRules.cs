using Nsc.Robots;
using Unity.Netcode;
using UnityEngine;

namespace Nsc.Match
{
    /// <summary>
    /// กติกาโหมดพาร์คัวร์ (เดิมคือส่วนกติกาของ ParkourFlowManager — GUID เดิม):
    /// จบด่านเมื่อหุ่นแตะเส้นชัย (ParkourGoalZone) — ผลลัพธ์คือเวลาและความสูงสูงสุดที่ไต่ได้
    /// โหมดนี้ไม่มีการแพ้ ตกแมพแล้ว respawn ที่เช็คพอยต์
    /// </summary>
    public class ParkourModeRules : ModeRules
    {
        public static ParkourModeRules Current { get; private set; }

        [Header("Height Tracking")]
        [Tooltip("sync ความสูงเพิ่มทีละกี่เมตร (กัน NetworkVariable ส่งถี่เกิน)")]
        [SerializeField] private float heightSyncStep = 0.5f;

        // ความสูงสูงสุดที่ไต่ได้ระหว่างเล่น sync เป็นสเต็ป (ค่าเป๊ะสุดท้ายอยู่ใน MatchResult)
        private readonly NetworkVariable<float> syncedPeakHeight = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private Transform trackedTorso;
        private float baselineY;
        private float peakHeight;

        public float PeakHeight => IsServer ? peakHeight : syncedPeakHeight.Value;

        private void Awake() => Current = this;

        public override void OnDestroy()
        {
            if (Current == this) Current = null;
            base.OnDestroy();
        }

        protected override void Update()
        {
            base.Update();
            if (!IsServer || session == null || !session.IsPlaying) return;

            // ตั้ง baseline ความสูงตอนเริ่มเล่นจริง
            if (trackedTorso == null)
            {
                Robot robot = Robot.All.Count > 0 ? Robot.All[0] : null;
                if (robot == null || robot.Torso == null || robot.Torso.Body == null) return;
                trackedTorso = robot.Torso.Body.transform;
                baselineY = trackedTorso.position.y;
            }

            float height = trackedTorso.position.y - baselineY;
            if (height <= peakHeight) return;

            peakHeight = height;
            if (peakHeight - syncedPeakHeight.Value >= heightSyncStep)
                syncedPeakHeight.Value = peakHeight;
        }

        protected override void EvaluateDefeat() { }

        /// <summary>[SERVER] หุ่นแตะเส้นชัย</summary>
        public void ServerReachGoal()
        {
            if (!IsServer) return;
            syncedPeakHeight.Value = peakHeight;
            Debug.Log($"[ParkourMode] 🏁 RUN END — height {peakHeight:F1}m");
            EndMatch(true);
        }

        protected override MatchResult BuildResult(bool victory)
        {
            MatchResult result = base.BuildResult(victory);
            result.height = peakHeight;
            return result;
        }
    }
}

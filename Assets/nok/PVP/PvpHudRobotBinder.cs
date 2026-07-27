using Unity.Netcode;
using UnityEngine;

namespace NscGame.Pvp
{
    /// <summary>
    /// บอก <see cref="LocalRobotBinder"/> ว่า "หุ่นของเรา" คือตัวไหนในโหมด PVP
    ///
    /// ทำไมต้องมี:
    ///   binder เดิมหาหุ่นของเราจาก ownership — ใช้ได้ดีตอนมีหุ่นตัวเดียวในฉาก
    ///   แต่ PVP มีสองตัว และ Host มี IsOwner = true กับชิ้นส่วนที่ยังไม่มีใครจอง
    ///   ของหุ่น "ทั้งสองตัว" → binder คว้าตัวแรกที่เจอ ซึ่งอาจเป็นหุ่นฝั่งตรงข้าม
    ///
    ///   อาการที่เกิด: ต่อยศัตรูแล้ว HUD ตัวเองเลือดลด / หุ่นเรายังครบแต่ UI บอกว่าแขนขาหลุด
    ///   (HUD ไปเกาะหุ่นศัตรู เลยรายงานสภาพของศัตรูแทนของเรา)
    ///
    /// วางไว้ที่ไหน: บน GameObject เดียวกับ PlayerHUD (ตัวที่มี LocalRobotBinder)
    /// </summary>
    [RequireComponent(typeof(LocalRobotBinder))]
    public class PvpHudRobotBinder : MonoBehaviour
    {
        [SerializeField, Min(0.1f)]
        [Tooltip("ความถี่ในการเช็คว่าทีมของเราเปลี่ยนไหม (วินาที)")]
        private float checkInterval = 0.5f;

        [SerializeField] private bool debugLog = true;

        private LocalRobotBinder binder;
        private PvpTeam appliedTeam = PvpTeam.None;
        private Transform appliedRoot;
        private float nextCheckTime;

        private void Awake()
        {
            binder = GetComponent<LocalRobotBinder>();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextCheckTime) return;
            nextCheckTime = Time.unscaledTime + checkInterval;

            PvpTeamManager manager = PvpTeamManager.Instance;

            // ไม่ได้อยู่ในฉาก PVP → คืนสิทธิ์ให้ binder เดาเองแบบเดิม
            if (manager == null || !manager.IsSpawned)
            {
                ClearIfNeeded();
                return;
            }

            PvpTeam myTeam = manager.LocalTeam;

            // ยังไม่ได้เลือกทีม → อย่าเพิ่งบังคับ ปล่อยให้ binder ทำงานปกติไปก่อน
            if (myTeam == PvpTeam.None)
            {
                ClearIfNeeded();
                return;
            }

            PvpRobotTeam myRobot = PvpRobotTeam.FindByTeam(myTeam);
            if (myRobot == null || myRobot.RobotRoot == null) return;

            if (myTeam == appliedTeam && myRobot.RobotRoot == appliedRoot) return;

            appliedTeam = myTeam;
            appliedRoot = myRobot.RobotRoot;
            binder.SetPreferredRobotRoot(appliedRoot);

            if (debugLog)
                Debug.Log($"[PVP] 🎯 HUD ผูกกับหุ่นทีม {myTeam.DisplayName()} ('{appliedRoot.name}')", this);
        }

        private void ClearIfNeeded()
        {
            if (appliedTeam == PvpTeam.None && appliedRoot == null) return;

            appliedTeam = PvpTeam.None;
            appliedRoot = null;
            binder.SetPreferredRobotRoot(null);
        }
    }
}

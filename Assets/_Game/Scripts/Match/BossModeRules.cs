using System.Collections.Generic;
using NscGame.Enemy;
using Nsc.Robots;
using Unity.Netcode;
using UnityEngine;

namespace Nsc.Match
{
    /// <summary>
    /// กติกาโหมดบอส (เดิมคือส่วนกติกาของ GameFlowManager — GUID เดิม):
    ///   ชนะ = บอสตาย (ฟังอีเวนต์ EnemyHealth.Died — บอสไม่ต้องรู้จักโหมดเกม)
    ///   แพ้ = แกนพลังงานหุ่นหมด — รั่วตามจำนวนแขนขาที่หลุดอยู่ + ท่าหนักของบอสกระแทกตรงแกน (ServerDamageCore)
    ///   ฮีล = ต่อชิ้นคืน (นิดหน่อย) + เซลล์พลังงานที่บอสหล่นเมื่อโดนตีครบเกณฑ์ → เอามือหุ่นแตะ = เก็บ
    ///   แขนขาไม่ฟื้นเลือดเองในโหมดนี้ (LimbHealth.RegenEnabled)
    /// หน้าจอผลลัพธ์อยู่ที่ MatchResultPanel | แถบแกน + ภาพเซลล์อยู่ที่ BossCoreView
    /// </summary>
    public class BossModeRules : ModeRules
    {
        [Tooltip("บอสของด่าน — ว่างไว้ = หาตัวแรกในฉาก")]
        [SerializeField] private EnemyHealth boss;

        [Header("แกนพลังงาน (หมด = แพ้)")]
        [SerializeField] private float maxCore = 100f;
        [Tooltip("รั่วต่อวินาที ต่อแขนขาหนึ่งชิ้นที่หลุดอยู่")]
        [SerializeField] private float drainPerDetachedLimb = 1f;
        [Tooltip("ต่อชิ้นคืนหนึ่งชิ้น = ฮีลแกนเท่านี้")]
        [SerializeField] private float reattachCoreHeal = 5f;

        [Header("เซลล์พลังงาน (ฮีล)")]
        [Tooltip("บอสโดนตีรวมเท่านี้ = หล่น 1 เซลล์")]
        [SerializeField] private float cellEveryBossDamage = 175f;
        [SerializeField] private float cellCoreHeal = 15f;
        [Tooltip("ฮีลแขนขาชิ้นที่เลือดน้อยสุด (ที่ยังติดอยู่)")]
        [SerializeField] private float cellLimbHeal = 100f;
        [SerializeField] private float cellLifetime = 20f;
        [Tooltip("มือหุ่นเข้าใกล้เซลล์เท่านี้ = เก็บ")]
        [SerializeField] private float cellPickupRadius = 3.5f;
        [Tooltip("เซลล์ลอยสูงจากพื้นเท่านี้ — ระดับที่มือหุ่นเอื้อมถึง")]
        [SerializeField] private float cellHeight = 8f;
        [Tooltip("เซลล์ตกห่างตัวบอสเท่านี้ (สุ่ม) ไปทางหุ่น")]
        [SerializeField] private Vector2 cellDistance = new Vector2(7f, 11f);

        private readonly NetworkVariable<float> core = new NetworkVariable<float>(
            100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public static BossModeRules Current { get; private set; }
        public float Core01 => maxCore > 0f ? Mathf.Clamp01(core.Value / maxCore) : 0f;

        public const float CellFlightTime = 0.8f;

        private struct Cell { public int id; public Vector3 from, to; public float born; }
        private readonly List<Cell> cells = new List<Cell>();
        private int nextCellId;
        private float bossDamage, lastBossHp = -1f;
        private int lastDetached = -1;

        private void Awake() => Current = this;

        protected override void Start()
        {
            base.Start();
            if (boss == null) boss = FindFirstObjectByType<EnemyHealth>();
            if (boss != null) boss.Died += OnBossDied;
            BossCoreView.Create(this);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer) return;
            core.Value = maxCore;
            LimbHealth.RegenEnabled = false;
        }

        public override void OnDestroy()
        {
            if (boss != null) boss.Died -= OnBossDied;
            if (Current == this) Current = null;
            LimbHealth.RegenEnabled = true; // ฉากถัดไป (PVP/Parkour) ฟื้นเลือดตามปกติ
            base.OnDestroy();
        }

        private void OnBossDied() => EndMatch(true);

        protected override void Update()
        {
            base.Update();
            if (!IsServer || session == null || !session.IsPlaying) return;
            TrackBossDamage();
            LeakCore(Time.deltaTime);
        }

        // เช็คเก็บเซลล์หลัง IK วางมือแล้ว — ใน Update มือหุ่น IK ยังอยู่ที่เป้าเฟรมก่อน (ห่างมือจริงได้เกินรัศมีเก็บ)
        private void LateUpdate()
        {
            if (!IsServer || session == null || !session.IsPlaying) return;
            UpdateCells();
        }

        // แพ้ = แกนหมด (เช็คช้ากว่าเฟรมได้ 1 วิตามี defeatCheckInterval)
        protected override void EvaluateDefeat()
        {
            if (core.Value <= 0f) EndMatch(false);
        }

        // รั่วทุกเฟรม (ไม่ใช่ก้อนทุกวินาที) — HUD แยก "ท่าบอสหักแกนก้อนใหญ่" ออกจากรั่วได้ด้วยขนาดการลดต่อเฟรม
        private void LeakCore(float dt)
        {
            int total = 0, detached = 0;
            foreach (Robot robot in Robot.All)
                foreach (LimbSlot slot in LimbSlots.All)
                {
                    RobotLimb limb = robot.GetLimb(slot);
                    if (limb == null) continue;
                    total++;
                    if (!limb.IsAttached) detached++;
                }

            if (lastDetached >= 0 && detached < lastDetached) ServerHealCore(reattachCoreHeal * (lastDetached - detached));
            if (detached != lastDetached) Debug.Log($"[BossMode] limbs detached: {detached}/{total} — แกนรั่ว {detached * drainPerDetachedLimb:F1}/วิ");
            lastDetached = detached;
            if (detached > 0) ServerDamageCore(detached * drainPerDetachedLimb * dt);
        }

        /// <summary>[SERVER] ท่าหนักของบอสกระแทกแกน / แขนขาหลุดรั่ว</summary>
        public void ServerDamageCore(float amount)
        {
            if (!IsServer || amount <= 0f || session == null || !session.IsPlaying) return;
            core.Value = Mathf.Max(0f, core.Value - amount);
        }

        public void ServerHealCore(float amount)
        {
            if (!IsServer || amount <= 0f) return;
            core.Value = Mathf.Min(maxCore, core.Value + amount);
        }

        // ================================================================ เซลล์พลังงาน

        // ทุกเครื่องคิดตำแหน่งเอง: พุ่งโค้งจากอกบอส CellFlightTime วิ แล้วลอยขึ้นลงช้าๆ
        public static Vector3 CellPosition(Vector3 from, Vector3 to, float age)
        {
            if (age < CellFlightTime)
            {
                float k = age / CellFlightTime;
                return Vector3.Lerp(from, to, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 4f);
            }
            return to + Vector3.up * (Mathf.Sin((age - CellFlightTime) * 2f) * 0.4f);
        }

        private void TrackBossDamage()
        {
            if (boss == null) return;
            float hp = boss.CurrentHp.Value;
            if (lastBossHp >= 0f && hp < lastBossHp) bossDamage += lastBossHp - hp;
            lastBossHp = hp;
            if (cellEveryBossDamage <= 0f || hp <= 0f) return;
            while (bossDamage >= cellEveryBossDamage)
            {
                bossDamage -= cellEveryBossDamage;
                ServerDropCells(1);
            }
        }

        /// <summary>[SERVER] บอสหล่นเซลล์ (โดนตีครบเกณฑ์ / เซหลังหุ่นหลุดจากกอด) — ตกห่างตัวบอสไปทางหุ่น</summary>
        public void ServerDropCells(int count)
        {
            if (!IsServer || boss == null) return;
            Vector3 b = boss.transform.position;
            Vector3 toward = Robot.All.Count > 0 ? Vector3.ProjectOnPlane(Robot.All[0].transform.position - b, Vector3.up) : boss.transform.forward;
            if (toward.sqrMagnitude < 0.01f) toward = boss.transform.forward;
            for (int n = 0; n < count; n++)
            {
                Vector3 dir = Quaternion.Euler(0f, Random.Range(-70f, 70f), 0f) * toward.normalized;
                var cell = new Cell
                {
                    id = nextCellId++,
                    from = b + Vector3.up * 8f,
                    to = b + dir * Random.Range(cellDistance.x, cellDistance.y) + Vector3.up * cellHeight,
                    born = Time.time
                };
                cells.Add(cell);
                CellSpawnedRpc(cell.id, cell.from, cell.to);
            }
            Debug.Log($"[BossMode] บอสหล่นเซลล์พลังงาน {count} — ค้างอยู่ {cells.Count}");
        }

        private void UpdateCells()
        {
            for (int c = cells.Count - 1; c >= 0; c--)
            {
                Cell cell = cells[c];
                float age = Time.time - cell.born;
                if (age > cellLifetime) { cells.RemoveAt(c); CellGoneRpc(cell.id, false); continue; }
                if (age < CellFlightTime) continue;
                Vector3 at = CellPosition(cell.from, cell.to, age);
                foreach (Robot robot in Robot.All)
                {
                    if (!TouchedByHand(robot, at)) continue;
                    cells.RemoveAt(c);
                    Collect(robot);
                    CellGoneRpc(cell.id, true);
                    break;
                }
            }
        }

        private bool TouchedByHand(Robot robot, Vector3 at)
        {
            foreach (RobotLimb limb in robot.AttachedLimbs())
                if (limb.Slot.IsArm() && (limb.transform.position - at).sqrMagnitude <= cellPickupRadius * cellPickupRadius) return true;
            return false;
        }

        private void Collect(Robot robot)
        {
            ServerHealCore(cellCoreHeal);
            LimbHealth weakest = null;
            foreach (RobotLimb limb in robot.AttachedLimbs())
                if (limb.Health != null && (weakest == null || limb.Health.HpPercent < weakest.HpPercent)) weakest = limb.Health;
            if (weakest != null) weakest.ServerHeal(cellLimbHeal);
            Debug.Log($"[BossMode] เก็บเซลล์ — แกน {core.Value:F0}/{maxCore:F0}, ฮีล {(weakest ? weakest.name : "-")} +{cellLimbHeal}");
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        private void CellSpawnedRpc(int id, Vector3 from, Vector3 to) { if (BossCoreView.Instance) BossCoreView.Instance.AddCell(id, from, to); }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        private void CellGoneRpc(int id, bool collected) { if (BossCoreView.Instance) BossCoreView.Instance.RemoveCell(id, collected); }
    }
}

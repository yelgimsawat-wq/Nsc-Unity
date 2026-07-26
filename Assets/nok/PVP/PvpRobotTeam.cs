using System;
using System.Collections.Generic;
using NscGame.Enemy; // AttackType
using Unity.Netcode;
using UnityEngine;

namespace NscGame.Pvp
{
    /// <summary>
    /// ป้ายบอกว่า "หุ่นตัวนี้เป็นของทีมไหน" + ตัวเฝ้าดูว่าหุ่นตัวนี้แพ้หรือยัง
    ///
    /// ⚠️ คลาสนี้ไม่มีเลือดของตัวเอง — เลือดใช้ระบบเดิม <see cref="RobotHealth"/> ที่มีอยู่ต่อชิ้นส่วน
    ///    (แต่ละแขน/ขามีเลือดแยกกัน หมดแล้วชิ้นหลุด และเพดานเลือดลดถาวรทุกครั้งที่หลุด)
    ///    หน้าที่ของ PVP คือแค่ "แยกทีม" กับ "ตัดสินว่าหุ่นตัวไหนแพ้"
    ///
    /// วางไว้ที่ไหน:
    ///   วางบน GameObject ที่มี NetworkObject อยู่แล้วภายในหุ่น — แนะนำ "ลำตัว (Torso)"
    ///   เพราะ root ของหุ่นในโปรเจกต์นี้ไม่มี NetworkObject (แต่ละ limb มีของตัวเอง)
    ///   แล้วเซ็ต robotRoot ชี้ไปที่ root ของหุ่นตัวนั้น (ว่างไว้ = หาให้อัตโนมัติ)
    ///
    /// ทำไมต้องมี registry:
    ///   ตอนหมัดชน เรามีแค่ Collider ของชิ้นส่วน ต้องรู้ให้ได้ว่า "ชิ้นนี้เป็นของหุ่นตัวไหน/ทีมไหน"
    ///   FindByPart() ไล่ขึ้น parent จนเจอ root ที่ลงทะเบียนไว้ — ไม่ต้อง GetComponentInParent ทุกเฟรม
    /// </summary>
    public class PvpRobotTeam : NetworkBehaviour
    {
        #region Inspector

        [Header("Team")]
        [Tooltip("หุ่นตัวนี้เป็นของทีมไหน — ต้องไม่ซ้ำกับหุ่นอีกตัวในฉาก")]
        [SerializeField] private PvpTeam team = PvpTeam.Red;

        [Header("References")]
        [Tooltip("Root ของหุ่นตัวนี้ (ตัวที่ครอบ แขน/ขา/ลำตัว ทั้งหมด)\nปล่อยว่าง = หาอัตโนมัติ")]
        [SerializeField] private Transform robotRoot;

        [Header("Defeat Rule")]
        [Tooltip("ชิ้นส่วนต้องหลุดกี่ชิ้นถึงนับว่าแพ้ (4 = ต้องหลุดครบทุกชิ้น)")]
        [SerializeField, Range(1, 4)] private int limbsLostToLose = 4;

        [Tooltip("ความถี่ในการเช็คว่าชิ้นส่วนหลุดหรือยัง (วินาที)")]
        [SerializeField, Min(0.05f)] private float defeatCheckInterval = 0.25f;

        [Header("Debug")]
        [SerializeField] private bool debugLog = true;

        #endregion

        #region Registry (part collider -> robot)

        private static readonly Dictionary<Transform, PvpRobotTeam> _byRoot = new Dictionary<Transform, PvpRobotTeam>();
        private static readonly List<PvpRobotTeam> _all = new List<PvpRobotTeam>();

        /// <summary>ยิงทุกครั้งที่มีหุ่นลงทะเบียน/ถอนทะเบียน — UI ใช้รอจนกว่าหุ่นจะพร้อม</summary>
        public static event Action OnRegistryChanged;

        /// <summary>หาหุ่นเจ้าของชิ้นส่วนนี้ โดยไล่ขึ้น parent จนเจอ root ที่ลงทะเบียนไว้</summary>
        public static PvpRobotTeam FindByPart(Transform part)
        {
            for (Transform t = part; t != null; t = t.parent)
            {
                if (_byRoot.TryGetValue(t, out PvpRobotTeam robot) && robot != null)
                    return robot;
            }
            return null;
        }

        public static PvpRobotTeam FindByTeam(PvpTeam wanted)
        {
            for (int i = 0; i < _all.Count; i++)
            {
                if (_all[i] != null && _all[i].team == wanted)
                    return _all[i];
            }
            return null;
        }

        #endregion

        #region Properties

        public PvpTeam Team => team;
        public Transform RobotRoot => robotRoot;
        public bool IsDefeated => netDefeated.Value;

        /// <summary>จำนวนชิ้นส่วนที่ยังต่ออยู่ (ใช้โชว์ใน UI ได้ถ้าต้องการ)</summary>
        public int LimbsRemaining
        {
            get
            {
                int alive = 0;
                for (int i = 0; i < _limbs.Count; i++)
                    if (_limbs[i] != null && _limbs[i].IsConnected) alive++;
                return alive;
            }
        }

        public int LimbsTotal => _limbs.Count;

        #endregion

        #region Network State

        /// <summary>หุ่นตัวนี้แพ้แล้วหรือยัง — Server เขียน ทุกคนอ่าน</summary>
        private readonly NetworkVariable<bool> netDefeated = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly List<JointPullAndReconnect> _limbs = new List<JointPullAndReconnect>();
        private readonly List<RobotHealth> _limbHealths = new List<RobotHealth>();
        private float _nextDefeatCheckTime;

        #endregion

        #region Unity / Netcode Lifecycle

        private void Awake()
        {
            if (robotRoot == null) robotRoot = ResolveRobotRoot(transform);
            Register();
        }

        // NetworkBehaviour.OnDestroy เป็น virtual และทำ cleanup ภายในของ Netcode อยู่
        // ต้อง override + เรียก base เสมอ ห้ามประกาศ OnDestroy ตัวใหม่ทับ
        public override void OnDestroy()
        {
            Unregister();
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            CacheLimbs();

            if (IsServer)
                netDefeated.Value = false;
        }

        /// <summary>
        /// เก็บ reference ชิ้นส่วนไว้ตั้งแต่ตอน spawn — ห้ามไปไล่หาใหม่ตอนเช็คแพ้
        /// เพราะชิ้นที่หลุดแล้วอาจถูกย้าย parent ออกไปนอกหุ่น (ดีไซน์เดิมของเกม)
        /// ถ้าไล่หาใหม่จะ "หาไม่เจอ" แทนที่จะเห็นว่ามันหลุด
        /// </summary>
        private void CacheLimbs()
        {
            _limbs.Clear();
            _limbHealths.Clear();
            if (robotRoot == null) return;

            robotRoot.GetComponentsInChildren(true, _limbs);
            robotRoot.GetComponentsInChildren(true, _limbHealths);

            if (debugLog)
                Debug.Log($"[PVP] หุ่นทีม {team.DisplayName()} ('{robotRoot.name}') " +
                          $"เจอชิ้นส่วนที่หลุดได้ {_limbs.Count} ชิ้น / มีเลือด {_limbHealths.Count} ชิ้น");
        }

        /// <summary>
        /// [SERVER] ดาเมจที่ลงลำตัว (หรือชิ้นที่ไม่มี RobotHealth ของตัวเอง)
        ///
        /// ทำไมต้องมี: ลำตัวหุ่นไม่มี RobotHealth เพราะมันไม่มี joint ให้หลุด
        /// แต่ผู้เล่นย่อมเล็งลำตัวเพราะเป็นเป้าใหญ่สุด ถ้าไม่นับดาเมจเลยเกมจะรู้สึกพัง
        ///
        /// วิธีคิด: โยนดาเมจเต็มจำนวนให้ชิ้นส่วนที่ "ใกล้จุดปะทะที่สุด" ชิ้นเดียว
        ///
        /// ⚠️ ห้ามหารกระจายให้ทุกชิ้นเท่าๆ กัน — ทุกชิ้นเลือดเท่ากัน (500) ถ้าโดนเท่ากันทุกหมัด
        ///    มันจะถึง 0 พร้อมกันเป๊ะ แล้ว "หลุดหมดทั้ง 4 ชิ้นพร้อมกัน" ซึ่งพังทั้งเกมเพลย์
        ///    (เงื่อนไขชนะคือหลุดครบ 4 → หมัดเดียวจบเกม) และดูไม่เป็นธรรมชาติ
        /// </summary>
        /// <returns>true ถ้ามีชิ้นส่วนรับดาเมจได้จริง</returns>
        public bool ServerApplyBodyDamage(float damage, AttackType source, Vector3 direction, Vector3 hitPoint)
        {
            if (!IsServer || damage <= 0f) return false;

            RobotHealth nearest = null;
            float nearestSqr = float.MaxValue;

            for (int i = 0; i < _limbHealths.Count; i++)
            {
                RobotHealth h = _limbHealths[i];
                if (!IsLimbDamageable(h)) continue; // ชิ้นที่หลุด/พังแล้วไม่รับดาเมจซ้ำ

                float sqr = (h.transform.position - hitPoint).sqrMagnitude;
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = h;
                }
            }

            if (nearest == null) return false;

            float hpBefore = nearest.currentHp.Value;
            nearest.ServerTakeDamage(damage, source, direction);

            if (debugLog)
                Debug.Log($"[PVP] 🫀 ลำตัวทีม {team.DisplayName()} รับ {damage:F1} " +
                          $"→ ลงที่ '{nearest.name}' (ชิ้นใกล้จุดปะทะสุด) | " +
                          $"เลือด {hpBefore:F0} → {nearest.currentHp.Value:F0} / {nearest.currentMaxHp.Value:F0}");

            return true;
        }

        /// <summary>
        /// เลือดรวมของหุ่นตัวนี้ (บวกทุกชิ้นส่วนที่มี RobotHealth)
        ///
        /// ทำไมต้องมี: HullRingUI เดิมโชว์เลือดของ "ชิ้นที่ผู้เล่นคนนี้ถือ" เท่านั้น
        /// ตอนโดนต่อยลำตัวหรือแขนอีกข้าง วงแหวนจึงไม่ขยับ ผู้เล่นเลยรู้สึกว่า "เลือดไม่ลด"
        /// PVP ต้องเห็นสภาพหุ่นทั้งตัวถึงจะรู้ว่าใกล้แพ้แค่ไหน
        ///
        /// ชิ้นที่หลุดแล้วนับเป็น 0 แต่เพดานยังนับอยู่ — หลุดหนึ่งชิ้นคือเสียเลือดไปหนึ่งก้อน
        /// </summary>
        public bool TryGetTotalHp(out float current, out float max)
        {
            current = 0f;
            max = 0f;

            for (int i = 0; i < _limbHealths.Count; i++)
            {
                RobotHealth h = _limbHealths[i];
                if (h == null) continue;

                max += h.currentMaxHp.Value;

                bool detached = h.Jpar != null && !h.Jpar.IsConnected;
                if (!detached) current += Mathf.Max(0f, h.currentHp.Value);
            }

            return max > 0.01f;
        }

        private static bool IsLimbDamageable(RobotHealth health)
        {
            if (health == null) return false;
            if (health.Jpar != null && !health.Jpar.IsConnected) return false;
            return health.currentHp.Value > 0f;
        }

        /// <summary>
        /// หา root ของ "หุ่นตัวนี้" — ไต่ขึ้นไปให้สูงสุดเท่าที่ยังครอบลำตัวเดียวอยู่
        ///
        /// ใช้ transform.root ตรงๆ ไม่ได้ เพราะถ้าเอาหุ่นสองตัวไปไว้ใต้ parent เดียวกัน
        /// (เช่น empty ชื่อ "Robots") ทั้งสองตัวจะได้ root เดียวกัน → ระบบแยกทีมไม่ออก
        /// และตีกันไม่เข้าเลยสักหมัด
        /// </summary>
        private static Transform ResolveRobotRoot(Transform from)
        {
            Transform best = from;

            for (Transform t = from; t != null; t = t.parent)
            {
                if (t.GetComponentsInChildren<TorsoMovement>(true).Length > 1) break;
                best = t;
            }

            return best;
        }

        private void Register()
        {
            if (robotRoot == null) return;
            _byRoot[robotRoot] = this;
            if (!_all.Contains(this)) _all.Add(this);
            OnRegistryChanged?.Invoke();
        }

        private void Unregister()
        {
            if (robotRoot != null && _byRoot.TryGetValue(robotRoot, out PvpRobotTeam r) && r == this)
                _byRoot.Remove(robotRoot);
            _all.Remove(this);
            OnRegistryChanged?.Invoke();
        }

        #endregion

        #region Server — Defeat Watch

        private void Update()
        {
            if (!IsServer || netDefeated.Value) return;
            if (PvpTeamManager.Instance == null || !PvpTeamManager.Instance.IsFighting) return;
            if (Time.time < _nextDefeatCheckTime) return;

            _nextDefeatCheckTime = Time.time + defeatCheckInterval;
            CheckDefeat();
        }

        private void CheckDefeat()
        {
            if (_limbs.Count == 0) return;

            int lost = 0;
            for (int i = 0; i < _limbs.Count; i++)
                if (_limbs[i] == null || !_limbs[i].IsConnected) lost++;

            if (lost < Mathf.Min(limbsLostToLose, _limbs.Count)) return;

            netDefeated.Value = true;

            if (debugLog)
                Debug.Log($"[PVP] ☠️ หุ่นทีม {team.DisplayName()} แพ้แล้ว — ชิ้นส่วนหลุด {lost}/{_limbs.Count} ชิ้น");

            if (PvpTeamManager.Instance != null)
                PvpTeamManager.Instance.ServerReportDefeat(team);
        }

        /// <summary>[SERVER] รีเซ็ตสถานะแพ้ (ใช้ตอนเริ่มแมตช์ใหม่)</summary>
        public void ServerResetDefeat()
        {
            if (!IsServer) return;
            netDefeated.Value = false;
            _nextDefeatCheckTime = 0f;
            CacheLimbs();
        }

        #endregion
    }
}

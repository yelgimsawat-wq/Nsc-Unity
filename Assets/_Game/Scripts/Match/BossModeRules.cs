using NscGame.Enemy;
using Nsc.Robots;
using UnityEngine;

namespace Nsc.Match
{
    /// <summary>
    /// กติกาโหมดบอส (เดิมคือส่วนกติกาของ GameFlowManager — GUID เดิม):
    ///   ชนะ = บอสตาย (ฟังอีเวนต์ EnemyHealth.Died — บอสไม่ต้องรู้จักโหมดเกม)
    ///   แพ้ = แขนขาหลุดพร้อมกันครบตามจำนวนที่ตั้ง
    /// หน้าจอผลลัพธ์อยู่ที่ MatchResultPanel
    /// </summary>
    public class BossModeRules : ModeRules
    {
        [Tooltip("บอสของด่าน — ว่างไว้ = หาตัวแรกในฉาก")]
        [SerializeField] private EnemyHealth boss;

        [Header("Defeat Rule")]
        [Tooltip("จำนวนชิ้นส่วนที่หลุดพร้อมกันแล้วนับว่าแพ้ | 0 = ปิดการเช็คแพ้")]
        [SerializeField] private int defeatDetachedLimbs = 4;

        private int lastLoggedDetached = -1;

        protected override void Start()
        {
            base.Start();
            if (boss == null) boss = FindFirstObjectByType<EnemyHealth>();
            if (boss != null) boss.Died += OnBossDied;
        }

        public override void OnDestroy()
        {
            if (boss != null) boss.Died -= OnBossDied;
            base.OnDestroy();
        }

        private void OnBossDied() => EndMatch(true);

        protected override void EvaluateDefeat()
        {
            if (defeatDetachedLimbs <= 0) return;

            int total = 0, detached = 0;
            foreach (Robot robot in robots)
            {
                foreach (LimbSlot slot in LimbSlots.All)
                {
                    RobotLimb limb = robot.GetLimb(slot);
                    if (limb == null) continue;
                    total++;
                    if (!limb.IsAttached) detached++;
                }
            }

            // log เฉพาะตอนตัวเลขเปลี่ยน — ไว้วินิจฉัยว่าระบบมองเห็นการหลุดไหม
            if (detached != lastLoggedDetached)
            {
                lastLoggedDetached = detached;
                Debug.Log($"[BossMode] limbs detached: {detached}/{total} (need {Mathf.Min(defeatDetachedLimbs, Mathf.Max(1, total))} to lose)");
            }

            // เพดานตามจำนวนชิ้นจริง — ตั้ง 4 แต่ซีนเทสมี 2 ชิ้นก็ยังแพ้ได้ถูกต้อง
            if (total > 0 && detached >= Mathf.Min(defeatDetachedLimbs, total))
                EndMatch(false);
        }
    }
}

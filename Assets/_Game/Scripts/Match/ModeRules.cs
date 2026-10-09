using System.Collections.Generic;
using Nsc.Robots;
using Unity.Netcode;
using UnityEngine;

namespace Nsc.Match
{
    /// <summary>
    /// กติกาแพ้/ชนะของโหมดหนึ่งโหมด (ฝั่ง server) — เพิ่มโหมดใหม่ = เพิ่มคอมโพเนนต์กติกาหนึ่งตัว
    /// ตัดสินผลแล้วส่งให้ MatchSession เท่านั้น ไม่แตะ UI ไม่แตะการเปลี่ยนฉาก
    /// </summary>
    public abstract class ModeRules : NetworkBehaviour
    {
        [Tooltip("ความถี่ในการเช็คเงื่อนไขแพ้ (วินาที)")]
        [SerializeField, Min(0.05f)] private float defeatCheckInterval = 1f;

        protected MatchSession session;
        protected readonly List<Robot> robots = new List<Robot>();

        private float nextDefeatCheckTime;

        protected virtual void Start()
        {
            session = MatchSession.Current;
            if (session == null)
                Debug.LogWarning($"[{GetType().Name}] ไม่มี MatchSession ในฉาก — กติกาของโหมดจะไม่ทำงาน", this);
        }

        protected virtual void Update()
        {
            if (!IsServer || session == null || !session.IsPlaying) return;
            if (Time.time < nextDefeatCheckTime) return;

            nextDefeatCheckTime = Time.time + defeatCheckInterval;

            // อ่านหุ่นสดทุกรอบ — ลอบบี้อาจเพิ่งปิดหุ่นตัวเกินไปตอนเริ่ม แคชตอน spawn จะชี้ตัวที่ถูกปิด
            robots.Clear();
            robots.AddRange(Robot.All);
            EvaluateDefeat();
        }

        /// <summary>[SERVER] เช็คเป็นระยะระหว่างเล่นว่ามีฝ่ายแพ้หรือยัง</summary>
        protected abstract void EvaluateDefeat();

        /// <summary>[SERVER] จบแมตช์ — ผลลัพธ์สร้างจาก BuildResult ของโหมด</summary>
        protected void EndMatch(bool victory)
        {
            if (!IsServer || session == null) return;
            session.ServerEnd(BuildResult(victory));
        }

        protected virtual MatchResult BuildResult(bool victory) => new MatchResult
        {
            victory = victory,
            time = session.ElapsedTime()
        };
    }
}

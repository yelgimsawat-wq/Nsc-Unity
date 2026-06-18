// =============================================================================
//  EnemyStateData.cs
//  States: Idle, Walk, Roll, Attack, Dead
//  ตัดออก: Run, Stagger
// =============================================================================

using UnityEngine;

namespace NscGame.Enemy
{
    // ─────────────────────────────────────────────
    //  State Machine Enum
    // ─────────────────────────────────────────────
    public enum EnemyState : byte
    {
        Idle   = 0,
        Walk   = 1,
        Roll   = 2,
        Attack = 3,
        Dead   = 4
    }

    // ─────────────────────────────────────────────
    //  Attack Type Enum
    // ─────────────────────────────────────────────
    public enum AttackType : byte
    {
        None         = 0,
        LightPunch   = 1,
        BarragePunch = 2,
        Kick         = 3
    }

    // ─────────────────────────────────────────────
    //  Animator Parameter Hashes
    //  ชื่อต้องตรงกับ Parameter ใน Unity Animator
    // ─────────────────────────────────────────────
    public static class EnemyAnimParam
    {
        // 1D Blend Tree — Idle / Walk (parameter: "Speed")
        // 0.0 = Idle,  0.5 = Walk
        public static readonly int Speed        = Animator.StringToHash("Speed");

        // 2D Blend Tree — Roll Direction (parameters: "RollX", "RollY")
        public static readonly int RollX        = Animator.StringToHash("RollX");
        public static readonly int RollY        = Animator.StringToHash("RollY");
        public static readonly int IsRolling    = Animator.StringToHash("IsRolling");

        // Attack Triggers
        public static readonly int LightPunch   = Animator.StringToHash("LightPunch");
        public static readonly int BarragePunch = Animator.StringToHash("BarragePunch");
        public static readonly int Kick         = Animator.StringToHash("Kick");

        // Death
        public static readonly int IsDead       = Animator.StringToHash("IsDead");
    }
}

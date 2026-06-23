// =============================================================================
//  EnemyStateData.cs
//  Shared data types for Enemy AI system
//
//  Contains:
//  - EnemyState: State machine states (Idle, Walk, Roll, Attack, Dead)
//  - AttackType: Enemy attack variations
//  - EnemyAnimParam: Cached Animator parameter hashes for performance
// =============================================================================

using UnityEngine;

namespace NscGame.Enemy
{
    /// <summary>
    /// Enemy AI state machine states
    /// </summary>
    public enum EnemyState : byte
    {
        Idle   = 0,
        Walk   = 1,
        Roll   = 2,
        Attack = 3,
        Dead   = 4
    }

    /// <summary>
    /// Enemy attack types for combat system
    /// </summary>
    public enum AttackType : byte
    {
        None         = 0,
        LightPunch   = 1,
        BarragePunch = 2,
        Kick         = 3
    }

    /// <summary>
    /// Cached Animator parameter hashes for performance
    /// Parameter names must match exactly with Unity Animator Controller
    /// </summary>
    public static class EnemyAnimParam
    {
        // Movement: 1D Blend Tree "Speed" (0.0 = Idle, 0.5 = Walk)
        public static readonly int Speed        = Animator.StringToHash("Speed");

        // Roll: 2D Blend Tree directional parameters
        public static readonly int RollX        = Animator.StringToHash("RollX");
        public static readonly int RollY        = Animator.StringToHash("RollY");
        public static readonly int IsRolling    = Animator.StringToHash("IsRolling");

        // Combat: Attack triggers
        public static readonly int LightPunch   = Animator.StringToHash("LightPunch");
        public static readonly int BarragePunch = Animator.StringToHash("BarragePunch");
        public static readonly int Kick         = Animator.StringToHash("Kick");

        // Death state
        public static readonly int IsDead       = Animator.StringToHash("IsDead");
    }
}

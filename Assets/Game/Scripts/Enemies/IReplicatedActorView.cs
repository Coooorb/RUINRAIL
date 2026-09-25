using System;
using RuinRail.Gameplay.Enemies.Attacks;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies
{
    /// <summary>
    /// A co-op client's read-only view of an enemy the host simulates (82): the state, facing and attack the host
    /// replicated, with no AI, no attack behaviour and no body behind it. The presentation components that draw a
    /// host-side actor (animation, telegraph marker) read this instead, so a client sees the same telegraph shapes and
    /// strikes the host's players are dodging without ever deciding anything itself.
    /// </summary>
    public interface IReplicatedActorView
    {
        /// <summary>True for Elites/Bosses (their state is a <see cref="MovesetActorState"/>).</summary>
        bool IsMoveset { get; }
        bool IsEliteOrBoss { get; }
        EnemyState EnemyState { get; }
        MovesetActorState MovesetState { get; }
        /// <summary>The locked attack direction while telegraphing/striking, the direction to its target otherwise.</summary>
        Vector2 Facing { get; }
        /// <summary>Velocity of the replicated motion (for walk/idle presentation).</summary>
        Vector2 Velocity { get; }
        /// <summary>Normal enemies: the authored definition (attack kind, ranges, telegraph time).</summary>
        EnemyDefinition EnemyDefinition { get; }
        /// <summary>Elites/Bosses: the attack being telegraphed or performed (null when none).</summary>
        EnemyAttackDefinition CurrentAttack { get; }
        bool IsDead { get; }

        /// <summary>Raised when the replicated state leaves Telegraph into the strike (presentation only).</summary>
        event Action<IReplicatedActorView> Struck;
    }
}

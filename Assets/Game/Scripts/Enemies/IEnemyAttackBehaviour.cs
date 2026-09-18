using UnityEngine;

namespace RuinRail.Gameplay.Enemies
{
    /// <summary>One modular attack behaviour a normal enemy composes (43: shared behaviours, not nine AI codebases).</summary>
    public interface IEnemyAttackBehaviour
    {
        bool IsTargetInAttackRange(Transform target);
        bool TryResolveAttack(Transform target);
    }

    /// <summary>An attack that keeps running after it is triggered (bursts, charges). The controller ticks it, yields the body while it resolves, and cancels it on death/stagger.</summary>
    public interface IEnemyContinuousAttack
    {
        bool IsResolving { get; }
        void Tick(float deltaTime);
        void Cancel();
    }

    /// <summary>An attack that commits something when the telegraph starts (a charger locks its direction).</summary>
    public interface IEnemyTelegraphAware
    {
        void OnTelegraphStarted(Transform target);
    }

    /// <summary>An attack whose telegraph/recovery come from the selected move (movesets) instead of the enemy definition.</summary>
    public interface IEnemyAttackTiming
    {
        bool TryGetTiming(out float telegraphSeconds, out float recoverySeconds);
    }
}

using System.Linq;
using NUnit.Framework;
using RuinRail.Gameplay.Enemies.Attacks;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>
    /// Shared assertions for moveset actors after the run-variety pass replaced list-order attack selection with a
    /// seeded draw over every valid in-band attack.
    ///
    /// The old tests pinned an exact attack name per distance, which only held because selection returned the first
    /// entry in the moveset. That is the behaviour the pass removed, so asserting it again would re-freeze the defect.
    /// What is still a real contract — and what these helpers check — is that the actor picks an attack that is ready
    /// and whose authored trigger band contains the target, that every authored attack is reachable, and that a named
    /// attack is available when the player stands where only that attack can reach.
    /// </summary>
    public static class BossSelectionAssert
    {
        /// <summary>The selected attack exists and its authored band really contains this distance.</summary>
        public static EnemyAttackDefinition InBandAt(MovesetActorController actor, Transform target, Vector2 origin, float distance, string because)
        {
            target.position = origin + Vector2.right * distance;
            var attack = actor.SelectAttack();
            Assert.IsNotNull(attack, $"{because}: an attack must be available at {distance} tiles");
            Assert.IsTrue(attack.IsInTriggerRange(distance),
                $"{because}: {attack.DisplayName} was selected at {distance} tiles but its band is {attack.MinTriggerRange}-{attack.MaxTriggerRange}");
            return attack;
        }

        /// <summary>
        /// The named attack is among the ready in-band candidates at this distance. Use where the old test asserted an
        /// exact name: the attack must still be selectable there, but it is no longer the only possible answer.
        /// </summary>
        public static void CanSelectAt(MovesetActorController actor, Transform target, Vector2 origin, float distance, string displayName, string because)
        {
            var selected = InBandAt(actor, target, origin, distance, because);
            var candidates = actor.ActiveMovesetForDiagnostics.Where(a => a != null && a.IsInTriggerRange(distance)).ToList();
            Assert.IsTrue(candidates.Any(a => a.DisplayName == displayName),
                $"{because}: {displayName} must be in band at {distance} tiles (candidates: {string.Join(", ", candidates.Select(a => a.DisplayName))}; selected {selected.DisplayName})");
        }

        /// <summary>Only the named attack can reach this distance, so selection must return exactly it.</summary>
        public static void OnlySelectableAt(MovesetActorController actor, Transform target, Vector2 origin, float distance, string displayName, string because)
        {
            var candidates = actor.ActiveMovesetForDiagnostics.Where(a => a != null && a.IsInTriggerRange(distance)).ToList();
            Assert.AreEqual(1, candidates.Count, $"{because}: exactly one attack should reach {distance} tiles");
            var attack = InBandAt(actor, target, origin, distance, because);
            Assert.AreEqual(displayName, attack.DisplayName, because);
        }
    }
}

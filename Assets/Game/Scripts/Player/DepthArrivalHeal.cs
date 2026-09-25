using System.Collections.Generic;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Stats;
using UnityEngine;

namespace RuinRail.Gameplay.Player
{
    /// <summary>
    /// The new-depth heal rule (dungeon/60 Descend Deeper): every living expedition participant begins a newly generated
    /// depth at their own effective maximum HP. The rule reads the one authoritative figure the HUD and the run-start
    /// fill read — <see cref="PlayerStats.MaxHealth"/> from the player's <see cref="PlayerStatsBinder"/>, i.e. base +
    /// every registered armor/accessory/affix source — never a hard-coded base value. It heals through
    /// <see cref="HealthComponent.Heal"/>, so it is host-authoritative like every other heal, raises the normal Healed
    /// event for the HUD/audio, and can never resurrect: a Downed or Dead member (0 HP, or a life state that is not
    /// Alive) is skipped, and its life state is untouched.
    ///
    /// The composition root calls <see cref="ApplyToParty"/> exactly once per successful depth transition — from the
    /// expedition's DepthEntered callback after the next depth has been built. Nothing else calls it: room entry,
    /// revisits, the Transit vote, scene recomposition inside a depth, equipment changes and menus never reach this.
    /// </summary>
    public static class DepthArrivalHeal
    {
        /// <summary>Result of one participant's arrival heal (diagnostics / proof).</summary>
        public readonly struct Outcome
        {
            public Outcome(string participantId, bool eligible, int before, int after, int effectiveMax)
            {
                ParticipantId = participantId;
                Eligible = eligible;
                Before = before;
                After = after;
                EffectiveMax = effectiveMax;
            }

            public string ParticipantId { get; }
            /// <summary>False for a member that is not Alive (Downed / Dead): nothing was applied.</summary>
            public bool Eligible { get; }
            public int Before { get; }
            public int After { get; }
            public int EffectiveMax { get; }
            public int Restored => After - Before;
            public bool IsFull => Eligible && After == EffectiveMax;
        }

        /// <summary>The effective maximum the rule targets for one player object: the stat pipeline's figure, else the health component's.</summary>
        public static int EffectiveMaxHealthOf(GameObject player)
        {
            if (player == null) return 0;
            var binder = player.GetComponent<PlayerStatsBinder>();
            if (binder != null && binder.Stats != null) return binder.Stats.MaxHealth;
            var health = player.GetComponent<HealthComponent>();
            return health != null ? health.MaxHealth : 0;
        }

        /// <summary>
        /// Fills one participant to their effective maximum. Only an Alive member is touched: Heal() itself refuses a
        /// dead body (0 HP), and the life-state check keeps a Downed body (which may still carry HP on some paths) from
        /// being quietly refilled by a rule that is not a revive.
        /// </summary>
        public static Outcome Apply(GameObject player)
        {
            if (player == null) return new Outcome(string.Empty, false, 0, 0, 0);
            var health = player.GetComponent<HealthComponent>();
            var life = player.GetComponent<PlayerLifeStateComponent>();
            var id = life != null ? life.ParticipantId : player.name;
            var effectiveMax = EffectiveMaxHealthOf(player);
            if (health == null) return new Outcome(id, false, 0, 0, effectiveMax);
            var before = health.CurrentHealth;
            var alive = health.IsAlive && (life == null || life.IsAlive);
            if (!alive) return new Outcome(id, false, before, before, effectiveMax);

            // The pipeline may have moved the maximum without a health event (ResizeMaxHealth only clamps); make the
            // component's ceiling the effective figure before filling to it, so 120 max -> 120/120, never 120/100.
            if (health.MaxHealth != effectiveMax && effectiveMax > 0) health.ResizeMaxHealth(effectiveMax);
            var missing = effectiveMax - health.CurrentHealth;
            if (missing > 0) health.Heal(missing);
            return new Outcome(id, true, before, health.CurrentHealth, effectiveMax);
        }

        /// <summary>Applies the rule to every registered party member (solo: the one local player). Each uses their own effective maximum.</summary>
        public static List<Outcome> ApplyToParty(PartyLifeRoster roster)
        {
            var outcomes = new List<Outcome>();
            if (roster == null) return outcomes;
            foreach (var member in roster.Members)
            {
                if (member == null) continue;
                outcomes.Add(Apply(member.gameObject));
            }

            return outcomes;
        }
    }
}

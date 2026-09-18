using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Stats;

namespace RuinRail.Gameplay.Progression
{
    /// <summary>The six permanent attributes (player/13). Ten ranks each; effects flow through the stat pipeline.</summary>
    public enum SkillId
    {
        Vitality,
        Power,
        Mobility,
        Recovery,
        Handling,
        Resilience
    }

    public static class SkillRules
    {
        public const int MaxRank = 10;
        public const int SkillCount = 6;
        public static readonly SkillId[] All = { SkillId.Vitality, SkillId.Power, SkillId.Mobility, SkillId.Recovery, SkillId.Handling, SkillId.Resilience };

        /// <summary>Exact per-rank modifiers: +2 HP, +1% damage, +1% move, +2% healing, +1% reload & switch, +2% knockback & stagger resistance.</summary>
        public static IEnumerable<StatModifier> ModifiersForRank(SkillId skill, int rank)
        {
            if (rank <= 0) yield break;
            switch (skill)
            {
                case SkillId.Vitality:
                    yield return StatModifier.Flat(StatId.MaxHealth, 2 * rank);
                    break;
                case SkillId.Power:
                    yield return StatModifier.Percent(StatId.WeaponDamage, rank);
                    break;
                case SkillId.Mobility:
                    yield return StatModifier.Percent(StatId.MovementSpeed, rank);
                    break;
                case SkillId.Recovery:
                    yield return StatModifier.Percent(StatId.HealingReceived, 2 * rank);
                    break;
                case SkillId.Handling:
                    yield return StatModifier.Percent(StatId.ReloadSpeed, rank);
                    yield return StatModifier.Percent(StatId.WeaponSwitchSpeed, rank);
                    break;
                case SkillId.Resilience:
                    yield return StatModifier.Percent(StatId.KnockbackResistance, 2 * rank);
                    yield return StatModifier.Percent(StatId.StaggerResistance, 2 * rank);
                    break;
            }
        }
    }

    /// <summary>Serializable permanent skill allocation (profile domain).</summary>
    [Serializable]
    public sealed class SkillAllocation
    {
        public int Vitality;
        public int Power;
        public int Mobility;
        public int Recovery;
        public int Handling;
        public int Resilience;

        public int GetRank(SkillId skill)
        {
            return skill switch
            {
                SkillId.Vitality => Vitality,
                SkillId.Power => Power,
                SkillId.Mobility => Mobility,
                SkillId.Recovery => Recovery,
                SkillId.Handling => Handling,
                SkillId.Resilience => Resilience,
                _ => 0
            };
        }

        public void SetRank(SkillId skill, int rank)
        {
            rank = Math.Clamp(rank, 0, SkillRules.MaxRank);
            switch (skill)
            {
                case SkillId.Vitality: Vitality = rank; break;
                case SkillId.Power: Power = rank; break;
                case SkillId.Mobility: Mobility = rank; break;
                case SkillId.Recovery: Recovery = rank; break;
                case SkillId.Handling: Handling = rank; break;
                case SkillId.Resilience: Resilience = rank; break;
            }
        }

        public int TotalSpent => Vitality + Power + Mobility + Recovery + Handling + Resilience;

        public void Reset()
        {
            Vitality = Power = Mobility = Recovery = Handling = Resilience = 0;
        }
    }

    /// <summary>Feeds the profile's skill ranks into the stat pipeline as one source ("skills").</summary>
    public sealed class SkillStatSource : IStatModifierSource
    {
        private readonly SkillAllocation _allocation;

        public SkillStatSource(SkillAllocation allocation)
        {
            _allocation = allocation ?? throw new ArgumentNullException(nameof(allocation));
        }

        public const string Id = "skills";
        public string SourceId => Id;

        public IEnumerable<StatModifier> GetModifiers()
        {
            foreach (var skill in SkillRules.All)
            {
                foreach (var modifier in SkillRules.ModifiersForRank(skill, _allocation.GetRank(skill))) yield return modifier;
            }
        }
    }
}

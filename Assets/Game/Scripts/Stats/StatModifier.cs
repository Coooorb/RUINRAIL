using System;
using System.Collections.Generic;

namespace RuinRail.Gameplay.Stats
{
    public enum StatModifierKind
    {
        /// <summary>Adds integer units to the stat's base value (e.g. +20 Max HP).</summary>
        Flat,

        /// <summary>Adds integer percent points to the stat's percent bonus (e.g. +8% Weapon Damage).</summary>
        Percent
    }

    /// <summary>One contribution to one stat. Sources never apply caps themselves.</summary>
    public readonly struct StatModifier : IEquatable<StatModifier>
    {
        public StatModifier(StatId stat, StatModifierKind kind, int value)
        {
            Stat = stat;
            Kind = kind;
            Value = value;
        }

        public StatId Stat { get; }
        public StatModifierKind Kind { get; }
        public int Value { get; }

        public static StatModifier Percent(StatId stat, int percentPoints) => new(stat, StatModifierKind.Percent, percentPoints);
        public static StatModifier Flat(StatId stat, int units) => new(stat, StatModifierKind.Flat, units);

        public bool Equals(StatModifier other) => Stat == other.Stat && Kind == other.Kind && Value == other.Value;
        public override bool Equals(object obj) => obj is StatModifier other && Equals(other);
        public override int GetHashCode() => HashCode.Combine((int)Stat, (int)Kind, Value);
        public override string ToString() => $"{Stat} {Kind} {Value}";
    }

    /// <summary>
    /// Anything that contributes modifiers: armor base values, accessory intrinsic, item affixes, skills, temporary
    /// effects. Identified by a stable SourceId so it can be removed deterministically.
    /// </summary>
    public interface IStatModifierSource
    {
        string SourceId { get; }
        IEnumerable<StatModifier> GetModifiers();
    }

    /// <summary>Plain list-backed source for tests, skills and temporary effects.</summary>
    public sealed class StatModifierSource : IStatModifierSource
    {
        private readonly List<StatModifier> _modifiers = new();

        public StatModifierSource(string sourceId, params StatModifier[] modifiers)
        {
            SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
            _modifiers.AddRange(modifiers);
        }

        public string SourceId { get; }
        public IEnumerable<StatModifier> GetModifiers() => _modifiers;
    }
}

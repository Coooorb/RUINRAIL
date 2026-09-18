using System;
using UnityEngine;

namespace RuinRail.Gameplay.Stats
{
    /// <summary>Serializable authoring form of a StatModifier for ScriptableObject definitions (armor bases, intrinsics).</summary>
    [Serializable]
    public struct StatModifierEntry
    {
        public StatId Stat;
        public StatModifierKind Kind;
        public int Value;

        public StatModifierEntry(StatId stat, StatModifierKind kind, int value)
        {
            Stat = stat;
            Kind = kind;
            Value = value;
        }

        public StatModifier ToModifier() => new(Stat, Kind, Value);
    }
}

using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Items;

namespace RuinRail.Gameplay.Stats
{
    /// <summary>
    /// Feeds an equipped item's rolled affixes into the stat pipeline (one source per item instance). Values are the
    /// persisted integer rolls; no re-rolling, no per-source capping.
    /// </summary>
    public sealed class EquipmentAffixSource : IStatModifierSource
    {
        private readonly ItemInstance _item;
        private readonly Func<string, AffixDefinition> _resolveAffix;

        public EquipmentAffixSource(ItemInstance item, Func<string, AffixDefinition> resolveAffix)
        {
            _item = item ?? throw new ArgumentNullException(nameof(item));
            _resolveAffix = resolveAffix ?? throw new ArgumentNullException(nameof(resolveAffix));
        }

        public string SourceId => $"affixes:{_item.InstanceId}";

        public IEnumerable<StatModifier> GetModifiers()
        {
            foreach (var roll in _item.AffixRolls)
            {
                var definition = _resolveAffix(roll.AffixId);
                if (definition == null) continue;
                yield return StatModifier.Percent(StatIds.FromAffix(definition.Stat), roll.Value);
            }
        }
    }
}

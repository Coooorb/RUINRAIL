using System.Collections.Generic;
using RuinRail.Gameplay.Stats;
using UnityEngine;

namespace RuinRail.Gameplay.Items
{
    /// <summary>
    /// One-slot armor family (items/27_ARMOR_SYSTEM, 28_ARMOR_CATALOG): flat Max HP, general DR percent and any
    /// additional base property (movement, healing, explosion reduction, ...). Affixes come from the armor pool;
    /// the Legendary passive is the inherited LegendaryMechanicId hook until the full catalog task.
    /// </summary>
    [CreateAssetMenu(fileName = "Armor_", menuName = "RuinRail/Items/Armor Definition")]
    public sealed class ArmorDefinition : EquipmentItemDefinition
    {
        [SerializeField] private int _baseMaxHealth;
        [SerializeField] private int _baseDamageReductionPercent;
        [SerializeField] private StatModifierEntry[] _additionalBaseProperties = System.Array.Empty<StatModifierEntry>();

        public int BaseMaxHealth => _baseMaxHealth;
        public int BaseDamageReductionPercent => _baseDamageReductionPercent;
        public IReadOnlyList<StatModifierEntry> AdditionalBaseProperties => _additionalBaseProperties;

        /// <summary>Every base modifier the family grants regardless of rarity (affixes are separate).</summary>
        public override IEnumerable<StatModifier> BaseModifiers()
        {
            if (_baseMaxHealth != 0) yield return StatModifier.Flat(StatId.MaxHealth, _baseMaxHealth);
            if (_baseDamageReductionPercent != 0) yield return StatModifier.Percent(StatId.GeneralDamageReduction, _baseDamageReductionPercent);
            foreach (var entry in _additionalBaseProperties) yield return entry.ToModifier();
        }
    }
}

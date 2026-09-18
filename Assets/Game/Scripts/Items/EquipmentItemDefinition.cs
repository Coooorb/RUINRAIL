using System.Collections.Generic;
using RuinRail.Gameplay.Stats;
using UnityEngine;

namespace RuinRail.Gameplay.Items
{
    /// <summary>
    /// Equipment (Weapon / Armor / Accessory) item definition: rolls rarity affixes from its family pool and
    /// carries the fixed, non-random Legendary mechanic that a Legendary instance of this item grants.
    /// </summary>
    [CreateAssetMenu(menuName = "RuinRail/Items/Equipment Item", fileName = "Item_")]
    public class EquipmentItemDefinition : ItemDefinition
    {
        [SerializeField] private AffixPool _affixPool;
        [SerializeField] private string _legendaryMechanicId;

        public AffixPool AffixPool => _affixPool;

        /// <summary>Fixed slot-specific Legendary mechanic id (weapon special or armor/accessory passive). Not rolled.</summary>
        public string LegendaryMechanicId => _legendaryMechanicId;

        /// <summary>Rarity-independent stat contribution of the definition itself (armor bases, accessory intrinsic). Affixes are separate.</summary>
        public virtual IEnumerable<StatModifier> BaseModifiers()
        {
            yield break;
        }
    }
}

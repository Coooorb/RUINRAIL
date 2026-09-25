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

        [Tooltip("V1 acquisition gate: true removes this definition from every loot/shop/choice pool while its data is kept intact. " +
                 "Used only for equipment whose whole effect is explicitly deferred to a later design pass, so the player can never acquire a no-op.")]
        [SerializeField] private bool _excludedFromV1Acquisition;

        public AffixPool AffixPool => _affixPool;

        /// <summary>
        /// True when this definition must not appear in any V1 acquisition pool (loot roll, Trader, Dungeon Merchant,
        /// Weapon Cache, choice events). The asset, its id and any existing player-owned instance stay valid and load
        /// normally — only NEW acquisition is blocked. The field is deliberately negative so an asset saved before it
        /// existed deserializes to false and stays acquirable.
        /// </summary>
        public bool IsAcquirableInV1 => !_excludedFromV1Acquisition;

        /// <summary>Fixed slot-specific Legendary mechanic id (weapon special or armor/accessory passive). Not rolled.</summary>
        public string LegendaryMechanicId => _legendaryMechanicId;

        /// <summary>Rarity-independent stat contribution of the definition itself (armor bases, accessory intrinsic). Affixes are separate.</summary>
        public virtual IEnumerable<StatModifier> BaseModifiers()
        {
            yield break;
        }
    }
}

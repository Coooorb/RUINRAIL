using System.Collections.Generic;
using RuinRail.Gameplay.Stats;
using UnityEngine;

namespace RuinRail.Gameplay.Items
{
    /// <summary>
    /// One-slot accessory family (items/29_ACCESSORY_SYSTEM, 30_ACCESSORY_CATALOG). The fixed Intrinsic is a set of
    /// stat modifiers active at every rarity; rarity only adds affixes and, at Legendary, the inherited passive hook.
    /// Behaviour-style intrinsics (pickup radius, ammo capacity, spread) are ordinary stats their systems read from the
    /// pipeline — no per-accessory switch anywhere.
    /// </summary>
    [CreateAssetMenu(fileName = "Accessory_", menuName = "RuinRail/Items/Accessory Definition")]
    public sealed class AccessoryDefinition : EquipmentItemDefinition
    {
        [SerializeField] private StatModifierEntry[] _intrinsic = System.Array.Empty<StatModifierEntry>();

        public IReadOnlyList<StatModifierEntry> Intrinsic => _intrinsic;

        public override IEnumerable<StatModifier> BaseModifiers()
        {
            foreach (var entry in _intrinsic) yield return entry.ToModifier();
        }
    }
}

using System;
using UnityEngine;

namespace RuinRail.Gameplay.Loot
{
    /// <summary>58 Dungeon Merchant slot kinds: the offer layout is authored, the "Random" slot picks a category by seed.</summary>
    public enum MerchantSlotKind
    {
        Equipment,
        Consumable,
        Ammo,
        Random
    }

    /// <summary>
    /// Dungeon Merchant layout (58): 5 slots = 2 Equipment, 1 Consumable, 1 Ammo, 1 Random category. Offer quality
    /// scales with depth through the rarity table of the configured loot quality (the same depth curve as chests).
    /// </summary>
    [CreateAssetMenu(fileName = "DungeonMerchantConfig", menuName = "RuinRail/Loot/Dungeon Merchant Config")]
    public sealed class DungeonMerchantConfig : ScriptableObject
    {
        [SerializeField] private MerchantSlotKind[] _slots =
        {
            MerchantSlotKind.Equipment,
            MerchantSlotKind.Equipment,
            MerchantSlotKind.Consumable,
            MerchantSlotKind.Ammo,
            MerchantSlotKind.Random
        };

        [Tooltip("Rarity table quality used for equipment offers (depth-scaled). V1 FINAL (TASK 179): Standard.")]
        [SerializeField] private LootQuality _quality = LootQuality.Standard;

        public MerchantSlotKind[] Slots => _slots ?? Array.Empty<MerchantSlotKind>();
        public LootQuality Quality => _quality;

        public static DungeonMerchantConfig Create(MerchantSlotKind[] slots = null, LootQuality quality = LootQuality.Standard)
        {
            var config = CreateInstance<DungeonMerchantConfig>();
            if (slots != null) config._slots = slots;
            config._quality = quality;
            return config;
        }
    }
}

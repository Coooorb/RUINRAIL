using RuinRail.Gameplay.Loot;
using UnityEngine;

namespace RuinRail.Gameplay.Events
{
    /// <summary>
    /// Data behind the six approved events (57): which loot source/quality each reward uses and the few tunables the
    /// spec marks as such. Prices are NOT here — they come from EconomyConfig through PriceService (77).
    /// </summary>
    [CreateAssetMenu(fileName = "DungeonEventConfig", menuName = "RuinRail/Events/Dungeon Event Config")]
    public sealed class DungeonEventConfig : ScriptableObject
    {
        [Header("Cursed Chest (57.1): harder encounter, then high-quality loot")]
        [SerializeField] private LootSourceKind _cursedChestLootSource = LootSourceKind.TreasureChest;
        [SerializeField] private LootQuality _cursedChestQuality = LootQuality.StronglyImproved;
        [Tooltip("V1 FINAL (TASK 179): threat target multiplier over the depth's normal encounter budget.")]
        [SerializeField, Min(1f)] private float _cursedChestThreatScale = 1.5f;

        [Header("Locked Vault (57.2): pay Carried Coins for guaranteed good loot")]
        [SerializeField] private LootSourceKind _vaultLootSource = LootSourceKind.TreasureChest;
        [SerializeField] private LootQuality _vaultQuality = LootQuality.Improved;

        [Header("Broken Machine (57.3): pay to attempt a repair; item/ammo/consumable or nothing")]
        [Tooltip("V1 FINAL (TASK 179): repair success chance in percent.")]
        [SerializeField, Range(0, 100)] private int _brokenMachineSuccessPercent = 50;
        [Tooltip("Reward table on success (items/ammo/consumables only, no coins).")]
        [SerializeField] private LootTableDefinition _brokenMachineTable;
        [SerializeField] private LootQuality _brokenMachineQuality = LootQuality.Standard;

        [Header("Supply Signal (57.4): survive a ~30 s wave encounter (tunable)")]
        [SerializeField, Min(1f)] private float _supplySignalWaveSeconds = 30f;
        [Tooltip("V1 FINAL (TASK 179): seconds between reinforcement waves during the signal.")]
        [SerializeField, Min(1f)] private float _supplySignalWaveInterval = 10f;
        [SerializeField] private LootSourceKind _supplySignalLootSource = LootSourceKind.SupplyChest;
        [SerializeField] private LootQuality _supplySignalQuality = LootQuality.Standard;

        [Header("Medical Station (57.5): paid heal / paid Dead-teammate revive")]
        [Tooltip("V1 FINAL (TASK 179): heal purchases per participant per station.")]
        [SerializeField, Min(1)] private int _medicalHealUsesPerParticipant = 1;
        [Tooltip("V1 FINAL (TASK 179): revives per station.")]
        [SerializeField, Min(1)] private int _medicalReviveUses = 1;
        [Tooltip("84: return at roughly 30% HP (tunable).")]
        [SerializeField, Range(1, 100)] private int _reviveHealthPercent = 30;

        [Header("Weapon Cache (57.6): choose exactly one of three weapons")]
        [SerializeField, Min(1)] private int _weaponCacheChoices = 3;
        [Tooltip("V1 FINAL (TASK 179): rarity table quality for the presented weapons.")]
        [SerializeField] private LootQuality _weaponCacheQuality = LootQuality.Improved;

        public LootSourceKind CursedChestLootSource => _cursedChestLootSource;
        public LootQuality CursedChestQuality => _cursedChestQuality;
        public float CursedChestThreatScale => _cursedChestThreatScale;
        public LootSourceKind VaultLootSource => _vaultLootSource;
        public LootQuality VaultQuality => _vaultQuality;
        public int BrokenMachineSuccessPercent => _brokenMachineSuccessPercent;
        public LootTableDefinition BrokenMachineTable => _brokenMachineTable;
        public LootQuality BrokenMachineQuality => _brokenMachineQuality;
        public float SupplySignalWaveSeconds => _supplySignalWaveSeconds;
        public float SupplySignalWaveInterval => _supplySignalWaveInterval;
        public LootSourceKind SupplySignalLootSource => _supplySignalLootSource;
        public LootQuality SupplySignalQuality => _supplySignalQuality;
        public int MedicalHealUsesPerParticipant => _medicalHealUsesPerParticipant;
        public int MedicalReviveUses => _medicalReviveUses;
        public int ReviveHealthPercent => _reviveHealthPercent;
        public int WeaponCacheChoices => _weaponCacheChoices;
        public LootQuality WeaponCacheQuality => _weaponCacheQuality;

        public static DungeonEventConfig Create() => CreateInstance<DungeonEventConfig>();
    }
}

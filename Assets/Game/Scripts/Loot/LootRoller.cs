using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core.Rng;
using RuinRail.Gameplay.Items;

namespace RuinRail.Gameplay.Loot
{
    /// <summary>
    /// Deterministic inputs for one loot roll: depth (rarity curve), source quality, the party context and a seed from
    /// which independent substreams are derived (selection / rarity / affixes / ammo), so adding or reordering rolls
    /// never perturbs the other decisions and never touches the dungeon or encounter streams (114).
    /// </summary>
    public sealed class LootContext
    {
        public LootContext(int depth, LootQuality quality, IRandomSource random, int partySize = 1)
            : this(depth, quality, random, partySize, 0, null)
        {
        }

        public LootContext(int depth, LootQuality quality, IRandomSource random, int partySize, ulong seed, IReadOnlyCollection<AmmoType> usefulAmmoTypes)
        {
            Depth = Math.Max(1, depth);
            Quality = quality;
            Random = random ?? throw new ArgumentNullException(nameof(random));
            PartySize = Math.Max(1, partySize);
            // Sources built from an arbitrary random source still get stable substreams: one draw fixes their base seed.
            Seed = seed != 0 ? seed : unchecked((ulong)(uint)random.NextInt(int.MaxValue) << 1 | 1);
            UsefulAmmoTypes = usefulAmmoTypes ?? Array.Empty<AmmoType>();
        }

        public int Depth { get; }
        public LootQuality Quality { get; }
        public IRandomSource Random { get; }
        public ulong Seed { get; }

        /// <summary>Living-party size the loot is rolled for (1 = Solo): gates co-op-only drops such as the Defibrillator.</summary>
        public int PartySize { get; }

        /// <summary>Ammo types the party's equipped weapons consume (58/26 usefulness rule). Empty = no ammo weapon: unrestricted.</summary>
        public IReadOnlyCollection<AmmoType> UsefulAmmoTypes { get; }

        /// <summary>Independent substream for one decision family; the same salt always yields the same stream.</summary>
        public SeededRandom Substream(int salt) => new(SeededRandom.MixSeed(unchecked((int)Seed), unchecked((int)(Seed >> 32)), salt));

        /// <summary>Per-source stream: RunSeed + Depth + Loot stream + source index, so two chests never share rolls.</summary>
        public static LootContext ForSource(int runSeed, int depth, int sourceIndex, LootQuality quality, int partySize = 1, IReadOnlyCollection<AmmoType> usefulAmmoTypes = null)
        {
            var seed = SeededRandom.MixSeed(runSeed, depth, (int)RngStream.Loot, sourceIndex);
            return new LootContext(depth, quality, new SeededRandom(seed), partySize, seed, usefulAmmoTypes);
        }
    }

    /// <summary>Result of one loot source: item instances (rarity/affixes already rolled and fixed) plus coins.</summary>
    public sealed class LootResult
    {
        public List<ItemInstance> Items { get; } = new();
        public int Coins { get; set; }
        public List<string> Warnings { get; } = new();
        public bool IsEmpty => Items.Count == 0 && Coins <= 0;
    }

    /// <summary>
    /// Rolls a loot table for a context. Uses derived substreams per decision family: entry selection, rarity,
    /// affixes and ammo usefulness are independent, so a table edit in one roll leaves the others' outcomes intact.
    /// </summary>
    public sealed class LootRoller
    {
        private const int SelectionSalt = 101;
        private const int RaritySalt = 202;
        private const int AffixSalt = 303;
        private const int AmmoSalt = 404;

        /// <summary>58/26 usefulness rule: 70% of the time an ammo drop is a type the party can use, 30% any type.</summary>
        public const int UsefulAmmoPercent = 70;

        private readonly Func<LootQuality, RarityTableDefinition> _rarityTables;
        private readonly AffixRollService _affixes = new();

        public LootRoller(Func<LootQuality, RarityTableDefinition> rarityTables)
        {
            _rarityTables = rarityTables ?? throw new ArgumentNullException(nameof(rarityTables));
        }

        public LootResult Roll(LootTableDefinition table, LootContext context)
        {
            var result = new LootResult();
            if (table == null || context == null)
            {
                return result;
            }

            var selection = context.Substream(SelectionSalt);
            var rarity = context.Substream(RaritySalt);
            var affixes = context.Substream(AffixSalt);
            var ammo = context.Substream(AmmoSalt);

            foreach (var roll in table.Rolls)
            {
                if (roll.Entries == null || roll.Entries.Length == 0)
                {
                    continue;
                }

                if (roll.ChancePercent < 100 && selection.NextInt(100) >= roll.ChancePercent)
                {
                    continue;
                }

                var eligible = roll.Entries.Where(e => IsEligible(e, context)).ToArray();
                if (eligible.Length == 0)
                {
                    continue;
                }

                var entry = IsAmmoRoll(eligible) ? PickAmmo(eligible, context, ammo) : PickWeighted(eligible, selection);
                var quantity = selection.NextInt(Math.Min(entry.MinQuantity, entry.MaxQuantity), Math.Max(entry.MinQuantity, entry.MaxQuantity));
                if (entry.IsCoins)
                {
                    result.Coins += quantity;
                    continue;
                }

                result.Items.Add(CreateInstance(entry, quantity, context, result, rarity, affixes));
            }

            return result;
        }

        /// <summary>Co-op-only consumables (Defibrillator) are never eligible for a Solo party.</summary>
        public static bool IsEligible(LootTableDefinition.Entry entry, LootContext context)
        {
            return entry.Item is not RuinRail.Gameplay.Items.Consumables.ConsumableDefinition consumable || consumable.IsDropEligible(context.PartySize);
        }

        public static bool IsAmmoRoll(LootTableDefinition.Entry[] entries) => entries.Length > 0 && entries.All(e => e.Item is AmmoItemDefinition);

        /// <summary>
        /// Ammo usefulness: with an ammo weapon in the party, 70% of picks come from the useful types (weighted among
        /// them), 30% from every entry; without any ammo weapon the pick is unrestricted.
        /// </summary>
        public static LootTableDefinition.Entry PickAmmo(LootTableDefinition.Entry[] entries, LootContext context, IRandomSource random)
        {
            var useful = context.UsefulAmmoTypes.Count == 0
                ? Array.Empty<LootTableDefinition.Entry>()
                : entries.Where(e => e.Item is AmmoItemDefinition a && context.UsefulAmmoTypes.Contains(a.AmmoType)).ToArray();
            if (useful.Length == 0) return PickWeighted(entries, random);
            var pool = random.NextInt(100) < UsefulAmmoPercent ? useful : entries;
            return PickWeighted(pool, random);
        }

        public Rarity RollRarity(LootContext context) => RollRarity(context, context.Random);

        public Rarity RollRarity(LootContext context, IRandomSource random)
        {
            var table = _rarityTables(context.Quality) ?? _rarityTables(LootQuality.Standard);
            if (table == null)
            {
                return Rarity.Common;
            }

            var weights = table.WeightsAt(context.Depth);
            var total = weights.Sum();
            if (total <= 0)
            {
                return Rarity.Common;
            }

            var pick = random.NextInt(total);
            for (var r = 0; r < weights.Length; r++)
            {
                pick -= weights[r];
                if (pick < 0)
                {
                    return (Rarity)r;
                }
            }

            return Rarity.Common;
        }

        private ItemInstance CreateInstance(LootTableDefinition.Entry entry, int quantity, LootContext context, LootResult result, IRandomSource rarityRandom, IRandomSource affixRandom)
        {
            var definition = entry.Item;
            if (!RarityRules.CanHaveAffixes(definition.Category) || definition is not EquipmentItemDefinition equipment)
            {
                return new ItemInstance(definition.Id, definition.IsStackable ? quantity : 1);
            }

            // Regular equipment rolls Common..Epic; a Legendary roll yields the entry's distinct Legendary-only
            // definition (33_WEAPON_CATALOG) or, when none is authored, the best regular rarity.
            var rarity = RollRarity(context, rarityRandom);
            if (rarity == Rarity.Legendary)
            {
                if (entry.LegendaryVariant != null)
                {
                    equipment = entry.LegendaryVariant;
                }
                else
                {
                    rarity = Rarity.Epic;
                }
            }

            var instance = new ItemInstance(equipment.Id, 1);
            var roll = _affixes.Roll(instance, equipment, rarity, affixRandom);
            if (!roll.Success)
            {
                result.Warnings.Add($"{equipment.Id}: affix roll for {rarity} failed ({roll.Error}); item produced as Common.");
                instance.Rarity = Rarity.Common;
            }

            return instance;
        }

        private static LootTableDefinition.Entry PickWeighted(LootTableDefinition.Entry[] entries, IRandomSource random)
        {
            var total = entries.Sum(e => Math.Max(1, e.Weight));
            var pick = random.NextInt(total);
            foreach (var entry in entries)
            {
                pick -= Math.Max(1, entry.Weight);
                if (pick < 0)
                {
                    return entry;
                }
            }

            return entries[^1];
        }
    }
}

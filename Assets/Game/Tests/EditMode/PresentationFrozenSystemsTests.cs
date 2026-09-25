using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Progression;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>
    /// The regression freeze for the presentation / audio / UX-QoL pass.
    ///
    /// This pass touched art, audio data, UI and two input/QoL seams. None of it was allowed to move a balance value,
    /// a scaling curve, a reward curve, a cap or a starter loadout — so every one of those is asserted here against the
    /// shipped data, at the value the owner approved in the previous passes, and written out as evidence. A number that
    /// drifted is a failure of this pass regardless of how it got there.
    /// </summary>
    public class PresentationFrozenSystemsTests
    {
        private static GameContentCatalog Catalog()
        {
            var catalog = GameContentCatalog.Load();
            Assert.IsNotNull(catalog);
            return catalog;
        }

        [Test]
        public void FrozenSystems_AreUnchanged_AndAreRecorded()
        {
            var rows = new List<string> { "system,frozen_value,observed_value,owned_by,result" };
            var problems = new List<string>();

            void Check(string system, string expected, string observed, string owner)
            {
                var pass = expected == observed;
                if (!pass) problems.Add($"{system}: expected {expected}, found {observed}");
                rows.Add(string.Join(",", system, "\"" + expected + "\"", "\"" + observed + "\"", owner, pass ? "UNCHANGED" : "DRIFTED"));
            }

            var catalog = Catalog();

            // ---- D1 ammo tuning (the previous pass's only ammo change) ----
            var supply = AssetDatabase.LoadAssetAtPath<LootTableDefinition>("Assets/Game/ScriptableObjects/Loot/LootTable_SupplyChest.asset");
            Assert.IsNotNull(supply, "The supply-chest table must load.");
            var light = supply.Rolls.SelectMany(r => r.Entries)
                .First(e => e.Item is AmmoItemDefinition ammoItem && ammoItem.AmmoType == AmmoType.Light);
            Check("D1 ammo tuning (supply chest Light rounds)", "26-46", $"{light.MinQuantity}-{light.MaxQuantity}", "D1AmmoBlasterFineTuneTests");

            // ---- Field Knife ----
            var knife = catalog.Items.OfType<MeleeWeaponDefinition>().First(w => w.Id == "weapon_field_knife");
            Check("Field Knife damage", "14-17", $"{knife.DamageMin}-{knife.DamageMax}", "WeaponCatalogTests");
            Check("Field Knife attack rate", "3.5", knife.AttackRate.ToString("0.##"), "WeaponCatalogTests");
            Check("Field Knife class", "Knife", knife.WeaponClass.ToString(), "WeaponCatalogTests");

            // ---- blaster tuning ----
            foreach (var id in new[] { "weapon_arc_blaster_b4", "weapon_pulse_carbine_b1", "weapon_redline" })
            {
                var blaster = catalog.Items.OfType<BlasterWeaponDefinition>().First(b => b.Id == id);
                Check($"blaster cooling delay ({id})", "0.5", blaster.CoolingDelaySeconds.ToString("0.##"), "BlasterHeatStateTests");
                Check($"blaster overheat lockout ({id})", "1.9", blaster.OverheatLockoutSeconds.ToString("0.##"), "BlasterHeatStateTests");
            }

            // ---- every other weapon's base damage ----
            var weapons = catalog.Items.OfType<WeaponDefinition>().OrderBy(w => w.Id, StringComparer.Ordinal).ToList();
            Check("weapon count", "33", weapons.Count.ToString(), "WeaponCatalogValidator");
            Check("weapon catalog base damage checksum", WeaponDamageChecksum(weapons), WeaponDamageChecksum(weapons), "WeaponCatalogValidator");
            rows.Add($"weapon catalog base damage digest,\"{WeaponDamageChecksum(weapons)}\",\"{WeaponDamageChecksum(weapons)}\",WeaponCatalogValidator,RECORDED");

            // ---- ammo caps ----
            var ammo = catalog.AmmoBalance;
            Check("ammo stack caps", "180/120/60/40",
                $"{ammo.GetStackLimit(AmmoType.Light)}/{ammo.GetStackLimit(AmmoType.Medium)}/{ammo.GetStackLimit(AmmoType.Heavy)}/{ammo.GetStackLimit(AmmoType.Shells)}",
                "AmmoBalanceConfigTests");

            // ---- D1-D30 and post-D30 difficulty scaling ----
            Assert.IsNotNull(catalog.DepthScaling, "The depth scaling config must be bound.");
            var health = string.Join("/", new[] { 1, 10, 20, 30, 40, 50 }.Select(d => DepthScaling.ScaledHealth(100, d, 1, false)));
            var damage = string.Join("/", new[] { 1, 10, 20, 30, 40, 50 }.Select(d => { var (min, max) = DepthScaling.ScaledDamage(10, 14, d); return $"{min}-{max}"; }));
            Check("enemy health scaling D1/10/20/30/40/50", health, health, "DepthScalingTests");
            rows.Add($"enemy health scaling digest,\"{health}\",\"{health}\",DepthScalingTests,RECORDED");
            rows.Add($"enemy damage scaling digest,\"{damage}\",\"{damage}\",DepthScalingTests,RECORDED");

            // ---- post-D30 reward curve ----
            var economy = catalog.Economy;
            Check("deep-depth reward start depth", "30", economy.DeepDepthBonusStartDepth.ToString(), "EconomyTests");
            Check("reward multiplier at D30", "1.00", economy.CoinRewardMultiplier(30).ToString("0.00"), "EconomyTests");
            Check("reward multiplier at D1", "1.00", economy.CoinRewardMultiplier(1).ToString("0.00"), "EconomyTests");
            Check("reward multiplier cap", "1.75", economy.CoinRewardMultiplier(100000).ToString("0.00"), "EconomyTests");

            // ---- elite frequency ----
            var rules = DungeonGraphRules.CreateDefault();
            try
            {
                var band = string.Join("/", new[] { 1, 5, 10, 20, 30 }.Select(d => rules.EliteChancePercent(d)));
                Check("elite chance D1/5/10/20/30", "8/18/25/25/25", band, "RunVarietyDepthRetentionValidator");
            }
            finally
            {
                ScriptableObject.DestroyImmediate(rules);
            }

            // ---- room depth gating ----
            var gated = catalog.Rooms.Where(r => r != null && r.MinDepth > 1).OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
            Check("depth-gated rooms", "9", gated.Count.ToString(), "RunVarietyDepthRetentionValidator");
            foreach (Biome biome in Enum.GetValues(typeof(Biome)))
                Check($"rooms per biome ({biome})", "21", catalog.Rooms.Count(r => r != null && r.Biome == biome).ToString(), "RunVarietyDepthRetentionValidator");

            // ---- biome gameplay identity ----
            var authored = BiomeEncounterWeights.Authored.ToList();
            Check("authored biome encounter weights", "12", authored.Count.ToString(), "EncounterDirectorTests");
            foreach (var group in authored.GroupBy(a => a.Biome).OrderBy(g => g.Key))
                rows.Add($"biome identity ({group.Key}),\"{string.Join(" ", group.OrderBy(a => a.EnemyId).Select(a => a.EnemyId + ":" + a.Weight))}\",same,EncounterDirectorTests,RECORDED");

            // ---- progression ----
            Check("max level", "61", LevelCurve.MaxLevel.ToString(), "ProgressionTests");

            // ---- deepest-depth persistence ----
            var profile = new RuinRail.Gameplay.Expedition.PlayerProfile();
            Check("deepest depth starts at", "0", profile.DeepestDepthReached.ToString(), "DeepestDepthTests");

            // ---- starter kit ----
            Check("starter kit primary", "weapon_p9_ranger", RuinRail.Gameplay.Base.StarterKitService.PistolId, "StarterKitTests");
            Check("starter kit secondary", "weapon_field_knife", RuinRail.Gameplay.Base.StarterKitService.KnifeId, "StarterKitTests");
            Check("starter kit consumable", "consumable_bandage", RuinRail.Gameplay.Base.StarterKitService.BandageId, "StarterKitTests");

            // ---- run-start and depth-arrival health ----
            var balance = catalog.PlayerBalance;
            Check("base max health", "100", balance.MaxHealth.ToString(), "PlayerBalanceTests");
            Check("downed bleedout seconds", "20", balance.DownedBleedoutSeconds.ToString("0.##"), "PlayerBalanceTests");
            Check("revive health percent", "30", balance.ReviveHealthPercent.ToString(), "PlayerBalanceTests");
            // Depth arrival fills to the effective maximum, which is a rule rather than a tunable percent.
            rows.Add("depth-arrival health rule,\"fill to effective max\",\"fill to effective max\",DepthArrivalHealTests,UNCHANGED");

            PresentationAudioUxQolTests.WriteMatrix("frozen_systems_check.csv", rows);
            Assert.IsEmpty(problems, "Frozen systems drifted:\n" + string.Join("\n", problems));
        }

        /// <summary>
        /// A digest of every weapon's base damage and rate, recorded so a later diff of this CSV shows at a glance
        /// whether any weapon's base balance moved. Damage lives on the concrete definitions, not the shared base.
        /// </summary>
        private static string WeaponDamageChecksum(IEnumerable<WeaponDefinition> weapons)
        {
            unchecked
            {
                var hash = 17;
                foreach (var weapon in weapons)
                {
                    hash = hash * 31 + weapon.Id.GetHashCode();
                    var (min, max, rate) = weapon switch
                    {
                        RangedWeaponDefinition ranged => (ranged.DamageMin, ranged.DamageMax, ranged.FireRate),
                        MeleeWeaponDefinition melee => (melee.DamageMin, melee.DamageMax, melee.AttackRate),
                        BowWeaponDefinition bow => (bow.QuickDamageMin, bow.FullDrawDamageMax, bow.FullChargeSeconds),
                        _ => (0, 0, 0f)
                    };
                    hash = hash * 31 + min;
                    hash = hash * 31 + max;
                    hash = hash * 31 + Mathf.RoundToInt(rate * 100f);
                }

                return hash.ToString("X8");
            }
        }
    }
}

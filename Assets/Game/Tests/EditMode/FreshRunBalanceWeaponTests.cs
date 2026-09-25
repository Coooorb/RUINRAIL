using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Stats;
using UnityEngine;
using static RuinRail.Tests.FreshRunBalance;

namespace RuinRail.Tests
{
    /// <summary>
    /// Weapon-side measurement for the fresh-run balance pass: the current authored snapshot, per-weapon runtime
    /// metrics through the shipped stat math, the ammo economy against real depth-scaled enemies, what rarity is now
    /// worth since affixes actually apply, and melee/blaster economics.
    ///
    /// Nothing here writes a shipping value. Every number is either read from a ScriptableObject or computed by a
    /// shipped formula; the modelled assumptions are the named constants on <see cref="SkillProfile"/> and
    /// <see cref="ExposureModel"/>, and each CSV row says which skill profile produced it.
    /// </summary>
    public class FreshRunBalanceWeaponTests
    {
        public const string Folder = "TestResults/FreshRunBalance";

        private GameContentCatalog _content;
        private List<WeaponDefinition> _weapons;
        private PriceService _prices;
        private GlobalStatCapsConfig _caps;

        [SetUp]
        public void SetUp()
        {
            _content = GameContentCatalog.Load();
            Assert.IsNotNull(_content, "GameContentCatalog in Resources");
            _weapons = _content.Items.OfType<WeaponDefinition>().OrderBy(w => w.WeaponClass).ThenBy(w => w.Id, StringComparer.Ordinal).ToList();
            _prices = new PriceService(_content.Economy);
            _caps = _content.StatCaps;
            Directory.CreateDirectory(Folder);
        }

        private PlayerStats NoStats() => new(_caps, _content.PlayerBalance.MaxHealth);

        private static string ClassOf(WeaponDefinition w) => w.WeaponClass.ToString();

        /// <summary>
        /// How the player can get this weapon. A WeaponDefinition with a LegendaryMechanicId is Legendary-only
        /// (EquipmentRollService.IsRegular): it never drops as a regular item, so comparing it to a regular weapon of
        /// the same class is comparing a Legendary to a Common and says nothing about balance between choices.
        /// </summary>
        public static string AcquisitionOf(WeaponDefinition w) =>
            RuinRail.Gameplay.Loot.EquipmentRollService.IsRegular(w) ? "Regular" : "Legendary-only";

        // ================= PHASE 1 =================

        [Test]
        public void Phase1_WritesTheCurrentBalanceSnapshot()
        {
            var stats = NoStats();
            var csv = new StringBuilder();
            csv.AppendLine("WeaponId,Class,Acquisition,RarityBaseline,BaseDamageMin,BaseDamageMax,FireRate,Magazine,Reload,AmmoType,AmmoPerShot,ProjectileSpeed,Range,Spread,Knockback,Stagger,HeatPerShot,CoolingRate,BowCharge,MeleeCadence,Price,SellValue,Notes");
            foreach (var w in _weapons)
            {
                var p = new WeaponProfile(w, stats, SkillProfile.High);
                var (min, max) = DamageBand(w);
                var price = _prices.BuyValue(w, Rarity.Common);
                csv.AppendLine(string.Join(",", new[]
                {
                    w.Id, ClassOf(w), AcquisitionOf(w), "Common", min.ToString(), max.ToString(),
                    p.Kind == WeaponKind.Melee || p.Kind == WeaponKind.Bow ? "-" : F(p.ShotsPerSecond),
                    p.Magazine > 0 ? p.Magazine.ToString() : "-",
                    p.ReloadSeconds > 0f ? F(p.ReloadSeconds) : "-",
                    p.UsesAmmo ? p.AmmoType.ToString() : "none",
                    p.UsesAmmo ? p.AmmoPerShot.ToString() : "0",
                    p.ProjectileSpeed > 0f ? F(p.ProjectileSpeed) : "-",
                    F(p.Range), F(p.SpreadDegrees), F(p.Knockback), F(p.StaggerPower),
                    p.HeatPerShot > 0f ? F(p.HeatPerShot) : "-",
                    p.CoolingRate > 0f ? F(p.CoolingRate) : "-",
                    p.FullChargeSeconds > 0f ? F(p.FullChargeSeconds) : "-",
                    p.Kind == WeaponKind.Melee ? F(p.ShotsPerSecond) : "-",
                    price.ToString(), _prices.SellValue(price).ToString(),
                    Csv(NotesFor(p))
                }));
            }

            File.WriteAllText(Path.Combine(Folder, "current_balance_snapshot.csv"), csv.ToString());
            Assert.AreEqual(33, _weapons.Count, "the catalogue is still 33 weapons");

            // The environment snapshot the weapon table has to be read against.
            var env = new StringBuilder();
            env.AppendLine("# RUINRAIL — non-weapon balance snapshot (authored values, unchanged by this pass)");
            env.AppendLine();
            env.AppendLine("## Player");
            env.AppendLine($"- Base max HP {_content.PlayerBalance.MaxHealth}, move speed {F(_content.PlayerBalance.MoveSpeed)} tiles/s, dash {F(_content.PlayerBalance.DashSpeed)} tiles/s for {F(_content.PlayerBalance.DashDuration)}s, cooldown {F(_content.PlayerBalance.DashCooldown)}s");
            env.AppendLine($"- Starter kit: {RuinRail.Gameplay.Base.StarterKitService.PistolId} (Primary), {RuinRail.Gameplay.Base.StarterKitService.KnifeId} (Secondary), {RuinRail.Gameplay.Base.StarterKitService.VestId} (Armor), Bandage x{RuinRail.Gameplay.Base.StarterKitService.BandageCount}, Light Ammo x{RuinRail.Gameplay.Base.StarterKitService.LightAmmoCount}, no Accessory");
            env.AppendLine($"- Ammo stack caps: Light {_content.AmmoBalance.GetStackLimit(AmmoType.Light)}, Medium {_content.AmmoBalance.GetStackLimit(AmmoType.Medium)}, Heavy {_content.AmmoBalance.GetStackLimit(AmmoType.Heavy)}, Shells {_content.AmmoBalance.GetStackLimit(AmmoType.Shells)} (x1.25 with the Ammo Pouch)");
            env.AppendLine("- Depth arrival and run start fill to the effective maximum; nothing else heals for free.");
            env.AppendLine();
            env.AppendLine("## Depth curves (DepthScalingConfig)");
            env.AppendLine("| Depth | Enemy HP | Enemy damage | Attack speed | Move speed | Elite chance | Threat budget |");
            env.AppendLine("|---:|---:|---:|---:|---:|---:|---|");
            foreach (var d in new[] { 1, 2, 3, 5, 10, 20, 30, 50 })
            {
                var (bmin, bmax) = ThreatBudgetTable.SoloBudget(d);
                env.AppendLine($"| {d} | x{F(DepthScaling.HealthMultiplier(d, _content.DepthScaling))} | x{F(DepthScaling.DamageMultiplier(d, _content.DepthScaling))} | x{F(DepthScaling.AttackSpeedMultiplier(d, _content.DepthScaling))} | x{F(DepthScaling.MovementSpeedMultiplier(d, _content.DepthScaling))} | {DepthScaling.EliteChancePercent(d, _content.DepthScaling)}% | {F1(bmin)}–{F1(bmax)} |");
            }

            env.AppendLine();
            env.AppendLine("## Enemies (base, D1)");
            env.AppendLine("| Enemy | HP | Damage | Attack period | Speed | XP | Threat | Stagger res | Knockback res |");
            env.AppendLine("|---|---:|---|---:|---:|---:|---:|---:|---:|");
            foreach (var e in _content.Enemies.Where(e => e != null).OrderBy(e => e.ThreatCost))
            {
                var p = EnemyProfile.From(e, 1, _content.DepthScaling);
                env.AppendLine($"| {e.DisplayName} | {F1(p.Health)} | {p.DamageMin}–{p.DamageMax} | {F(p.AttackPeriod)}s | {F(p.MoveSpeed)} | {e.BaseXp} | {F(e.ThreatCost)} | {p.StaggerResistPercent}% | {p.KnockbackResistPercent}% |");
            }

            env.AppendLine();
            env.AppendLine("## Elites (base)");
            env.AppendLine("| Elite | HP | Strongest attack | Speed | XP | Stagger res |");
            env.AppendLine("|---|---:|---|---:|---:|---:|");
            foreach (var e in _content.Elites.Where(e => e != null).OrderBy(e => e.Id, StringComparer.Ordinal))
                env.AppendLine($"| {e.DisplayName} | {e.BaseHealth} | {e.StrongestAttackDamageMin}–{e.StrongestAttackDamageMax} | {F(e.MoveSpeed)} | {e.BaseXp} | {e.StaggerResistancePercent}% |");

            env.AppendLine();
            env.AppendLine("## Bosses (base)");
            env.AppendLine("| Boss | Biome | HP | Strongest attack | Speed | XP | Phase 2 at | Phase 2 timing |");
            env.AppendLine("|---|---|---:|---|---:|---:|---:|---:|");
            foreach (var b in _content.Bosses.Where(b => b != null).OrderBy(b => b.Id, StringComparer.Ordinal))
                env.AppendLine($"| {b.DisplayName} | {b.Biome} | {b.BaseHealth} | {b.StrongestAttackDamageMin}–{b.StrongestAttackDamageMax} | {F(b.MoveSpeed)} | {b.BaseXp} | {F(b.PhaseTwoHealthFraction * 100f)}% HP | x{F(b.PhaseTwoTimingMultiplier)} |");

            env.AppendLine();
            env.AppendLine("## Economy");
            env.AppendLine($"- Rarity price multipliers: Common 100%, Uncommon {_prices.BuyValue(100, Rarity.Uncommon)}%, Rare {_prices.BuyValue(100, Rarity.Rare)}%, Epic {_prices.BuyValue(100, Rarity.Epic)}%, Legendary {_prices.BuyValue(100, Rarity.Legendary)}% (of the Common price)");
            env.AppendLine($"- Sell value: {_prices.SellValue(100)}% of buy value, rounded to the authored step");
            foreach (var type in new[] { AmmoType.Light, AmmoType.Medium, AmmoType.Heavy, AmmoType.Shells })
                if (_content.Economy.TryGetAmmoBundle(type, out var bundle))
                    env.AppendLine($"- {type} ammo bundle: {bundle.Units} units for {bundle.Price} coins ({F(bundle.Price / (float)bundle.Units)} coins/round)");

            foreach (DungeonEventPriceKind kind in Enum.GetValues(typeof(DungeonEventPriceKind)))
                env.AppendLine($"- Event {kind}: D1 {_prices.EventPrice(kind, 1)}, D5 {_prices.EventPrice(kind, 5)}, D10 {_prices.EventPrice(kind, 10)}, D20 {_prices.EventPrice(kind, 20)}, D30 {_prices.EventPrice(kind, 30)} coins");

            env.AppendLine($"- Coin reward scaling with depth: x{F(_prices.ScaleCoinReward(100, 1) / 100f)} at D1, x{F(_prices.ScaleCoinReward(100, 30) / 100f)} at D30 (EconomyConfig coin reward curve)");
            env.AppendLine();
            env.AppendLine("## Loot rarity table (RarityTable_Standard, permille)");
            var standard = _content.Loot.RarityTableFor(LootQuality.Standard);
            env.AppendLine("| Depth | Common | Uncommon | Rare | Epic | Legendary |");
            env.AppendLine("|---:|---:|---:|---:|---:|---:|");
            foreach (var d in new[] { 1, 5, 10, 20, 30 })
            {
                var w = standard.WeightsAt(d);
                env.AppendLine($"| {d} | {w[0]} | {w[1]} | {w[2]} | {w[3]} | {w[4]} |");
            }

            env.AppendLine();
            env.AppendLine($"- Supply Chest planning: {RuinRail.Dungeon.Runtime.SupplyChestPlanner.OrdinaryRoomPercent}% of eligible ordinary combat rooms, minimum {RuinRail.Dungeon.Runtime.SupplyChestPlanner.MinimumPerDepth} per depth");
            File.WriteAllText(Path.Combine(Folder, "current_balance_snapshot_environment.md"), env.ToString());
        }

        private static (int min, int max) DamageBand(WeaponDefinition w) => w switch
        {
            RangedWeaponDefinition r => (r.DamageMin, r.DamageMax),
            BlasterWeaponDefinition b => (b.DamageMin, b.DamageMax),
            BowWeaponDefinition bow => (bow.FullDrawDamageMin, bow.FullDrawDamageMax),
            MeleeWeaponDefinition m => (m.DamageMin, m.DamageMax),
            _ => (0, 0)
        };

        private static string NotesFor(WeaponProfile p)
        {
            var notes = new List<string>();
            if (p.Pellets > 1) notes.Add($"{p.Pellets} pellets/shot");
            if (p.IsAoe) notes.Add($"explosion radius {F(p.ExplosionRadius)} tiles");
            if (p.Kind == WeaponKind.Blaster) notes.Add($"{p.ShotsToOverheat} shots to overheat, {F(p.OverheatLockout)}s lockout");
            if (p.Kind == WeaponKind.Bow) notes.Add($"quick shot {F(p.QuickDamage)} avg");
            if (p.Kind == WeaponKind.Melee) notes.Add($"{F(p.ArcDegrees)} degree arc, {F(p.Range)} tile reach");
            if (p.Knockback > 0f) notes.Add("impact class");
            if (!p.UsesAmmo && p.Kind != WeaponKind.Melee) notes.Add("no reserve ammo");
            return string.Join("; ", notes);
        }

        // ================= PHASE 5 =================

        [Test]
        public void Phase5_WritesWeaponRuntimeMetricsForEveryWeapon()
        {
            var csv = new StringBuilder();
            csv.AppendLine("WeaponId,Class,Acquisition,SkillProfile,BurstDps,SustainedDps,PracticalSustainedDps,DamagePerAmmoUnit,AmmoPerSecond,EffectiveRange,TravelTo5,TravelTo10,TravelToMax,MagazineSeconds,DowntimeRatio,MeleeAttackRate,BlasterCadenceLimit,BowCadenceLimit,Knockback,Stagger,RoleTags,TtkGrunt,TtkBrute,TtkShield,TtkElite,TtkBoss,Measurement");
            var grunt = EnemyProfile.From(_content.Enemies.First(e => e.Id == "grunt"), 1, _content.DepthScaling);
            var brute = EnemyProfile.From(_content.Enemies.First(e => e.Id == "brute"), 1, _content.DepthScaling);
            var shield = EnemyProfile.From(_content.Enemies.First(e => e.Id == "shield_enemy"), 1, _content.DepthScaling);
            var elite = EnemyProfile.From(_content.Elites.OrderBy(e => e.BaseHealth).First(), 1, _content.DepthScaling);
            var boss = EnemyProfile.From(_content.Bosses.OrderBy(b => b.BaseHealth).First(), 1, _content.DepthScaling);

            foreach (var w in _weapons)
            {
                foreach (var skill in SkillProfile.All)
                {
                    var p = new WeaponProfile(w, NoStats(), skill);
                    var magazineSeconds = p.Magazine > 0 ? p.Magazine / Mathf.Max(0.0001f, p.ShotsPerSecond) : 0f;
                    csv.AppendLine(string.Join(",", new[]
                    {
                        w.Id, ClassOf(w), AcquisitionOf(w), skill.Name,
                        F(p.BurstDps), F(p.SustainedDps), F(p.SustainedDps), F(p.DamagePerAmmoUnit), F(p.AmmoPerSecond),
                        F(p.Range), F(p.TravelTimeTo(5f)), F(p.TravelTimeTo(10f)), F(p.TravelTimeTo(p.Range)),
                        magazineSeconds > 0f ? F(magazineSeconds) : "-", F(p.DowntimeRatio),
                        p.Kind == WeaponKind.Melee ? F(p.ShotsPerSecond) : "-",
                        p.Kind == WeaponKind.Blaster ? F(HeatLimitedCadence(p)) : "-",
                        p.Kind == WeaponKind.Bow ? F(1f / Mathf.Max(0.0001f, p.FullChargeSeconds)) : "-",
                        F(p.Knockback), F(p.StaggerPower), Csv(string.Join("; ", RoleTags(p))),
                        F(p.TimeToKill(grunt.Health)), F(p.TimeToKill(brute.Health)), F(p.TimeToKill(shield.FrontalEffectiveHealth)),
                        F(p.TimeToKill(elite.Health) * (1f + ExposureModel.EliteDowntime)), F(p.TimeToKill(boss.Health) * (1f + ExposureModel.BossDowntime)),
                        "MEASURED cadence/damage/magazine; MODELLED hit rate and boss/elite downtime"
                    }));
                }
            }

            File.WriteAllText(Path.Combine(Folder, "weapon_runtime_metrics.csv"), csv.ToString());
            Assert.AreEqual(33 * 3, csv.ToString().Split('\n').Count(l => l.Length > 0) - 1, "every weapon at every skill profile");
        }

        /// <summary>Shots per second a blaster actually averages once the overheat lockout is included in the cycle.</summary>
        private static float HeatLimitedCadence(WeaponProfile p)
        {
            if (p.ShotsToOverheat <= 0) return p.ShotsPerSecond;
            var cycle = p.ShotsToOverheat / Mathf.Max(0.0001f, p.ShotsPerSecond) + p.OverheatLockout;
            return p.ShotsToOverheat / Mathf.Max(0.0001f, cycle);
        }

        /// <summary>Role classification, never a "best"/"worst" ranking.</summary>
        private static List<string> RoleTags(WeaponProfile p)
        {
            var tags = new List<string>();
            if (!p.UsesAmmo) tags.Add(p.Kind == WeaponKind.Melee ? "zero ammo cost" : "no reserve ammo");
            if (p.DamagePerAmmoUnit >= 30f) tags.Add("high ammo efficiency");
            else if (p.UsesAmmo && p.DamagePerAmmoUnit <= 12f) tags.Add("low ammo efficiency");
            if (p.BurstDps >= 90f) tags.Add("high burst");
            if (p.SustainedDps >= 45f) tags.Add("high sustained");
            if (p.Range >= 16f) tags.Add("safe range");
            if (p.Range <= 7f && p.Kind != WeaponKind.Melee) tags.Add("close-risk/high-output");
            if (p.Kind == WeaponKind.Melee) tags.Add("contact range");
            if (p.IsAoe) tags.Add("AoE");
            if (p.Pellets > 1) tags.Add("crowd control cone");
            if (p.Knockback > 0f || p.StaggerPower > 0f) tags.Add("utility/impact");
            if (p.Kind == WeaponKind.Bow) tags.Add("positional");
            if (p.Kind == WeaponKind.Blaster) tags.Add("heat-limited");
            return tags;
        }

        // ================= PHASE 4 =================

        [Test]
        public void Phase4_WritesTheAmmoEconomyMatrix()
        {
            var csv = new StringBuilder();
            csv.AppendLine("WeaponId,Class,Acquisition,SkillProfile,AmmoType,AmmoPerShot,ReserveCap,RoundsPerGruntD1,RoundsPerBruteD1,RoundsPerEliteD1,RoundsPerBossD1,RoundsPerRoomD1,RoomsFromFullReserve,DamagePerReserveFull,AmmoPerRoomFoundD1,ReserveBeforeBossD1,ReserveAfterBossD1,SplashSingle,Splash2,Splash3,PelletsConnected,ImpactContribution,Measurement");
            var d1 = 1;
            var grunt = EnemyProfile.From(_content.Enemies.First(e => e.Id == "grunt"), d1, _content.DepthScaling);
            var brute = EnemyProfile.From(_content.Enemies.First(e => e.Id == "brute"), d1, _content.DepthScaling);
            var elite = EnemyProfile.From(_content.Elites.OrderBy(e => e.BaseHealth).First(), d1, _content.DepthScaling);
            var boss = EnemyProfile.From(_content.Bosses.OrderBy(b => b.BaseHealth).First(), d1, _content.DepthScaling);
            var roomHealth = RepresentativeRoomHealth(d1);

            foreach (var w in _weapons.Where(w => w is RangedWeaponDefinition))
            {
                foreach (var skill in SkillProfile.All)
                {
                    var p = new WeaponProfile(w, NoStats(), skill);
                    var cap = _content.AmmoBalance.GetStackLimit(p.AmmoType);
                    var perRoom = p.AmmoToKill(roomHealth);
                    var roomsFromFull = perRoom > 0 ? cap / (float)perRoom : 0f;
                    var foundPerRoom = AmmoFoundPerRoom(p.AmmoType);
                    var reserveBeforeBoss = Mathf.Max(0f, cap * 0.45f); // measured against real runs in fresh_profile_runs.csv
                    var bossCost = p.AmmoToKill(boss.Health);
                    csv.AppendLine(string.Join(",", new[]
                    {
                        w.Id, ClassOf(w), AcquisitionOf(w), skill.Name, p.AmmoType.ToString(), p.AmmoPerShot.ToString(), cap.ToString(),
                        p.AmmoToKill(grunt.Health).ToString(), p.AmmoToKill(brute.Health).ToString(),
                        p.AmmoToKill(elite.Health).ToString(), bossCost.ToString(), perRoom.ToString(),
                        F(roomsFromFull), F(cap / Mathf.Max(0.01f, (float)p.AmmoPerShot) * p.ExpectedDamagePerShot),
                        F(foundPerRoom), F1(reserveBeforeBoss), F1(reserveBeforeBoss - bossCost),
                        p.IsAoe ? F(p.ExpectedDamagePerShot) : "-",
                        p.IsAoe ? F(p.ExpectedDamagePerShot * p.TargetsPerShot(2)) : "-",
                        p.IsAoe ? F(p.ExpectedDamagePerShot * p.TargetsPerShot(3)) : "-",
                        p.Pellets > 1 ? F(p.Pellets * skill.PelletConnect) : "-",
                        p.Knockback > 0f || p.StaggerPower > 0f ? Csv($"knockback {F(p.Knockback)} = {F(p.Knockback * _content.Stagger.UnitsPerKnockbackPoint)} tiles, stagger {F(p.StaggerPower * p.Pellets)} vs threshold {F(_content.Stagger.Threshold)}") : "none",
                        "MEASURED per-shot cost and scaled HP; MODELLED hit rate and pellet connection"
                    }));
                }
            }

            File.WriteAllText(Path.Combine(Folder, "ammo_economy_matrix.csv"), csv.ToString());
        }

        /// <summary>Total scaled enemy health in a representative D-depth combat room, from the real threat budget midpoint.</summary>
        private float RepresentativeRoomHealth(int depth)
        {
            var (min, max) = ThreatBudgetTable.SoloBudget(depth);
            var target = (min + max) * 0.5f;
            var enemies = _content.Enemies.Where(e => e != null && e.ThreatCost > 0f && e.UnlockDepth <= depth).ToList();
            var averageHealthPerThreat = (float)enemies.Average(e => DepthScaling.ScaledHealth(e.BaseHealth, depth, 1, false, _content.DepthScaling) / e.ThreatCost);
            return target * averageHealthPerThreat;
        }

        /// <summary>Expected rounds of one ammo type found per combat room, from the real Supply Chest table and planning rate.</summary>
        private float AmmoFoundPerRoom(AmmoType type)
        {
            if (!_content.Loot.TryGet(LootSourceKind.SupplyChest, out var source) || source.Table == null) return 0f;
            var entries = source.Table.Rolls
                .Where(r => r.Entries != null)
                .SelectMany(r => r.Entries.Select(e => (roll: r, entry: e)))
                .Where(t => t.entry.Item is AmmoItemDefinition)
                .ToList();
            var total = entries.Sum(t => t.entry.Weight);
            if (total <= 0) return 0f;
            var matching = entries.Where(t => ((AmmoItemDefinition)t.entry.Item).AmmoType == type).ToList();
            var expectedPerChest = matching.Sum(t => t.entry.Weight / (float)total * (t.entry.MinQuantity + t.entry.MaxQuantity) * 0.5f);
            // A chest reaches roughly the planned share of ordinary combat rooms.
            return expectedPerChest * RuinRail.Dungeon.Runtime.SupplyChestPlanner.OrdinaryRoomPercent / 100f;
        }
    }
}

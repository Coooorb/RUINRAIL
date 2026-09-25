using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Core.Rng;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Stats;
using UnityEngine;
using static RuinRail.Tests.FreshRunBalance;

namespace RuinRail.Tests
{
    /// <summary>
    /// Run-side measurement: fresh-profile depths on a fixed seed list, enemy TTK and boss duration across depths,
    /// depth pacing, the coin/merchant economy, the loot rarity curve over a large deterministic sample, and the
    /// descend-vs-return snapshot.
    ///
    /// Every seed used anywhere in this pass is listed in seed_manifest.txt. Nothing here is clock-seeded and nothing
    /// writes a shipping value.
    /// </summary>
    public class FreshRunBalanceRunTests
    {
        public const string Folder = FreshRunBalanceWeaponTests.Folder;

        /// <summary>The fixed seed list for the fresh-profile pass: 30 runs, 10 per biome.</summary>
        public static readonly int[] FreshRunSeeds =
        {
            1000, 1103, 1207, 1311, 1417, 1523, 1629, 1733, 1839, 1947,
            2053, 2161, 2267, 2371, 2477, 2583, 2689, 2797, 2903, 3011,
            3119, 3223, 3329, 3437, 3541, 3647, 3753, 3859, 3967, 4073
        };

        /// <summary>A wider seed block for the loot-curve sample (500+ depth generations).</summary>
        public static readonly int[] LootSeeds = Enumerable.Range(0, 34).Select(i => 7000 + i * 97).ToArray();

        public static readonly int[] DepthLadder = { 1, 5, 10, 20, 30 };
        public static readonly Biome[] Biomes = { Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs };

        private GameContentCatalog _content;
        private FreshRunSimulator _simulator;
        private PriceService _prices;

        [SetUp]
        public void SetUp()
        {
            _content = GameContentCatalog.Load();
            Assert.IsNotNull(_content);
            _simulator = new FreshRunSimulator(_content);
            _prices = new PriceService(_content.Economy);
            Directory.CreateDirectory(Folder);
        }

        private PlayerStats Stats(params StatModifier[] modifiers)
        {
            var stats = new PlayerStats(_content.StatCaps, 100);
            if (modifiers.Length > 0) stats.SetSource(new StatModifierSource("harness", modifiers));
            return stats;
        }

        private WeaponDefinition Weapon(string id) =>
            _content.Items.OfType<WeaponDefinition>().FirstOrDefault(w => w.Id == id)
            ?? throw new InvalidOperationException("no weapon " + id);

        /// <summary>The starter loadout exactly as StarterKitService grants it: no affixes, no accessory, 60 Light.</summary>
        private FreshRunSimulator.Loadout StarterLoadout(SkillProfile skill)
        {
            var stats = Stats();
            return new FreshRunSimulator.Loadout("STARTER",
                new WeaponProfile(Weapon(RuinRail.Gameplay.Base.StarterKitService.PistolId), stats, skill),
                new WeaponProfile(Weapon(RuinRail.Gameplay.Base.StarterKitService.KnifeId), stats, skill),
                new Dictionary<AmmoType, int> { [AmmoType.Light] = RuinRail.Gameplay.Base.StarterKitService.LightAmmoCount },
                _content.PlayerBalance.MaxHealth + ScrapVestHealth(), 1);
        }

        private int ScrapVestHealth()
        {
            var vest = _content.Items.OfType<ArmorDefinition>().FirstOrDefault(a => a.Id == RuinRail.Gameplay.Base.StarterKitService.VestId);
            return vest != null ? vest.BaseMaxHealth : 0;
        }

        /// <summary>A realistic mid-account build: Rare gear with two maximum affixes and one accessory, no impossible stats.</summary>
        private FreshRunSimulator.Loadout MidLoadout(SkillProfile skill)
        {
            var stats = Stats(
                StatModifier.Percent(StatId.WeaponDamage, 10), StatModifier.Percent(StatId.FireRate, 9),
                StatModifier.Percent(StatId.ReloadSpeed, 14), StatModifier.Percent(StatId.MovementSpeed, 5),
                StatModifier.Flat(StatId.MaxHealth, 18), StatModifier.Percent(StatId.GeneralDamageReduction, 7));
            return new FreshRunSimulator.Loadout("MID_RARE",
                new WeaponProfile(Weapon("weapon_marauder_a2"), stats, skill),
                new WeaponProfile(Weapon("weapon_field_knife"), stats, skill),
                new Dictionary<AmmoType, int> { [AmmoType.Medium] = _content.AmmoBalance.GetStackLimit(AmmoType.Medium) / 2 },
                stats.MaxHealth, 2);
        }

        /// <summary>A strong late-account build: Epic weapon affixes, an accessory, and maxed permanent attributes.</summary>
        private FreshRunSimulator.Loadout StrongLoadout(SkillProfile skill)
        {
            var stats = Stats(
                StatModifier.Percent(StatId.WeaponDamage, 20), StatModifier.Percent(StatId.FireRate, 9),
                StatModifier.Percent(StatId.MagazineSize, 20), StatModifier.Percent(StatId.ReloadSpeed, 24),
                StatModifier.Percent(StatId.ProjectileRange, 15), StatModifier.Percent(StatId.MovementSpeed, 13),
                StatModifier.Flat(StatId.MaxHealth, 50), StatModifier.Percent(StatId.GeneralDamageReduction, 16),
                StatModifier.Percent(StatId.KnockbackResistance, 23), StatModifier.Percent(StatId.StaggerResistance, 23));
            return new FreshRunSimulator.Loadout("STRONG_EPIC",
                new WeaponProfile(Weapon("weapon_vanguard"), stats, skill),
                new WeaponProfile(Weapon("weapon_railspike"), stats, skill),
                new Dictionary<AmmoType, int> { [AmmoType.Medium] = _content.AmmoBalance.GetStackLimit(AmmoType.Medium) },
                stats.MaxHealth, 3);
        }

        // ================= PHASE 2 + 3 =================

        [Test]
        public void Phase2And3_WriteThirtyFreshProfileRunsAcrossEverySkillProfile()
        {
            var csv = new StringBuilder();
            csv.AppendLine("Seed,Biome,Depth,SkillProfile,Loadout,StartHp,MaxHp,StartLightAmmo,Rooms,CombatRooms,EliteRooms,HasMerchant,SupplyChests,OtherChests,EventRooms," +
                           "Enemies,ShotsFired,Hits,Misses,HitRate,Reloads,MeleeAttacks,MeleeFallbackRooms,AmmoFound,AmmoPurchased,AmmoUsed," +
                           "ReserveBeforeBoss,AmmoUsedOnBoss,ReserveAfterBoss,RanDryBeforeBoss,BandagesUsed,HpBeforeBoss,HpAfterBoss,LowestHp,Died,DiedAt," +
                           "CoinsEarned,CoinsSpent,ItemsFound,Common,Uncommon,Rare,Epic,Legendary,CombatSeconds,TraversalSeconds,LootSeconds,BossSeconds,RunSeconds,BossId,BossCleared,EnemyMix");
            var runs = new List<FreshRunSimulator.RunResult>();

            foreach (var skill in SkillProfile.All)
            {
                for (var i = 0; i < FreshRunSeeds.Length; i++)
                {
                    var seed = FreshRunSeeds[i];
                    var biome = Biomes[i % Biomes.Length];
                    var run = _simulator.Simulate(StarterLoadout(skill), skill, biome, seed, 1);
                    runs.Add(run);
                    csv.AppendLine(Row(run));
                }
            }

            File.WriteAllText(Path.Combine(Folder, "fresh_profile_runs.csv"), csv.ToString());

            var starterRuns = runs.Where(r => r.SkillProfile == SkillProfile.Normal.Name).ToList();
            Assert.AreEqual(30, starterRuns.Count, "30 deterministic fresh-profile D1 runs at the normal skill profile");
            Assert.AreEqual(3, starterRuns.Select(r => r.Biome).Distinct().Count(), "all three biomes are covered");
            Assert.IsTrue(runs.All(r => r.RoomCount > 0), "every seed generated a real depth");

            // A deterministic harness must be deterministic: the same seed twice must produce the same run.
            var repeat = _simulator.Simulate(StarterLoadout(SkillProfile.Normal), SkillProfile.Normal, Biomes[0], FreshRunSeeds[0], 1);
            var original = runs.First(r => r.Seed == FreshRunSeeds[0] && r.SkillProfile == SkillProfile.Normal.Name);
            Assert.AreEqual(original.ShotsFired, repeat.ShotsFired, "the same seed fires the same number of shots");
            Assert.AreEqual(original.AmmoUsed, repeat.AmmoUsed);
            Assert.AreEqual(original.CoinsEarned, repeat.CoinsEarned);
        }

        private static string Row(FreshRunSimulator.RunResult r)
        {
            r.RarityFound.TryGetValue(Rarity.Common, out var c);
            r.RarityFound.TryGetValue(Rarity.Uncommon, out var u);
            r.RarityFound.TryGetValue(Rarity.Rare, out var ra);
            r.RarityFound.TryGetValue(Rarity.Epic, out var e);
            r.RarityFound.TryGetValue(Rarity.Legendary, out var l);
            r.StartAmmo.TryGetValue(AmmoType.Light, out var light);
            var mix = string.Join(" ", r.EnemiesByArchetype.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + "x" + kv.Value));
            return string.Join(",", new[]
            {
                r.Seed.ToString(), r.Biome.ToString(), r.Depth.ToString(), r.SkillProfile, r.LoadoutName,
                r.StartHealth.ToString(), r.MaxHealth.ToString(), light.ToString(),
                r.RoomCount.ToString(), r.CombatRooms.ToString(), r.EliteRooms.ToString(), r.HasMerchant ? "yes" : "no",
                r.SupplyChests.ToString(), r.OtherChests.ToString(), r.EventRooms.ToString(),
                r.EnemiesKilled.ToString(), r.ShotsFired.ToString(), r.Hits.ToString(), r.Misses.ToString(), F(r.HitRate),
                r.Reloads.ToString(), r.MeleeAttacks.ToString(), r.MeleeFallbackRooms.ToString(),
                r.AmmoFound.ToString(), r.AmmoPurchased.ToString(), r.AmmoUsed.ToString(),
                r.ReserveBeforeBoss.ToString(), r.AmmoUsedOnBoss.ToString(), r.ReserveAfterBoss.ToString(),
                r.RanDryBeforeBoss ? "yes" : "no", r.BandagesUsed.ToString(),
                F1(r.HealthBeforeBoss), F1(r.HealthAfterBoss), F1(r.LowestHealth), r.Died ? "yes" : "no", Csv(r.DiedAt),
                r.CoinsEarned.ToString(), r.CoinsSpent.ToString(), r.ItemsFound.ToString(),
                c.ToString(), u.ToString(), ra.ToString(), e.ToString(), l.ToString(),
                F1(r.CombatSeconds), F1(r.TraversalSeconds), F1(r.LootSeconds), F1(r.BossSeconds), F1(r.RunSeconds),
                r.BossId, r.BossCleared ? "yes" : "no", Csv(mix)
            });
        }

        // ================= PHASE 9 =================

        [Test]
        public void Phase9_WritesTheDepthTtkMatrix()
        {
            var csv = new StringBuilder();
            csv.AppendLine("Depth,Loadout,SkillProfile,Target,TargetKind,ScaledHp,ScaledDamage,ShotsToKill,AmmoToKill,TtkSeconds,IncomingDamage,PlayerHp,SurvivesSolo,Notes");
            foreach (var depth in DepthLadder)
            {
                foreach (var name in new[] { "STARTER", "MID_RARE", "STRONG_EPIC" })
                {
                    foreach (var skill in SkillProfile.All)
                    {
                        var build = Loadouts(skill).First(l => l.name == name).loadout;
                        var weapon = build.Primary;
                        foreach (var target in TargetsAt(depth))
                        {
                            var health = target.profile.Health;
                            var extraDowntime = target.kind == "boss" ? ExposureModel.BossDowntime : target.kind == "elite" ? ExposureModel.EliteDowntime : 0f;
                            var ttk = weapon.TimeToKill(health) * (1f + extraDowntime);
                            var incoming = IncomingDamage(weapon, target.profile, skill, extraDowntime);
                            csv.AppendLine(string.Join(",", new[]
                            {
                                depth.ToString(), name, skill.Name, target.profile.Display, target.kind,
                                F1(health), $"{target.profile.DamageMin}-{target.profile.DamageMax}",
                                weapon.ShotsToKill(health).ToString(), weapon.AmmoToKill(health).ToString(),
                                F(ttk), F1(incoming), build.MaxHealth.ToString(),
                                incoming < build.MaxHealth ? "yes" : "no", Csv(target.notes)
                            }));
                        }

                        // The Shield Enemy is the one archetype with a real counterplay axis: frontal vs flank.
                        var shield = _content.Enemies.First(e => e.Id == "shield_enemy");
                        var shieldProfile = EnemyProfile.From(shield, depth, _content.DepthScaling);
                        foreach (var (label, hp) in new[] { ("shield_enemy (frontal)", shieldProfile.FrontalEffectiveHealth), ("shield_enemy (flanked)", shieldProfile.Health) })
                        {
                            csv.AppendLine(string.Join(",", new[]
                            {
                                depth.ToString(), name, skill.Name, label, "normal",
                                F1(hp), $"{shieldProfile.DamageMin}-{shieldProfile.DamageMax}",
                                weapon.ShotsToKill(hp).ToString(), weapon.AmmoToKill(hp).ToString(),
                                F(weapon.TimeToKill(hp)), F1(IncomingDamage(weapon, shieldProfile, skill)),
                                build.MaxHealth.ToString(), IncomingDamage(weapon, shieldProfile, skill) < build.MaxHealth ? "yes" : "no",
                                Csv($"{shield.FrontalShieldPercent}% frontal shield over a {F(shield.FrontalShieldArcDegrees)} degree arc")
                            }));
                        }

                        // An AoE weapon is measured single-target and against a realistic group, separately.
                        var rocket = new WeaponProfile(Weapon("weapon_pipe_launcher"), StatsFor(name), skill);
                        var grunt = EnemyProfile.From(_content.Enemies.First(e => e.Id == "grunt"), depth, _content.DepthScaling);
                        foreach (var grouped in new[] { 1, 3 })
                        {
                            var effective = grunt.Health * grouped / rocket.TargetsPerShot(grouped);
                            csv.AppendLine(string.Join(",", new[]
                            {
                                depth.ToString(), name + "+rocket", skill.Name, $"grunt x{grouped}", "normal",
                                F1(grunt.Health * grouped), $"{grunt.DamageMin}-{grunt.DamageMax}",
                                rocket.ShotsToKill(effective).ToString(), rocket.AmmoToKill(effective).ToString(),
                                F(rocket.TimeToKill(effective)), F1(IncomingDamage(rocket, grunt, skill)),
                                build.MaxHealth.ToString(), "n/a",
                                Csv(grouped == 1 ? "single target, no splash value" : $"splash reaches {F(rocket.TargetsPerShot(grouped))} targets per shot")
                            }));
                        }
                    }
                }
            }

            File.WriteAllText(Path.Combine(Folder, "depth_ttk_matrix.csv"), csv.ToString());
        }

        private PlayerStats StatsFor(string loadoutName) => loadoutName switch
        {
            "MID_RARE" => Stats(StatModifier.Percent(StatId.WeaponDamage, 10), StatModifier.Percent(StatId.FireRate, 9)),
            "STRONG_EPIC" => Stats(StatModifier.Percent(StatId.WeaponDamage, 20), StatModifier.Percent(StatId.FireRate, 9), StatModifier.Percent(StatId.MagazineSize, 20)),
            _ => Stats()
        };

        private List<(string name, FreshRunSimulator.Loadout loadout)> Loadouts(SkillProfile skill) => new()
        {
            ("STARTER", StarterLoadout(skill)),
            ("MID_RARE", MidLoadout(skill)),
            ("STRONG_EPIC", StrongLoadout(skill))
        };

        private List<(EnemyProfile profile, string kind, string notes)> TargetsAt(int depth)
        {
            var list = new List<(EnemyProfile, string, string)>();
            foreach (var id in new[] { "grunt", "shooter", "charger", "brute", "sniper_enemy", "summoner" })
            {
                var definition = _content.Enemies.FirstOrDefault(e => e.Id == id);
                if (definition == null) continue;
                list.Add((EnemyProfile.From(definition, depth, _content.DepthScaling), "normal",
                    definition.AttackKind == EnemyAttackKind.MeleeContact ? "melee contact" : $"{definition.AttackKind}, reach {F(definition.AttackRange)} tiles"));
            }

            var elite = _content.Elites.OrderBy(e => e.Id, StringComparer.Ordinal).First();
            list.Add((EnemyProfile.From(elite, depth, _content.DepthScaling), "elite", "representative elite; 25% repositioning downtime modelled"));
            var boss = _content.Bosses.OrderBy(b => b.BaseHealth).First();
            list.Add((EnemyProfile.From(boss, depth, _content.DepthScaling), "boss", "lightest boss; 35% telegraph downtime modelled"));
            return list;
        }

        // ================= PHASE 17 =================

        [Test]
        public void Phase17_WritesTheSeedManifest()
        {
            var sb = new StringBuilder();
            sb.AppendLine("RUINRAIL — fresh-run balance pass: deterministic seed manifest");
            sb.AppendLine("Every simulation in this pass derives from a seed on this list. No clock-derived seed is used anywhere.");
            sb.AppendLine();
            sb.AppendLine("## Fresh-profile D1 runs (Phase 2/3) — 30 seeds, biome = seeds[i % 3]");
            for (var i = 0; i < FreshRunSeeds.Length; i++)
                sb.AppendLine($"  {FreshRunSeeds[i],6}  {Biomes[i % Biomes.Length]}");
            sb.AppendLine();
            sb.AppendLine("## Loot-curve sample (Phase 13) — 34 seeds x 5 depths x 3 biomes = 510 depth generations");
            sb.AppendLine("  " + string.Join(", ", LootSeeds));
            sb.AppendLine();
            sb.AppendLine("## Depth ladder (Phases 9/10/11/12/14)");
            sb.AppendLine("  " + string.Join(", ", DepthLadder.Select(d => "D" + d)));
            sb.AppendLine();
            sb.AppendLine("## Skill profiles (stated modelling assumptions, not shipped data)");
            foreach (var s in SkillProfile.All)
                sb.AppendLine($"  {s.Name,-16} accuracy {F(s.Accuracy)}, pellet connection {F(s.PelletConnect)}, extra reloads x{F(s.ReloadWasteFactor)}, dodge efficiency {F(s.DodgeEfficiency)}");
            sb.AppendLine();
            sb.AppendLine("## Exposure model (stated modelling assumptions)");
            sb.AppendLine($"  ranged vs melee enemy   {F(ExposureModel.RangedVsMelee)} of the fight inside its reach");
            sb.AppendLine($"  ranged vs ranged enemy  {F(ExposureModel.RangedVsRanged)}");
            sb.AppendLine($"  melee                   {F(ExposureModel.MeleeContact)}");
            sb.AppendLine($"  boss telegraph downtime {F(ExposureModel.BossDowntime)}");
            sb.AppendLine($"  elite downtime          {F(ExposureModel.EliteDowntime)}");
            File.WriteAllText(Path.Combine(Folder, "seed_manifest.txt"), sb.ToString());
            Assert.AreEqual(30, FreshRunSeeds.Distinct().Count());
            Assert.GreaterOrEqual(LootSeeds.Length * DepthLadder.Length * Biomes.Length, 500, "the loot sample is at least 500 depth generations");
        }
    }
}

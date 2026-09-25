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
    /// Rarity value, melee and blaster economics, boss duration, depth pacing, the coin economy, the loot rarity curve
    /// and the descend snapshot. Same rules as the rest of the pass: shipped data, shipped formulas, deterministic
    /// seeds from <see cref="FreshRunBalanceRunTests"/>, no shipping value written.
    /// </summary>
    public class FreshRunBalanceEconomyTests
    {
        public const string Folder = FreshRunBalanceWeaponTests.Folder;

        private GameContentCatalog _content;
        private PriceService _prices;
        private LootRoller _roller;

        [SetUp]
        public void SetUp()
        {
            _content = GameContentCatalog.Load();
            Assert.IsNotNull(_content);
            _prices = new PriceService(_content.Economy);
            _roller = _content.Loot.CreateRoller();
            Directory.CreateDirectory(Folder);
        }

        private PlayerStats Stats(params StatModifier[] modifiers)
        {
            var stats = new PlayerStats(_content.StatCaps, 100);
            if (modifiers.Length > 0) stats.SetSource(new StatModifierSource("harness", modifiers));
            return stats;
        }

        private WeaponDefinition Weapon(string id) => _content.Items.OfType<WeaponDefinition>().First(w => w.Id == id);

        // ================= PHASE 6 =================

        /// <summary>
        /// The affix combination each rarity actually rolls, chosen to be representative rather than damage-only: one
        /// affix at Uncommon, two at Rare, three at Epic, three plus the family's fixed mechanic at Legendary. Every
        /// value is the pool affix's maximum roll, so the row is the ceiling of that rarity for that weapon.
        /// </summary>
        private static readonly Dictionary<string, StatModifier[][]> RarityCombos = new(StringComparer.Ordinal)
        {
            ["weapon_p9_ranger"] = new[]
            {
                Array.Empty<StatModifier>(),
                new[] { StatModifier.Percent(StatId.FireRate, 9) },
                new[] { StatModifier.Percent(StatId.FireRate, 9), StatModifier.Percent(StatId.MagazineSize, 20) },
                new[] { StatModifier.Percent(StatId.FireRate, 9), StatModifier.Percent(StatId.MagazineSize, 20), StatModifier.Percent(StatId.ReloadSpeed, 14) },
                new[] { StatModifier.Percent(StatId.WeaponDamage, 10), StatModifier.Percent(StatId.FireRate, 9), StatModifier.Percent(StatId.MagazineSize, 20) }
            },
            ["weapon_marauder_a2"] = new[]
            {
                Array.Empty<StatModifier>(),
                new[] { StatModifier.Percent(StatId.MagazineSize, 20) },
                new[] { StatModifier.Percent(StatId.MagazineSize, 20), StatModifier.Percent(StatId.ReloadSpeed, 14) },
                new[] { StatModifier.Percent(StatId.MagazineSize, 20), StatModifier.Percent(StatId.ReloadSpeed, 14), StatModifier.Percent(StatId.ProjectileRange, 15) },
                new[] { StatModifier.Percent(StatId.WeaponDamage, 10), StatModifier.Percent(StatId.FireRate, 9), StatModifier.Percent(StatId.MagazineSize, 20) }
            },
            ["weapon_breacher_12"] = new[]
            {
                Array.Empty<StatModifier>(),
                new[] { StatModifier.Percent(StatId.Knockback, 20) },
                new[] { StatModifier.Percent(StatId.Knockback, 20), StatModifier.Percent(StatId.StaggerPower, 16) },
                new[] { StatModifier.Percent(StatId.Knockback, 20), StatModifier.Percent(StatId.StaggerPower, 16), StatModifier.Percent(StatId.FireRate, 9) },
                new[] { StatModifier.Percent(StatId.WeaponDamage, 10), StatModifier.Percent(StatId.Knockback, 20), StatModifier.Percent(StatId.StaggerPower, 16) }
            },
            ["weapon_farline"] = new[]
            {
                Array.Empty<StatModifier>(),
                new[] { StatModifier.Percent(StatId.ProjectileRange, 15) },
                new[] { StatModifier.Percent(StatId.ProjectileRange, 15), StatModifier.Percent(StatId.ReloadSpeed, 14) },
                new[] { StatModifier.Percent(StatId.ProjectileRange, 15), StatModifier.Percent(StatId.ReloadSpeed, 14), StatModifier.Percent(StatId.ProjectileSpeed, 15) },
                new[] { StatModifier.Percent(StatId.WeaponDamage, 10), StatModifier.Percent(StatId.ProjectileRange, 15), StatModifier.Percent(StatId.ReloadSpeed, 14) }
            },
            ["weapon_pipe_launcher"] = new[]
            {
                Array.Empty<StatModifier>(),
                new[] { StatModifier.Percent(StatId.ReloadSpeed, 14) },
                new[] { StatModifier.Percent(StatId.ReloadSpeed, 14), StatModifier.Percent(StatId.Knockback, 20) },
                new[] { StatModifier.Percent(StatId.ReloadSpeed, 14), StatModifier.Percent(StatId.Knockback, 20), StatModifier.Percent(StatId.ProjectileSpeed, 15) },
                new[] { StatModifier.Percent(StatId.WeaponDamage, 10), StatModifier.Percent(StatId.ReloadSpeed, 14), StatModifier.Percent(StatId.Knockback, 20) }
            },
            ["weapon_pulse_carbine_b1"] = new[]
            {
                Array.Empty<StatModifier>(),
                new[] { StatModifier.Percent(StatId.FireRate, 9) },
                new[] { StatModifier.Percent(StatId.FireRate, 9), StatModifier.Percent(StatId.ProjectileRange, 15) },
                new[] { StatModifier.Percent(StatId.FireRate, 9), StatModifier.Percent(StatId.ProjectileRange, 15), StatModifier.Percent(StatId.ProjectileSpeed, 15) },
                new[] { StatModifier.Percent(StatId.WeaponDamage, 10), StatModifier.Percent(StatId.FireRate, 9), StatModifier.Percent(StatId.ProjectileRange, 15) }
            },
            ["weapon_recurve_bow"] = new[]
            {
                Array.Empty<StatModifier>(),
                new[] { StatModifier.Percent(StatId.BowChargeSpeed, 15) },
                new[] { StatModifier.Percent(StatId.BowChargeSpeed, 15), StatModifier.Percent(StatId.ProjectileSpeed, 15) },
                new[] { StatModifier.Percent(StatId.BowChargeSpeed, 15), StatModifier.Percent(StatId.ProjectileSpeed, 15), StatModifier.Percent(StatId.ProjectileRange, 15) },
                new[] { StatModifier.Percent(StatId.WeaponDamage, 10), StatModifier.Percent(StatId.BowChargeSpeed, 15), StatModifier.Percent(StatId.ProjectileSpeed, 15) }
            },
            ["weapon_field_knife"] = new[]
            {
                Array.Empty<StatModifier>(),
                new[] { StatModifier.Percent(StatId.MeleeAttackSpeed, 9) },
                new[] { StatModifier.Percent(StatId.MeleeAttackSpeed, 9), StatModifier.Percent(StatId.StaggerPower, 16) },
                new[] { StatModifier.Percent(StatId.MeleeAttackSpeed, 9), StatModifier.Percent(StatId.StaggerPower, 16), StatModifier.Percent(StatId.WeaponDamage, 10) },
                new[] { StatModifier.Percent(StatId.WeaponDamage, 10), StatModifier.Percent(StatId.MeleeAttackSpeed, 9), StatModifier.Percent(StatId.StaggerPower, 16) }
            },
            ["weapon_scrap_spear"] = new[]
            {
                Array.Empty<StatModifier>(),
                new[] { StatModifier.Percent(StatId.Knockback, 20) },
                new[] { StatModifier.Percent(StatId.Knockback, 20), StatModifier.Percent(StatId.MeleeAttackSpeed, 9) },
                new[] { StatModifier.Percent(StatId.Knockback, 20), StatModifier.Percent(StatId.MeleeAttackSpeed, 9), StatModifier.Percent(StatId.StaggerPower, 16) },
                new[] { StatModifier.Percent(StatId.WeaponDamage, 10), StatModifier.Percent(StatId.Knockback, 20), StatModifier.Percent(StatId.MeleeAttackSpeed, 9) }
            }
        };

        [Test]
        public void Phase6_WritesTheRarityAffixEffectMatrix()
        {
            var rarities = new[] { Rarity.Common, Rarity.Uncommon, Rarity.Rare, Rarity.Epic, Rarity.Legendary };
            var csv = new StringBuilder();
            csv.AppendLine("WeaponId,Class,Rarity,AffixCount,AffixCombination,SustainedDps,DpsVsCommon,BurstDps,EffectiveRange,RangeVsCommon,Magazine,MagazineUptime,DamagePerAmmoUnit,AmmoEfficiencyVsCommon,HeatPerShot,FullDraw,Knockback,Stagger,TtkGruntD10,TtkVsCommon,Price,Notes");
            var gruntD10 = EnemyProfile.From(_content.Enemies.First(e => e.Id == "grunt"), 10, _content.DepthScaling);

            foreach (var kv in RarityCombos.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var definition = Weapon(kv.Key);
                var baseline = new WeaponProfile(definition, Stats(), SkillProfile.Normal);
                for (var i = 0; i < rarities.Length; i++)
                {
                    var rarity = rarities[i];
                    var modifiers = kv.Value[i];
                    var p = new WeaponProfile(definition, Stats(modifiers), SkillProfile.Normal);
                    var mechanic = rarity == Rarity.Legendary && definition is EquipmentItemDefinition eq && !string.IsNullOrEmpty(eq.LegendaryMechanicId)
                        ? "+ fixed mechanic " + eq.LegendaryMechanicId
                        : string.Empty;
                    csv.AppendLine(string.Join(",", new[]
                    {
                        definition.Id, definition.WeaponClass.ToString(), rarity.ToString(),
                        RarityRules.RandomAffixCount(rarity).ToString(),
                        Csv(modifiers.Length == 0 ? "none" : string.Join(" + ", modifiers.Select(m => $"{m.Stat} +{m.Value}%")) + " " + mechanic),
                        F(p.SustainedDps), Pct(p.SustainedDps, baseline.SustainedDps), F(p.BurstDps),
                        F(p.Range), Pct(p.Range, baseline.Range),
                        p.Magazine > 0 ? p.Magazine.ToString() : "-",
                        F(1f - p.DowntimeRatio), F(p.DamagePerAmmoUnit), Pct(p.DamagePerAmmoUnit, baseline.DamagePerAmmoUnit),
                        p.HeatPerShot > 0f ? F(p.HeatPerShot) : "-", p.FullChargeSeconds > 0f ? F(p.FullChargeSeconds) : "-",
                        F(p.Knockback), F(p.StaggerPower),
                        F(p.TimeToKill(gruntD10.Health)), Pct(baseline.TimeToKill(gruntD10.Health), p.TimeToKill(gruntD10.Health)),
                        _prices.BuyValue(definition, rarity).ToString(),
                        Csv(rarity == Rarity.Common ? "baseline" : "maximum rolls of the listed pool affixes")
                    }));
                }
            }

            File.WriteAllText(Path.Combine(Folder, "rarity_affix_effect.csv"), csv.ToString());
            Assert.AreEqual(9, RarityCombos.Count, "one representative weapon per relevant class");
        }

        private static string Pct(float value, float baseline) =>
            baseline <= 0.0001f ? "-" : (value / baseline - 1f).ToString("+0.0%;-0.0%;0.0%", System.Globalization.CultureInfo.InvariantCulture);

        // ================= PHASE 7 =================

        [Test]
        public void Phase7_WritesMeleeEconomy()
        {
            var csv = new StringBuilder();
            csv.AppendLine("WeaponId,Class,SkillProfile,SustainedDps,AttackRate,Reach,Arc,WindUp,Recovery,Knockback,StaggerPower,StaggersGrunt,TtkGruntD1,TtkGruntD10,TtkBruteD1,TtkEliteD1,TtkBossD1,IncomingPerGruntKill,IncomingPerBruteKill,IncomingPerBossKill,AmmoCost,DpsVsStarterPistol,Notes");
            var pistol = new WeaponProfile(Weapon(RuinRail.Gameplay.Base.StarterKitService.PistolId), Stats(), SkillProfile.Normal);
            var gruntD1 = EnemyProfile.From(_content.Enemies.First(e => e.Id == "grunt"), 1, _content.DepthScaling);
            var gruntD10 = EnemyProfile.From(_content.Enemies.First(e => e.Id == "grunt"), 10, _content.DepthScaling);
            var brute = EnemyProfile.From(_content.Enemies.First(e => e.Id == "brute"), 1, _content.DepthScaling);
            var elite = EnemyProfile.From(_content.Elites.OrderBy(e => e.BaseHealth).First(), 1, _content.DepthScaling);
            var boss = EnemyProfile.From(_content.Bosses.OrderBy(b => b.BaseHealth).First(), 1, _content.DepthScaling);

            foreach (var w in _content.Items.OfType<MeleeWeaponDefinition>().OrderBy(w => w.WeaponClass).ThenBy(w => w.Id, StringComparer.Ordinal))
            {
                foreach (var skill in SkillProfile.All)
                {
                    var p = new WeaponProfile(w, Stats(), skill);
                    var staggerPerHit = p.StaggerPower;
                    var hitsToStagger = staggerPerHit <= 0f ? 0 : Mathf.CeilToInt(_content.Stagger.Threshold / staggerPerHit);
                    csv.AppendLine(string.Join(",", new[]
                    {
                        w.Id, w.WeaponClass.ToString(), skill.Name, F(p.SustainedDps), F(p.ShotsPerSecond), F(p.Range), F(p.ArcDegrees),
                        F(p.WindUpSeconds), F(p.RecoverySeconds), F(p.Knockback), F(p.StaggerPower),
                        hitsToStagger > 0 ? Csv($"{hitsToStagger} hits (recovery {F(_content.Stagger.RecoveryPerSecond)}/s)") : "never",
                        F(p.TimeToKill(gruntD1.Health)), F(p.TimeToKill(gruntD10.Health)), F(p.TimeToKill(brute.Health)),
                        F(p.TimeToKill(elite.Health) * (1f + ExposureModel.EliteDowntime)), F(p.TimeToKill(boss.Health) * (1f + ExposureModel.BossDowntime)),
                        F1(IncomingDamage(p, gruntD1, skill)), F1(IncomingDamage(p, brute, skill)), F1(IncomingDamage(p, boss, skill, ExposureModel.BossDowntime)),
                        "0", Pct(p.SustainedDps, pistol.SustainedDps),
                        Csv($"contact range; full exposure modelled; zero reserve cost; {(p.Knockback > 0f ? F(p.Knockback * _content.Stagger.UnitsPerKnockbackPoint) + " tile shove creates its own spacing" : "no shove, so the target stays in reach")}")
                    }));
                }
            }

            File.WriteAllText(Path.Combine(Folder, "melee_economy.csv"), csv.ToString());
        }

        // ================= PHASE 8 =================

        [Test]
        public void Phase8_WritesBlasterEconomy()
        {
            var csv = new StringBuilder();
            csv.AppendLine("WeaponId,SkillProfile,HeatLimitedDps,ShotsPerHeatCycle,SecondsToOverheat,LockoutSeconds,FullCoolSeconds,EffectiveDowntimeRatio,DamagePerMinute,DamagePerCycle,VsPistolDpm,VsArDpm,AmmoCostPerDepth,EquivalentReserveSaved,Notes");
            var pistol = new WeaponProfile(Weapon(RuinRail.Gameplay.Base.StarterKitService.PistolId), Stats(), SkillProfile.Normal);
            var ar = new WeaponProfile(Weapon("weapon_marauder_a2"), Stats(), SkillProfile.Normal);

            foreach (var w in _content.Items.OfType<BlasterWeaponDefinition>().OrderBy(w => w.Id, StringComparer.Ordinal))
            {
                foreach (var skill in SkillProfile.All)
                {
                    var p = new WeaponProfile(w, Stats(), skill);
                    var interval = 1f / Mathf.Max(0.0001f, p.ShotsPerSecond);
                    var secondsToOverheat = p.ShotsToOverheat * interval;
                    var dpm = p.SustainedDps * 60f;
                    // What a depth's worth of the same damage would cost an ammo weapon.
                    var depthDamage = p.SustainedDps * 240f; // a measured D1 depth is a few minutes of engagement
                    var arRounds = ar.ExpectedDamagePerShot <= 0f ? 0f : depthDamage / ar.ExpectedDamagePerShot * ar.AmmoPerShot;
                    csv.AppendLine(string.Join(",", new[]
                    {
                        w.Id, skill.Name, F(p.SustainedDps), p.ShotsToOverheat.ToString(), F(secondsToOverheat), F(p.OverheatLockout),
                        F(p.FullCoolSeconds), F(p.DowntimeRatio), F1(dpm), F1(p.ShotsToOverheat * p.ExpectedDamagePerShot),
                        Pct(p.SustainedDps, pistol.SustainedDps), Pct(p.SustainedDps, ar.SustainedDps),
                        "0", F1(arRounds),
                        Csv($"no reserve ammo; {p.ShotsToOverheat} shots then a {F(p.OverheatLockout)}s lockout; cooling {F(p.CoolingRate)}/s after a {F(p.CoolingDelay)}s delay")
                    }));
                }
            }

            File.WriteAllText(Path.Combine(Folder, "blaster_economy.csv"), csv.ToString());
        }

        // ================= PHASE 10 =================

        [Test]
        public void Phase10_WritesBossDurationMatrix()
        {
            var csv = new StringBuilder();
            csv.AppendLine("BossId,Biome,Depth,Build,SkillProfile,ScaledHp,StrongestAttack,TheoreticalDpsSeconds,PracticalSeconds,DamagingTimePercent,CycleDowntimeSeconds,DodgeDowntimeSeconds,AmmoConsumed,ReserveCap,CompletableFromFullReserve,BandagesNeeded,DamageTaken,PlayerHp,MeleeFallbackRequired,AttritionFlag,Notes");
            foreach (var boss in _content.Bosses.Where(b => b != null).OrderBy(b => b.Id, StringComparer.Ordinal))
            {
                foreach (var depth in FreshRunBalanceRunTests.DepthLadder)
                {
                    foreach (var build in Builds())
                    {
                        foreach (var skill in SkillProfile.All)
                        {
                            var p = new WeaponProfile(Weapon(build.weaponId), Stats(build.modifiers), skill);
                            var profile = EnemyProfile.From(boss, depth, _content.DepthScaling);
                            var pure = p.ShotsToKill(profile.Health) / Mathf.Max(0.0001f, p.ShotsPerSecond);
                            var withCycle = p.TimeToKill(profile.Health);
                            var practical = withCycle * (1f + ExposureModel.BossDowntime);
                            var ammo = p.AmmoToKill(profile.Health);
                            var cap = p.UsesAmmo ? _content.AmmoBalance.GetStackLimit(p.AmmoType) : 0;
                            var taken = IncomingDamage(p, profile, skill, ExposureModel.BossDowntime);
                            var bandages = Mathf.CeilToInt(Mathf.Max(0f, taken - build.maxHealth * 0.5f) / 40f);
                            csv.AppendLine(string.Join(",", new[]
                            {
                                boss.Id, boss.Biome.ToString(), depth.ToString(), build.name, skill.Name,
                                F1(profile.Health), $"{profile.DamageMin}-{profile.DamageMax}",
                                F(pure), F(practical), F(pure / Mathf.Max(0.0001f, practical) * 100f),
                                F(withCycle - pure), F(practical - withCycle),
                                p.UsesAmmo ? ammo.ToString() : "0", cap.ToString(),
                                p.UsesAmmo ? (ammo <= cap ? "yes" : "no") : "n/a (no reserve)",
                                bandages.ToString(), F1(taken), build.maxHealth.ToString(),
                                p.UsesAmmo && ammo > cap ? "yes" : "no",
                                practical > 120f ? "LONG_ATTRITION" : practical > 60f ? "watch" : "no",
                                Csv($"phase 2 at {F(boss.PhaseTwoHealthFraction * 100f)}% HP, timing x{F(boss.PhaseTwoTimingMultiplier)}; {F(ExposureModel.BossDowntime * 100f)}% telegraph downtime modelled")
                            }));
                        }
                    }
                }
            }

            File.WriteAllText(Path.Combine(Folder, "boss_duration_matrix.csv"), csv.ToString());
            Assert.AreEqual(6, _content.Bosses.Count(b => b != null), "all six bosses measured");
        }

        private List<(string name, string weaponId, int maxHealth, StatModifier[] modifiers)> Builds() => new()
        {
            ("STARTER", RuinRail.Gameplay.Base.StarterKitService.PistolId, 120, Array.Empty<StatModifier>()),
            ("MID_RARE", "weapon_marauder_a2", 138, new[]
            {
                StatModifier.Percent(StatId.WeaponDamage, 10), StatModifier.Percent(StatId.FireRate, 9), StatModifier.Percent(StatId.ReloadSpeed, 14)
            }),
            ("STRONG_EPIC", "weapon_vanguard", 170, new[]
            {
                StatModifier.Percent(StatId.WeaponDamage, 20), StatModifier.Percent(StatId.FireRate, 9),
                StatModifier.Percent(StatId.MagazineSize, 20), StatModifier.Percent(StatId.ReloadSpeed, 24)
            })
        };
    }
}

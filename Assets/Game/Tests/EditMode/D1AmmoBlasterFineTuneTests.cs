using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat.Impact;
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
    /// The D1 ammo / blaster fine-tuning pass: a small Depth 1 Light-ammo correction sized to the owner's observed
    /// 10–15 round shortfall in mixed P9 + Field Knife play, and a mild blaster buff on the heat axis.
    ///
    /// The ammo before/after runs on one code path in one process: the shipped Supply Chest table and a candidate
    /// clone of it are both driven through the same simulator over the same seed list, so the delta is the change and
    /// nothing else. The blaster before/after is computed from the shipped definitions against the recorded pre-change
    /// values pinned in <see cref="BlasterBefore"/>.
    ///
    /// Frozen values are asserted, not assumed: <see cref="Phase6_FrozenValuesDidNotChange"/> fails if the Field Knife,
    /// boss HP, ammo caps, depth scaling or any non-blaster weapon moved.
    /// </summary>
    public class D1AmmoBlasterFineTuneTests
    {
        public const string Folder = "TestResults/D1AmmoBlasterFineTune";

        /// <summary>The 30 deterministic seeds from the fresh-run validation pass, reused so before/after is comparable.</summary>
        private static int[] Seeds => FreshRunBalanceRunTests.FreshRunSeeds;
        private static Biome[] Biomes => FreshRunBalanceRunTests.Biomes;

        /// <summary>
        /// The Supply Chest Light-ammo quantity this pass shipped, and the value it replaced. The "before" figure is the
        /// value recorded from the working tree at the start of the pass; the test asserts the asset now holds the
        /// "after" figure, so the artefacts can never drift from the repository.
        /// </summary>
        public const int LightAmmoMinBefore = 20;
        public const int LightAmmoMaxBefore = 40;
        public const int LightAmmoMinAfter = 26;
        public const int LightAmmoMaxAfter = 46;

        /// <summary>Blaster heat values as they stood before this pass, per id: (heatPerShot, cooling, maxHeat, coolingDelay, lockout).</summary>
        public static readonly Dictionary<string, (float Heat, float Cooling, float MaxHeat, float Delay, float Lockout)> BlasterBefore =
            new(StringComparer.Ordinal)
            {
                ["weapon_arc_blaster_b4"] = (10f, 35f, 100f, 0.6f, 2.2f),
                ["weapon_pulse_carbine_b1"] = (7f, 35f, 100f, 0.6f, 2.2f),
                ["weapon_redline"] = (8f, 40f, 100f, 0.6f, 2.2f)
            };

        /// <summary>Everything this pass promised not to touch, with the value it must still have.</summary>
        private static readonly (string Subject, string Field, string Expected)[] Frozen =
        {
            ("weapon_field_knife", "damage", "14-17"),
            ("weapon_field_knife", "attack rate", "3.5"),
            ("weapon_field_knife", "reach", "1.2"),
            ("weapon_field_knife", "arc", "80"),
            ("weapon_field_knife", "wind-up / recovery", "0.08 / 0.12"),
            ("weapon_field_knife", "knockback / stagger", "0 / 3"),
            ("weapon_p9_ranger", "damage", "12-14"),
            ("weapon_p9_ranger", "fire rate / magazine / reload", "4 / 12 / 1.2"),
            ("ammo caps", "Light / Medium / Heavy / Shells", "180 / 120 / 60 / 40"),
            ("starter kit", "Light ammo granted", "60"),
        };

        private GameContentCatalog _content;
        private FreshRunSimulator _simulator;
        private PriceService _prices;
        private readonly List<ScriptableObject> _created = new();

        [SetUp]
        public void SetUp()
        {
            _content = GameContentCatalog.Load();
            Assert.IsNotNull(_content);
            _simulator = new FreshRunSimulator(_content);
            _prices = new PriceService(_content.Economy);
            Directory.CreateDirectory(Folder);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            _created.Clear();
        }

        private PlayerStats Stats() => new(_content.StatCaps, _content.PlayerBalance.MaxHealth);
        private WeaponDefinition Weapon(string id) => _content.Items.OfType<WeaponDefinition>().First(w => w.Id == id);
        private LootTableDefinition SupplyTable() => _content.Loot.TryGet(LootSourceKind.SupplyChest, out var s) ? s.Table : null;

        // ---- the three usage profiles the pass is measured against ----

        private const float MixedMeleeShare = 0.5f;
        private const float PistolHeavyMeleeShare = 0f;
        private const float MeleeHeavyMeleeShare = 0.85f;

        private FreshRunSimulator.Loadout Starter(string name, float meleeShare)
        {
            var stats = Stats();
            var vest = _content.Items.OfType<ArmorDefinition>().First(a => a.Id == RuinRail.Gameplay.Base.StarterKitService.VestId);
            return new FreshRunSimulator.Loadout(name,
                new WeaponProfile(Weapon(RuinRail.Gameplay.Base.StarterKitService.PistolId), stats, SkillProfile.Normal),
                new WeaponProfile(Weapon(RuinRail.Gameplay.Base.StarterKitService.KnifeId), stats, SkillProfile.Normal),
                new Dictionary<AmmoType, int> { [AmmoType.Light] = RuinRail.Gameplay.Base.StarterKitService.LightAmmoCount },
                _content.PlayerBalance.MaxHealth + vest.BaseMaxHealth, 1, meleeShare);
        }

        /// <summary>A clone of the shipped Supply Chest table with the Light-ammo quantity replaced.</summary>
        private LootTableDefinition SupplyTableWithLightQuantity(int min, int max)
        {
            var source = SupplyTable();
            Assert.IsNotNull(source, "the Supply Chest table");
            var clone = ScriptableObject.CreateInstance<LootTableDefinition>();
            _created.Add(clone);
            var rolls = source.Rolls.Select(r => new LootTableDefinition.Roll
            {
                Label = r.Label,
                ChancePercent = r.ChancePercent,
                Entries = (r.Entries ?? Array.Empty<LootTableDefinition.Entry>()).Select(e => new LootTableDefinition.Entry
                {
                    Item = e.Item,
                    LegendaryVariant = e.LegendaryVariant,
                    Weight = e.Weight,
                    MinQuantity = e.Item is AmmoItemDefinition ammo && ammo.AmmoType == AmmoType.Light ? min : e.MinQuantity,
                    MaxQuantity = e.Item is AmmoItemDefinition ammo2 && ammo2.AmmoType == AmmoType.Light ? max : e.MaxQuantity
                }).ToArray()
            }).ToArray();
            typeof(LootTableDefinition).GetField("_rolls", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(clone, rolls);
            typeof(LootTableDefinition).GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(clone, source.Id + "_candidate");
            return clone;
        }

        private sealed class Sample
        {
            public readonly List<FreshRunSimulator.RunResult> Runs = new();
            public float Median(Func<FreshRunSimulator.RunResult, float> pick)
            {
                var ordered = Runs.Select(pick).OrderBy(v => v).ToList();
                return ordered.Count == 0 ? 0f : ordered.Count % 2 == 1 ? ordered[ordered.Count / 2] : (ordered[ordered.Count / 2 - 1] + ordered[ordered.Count / 2]) * 0.5f;
            }

            public float Mean(Func<FreshRunSimulator.RunResult, float> pick) => Runs.Count == 0 ? 0f : Runs.Average(pick);
        }

        private Sample Run(string name, float meleeShare, LootTableDefinition supplyOverride)
        {
            var sample = new Sample();
            _simulator.SupplyTableOverride = supplyOverride;
            try
            {
                for (var i = 0; i < Seeds.Length; i++)
                    sample.Runs.Add(_simulator.Simulate(Starter(name, meleeShare), SkillProfile.Normal, Biomes[i % Biomes.Length], Seeds[i], 1));
            }
            finally
            {
                _simulator.SupplyTableOverride = null;
            }

            return sample;
        }

        // ================= PHASE 1 =================

        [Test]
        public void Phase1_WritesTheBaselineBefore()
        {
            var pistol = (RangedWeaponDefinition)Weapon(RuinRail.Gameplay.Base.StarterKitService.PistolId);
            var knife = (MeleeWeaponDefinition)Weapon(RuinRail.Gameplay.Base.StarterKitService.KnifeId);
            var stats = Stats();
            var pistolProfile = new WeaponProfile(pistol, stats, SkillProfile.Normal);
            var knifeProfile = new WeaponProfile(knife, stats, SkillProfile.Normal);
            var supply = SupplyTable();
            var lightEntry = supply.Rolls.SelectMany(r => r.Entries ?? Array.Empty<LootTableDefinition.Entry>())
                .First(e => e.Item is AmmoItemDefinition a && a.AmmoType == AmmoType.Light);

            var csv = new StringBuilder();
            csv.AppendLine("Subject,Field,ValueBefore,Source,Frozen,Notes");
            void Row(string subject, string field, string value, string source, bool frozen, string notes = "") =>
                csv.AppendLine(string.Join(",", subject, field, Csv(value), Csv(source), frozen ? "FROZEN" : "in scope", Csv(notes)));

            Row("starter P9 Ranger", "damage", $"{pistol.DamageMin}-{pistol.DamageMax}", "P9Ranger.asset", true);
            Row("starter P9 Ranger", "fire rate", F(pistol.FireRate), "P9Ranger.asset", true);
            Row("starter P9 Ranger", "magazine", pistol.MagazineSize.ToString(), "P9Ranger.asset", true);
            Row("starter P9 Ranger", "reload", F(pistol.ReloadTime) + "s", "P9Ranger.asset", true);
            Row("starter P9 Ranger", "range / projectile speed", $"{F(pistol.Range)} / {F(pistol.ProjectileSpeed)}", "P9Ranger.asset", true);
            Row("starter P9 Ranger", "ammo type / cost per shot", $"{pistol.AmmoType} / {pistol.AmmoCostPerShot}", "P9Ranger.asset", true);
            Row("starter P9 Ranger", "sustained DPS (normal accuracy)", F(pistolProfile.SustainedDps), "WeaponStatMath", true);
            Row("starter Field Knife", "damage", $"{knife.DamageMin}-{knife.DamageMax}", "FieldKnife.asset", true, "owner: correctly tuned, do not change");
            Row("starter Field Knife", "attack rate", F(knife.AttackRate), "FieldKnife.asset", true);
            Row("starter Field Knife", "reach / arc", $"{F(knife.AttackRange)} / {F(knife.AttackArcDegrees)}", "FieldKnife.asset", true);
            Row("starter Field Knife", "wind-up / recovery", $"{F(knife.WindUpSeconds)} / {F(knife.RecoverySeconds)}", "FieldKnife.asset", true);
            Row("starter Field Knife", "sustained DPS (normal accuracy)", F(knifeProfile.SustainedDps), "WeaponStatMath", true);
            Row("starter kit", "Light ammo granted", RuinRail.Gameplay.Base.StarterKitService.LightAmmoCount.ToString(), "StarterKitService", false, "Option B lever; not used");
            Row("ammo caps", "Light / Medium / Heavy / Shells",
                $"{_content.AmmoBalance.GetStackLimit(AmmoType.Light)} / {_content.AmmoBalance.GetStackLimit(AmmoType.Medium)} / {_content.AmmoBalance.GetStackLimit(AmmoType.Heavy)} / {_content.AmmoBalance.GetStackLimit(AmmoType.Shells)}",
                "AmmoBalanceConfig", true);
            Row("Supply Chest", "planned share of eligible ordinary rooms", SupplyChestPlanner.OrdinaryRoomPercent + "%", "SupplyChestPlanner", false);
            Row("Supply Chest", "guaranteed minimum per depth", SupplyChestPlanner.MinimumPerDepth.ToString(), "SupplyChestPlanner", false, "Option A lever; not used (a whole extra chest is ~30 rounds)");
            Row("Supply Chest", "ammo roll chance", supply.Rolls.First(r => r.Label == "Ammo").ChancePercent + "%", "LootTable_SupplyChest", false);
            Row("Supply Chest", "Light ammo quantity", $"{lightEntry.MinQuantity}-{lightEntry.MaxQuantity}", "LootTable_SupplyChest", false, "THE LEVER THIS PASS USES");
            Row("Supply Chest", "Light ammo pick weight", lightEntry.Weight.ToString(), "LootTable_SupplyChest", false);
            Row("loot roller", "useful-ammo weighting", LootRoller.UsefulAmmoPercent + "%", "LootRoller", false, "Option A lever; measured and rejected (~4 rounds)");
            Row("D1 boss HP", "base range", $"{_content.Bosses.Min(b => b.BaseHealth)}-{_content.Bosses.Max(b => b.BaseHealth)}", "Boss_*.asset", true);
            Row("D1 depth scaling", "enemy HP / damage multiplier", $"x{F(DepthScaling.HealthMultiplier(1, _content.DepthScaling))} / x{F(DepthScaling.DamageMultiplier(1, _content.DepthScaling))}", "DepthScalingConfig", true);
            Row("D1 threat budget", "solo min-max", $"{F1(ThreatBudgetTable.SoloBudget(1).min)}-{F1(ThreatBudgetTable.SoloBudget(1).max)}", "ThreatBudgetTable", true);
            Row("P9 practical need", "rounds per D1 boss (normal accuracy)", pistolProfile.AmmoToKill(_content.Bosses.Min(b => b.BaseHealth)).ToString(), "WeaponProfile", false, "measured, not authored");

            foreach (var id in BlasterBefore.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                var blaster = (BlasterWeaponDefinition)Weapon(id);
                var before = BlasterBefore[id];
                var profile = new WeaponProfile(blaster, stats, SkillProfile.Normal);
                Row(id, "damage", $"{blaster.DamageMin}-{blaster.DamageMax}", id + ".asset", true, "raw damage is the last-resort axis; unchanged");
                Row(id, "fire rate", F(blaster.FireRate), id + ".asset", true);
                Row(id, "heat per shot", F(before.Heat), "recorded pre-pass value", false);
                Row(id, "cooling rate", F(before.Cooling), "recorded pre-pass value", false);
                Row(id, "max heat", F(before.MaxHeat), "recorded pre-pass value", true);
                Row(id, "cooling delay", F(before.Delay) + "s", "recorded pre-pass value", false, "THE LEVER THIS PASS USES");
                Row(id, "overheat lockout", F(before.Lockout) + "s", "recorded pre-pass value", false, "THE LEVER THIS PASS USES");
                Row(id, "shots per heat cycle", Before(id).ShotsToOverheat.ToString(), "derived", false);
                Row(id, "practical sustained DPS (normal accuracy)", F(Before(id).SustainedDps), "derived", false);
                Row(id, "share of cycle unable to fire", F(Before(id).DowntimeRatio * 100f) + "%", "derived", false);
            }

            File.WriteAllText(Path.Combine(Folder, "baseline_before.csv"), csv.ToString());
            Assert.IsTrue(File.Exists(Path.Combine(Folder, "baseline_before.csv")));
        }

        /// <summary>A blaster profile built from the recorded pre-pass heat values, for honest before/after arithmetic.</summary>
        private WeaponProfile Before(string id)
        {
            var shipped = (BlasterWeaponDefinition)Weapon(id);
            var before = BlasterBefore[id];
            var clone = ScriptableObject.Instantiate(shipped);
            _created.Add(clone);
            void Set(string field, object value) =>
                typeof(BlasterWeaponDefinition).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(clone, value);
            Set("_heatPerShot", before.Heat);
            Set("_coolingRatePerSecond", before.Cooling);
            Set("_maxHeat", before.MaxHeat);
            Set("_coolingDelaySeconds", before.Delay);
            Set("_overheatLockoutSeconds", before.Lockout);
            return new WeaponProfile(clone, Stats(), SkillProfile.Normal);
        }

        // ================= PHASE 3 =================

        [Test]
        public void Phase3_WritesTheD1AmmoBeforeAndAfter()
        {
            var beforeTable = SupplyTableWithLightQuantity(LightAmmoMinBefore, LightAmmoMaxBefore);
            var afterTable = SupplyTable();
            var lightEntry = afterTable.Rolls.SelectMany(r => r.Entries ?? Array.Empty<LootTableDefinition.Entry>())
                .First(e => e.Item is AmmoItemDefinition a && a.AmmoType == AmmoType.Light);
            Assert.AreEqual(LightAmmoMinAfter, lightEntry.MinQuantity, "the shipped Supply Chest Light quantity is the pass's 'after' value");
            Assert.AreEqual(LightAmmoMaxAfter, lightEntry.MaxQuantity);

            var csv = new StringBuilder();
            csv.AppendLine("UsageProfile,MeleeShare,Phase,Seed,Biome,StartLightAmmo,LightFound,LightSpentInRooms,VoluntaryMeleeKills,ForcedMeleeKills,MeleeAttacks," +
                           "AmmoBeforeBoss,AmmoSpentOnBoss,AmmoAfterBoss,MeleeUsedAtBoss,MeleeRequiredAtBoss,DepthSeconds,Rooms,CombatRooms,SupplyChests");

            var results = new Dictionary<(string profile, string phase), Sample>();
            foreach (var (name, share) in new[] { ("MIXED", MixedMeleeShare), ("PISTOL_HEAVY", PistolHeavyMeleeShare), ("MELEE_HEAVY", MeleeHeavyMeleeShare) })
            {
                foreach (var (phase, table) in new[] { ("BEFORE", beforeTable), ("AFTER", (LootTableDefinition)null) })
                {
                    var sample = Run(name, share, table);
                    results[(name, phase)] = sample;
                    foreach (var r in sample.Runs)
                    {
                        r.StartAmmo.TryGetValue(AmmoType.Light, out var start);
                        var roomsSpend = r.AmmoUsed - r.AmmoUsedOnBoss;
                        csv.AppendLine(string.Join(",", new[]
                        {
                            name, F(share), phase, r.Seed.ToString(), r.Biome.ToString(),
                            start.ToString(), r.AmmoFound.ToString(), roomsSpend.ToString(),
                            r.VoluntaryMeleeKills.ToString(), r.ForcedMeleeKills.ToString(), r.MeleeAttacks.ToString(),
                            r.ReserveBeforeBoss.ToString(), r.AmmoUsedOnBoss.ToString(), r.ReserveAfterBoss.ToString(),
                            r.MeleeFallbackRooms > 0 ? "yes" : "no", r.RanDryBeforeBoss || r.ReserveAfterBoss == 0 ? "yes" : "no",
                            F1(r.RunSeconds), r.RoomCount.ToString(), r.CombatRooms.ToString(), r.SupplyChests.ToString()
                        }));
                    }
                }
            }

            csv.AppendLine();
            csv.AppendLine("# SUMMARY — median and mean usable Light rounds on arrival at the boss");
            csv.AppendLine("UsageProfile,MedianBefore,MedianAfter,MedianGain,MeanBefore,MeanAfter,MeanGain,LightFoundBefore,LightFoundAfter,ForcedMeleeKillsBefore,ForcedMeleeKillsAfter");
            foreach (var name in new[] { "MIXED", "PISTOL_HEAVY", "MELEE_HEAVY" })
            {
                var b = results[(name, "BEFORE")];
                var a = results[(name, "AFTER")];
                csv.AppendLine(string.Join(",", new[]
                {
                    name,
                    F1(b.Median(r => r.ReserveBeforeBoss)), F1(a.Median(r => r.ReserveBeforeBoss)), F1(a.Median(r => r.ReserveBeforeBoss) - b.Median(r => r.ReserveBeforeBoss)),
                    F1(b.Mean(r => r.ReserveBeforeBoss)), F1(a.Mean(r => r.ReserveBeforeBoss)), F1(a.Mean(r => r.ReserveBeforeBoss) - b.Mean(r => r.ReserveBeforeBoss)),
                    F1(b.Mean(r => r.AmmoFound)), F1(a.Mean(r => r.AmmoFound)),
                    F1(b.Mean(r => r.ForcedMeleeKills)), F1(a.Mean(r => r.ForcedMeleeKills))
                }));
            }

            File.WriteAllText(Path.Combine(Folder, "d1_ammo_after.csv"), csv.ToString());

            // ---- acceptance ----
            var mixedBefore = results[("MIXED", "BEFORE")];
            var mixedAfter = results[("MIXED", "AFTER")];
            var mixedGain = mixedAfter.Median(r => r.ReserveBeforeBoss) - mixedBefore.Median(r => r.ReserveBeforeBoss);
            Assert.GreaterOrEqual(mixedGain, 8f, $"the mixed profile must gain roughly 10-15 usable Light rounds (gained {mixedGain:0.#})");
            Assert.LessOrEqual(mixedGain, 18f, $"the correction must stay small (gained {mixedGain:0.#})");

            Assert.AreEqual(30, mixedAfter.Runs.Count, "30 deterministic fresh-profile D1 runs");
            Assert.AreEqual(3, mixedAfter.Runs.Select(r => r.Biome).Distinct().Count(), "all three biomes");

            // Pistol-only clearing must still cost something: it must still run the firearm dry and still need the knife.
            var pistolHeavy = results[("PISTOL_HEAVY", "AFTER")];
            Assert.Greater(pistolHeavy.Mean(r => r.ForcedMeleeKills), 0f,
                "pistol-heavy play must still be forced onto the secondary somewhere in the depth");
            Assert.Less(pistolHeavy.Median(r => r.ReserveBeforeBoss), _content.AmmoBalance.GetStackLimit(AmmoType.Light) * 0.5f,
                "pistol-heavy play must not arrive at the boss with a near-full reserve");
            Assert.Greater(mixedAfter.Median(r => r.ReserveBeforeBoss), pistolHeavy.Median(r => r.ReserveBeforeBoss),
                "mixing weapons must still be the ammo-efficient choice");

            // And melee must remain worth using rather than being made redundant by the extra ammo.
            var meleeHeavy = results[("MELEE_HEAVY", "AFTER")];
            Assert.Greater(meleeHeavy.Median(r => r.ReserveBeforeBoss), mixedAfter.Median(r => r.ReserveBeforeBoss),
                "leaning on melee must still conserve more ammo than mixed play");
        }

        /// <summary>
        /// Calibration sweep used to size the correction. Explicit, so it does not run in a normal suite: it exists so
        /// the chosen quantity is reproducible rather than asserted from memory.
        /// </summary>
        [Test, Explicit]
        public void CalibrateLightAmmoQuantity()
        {
            var baseline = Run("MIXED", MixedMeleeShare, SupplyTableWithLightQuantity(LightAmmoMinBefore, LightAmmoMaxBefore));
            var baseMedian = baseline.Median(r => r.ReserveBeforeBoss);
            Debug.Log($"[CALIB] BEFORE 20-40: median reserve at boss {baseMedian:0.#}, mean {baseline.Mean(r => r.ReserveBeforeBoss):0.#}, light found {baseline.Mean(r => r.AmmoFound):0.#}");
            foreach (var bump in new[] { 2, 4, 5, 6, 8, 10, 12 })
            {
                var sample = Run("MIXED", MixedMeleeShare, SupplyTableWithLightQuantity(LightAmmoMinBefore + bump, LightAmmoMaxBefore + bump));
                var pistol = Run("PISTOL_HEAVY", PistolHeavyMeleeShare, SupplyTableWithLightQuantity(LightAmmoMinBefore + bump, LightAmmoMaxBefore + bump));
                Debug.Log($"[CALIB] +{bump} ({LightAmmoMinBefore + bump}-{LightAmmoMaxBefore + bump}): mixed median {sample.Median(r => r.ReserveBeforeBoss):0.#} " +
                          $"(gain {sample.Median(r => r.ReserveBeforeBoss) - baseMedian:+0.#;-0.#}), mixed mean gain {sample.Mean(r => r.ReserveBeforeBoss) - baseline.Mean(r => r.ReserveBeforeBoss):+0.#;-0.#}, " +
                          $"light found {sample.Mean(r => r.AmmoFound):0.#}, pistol-heavy median {pistol.Median(r => r.ReserveBeforeBoss):0.#}, pistol-heavy forced melee kills {pistol.Mean(r => r.ForcedMeleeKills):0.##}");
            }
        }

        // ================= PHASE 5 =================

        [Test]
        public void Phase5_WritesBlasterBeforeAndAfter()
        {
            var csv = new StringBuilder();
            csv.AppendLine("WeaponId,Phase,DamageMin,DamageMax,FireRate,HeatPerShot,CoolingRate,MaxHeat,CoolingDelay,OverheatLockout," +
                           "ShotsPerHeatCycle,SecondsToOverheat,CycleSeconds,UnableToFirePercent,PracticalSustainedDps,Damage30s,Damage60s," +
                           "RecoveryFromFullHeat,RecoveryFromHalfHeat,WithMaxCoolingAffix,WithMaxHeatReductionAffix,Notes");

            var deltas = new List<(string id, float before, float after, float downBefore, float downAfter)>();
            foreach (var id in BlasterBefore.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                var shipped = (BlasterWeaponDefinition)Weapon(id);
                var before = Before(id);
                var after = new WeaponProfile(shipped, Stats(), SkillProfile.Normal);
                var recorded = BlasterBefore[id];

                void Row(string phase, WeaponProfile p, float heat, float cooling, float maxHeat, float delay, float lockout, string notes)
                {
                    var interval = 1f / Mathf.Max(0.0001f, p.ShotsPerSecond);
                    var cycle = p.ShotsToOverheat * interval + lockout;
                    csv.AppendLine(string.Join(",", new[]
                    {
                        id, phase, shipped.DamageMin.ToString(), shipped.DamageMax.ToString(), F(shipped.FireRate),
                        F(heat), F(cooling), F(maxHeat), F(delay), F(lockout),
                        p.ShotsToOverheat.ToString(), F(p.ShotsToOverheat * interval), F(cycle), F(p.DowntimeRatio * 100f),
                        F(p.SustainedDps), F1(p.SustainedDps * 30f), F1(p.SustainedDps * 60f),
                        F(delay + maxHeat / Mathf.Max(0.0001f, cooling)), F(delay + maxHeat * 0.5f / Mathf.Max(0.0001f, cooling)),
                        F(CoolingWithAffix(p, cooling, delay, maxHeat)), F(HeatWithAffix(p, heat)),
                        Csv(notes)
                    }));
                }

                Row("BEFORE", before, recorded.Heat, recorded.Cooling, recorded.MaxHeat, recorded.Delay, recorded.Lockout, "recorded pre-pass values");
                Row("AFTER", after, shipped.HeatPerShot, shipped.CoolingRatePerSecond, shipped.MaxHeat, shipped.CoolingDelaySeconds, shipped.OverheatLockoutSeconds, "shipped values after this pass");
                deltas.Add((id, before.SustainedDps, after.SustainedDps, before.DowntimeRatio, after.DowntimeRatio));

                // Frozen for blasters: damage, fire rate, max heat, range, projectile speed.
                Assert.AreEqual(recorded.MaxHeat, shipped.MaxHeat, 0.001f, id + ": max heat is not a lever in this pass");
                Assert.AreEqual(before.PerPelletDamage, after.PerPelletDamage, 0.001f, id + ": raw damage must not change");
                Assert.AreEqual(before.ShotsPerSecond, after.ShotsPerSecond, 0.001f, id + ": fire rate must not change");
                Assert.AreEqual(before.Range, after.Range, 0.001f, id + ": range must not change");
                Assert.AreEqual(before.ProjectileSpeed, after.ProjectileSpeed, 0.001f, id + ": projectile speed must not change");
            }

            csv.AppendLine();
            csv.AppendLine("# SUMMARY");
            csv.AppendLine("WeaponId,SustainedDpsBefore,SustainedDpsAfter,DpsChangePercent,UnableToFireBefore,UnableToFireAfter,DowntimeChangePoints,VsStarterPistolAfter,VsAssaultRifleAfter");
            var pistol = new WeaponProfile(Weapon(RuinRail.Gameplay.Base.StarterKitService.PistolId), Stats(), SkillProfile.Normal);
            var ar = new WeaponProfile(Weapon("weapon_marauder_a2"), Stats(), SkillProfile.Normal);
            foreach (var (id, b, a, db, da) in deltas)
            {
                csv.AppendLine(string.Join(",", new[]
                {
                    id, F(b), F(a), Pct(a, b), F(db * 100f) + "%", F(da * 100f) + "%", F((da - db) * 100f),
                    Pct(a, pistol.SustainedDps), Pct(a, ar.SustainedDps)
                }));

                // Acceptance: modest, on the heat axis, and still constrained.
                var change = a / Mathf.Max(0.01f, b) - 1f;
                Assert.Greater(change, 0.03f, $"{id}: the buff must be noticeable (was {change:P1})");
                Assert.Less(change, 0.12f, $"{id}: the buff must stay mild (was {change:P1})");
                Assert.Greater(da, 0.4f, $"{id}: heat must still cost at least 40% of a sustained cycle (was {da:P1})");
                Assert.Less(a, ar.SustainedDps, $"{id}: a blaster must not reach assault-rifle sustained output");
            }

            File.WriteAllText(Path.Combine(Folder, "blaster_before_after.csv"), csv.ToString());
        }

        /// <summary>Full-heat recovery with the Cooling Module's maximum contribution, to show the affix still matters.</summary>
        private float CoolingWithAffix(WeaponProfile p, float cooling, float delay, float maxHeat)
        {
            var capped = 1f + Mathf.Min(15, _content.StatCaps.GetCapPercent(StatId.BlasterCoolingRate)) / 100f;
            return delay + maxHeat / Mathf.Max(0.0001f, cooling * capped);
        }

        private float HeatWithAffix(WeaponProfile p, float heat) =>
            heat * (1f - Mathf.Min(10, _content.StatCaps.GetCapPercent(StatId.BlasterHeatPerShotReduction)) / 100f);

        private static string Pct(float value, float baseline) =>
            baseline <= 0.0001f ? "-" : (value / baseline - 1f).ToString("+0.0%;-0.0%;0.0%", System.Globalization.CultureInfo.InvariantCulture);

        // ================= PHASE 6 =================

        [Test]
        public void Phase6_FrozenValuesDidNotChange()
        {
            var csv = new StringBuilder();
            csv.AppendLine("Subject,Field,ExpectedValue,ActualValue,Result");
            var knife = (MeleeWeaponDefinition)Weapon(RuinRail.Gameplay.Base.StarterKitService.KnifeId);
            var pistol = (RangedWeaponDefinition)Weapon(RuinRail.Gameplay.Base.StarterKitService.PistolId);

            var actual = new Dictionary<(string, string), string>
            {
                [("weapon_field_knife", "damage")] = $"{knife.DamageMin}-{knife.DamageMax}",
                [("weapon_field_knife", "attack rate")] = F(knife.AttackRate),
                [("weapon_field_knife", "reach")] = F(knife.AttackRange),
                [("weapon_field_knife", "arc")] = F(knife.AttackArcDegrees),
                [("weapon_field_knife", "wind-up / recovery")] = $"{F(knife.WindUpSeconds)} / {F(knife.RecoverySeconds)}",
                [("weapon_field_knife", "knockback / stagger")] = $"{F(knife.Knockback)} / {F(knife.StaggerPower)}",
                [("weapon_p9_ranger", "damage")] = $"{pistol.DamageMin}-{pistol.DamageMax}",
                [("weapon_p9_ranger", "fire rate / magazine / reload")] = $"{F(pistol.FireRate)} / {pistol.MagazineSize} / {F(pistol.ReloadTime)}",
                [("ammo caps", "Light / Medium / Heavy / Shells")] =
                    $"{_content.AmmoBalance.GetStackLimit(AmmoType.Light)} / {_content.AmmoBalance.GetStackLimit(AmmoType.Medium)} / {_content.AmmoBalance.GetStackLimit(AmmoType.Heavy)} / {_content.AmmoBalance.GetStackLimit(AmmoType.Shells)}",
                [("starter kit", "Light ammo granted")] = RuinRail.Gameplay.Base.StarterKitService.LightAmmoCount.ToString()
            };

            foreach (var (subject, field, expected) in Frozen)
            {
                var value = actual[(subject, field)];
                csv.AppendLine(string.Join(",", subject, field, Csv(expected), Csv(value), value == expected ? "UNCHANGED" : "CHANGED"));
                Assert.AreEqual(expected, value, $"{subject} {field} is frozen in this pass");
            }

            // Depth scaling, D1 through D30 and the post-D30 tail, value by value.
            foreach (var depth in new[] { 1, 2, 3, 5, 10, 20, 30, 50, 100 })
            {
                foreach (var (label, value, expected) in new (string, float, float)[]
                {
                    ("enemy HP multiplier", DepthScaling.HealthMultiplier(depth, _content.DepthScaling), ExpectedHealth(depth)),
                    ("enemy damage multiplier", DepthScaling.DamageMultiplier(depth, _content.DepthScaling), ExpectedDamage(depth))
                })
                {
                    csv.AppendLine(string.Join(",", "depth scaling D" + depth, label, F(expected), F(value), Mathf.Abs(value - expected) < 0.0005f ? "UNCHANGED" : "CHANGED"));
                    Assert.AreEqual(expected, value, 0.0005f, $"D{depth} {label} is frozen in this pass");
                }
            }

            // Boss base HP, one row per boss.
            foreach (var boss in _content.Bosses.Where(b => b != null).OrderBy(b => b.Id, StringComparer.Ordinal))
            {
                var expected = ExpectedBossHealth(boss.Id);
                csv.AppendLine(string.Join(",", boss.Id, "base HP", expected.ToString(), boss.BaseHealth.ToString(), boss.BaseHealth == expected ? "UNCHANGED" : "CHANGED"));
                Assert.AreEqual(expected, boss.BaseHealth, boss.Id + ": boss HP is frozen in this pass");
            }

            // Every non-blaster weapon: damage band and cadence.
            foreach (var w in _content.Items.OfType<WeaponDefinition>().Where(w => w is not BlasterWeaponDefinition).OrderBy(w => w.Id, StringComparer.Ordinal))
            {
                var expected = ExpectedWeaponSignature(w.Id);
                var signature = WeaponSignature(w);
                csv.AppendLine(string.Join(",", w.Id, "damage/cadence signature", Csv(expected), Csv(signature), signature == expected ? "UNCHANGED" : "CHANGED"));
                Assert.AreEqual(expected, signature, w.Id + ": non-blaster weapon balance is frozen in this pass");
            }

            // Threat budgets and elite chance.
            foreach (var depth in new[] { 1, 5, 10, 20, 30 })
            {
                var (min, max) = ThreatBudgetTable.SoloBudget(depth);
                var expected = ExpectedBudget(depth);
                var value = $"{F1(min)}-{F1(max)}";
                csv.AppendLine(string.Join(",", "threat budget D" + depth, "solo min-max", Csv(expected), Csv(value), value == expected ? "UNCHANGED" : "CHANGED"));
                Assert.AreEqual(expected, value, $"D{depth} threat budget is frozen in this pass");
            }

            File.WriteAllText(Path.Combine(Folder, "frozen_values_check.csv"), csv.ToString());
        }

        private static float ExpectedHealth(int depth) => depth switch
        {
            1 => 1.00f, 2 => 1.08f, 3 => 1.16f, 5 => 1.32f, 10 => 1.70f, 20 => 2.35f, 30 => 2.90f, 50 => 3.80f, 100 => 5.50f,
            _ => throw new ArgumentOutOfRangeException(nameof(depth))
        };

        private static float ExpectedDamage(int depth) => depth switch
        {
            1 => 1.00f, 2 => 1.0375f, 3 => 1.075f, 5 => 1.15f, 10 => 1.30f, 20 => 1.55f, 30 => 1.75f, 50 => 2.05f, 100 => 2.60f,
            _ => throw new ArgumentOutOfRangeException(nameof(depth))
        };

        private static int ExpectedBossHealth(string id) => id switch
        {
            "boss_aegis_core" => 1050, "boss_scrap_king" => 1000, "boss_subject_omega" => 1250,
            "boss_the_conductor" => 1050, "boss_the_foundry_titan" => 1350, "boss_tunnel_maw" => 1150,
            _ => throw new ArgumentOutOfRangeException(nameof(id), id)
        };

        private static string ExpectedBudget(int depth) => depth switch
        {
            1 => "4-6", 5 => "6-8", 10 => "8-10", 20 => "10-13", 30 => "12-15",
            _ => throw new ArgumentOutOfRangeException(nameof(depth))
        };

        /// <summary>A weapon's balance fingerprint: damage band plus whatever cadence its class uses.</summary>
        private static string WeaponSignature(WeaponDefinition w) => w switch
        {
            RangedWeaponDefinition r => $"{r.DamageMin}-{r.DamageMax} @ {F(r.FireRate)}/s mag {r.MagazineSize} reload {F(r.ReloadTime)}",
            BowWeaponDefinition b => $"{b.FullDrawDamageMin}-{b.FullDrawDamageMax} @ draw {F(b.FullChargeSeconds)}",
            MeleeWeaponDefinition m => $"{m.DamageMin}-{m.DamageMax} @ {F(m.AttackRate)}/s reach {F(m.AttackRange)}",
            _ => "?"
        };

        /// <summary>
        /// The fingerprint every non-blaster weapon had before this pass, recorded from the working tree. Any drift
        /// fails Phase 6 — this is the guard that a "small" pass did not quietly touch the arsenal.
        /// </summary>
        private static string ExpectedWeaponSignature(string id) => Signatures.TryGetValue(id, out var s)
            ? s
            : throw new ArgumentOutOfRangeException(nameof(id), id + " has no recorded pre-pass signature");

        private static readonly Dictionary<string, string> Signatures = new(StringComparer.Ordinal)
        {
            ["weapon_ar_17"] = "8-10 @ 7.5/s mag 30 reload 1.8",
            ["weapon_breacher_12"] = "5-7 @ 1.4/s mag 6 reload 2.2",
            ["weapon_buzzsaw"] = "7-9 @ 11/s mag 36 reload 1.8",
            ["weapon_compound_bow"] = "32-38 @ draw 1",
            ["weapon_crowdbreaker"] = "5-7 @ 1.5/s mag 6 reload 2.2",
            ["weapon_farline"] = "48-55 @ 0.9/s mag 5 reload 2.4",
            ["weapon_field_knife"] = "14-17 @ 3.5/s reach 1.2",
            ["weapon_ghostedge"] = "15-18 @ 3.8/s reach 1.2",
            ["weapon_guard_lance"] = "20-24 @ 2.2/s reach 3",
            ["weapon_hound_br"] = "15-18 @ 4/s mag 16 reload 2.1",
            ["weapon_judicator"] = "20-24 @ 2.8/s mag 10 reload 2",
            ["weapon_kestrel_12"] = "10-12 @ 5.2/s mag 15 reload 1.4",
            ["weapon_longshot_s1"] = "45-52 @ 1/s mag 5 reload 2.4",
            ["weapon_marauder_a2"] = "10-12 @ 6.5/s mag 28 reload 1.9",
            ["weapon_needle_m7"] = "34-40 @ 1.5/s mag 7 reload 2.2",
            ["weapon_p9_ranger"] = "12-14 @ 4/s mag 12 reload 1.2",
            ["weapon_pipe_launcher"] = "65-80 @ 0.5/s mag 1 reload 2.2",
            ["weapon_quickfang"] = "13-15 @ 4.4/s mag 12 reload 1.2",
            ["weapon_railspike"] = "26-30 @ 1.7/s reach 3",
            ["weapon_rattler_9"] = "6-8 @ 10/s mag 32 reload 1.6",
            ["weapon_recurve_bow"] = "25-30 @ draw 0.65",
            ["weapon_ripper_knife"] = "10-13 @ 5/s reach 1",
            ["weapon_scatter_8"] = "4-5 @ 1.7/s mag 8 reload 2.4",
            ["weapon_scrap_spear"] = "24-28 @ 1.8/s reach 2.6",
            ["weapon_sentinel_br"] = "18-22 @ 3/s mag 12 reload 2",
            ["weapon_stormstring"] = "30-36 @ draw 0.8",
            ["weapon_sunbreaker"] = "70-85 @ 0.5/s mag 1 reload 2.3",
            ["weapon_twin_tube"] = "50-60 @ 0.65/s mag 2 reload 2.8",
            ["weapon_vanguard"] = "9-11 @ 8/s mag 30 reload 1.8",
            ["weapon_wasp_45"] = "8-10 @ 8/s mag 26 reload 1.7"
        };

        // ================= PHASE 8 =================

        [Test]
        public void Phase8_WritesTheOwnerPlaytestChecklist()
        {
            var text = @"# RUINRAIL — owner playtest checklist (D1 ammo / blaster fine-tuning)

Two changes shipped. Everything else is frozen. Play one fresh profile for 1–5 and any account for 6–10.

**What changed:** a Supply Chest now drops **26–46 Light rounds** instead of 20–40, and all three blasters recover from
an overheat in **1.9 s** instead of 2.2 s with a **0.5 s** cooling delay instead of 0.6 s. Nothing else moved.

1. **In a fresh D1 run, did mixed P9 + Field Knife play leave you only *slightly* ammo-constrained at the boss?**
   *Measured prediction:* you arrive with about 12 more Light rounds than before — low, not comfortable.
2. **Did you feel rewarded for using both weapons?** Specifically: did switching to the knife on a grunt feel like
   banking ammo for the boss rather than like a chore?
3. **If you intentionally cleared almost everything with the pistol, did ammo become noticeably tighter?**
   *Measured prediction:* yes — pistol-heavy play still ends the depth forced onto the knife.
4. **Did the Field Knife feel unchanged?** It was not touched. If it feels different, something is wrong.
5. **Did the boss feel the same difficulty apart from slightly better ammo availability?** Boss HP was not touched.
6. **Did the blaster feel a little smoother?** The gap after an overheat is 0.3 s shorter.
7. **Did heat still force pauses and management?** *Measured prediction:* yes — heat still costs 50–57 % of a
   sustained firing cycle. If you can now hold the trigger indefinitely, report it as a bug.
8. **Did the blaster avoid feeling obviously stronger than comparable ammo weapons?** *Measured prediction:* the best
   blaster is still slightly below the free starter pistol and well below an assault rifle in sustained output.
9. **Did any ammo pickup pattern feel artificial or overly generous?** The change is a quantity on the existing Supply
   Chest — no new chests, no new sources, no guaranteed pre-boss refill.
10. **Did the run still preserve resource tension?** If you now finish D1 with a comfortable reserve in mixed play, the
    correction went too far and should be reported.
";
            File.WriteAllText(Path.Combine(Folder, "HUMAN_PLAYTEST_CHECKLIST.md"), text);
            Assert.GreaterOrEqual(text.Split('\n').Count(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^\d+\. ")), 10);
        }
    }
}

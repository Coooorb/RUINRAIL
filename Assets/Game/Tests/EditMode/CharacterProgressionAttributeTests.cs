using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.EditorTools.Production;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Progression;
using RuinRail.Gameplay.Stats;
using RuinRail.Persistence;
using RuinRail.UI.Base;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>
    /// The permanent character attributes, end to end on the data side: the authoritative table (player/13), the
    /// purchase transaction and its economy, persistence across save/load, the composition order against equipment, the
    /// Character Station's presentation, and the description/effect gate.
    ///
    /// It also writes the two machine-readable proofs this pass is measured by —
    /// TestResults/CharacterProgressionAttributeProof/attribute_effect_matrix.csv and attribute_rank_sweep.csv.
    /// </summary>
    public class CharacterProgressionAttributeTests
    {
        public const string ProofFolder = "TestResults/CharacterProgressionAttributeProof";

        /// <summary>
        /// player/13_LEVELING_AND_SKILL_POINTS, the approved V1 table. This is the design oracle: the implementation is
        /// checked against it rather than against itself, so a change to SkillRules that contradicts the approved design
        /// fails here.
        /// </summary>
        private static readonly (SkillId Skill, StatId[] Stats, StatModifierKind Kind, int PerRank, int MaxBonus)[] Approved =
        {
            (SkillId.Vitality, new[] { StatId.MaxHealth }, StatModifierKind.Flat, 2, 20),
            (SkillId.Power, new[] { StatId.WeaponDamage }, StatModifierKind.Percent, 1, 10),
            (SkillId.Mobility, new[] { StatId.MovementSpeed }, StatModifierKind.Percent, 1, 10),
            (SkillId.Recovery, new[] { StatId.HealingReceived }, StatModifierKind.Percent, 2, 20),
            (SkillId.Handling, new[] { StatId.ReloadSpeed, StatId.WeaponSwitchSpeed }, StatModifierKind.Percent, 1, 10),
            (SkillId.Resilience, new[] { StatId.KnockbackResistance, StatId.StaggerResistance }, StatModifierKind.Percent, 2, 20)
        };

        /// <summary>
        /// The one stat an attribute feeds that no gameplay system reads: weapon switching is instantaneous in this
        /// build and no repository document defines a base switch duration to scale, so there is nothing for the
        /// modifier to act on. Pinned here so a NEWLY disconnected stat fails this test instead of passing quietly.
        /// Handling's other stat, Reload Speed, is consumed and measured, so the attribute itself is not cosmetic.
        /// </summary>
        private static readonly StatId[] KnownDisconnectedStats = { StatId.WeaponSwitchSpeed };

        private GlobalStatCapsConfig _caps;
        private EconomyConfig _economy;
        private ItemDefinitionRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _caps = AssetDatabase.LoadAssetAtPath<GlobalStatCapsConfig>("Assets/Game/ScriptableObjects/Balance/GlobalStatCapsConfig.asset");
            if (_caps == null) _caps = ScriptableObject.CreateInstance<GlobalStatCapsConfig>();
            _economy = AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset");
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition")
                .Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(catalog);
            Directory.CreateDirectory(ProofFolder);
        }

        private PlayerStats NewStats(SkillAllocation allocation, int baseMaxHealth = 100)
        {
            var stats = new PlayerStats(_caps, baseMaxHealth);
            stats.SetSource(new SkillStatSource(allocation));
            return stats;
        }

        // ---------------- 1. the authoritative attribute table ----------------

        [Test]
        public void EveryAttribute_MatchesTheApprovedDesignTable()
        {
            Assert.AreEqual(6, SkillRules.SkillCount, "player/13: six permanent attributes");
            Assert.AreEqual(6, SkillRules.All.Length);
            Assert.AreEqual(10, SkillRules.MaxRank, "player/13: ten ranks each");
            CollectionAssert.AreEquivalent(Enum.GetValues(typeof(SkillId)).Cast<SkillId>().ToArray(), SkillRules.All);
            CollectionAssert.AreEquivalent(Approved.Select(a => a.Skill).ToArray(), SkillRules.All, "the oracle covers every attribute");

            foreach (var (skill, stats, kind, perRank, maxBonus) in Approved)
            {
                for (var rank = 0; rank <= SkillRules.MaxRank; rank++)
                {
                    var modifiers = SkillRules.ModifiersForRank(skill, rank).ToList();
                    if (rank == 0)
                    {
                        Assert.IsEmpty(modifiers, $"{skill} rank 0 grants nothing");
                        continue;
                    }

                    CollectionAssert.AreEqual(stats, modifiers.Select(m => m.Stat).ToArray(), $"{skill} rank {rank} stats");
                    Assert.IsTrue(modifiers.All(m => m.Kind == kind), $"{skill} rank {rank} modifier kind");
                    Assert.IsTrue(modifiers.All(m => m.Value == perRank * rank), $"{skill} rank {rank} is {perRank} x rank");
                }

                Assert.IsTrue(SkillRules.ModifiersForRank(skill, SkillRules.MaxRank).All(m => m.Value == maxBonus),
                    $"{skill} at rank {SkillRules.MaxRank} is the approved maximum bonus {maxBonus}");
            }
        }

        [Test]
        public void SixtyPoints_MaxEverySingleAttribute_AndNothingExceedsTheCaps()
        {
            Assert.AreEqual(60, LevelCurve.SkillPointsEarned(LevelCurve.TotalXpToMaxLevel));
            Assert.AreEqual(60, SkillRules.SkillCount * SkillRules.MaxRank, "60 points buy exactly every rank of all six");

            var profile = new PlayerProfile { TotalXp = LevelCurve.TotalXpToMaxLevel };
            var progression = new ProgressionService(profile) { IsAtBase = true };
            foreach (var skill in SkillRules.All) Assert.AreEqual(SkillSpendError.None, progression.TrySpend(skill, SkillRules.MaxRank), skill.ToString());
            Assert.AreEqual(0, progression.UnspentSkillPoints);
            Assert.AreEqual(60, progression.SpentSkillPoints);

            var stats = NewStats(profile.Skills);
            foreach (var (_, affected, kind, _, maxBonus) in Approved)
            {
                foreach (var stat in affected)
                {
                    if (kind == StatModifierKind.Flat) continue;
                    var cap = stats.CapFor(stat);
                    Assert.LessOrEqual(maxBonus, cap <= 0 ? int.MaxValue : cap, $"{stat}: the attribute alone can never exceed the global cap");
                    Assert.AreEqual(maxBonus, stats.GetPercent(stat), $"{stat} at max rank");
                }
            }

            Assert.AreEqual(120, stats.MaxHealth, "base 100 + Vitality rank 10 (+20 HP)");
        }

        // ---------------- 2. the purchase transaction and its economy ----------------

        [Test]
        public void Purchase_DeductsExactlyOnce_AndEveryRejectionChangesNothing()
        {
            var profile = new PlayerProfile { TotalXp = LevelCurve.TotalXpForLevel(4) };
            var progression = new ProgressionService(profile);
            Assert.AreEqual(3, progression.UnspentSkillPoints);

            Assert.AreEqual(SkillSpendError.NotAtBase, progression.TrySpend(SkillId.Vitality));
            Assert.AreEqual(0, profile.Skills.Vitality);
            Assert.AreEqual(3, progression.UnspentSkillPoints, "a rejected purchase deducts nothing");

            progression.IsAtBase = true;
            Assert.AreEqual(SkillSpendError.None, progression.TrySpend(SkillId.Vitality));
            Assert.AreEqual(1, profile.Skills.Vitality, "rank increments exactly once");
            Assert.AreEqual(2, progression.UnspentSkillPoints, "exactly one point is deducted");

            // Duplicate input (a double click, a repeated key): each press is its own atomic transaction and the points
            // simply run out; nothing is ever spent twice for one rank or granted twice for one point.
            Assert.AreEqual(SkillSpendError.None, progression.TrySpend(SkillId.Vitality));
            Assert.AreEqual(SkillSpendError.None, progression.TrySpend(SkillId.Vitality));
            Assert.AreEqual(SkillSpendError.NoUnspentPoints, progression.TrySpend(SkillId.Vitality));
            Assert.AreEqual(3, profile.Skills.Vitality);
            Assert.AreEqual(0, progression.UnspentSkillPoints);
            Assert.AreEqual(3, progression.SpentSkillPoints, "spent + unspent == earned, always");
        }

        [Test]
        public void MaxRank_IsRejected_AndNeverDeducts()
        {
            var profile = new PlayerProfile { TotalXp = LevelCurve.TotalXpToMaxLevel };
            var progression = new ProgressionService(profile) { IsAtBase = true };
            Assert.AreEqual(SkillSpendError.None, progression.TrySpend(SkillId.Power, SkillRules.MaxRank));
            var pointsAtCap = progression.UnspentSkillPoints;

            Assert.AreEqual(SkillSpendError.RankAtMax, progression.TrySpend(SkillId.Power));
            Assert.AreEqual(SkillRules.MaxRank, profile.Skills.Power);
            Assert.AreEqual(pointsAtCap, progression.UnspentSkillPoints, "a purchase at the cap costs nothing");
            Assert.AreEqual(SkillRules.MaxRank, NewStats(profile.Skills).GetPercent(StatId.WeaponDamage), "and never overflows past the cap");

            profile.Skills.SetRank(SkillId.Power, 99);
            Assert.AreEqual(SkillRules.MaxRank, profile.Skills.Power, "the allocation clamps at the cap even when written directly");
        }

        [Test]
        public void XpNeverGoesNegative_AndThePointEconomyStaysBalanced()
        {
            var profile = new PlayerProfile();
            var progression = new ProgressionService(profile) { IsAtBase = true };
            Assert.AreEqual(0, progression.AddXp(-500));
            Assert.AreEqual(0, profile.TotalXp);
            Assert.AreEqual(0, progression.UnspentSkillPoints);
            Assert.AreEqual(SkillSpendError.NoUnspentPoints, progression.TrySpend(SkillId.Mobility));

            progression.AddXp(LevelCurve.TotalXpForLevel(11));
            Assert.AreEqual(10, progression.UnspentSkillPoints);
            progression.TrySpend(SkillId.Mobility, 4);
            Assert.AreEqual(10, progression.SpentSkillPoints + progression.UnspentSkillPoints);

            // Respec returns every point at the configured Banked Coin price and never touches level or XP.
            var banked = new CoinWallet(CoinDomain.Banked, () => profile.BankedCoins, v => profile.BankedCoins = v);
            var station = new CharacterStation(progression, banked, _economy) { IsAtBase = true };
            Assert.AreEqual(RespecError.InsufficientFunds, station.Respec());
            Assert.AreEqual(4, profile.Skills.Mobility, "a refused respec refunds nothing");
            banked.Credit(station.RespecPrice, "test");
            Assert.AreEqual(RespecError.None, station.Respec());
            Assert.AreEqual(0, profile.Skills.TotalSpent);
            Assert.AreEqual(10, progression.UnspentSkillPoints);
            Assert.AreEqual(11, progression.Level);
            Assert.AreEqual(0, profile.BankedCoins);
        }

        // ---------------- 3. persistence ----------------

        [Test]
        public void EveryAttributeRank_SurvivesSaveAndReload_AndAppliesAfterwards()
        {
            var store = new MemorySaveStore();
            var saves = new SaveSlotService(store, id => _registry.TryGet(id, out var d) ? d : null);
            var slot = new SaveSlot { Profile = { TotalXp = LevelCurve.TotalXpForLevel(25) } };
            var progression = new ProgressionService(slot.Profile) { IsAtBase = true };

            var expected = new Dictionary<SkillId, int>();
            var rank = 1;
            foreach (var skill in SkillRules.All)
            {
                Assert.AreEqual(SkillSpendError.None, progression.TrySpend(skill, rank), skill.ToString());
                expected[skill] = rank;
                rank++;
            }

            Assert.AreEqual(SaveError.None, saves.Save(slot, new SaveDiagnostics()));
            var result = saves.Load();
            Assert.IsTrue(result.Success, result.Diagnostics?.ToString());
            var loaded = result.Slot;

            foreach (var skill in SkillRules.All)
                Assert.AreEqual(expected[skill], loaded.Profile.Skills.GetRank(skill), $"{skill} survives the round trip");

            Assert.AreEqual(slot.Profile.TotalXp, loaded.Profile.TotalXp);
            var reloadedProgression = new ProgressionService(loaded.Profile);
            Assert.AreEqual(24 - expected.Values.Sum(), reloadedProgression.UnspentSkillPoints, "points reconcile against the reloaded ranks, never reset");

            var stats = NewStats(loaded.Profile.Skills);
            Assert.AreEqual(100 + 2 * expected[SkillId.Vitality], stats.MaxHealth, "the reloaded ranks reach the stat pipeline");
            Assert.AreEqual(expected[SkillId.Power], stats.GetPercent(StatId.WeaponDamage));
        }

        [Test]
        public void ProfileWithRanksButNoLevels_IsRepairedWithoutLosingLegitimateProgression()
        {
            // An older or hand-edited profile whose allocation exceeds what its XP earned is repaired (anti-exploit),
            // while a profile whose allocation is legitimate keeps every rank exactly as stored.
            var corrupt = new PlayerProfile { TotalXp = 0 };
            corrupt.Skills.Vitality = 5;
            new ProgressionService(corrupt);
            Assert.AreEqual(0, corrupt.Skills.TotalSpent);

            var legitimate = new PlayerProfile { TotalXp = LevelCurve.TotalXpForLevel(9) };
            legitimate.Skills.Vitality = 4;
            legitimate.Skills.Handling = 3;
            var progression = new ProgressionService(legitimate);
            Assert.AreEqual(4, legitimate.Skills.Vitality, "existing earned progression is preserved");
            Assert.AreEqual(3, legitimate.Skills.Handling);
            Assert.AreEqual(1, progression.UnspentSkillPoints, "8 earned - 7 spent");
        }

        [Test]
        public void LegacyV1Document_MigratesWithEveryAttributeRankIntact()
        {
            var legacy = new PlayerProfile { Version = 1, TotalXp = LevelCurve.TotalXpForLevel(13), DisplayName = "Runner" };
            legacy.Skills.SetRank(SkillId.Resilience, 6);
            legacy.Skills.SetRank(SkillId.Recovery, 2);
            var document = JsonUtility.ToJson(legacy);

            var diagnostics = new SaveDiagnostics();
            var migrated = SaveMigrationPipeline.Default.Migrate(document, 1, diagnostics);
            Assert.IsNotNull(migrated, diagnostics.ToString());
            var slot = JsonUtility.FromJson<SaveSlot>(migrated);
            Assert.AreEqual(SaveSlot.CurrentVersion, slot.SaveVersion);
            Assert.AreEqual(6, slot.Profile.Skills.Resilience, "a rank stored by an older save is preserved, and now applied");
            Assert.AreEqual(2, slot.Profile.Skills.Recovery);
            Assert.AreEqual(12, NewStats(slot.Profile.Skills).GetPercent(StatId.KnockbackResistance), "and reaches the pipeline after migration");
        }

        // ---------------- 4. composition order against equipment ----------------

        [Test]
        public void Progression_SumsWithEquipment_AndNeitherOverwritesTheOther()
        {
            var allocation = new SkillAllocation();
            allocation.SetRank(SkillId.Vitality, 5);
            allocation.SetRank(SkillId.Mobility, 4);
            var stats = NewStats(allocation);
            Assert.AreEqual(110, stats.MaxHealth);
            Assert.AreEqual(4, stats.GetPercent(StatId.MovementSpeed));

            // Equipment registers afterwards, exactly as the run's LoadoutStatRegistrar does.
            stats.SetSource(new StatModifierSource("equipped:armor", StatModifier.Flat(StatId.MaxHealth, 20)));
            stats.SetSource(new StatModifierSource("equipped:accessory", StatModifier.Percent(StatId.MovementSpeed, 6)));
            Assert.AreEqual(130, stats.MaxHealth, "base 100 + skills 10 + armor 20");
            Assert.AreEqual(10, stats.GetPercent(StatId.MovementSpeed), "skills 4 + accessory 6");
            CollectionAssert.Contains(stats.SourceIds.ToArray(), SkillStatSource.Id, "the progression source is still registered");

            // A temporary buff on top, then every equipment source removed: the permanent ranks are untouched by both.
            stats.SetSource(new StatModifierSource("buff:momentum", StatModifier.Percent(StatId.MovementSpeed, 8)));
            Assert.AreEqual(18, stats.GetPercent(StatId.MovementSpeed));
            stats.RemoveSource("buff:momentum");
            stats.RemoveSource("equipped:armor");
            stats.RemoveSource("equipped:accessory");
            Assert.AreEqual(110, stats.MaxHealth, "removing gear leaves the progression bonus exactly as it was");
            Assert.AreEqual(4, stats.GetPercent(StatId.MovementSpeed));

            // Re-registering the same source id can never double a bonus.
            stats.SetSource(new SkillStatSource(allocation));
            stats.SetSource(new SkillStatSource(allocation));
            Assert.AreEqual(110, stats.MaxHealth, "the skills source is keyed, so it applies once however often it is registered");
        }

        [Test]
        public void GlobalCaps_ClampTheSumOnce_NeverTheAttributeAlone()
        {
            var allocation = new SkillAllocation();
            allocation.SetRank(SkillId.Mobility, SkillRules.MaxRank);
            var stats = NewStats(allocation);
            var cap = stats.CapFor(StatId.MovementSpeed);
            Assert.Greater(cap, 0, "player/16 defines a Movement Speed cap");
            Assert.AreEqual(10, stats.GetPercent(StatId.MovementSpeed));

            stats.SetSource(new StatModifierSource("equipped:gear", StatModifier.Percent(StatId.MovementSpeed, cap)));
            Assert.AreEqual(cap, stats.GetPercent(StatId.MovementSpeed), "the cap applies to the sum, once");
        }

        // ---------------- 5. the Character Station's presentation ----------------

        [Test]
        public void CharacterPanel_ShowsRankCostDescriptionPreviewAndMax()
        {
            var session = OpenSession();
            try
            {
                session.Progression.AddXp(LevelCurve.TotalXpForLevel(3));
                using var hub = new BaseHubViewModel(session);
                var character = hub.Character;

                foreach (var skill in CharacterPanelViewModel.Attributes)
                {
                    Assert.AreEqual(SkillCatalog.DisplayName(skill), character.NameOf(skill));
                    Assert.AreEqual("0 / 10", character.RankTextOf(skill));
                    Assert.AreEqual(1, character.CostOf(skill), "one Skill Point per rank");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(character.DescriptionOf(skill)));
                    Assert.AreEqual(SkillCatalog.EffectText(skill, 1), character.EffectNext(skill), "the preview is the rule's next rank");
                    Assert.IsTrue(character.CanAllocate(skill), "affordable at the Shelter with points in hand");
                }

                Assert.IsTrue(character.Allocate(SkillId.Vitality));
                Assert.AreEqual("1 / 10", character.RankTextOf(SkillId.Vitality));
                Assert.AreEqual("Max HP +2", character.EffectNow(SkillId.Vitality));
                Assert.AreEqual("Max HP +4", character.EffectNext(SkillId.Vitality));
                CollectionAssert.AreEqual(new[] { "Max HP +2->+4" }, character.EffectRows(SkillId.Vitality).ToArray());

                // At the cap: MAX, no preview, purchase disabled, nothing deducted.
                session.Progression.AddXp(LevelCurve.TotalXpToMaxLevel);
                for (var r = character.RankOf(SkillId.Vitality); r < SkillRules.MaxRank; r++) Assert.IsTrue(character.Allocate(SkillId.Vitality));
                Assert.IsTrue(character.IsMaxed(SkillId.Vitality));
                Assert.IsNull(character.EffectNext(SkillId.Vitality));
                Assert.IsFalse(character.CanAllocate(SkillId.Vitality));
                Assert.AreEqual(0, character.CostOf(SkillId.Vitality));
                CollectionAssert.AreEqual(new[] { "Max HP +20 MAX" }, character.EffectRows(SkillId.Vitality).ToArray());
                var pointsAtCap = character.UnspentPoints;
                Assert.IsFalse(character.Allocate(SkillId.Vitality));
                Assert.AreEqual(pointsAtCap, character.UnspentPoints, "a refused purchase deducts nothing");
                Assert.AreEqual(SkillRules.MaxRank, character.RankOf(SkillId.Vitality));

                var rows = StationPresentation.For(BaseStation.Character, hub, null).Rows;
                Assert.IsTrue(rows.Any(r => r.Key == "RANK COST" && r.Value == SkillCatalog.PointCostText), "the purchase price is on screen");
                foreach (var skill in CharacterPanelViewModel.Attributes)
                {
                    var name = SkillCatalog.DisplayName(skill);
                    var row = rows.FirstOrDefault(r => r.Key == name);
                    Assert.AreEqual(name, row.Key, $"{skill} has a rank row");
                    StringAssert.StartsWith(character.RankTextOf(skill), row.Value);
                    Assert.IsTrue(rows.Any(r => r.IsText && r.Key == SkillCatalog.Description(skill)), $"{skill} shows its description");
                    foreach (var effect in character.EffectRows(skill))
                        Assert.IsTrue(rows.Any(r => r.IsText && r.Key == effect), $"{skill} shows the effect line '{effect}'");
                }

                StringAssert.EndsWith(SkillCatalog.MaxedText, rows.First(r => r.Key == SkillCatalog.DisplayName(SkillId.Vitality)).Value);
            }
            finally
            {
                session.Dispose();
            }
        }

        [Test]
        public void CharacterControls_AreOfferedOnlyWhenThePurchaseWouldSucceed()
        {
            var session = OpenSession();
            try
            {
                using var hub = new BaseHubViewModel(session);
                var list = RuinRail.UI.Navigation.ScreenNavigation.Character(hub.Character);
                Assert.AreEqual(SkillRules.SkillCount + 1, list.Items.Count, "six attributes plus respec");
                Assert.IsTrue(list.Items.All(i => !i.IsEnabled), "a fresh profile has no Skill Point, so nothing is offered");

                session.Progression.AddXp(LevelCurve.TotalXpForLevel(2));
                Assert.IsTrue(list.Items.Take(SkillRules.SkillCount).All(i => i.IsEnabled), "one point enables every attribute");
                Assert.IsFalse(list.Find("character.respec").IsEnabled, "nothing to refund yet");

                var vitality = list.Find("character.allocate." + SkillId.Vitality);
                Assert.IsTrue(vitality.TryActivate());
                Assert.AreEqual(1, hub.Character.RankOf(SkillId.Vitality));
                Assert.IsTrue(list.Items.Take(SkillRules.SkillCount).All(i => !i.IsEnabled), "the point is spent; nothing more is offered");
                Assert.IsFalse(vitality.TryActivate(), "a disabled control cannot spend");
                Assert.AreEqual(1, hub.Character.RankOf(SkillId.Vitality), "and the rank did not move");
            }
            finally
            {
                session.Dispose();
            }
        }

        // ---------------- 6. the description / effect gate ----------------

        [Test]
        public void AttributeDescriptionValidator_PassesAndPinsTheKnownDisconnectedStats()
        {
            var report = AttributeDescriptionValidator.WriteReport();
            Assert.AreEqual(SkillRules.SkillCount, report.Count, "every attribute is audited");
            Assert.IsTrue(report.Pass, report.ToMarkdown());
            CollectionAssert.AreEquivalent(KnownDisconnectedStats, report.Disconnected.ToArray(),
                "a stat fed by an attribute that no gameplay system reads must be a known, documented gap");
        }

        // ---------------- 7. the machine-readable proofs ----------------

        [Test]
        public void WritesTheAttributeEffectMatrix()
        {
            // current_rank is the rank a newly created profile holds; the sweep CSV covers every other legal rank.
            var profile = new PlayerProfile();
            var sb = new StringBuilder();
            sb.AppendLine("attribute_id,display_name,current_rank,max_rank,cost_rule,effect_per_rank,effect_source,derived_stats,consumers,stacking,persistence_field,runtime_path");

            foreach (var skill in SkillRules.All)
            {
                var unit = SkillCatalog.ModifiersAt(skill, 1);
                var kind = unit[0].Kind == StatModifierKind.Percent ? "percent points" : "flat units";
                var effect = string.Join(" + ", unit.Select(m => $"{StatLabels.Of(m.Stat)} {StatLabels.Format(m)} per rank"));
                sb.AppendLine(string.Join(",", new[]
                {
                    skill.ToString(),
                    SkillCatalog.DisplayName(skill),
                    profile.Skills.GetRank(skill).ToString(CultureInfo.InvariantCulture),
                    SkillRules.MaxRank.ToString(CultureInfo.InvariantCulture),
                    $"{SkillCatalog.PointCostPerRank} Skill Point per rank",
                    effect,
                    "SkillRules.ModifiersForRank (player/13_LEVELING_AND_SKILL_POINTS)",
                    string.Join(" + ", unit.Select(m => m.Stat.ToString())),
                    string.Join(" + ", unit.Select(m => ConsumerOf(m.Stat))),
                    $"additive in {kind}; PlayerStats sums every source then clamps once at the player/16 cap",
                    $"PlayerProfile.Skills.{skill}",
                    "PlayerProfile.Skills -> SkillStatSource(\"skills\") -> PlayerStatsBinder.ApplyProgression -> PlayerStats -> consumer"
                }.Select(Csv)));
            }

            var path = Path.Combine(ProofFolder, "attribute_effect_matrix.csv");
            File.WriteAllText(path, sb.ToString());
            Assert.AreEqual(SkillRules.SkillCount + 1, File.ReadAllLines(path).Count(l => l.Length > 0), "one header + one row per attribute");
        }

        [Test]
        public void WritesTheFullRankSweep_EveryLegalRankOfEveryAttribute()
        {
            var sb = new StringBuilder();
            sb.AppendLine("attribute_id,display_name,rank,max_rank,cost_if_next,expected_effect_value,actual_effect_value,derived_stat,result");
            var rows = 0;
            var failures = new List<string>();

            foreach (var skill in SkillRules.All)
            {
                var unit = SkillCatalog.ModifiersAt(skill, 1);
                var previous = new Dictionary<StatId, int>();
                for (var rank = 0; rank <= SkillRules.MaxRank; rank++)
                {
                    var allocation = new SkillAllocation();
                    allocation.SetRank(skill, rank);
                    var stats = NewStats(allocation);

                    foreach (var reference in unit)
                    {
                        var stat = reference.Stat;
                        var perRank = reference.Value;
                        var expected = perRank * rank;
                        var actual = reference.Kind == StatModifierKind.Percent ? stats.GetPercent(stat) : stats.GetFlat(stat);
                        var pass = expected == actual;
                        if (!pass) failures.Add($"{skill} rank {rank} {stat}: expected {expected}, got {actual}");

                        // Monotonic in the direction the definition requires: every rank is worth strictly more than
                        // the one below it, and no rank is skipped.
                        if (previous.TryGetValue(stat, out var before))
                        {
                            if (actual - before != perRank) failures.Add($"{skill} rank {rank} {stat}: step {actual - before}, expected {perRank}");
                        }

                        previous[stat] = actual;
                        rows++;
                        sb.AppendLine(string.Join(",", new[]
                        {
                            skill.ToString(), SkillCatalog.DisplayName(skill), rank.ToString(CultureInfo.InvariantCulture),
                            SkillRules.MaxRank.ToString(CultureInfo.InvariantCulture),
                            rank >= SkillRules.MaxRank ? "MAX" : SkillCatalog.PointCostPerRank + " point",
                            Value(expected, reference.Kind), Value(actual, reference.Kind), stat.ToString(), pass ? "PASS" : "FAIL"
                        }.Select(Csv)));
                    }

                    if (rank == SkillRules.MaxRank)
                    {
                        var overflow = new SkillAllocation();
                        overflow.SetRank(skill, SkillRules.MaxRank + 5);
                        Assert.AreEqual(SkillRules.MaxRank, overflow.GetRank(skill), $"{skill} cannot overflow past the cap");
                    }
                }
            }

            var path = Path.Combine(ProofFolder, "attribute_rank_sweep.csv");
            File.WriteAllText(path, sb.ToString());
            Assert.AreEqual(SkillRules.All.Sum(s => SkillCatalog.AffectedStats(s).Count) * (SkillRules.MaxRank + 1), rows, "every legal rank of every stat is swept");
            CollectionAssert.IsEmpty(failures, string.Join("\n", failures));
        }

        // ---------------- helpers ----------------

        /// <summary>A real Shelter session over an in-memory slot, opened the way the Main Menu opens it.</summary>
        private BaseSession OpenSession()
        {
            var saves = new SaveSlotService(new MemorySaveStore(), id => _registry.TryGet(id, out var d) ? d : null);
            var configs = new BaseConfigs
            {
                Registry = _registry,
                AmmoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset"),
                Economy = _economy,
                Trader = AssetDatabase.LoadAssetAtPath<TraderConfig>("Assets/Game/ScriptableObjects/Base/TraderConfig.asset"),
                Workshop = AssetDatabase.LoadAssetAtPath<WorkshopConfig>("Assets/Game/ScriptableObjects/Base/WorkshopConfig.asset")
            };
            return BaseSession.Open(new SaveSlot(), saves, configs);
        }

        private static string ConsumerOf(StatId stat) => stat switch
        {
            StatId.MaxHealth => "PlayerStatsBinder -> HealthComponent.ResizeMaxHealth",
            StatId.MovementSpeed => "PlayerMovement.CurrentMoveSpeed",
            StatId.WeaponDamage => "RangedWeapon / BowWeapon / BlasterWeapon / MeleeWeapon damage roll",
            StatId.ReloadSpeed => "RangedWeapon.CurrentReloadTime",
            StatId.HealingReceived => "ConsumableEffects heal",
            StatId.KnockbackResistance => "PlayerImpactReceiver knockback distance",
            StatId.StaggerResistance => "PlayerImpactReceiver stagger meter",
            StatId.WeaponSwitchSpeed => "NONE (weapon switching is instantaneous; no base duration is defined)",
            _ => "NONE"
        };

        private static string Value(int value, StatModifierKind kind) => kind == StatModifierKind.Percent ? $"+{value}%" : $"+{value}";

        private static string Csv(string field) => field != null && (field.Contains(',') || field.Contains('"'))
            ? "\"" + field.Replace("\"", "\"\"") + "\""
            : field ?? string.Empty;
    }
}

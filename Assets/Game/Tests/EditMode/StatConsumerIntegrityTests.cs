using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using RuinRail.Core.Rng;
using RuinRail.EditorTools.Production;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Stats;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>
    /// The no-dead-content gate and the arithmetic behind every stat it protects.
    ///
    /// Three things are proven here. First, that the shipped content passes
    /// <see cref="StatConsumerIntegrityValidator"/>. Second — the part that matters for the long term — that the
    /// validator actually fails when a consumer, a composition seam, a stat-math caller or a provider argument is
    /// disconnected: a gate nobody has watched fail is not a gate. Third, that each newly connected stat computes the
    /// value it claims to, that the global caps are applied exactly once, and that adding and removing a source
    /// returns the baseline exactly.
    ///
    /// It also writes TestResults/StatConsumerIntegrity/stat_consumer_matrix.csv, whose machine-checkable columns are
    /// asserted rather than narrated: a stat marked CONSUME must have a reader, a stat marked DEFER_EXPLICITLY must
    /// appear in the validator's documented deferral list, and neither may be both.
    /// </summary>
    public class StatConsumerIntegrityTests
    {
        public const string ProofFolder = "TestResults/StatConsumerIntegrity";

        /// <summary>
        /// The decision record from Phase 2, one row per stat a source can grant. SourceType/SourceDefinition name the
        /// most player-visible granter; RuntimeConsumer names the file that reads the stat back out of the pipeline.
        /// The last three columns are what this test enforces against the real tree.
        /// </summary>
        private static readonly MatrixRow[] Matrix =
        {
            new("MaxHealth", "Armor intrinsic + armor affix", "armor_heavy_plate / affix_max_health", "Max Health", "+35 flat, +6..10%",
                "PlayerStats.MaxHealth -> HealthComponent.SetMaxHealth", "PlayerStats.cs", true, "CONSUME",
                "unchanged; already live", "PlayMode: run starts at the effective maximum"),
            new("MovementSpeed", "Accessory intrinsic + armor/accessory affix", "accessory_runners_watch / affix_movement_speed", "Movement Speed", "+5%, +5..9%",
                "PlayerMovement.CurrentMoveSpeed", "PlayerMovement.cs", true, "CONSUME",
                "unchanged; already live", "PlayMode: measured travel over fixed steps"),
            new("GeneralDamageReduction", "Armor intrinsic + armor affix", "armor_riot_armor / affix_damage_reduction", "Damage Reduction", "+7%, +2..5%",
                "PlayerStats.ApplyDamageReduction", "PlayerStats.cs", true, "CONSUME", "unchanged; already live", "EditMode: damage arithmetic"),
            new("ExplosionDamageReduction", "Armor intrinsic", "armor_blast_suit", "Explosion Damage Reduction", "+30%",
                "PlayerStats.ApplyDamageReduction (explosion branch)", "PlayerStats.cs", true, "CONSUME", "unchanged; already live", "EditMode: damage arithmetic"),
            new("WeaponDamage", "Armor intrinsic + every weapon pool", "armor_combat_harness / affix_damage", "Weapon Damage", "+4%, +6..10%",
                "RangedWeapon / BlasterWeapon / BowWeapon / MeleeWeapon damage roll", "RangedWeapon.cs", true, "CONSUME",
                "reader unchanged, but the affix now reaches it", "PlayMode: applied damage on a target"),
            new("FireRate", "Ranged + blaster weapon affix", "affix_fire_rate", "Fire Rate", "+5..9%",
                "WeaponStatMath.FireRate -> RangedWeapon.CurrentFireInterval / BlasterWeapon.CurrentFireInterval", "WeaponStatMath.cs", false, "CONSUME",
                "shot cadence", "PlayMode: shots counted over a fixed time"),
            new("ReloadSpeed", "Accessory intrinsic + ranged/accessory affix", "accessory_loaders_glove / affix_reload_speed", "Reload Speed", "+10%, +8..14%",
                "RangedWeapon.CurrentReloadTime", "RangedWeapon.cs", true, "CONSUME", "unchanged; already live", "PlayMode: measured reload duration"),
            new("MagazineSize", "Ranged weapon affix", "affix_magazine_size", "Magazine Size", "+10..20%",
                "WeaponStatMath.MagazineSize -> RangedWeapon.CurrentMagazineSize", "WeaponStatMath.cs", false, "CONSUME",
                "effective magazine capacity, reload target and network clamp", "PlayMode: rounds loaded, reserve conserved"),
            new("WeaponSwitchSpeed", "Accessory intrinsic (excluded) + Handling attribute", "accessory_quickdraw_holster / skill_handling", "Weapon Switch Speed", "+15%, +1%/rank",
                "none — weapon switching is instantaneous", "n/a", false, "DEFER_EXPLICITLY",
                "still nothing; the Holster is excluded from V1 acquisition", "EditMode: validator deferral list + acquisition exclusion"),
            new("DashCooldownReduction", "Accessory intrinsic + armor/accessory affix", "accessory_dash_capacitor / affix_dash_cooldown", "Dash Cooldown Reduction", "+10%, +6..10%",
                "PlayerDash cooldown", "PlayerDash.cs", true, "CONSUME", "unchanged; already live", "EditMode: dash cooldown arithmetic"),
            new("DashDistance", "Legendary/temporary sources", "special_discharge", "Dash Distance", "situational",
                "PlayerDash distance", "PlayerDash.cs", true, "CONSUME", "unchanged; already live", "EditMode: dash distance arithmetic"),
            new("MeleeAttackSpeed", "Accessory intrinsic + melee weapon affix", "accessory_combat_bracelet / affix_melee_attack_speed", "Melee Attack Speed", "+8%, +5..9%",
                "WeaponStatMath.MeleeAttackRate / MeleePhaseSeconds -> MeleeWeapon", "WeaponStatMath.cs", false, "CONSUME",
                "swing cadence including wind-up and recovery", "PlayMode: swings counted over a fixed time"),
            new("ProjectileRange", "Accessory intrinsic + ranged/blaster/bow affix", "accessory_field_scope / affix_range", "Projectile Range", "+10%, +8..15%",
                "WeaponStatMath.ProjectileRange -> weapon CurrentRange -> ProjectileSpawnData.MaxDistance", "WeaponStatMath.cs", false, "CONSUME",
                "real travel limit and hitscan resolve distance", "PlayMode: measured maximum reach"),
            new("ProjectileSpeed", "Accessory intrinsic + weapon/accessory affix", "accessory_rangefinder / affix_projectile_speed", "Projectile Speed", "+12%, +8..15%",
                "WeaponStatMath.ProjectileSpeed -> weapon CurrentProjectileSpeed", "WeaponStatMath.cs", false, "CONSUME",
                "real projectile motion", "PlayMode: distance travelled per second"),
            new("HealingReceived", "Armor/accessory intrinsic + affix", "accessory_trauma_pendant / affix_healing_received", "Healing Received", "+15%, +8..14%",
                "ConsumableEffects heal", "ConsumableEffects.cs", true, "CONSUME", "unchanged; already live", "PlayMode: HP restored by a consumable"),
            new("BlasterCoolingRate", "Accessory intrinsic + blaster affix path", "accessory_cooling_module", "Blaster Cooling Rate", "+15%",
                "WeaponStatMath.BlasterCoolingRate -> BlasterHeatState.EffectiveCoolingRatePerSecond", "WeaponStatMath.cs", false, "CONSUME",
                "heat shed per second", "PlayMode: heat recovered over a fixed time"),
            new("BlasterHeatPerShotReduction", "Accessory intrinsic", "accessory_heat_sink", "Blaster Heat per Shot", "-10%",
                "WeaponStatMath.BlasterHeatPerShot -> BlasterWeapon.CurrentHeatPerShot", "WeaponStatMath.cs", false, "CONSUME",
                "heat added per shot and shots to overheat", "PlayMode: heat after N shots"),
            new("BowChargeSpeed", "Accessory intrinsic + bow affix", "accessory_archers_ring / affix_bow_charge_speed", "Bow Charge Speed", "+12%, +8..15%",
                "WeaponStatMath.BowFullChargeSeconds -> BowWeapon.CurrentFullChargeSeconds", "WeaponStatMath.cs", false, "CONSUME",
                "time to a full draw", "PlayMode: measured charge duration"),
            new("Knockback", "Accessory intrinsic + heavy weapon affix", "accessory_impact_module / affix_knockback", "Knockback", "+15%, +10..20%",
                "WeaponStatMath.Knockback -> ImpactDispatcher -> ImpactReceiver displacement", "WeaponStatMath.cs", false, "CONSUME",
                "shotguns, rockets and spears now author a non-zero base for it to scale", "PlayMode: measured enemy displacement"),
            new("StaggerPower", "Accessory intrinsic + melee/impact affix", "accessory_shock_charm / affix_stagger_power", "Stagger Power", "+15%, +8..16%",
                "WeaponStatMath.StaggerPower -> ImpactDispatcher -> StaggerMeter", "WeaponStatMath.cs", false, "CONSUME",
                "impact classes now author a non-zero base for it to scale", "PlayMode: stagger applied / resisted"),
            new("KnockbackResistance", "Armor affix + Resilience attribute", "affix_knockback_resistance / skill_resilience", "Knockback Resistance", "+8..15%, +2%/rank",
                "PlayerImpactReceiver displacement", "PlayerImpactReceiver.cs", true, "CONSUME", "unchanged; already live", "PlayMode: player displacement"),
            new("StaggerResistance", "Armor intrinsic/affix + Resilience attribute", "armor_reinforced_exo_rig / skill_resilience", "Stagger Resistance", "+25%, +2%/rank",
                "PlayerImpactReceiver stagger meter", "PlayerImpactReceiver.cs", true, "CONSUME", "unchanged; already live", "PlayMode: player stagger"),
            new("AmmoStackCapacity", "Accessory intrinsic", "accessory_ammo_pouch", "Ammo Stack Capacity", "+25%",
                "PlayerInventory.MaxStackFor -> ItemSlotContainer capacity rules", "PlayerRigComposer.cs", false, "CONSUME",
                "every ammo stack limit, through the one existing capacity rule", "PlayMode: ammo actually carried"),
            new("PickupAttractionRadius", "Accessory intrinsic", "accessory_magnetic_coil", "Pickup Attraction Radius", "+3 flat",
                "PickupAttractor radius", "PickupAttractor.cs", true, "CONSUME", "unchanged; already live", "EditMode: radius arithmetic"),
            new("WeaponSpreadReduction", "Accessory intrinsic", "accessory_stabilizer", "Weapon Spread Reduction", "+15%",
                "WeaponStatMath.SpreadDegrees -> RangedWeapon.FiringPattern", "WeaponStatMath.cs", false, "CONSUME",
                "the authored 30 degree shotgun cone only; no baseline spread was invented", "EditMode: pattern rebuild; see spread_decision.md"),
        };

        private sealed class MatrixRow
        {
            public MatrixRow(string stat, string sourceType, string sourceDefinition, string label, string granted, string consumer,
                string codePath, bool effectiveBefore, string decision, string effectiveAfter, string verification)
            {
                Stat = (StatId)Enum.Parse(typeof(StatId), stat);
                SourceType = sourceType;
                SourceDefinition = sourceDefinition;
                Label = label;
                Granted = granted;
                Consumer = consumer;
                CodePath = codePath;
                EffectiveBefore = effectiveBefore;
                Decision = decision;
                EffectiveAfter = effectiveAfter;
                Verification = verification;
            }

            public StatId Stat { get; }
            public string SourceType { get; }
            public string SourceDefinition { get; }
            public string Label { get; }
            public string Granted { get; }
            public string Consumer { get; }
            public string CodePath { get; }
            public bool EffectiveBefore { get; }
            public string Decision { get; }
            public string EffectiveAfter { get; }
            public string Verification { get; }
        }

        private readonly List<UnityEngine.Object> _created = new();
        private GlobalStatCapsConfig _caps;

        [SetUp]
        public void SetUp()
        {
            _caps = AssetDatabase.LoadAssetAtPath<GlobalStatCapsConfig>("Assets/Game/ScriptableObjects/Balance/GlobalStatCapsConfig.asset");
            Assert.IsNotNull(_caps, "the approved global caps asset");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            _created.Clear();
        }

        private PlayerStats Stats(params StatModifier[] modifiers)
        {
            var stats = new PlayerStats(_caps, 100);
            if (modifiers.Length > 0) stats.SetSource(new StatModifierSource("test", modifiers));
            return stats;
        }

        // ================= the gate itself =================

        [Test]
        public void Validator_PassesOnTheShippedContent()
        {
            var report = StatConsumerIntegrityValidator.WriteReport();
            Assert.IsTrue(report.Pass, report.ToMarkdown());
            CollectionAssert.IsEmpty(report.UnconsumedGrantedStats, "every granted stat has a runtime consumer or a documented deferral");
            CollectionAssert.IsEmpty(report.UnwiredSeams, "every composition seam has a gameplay caller");
            CollectionAssert.IsEmpty(report.UncalledFacadeMethods, "every stat-math entry point is called by a weapon");
        }

        // ---- Phase 8: the gate must fail when the bug it exists for is reintroduced ----

        [Test]
        public void Validator_FailsWhenAStatLosesItsOnlyReader()
        {
            var broken = Sources();
            broken["WeaponStatMath.cs"] = broken["WeaponStatMath.cs"].Replace("GetMultiplier(StatId.FireRate)", "GetMultiplier(StatId.WeaponDamage)");
            var report = StatConsumerIntegrityValidator.Validate(Equipment(), Pools(), broken);
            Assert.IsFalse(report.Pass, "a stat with no reader must fail the gate");
            CollectionAssert.Contains(report.UnconsumedGrantedStats, StatId.FireRate);
        }

        [Test]
        public void Validator_FailsWhenACompositionSeamIsOnlyCalledByTests()
        {
            // The historical Ammo Pouch defect, reproduced exactly: the provider method still exists and is still
            // correct, and the only thing that changed is that no composition root calls it any more.
            var broken = Sources();
            broken["PlayerRigComposer.cs"] = broken["PlayerRigComposer.cs"].Replace("Inventory.SetAmmoCapacityBonusProvider(", "// Inventory.RemovedOnPurpose(");
            broken["BaseSession.cs"] = broken["BaseSession.cs"].Replace("Loadout.SetAmmoCapacityBonusProvider(", "// Loadout.RemovedOnPurpose(");
            // The co-op host's mirror of a joined member is the third composition root that wires the seam.
            broken["ExpeditionScene.Coop.cs"] = broken["ExpeditionScene.Coop.cs"].Replace("Inventory.SetAmmoCapacityBonusProvider(", "// Inventory.RemovedOnPurpose(");
            var report = StatConsumerIntegrityValidator.Validate(Equipment(), Pools(), broken);
            Assert.IsFalse(report.Pass, "an unwired provider seam must fail the gate");
            CollectionAssert.Contains(report.UnwiredSeams, "SetAmmoCapacityBonusProvider");
        }

        [Test]
        public void Validator_FailsWhenAResolverIsHandedInAsNull()
        {
            // The defect this pass found in the run composition root: every affix resolved to null, so every rolled
            // affix contributed nothing while remaining rolled, priced and displayed.
            var broken = Sources();
            broken["PlayerRigComposer.cs"] = broken["PlayerRigComposer.cs"]
                .Replace("new LoadoutStatRegistrar(Inventory, stats, Resolve, ResolveAffix)", "new LoadoutStatRegistrar(Inventory, stats, Resolve, _ => null)");
            var report = StatConsumerIntegrityValidator.Validate(Equipment(), Pools(), broken);
            Assert.IsFalse(report.Pass, "a null resolver argument must fail the gate");
            Assert.IsTrue(report.Lines.Any(l => l.Kind == "ProviderArgument" && !l.Pass && l.Detail.Contains("PlayerRigComposer.cs")),
                "the failing line must name the file that passed the null");
        }

        [Test]
        public void Validator_FailsWhenNothingCallsTheStatMathFacade()
        {
            var broken = Sources();
            foreach (var file in new[] { "RangedWeapon.cs", "BlasterWeapon.cs" })
                broken[file] = broken[file].Replace("WeaponStatMath.FireInterval(", "Authored(").Replace("WeaponStatMath.FireRate(", "Authored(");
            var report = StatConsumerIntegrityValidator.Validate(Equipment(), Pools(), broken);
            Assert.IsFalse(report.Pass, "a stat-math entry point nothing calls must fail the gate");
            CollectionAssert.Contains(report.UncalledFacadeMethods, "WeaponStatMath.FireRate");
        }

        [Test]
        public void Validator_FailsWhenAnAcquirableItemGrantsAStatItCannotUse()
        {
            // A Knockback affix on a weapon that authors 0 knockback multiplies zero: the reader exists, the stat is
            // live, and the affix is still a decorative tooltip line. Only the acquisition check can see this.
            var pool = ScriptableObject.CreateInstance<AffixPool>();
            _created.Add(pool);
            var knockback = AssetDatabase.LoadAssetAtPath<AffixDefinition>("Assets/Game/ScriptableObjects/Affixes/Affix_Knockback.asset");
            Assert.IsNotNull(knockback);
            Set(pool, "_id", "pool_test");
            Set(pool, "_affixes", new[] { knockback, knockback, knockback });

            var weapon = ScriptableObject.CreateInstance<RangedWeaponDefinition>();
            _created.Add(weapon);
            Set(weapon, "_id", "weapon_test_pistol");
            Set(weapon, "_category", ItemCategory.Weapon);
            Set(weapon, "_magazineSize", 10);
            Set(weapon, "_knockback", 0f);
            Set(weapon, "_affixPool", pool);

            var report = StatConsumerIntegrityValidator.Validate(new EquipmentItemDefinition[] { weapon }, new[] { pool }, Sources());
            Assert.IsFalse(report.Pass, "an acquirable item whose affix can never do anything must fail the gate");
            Assert.IsTrue(report.Lines.Any(l => l.Subject == "weapon_test_pistol" && !l.Pass && l.Problems.Any(p => p.Contains("always 0 on this definition"))));
        }

        [Test]
        public void ExcludedContentIsAllowedToKeepADeferredStat()
        {
            var holster = Equipment().Single(e => e.Id == "accessory_quickdraw_holster");
            Assert.IsFalse(holster.IsAcquirableInV1, "the Quickdraw Holster is the one item whose only effect is deferred");
            Assert.IsTrue(holster.BaseModifiers().Any(m => m.Stat == StatId.WeaponSwitchSpeed));
            Assert.IsTrue(StatConsumerIntegrityValidator.DeferredStats.ContainsKey(StatId.WeaponSwitchSpeed));
            Assert.IsTrue(Equipment().Where(e => e.IsAcquirableInV1).All(e => e.BaseModifiers().All(m => m.Stat != StatId.WeaponSwitchSpeed)),
                "no acquirable item advertises the deferred stat");
        }

        // ================= Phase 10: the arithmetic of each connected stat =================

        [Test]
        public void NullProvider_AlwaysYieldsTheAuthoredValue()
        {
            Assert.AreEqual(5f, WeaponStatMath.FireRate(5f, null), 1e-4f);
            Assert.AreEqual(12, WeaponStatMath.MagazineSize(12, null));
            Assert.AreEqual(18f, WeaponStatMath.ProjectileSpeed(18f, null), 1e-4f);
            Assert.AreEqual(9f, WeaponStatMath.ProjectileRange(9f, null), 1e-4f);
            Assert.AreEqual(3.5f, WeaponStatMath.MeleeAttackRate(3.5f, null), 1e-4f);
            Assert.AreEqual(35f, WeaponStatMath.BlasterCoolingRate(35f, null), 1e-4f);
            Assert.AreEqual(7f, WeaponStatMath.BlasterHeatPerShot(7f, null), 1e-4f);
            Assert.AreEqual(0.8f, WeaponStatMath.BowFullChargeSeconds(0.8f, null), 1e-4f);
            Assert.AreEqual(30f, WeaponStatMath.SpreadDegrees(30f, null), 1e-4f);
            Assert.AreEqual(6f, WeaponStatMath.Knockback(6f, null), 1e-4f);
            Assert.AreEqual(3f, WeaponStatMath.StaggerPower(3f, null), 1e-4f);
        }

        [Test]
        public void FireRate_RaisesCadenceAndShortensTheInterval()
        {
            var stats = Stats(StatModifier.Percent(StatId.FireRate, 20));
            Assert.AreEqual(6f, WeaponStatMath.FireRate(5f, stats), 1e-4f);
            Assert.AreEqual(1f / 6f, WeaponStatMath.FireInterval(5f, stats), 1e-5f);
            Assert.AreEqual(1f / 5f, WeaponStatMath.FireInterval(5f, Stats()), 1e-5f, "no source means the authored cadence");
        }

        [Test]
        public void MagazineSize_RoundsDeterministicallyAndNeverBelowOne()
        {
            Assert.AreEqual(12, WeaponStatMath.MagazineSize(10, Stats(StatModifier.Percent(StatId.MagazineSize, 20))));
            Assert.AreEqual(8, WeaponStatMath.MagazineSize(7, Stats(StatModifier.Percent(StatId.MagazineSize, 10))), "7 x 1.10 = 7.7 rounds up");
            Assert.AreEqual(7, WeaponStatMath.MagazineSize(7, Stats(StatModifier.Percent(StatId.MagazineSize, 5))), "7 x 1.05 = 7.35 rounds down");
            Assert.AreEqual(1, WeaponStatMath.MagazineSize(1, Stats(StatModifier.Percent(StatId.MagazineSize, -90))), "a magazine never drops below one round");
            Assert.AreEqual(0, WeaponStatMath.MagazineSize(0, Stats(StatModifier.Percent(StatId.MagazineSize, 50))), "a magazineless weapon stays magazineless");
        }

        [Test]
        public void ProjectileSpeedAndRange_ScaleTheAuthoredValue()
        {
            Assert.AreEqual(22f, WeaponStatMath.ProjectileSpeed(20f, Stats(StatModifier.Percent(StatId.ProjectileSpeed, 10))), 1e-4f);
            Assert.AreEqual(13.2f, WeaponStatMath.ProjectileRange(12f, Stats(StatModifier.Percent(StatId.ProjectileRange, 10))), 1e-4f);
        }

        [Test]
        public void MeleeAttackSpeed_ShortensEveryPhaseOfTheSwing()
        {
            var stats = Stats(StatModifier.Percent(StatId.MeleeAttackSpeed, 25));
            Assert.AreEqual(4.375f, WeaponStatMath.MeleeAttackRate(3.5f, stats), 1e-4f);
            Assert.AreEqual(0.08f / 1.25f, WeaponStatMath.MeleePhaseSeconds(0.08f, stats), 1e-5f, "wind-up shortens with the cadence");
            Assert.AreEqual(0.12f / 1.25f, WeaponStatMath.MeleePhaseSeconds(0.12f, stats), 1e-5f, "so does recovery");
        }

        [Test]
        public void BlasterHeat_CoolsFasterAndHeatsLess()
        {
            Assert.AreEqual(40.25f, WeaponStatMath.BlasterCoolingRate(35f, Stats(StatModifier.Percent(StatId.BlasterCoolingRate, 15))), 1e-4f);
            Assert.AreEqual(6.3f, WeaponStatMath.BlasterHeatPerShot(7f, Stats(StatModifier.Percent(StatId.BlasterHeatPerShotReduction, 10))), 1e-4f);
            Assert.AreEqual(7f * 0.7f, WeaponStatMath.BlasterHeatPerShot(7f, Stats(StatModifier.Percent(StatId.BlasterHeatPerShotReduction, 200))), 1e-4f,
                "a runaway source is clamped once at the approved 30% cap, so heat per shot can never reach zero");
            Assert.AreEqual(0f, WeaponStatMath.BlasterHeatPerShot(7f, new FullReduction()), 1e-4f,
                "and if a future cap ever allowed 100%, the factor floors at zero rather than adding heat back");
        }

        [Test]
        public void BlasterHeatState_AppliesTheCoolingMultiplierWithoutDiscardingAccumulatedHeat()
        {
            var heat = new BlasterHeatState(100f, 10f, 0f, 1f);
            Assert.IsTrue(heat.AddShotHeat(50f));
            heat.CoolingRateMultiplier = 1.5f;
            heat.Tick(1f);
            Assert.AreEqual(35f, heat.Heat, 1e-3f, "one second sheds 15, not the authored 10");
            heat.CoolingRateMultiplier = 1f;
            heat.Tick(1f);
            Assert.AreEqual(25f, heat.Heat, 1e-3f, "removing the source restores the authored rate and keeps the heat");
        }

        [Test]
        public void BowChargeSpeed_ShortensTheDraw()
        {
            Assert.AreEqual(0.8f / 1.12f, WeaponStatMath.BowFullChargeSeconds(0.8f, Stats(StatModifier.Percent(StatId.BowChargeSpeed, 12))), 1e-5f);
        }

        [Test]
        public void SpreadKnockbackAndStagger_ScaleOnlyWhatTheWeaponAuthors()
        {
            var stats = Stats(StatModifier.Percent(StatId.WeaponSpreadReduction, 15), StatModifier.Percent(StatId.Knockback, 15), StatModifier.Percent(StatId.StaggerPower, 20));
            Assert.AreEqual(25.5f, WeaponStatMath.SpreadDegrees(30f, stats), 1e-4f);
            Assert.AreEqual(0f, WeaponStatMath.SpreadDegrees(0f, stats), 1e-4f, "a single-shot weapon gains no cone to tighten");
            Assert.AreEqual(6.9f, WeaponStatMath.Knockback(6f, stats), 1e-4f);
            Assert.AreEqual(0f, WeaponStatMath.Knockback(0f, stats), 1e-4f, "no stat can create impact a weapon does not have");
            Assert.AreEqual(3.6f, WeaponStatMath.StaggerPower(3f, stats), 1e-4f);
            Assert.AreEqual(0f, WeaponStatMath.StaggerPower(0f, stats), 1e-4f);
        }

        // ---- caps, recompute, no double application ----

        [Test]
        public void Caps_AreAppliedOnceOverTheSumOfEverySource()
        {
            var stats = new PlayerStats(_caps, 100);
            stats.SetSource(new StatModifierSource("accessory", StatModifier.Percent(StatId.ProjectileRange, 30)));
            stats.SetSource(new StatModifierSource("affix", StatModifier.Percent(StatId.ProjectileRange, 30)));
            Assert.AreEqual(_caps.GetCapPercent(StatId.ProjectileRange), stats.GetPercent(StatId.ProjectileRange), "60 is clamped to the approved cap");
            Assert.AreEqual(12f * 1.4f, WeaponStatMath.ProjectileRange(12f, stats), 1e-4f, "the consumer reads the capped value and does no cap math of its own");
        }

        [Test]
        public void AddingAndRemovingASourceReturnsTheExactBaseline()
        {
            var stats = new PlayerStats(_caps, 100);
            var baseline = (WeaponStatMath.FireRate(5f, stats), WeaponStatMath.MagazineSize(10, stats), WeaponStatMath.BowFullChargeSeconds(0.8f, stats));
            stats.SetSource(new StatModifierSource("gear",
                StatModifier.Percent(StatId.FireRate, 9), StatModifier.Percent(StatId.MagazineSize, 20), StatModifier.Percent(StatId.BowChargeSpeed, 15)));
            Assert.AreNotEqual(baseline.Item1, WeaponStatMath.FireRate(5f, stats));
            Assert.AreNotEqual(baseline.Item2, WeaponStatMath.MagazineSize(10, stats));

            Assert.IsTrue(stats.RemoveSource("gear"));
            Assert.AreEqual(baseline.Item1, WeaponStatMath.FireRate(5f, stats), 1e-5f);
            Assert.AreEqual(baseline.Item2, WeaponStatMath.MagazineSize(10, stats));
            Assert.AreEqual(baseline.Item3, WeaponStatMath.BowFullChargeSeconds(0.8f, stats), 1e-5f);
        }

        [Test]
        public void RegisteringTheSameSourceTwiceDoesNotApplyItTwice()
        {
            var stats = new PlayerStats(_caps, 100);
            stats.SetSource(new StatModifierSource("gear", StatModifier.Percent(StatId.FireRate, 9)));
            var once = WeaponStatMath.FireRate(5f, stats);
            stats.SetSource(new StatModifierSource("gear", StatModifier.Percent(StatId.FireRate, 9)));
            Assert.AreEqual(once, WeaponStatMath.FireRate(5f, stats), 1e-5f, "sources are keyed, so re-registering replaces rather than stacks");
            Assert.AreEqual(9, stats.GetPercent(StatId.FireRate));
        }

        // ---- ammo stack capacity, through the real inventory rule ----

        [Test]
        public void AmmoStackCapacity_ChangesTheOneCapacityRuleAndRestoresOnRemoval()
        {
            var ammoBalance = ScriptableObject.CreateInstance<AmmoBalanceConfig>();
            _created.Add(ammoBalance);
            var ammo = ScriptableObject.CreateInstance<AmmoItemDefinition>();
            _created.Add(ammo);
            Set(ammo, "_id", "ammo_light");
            Set(ammo, "_category", ItemCategory.Ammo);
            Set(ammo, "_isStackable", true);
            Set(ammo, "_ammoType", AmmoType.Light);

            var registry = ItemDefinitionRegistry.Build(new ItemDefinition[] { ammo });
            var inventory = PlayerInventory.FromRegistry(registry, ammoBalance);
            var stats = new PlayerStats(_caps, 100);
            inventory.SetAmmoCapacityBonusProvider(() => stats.GetPercent(StatId.AmmoStackCapacity));

            var baseLimit = ammoBalance.GetStackLimit(AmmoType.Light);
            Assert.AreEqual(baseLimit, inventory.MaxStackFor(ammo));

            stats.SetSource(new StatModifierSource("pouch", StatModifier.Percent(StatId.AmmoStackCapacity, 25)));
            Assert.AreEqual(Mathf.RoundToInt(baseLimit * 1.25f), inventory.MaxStackFor(ammo), "the limit follows the live stat, with no second rule");

            stats.RemoveSource("pouch");
            Assert.AreEqual(baseLimit, inventory.MaxStackFor(ammo), "removing the accessory restores the baseline exactly");
        }

        // ---- affix generation: nothing a player can roll may be a no-op ----

        [Test]
        public void EveryRolledAffixOnEveryAcquirableItemMapsToALiveStat()
        {
            var sources = Sources();
            var service = new AffixRollService();
            var rolled = new HashSet<string>(StringComparer.Ordinal);
            var checks = 0;

            foreach (var definition in Equipment().Where(e => e.IsAcquirableInV1 && e.AffixPool != null).OrderBy(e => e.Id, StringComparer.Ordinal))
            {
                foreach (var rarity in new[] { Rarity.Uncommon, Rarity.Rare, Rarity.Epic, Rarity.Legendary })
                {
                    for (var seed = 0; seed < 40; seed++)
                    {
                        var instance = new ItemInstance(definition.Id);
                        var result = service.Roll(instance, definition, rarity, new SeededRandom(SeededRandom.MixSeed(definition.Id.GetHashCode(), (int)rarity, seed)));
                        Assert.IsTrue(result.Success, $"{definition.Id} {rarity}: {result.Error} — an Epic roll that fails silently downgrades the item to Common");
                        foreach (var roll in instance.AffixRolls)
                        {
                            var affix = definition.AffixPool.Affixes.First(a => a != null && a.Id == roll.AffixId);
                            var stat = StatIds.FromAffix(affix.Stat);
                            Assert.IsTrue(StatConsumerIntegrityValidator.HasReader(stat, sources), $"{definition.Id} rolled {affix.Id}, whose {stat} has no reader");
                            Assert.IsFalse(StatConsumerIntegrityValidator.DeferredStats.ContainsKey(stat), $"{definition.Id} rolled the deferred stat {stat}");
                            Assert.IsTrue(StatConsumerIntegrityValidator.HasEffectOn(definition, stat), $"{definition.Id} rolled {affix.Id}, which is always 0 on it");
                            rolled.Add(affix.Id);
                            checks++;
                        }
                    }
                }
            }

            Assert.Greater(checks, 1000, "the sample must be large enough to reach every pool entry");
            Assert.Greater(rolled.Count, 15, "and wide enough to have rolled most of the catalogue's affixes");
        }

        [Test]
        public void RollingIsDeterministicForAGivenSeed()
        {
            var definition = Equipment().Single(e => e.Id == "weapon_breacher_12");
            var service = new AffixRollService();
            string Roll(int seed)
            {
                var instance = new ItemInstance(definition.Id);
                service.Roll(instance, definition, Rarity.Epic, new SeededRandom(seed));
                return string.Join(",", instance.AffixRolls.Select(r => r.AffixId + "=" + r.Value));
            }

            Assert.AreEqual(Roll(4242), Roll(4242), "the same seed rolls the same affixes");
            Assert.AreNotEqual(Roll(4242), Roll(99), "different seeds do not");
        }

        // ---- legacy saves ----

        [Test]
        public void ALegacyAffixIdLoadsSafelyAndContributesNothing()
        {
            var registry = AffixRegistry.FromDefinitions(Equipment());
            Assert.Greater(registry.Count, 10, "the registry sees the real affix catalogue");
            Assert.IsNull(registry.Get("affix_weapon_switch_speed"), "an id no current pool contains resolves to nothing");

            var item = new ItemInstance("weapon_breacher_12", 1, Rarity.Rare);
            item.AddAffixRoll(new AffixRoll("affix_weapon_switch_speed", 15));
            item.AddAffixRoll(new AffixRoll("affix_damage", 8));
            var source = new EquipmentAffixSource(item, registry.Get);
            var modifiers = source.GetModifiers().ToList();
            Assert.AreEqual(1, modifiers.Count, "the unknown roll is skipped, not fatal");
            Assert.AreEqual(StatId.WeaponDamage, modifiers[0].Stat);
            Assert.AreEqual(8, modifiers[0].Value);
            Assert.AreEqual(2, item.AffixRolls.Count, "and the stored roll is preserved rather than deleted");
        }

        // ================= the matrix artefact =================

        [Test]
        public void StatConsumerMatrix_IsCompleteAndMatchesTheTree()
        {
            var sources = Sources();
            CollectionAssert.AreEquivalent(Enum.GetValues(typeof(StatId)).Cast<StatId>().ToList(), Matrix.Select(r => r.Stat).ToList(),
                "the matrix must cover every stat any source can grant, exactly once");

            foreach (var row in Matrix)
            {
                var hasReader = StatConsumerIntegrityValidator.HasReader(row.Stat, sources);
                var deferred = StatConsumerIntegrityValidator.DeferredStats.ContainsKey(row.Stat);
                Assert.Contains(row.Decision, new[] { "CONSUME", "REMOVE_FROM_V1_POOL", "DEFER_EXPLICITLY" }, $"{row.Stat}: decision vocabulary");
                if (row.Decision == "CONSUME")
                {
                    Assert.IsTrue(hasReader, $"{row.Stat} is recorded as CONSUME but nothing reads it");
                    Assert.IsFalse(deferred, $"{row.Stat} is recorded as CONSUME but is also listed as deferred");
                    Assert.IsTrue(sources.ContainsKey(row.CodePath), $"{row.Stat}: RuntimeCodePath '{row.CodePath}' is not a gameplay file");
                }
                else
                {
                    Assert.IsFalse(hasReader, $"{row.Stat} is recorded as {row.Decision} but a reader now exists");
                    Assert.IsTrue(deferred, $"{row.Stat} is recorded as {row.Decision} but carries no documented reason");
                }
            }

            Directory.CreateDirectory(ProofFolder);
            var csv = new StringBuilder();
            csv.AppendLine("StatId,SourceType,SourceDefinition,PlayerFacingLabel,GrantedValue,RuntimeConsumer,RuntimeCodePath,EffectiveAtRuntimeBefore,Decision,EffectiveAtRuntimeAfter,VerificationMethod,Notes");
            foreach (var row in Matrix.OrderBy(r => r.Stat.ToString(), StringComparer.Ordinal))
            {
                var after = row.Decision == "CONSUME" ? "YES" : "NO (documented)";
                var notes = row.Decision == "DEFER_EXPLICITLY" ? StatConsumerIntegrityValidator.DeferredStats[row.Stat] : row.EffectiveAfter;
                csv.AppendLine(string.Join(",", new[]
                {
                    row.Stat.ToString(), row.SourceType, row.SourceDefinition, row.Label, row.Granted, row.Consumer, row.CodePath,
                    row.EffectiveBefore ? "YES" : "NO", row.Decision, after, row.Verification, notes
                }.Select(Csv)));
            }

            File.WriteAllText(Path.Combine(ProofFolder, "stat_consumer_matrix.csv"), csv.ToString());
        }

        // ================= helpers =================

        private static string Csv(string value)
        {
            value ??= string.Empty;
            return value.Contains(',') || value.Contains('"') ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        private static Dictionary<string, string> Sources() => StatConsumerIntegrityValidator.LoadSources();
        private static List<EquipmentItemDefinition> Equipment() => StatConsumerIntegrityValidator.LoadAllEquipment();
        private static List<AffixPool> Pools() => StatConsumerIntegrityValidator.LoadAllAffixPools();

        /// <summary>A provider that reduces everything completely, to pin the floor the caps currently keep out of reach.</summary>
        private sealed class FullReduction : IPlayerStatsProvider
        {
            public int GetPercent(StatId stat) => 100;
            public int GetFlat(StatId stat) => 0;
            public float GetMultiplier(StatId stat) => 2f;
            public float GetReductionFactor(StatId stat) => 0f;
            public int MaxHealth => 100;
            public bool ArmorInjectorActive => false;
            public event Action Recomputed { add { } remove { } }
        }

        private static void Set(object target, string field, object value)
        {
            var type = target.GetType();
            FieldInfo info = null;
            while (type != null && info == null)
            {
                info = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
                type = type.BaseType;
            }

            Assert.IsNotNull(info, $"field {field}");
            info.SetValue(target, value);
        }
    }
}

using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Stats;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 050 — stagger meter, knockback arithmetic and shared configuration (pure logic).</summary>
    public class StaggerKnockbackTests
    {
        private StaggerConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = StaggerConfig.Create(threshold: 10f, recoveryPerSecond: 5f, staggerDuration: 0.6f, immunity: 1f, unitsPerPoint: 0.25f, maxDistance: 4f, knockbackDuration: 0.15f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_config != null) Object.DestroyImmediate(_config);
        }

        [Test]
        public void ApprovedConfigAsset_ExistsAndIsTheOnlyTuningSource()
        {
            var asset = AssetDatabase.LoadAssetAtPath<StaggerConfig>("Assets/Game/ScriptableObjects/Balance/StaggerConfig.asset");
            Assert.IsNotNull(asset);
            Assert.Greater(asset.Threshold, 0f);
            Assert.Greater(asset.StaggerDurationSeconds, 0f);
            Assert.Greater(asset.PostStaggerImmunitySeconds, 0f, "Anti stun-lock window is mandatory.");
            Assert.Greater(asset.UnitsPerKnockbackPoint, 0f);
            Assert.Greater(asset.KnockbackDurationSeconds, 0f);
            Assert.IsNull(typeof(ImpactProfile).GetProperty("RandomModifier"), "No per-enemy random stagger modifiers.");
        }

        // ---- Acceptance 2: exact threshold trigger, deterministic reset/recovery ----

        [Test]
        public void Stagger_TriggersExactlyWhenThresholdIsReached_ThenResetsAndRecovers()
        {
            var meter = new StaggerMeter(_config);
            var triggers = 0;
            meter.Staggered += () => triggers++;

            Assert.IsFalse(meter.Apply(4f, 0).Triggered);
            Assert.AreEqual(4f, meter.Pressure);
            Assert.IsFalse(meter.Apply(5.9f, 0).Triggered, "9.9 < 10.");
            Assert.AreEqual(0, triggers);
            var hit = meter.Apply(0.1f, 0);
            Assert.IsTrue(hit.Triggered, "Exactly at the threshold.");
            Assert.AreEqual(1, triggers);
            Assert.AreEqual(0f, meter.Pressure, "Pressure resets on trigger.");
            Assert.IsTrue(meter.IsStaggered);
            Assert.AreEqual(0.6f, meter.StaggerTimeRemaining, 1e-5f);

            // While staggered nothing accumulates; after the stagger a fixed immunity window follows.
            Assert.AreEqual(0f, meter.Apply(50f, 0).Applied);
            meter.Tick(0.6f);
            Assert.IsFalse(meter.IsStaggered);
            Assert.IsTrue(meter.IsImmune);
            Assert.AreEqual(1f, meter.ImmunityRemaining, 1e-5f);
            Assert.AreEqual(0f, meter.Apply(50f, 0).Applied, "Immune: no stun-lock.");
            meter.Tick(1f);
            Assert.IsFalse(meter.IsImmune);

            // Recovery decays pressure deterministically (5/s).
            meter.Apply(8f, 0);
            meter.Tick(1f);
            Assert.AreEqual(3f, meter.Pressure, 1e-5f);
            meter.Tick(1f);
            Assert.AreEqual(0f, meter.Pressure);
            Assert.AreEqual(1, meter.TriggerCount);

            // Same inputs, same outcome.
            var a = new StaggerMeter(_config);
            var b = new StaggerMeter(_config);
            for (var i = 0; i < 7; i++) { a.Apply(3f, 20); a.Tick(0.1f); b.Apply(3f, 20); b.Tick(0.1f); }
            Assert.AreEqual(a.TriggerCount, b.TriggerCount);
            Assert.AreEqual(a.Pressure, b.Pressure);
        }

        [Test]
        public void StaggerResistance_ReducesPressure_AndFullResistanceBlocks()
        {
            var meter = new StaggerMeter(_config);
            Assert.AreEqual(5f, meter.Apply(10f, 50).Applied, 1e-5f);
            Assert.IsFalse(meter.IsStaggered);
            Assert.AreEqual(0.5f, meter.Apply(10f, 95).Applied, 1e-5f, "Boss-grade resistance: 95% of the pressure is shrugged off.");
            Assert.AreEqual(0f, meter.Apply(100f, 100).Applied);
            Assert.AreEqual(0f, new StaggerMeter(_config).Apply(10f, 150).Applied, "Clamped to 100.");
        }

        // ---- Acceptance 1: predictable knockback, resistance within cap ----

        [Test]
        public void Knockback_DistanceIsPredictable_AndResistanceReducesIt()
        {
            Assert.AreEqual(2f, KnockbackMath.Distance(8f, 0, _config), 1e-5f, "8 points x 0.25 = 2 units.");
            Assert.AreEqual(1f, KnockbackMath.Distance(8f, 50, _config), 1e-5f, "50% resistance halves it.");
            Assert.AreEqual(0.4f, KnockbackMath.Distance(8f, 80, _config), 1e-5f, "Elite 80%.");
            Assert.AreEqual(0f, KnockbackMath.Distance(8f, 100, _config));
            Assert.AreEqual(4f, KnockbackMath.Distance(100f, 0, _config), "Capped at the max distance.");
            Assert.AreEqual(0f, KnockbackMath.Distance(0.1f, 0, _config), "Below the minimum: ignored.");
            Assert.AreEqual(0f, KnockbackMath.Distance(-5f, 0, _config));
        }

        [Test]
        public void PlayerResistanceStats_AreCappedAt50Percent_ByTheApprovedCaps()
        {
            var caps = AssetDatabase.LoadAssetAtPath<GlobalStatCapsConfig>("Assets/Game/ScriptableObjects/Balance/GlobalStatCapsConfig.asset");
            var stats = new PlayerStats(caps);
            stats.SetSource(new StatModifierSource("test", StatModifier.Percent(StatId.KnockbackResistance, 70), StatModifier.Percent(StatId.StaggerResistance, 90)));
            Assert.AreEqual(50, stats.GetPercent(StatId.KnockbackResistance));
            Assert.AreEqual(50, stats.GetPercent(StatId.StaggerResistance));
            Assert.AreEqual(1f, KnockbackMath.Distance(8f, stats.GetPercent(StatId.KnockbackResistance), _config), 1e-5f);
        }

        // ---- Requirement 5: payload fields are live on definitions ----

        [Test]
        public void WeaponDefinitions_CarryKnockbackAndStaggerPower_AndMeleeAssetStillBinds()
        {
            Assert.IsNotNull(typeof(WeaponDefinition).GetProperty("Knockback"));
            Assert.IsNotNull(typeof(WeaponDefinition).GetProperty("StaggerPower"));
            var knife = AssetDatabase.LoadAssetAtPath<MeleeWeaponDefinition>("Assets/Game/ScriptableObjects/Items/FieldKnife.asset");
            Assert.IsNotNull(knife);
            Assert.AreEqual(0f, knife.Knockback, "No approved number yet: authored 0 (24: knife = low stagger).");
            var request = new ImpactRequest(new Vector2(3f, 4f), 2f, 3f, DamageKind.Explosion);
            Assert.AreEqual(1f, request.Direction.magnitude, 1e-5f);
            Assert.IsTrue(request.IsExplosion);
            Assert.IsTrue(request.HasKnockback && request.HasStagger);
            Assert.IsFalse(new ImpactRequest(Vector2.zero, 5f, 0f).HasKnockback, "No direction, no push.");
        }
    }
}

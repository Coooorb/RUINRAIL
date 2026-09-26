using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Items;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    public class BowChargeResolverTests
    {
        private const string RecurveBowAssetPath = "Assets/Game/ScriptableObjects/Items/RecurveBow.asset";
        private BowWeaponDefinition _bow;

        [SetUp]
        public void SetUp()
        {
            _bow = ScriptableObject.CreateInstance<BowWeaponDefinition>();
            Set("_quickDamageMin", 10);
            Set("_quickDamageMax", 12);
            Set("_fullDrawDamageMin", 25);
            Set("_fullDrawDamageMax", 30);
            Set("_fullChargeSeconds", 0.65f);
            Set("_quickProjectileSpeed", 16f);
            Set("_fullProjectileSpeed", 24f);
            Set("_quickRange", 8f);
            Set("_fullRange", 12f);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_bow);
        }

        private void Set(string field, object value)
        {
            typeof(BowWeaponDefinition).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_bow, value);
        }

        [Test]
        public void RecurveBow_UsesApprovedV1Values()
        {
            var definition = AssetDatabase.LoadAssetAtPath<BowWeaponDefinition>(RecurveBowAssetPath);

            Assert.IsNotNull(definition, $"Expected a BowWeaponDefinition asset at {RecurveBowAssetPath}.");
            Assert.AreEqual("weapon_recurve_bow", definition.Id);
            Assert.AreEqual("Recurve Bow", definition.DisplayName);
            Assert.AreEqual(WeaponClass.Bow, definition.WeaponClass);
            Assert.AreEqual(10, definition.QuickDamageMin);
            Assert.AreEqual(12, definition.QuickDamageMax);
            Assert.AreEqual(25, definition.FullDrawDamageMin);
            Assert.AreEqual(30, definition.FullDrawDamageMax);
            Assert.AreEqual(0.65f, definition.FullChargeSeconds, 0.0001f);
            Assert.IsNull(typeof(BowWeaponDefinition).GetProperty("AmmoType"));
            Assert.IsNull(typeof(BowWeaponDefinition).GetProperty("MagazineSize"));
            Assert.IsNull(typeof(BowWeaponDefinition).GetProperty("ReloadTime"));
        }

        [Test]
        public void ChargeFraction_ClampsBetweenZeroAndOne_AndReachesFullAtFullChargeTime()
        {
            Assert.AreEqual(0f, BowChargeResolver.ChargeFraction(0f, 0.65f), 0.0001f);
            Assert.AreEqual(0f, BowChargeResolver.ChargeFraction(-1f, 0.65f), 0.0001f);
            Assert.AreEqual(0.5f, BowChargeResolver.ChargeFraction(0.325f, 0.65f), 0.0001f);
            Assert.AreEqual(1f, BowChargeResolver.ChargeFraction(0.65f, 0.65f), 0.0001f);
            Assert.AreEqual(1f, BowChargeResolver.ChargeFraction(3f, 0.65f), 0.0001f, "Overcharge clamps to full.");
        }

        [Test]
        public void QuickShot_ResolvesExactlyQuickWindow()
        {
            var shot = BowChargeResolver.Resolve(_bow, 0f);
            Assert.AreEqual(10, shot.DamageMin);
            Assert.AreEqual(12, shot.DamageMax);
            Assert.AreEqual(16f, shot.ProjectileSpeed, 0.0001f);
            Assert.AreEqual(8f, shot.Range, 0.0001f);
        }

        [Test]
        public void FullDraw_ResolvesExactlyFullWindow_AndOverchargeAddsNothing()
        {
            var full = BowChargeResolver.Resolve(_bow, 1f);
            Assert.AreEqual(25, full.DamageMin);
            Assert.AreEqual(30, full.DamageMax);
            Assert.AreEqual(24f, full.ProjectileSpeed, 0.0001f);
            Assert.AreEqual(12f, full.Range, 0.0001f);

            var over = BowChargeResolver.Resolve(_bow, 5f);
            Assert.AreEqual(25, over.DamageMin);
            Assert.AreEqual(30, over.DamageMax);
        }

        [Test]
        public void PartialCharge_StaysIntegerAndMonotonic_BetweenWindows()
        {
            var previousMin = 10;
            var previousMax = 12;
            for (var i = 0; i <= 20; i++)
            {
                var shot = BowChargeResolver.Resolve(_bow, i / 20f);
                Assert.GreaterOrEqual(shot.DamageMin, previousMin);
                Assert.GreaterOrEqual(shot.DamageMax, previousMax);
                Assert.LessOrEqual(shot.DamageMin, shot.DamageMax);
                Assert.GreaterOrEqual(shot.DamageMin, 10);
                Assert.LessOrEqual(shot.DamageMax, 30);
                previousMin = shot.DamageMin;
                previousMax = shot.DamageMax;
            }

            var half = BowChargeResolver.Resolve(_bow, 0.5f);
            Assert.IsTrue(half.DamageMin == 17 || half.DamageMin == 18, "Half draw sits midway (17.5 rounded), strictly between quick and full.");
            Assert.AreEqual(21, half.DamageMax, "round(lerp(12,30,0.5)) = 21.");
        }
    

        [Test]
        public void Recovery_KeepsEveryDrawLengthAtOrBelowTheFullDrawDamageRate_ForEveryShippedBow()
        {
            foreach (var path in new[] { RecurveBowAssetPath, "Assets/Game/ScriptableObjects/Items/CompoundBow.asset", "Assets/Game/ScriptableObjects/Items/Stormstring.asset" })
            {
                var bow = AssetDatabase.LoadAssetAtPath<BowWeaponDefinition>(path);
                Assert.IsNotNull(bow, path);
                var full = bow.FullChargeSeconds;
                var fullRate = (bow.FullDrawDamageMin + bow.FullDrawDamageMax) * 0.5f / full;

                Assert.AreEqual(0f, BowChargeResolver.RecoverySeconds(bow, 1f, full), 1e-5f, $"{bow.Id}: a full draw owes no recovery");
                Assert.Greater(BowChargeResolver.RecoverySeconds(bow, 0f, full), 0f, $"{bow.Id}: a zero-draw tap owes recovery");

                for (var c = 0f; c <= 1.0001f; c += 0.05f)
                {
                    var shot = BowChargeResolver.Resolve(bow, c);
                    var cycle = c * full + BowChargeResolver.RecoverySeconds(bow, c, full);
                    var rate = (shot.DamageMin + shot.DamageMax) * 0.5f / cycle;
                    // Integer rounding of the interpolated damage band may add at most half a point per shot.
                    Assert.LessOrEqual(rate, fullRate + 0.5f / cycle + 1e-3f, $"{bow.Id}: charge {c:0.00} delivers {rate:0.0}/s, above the full-draw {fullRate:0.0}/s");
                }

                // A shorter full draw (Bow Charge Speed) shortens the recovery in proportion: the ratio holds.
                Assert.AreEqual(BowChargeResolver.RecoverySeconds(bow, 0f, full) * 0.8f, BowChargeResolver.RecoverySeconds(bow, 0f, full * 0.8f), 1e-5f);
            }
        }
    }
}

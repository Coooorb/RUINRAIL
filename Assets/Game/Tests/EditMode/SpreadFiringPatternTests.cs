using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core.Rng;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Items;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    public class SpreadFiringPatternTests
    {
        private const string Breacher12AssetPath = "Assets/Game/ScriptableObjects/Items/Breacher12.asset";

        [Test]
        public void Breacher12_UsesApprovedV1Values()
        {
            var definition = AssetDatabase.LoadAssetAtPath<RangedWeaponDefinition>(Breacher12AssetPath);

            Assert.IsNotNull(definition, $"Expected a RangedWeaponDefinition asset at {Breacher12AssetPath}.");
            Assert.AreEqual("weapon_breacher_12", definition.Id);
            Assert.AreEqual("Breacher-12", definition.DisplayName);
            Assert.AreEqual(WeaponClass.Shotgun, definition.WeaponClass);
            Assert.AreEqual(6, definition.ProjectilesPerShot);
            Assert.AreEqual(5, definition.DamageMin);
            Assert.AreEqual(7, definition.DamageMax);
            Assert.AreEqual(1.4f, definition.FireRate, 0.001f);
            Assert.AreEqual(6, definition.MagazineSize);
            Assert.AreEqual(2.2f, definition.ReloadTime, 0.001f);
            Assert.AreEqual(6f, definition.Range, 0.001f);
            Assert.AreEqual(16f, definition.ProjectileSpeed, 0.001f);
            Assert.AreEqual(AmmoType.Shells, definition.AmmoType);
            Assert.AreEqual(1, definition.AmmoCostPerShot, "One Shell per shot, not per pellet.");
            Assert.Greater(definition.SpreadDegrees, 0f);
        }

        [Test]
        public void SingleProjectileGuns_KeepOnePelletAndNoSpread()
        {
            foreach (var path in new[] { "Assets/Game/ScriptableObjects/Items/P9Ranger.asset", "Assets/Game/ScriptableObjects/Items/AR17.asset" })
            {
                var definition = AssetDatabase.LoadAssetAtPath<RangedWeaponDefinition>(path);
                Assert.AreEqual(1, definition.ProjectilesPerShot, path);
                Assert.AreEqual(0f, definition.SpreadDegrees, 0.001f, path);
            }
        }

        [Test]
        public void SpreadPattern_EmitsConfiguredPelletCount_WithinHalfConeAroundAim()
        {
            var pattern = new SpreadFiringPattern(6, 30f, new SeededRandom(11));
            var directions = new List<Vector2>();
            var aim = new Vector2(1f, 1f).normalized;

            for (var shot = 0; shot < 100; shot++)
            {
                pattern.ResolveDirections(aim, directions);
                Assert.AreEqual(6, directions.Count);
                foreach (var d in directions)
                {
                    Assert.AreEqual(1f, d.magnitude, 0.001f, "Directions stay unit length.");
                    Assert.LessOrEqual(Vector2.Angle(aim, d), 15f + 0.001f, "Each pellet lies within ±spread/2 of aim.");
                }
            }
        }

        [Test]
        public void SpreadPattern_UsesTheWholeCone_NotJustCenter()
        {
            var pattern = new SpreadFiringPattern(6, 30f, new SeededRandom(5));
            var directions = new List<Vector2>();
            var maxAngle = 0f;
            var leftSeen = false;
            var rightSeen = false;

            for (var shot = 0; shot < 200; shot++)
            {
                pattern.ResolveDirections(Vector2.right, directions);
                foreach (var d in directions)
                {
                    maxAngle = Mathf.Max(maxAngle, Vector2.Angle(Vector2.right, d));
                    leftSeen |= d.y > 0.05f;
                    rightSeen |= d.y < -0.05f;
                }
            }

            Assert.Greater(maxAngle, 12f, "Pellets must reach near the cone edge.");
            Assert.IsTrue(leftSeen && rightSeen, "Pellets scatter to both sides of the aim.");
        }

        [Test]
        public void SpreadPattern_IsDeterministicForSameSeed()
        {
            var a = new SpreadFiringPattern(6, 30f, new SeededRandom(42));
            var b = new SpreadFiringPattern(6, 30f, new SeededRandom(42));
            var c = new SpreadFiringPattern(6, 30f, new SeededRandom(43));
            var da = new List<Vector2>();
            var db = new List<Vector2>();
            var dc = new List<Vector2>();

            a.ResolveDirections(Vector2.up, da);
            b.ResolveDirections(Vector2.up, db);
            c.ResolveDirections(Vector2.up, dc);

            CollectionAssert.AreEqual(da, db);
            Assert.IsTrue(da.Zip(dc, (x, y) => (x - y).sqrMagnitude > 1e-6f).Any());
        }

        [Test]
        public void SpreadPattern_ZeroSpread_FiresAllPelletsStraight_AndSinglePatternReturnsAim()
        {
            var straight = new SpreadFiringPattern(4, 0f, new SeededRandom(1));
            var directions = new List<Vector2>();
            straight.ResolveDirections(Vector2.down, directions);
            Assert.AreEqual(4, directions.Count);
            Assert.IsTrue(directions.All(d => Vector2.Angle(Vector2.down, d) < 0.001f));

            SingleProjectilePattern.Instance.ResolveDirections(Vector2.left, directions);
            Assert.AreEqual(1, directions.Count);
            Assert.AreEqual(Vector2.left, directions[0]);
        }
    }
}

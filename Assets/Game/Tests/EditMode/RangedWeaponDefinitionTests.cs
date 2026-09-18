using System;
using System.Linq;
using NUnit.Framework;
using RuinRail.Gameplay.Items;
using UnityEditor;

namespace RuinRail.Tests
{
    public class RangedWeaponDefinitionTests
    {
        private const string P9RangerAssetPath = "Assets/Game/ScriptableObjects/Items/P9Ranger.asset";
        private const string Ar17AssetPath = "Assets/Game/ScriptableObjects/Items/AR17.asset";
        private const string FieldKnifeAssetPath = "Assets/Game/ScriptableObjects/Items/FieldKnife.asset";
        private const string Rattler9AssetPath = "Assets/Game/ScriptableObjects/Items/Rattler9.asset";
        private const string SentinelAssetPath = "Assets/Game/ScriptableObjects/Items/SentinelBR.asset";
        private const string LongshotAssetPath = "Assets/Game/ScriptableObjects/Items/LongshotS1.asset";
        private const string PipeLauncherAssetPath = "Assets/Game/ScriptableObjects/Items/PipeLauncher.asset";
        private const string ScrapSpearAssetPath = "Assets/Game/ScriptableObjects/Items/ScrapSpear.asset";

        [Test]
        public void P9Ranger_UsesApprovedV1Values()
        {
            var definition = AssetDatabase.LoadAssetAtPath<RangedWeaponDefinition>(P9RangerAssetPath);

            Assert.IsNotNull(definition, $"Expected a RangedWeaponDefinition asset at {P9RangerAssetPath}.");
            Assert.AreEqual(12, definition.DamageMin);
            Assert.AreEqual(14, definition.DamageMax);
            Assert.AreEqual(4.0f, definition.FireRate, 0.001f);
            Assert.AreEqual(12, definition.MagazineSize);
            Assert.AreEqual(1.2f, definition.ReloadTime, 0.001f);
            Assert.AreEqual(10f, definition.Range, 0.001f);
            Assert.AreEqual(20f, definition.ProjectileSpeed, 0.001f);
            Assert.AreEqual(AmmoType.Light, definition.AmmoType);
            Assert.AreEqual("weapon_p9_ranger", definition.Id);
            Assert.AreEqual(WeaponClass.Pistol, definition.WeaponClass);
            Assert.AreEqual(ItemCategory.Weapon, definition.Category);
        }

        [Test]
        public void AR17_UsesApprovedV1Values()
        {
            var definition = AssetDatabase.LoadAssetAtPath<RangedWeaponDefinition>(Ar17AssetPath);

            Assert.IsNotNull(definition, $"Expected a RangedWeaponDefinition asset at {Ar17AssetPath}.");
            Assert.AreEqual(8, definition.DamageMin);
            Assert.AreEqual(10, definition.DamageMax);
            Assert.AreEqual(7.5f, definition.FireRate, 0.001f);
            Assert.AreEqual(30, definition.MagazineSize);
            Assert.AreEqual(1.8f, definition.ReloadTime, 0.001f);
            Assert.AreEqual(12f, definition.Range, 0.001f);
            Assert.AreEqual(22f, definition.ProjectileSpeed, 0.001f);
            Assert.AreEqual(AmmoType.Medium, definition.AmmoType);
            Assert.AreEqual(1, definition.AmmoCostPerShot);
            Assert.AreEqual("weapon_ar_17", definition.Id);
            Assert.AreEqual("AR-17", definition.DisplayName);
            Assert.AreEqual(WeaponClass.AssaultRifle, definition.WeaponClass);
            Assert.AreEqual(ItemCategory.Weapon, definition.Category);
            Assert.IsFalse(definition.IsStackable);
            Assert.IsNotNull(definition.AffixPool, "AR-17 must be linked to the ranged affix pool for later loot rolls.");
            Assert.AreEqual("pool_ranged", definition.AffixPool.Id);
        }

        [Test]
        public void Rattler9_UsesApprovedV1Values()
        {
            var definition = AssetDatabase.LoadAssetAtPath<RangedWeaponDefinition>(Rattler9AssetPath);

            Assert.IsNotNull(definition, $"Expected a RangedWeaponDefinition asset at {Rattler9AssetPath}.");
            Assert.AreEqual(6, definition.DamageMin);
            Assert.AreEqual(8, definition.DamageMax);
            Assert.AreEqual(10.0f, definition.FireRate, 0.001f);
            Assert.AreEqual(32, definition.MagazineSize);
            Assert.AreEqual(1.6f, definition.ReloadTime, 0.001f);
            Assert.AreEqual(8f, definition.Range, 0.001f);
            Assert.AreEqual(18f, definition.ProjectileSpeed, 0.001f, "33: SMG projectile speed 18 tiles/s.");
            Assert.AreEqual(AmmoType.Light, definition.AmmoType);
            Assert.AreEqual(1, definition.AmmoCostPerShot);
            Assert.AreEqual(1, definition.ProjectilesPerShot);
            Assert.AreEqual(0f, definition.SpreadDegrees, "No approved SMG spread number yet: straight until itemization tunes it.");
            Assert.AreEqual(0f, definition.Knockback);
            Assert.AreEqual(0f, definition.StaggerPower);
            Assert.AreEqual("weapon_rattler_9", definition.Id);
            Assert.AreEqual("Rattler-9", definition.DisplayName);
            Assert.AreEqual(WeaponClass.Smg, definition.WeaponClass);
            Assert.AreEqual(ItemCategory.Weapon, definition.Category);
            Assert.IsFalse(definition.IsStackable);
            Assert.IsNotNull(definition.AffixPool);
            Assert.AreEqual("pool_ranged", definition.AffixPool.Id);
            Assert.IsTrue(string.IsNullOrEmpty(definition.LegendaryMechanicId), "Rattler-9 is not a Legendary.");
        }
        [Test]
        public void SentinelBR_UsesApprovedV1Values_AndClassIdentityFeedsDataNotBehaviour()
        {
            var definition = AssetDatabase.LoadAssetAtPath<RangedWeaponDefinition>(SentinelAssetPath);

            Assert.IsNotNull(definition, $"Expected a RangedWeaponDefinition asset at {SentinelAssetPath}.");
            Assert.AreEqual(18, definition.DamageMin);
            Assert.AreEqual(22, definition.DamageMax);
            Assert.AreEqual(3.0f, definition.FireRate, 0.001f);
            Assert.AreEqual(12, definition.MagazineSize);
            Assert.AreEqual(2.0f, definition.ReloadTime, 0.001f);
            Assert.AreEqual(15f, definition.Range, 0.001f);
            Assert.AreEqual(26f, definition.ProjectileSpeed, 0.001f, "33: Battle Rifle projectile speed 26 tiles/s.");
            Assert.AreEqual(AmmoType.Medium, definition.AmmoType);
            Assert.AreEqual(1, definition.AmmoCostPerShot);
            Assert.AreEqual(1, definition.ProjectilesPerShot);
            Assert.AreEqual(0f, definition.SpreadDegrees);
            Assert.AreEqual("weapon_sentinel_br", definition.Id);
            Assert.AreEqual("Sentinel BR", definition.DisplayName);
            Assert.AreEqual(WeaponClass.BattleRifle, definition.WeaponClass);
            Assert.AreEqual(ItemCategory.Weapon, definition.Category);
            Assert.IsNotNull(definition.AffixPool);
            Assert.AreEqual("pool_ranged", definition.AffixPool.Id);
            Assert.IsTrue(string.IsNullOrEmpty(definition.LegendaryMechanicId));

            // Class identity is data: the economy prices it by class (500 base per 77) with no class switch in combat code.
            var economy = AssetDatabase.LoadAssetAtPath<RuinRail.Gameplay.Economy.EconomyConfig>("Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset");
            Assert.IsTrue(economy.TryGetWeaponClassPrice(WeaponClass.BattleRifle, out var basePrice));
            Assert.AreEqual(500, basePrice);
            var combatSources = System.IO.Directory.GetFiles("Assets/Game/Scripts/Combat", "*.cs", System.IO.SearchOption.AllDirectories)
                .Select(System.IO.File.ReadAllText);
            Assert.IsFalse(combatSources.Any(src => src.Contains("WeaponClass.BattleRifle") || src.Contains("WeaponClass.Smg") || src.Contains("WeaponClass.AssaultRifle")),
                "No per-class behaviour switches in combat code.");
        }
        [Test]
        public void LongshotS1_UsesApprovedV1Values()
        {
            var definition = AssetDatabase.LoadAssetAtPath<RangedWeaponDefinition>(LongshotAssetPath);

            Assert.IsNotNull(definition, $"Expected a RangedWeaponDefinition asset at {LongshotAssetPath}.");
            Assert.AreEqual(45, definition.DamageMin);
            Assert.AreEqual(52, definition.DamageMax);
            Assert.AreEqual(1.0f, definition.FireRate, 0.001f);
            Assert.AreEqual(5, definition.MagazineSize);
            Assert.AreEqual(2.4f, definition.ReloadTime, 0.001f);
            Assert.AreEqual(20f, definition.Range, 0.001f);
            Assert.AreEqual(34f, definition.ProjectileSpeed, 0.001f, "33: Sniper projectile speed 34 tiles/s; visible projectile, never hitscan.");
            Assert.AreEqual(AmmoType.Heavy, definition.AmmoType, "33: Snipers use Heavy Ammo.");
            Assert.AreEqual(1, definition.AmmoCostPerShot);
            Assert.AreEqual(1, definition.ProjectilesPerShot);
            Assert.AreEqual(0f, definition.SpreadDegrees);
            Assert.AreEqual("weapon_longshot_s1", definition.Id);
            Assert.AreEqual("Longshot S1", definition.DisplayName);
            Assert.AreEqual(WeaponClass.Sniper, definition.WeaponClass);
            Assert.AreEqual(ItemCategory.Weapon, definition.Category);
            Assert.AreEqual("pool_ranged", definition.AffixPool.Id);
            Assert.IsTrue(string.IsNullOrEmpty(definition.LegendaryMechanicId));
            Assert.IsNull(typeof(RangedWeaponDefinition).GetProperty("CritChance"), "No crits.");
            Assert.IsNull(typeof(RangedWeaponDefinition).GetProperty("WeakSpotMultiplier"), "No weak spots.");
        }
        [Test]
        public void PipeLauncher_UsesApprovedV1Values_WithFourHeavyPerShot()
        {
            var definition = AssetDatabase.LoadAssetAtPath<RangedWeaponDefinition>(PipeLauncherAssetPath);

            Assert.IsNotNull(definition, $"Expected a RangedWeaponDefinition asset at {PipeLauncherAssetPath}.");
            Assert.AreEqual(65, definition.DamageMin);
            Assert.AreEqual(80, definition.DamageMax);
            Assert.AreEqual(0.5f, definition.FireRate, 0.001f);
            Assert.AreEqual(1, definition.MagazineSize);
            Assert.AreEqual(2.2f, definition.ReloadTime, 0.001f);
            Assert.AreEqual(14f, definition.Range, 0.001f);
            Assert.AreEqual(10f, definition.ProjectileSpeed, 0.001f, "33: Rocket Launcher projectile speed 10 tiles/s.");
            Assert.AreEqual(AmmoType.Heavy, definition.AmmoType, "26: rockets use Heavy Ammo, no fifth category.");
            Assert.AreEqual(4, definition.AmmoCostPerShot, "26: 4 Heavy Ammo per shot.");
            Assert.AreEqual(1, definition.ProjectilesPerShot);
            Assert.IsTrue(definition.IsExplosive);
            Assert.AreEqual(2f, definition.ExplosionRadiusTiles, 0.001f, "PROTOTYPE radius (no approved number).");
            Assert.AreEqual("weapon_pipe_launcher", definition.Id);
            Assert.AreEqual("Pipe Launcher", definition.DisplayName);
            Assert.AreEqual(WeaponClass.RocketLauncher, definition.WeaponClass);
            Assert.AreEqual("pool_ranged", definition.AffixPool.Id);
            Assert.IsTrue(string.IsNullOrEmpty(definition.LegendaryMechanicId), "Sunbreaker salvo is not implemented here.");

            foreach (var other in new[] { P9RangerAssetPath, Ar17AssetPath, Rattler9AssetPath, SentinelAssetPath, LongshotAssetPath })
            {
                Assert.IsFalse(AssetDatabase.LoadAssetAtPath<RangedWeaponDefinition>(other).IsExplosive, $"{other} is a direct-hit weapon.");
            }
        }
        [Test]
        public void ScrapSpear_UsesApprovedV1Values_OnTheSharedMeleeDefinition()
        {
            var spear = AssetDatabase.LoadAssetAtPath<MeleeWeaponDefinition>(ScrapSpearAssetPath);
            Assert.IsNotNull(spear, $"Expected a MeleeWeaponDefinition asset at {ScrapSpearAssetPath}.");
            Assert.AreEqual(24, spear.DamageMin);
            Assert.AreEqual(28, spear.DamageMax);
            Assert.AreEqual(1.8f, spear.AttackRate, 0.001f);
            Assert.AreEqual(2.6f, spear.AttackRange, 0.001f);
            Assert.AreEqual(20f, spear.AttackArcDegrees, 0.001f, "Total arc: +/-10 degrees around AimDirection.");
            Assert.AreEqual("weapon_scrap_spear", spear.Id);
            Assert.AreEqual("Scrap Spear", spear.DisplayName);
            Assert.AreEqual(WeaponClass.Spear, spear.WeaponClass);
            Assert.AreEqual(ItemCategory.Weapon, spear.Category);
            Assert.IsTrue(spear.WindUpSeconds + spear.RecoverySeconds < 1f / spear.AttackRate, "Swing phases fit inside the 1.8/s cadence.");
            Assert.AreEqual(0f, spear.Knockback);
            Assert.AreEqual(0f, spear.StaggerPower);
            Assert.IsTrue(string.IsNullOrEmpty(spear.LegendaryMechanicId));
            Assert.AreSame(spear.GetType(), AssetDatabase.LoadAssetAtPath<MeleeWeaponDefinition>(FieldKnifeAssetPath).GetType(), "Same definition type as the knife: shape is data.");

            // Knife is untouched by spear support.
            var knife = AssetDatabase.LoadAssetAtPath<MeleeWeaponDefinition>(FieldKnifeAssetPath);
            Assert.AreEqual(14, knife.DamageMin);
            Assert.AreEqual(17, knife.DamageMax);
            Assert.AreEqual(3.5f, knife.AttackRate, 0.001f);
            Assert.AreEqual(1.2f, knife.AttackRange, 0.001f);
            Assert.AreEqual(80f, knife.AttackArcDegrees, 0.001f);
        }
        [Test]
        public void WeaponDefinitions_AreEquipmentItems_WithClassIdentity()
        {
            var knife = AssetDatabase.LoadAssetAtPath<MeleeWeaponDefinition>(FieldKnifeAssetPath);
            Assert.IsNotNull(knife);
            Assert.AreEqual("weapon_field_knife", knife.Id);
            Assert.AreEqual(WeaponClass.Knife, knife.WeaponClass);
            Assert.AreEqual(ItemCategory.Weapon, knife.Category);
            Assert.IsInstanceOf<EquipmentItemDefinition>(knife);
            Assert.IsInstanceOf<EquipmentItemDefinition>(AssetDatabase.LoadAssetAtPath<RangedWeaponDefinition>(P9RangerAssetPath));
        }

        [Test]
        public void WeaponClass_ContainsExactlyTheElevenApprovedClasses()
        {
            CollectionAssert.AreEquivalent(
                new[]
                {
                    WeaponClass.Pistol, WeaponClass.Smg, WeaponClass.AssaultRifle, WeaponClass.BattleRifle, WeaponClass.Shotgun,
                    WeaponClass.Sniper, WeaponClass.Bow, WeaponClass.RocketLauncher, WeaponClass.Blaster, WeaponClass.Knife, WeaponClass.Spear
                },
                Enum.GetValues(typeof(WeaponClass)).Cast<WeaponClass>());
        }

        [Test]
        public void AuthoredWeaponAssets_HaveUniqueIds_AndMatchCatalogClassResource()
        {
            var weapons = AssetDatabase.FindAssets("t:WeaponDefinition", new[] { "Assets/Game/ScriptableObjects/Items" })
                .Select(g => AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .ToArray();

            Assert.GreaterOrEqual(weapons.Length, 3);
            Assert.AreEqual(weapons.Length, weapons.Select(w => w.Id).Distinct().Count());
            foreach (var ranged in weapons.OfType<RangedWeaponDefinition>())
            {
                var expectedAmmo = ranged.WeaponClass switch
                {
                    WeaponClass.Pistol => AmmoType.Light,
                    WeaponClass.Smg => AmmoType.Light,
                    WeaponClass.AssaultRifle => AmmoType.Medium,
                    WeaponClass.BattleRifle => AmmoType.Medium,
                    WeaponClass.Shotgun => AmmoType.Shells,
                    WeaponClass.Sniper => AmmoType.Heavy,
                    WeaponClass.RocketLauncher => AmmoType.Heavy,
                    _ => (AmmoType?)null
                };
                if (expectedAmmo.HasValue)
                {
                    Assert.AreEqual(expectedAmmo.Value, ranged.AmmoType, $"{ranged.Id} must use its class ammo type.");
                }
            }
        }
    }
}

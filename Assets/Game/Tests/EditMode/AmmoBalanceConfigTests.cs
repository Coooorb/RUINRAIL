using System;
using NUnit.Framework;
using RuinRail.Gameplay.Items;
using UnityEditor;

namespace RuinRail.Tests
{
    public class AmmoBalanceConfigTests
    {
        private const string AssetPath = "Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset";

        [Test]
        public void ExactlyFourNormalAmmoTypesExist()
        {
            var values = Enum.GetValues(typeof(AmmoType));
            Assert.AreEqual(4, values.Length);
            CollectionAssert.AreEquivalent(
                new[] { AmmoType.Light, AmmoType.Medium, AmmoType.Heavy, AmmoType.Shells },
                values);
        }

        [Test]
        public void StackLimits_UseApprovedV1Values()
        {
            var config = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>(AssetPath);

            Assert.IsNotNull(config, $"Expected an AmmoBalanceConfig asset at {AssetPath}.");
            Assert.AreEqual(180, config.GetStackLimit(AmmoType.Light));
            Assert.AreEqual(120, config.GetStackLimit(AmmoType.Medium));
            Assert.AreEqual(60, config.GetStackLimit(AmmoType.Heavy));
            Assert.AreEqual(40, config.GetStackLimit(AmmoType.Shells));
        }

        [Test]
        public void P9Ranger_AmmoCostPerShot_IsOne()
        {
            var definition = AssetDatabase.LoadAssetAtPath<RangedWeaponDefinition>("Assets/Game/ScriptableObjects/Items/P9Ranger.asset");
            Assert.IsNotNull(definition);
            Assert.AreEqual(1, definition.AmmoCostPerShot);
        }
    }
}

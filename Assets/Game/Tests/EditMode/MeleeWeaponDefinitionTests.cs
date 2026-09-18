using System.Linq;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Items;
using UnityEditor;

namespace RuinRail.Tests
{
    public class MeleeWeaponDefinitionTests
    {
        private const string FieldKnifeAssetPath = "Assets/Game/ScriptableObjects/Items/FieldKnife.asset";

        [Test]
        public void FieldKnife_UsesApprovedV1Values()
        {
            var definition = AssetDatabase.LoadAssetAtPath<MeleeWeaponDefinition>(FieldKnifeAssetPath);

            Assert.IsNotNull(definition, $"Expected a MeleeWeaponDefinition asset at {FieldKnifeAssetPath}.");
            Assert.AreEqual(14, definition.DamageMin);
            Assert.AreEqual(17, definition.DamageMax);
            Assert.AreEqual(3.5f, definition.AttackRate, 0.001f);
            Assert.AreEqual(1.2f, definition.AttackRange, 0.001f);
            Assert.AreEqual(80f, definition.AttackArcDegrees, 0.001f);
        }

        [Test]
        public void MeleeWeaponDefinition_HasNoAmmoOrStaminaFields()
        {
            var fieldNames = typeof(MeleeWeaponDefinition)
                .GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Select(f => f.Name.ToLowerInvariant())
                .ToArray();

            Assert.IsFalse(fieldNames.Any(name => name.Contains("ammo")), "Melee weapons must not consume ammo.");
            Assert.IsFalse(fieldNames.Any(name => name.Contains("stamina")), "Melee weapons must not consume stamina.");
        }

        [Test]
        public void SharedDamageRoller_AlwaysReturnsIntegerWithinFieldKnifeRange()
        {
            var roller = new UnityRandomDamageRoller();

            for (var i = 0; i < 200; i++)
            {
                var damage = roller.Roll(14, 17);
                Assert.GreaterOrEqual(damage, 14);
                Assert.LessOrEqual(damage, 17);
            }
        }
    }
}

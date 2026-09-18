using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Items;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    public class ItemDataModelTests
    {
        private sealed class TestItemDefinition : ItemDefinition
        {
        }

        private static TestItemDefinition CreateDefinition(string id, ItemCategory category = ItemCategory.Weapon, bool stackable = false)
        {
            var definition = ScriptableObject.CreateInstance<TestItemDefinition>();
            definition.name = id ?? "unnamed";
            typeof(ItemDefinition).GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(definition, id);
            typeof(ItemDefinition).GetField("_category", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(definition, category);
            typeof(ItemDefinition).GetField("_isStackable", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(definition, stackable);
            return definition;
        }

        [Test]
        public void TwoInstancesOfSameDefinition_HaveDifferentInstanceIds_ButSameDefinitionId()
        {
            var a = new ItemInstance("weapon_p9_ranger");
            var b = new ItemInstance("weapon_p9_ranger");

            Assert.AreNotEqual(a.InstanceId, b.InstanceId);
            Assert.AreEqual("weapon_p9_ranger", a.DefinitionId);
            Assert.AreEqual(a.DefinitionId, b.DefinitionId);
        }

        [Test]
        public void ItemDefinition_ExposesNoPublicMutators()
        {
            var publicSetters = typeof(ItemDefinition)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.SetMethod != null && p.SetMethod.IsPublic)
                .Select(p => p.Name)
                .ToArray();
            var publicMutatingMethods = typeof(ItemDefinition)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.Name.StartsWith("Set"))
                .Select(m => m.Name)
                .ToArray();

            Assert.IsEmpty(publicSetters, "Shared definitions must not be mutable through instance state.");
            Assert.IsEmpty(publicMutatingMethods);
        }

        [Test]
        public void InstanceMutation_DoesNotTouchDefinition()
        {
            var definition = CreateDefinition("consumable_medkit", ItemCategory.Consumable, stackable: true);
            var instance = new ItemInstance(definition.Id, quantity: 3);

            instance.SetQuantity(7);
            instance.Rarity = Rarity.Epic;
            instance.IsAtRisk = true;
            instance.AddAffixRoll(new AffixRoll("affix_test", 5));

            Assert.AreEqual("consumable_medkit", definition.Id);
            Assert.AreEqual(ItemCategory.Consumable, definition.Category);
            Assert.IsTrue(definition.IsStackable);
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void Snapshot_RoundTrips_Ids_Rarity_Affixes_Quantity_AndRiskFlag()
        {
            var original = new ItemInstance("armor_riot", quantity: 1, rarity: Rarity.Rare) { IsAtRisk = true };
            original.AddAffixRoll(new AffixRoll("affix_max_hp", 12));
            original.AddAffixRoll(new AffixRoll("affix_move_speed", 3));

            var json = JsonUtility.ToJson(original.ToSnapshot());
            var restored = ItemInstance.FromSnapshot(JsonUtility.FromJson<ItemInstanceSnapshot>(json));

            Assert.AreEqual(original.InstanceId, restored.InstanceId);
            Assert.AreEqual(original.DefinitionId, restored.DefinitionId);
            Assert.AreEqual(Rarity.Rare, restored.Rarity);
            Assert.AreEqual(1, restored.Quantity);
            Assert.IsTrue(restored.IsAtRisk);
            Assert.AreEqual(2, restored.AffixRolls.Count);
            Assert.AreEqual("affix_max_hp", restored.AffixRolls[0].AffixId);
            Assert.AreEqual(12, restored.AffixRolls[0].Value);
            Assert.AreEqual("affix_move_speed", restored.AffixRolls[1].AffixId);
            Assert.AreEqual(3, restored.AffixRolls[1].Value);
        }

        [Test]
        public void StackSnapshot_RoundTrips_Quantity()
        {
            var stack = new ItemInstance("ammo_light", quantity: 120);
            var restored = ItemInstance.FromSnapshot(JsonUtility.FromJson<ItemInstanceSnapshot>(JsonUtility.ToJson(stack.ToSnapshot())));
            Assert.AreEqual(120, restored.Quantity);
            Assert.AreEqual(0, restored.AffixRolls.Count);
        }

        [Test]
        public void Quantity_NeverNegative()
        {
            var stack = new ItemInstance("ammo_light", quantity: -5);
            Assert.AreEqual(0, stack.Quantity);
            stack.SetQuantity(-1);
            Assert.AreEqual(0, stack.Quantity);
        }

        [Test]
        public void Registry_DetectsDuplicateIds()
        {
            var a = CreateDefinition("weapon_ar17");
            var b = CreateDefinition("weapon_ar17");
            var registry = ItemDefinitionRegistry.Build(new ItemDefinition[] { a, b });

            Assert.AreEqual(1, registry.Count);
            Assert.AreEqual(1, registry.Problems.Count);
            StringAssert.Contains("Duplicate id 'weapon_ar17'", registry.Problems[0]);
            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }

        [Test]
        public void Registry_DetectsMissingOrInvalidIds()
        {
            var missing = CreateDefinition(null);
            var invalid = CreateDefinition("Weapon-AR17");
            var registry = ItemDefinitionRegistry.Build(new ItemDefinition[] { missing, invalid });

            Assert.AreEqual(0, registry.Count);
            Assert.AreEqual(2, registry.Problems.Count);
            Object.DestroyImmediate(missing);
            Object.DestroyImmediate(invalid);
        }

        [TestCase("weapon_ar17", true)]
        [TestCase("accessory_runner_watch", true)]
        [TestCase("consumable_medkit", true)]
        [TestCase("", false)]
        [TestCase("Weapon_AR17", false)]
        [TestCase("weapon ar17", false)]
        [TestCase("_weapon", false)]
        public void IdPattern_IsValidationFriendly(string id, bool expected)
        {
            Assert.AreEqual(expected, ItemDefinitionRegistry.IsValidId(id));
        }

        [Test]
        public void ProjectItemDefinitionAssets_HaveNoDuplicateOrInvalidIds()
        {
            var definitions = AssetDatabase.FindAssets("t:ItemDefinition")
                .Select(guid => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(d => d != null)
                .ToArray();

            var registry = ItemDefinitionRegistry.Build(definitions);

            Assert.IsEmpty(registry.Problems, string.Join("\n", registry.Problems));
            Assert.AreEqual(definitions.Length, registry.Count);
        }

        [Test]
        public void ItemCategories_MatchApprovedSlotsAndStackTypes()
        {
            CollectionAssert.AreEquivalent(
                new[] { ItemCategory.Weapon, ItemCategory.Armor, ItemCategory.Accessory, ItemCategory.Consumable, ItemCategory.Ammo },
                System.Enum.GetValues(typeof(ItemCategory)));
            CollectionAssert.AreEquivalent(
                new[] { Rarity.Common, Rarity.Uncommon, Rarity.Rare, Rarity.Epic, Rarity.Legendary },
                System.Enum.GetValues(typeof(Rarity)));
        }
    }
}

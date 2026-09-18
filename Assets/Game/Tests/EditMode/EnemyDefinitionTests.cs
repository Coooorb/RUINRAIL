using NUnit.Framework;
using RuinRail.Gameplay.Enemies;
using UnityEditor;

namespace RuinRail.Tests
{
    public class EnemyDefinitionTests
    {
        private const string GruntAssetPath = "Assets/Game/ScriptableObjects/Enemies/Grunt.asset";

        [Test]
        public void Grunt_UsesApprovedV1BaseStats()
        {
            var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(GruntAssetPath);

            Assert.IsNotNull(definition, $"Expected an EnemyDefinition asset at {GruntAssetPath}.");
            Assert.AreEqual("grunt", definition.Id);
            Assert.AreEqual(30, definition.BaseHealth);
            Assert.AreEqual(6, definition.DamageMin);
            Assert.AreEqual(8, definition.DamageMax);
            Assert.AreEqual(3.0f, definition.MoveSpeed, 0.001f);
            Assert.AreEqual(12, definition.BaseXp);
        }
    }
}

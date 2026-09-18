using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Player;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RuinRail.Tests
{
    public class PlayerMovementTestRoomSceneTests
    {
        private const string ScenePath = "Assets/Game/Scenes/_Test/PlayerMovementTestRoom.unity";

        [Test]
        public void TestRoomScene_HasWiredPlayerWithBalanceConfig()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var playerObject = scene.GetRootGameObjects().FirstOrDefault(go => go.name == "Player");
                Assert.IsNotNull(playerObject, "Expected a 'Player' GameObject in the test room scene.");

                var movement = playerObject.GetComponent<PlayerMovement>();
                Assert.IsNotNull(movement, "Expected a PlayerMovement component on the Player GameObject.");

                var configField = typeof(PlayerMovement).GetField("_balanceConfig", BindingFlags.NonPublic | BindingFlags.Instance);
                var config = configField.GetValue(movement);
                Assert.IsNotNull(config, "Expected PlayerMovement's balance config to be assigned in the authored test scene.");

                Assert.IsNotNull(playerObject.GetComponent<PlayerInput>(), "Expected a PlayerInput component on the Player GameObject.");
                Assert.IsNotNull(playerObject.GetComponent<Rigidbody2D>(), "Expected a Rigidbody2D on the Player GameObject.");
                Assert.IsNotNull(playerObject.GetComponent<Collider2D>(), "Expected a Collider2D on the Player GameObject.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void TestRoomScene_HasWiredHealthComponentWithDashInvulnerability()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var playerObject = scene.GetRootGameObjects().FirstOrDefault(go => go.name == "Player");
                Assert.IsNotNull(playerObject, "Expected a 'Player' GameObject in the test room scene.");

                var health = playerObject.GetComponent<HealthComponent>();
                Assert.IsNotNull(health, "Expected a HealthComponent on the Player GameObject.");

                var maxHealthField = typeof(HealthComponent).GetField("_maxHealth", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.AreEqual(100, (int)maxHealthField.GetValue(health));

                Assert.IsNotNull(playerObject.GetComponent<PlayerDash>(),
                    "Expected a PlayerDash component so HealthComponent can auto-discover it as the IInvulnerabilityState source.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void TestRoomScene_HasFourRoomWalls()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var wallNames = new[] { "Wall_North", "Wall_South", "Wall_East", "Wall_West" };
                foreach (var wallName in wallNames)
                {
                    var wall = scene.GetRootGameObjects().FirstOrDefault(go => go.name == wallName);
                    Assert.IsNotNull(wall, $"Expected a '{wallName}' GameObject in the test room scene.");
                    Assert.IsNotNull(wall.GetComponent<Collider2D>(), $"Expected '{wallName}' to have a Collider2D.");
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}

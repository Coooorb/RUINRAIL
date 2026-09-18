using NUnit.Framework;
using UnityEngine.TestTools;
using RuinRail.EditorTools.Production;
using RuinRail.Networking;
using Unity.Netcode;
using UnityEngine;

namespace RuinRail.Tests.EditMode
{
    /// <summary>
    /// TASK 180 — two release-honesty rules. A shipped build must not silently run online play on the in-memory
    /// fakes, and the network player prefab must actually exist and be registered, or NGO cannot spawn anyone.
    /// </summary>
    public sealed class LiveServiceCompositionTests
    {
        [Test]
        public void APlayerBuild_DefaultsToLiveAdapters()
        {
            Assert.AreEqual(LiveServiceConfiguration.Mode.Live,
                LiveServiceConfiguration.Resolve(new[] { "RUINRAIL.exe" }),
                "A shipped build presenting FakeMultiplayerServices as online play would be lying to the player.");
        }

        [Test]
        public void FakesRequireAnExplicitOptOut()
        {
            Assert.AreEqual(LiveServiceConfiguration.Mode.Fake,
                LiveServiceConfiguration.Resolve(new[] { "RUINRAIL.exe", LiveServiceConfiguration.OfflineArgument }));

            Assert.AreEqual(LiveServiceConfiguration.Mode.Fake,
                LiveServiceConfiguration.Resolve(new[] { "RUINRAIL.exe", LiveServiceConfiguration.SmokeArgument }),
                "The deterministic smoke must never touch a live service.");
        }

        [Test]
        public void FakeMode_ComposesTheInMemoryAdapters()
        {
            var (services, driver) = LiveServiceConfiguration.Compose(LiveServiceConfiguration.Mode.Fake, null);

            Assert.IsInstanceOf<FakeMultiplayerServices>(services);
            Assert.IsInstanceOf<FakeNetworkDriver>(driver);
        }

        [Test]
        public void LiveModeWithoutANetworkManager_ReportsItRatherThanDegradingSilently()
        {
            LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("no NetworkManager"));

            var (services, driver) = LiveServiceConfiguration.Compose(LiveServiceConfiguration.Mode.Live, null);

            Assert.IsInstanceOf<UnityMultiplayerServices>(services);
            Assert.IsNull(driver, "No driver is better than a fake one that pretends online play works.");
        }

        [Test]
        public void LiveModeWithANetworkManager_ComposesTheRealAdapters()
        {
            var go = new GameObject("NetworkManagerFixture");
            try
            {
                var manager = go.AddComponent<NetworkManager>();
                var (services, driver) = LiveServiceConfiguration.Compose(LiveServiceConfiguration.Mode.Live, manager);

                Assert.IsInstanceOf<UnityMultiplayerServices>(services);
                Assert.IsInstanceOf<NgoNetworkDriver>(driver);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void NetworkPlayerPrefab_ExistsAndIsRegistered()
        {
            var prefab = NetworkPlayerPrefabAuthoring.Load();
            Assert.IsNotNull(prefab, $"{NetworkPlayerPrefabAuthoring.PrefabPath} must exist: NgoPlayerEntityFactory has nothing to spawn without it.");

            Assert.IsNotNull(prefab.GetComponent<NetworkObject>(), "NGO cannot spawn an object without a NetworkObject.");
            Assert.IsNotNull(prefab.GetComponent<NetworkPlayerObject>(), "Identity and owner input isolation.");
            Assert.IsNotNull(prefab.GetComponent<NetworkHealth>(), "Authoritative health replication.");
            Assert.IsNotNull(prefab.GetComponent<NetworkPlayerCombat>(), "Authoritative combat replication.");

            Assert.IsTrue(NetworkPlayerPrefabAuthoring.IsRegistered(),
                "An unregistered prefab fails at spawn time in a build, not here.");
        }

        [Test]
        public void NetworkPlayerPrefab_IsTheProvenPlayerEntity_NotAParallelOne()
        {
            var prefab = NetworkPlayerPrefabAuthoring.Load();

            // Built by PlayerEntityBuilder, so it carries the same gameplay components the solo player has.
            foreach (var expected in new[]
                     {
                         typeof(RuinRail.Gameplay.Player.PlayerMovement),
                         typeof(RuinRail.Gameplay.Player.PlayerAiming),
                         typeof(RuinRail.Gameplay.Player.PlayerDash),
                         typeof(RuinRail.Gameplay.Player.PlayerInteractor),
                         typeof(RuinRail.Gameplay.Player.PlayerLootReceiver)
                     })
            {
                Assert.IsNotNull(prefab.GetComponent(expected),
                    $"{expected.Name} is missing: the networked player must be the proven player entity, not a hand-built copy that can drift from it.");
            }
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 106: ~60 s reconnect grace with identity/state reservation, Dead on expiry (no drop), host loss = failure for everyone, no host migration.</summary>
    public class DisconnectGraceTests
    {
        private readonly List<Object> _created = new();
        private PlayerBalanceConfig _balance;
        private DisplayNamePolicy _policy;
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;
        private double _now;

        private sealed class TrackingFactory : IPlayerEntityFactory
        {
            private readonly LocalPlayerEntityFactory _inner;
            public readonly List<GameObject> Spawned = new();
            public int Despawns;
            public TrackingFactory(PlayerBalanceConfig balance) { _inner = new LocalPlayerEntityFactory(balance); }
            public GameObject Spawn(PlayerIdentity identity, bool isLocalOwner)
            {
                var go = _inner.Spawn(identity, isLocalOwner);
                Spawned.Add(go);
                return go;
            }

            public void Despawn(GameObject entity)
            {
                Despawns++;
                if (entity != null) Object.DestroyImmediate(entity);
            }
        }

        [SetUp]
        public void SetUp()
        {
            _balance = AssetDatabase.LoadAssetAtPath<PlayerBalanceConfig>("Assets/Game/ScriptableObjects/Player/PlayerBalanceConfig.asset");
            _policy = AssetDatabase.LoadAssetAtPath<DisplayNamePolicy>("Assets/Game/ScriptableObjects/Player/DisplayNamePolicy.asset");
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(catalog);
            _ammoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
            _now = 1000.0;
            FakeMultiplayerServices.ResetRegistry();
            DamageAuthority.LocalIsAuthoritative = true;
        }

        [TearDown]
        public void TearDown()
        {
            DamageAuthority.LocalIsAuthoritative = true;
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var movement in Object.FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None)) if (movement != null) Object.DestroyImmediate(movement.gameObject);
            _created.Clear();
        }

        private ItemDefinition Resolve(string id) => _registry.TryGet(id, out var d) ? d : null;

        private ExpeditionService NewExpedition(out PlayerProfile profile)
        {
            profile = new PlayerProfile
            {
                SafeLoadout = new InventorySnapshot
                {
                    Equipped = new[] { new InventorySnapshot.Entry { Slot = (int)EquippedSlot.PrimaryWeapon, Item = new ItemInstance("weapon_p9_ranger").ToSnapshot() } },
                    Backpack = System.Array.Empty<InventorySnapshot.Entry>()
                }
            };
            var ammoByType = _registry.Definitions.OfType<AmmoItemDefinition>().ToDictionary(a => a.AmmoType, a => a);
            return new ExpeditionService(Resolve, t => ammoByType.TryGetValue(t, out var a) ? a : null, _ammoBalance);
        }

        private (PlayerPresenceService presence, FakeConnectionEvents connection, TrackingFactory factory, SessionRoster roster, ReconnectGraceService grace) Host(bool expeditionActive)
        {
            var connection = new FakeConnectionEvents(0, isHost: true);
            var factory = new TrackingFactory(_balance);
            var roster = new SessionRoster(_policy);
            var config = AssetDatabase.LoadAssetAtPath<MultiplayerBalanceConfig>("Assets/Game/ScriptableObjects/Balance/MultiplayerBalanceConfig.asset");
            Assert.IsNotNull(config, "Approved multiplayer config asset must exist.");
            Assert.AreEqual(60f, config.ReconnectGraceSeconds, 0.0001f, "85: ~60 s reconnect grace.");
            var grace = new ReconnectGraceService(config.ReconnectGraceSeconds, () => expeditionActive, () => _now);
            return (new PlayerPresenceService(connection, factory, roster, grace), connection, factory, roster, grace);
        }

        private static (NetworkSessionController controller, FakeMultiplayerServices services, FakeNetworkDriver driver) Session()
        {
            var services = new FakeMultiplayerServices();
            var driver = new FakeNetworkDriver();
            return (new NetworkSessionController(services, driver), services, driver);
        }

        private static T Wait<T>(Task<T> task)
        {
            task.Wait();
            return task.Result;
        }

        // ---- Acceptance 1: reconnect within grace restores the same entity/ids exactly once ----

        [Test]
        public void ClientDisconnect_DuringExpedition_KeepsTheCharacterAtRisk_AndReconnectRebindsTheSameEntityOnce()
        {
            var (presence, connection, factory, roster, grace) = Host(expeditionActive: true);
            connection.Connect(0, "Host");
            connection.Connect(1, "Mate");
            var entity = presence.Entities[1];
            var go = entity.GameObject;
            var token = entity.ReconnectToken;
            Assert.IsFalse(string.IsNullOrEmpty(token));
            var inventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            var rifle = new ItemInstance("weapon_p9_ranger");
            inventory.TryEquip(rifle, EquippedSlot.PrimaryWeapon);
            go.GetComponent<PlayerLootReceiver>().SetInventory(inventory);
            go.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(30));

            connection.Disconnect(1);
            Assert.AreEqual(0, factory.Despawns, "The character stays represented.");
            Assert.IsTrue(go != null && go.activeInHierarchy);
            Assert.AreEqual(1, grace.Pending.Count);
            Assert.IsFalse(presence.Entities.ContainsKey(1));
            Assert.IsFalse(roster.Contains(1), "Not a connected member while dropped...");
            Assert.IsTrue(go.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(10)), "...but still at risk in the world.");

            // Reconnects with a new client id inside the grace.
            _now += 30;
            connection.Reconnect(7, token, "Mate");
            Assert.AreEqual(1, presence.ReconnectCount);
            Assert.AreEqual(2, presence.SpawnCount, "No new entity was spawned.");
            Assert.AreEqual(1, factory.Spawned.Count(g => g != null && g.name.Contains("Mate")), "Exactly one Mate object exists.");
            Assert.AreSame(entity, presence.Entities[7]);
            Assert.AreSame(go, presence.Entities[7].GameObject);
            Assert.AreEqual(7UL, entity.Identity.ClientId);
            Assert.AreEqual("Mate", entity.Identity.DisplayName);
            Assert.IsTrue(roster.Contains(7));
            Assert.AreSame(rifle, inventory.GetEquipped(EquippedSlot.PrimaryWeapon), "Same item instance, never duplicated.");
            Assert.AreEqual(60, go.GetComponent<HealthComponent>().CurrentHealth, "State continued through the drop.");
            Assert.AreEqual(0, grace.Pending.Count);

            // A second reclaim of the same token finds nothing (idempotent), and no second entity appears.
            connection.Reconnect(8, token, "Mate");
            Assert.AreEqual(1, presence.ReconnectCount);
            Assert.AreEqual(3, presence.SpawnCount, "A stale token is just a fresh join (new entity for client 8).");
            Assert.AreEqual(1, grace.Reclaimed);
            Assert.AreEqual(3, roster.Count);
        }

        // ---- Acceptance 2: grace expiry -> Dead once, gear never dropped ----

        [Test]
        public void GraceExpiry_MakesTheCharacterDeadOnce_AndDropsNothing()
        {
            var (presence, connection, factory, _, grace) = Host(expeditionActive: true);
            connection.Connect(0, "Host");
            connection.Connect(1, "Mate");
            var go = presence.Entities[1].GameObject;
            var life = go.GetComponent<PlayerLifeStateComponent>();
            var deaths = 0;
            life.Died += _ => deaths++;
            var inventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            var rifle = new ItemInstance("weapon_p9_ranger");
            inventory.TryEquip(rifle, EquippedSlot.PrimaryWeapon);
            go.GetComponent<PlayerLootReceiver>().SetInventory(inventory);
            var token = presence.Entities[1].ReconnectToken;

            connection.Disconnect(1);
            _now += 59.9;
            Assert.AreEqual(0, grace.Tick());
            Assert.AreEqual(PlayerLifeState.Alive, life.State);
            _now += 0.2;
            Assert.AreEqual(1, grace.Tick());
            Assert.AreEqual(PlayerLifeState.Dead, life.State);
            Assert.AreEqual("reconnect_grace_expired", life.LastDeathReason);
            Assert.AreEqual(1, deaths);
            Assert.AreEqual(0, grace.Tick(), "Expired once.");
            Assert.AreEqual(1, grace.Expired);
            Assert.AreSame(rifle, inventory.GetEquipped(EquippedSlot.PrimaryWeapon), "Gear stays on the dead character.");
            Assert.AreEqual(0, factory.Despawns);
            Assert.IsFalse(go.GetComponent<PlayerLootReceiver>().TryDrop(rifle.InstanceId).Success);

            // Too late: the token is gone; a reconnect is a fresh join and the dead character is not rebound.
            connection.Reconnect(9, token, "Mate");
            Assert.AreEqual(0, presence.ReconnectCount);
            Assert.AreEqual(1, deaths);
        }

        [Test]
        public void OutsideAnExpedition_ADroppedMemberIsSimplyDespawned()
        {
            var (presence, connection, factory, _, grace) = Host(expeditionActive: false);
            connection.Connect(0, "Host");
            connection.Connect(1, "Mate");
            connection.Disconnect(1);
            Assert.AreEqual(1, factory.Despawns);
            Assert.AreEqual(0, grace.Pending.Count);
            Assert.AreEqual(1, presence.DespawnCount);
        }

        [Test]
        public void ConnectionPayload_RoundTripsNameAndToken()
        {
            Assert.AreEqual(("Mate", (string)null), ConnectionPayload.Decode(ConnectionPayload.Encode("Mate")));
            var (name, token) = ConnectionPayload.Decode(ConnectionPayload.Encode("Mate", "abc123"));
            Assert.AreEqual("Mate", name);
            Assert.AreEqual("abc123", token);
            Assert.AreEqual((string.Empty, (string)null), ConnectionPayload.Decode(null));
        }

        // ---- Acceptance 3/4: host loss = one failure transaction per peer, no host migration ----

        [Test]
        public void HostDisconnect_FailsEveryPeersExpeditionOnce_NoPartialExtraction_NoMigration()
        {
            var (host, _, hostDriver) = Session();
            Wait(host.HostAsync(new SessionRequest()));
            var (clientA, _, driverA) = Session();
            var (clientB, _, driverB) = Session();
            var joinA = Wait(clientA.JoinAsync(host.Session.JoinCode));
            Assert.IsTrue(joinA.Success, $"A: {joinA.Error} {joinA.Message}");
            var joinB = Wait(clientB.JoinAsync(host.Session.JoinCode));
            Assert.IsTrue(joinB.Success, $"B: {joinB.Error} {joinB.Message}");

            var peers = new[] { host, clientA, clientB }.Select(c =>
            {
                var expedition = NewExpedition(out var profile);
                expedition.Start(profile, 3, Biome.RuinedMetro, 3);
                expedition.AddCarriedCoins(150);
                return (controller: c, expedition, profile, binding: new SessionExpeditionBinding(c, expedition));
            }).ToList();

            // The host's transport goes away: the host itself and both clients lose the session.
            hostDriver.Drop();
            driverA.Drop();
            driverB.Drop();

            foreach (var peer in peers)
            {
                Assert.AreEqual(NetworkLifecycleState.Failed, peer.controller.State);
                Assert.AreEqual(NetworkRole.Offline, peer.controller.Role, "Nobody becomes host.");
                Assert.IsFalse(peer.expedition.IsExpeditionActive, $"peer index {peers.IndexOf(peer)} role-before-drop; failures={peer.binding.Failures} state={peer.controller.State} err={peer.controller.LastError.Message}");
                Assert.AreEqual(ExpeditionOutcome.Failed, peer.expedition.LastSummary.Outcome);
                Assert.AreEqual(150, peer.expedition.LastSummary.CoinsLost);
                Assert.AreEqual(0, peer.expedition.LastSummary.CoinsExtracted, "No partial extraction.");
                Assert.IsNull(peer.profile.SafeLoadout);
                Assert.AreEqual(1, peer.binding.Failures);
                Assert.AreSame(peer.expedition.LastSummary, peer.expedition.Return(), "The closed transaction cannot be extracted afterwards.");
                peer.binding.Dispose();
            }

            Assert.IsFalse(NetworkSessionController.SupportsHostMigration);
            var sources = System.IO.Directory.GetFiles("Assets/Game/Scripts/Multiplayer", "*.cs").Select(System.IO.File.ReadAllText);
            foreach (var source in sources)
            {
                var stripped = source.Replace("SupportsHostMigration = false", string.Empty).Replace("no host migration", string.Empty).Replace("No host migration", string.Empty).Replace("Host migration is explicitly post-MVP", string.Empty);
                Assert.IsFalse(stripped.Contains("HostMigration") || stripped.Contains("MigrateHost") || stripped.Contains("PromoteToHost"), "No host migration code exists (V1).");
            }
        }

        [Test]
        public void LeavingMidRun_IsAFailure_ButLeavingAfterExtractionChangesNothing()
        {
            var (host, _, _) = Session();
            Wait(host.HostAsync(new SessionRequest()));
            var expedition = NewExpedition(out var profile);
            expedition.Start(profile, 1, Biome.RuinedMetro);
            using var binding = new SessionExpeditionBinding(host, expedition);
            Wait(host.LeaveAsync());
            Assert.AreEqual(ExpeditionOutcome.Failed, expedition.LastSummary.Outcome, "Quitting mid-run is a failure (no Alt+F4 extraction).");
            Assert.AreEqual(1, binding.Failures);

            var (host2, _, _) = Session();
            Wait(host2.HostAsync(new SessionRequest()));
            var expedition2 = NewExpedition(out var profile2);
            expedition2.Start(profile2, 1, Biome.RuinedMetro);
            using var binding2 = new SessionExpeditionBinding(host2, expedition2);
            var summary = expedition2.Return();
            Wait(host2.LeaveAsync());
            Assert.AreSame(summary, expedition2.LastSummary);
            Assert.AreEqual(ExpeditionOutcome.Extracted, summary.Outcome);
            Assert.AreEqual(0, binding2.Failures);
        }
    }
}

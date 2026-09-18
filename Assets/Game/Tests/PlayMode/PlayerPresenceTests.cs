using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 093: one owned entity per member, remote replicas are input-isolated, despawn once, names sanitized.</summary>
    public class PlayerPresenceTests
    {
        private readonly List<Object> _created = new();
        private DisplayNamePolicy _policy;
        private PlayerBalanceConfig _balance;

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
            _policy = AssetDatabase.LoadAssetAtPath<DisplayNamePolicy>("Assets/Game/ScriptableObjects/Player/DisplayNamePolicy.asset");
            _balance = AssetDatabase.FindAssets("t:PlayerBalanceConfig").Select(g => AssetDatabase.LoadAssetAtPath<PlayerBalanceConfig>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
            Assert.IsNotNull(_policy);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var movement in Object.FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None)) if (movement != null) Object.DestroyImmediate(movement.gameObject);
            _created.Clear();
        }

        private (PlayerPresenceService presence, FakeConnectionEvents connection, TrackingFactory factory, SessionRoster roster) Host(ulong localId = 0)
        {
            var connection = new FakeConnectionEvents(localId, isHost: true);
            var factory = new TrackingFactory(_balance);
            var roster = new SessionRoster(_policy);
            return (new PlayerPresenceService(connection, factory, roster), connection, factory, roster);
        }

        [Test]
        public void SoloDuoTrio_SpawnExactlyOneOwnedEntityPerMember_LocalOwnerOnlyForTheLocalClient()
        {
            foreach (var party in new[] { 1, 2, 3 })
            {
                var (presence, connection, factory, roster) = Host();
                for (ulong id = 0; id < (ulong)party; id++) connection.Connect(id, $"Runner{id}");

                Assert.AreEqual(party, presence.Entities.Count, $"party {party}");
                Assert.AreEqual(party, factory.Spawned.Count);
                Assert.AreEqual(party, roster.Count);
                Assert.AreEqual(1, presence.Entities.Values.Count(e => e.IsLocalOwner), "Exactly one local owner.");
                Assert.AreEqual(0ul, presence.LocalEntity.OwnerClientId);
                CollectionAssert.AllItemsAreUnique(presence.Entities.Values.Select(e => e.OwnerClientId));
                foreach (var entity in presence.Entities.Values)
                {
                    Assert.AreEqual(entity.Identity.ClientId, entity.OwnerClientId, "Stable ownership = member client id.");
                    Assert.IsNotNull(entity.GameObject.GetComponent<PlayerMovement>());
                    Assert.IsNotNull(entity.GameObject.GetComponent<PlayerDash>());
                    Assert.IsNotNull(entity.GameObject.GetComponent<PlayerLootReceiver>());
                    Assert.AreEqual(entity.IsLocalOwner, entity.GameObject.GetComponent<PlayerInput>() != null, "Only the local owner carries PlayerInput.");
                }

                // Duplicate connect callbacks never duplicate an entity.
                connection.Connect(0, "Runner0");
                Assert.AreEqual(party, presence.Entities.Count);
                presence.Dispose();
            }
        }

        [Test]
        public void FourthMember_IsNotSpawned_PartyLimitIsThree()
        {
            var (presence, connection, _, roster) = Host();
            for (ulong id = 0; id < 4; id++) connection.Connect(id, $"P{id}");
            Assert.AreEqual(3, presence.Entities.Count);
            Assert.AreEqual(3, roster.Count);
            Assert.IsFalse(roster.Contains(3));
            presence.Dispose();
        }

        [UnityTest]
        public IEnumerator RemoteReplica_NeverReadsLocalDevices()
        {
            var (presence, connection, _, _) = Host(localId: 0);
            connection.Connect(0, "Local");
            connection.Connect(1, "Remote");
            var local = presence.Entities[0].GameObject;
            var remote = presence.Entities[1].GameObject;
            Assert.IsNotNull(local.GetComponent<PlayerInput>());
            Assert.IsNull(remote.GetComponent<PlayerInput>(), "Remote replicas have no local input component.");
            Assert.IsTrue(PlayerEntityBuilder.IsInputIsolated(remote));
            Assert.IsFalse(PlayerEntityBuilder.IsInputIsolated(local));

            var inputReaderField = typeof(PlayerMovement).GetField("_inputReader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsInstanceOf<NullPlayerInputReader>(inputReaderField.GetValue(remote.GetComponent<PlayerMovement>()));
            Assert.IsNotInstanceOf<NullPlayerInputReader>(inputReaderField.GetValue(local.GetComponent<PlayerMovement>()));
            var dashField = typeof(PlayerDash).GetField("_inputReader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsInstanceOf<NullPlayerInputReader>(dashField.GetValue(remote.GetComponent<PlayerDash>()));

            var start = remote.transform.position;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(start, remote.transform.position, "A replica with the null reader never moves from local input.");
            presence.Dispose();
        }

        [Test]
        public void Disconnect_DespawnsExactlyOnce_AndKeepsOtherMembers()
        {
            var (presence, connection, factory, roster) = Host();
            connection.Connect(0, "Host");
            connection.Connect(1, "Guest");
            var despawned = new List<ulong>();
            presence.Despawned += e => despawned.Add(e.OwnerClientId);

            connection.Disconnect(1);
            connection.Disconnect(1);
            Assert.AreEqual(1, factory.Despawns);
            CollectionAssert.AreEqual(new ulong[] { 1 }, despawned);
            Assert.AreEqual(1, presence.Entities.Count);
            Assert.IsTrue(roster.Contains(0));
            Assert.IsFalse(roster.Contains(1));

            connection.Connect(1, "Guest");
            Assert.AreEqual(2, presence.Entities.Count, "Rejoining spawns a fresh entity.");
            presence.Dispose();
            Assert.AreEqual(0, presence.Entities.Count, "Dispose despawns everything once.");
            Assert.AreEqual(3, factory.Despawns);
        }

        [Test]
        public void Client_DoesNotSpawnEntities_SpawningIsAHostDecision()
        {
            var connection = new FakeConnectionEvents(2, isHost: false);
            var factory = new TrackingFactory(_balance);
            var presence = new PlayerPresenceService(connection, factory, new SessionRoster(_policy));
            connection.Connect(2, "Me");
            Assert.AreEqual(0, presence.Entities.Count);
            Assert.AreEqual(0, factory.Spawned.Count);
            presence.Dispose();
        }

        [Test]
        public void DisplayNames_AreSanitizedForTheSession_WithoutTouchingTheProfile()
        {
            var roster = new SessionRoster(_policy);
            var profileName = "  Rail_Runner  ";
            var identity = roster.Add(0, profileName, true);
            Assert.AreEqual("Rail_Runner", identity.DisplayName);
            Assert.AreEqual("  Rail_Runner  ", profileName, "The caller's string (profile copy) is untouched.");

            Assert.AreEqual("Player 2", roster.Add(1, "<b>evil</b>", false).DisplayName, "Markup is rejected → fallback.");
            Assert.AreEqual("Player 3", roster.Add(2, "", false).DisplayName, "Empty → fallback.");
            Assert.Throws<System.InvalidOperationException>(() => roster.Add(3, "Fourth", false));
            Assert.AreEqual(3, roster.Count);
            Assert.AreSame(identity, roster.Add(0, "Other", true), "Adding an existing id returns the existing identity.");
        }

        [Test]
        public void PartyRoster_IsAHostAuthoritativeDomain()
        {
            Assert.IsTrue(HostAuthorityContract.CanDecide(NetworkRole.Host, AuthoritativeDomain.PartyRoster));
            Assert.IsFalse(HostAuthorityContract.CanDecide(NetworkRole.Client, AuthoritativeDomain.PartyRoster));
        }
    }
}

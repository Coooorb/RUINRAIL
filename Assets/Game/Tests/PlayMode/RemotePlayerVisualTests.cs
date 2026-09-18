using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core.Input;
using RuinRail.Core.Rendering;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using RuinRail.Presentation;
using RuinRail.Presentation.Animation;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>
    /// Every session member's entity — the local owner and every remote replica — receives the one player
    /// presentation composition (body + animation + held weapon) through the presence factory's decorate hook and the
    /// network player object's composer hook: host sees the client's body, a client sees the host's body, the
    /// replicated weapon id drives the replica's held sprite, reconnects keep the same visuals, despawns remove them,
    /// and nothing is ever stacked twice. Runs on the deterministic fake-transport harness; no live Relay claim.
    /// </summary>
    public sealed class RemotePlayerVisualTests
    {
        private readonly List<Object> _created = new();
        private GameContentCatalog _catalog;
        private DisplayNamePolicy _policy;

        [SetUp]
        public void SetUp()
        {
            _catalog = GameContentCatalog.Load();
            _policy = AssetDatabase.LoadAssetAtPath<DisplayNamePolicy>("Assets/Game/ScriptableObjects/Player/DisplayNamePolicy.asset");
            NetworkPlayerObject.VisualComposer = go => PlayerVisualComposer.Compose(go, _catalog);
        }

        [TearDown]
        public void TearDown()
        {
            NetworkPlayerObject.VisualComposer = null;
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var movement in Object.FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None)) if (movement != null) Object.DestroyImmediate(movement.gameObject);
            _created.Clear();
        }

        private sealed class TrackingFactory : IPlayerEntityFactory
        {
            private readonly LocalPlayerEntityFactory _inner;
            public readonly List<GameObject> Spawned = new();
            public int Despawns;
            public TrackingFactory(GameContentCatalog catalog)
            {
                _inner = new LocalPlayerEntityFactory(catalog.PlayerBalance, catalog.StatCaps, null, (go, _) => PlayerVisualComposer.Compose(go, catalog));
            }

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

        private static void AssertComposedOnce(GameObject entity, string label)
        {
            Assert.IsNotNull(CharacterVisual.RendererOf(entity), $"{label}: body renderer");
            Assert.IsNotNull(entity.GetComponent<PlayerAnimationDriver>(), $"{label}: animation driver");
            Assert.IsNotNull(entity.GetComponent<HeldWeaponVisual>(), $"{label}: held weapon visual");
            Assert.AreEqual(SortingLayers.Characters, CharacterVisual.RendererOf(entity).sortingLayerName, $"{label}: body on Characters");
            Assert.IsNotNull(CharacterVisual.RendererOf(entity).GetComponent<YSorter>(), $"{label}: y-sorted");
            Assert.AreEqual(1, entity.GetComponents<HeldWeaponVisual>().Length, $"{label}: one weapon visual");
            Assert.AreEqual(1, entity.GetComponents<PlayerAnimationDriver>().Length, $"{label}: one driver");
            Assert.AreEqual(2, entity.GetComponentsInChildren<SpriteRenderer>(true).Length, $"{label}: body + weapon renderers, no duplicate stack");
        }

        [Test]
        public void Host_SeesEveryMembersBody_LocalAndRemote_ComposedExactlyOnce()
        {
            var connection = new FakeConnectionEvents(0, isHost: true);
            var factory = new TrackingFactory(_catalog);
            using var presence = new PlayerPresenceService(connection, factory, new SessionRoster(_policy));
            connection.Connect(0, "Host");
            connection.Connect(1, "Mate");
            connection.Connect(2, "Third");
            Assert.AreEqual(3, presence.Entities.Count);
            foreach (var entity in presence.Entities.Values)
            {
                AssertComposedOnce(entity.GameObject, entity.Identity.DisplayName);
                Assert.AreEqual(entity.IsLocalOwner, entity.GameObject.GetComponent<PlayerInput>() != null, "owner/non-owner input isolation unchanged");
            }

            connection.Disconnect(2);
            Assert.AreEqual(1, factory.Despawns);
            Assert.AreEqual(2, Object.FindObjectsByType<HeldWeaponVisual>(FindObjectsSortMode.None).Length, "despawn removed the replica's visuals with it");
        }

        [Test]
        public void Reconnect_KeepsTheSameEntityAndItsVisuals_WithoutRecomposing()
        {
            var connection = new FakeConnectionEvents(0, isHost: true);
            var factory = new TrackingFactory(_catalog);
            var grace = new ReconnectGraceService(60f, () => true, () => 0d);
            using var presence = new PlayerPresenceService(connection, factory, new SessionRoster(_policy), grace);
            connection.Connect(0, "Host");
            connection.Connect(1, "Mate");
            var mate = presence.Entities[1];
            var body = CharacterVisual.RendererOf(mate.GameObject);
            connection.Disconnect(1);
            Assert.AreEqual(0, factory.Despawns, "held under the reconnect grace");
            connection.Reconnect(7, mate.ReconnectToken, "Mate");
            Assert.AreEqual(1, presence.ReconnectCount);
            Assert.AreSame(mate, presence.Entities[7]);
            Assert.AreSame(body, CharacterVisual.RendererOf(mate.GameObject), "the same body renderer survives the reconnect");
            AssertComposedOnce(mate.GameObject, "reconnected mate");
        }

        [Test]
        public void Client_SeesTheHostsReplica_ThroughTheNetworkPlayerObjectComposer_AndTheReplicatedWeaponId()
        {
            // The NGO prefab as a client would instantiate it (no NetworkManager here: the composer seam is what is under test).
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Network/PlayerNetworkEntity.prefab");
            Assert.IsNotNull(prefab, "release network player prefab");
            var replica = Object.Instantiate(prefab);
            _created.Add(replica);
            var networkObject = replica.GetComponent<NetworkPlayerObject>();
            Assert.IsNotNull(networkObject);
            // A pure replica: the null reader on its consumers, no loadout of its own.
            replica.GetComponent<PlayerAiming>().SetInputReader(NullPlayerInputReader.Instance);

            networkObject.ComposeVisuals();
            Assert.AreEqual(1, networkObject.VisualCompositions);
            AssertComposedOnce(replica, "host replica on a client");
            networkObject.ComposeVisuals();
            AssertComposedOnce(replica, "composing again on respawn");

            // The replicated active weapon id drives the replica's held sprite (presentation only).
            var view = replica.GetComponent<IHeldWeaponView>();
            Assert.IsNotNull(view);
            view.ShowWeapon("weapon_p9_ranger");
            var held = replica.GetComponent<HeldWeaponVisual>();
            Assert.IsTrue(held.IsVisible, "remote active weapon visible");
            Assert.AreSame(_catalog.WeaponSpriteFor("weapon_p9_ranger"), held.Renderer.sprite);
            view.ShowWeapon("weapon_field_knife");
            Assert.AreSame(_catalog.WeaponSpriteFor("weapon_field_knife"), held.Renderer.sprite, "follows the host's slot change");
            view.ShowWeapon(string.Empty);
            Assert.IsFalse(held.IsVisible, "empty hands when the host holds nothing");

            // Replicated aim turns the body and the weapon pivot without any local input.
            var aiming = replica.GetComponent<PlayerAiming>();
            aiming.ApplyReplicatedAim(Vector2.up);
            Assert.AreEqual(BodyFacing8.N, aiming.BodyFacing);
            Assert.AreEqual(90f, held.Pivot.rotation.eulerAngles.z, 0.5f, "pivot follows the replicated aim");
            aiming.ApplyReplicatedAim(Vector2.left);
            Assert.AreEqual(BodyFacing8.W, aiming.BodyFacing);
        }

        [Test]
        public void ReplicatedAim_NeverOverridesAnOwnerWithRealInput()
        {
            var reader = new FakePlayerInputReader { Aim = Vector2.right };
            var owner = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = "Owner", IsLocal = true, InputReader = reader, BalanceConfig = _catalog.PlayerBalance });
            _created.Add(owner);
            var aiming = owner.GetComponent<PlayerAiming>();
            aiming.ApplyReplicatedAim(Vector2.up);
            Assert.AreNotEqual(BodyFacing8.N, aiming.BodyFacing, "an owner's own aim is authoritative for its body");
        }

        [Test]
        public void WeaponNetState_CarriesTheActiveWeaponId_ForReplicasWithoutALoadout()
        {
            var reader = new FakePlayerInputReader();
            var go = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = "HostSide", IsLocal = true, InputReader = reader, BalanceConfig = _catalog.PlayerBalance });
            _created.Add(go);
            var loadout = go.AddComponent<WeaponLoadout>();
            var knife = go.AddComponent<MeleeWeapon>();
            knife.SetDefinition(_catalog.BuildRegistry().Definitions.OfType<MeleeWeaponDefinition>().First(d => d.Id == "weapon_field_knife"));
            loadout.SetPrimary(knife);
            loadout.Initialize();
            var state = WeaponStateSync.Capture(loadout);
            Assert.AreEqual("weapon_field_knife", state.ActiveWeaponId.ToString());
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Co-op runtime composition completion: the host world and the client world talking over the in-process loopback
    /// bus (the same runtime classes the shipped player runs over NGO; the built-player proof covers the real transport).
    /// Room activation/clear, enemy replication and death, hits as validated requests, loot presentation, the host-decided
    /// Transit, per-member economy, the local-interaction filter, the owned-object rig attach and the member mirror.
    /// </summary>
    public class CoopExpeditionRuntimeTests
    {
        private readonly List<UnityEngine.Object> _created = new();
        private readonly List<IDisposable> _disposables = new();
        private List<EnemyDefinition> _archetypes;
        private GameContentCatalog _content;

        [SetUp]
        public void SetUp()
        {
            _content = GameContentCatalog.Load();
            _archetypes = AssetDatabase.FindAssets("t:EnemyDefinition", new[] { "Assets/Game/ScriptableObjects/Enemies" })
                .Select(g => AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var d in _disposables) d.Dispose();
            _disposables.Clear();
            DamageAuthority.LocalIsAuthoritative = true;
            DamageAuthority.ClearRelays();
            RuinRail.Gameplay.Combat.Impact.ImpactDispatcher.RemoteImpactRelay = null;
            PlayerInteractor.LocalInteractionFilter = null;
            PickupArbiter.Clear();
            RuinRail.Core.Input.GameplayInputGate.Reset();
            foreach (var o in _created) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            foreach (var enemy in UnityEngine.Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (enemy != null) UnityEngine.Object.DestroyImmediate(enemy.gameObject);
            foreach (var replica in UnityEngine.Object.FindObjectsByType<EnemyReplica>(FindObjectsSortMode.None)) if (replica != null) UnityEngine.Object.DestroyImmediate(replica.gameObject);
            _created.Clear();
        }

        private T Track<T>(T o) where T : UnityEngine.Object { _created.Add(o); return o; }
        private T Own<T>(T d) where T : IDisposable { _disposables.Add(d); return d; }

        private static void Set(object target, string field, object value)
        {
            var type = target.GetType();
            FieldInfo info = null;
            while (type != null && info == null) { info = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance); type = type.BaseType; }
            info.SetValue(target, value);
        }

        private RoomRoot CreateRoom(RoomType type, Vector2Int size, IEnumerable<Vector2Int> enemySpawns, Vector2 worldOffset, string id = null)
        {
            var definition = Track(ScriptableObject.CreateInstance<RoomDefinition>());
            Set(definition, "_id", id ?? $"coop_{type}");
            Set(definition, "_roomType", type);
            var go = Track(new GameObject($"CoopRoom_{type}"));
            go.transform.position = worldOffset;
            go.AddComponent<UnityEngine.Grid>();
            var root = go.AddComponent<RoomRoot>();
            root.Configure(definition, size);
            foreach (var cell in enemySpawns)
            {
                var marker = new GameObject("EnemySpawn").AddComponent<RoomMarker>();
                marker.transform.SetParent(go.transform, false);
                marker.Configure(RoomMarkerRole.EnemySpawn, cell);
                marker.SnapToGrid();
            }

            var north = new GameObject("Door_N").AddComponent<DoorSocket>();
            north.transform.SetParent(go.transform, false);
            north.Configure(DoorDirection.North, new Vector2Int(size.x / 2 - 1, size.y - 1));
            north.SnapToGrid();
            return root;
        }

        private EncounterPlan Plan(int count)
        {
            var grunt = _archetypes.Single(a => a.Id == "grunt");
            var context = new EncounterContext(7, 1, 2, Biome.RuinedMetro, 3);
            return new EncounterPlan(context, 2f, 3f, 2f, new[] { new EncounterEntry(grunt, count) });
        }

        private GameObject Member(PartyLifeRoster roster, string participantId, Vector2 position)
        {
            return Track(PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options
            {
                Name = "Member_" + participantId, IsLocal = false,
                BalanceConfig = _content.PlayerBalance, Caps = _content.StatCaps,
                Position = position, LifeRoster = roster, ParticipantId = participantId
            }));
        }

        private sealed class World
        {
            public LoopbackCoopBus HostBus;
            public LoopbackCoopBus ClientBus;
            public LoopbackCoopBus.LoopbackNetwork Network;
            public CoopHostWorld Host;
            public CoopClientWorld Client;
            public RoomRuntime HostRoom;
            public RoomRuntime ClientRoom;
            public GameObject HostPlayer;
            public GameObject ClientCopy;
            public GroundLootRegistry HostGround;
            public LootSpawner ClientLoot;
        }

        /// <summary>A host with one combat room and a client with its own copy of the same room (same node id), over loopback.</summary>
        private World Compose(int enemies = 2, CoopHostParty party = null, Func<string, ItemDefinition> resolve = null, LootAuthorityService loot = null, ExpeditionService expedition = null)
        {
            var w = new World();
            w.HostBus = LoopbackCoopBus.CreateHost(out w.Network);
            w.ClientBus = LoopbackCoopBus.Join(w.Network, 1);
            var roster = new PartyLifeRoster();
            var hostRoot = CreateRoom(RoomType.Combat, new Vector2Int(16, 12), new[] { new Vector2Int(2, 2), new Vector2Int(13, 9), new Vector2Int(13, 2) }, new Vector2(0f, 3000f));
            w.HostRoom = hostRoot.gameObject.AddComponent<RoomRuntime>();
            w.HostRoom.Configure(hostRoot, 4, 1, 2);
            w.HostRoom.SetEncounter(Plan(enemies), new DefaultEnemySpawner());
            var clientRoot = CreateRoom(RoomType.Combat, new Vector2Int(16, 12), new[] { new Vector2Int(2, 2) }, new Vector2(200f, 3000f));
            w.ClientRoom = clientRoot.gameObject.AddComponent<RoomRuntime>();
            w.ClientRoom.Configure(clientRoot, 4, 1, 2);
            w.ClientRoom.SetEncounter(Plan(enemies), new AuthoritativeEnemySpawner(new DefaultEnemySpawner(), new CoopClientAuthority()));
            var origin = (Vector2)hostRoot.transform.position;
            w.HostPlayer = Member(roster, "host", origin + new Vector2(3f, 3f));
            w.ClientCopy = Member(roster, "client", origin + new Vector2(4f, 3f));
            party ??= new CoopHostParty
            {
                EntityOf = id => id == 0 ? w.HostPlayer : id == 1 ? w.ClientCopy : null,
                ParticipantOf = id => id == 0 ? "host" : id == 1 ? "client" : null,
                MaxHitOf = _ => 200
            };
            w.HostGround = new GroundLootRegistry();
            w.Host = Own(new CoopHostWorld(w.HostBus, party, loot, expedition, resolve));
            w.Client = Own(new CoopClientWorld(w.ClientBus));
            var lootHost = Track(new GameObject("ClientLoot"));
            w.ClientLoot = lootHost.AddComponent<LootSpawner>();
            w.ClientLoot.SetRegistry(new GroundLootRegistry());
            w.Client.BindDepth(1, new Dictionary<int, RoomRuntime> { [4] = w.ClientRoom }, w.ClientLoot, id => _content.Items.FirstOrDefault(i => i.Id == id));
            w.Host.BindDepth(1, new Dictionary<int, RoomRuntime> { [4] = w.HostRoom }, w.HostGround, new DungeonSyncPayload { RunSeed = 7, Depth = 1, IsValid = true }, w.HostPlayer);
            // The client reports its depth; the host releases gameplay once every peer is in.
            w.Client.ReportDepth(1, true, "fp", null);
            w.Host.Tick(0.01f);
            return w;
        }

        private static void Pump(World w, int frames = 1)
        {
            for (var i = 0; i < frames; i++)
            {
                w.Host.Tick(0.1f);
                w.Client.Tick(0.1f);
            }
        }

        // ---------------------------------------------------------------- rooms / enemies

        [UnityTest]
        public IEnumerator HostRoomActivation_EnemySet_Deaths_AndClear_ReachTheClientOnce()
        {
            var w = Compose(3);
            Assert.AreEqual(1, w.Host.GameplayReleases, "every peer reported the depth: gameplay released together");
            Assert.IsFalse(w.ClientRoom.IsAuthoritative, "the client's rooms only mirror the host");
            var clientClears = 0;
            w.ClientRoom.Cleared += (_, _) => clientClears++;

            // Two members enter the host's room: one activation, one spawn set.
            Assert.IsTrue(w.HostRoom.NotifyPlayerEntered(w.HostPlayer));
            Assert.IsFalse(w.HostRoom.NotifyPlayerEntered(w.ClientCopy), "the second member never re-activates");
            yield return null;
            Pump(w, 3);
            yield return null;
            var hostEnemies = UnityEngine.Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Where(e => e.IsAlive).ToList();
            Assert.AreEqual(3, hostEnemies.Count);
            Assert.AreEqual(3, w.Client.Replicas.Replicas.Count, "one replica per host actor");
            Assert.IsTrue(w.Client.Replicas.Replicas.Values.All(r => r.GetComponent<EnemyController>() == null && r.GetComponent<Rigidbody2D>() == null), "replicas carry no AI and no body");
            Assert.AreEqual(RoomLifecycleState.Active, w.ClientRoom.Lifecycle, "the client sees the room active");
            Assert.IsTrue(w.ClientRoom.DoorsLocked, "the client sees the doors locked");
            Assert.AreEqual(0, UnityEngine.Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Count(e => w.ClientRoom.InteriorWorldBounds.Contains(e.transform.position)), "the client spawned nothing");

            // Motion reaches the replicas.
            Assert.Greater(w.Client.EnemyStatesApplied, 0);

            // A resync (reconnect) never duplicates a replica.
            w.Host.ServeResync(1);
            Assert.AreEqual(3, w.Client.Replicas.Replicas.Count);

            // The host kills everything: deaths once, the clear once, doors open, XP to the client once per kill.
            var xp = 0;
            w.Client.Xp += m => xp += m.Amount;
            foreach (var enemy in hostEnemies) enemy.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(99999));
            yield return null;
            Pump(w, 3);
            yield return null;
            Assert.AreEqual(RoomLifecycleState.Cleared, w.HostRoom.Lifecycle);
            Assert.AreEqual(RoomLifecycleState.Cleared, w.ClientRoom.Lifecycle);
            Assert.IsFalse(w.ClientRoom.DoorsLocked);
            Assert.AreEqual(1, clientClears, "the client's clear is raised exactly once");
            Assert.IsTrue(w.Client.Replicas.Replicas.Values.All(r => r == null || r.IsDead));
            Assert.AreEqual(3, w.Client.EnemyDeaths);
            Assert.AreEqual(hostEnemies.Sum(e => e.XpValue), xp, "each kill's XP reaches the client once");

            // Stale or repeated room records change nothing.
            w.Host.ServeResync(1);
            Assert.AreEqual(1, clientClears);
        }

        // ---------------------------------------------------------------- hits as requests

        [UnityTest]
        public IEnumerator ClientHit_IsARequest_TheHostValidatesAndApplies_ExploitsAreRefused()
        {
            var w = Compose(1);
            w.HostRoom.NotifyPlayerEntered(w.HostPlayer);
            yield return null;
            Pump(w, 2);
            var enemy = UnityEngine.Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Single(e => e.IsAlive);
            var health = enemy.GetComponent<HealthComponent>();
            var replica = w.Client.Replicas.Replicas.Values.Single();
            replica.ConfigureCombatPresence(CoopActorKind.Normal, 4, enemy.Definition, null);
            var localPlayer = Track(new GameObject("ClientLocalPlayer"));
            w.Client.InstallRelays(localPlayer);

            // On the client process a hit on the replica is forwarded, never applied.
            w.HostBus.Queued = true;
            DamageAuthority.LocalIsAuthoritative = false;
            var before = health.CurrentHealth;
            var replicaBefore = replica.Health.CurrentHealth;
            Assert.IsTrue(replica.Health.TryApplyDamage(new DamageRequest(7)), "the hit landed (forwarded)");
            Assert.AreEqual(replicaBefore, replica.Health.CurrentHealth, "the replica's health is never changed locally");
            DamageAuthority.LocalIsAuthoritative = true;
            w.Network.Pump();
            w.HostBus.Queued = false;
            Assert.AreEqual(before - 7, health.CurrentHealth, "the host applied the validated request");
            Assert.AreEqual(1, w.Host.HitsApplied);

            uint id = replica.NetId;
            Assert.IsFalse(w.Host.HandleHit(9, new HitRequestMessage { NetId = id, Amount = 5 }), "unknown sender");
            Assert.IsFalse(w.Host.HandleHit(1, new HitRequestMessage { NetId = 999, Amount = 5 }), "unknown target");
            Assert.IsFalse(w.Host.HandleHit(1, new HitRequestMessage { NetId = id, Amount = 100000 }), "a hit larger than the sender's weapons allow");
            Assert.IsFalse(w.Host.HandleHit(1, new HitRequestMessage { NetId = id, Amount = -3 }), "a negative hit");
            w.ClientCopy.transform.position += new Vector3(500f, 0f, 0f);
            Assert.IsFalse(w.Host.HandleHit(1, new HitRequestMessage { NetId = id, Amount = 5 }), "out of reach of the sender's host position");
            w.ClientCopy.transform.position -= new Vector3(500f, 0f, 0f);
            var life = w.ClientCopy.GetComponent<PlayerLifeStateComponent>();
            w.ClientCopy.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(100000));
            yield return null;
            Assert.IsFalse(life.IsAlive);
            Assert.IsFalse(w.Host.HandleHit(1, new HitRequestMessage { NetId = id, Amount = 5 }), "a Downed/Dead sender cannot attack");
            Assert.AreEqual(before - 7, health.CurrentHealth, "no refused request changed the enemy");
            Assert.GreaterOrEqual(w.Host.HitsRejected, 6);

            // A heal request only ever heals the sender's own character.
            Assert.IsFalse(w.Host.HandleHeal(9, new HealRequestMessage { Amount = 10 }));
        }

        // ---------------------------------------------------------------- grenade presentation

        [UnityTest]
        public IEnumerator Grenades_AreShownToEveryOtherPeer_FromTheHostsValidation_AndNeverSimulatedTwice()
        {
            var w = Compose(0, resolve: id => _content.Items.FirstOrDefault(i => i.Id == id));
            var otherBus = LoopbackCoopBus.Join(w.Network, 2);
            var other = Own(new CoopClientWorld(otherBus));
            var hostShown = new List<GrenadeShowMessage>();
            var throwerShown = new List<GrenadeShowMessage>();
            var otherShown = new List<GrenadeShowMessage>();
            w.Host.GrenadeShown += hostShown.Add;
            w.Client.GrenadeShown += throwerShown.Add;
            other.GrenadeShown += otherShown.Add;
            var grenades = _content.Items.OfType<RuinRail.Gameplay.Items.Consumables.ConsumableDefinition>()
                .Where(c => c.EffectKind == RuinRail.Gameplay.Items.Consumables.ConsumableEffectKind.Grenade).ToList();
            Assert.AreEqual(4, grenades.Count);
            var origin = (Vector2)w.ClientCopy.transform.position;

            // Every shipped grenade from the client: the host draws it and forwards it to the other client, never back.
            foreach (var grenade in grenades)
            {
                var landing = origin + Vector2.right * (grenade.Grenade.ThrowRangeTiles - 0.5f);
                w.Client.AnnounceGrenade(grenade.Id, origin, landing);
                yield return new WaitForSecondsRealtime(CoopHostWorld.GrenadeMinIntervalSeconds + 0.02f);
            }

            Assert.AreEqual(4, hostShown.Count, "the host draws each validated throw once");
            Assert.AreEqual(4, otherShown.Count, "the other client draws each throw once");
            Assert.AreEqual(0, throwerShown.Count, "the thrower is never sent its own throw (it draws it locally)");
            CollectionAssert.AreEqual(grenades.Select(g => g.Id), otherShown.Select(s => s.ConsumableId));
            Assert.IsTrue(otherShown.All(s => s.Thrower == 1));
            Assert.AreEqual(origin.x + grenades[0].Grenade.ThrowRangeTiles - 0.5f, otherShown[0].LandingX, 1e-4f, "the host's landing is shown");
            Assert.AreEqual(0, UnityEngine.Object.FindObjectsByType<RuinRail.Gameplay.Combat.Area.ThrownGrenade>(FindObjectsSortMode.None).Length, "no grenade is simulated for a shown throw");
            Assert.AreEqual(0, UnityEngine.Object.FindObjectsByType<RuinRail.Gameplay.Combat.Area.SmokeZone>(FindObjectsSortMode.None).Length, "no smoke zone either");
            Assert.AreEqual(0, UnityEngine.Object.FindObjectsByType<RuinRail.Gameplay.Combat.Area.BurnZone>(FindObjectsSortMode.None).Length, "no burn zone either");

            // Refused throws are drawn nowhere.
            var frag = grenades.First(g => g.Grenade.Kind == RuinRail.Gameplay.Items.Consumables.GrenadeEffectKind.Frag);
            GrenadeThrowMessage Throw(uint seq, string id, Vector2 from, Vector2 to) =>
                new() { Sequence = seq, ConsumableId = id, OriginX = from.x, OriginY = from.y, LandingX = to.x, LandingY = to.y };
            yield return new WaitForSecondsRealtime(CoopHostWorld.GrenadeMinIntervalSeconds + 0.02f);
            Assert.IsFalse(w.Host.HandleGrenadeThrow(1, Throw(4, frag.Id, origin, origin + Vector2.up)), "a replayed sequence");
            Assert.IsFalse(w.Host.HandleGrenadeThrow(9, Throw(50, frag.Id, origin, origin + Vector2.up)), "an unknown sender");
            Assert.IsFalse(w.Host.HandleGrenadeThrow(1, Throw(50, "consumable_medkit", origin, origin + Vector2.up)), "not a grenade");
            Assert.IsFalse(w.Host.HandleGrenadeThrow(1, Throw(50, frag.Id, origin, origin + Vector2.up * 40f)), "beyond the throw range");
            Assert.IsFalse(w.Host.HandleGrenadeThrow(1, Throw(50, frag.Id, origin + Vector2.right * 20f, origin + Vector2.right * 22f)), "not thrown from the member's position");
            Assert.IsTrue(w.Host.HandleGrenadeThrow(1, Throw(50, frag.Id, origin, origin + Vector2.up)), "a valid throw");
            Assert.IsFalse(w.Host.HandleGrenadeThrow(1, Throw(51, frag.Id, origin, origin + Vector2.up)), "a flood faster than any use");
            w.ClientCopy.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(100000));
            yield return new WaitForSecondsRealtime(CoopHostWorld.GrenadeMinIntervalSeconds + 0.02f);
            Assert.IsFalse(w.Host.HandleGrenadeThrow(1, Throw(60, frag.Id, origin, origin + Vector2.up)), "a Downed/Dead member throws nothing");
            Assert.AreEqual(5, otherShown.Count, "only the one valid throw reached the other client");
            Assert.AreEqual(7, w.Host.GrenadesRejected, "replay, unknown sender, not a grenade, range, origin, flood, Downed");

            // The host's own throw: every client draws it; the host does not draw it twice.
            var hostOrigin = (Vector2)w.HostPlayer.transform.position;
            w.Host.AnnounceLocalGrenade(frag.Id, hostOrigin, hostOrigin + Vector2.left * 3f);
            Assert.AreEqual(1, throwerShown.Count, "client 1 draws the host's throw");
            Assert.AreEqual(6, otherShown.Count, "client 2 draws the host's throw");
            Assert.AreEqual(0, throwerShown[0].Thrower);
            Assert.AreEqual(5, hostShown.Count, "the host draws its own throw locally, not through the relay");
        }

        [Test]
        public void HitRate_IsBounded_PerSender()
        {
            var w = Compose(0);
            var dummy = Track(new GameObject("Dummy"));
            dummy.transform.position = w.ClientCopy.transform.position + Vector3.right;
            var id = w.Host.Register(dummy, 4, false);
            Assert.AreEqual(0u, id, "only enemies are replicated actors");
            var enemy = new DefaultEnemySpawner().Spawn(_archetypes.Single(a => a.Id == "grunt"), (Vector2)w.ClientCopy.transform.position + Vector2.right, null);
            Track(enemy.gameObject);
            enemy.GetComponent<HealthComponent>().SetMaxHealth(100000);
            id = w.Host.Register(enemy.gameObject, 4, true);
            var accepted = 0;
            for (var i = 0; i < CoopHostWorld.MaxHitsPerSecond + 20; i++) if (w.Host.HandleHit(1, new HitRequestMessage { NetId = id, Amount = 1 })) accepted++;
            Assert.AreEqual(CoopHostWorld.MaxHitsPerSecond, accepted, "a flood beyond the per-second ceiling is refused");
        }

        // ---------------------------------------------------------------- loot presentation

        [UnityTest]
        public IEnumerator ClientLoot_IsPresentationOfTheHosts_AndResolvesNothingLocally()
        {
            var w = Compose(0);
            PickupArbiter.Items = (_, _) => false;
            PickupArbiter.Coins = (_, _) => false;
            var hostLootHost = Track(new GameObject("HostLoot"));
            var hostSpawner = hostLootHost.AddComponent<LootSpawner>();
            hostSpawner.SetRegistry(w.HostGround);
            var consumable = _content.Items.OfType<RuinRail.Gameplay.Items.Consumables.ConsumableDefinition>().First();
            var pickup = hostSpawner.CreateItemPickup(new Vector2(3f, 3003f));
            pickup.Hold(new ItemInstance(consumable.Id, 1), consumable.Category);
            var coins = hostSpawner.CreateCoinPickup(new Vector2(4f, 3003f));
            coins.SetAmount(50);
            Pump(w, 2);
            Assert.AreEqual(2, w.Client.Loot.Count, "every host pickup appears once on the client");
            w.Host.ServeResync(1);
            Assert.AreEqual(2, w.Client.Loot.Count, "a resync never duplicates a pickup");
            var presentation = w.Client.Loot.Values.Select(go => go.GetComponent<WorldItemPickup>()).First(p => p != null);
            Assert.IsFalse(presentation.Interact(w.ClientCopy), "a client pickup resolves nothing locally");
            Assert.IsNotNull(presentation.Item, "and is not consumed by the attempt");
            UnityEngine.Object.Destroy(pickup.gameObject);
            yield return null;
            Pump(w, 2);
            yield return null;
            Assert.AreEqual(1, w.Client.Loot.Count, "a pickup consumed on the host disappears on the client");
        }

        // ---------------------------------------------------------------- transit

        [Test]
        public void ClientTransitDecision_ResolvesOnlyFromTheHost_Once()
        {
            var decision = new TransitDecision(new HostDecidedTransitPolicy(), new[] { "a", "b" });
            decision.Open();
            var submitted = new List<string>();
            decision.Submitted += (_, id, _) => submitted.Add(id);
            var resolutions = 0;
            decision.Resolved += (_, _) => resolutions++;
            Assert.IsFalse(decision.Submit("a", TransitChoice.DescendDeeper), "a client's own vote never resolves locally");
            Assert.IsTrue(decision.MirrorVote("b", TransitChoice.DescendDeeper), "another member's vote is mirrored for display");
            Assert.AreEqual(TransitDecisionState.Open, decision.State, "even with every vote in, only the host decides");
            CollectionAssert.AreEqual(new[] { "a" }, submitted, "only this player's own submission is announced for forwarding");
            Assert.IsTrue(decision.ResolveFromAuthority(TransitChoice.DescendDeeper));
            Assert.IsFalse(decision.ResolveFromAuthority(TransitChoice.ReturnToShelter), "a repeated result is ignored");
            Assert.AreEqual(1, resolutions);
            Assert.AreEqual(TransitChoice.DescendDeeper, decision.Result);
        }

        [Test]
        public void HostVote_CountsTheSendersOwnVoteOnce_DeadAndUnknownHaveNone()
        {
            var expedition = new ExpeditionService(id => _content.Items.FirstOrDefault(i => i.Id == id), t => _content.Items.OfType<AmmoItemDefinition>().FirstOrDefault(a => a.AmmoType == t), _content.AmmoBalance);
            expedition.Start(new PlayerProfile(), 5, Biome.RuinedMetro, 2, "host");
            expedition.SetPartyTransit(() => new[] { "host", "client" }, () => Array.Empty<string>(), new PartyTransitPolicy());
            var bus = LoopbackCoopBus.CreateHost(out var network);
            LoopbackCoopBus.Join(network, 1);
            var world = Own(new CoopHostWorld(bus, new CoopHostParty { ParticipantOf = id => id == 0 ? "host" : id == 1 ? "client" : null }, null, expedition, null));
            var decision = expedition.RecordBossDefeated(10);
            Assert.IsFalse(world.HandleVote(7, new VoteRequestMessage { Choice = (int)TransitChoice.DescendDeeper }), "an unknown sender has no vote");
            Assert.IsTrue(world.HandleVote(1, new VoteRequestMessage { Choice = (int)TransitChoice.DescendDeeper }));
            Assert.IsTrue(world.HandleVote(1, new VoteRequestMessage { Choice = (int)TransitChoice.DescendDeeper }), "a repeated vote is the same vote");
            Assert.AreEqual(1, decision.Choices.Count, "one counted vote per member");
            Assert.AreEqual(TransitDecisionState.Open, decision.State, "the host has not voted: no transition");
            decision.Submit("host", TransitChoice.DescendDeeper);
            Assert.AreEqual(TransitDecisionState.Resolved, decision.State);
            Assert.AreEqual(2, expedition.State.Depth, "the party descends exactly once");
            Assert.AreEqual(1, world.TransitResolutionsSent, "the result is published once");
        }

        [Test]
        public void ClientExpedition_StartsItsOwnTransaction_UnderTheHostAssignedId()
        {
            var expedition = new ExpeditionService(id => _content.Items.FirstOrDefault(i => i.Id == id), t => null, _content.AmmoBalance);
            var coordinator = new ExpeditionStartCoordinator();
            var snapshot = new ExpeditionStartSnapshot { StartTransactionId = "start-1", RunSeed = 42, Biome = (int)Biome.Rustworks, PartySize = 2 };
            var state = coordinator.Apply(snapshot, expedition, new PlayerProfile(), "participant-abc");
            Assert.AreEqual("participant-abc", state.TransactionId);
            Assert.AreEqual(42, state.RunSeed);
            Assert.AreEqual(Biome.Rustworks, state.Biome);
            Assert.AreEqual(2, state.StartingPartySize);
            Assert.AreSame(state, coordinator.Apply(snapshot, expedition, new PlayerProfile(), "participant-abc"), "a re-delivered start never starts a second transaction");
            Assert.AreEqual(1, coordinator.Applied);
        }

        // ---------------------------------------------------------------- economy

        private DungeonMerchantService Merchant(CoinWallet composed)
        {
            return new DungeonMerchantService(_content.Merchant, new PriceService(_content.Economy), composed, new DungeonMerchantState(1), 11, 2,
                _content.Items, id => _content.Items.FirstOrDefault(i => i.Id == id), _content.Loot.RarityTableFor);
        }

        [Test]
        public void MerchantTrades_UseTheMembersOwnWallet_OnceForTheParty()
        {
            var registry = _content.BuildRegistry();
            var loot = new LootAuthorityService(CoopHostAuthority.Instance);
            var hostInventory = PlayerInventory.FromRegistry(registry, _content.AmmoBalance);
            var clientInventory = PlayerInventory.FromRegistry(registry, _content.AmmoBalance);
            var hostWallet = new CoinWallet(CoinDomain.Carried, 5000);
            var clientWallet = new CoinWallet(CoinDomain.Carried, 5000);
            loot.RegisterParticipant(new LootParticipant(0, "host", new BackpackContainer(hostInventory), hostWallet));
            loot.RegisterParticipant(new LootParticipant(1, "client", new BackpackContainer(clientInventory), clientWallet));
            var merchant = Merchant(hostWallet);
            var offer = merchant.Offers.First(o => !o.IsSold);

            var bought = loot.RequestMerchantBuy("tx-1", 1, merchant, offer.Index);
            Assert.AreEqual(LootVerdict.Accepted, bought.Verdict);
            Assert.AreEqual(5000 - offer.Price, clientWallet.Balance, "the buyer pays from its own Carried wallet");
            Assert.AreEqual(5000, hostWallet.Balance, "never from the host's");
            Assert.AreEqual(LootVerdict.Accepted, loot.RequestMerchantBuy("tx-1", 1, merchant, offer.Index).Verdict, "a replayed transaction returns the stored result");
            Assert.AreEqual(5000 - offer.Price, clientWallet.Balance, "and never charges twice");
            Assert.AreEqual(LootVerdict.AlreadyTaken, loot.RequestMerchantBuy("tx-2", 0, merchant, offer.Index).Verdict, "a sold offer stays sold for the party");
            Assert.AreEqual(5000, hostWallet.Balance, "the refused buyer is not charged");

            var carried = clientInventory.BackpackSlots.First(i => i != null);
            var value = merchant.QuoteSellValue(carried);
            var sold = loot.RequestMerchantSell("tx-3", 1, merchant, carried.InstanceId);
            Assert.AreEqual(value > 0 ? LootVerdict.Accepted : LootVerdict.Rejected, sold.Verdict);
            Assert.AreEqual(LootVerdict.Rejected, loot.RequestMerchantSell("tx-4", 0, merchant, carried.InstanceId).Verdict, "nobody sells another member's item");
        }

        // ---------------------------------------------------------------- local interaction filter

        [Test]
        public void ClientInteractionFilter_LetsOnlyLocalPresentationRun()
        {
            var roster = new PartyLifeRoster();
            var player = Member(roster, "p", new Vector2(0f, 4000f));
            var interactor = player.GetComponent<PlayerInteractor>();
            var chestGo = Track(new GameObject("Chest"));
            chestGo.transform.position = new Vector2(0.5f, 4000f);
            chestGo.AddComponent<BoxCollider2D>().isTrigger = true;
            var probe = chestGo.AddComponent<ProbeInteractable>();
            Physics2D.SyncTransforms();
            PlayerInteractor.LocalInteractionFilter = (target, _) => target is DungeonMerchantInteractable;
            Assert.IsFalse(interactor.TryInteract(), "a commit-type interaction is the host's");
            Assert.AreEqual(0, probe.Interactions);
            PlayerInteractor.LocalInteractionFilter = null;
            Assert.IsTrue(interactor.TryInteract(), "solo/host are unchanged");
            Assert.AreEqual(1, probe.Interactions);
        }

        private sealed class ProbeInteractable : MonoBehaviour, IInteractable
        {
            public int Interactions;
            public bool CanInteract(GameObject interactor) => true;
            public bool Interact(GameObject interactor) { Interactions++; return true; }
        }

        // ---------------------------------------------------------------- local player attach / member mirror

        [Test]
        public void PlayerRigAttach_ComposesOntoTheOwnedObject_WithoutASecondPlayer()
        {
            var registry = _content.BuildRegistry();
            var roster = new PartyLifeRoster();
            var owned = Track(PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = "Owned", IsLocal = false, BalanceConfig = _content.PlayerBalance, Caps = _content.StatCaps }));
            var inventory = PlayerInventory.FromRegistry(registry, _content.AmmoBalance);
            var weapon = _content.Items.OfType<RangedWeaponDefinition>().First();
            inventory.TryEquip(new ItemInstance(weapon.Id, 1), EquippedSlot.PrimaryWeapon);
            var state = new ExpeditionState(1, Biome.RuinedMetro, inventory, "pid-1", 2);
            var playersBefore = UnityEngine.Object.FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None).Length;
            var rig = Own(new PlayerRig(_content, registry, _content.BuildSpecials()));
            var player = rig.Attach(owned, state, roster, "pid-1");
            Assert.AreSame(owned, player, "the rig is composed onto the owned object");
            Assert.AreEqual(playersBefore, UnityEngine.Object.FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None).Length, "no second player entity");
            Assert.IsTrue(rig.IsAttached);
            Assert.AreEqual("pid-1", owned.GetComponent<PlayerLifeStateComponent>().ParticipantId);
            Assert.IsTrue(roster.Members.Contains(owned.GetComponent<PlayerLifeStateComponent>()), "one roster entry");
            Assert.IsNotNull(owned.GetComponent<RuinRail.Gameplay.Combat.Weapons.WeaponLoadout>(), "weapons mounted from the at-risk inventory");
            Assert.AreSame(inventory, owned.GetComponent<PlayerLootReceiver>().Inventory, "one inventory: the expedition's");
            Assert.AreSame(state.CarriedWallet, owned.GetComponent<PlayerLootReceiver>().Wallet, "one wallet: the expedition's");
            Assert.AreEqual(1, owned.GetComponents<PlayerInput>().Length, "one local input on the owned object");
            Assert.AreEqual(0, owned.GetComponentsInChildren<Camera>(true).Length + owned.GetComponentsInChildren<AudioListener>(true).Length, "no camera/listener on the character");
        }

        [Test]
        public void MemberMirror_TakesTheMembersLoadoutAndRanks_NewestSnapshotWins()
        {
            var registry = _content.BuildRegistry();
            var copy = Track(PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = "HostCopy", IsLocal = false, BalanceConfig = _content.PlayerBalance, Caps = _content.StatCaps }));
            var loadout = PlayerInventory.FromRegistry(registry, _content.AmmoBalance);
            var weapon = _content.Items.OfType<RangedWeaponDefinition>().OrderByDescending(w => w.DamageMax).First();
            loadout.TryEquip(new ItemInstance(weapon.Id, 1), EquippedSlot.PrimaryWeapon);
            var profile = new CoopMemberProfile { ClientId = 1, Loadout = loadout.ToSnapshot(), SkillRanks = new[] { 3, 0, 0, 0, 0, 0 } };
            var mirror = Own(new CoopMemberMirror(copy, 1, _content, registry, profile));
            var health = copy.GetComponent<HealthComponent>();
            Assert.AreEqual(_content.PlayerBalance.MaxHealth + 6, health.MaxHealth, "Vitality 3 (+2 HP per rank) reaches the host copy's max HP");
            Assert.AreEqual(health.MaxHealth, health.CurrentHealth, "a new expedition starts at the true maximum");
            Assert.GreaterOrEqual(mirror.MaxHit(), weapon.DamageMax, "hit validation allows the member's own weapon");
            Assert.AreSame(mirror.Inventory, copy.GetComponent<PlayerLootReceiver>().Inventory, "the loot authority checks this member's real backpack");
            var newer = PlayerInventory.FromRegistry(registry, _content.AmmoBalance).ToSnapshot();
            Assert.IsTrue(mirror.Apply(new InventorySnapshotMessage { Version = 2, Inventory = newer }));
            Assert.IsFalse(mirror.Apply(new InventorySnapshotMessage { Version = 1, Inventory = loadout.ToSnapshot() }), "an older snapshot never overwrites a newer one");
            Assert.IsNull(mirror.Inventory.GetEquipped(EquippedSlot.PrimaryWeapon));
        }

        [Test]
        public void MemberMirror_SeedsTheMembersCarriedWalletWithItsTakenCoins_Once_AndAReportOnlyLowersIt()
        {
            var registry = _content.BuildRegistry();
            PlayerInventory Kit()
            {
                var kit = PlayerInventory.FromRegistry(registry, _content.AmmoBalance);
                kit.TryEquip(new ItemInstance(_content.Items.OfType<RangedWeaponDefinition>().First().Id, 1), EquippedSlot.PrimaryWeapon);
                return kit;
            }

            // The member's own Start moved the full captured amount: the seed stands, a repeated report changes nothing.
            var copy = Track(PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = "HostCopyCoins", IsLocal = false, BalanceConfig = _content.PlayerBalance, Caps = _content.StatCaps }));
            var mirror = Own(new CoopMemberMirror(copy, 1, _content, registry, new CoopMemberProfile { ClientId = 1, Loadout = Kit().ToSnapshot() }, null, 300));
            var wallet = copy.GetComponent<PlayerLootReceiver>().Wallet;
            Assert.AreEqual(300, wallet.Balance, "the host holds the member's taken coins as its Carried Coins");
            Assert.IsTrue(mirror.Apply(new InventorySnapshotMessage { Version = 1, Inventory = Kit().ToSnapshot(), CoinsBroughtIn = 300 }));
            Assert.IsTrue(mirror.Apply(new InventorySnapshotMessage { Version = 2, Inventory = Kit().ToSnapshot(), CoinsBroughtIn = 999 }));
            Assert.AreEqual(300, wallet.Balance, "a report never raises the host's wallet");

            // The member's bank could only cover 120 (it spent between the capture and the start): the seed comes down once.
            var short1 = Track(PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = "HostCopyShort", IsLocal = false, BalanceConfig = _content.PlayerBalance, Caps = _content.StatCaps }));
            var shortMirror = Own(new CoopMemberMirror(short1, 2, _content, registry, new CoopMemberProfile { ClientId = 2, Loadout = Kit().ToSnapshot() }, null, 300));
            var shortWallet = short1.GetComponent<PlayerLootReceiver>().Wallet;
            Assert.IsTrue(shortMirror.Apply(new InventorySnapshotMessage { Version = 1, Inventory = Kit().ToSnapshot(), CoinsBroughtIn = -1 }), "an unreported amount leaves the seed");
            Assert.AreEqual(300, shortWallet.Balance);
            Assert.IsTrue(shortMirror.Apply(new InventorySnapshotMessage { Version = 2, Inventory = Kit().ToSnapshot(), CoinsBroughtIn = 120 }));
            Assert.AreEqual(120, shortWallet.Balance, "exactly what the member's bank paid");
            Assert.AreEqual(180, shortMirror.CoinsShortfallRemoved);
            Assert.IsTrue(shortMirror.Apply(new InventorySnapshotMessage { Version = 3, Inventory = Kit().ToSnapshot(), CoinsBroughtIn = 0 }));
            Assert.AreEqual(120, shortWallet.Balance, "reconciled once per run");

            // Nothing taken: nothing seeded.
            var none = Track(PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = "HostCopyNone", IsLocal = false, BalanceConfig = _content.PlayerBalance, Caps = _content.StatCaps }));
            Own(new CoopMemberMirror(none, 3, _content, registry, new CoopMemberProfile { ClientId = 3, Loadout = Kit().ToSnapshot() }));
            Assert.AreEqual(0, none.GetComponent<PlayerLootReceiver>().Wallet.Balance);
        }

        // ---------------------------------------------------------------- every actor type

        [UnityTest]
        public IEnumerator EveryArchetypeEliteAndBoss_ReplicatesSpawnStateAndDeath_Once()
        {
            var w = Compose(0);
            var rows = new List<string> { "actor,kind,definition,spawn_replicated,state_replicated,moveset_slots_host,moveset_slots_client_resolvable,animation_set,death_once,result" };
            var actors = new List<(GameObject go, string id, CoopActorKind kind, int slots)>();
            var x = 0f;
            Vector2 Next() { x += 12f; return new Vector2(x, 5000f); }
            foreach (var enemy in _content.Enemies.Where(e => e != null))
            {
                var controller = new DefaultEnemySpawner().Spawn(enemy, Next(), null);
                Track(controller.gameObject);
                actors.Add((controller.gameObject, enemy.Id, CoopActorKind.Normal, 0));
            }

            var eliteSpawner = new RuinRail.Gameplay.Enemies.Elites.DefaultEliteSpawner();
            foreach (var elite in _content.Elites.Where(e => e != null))
            {
                var encounter = eliteSpawner.Spawn(elite, Next(), null, null);
                Track(encounter.gameObject);
                actors.Add((encounter.Elite.gameObject, elite.Id, CoopActorKind.Elite, CoopHostWorld.MovesetOf(encounter.Elite).Count));
            }

            var bossSpawner = new RuinRail.Gameplay.Enemies.Bosses.DefaultBossSpawner(_content.Bosses);
            foreach (var boss in _content.Bosses.Where(b => b != null))
            {
                var encounter = bossSpawner.Spawn(boss, Next(), null);
                Track(encounter.gameObject);
                actors.Add((encounter.Boss.gameObject, boss.Id, CoopActorKind.Boss, CoopHostWorld.MovesetOf(encounter.Boss).Count));
            }

            Assert.AreEqual(9, actors.Count(a => a.kind == CoopActorKind.Normal), "9 normal archetypes");
            Assert.AreEqual(6, actors.Count(a => a.kind == CoopActorKind.Elite), "6 elite variants");
            Assert.AreEqual(6, actors.Count(a => a.kind == CoopActorKind.Boss), "6 bosses");
            var ids = actors.ToDictionary(a => a.go, a => w.Host.Register(a.go, 4, true));
            yield return null;
            Pump(w, 3);
            foreach (var actor in actors)
            {
                var id = ids[actor.go];
                Assert.IsTrue(w.Client.Replicas.TryGet(id, out var replica), actor.id + " replicated");
                Assert.AreEqual(actor.id, replica.DefinitionId);
                Assert.Greater(replica.AppliedStates, 0, actor.id + " state replicated");
                Assert.AreEqual(actor.kind != CoopActorKind.Normal, replica.IsMoveset, actor.id + " moveset state");
            }

            foreach (var actor in actors) actor.go.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(10000000));
            yield return null;
            Pump(w, 3);
            yield return null;
            foreach (var actor in actors)
            {
                var id = ids[actor.go];
                w.Client.Replicas.TryGet(id, out var replica);
                var died = replica == null || replica.IsDead;
                var clientSlots = actor.kind == CoopActorKind.Boss
                    ? _content.Bosses.First(b => b.Id == actor.id).Moveset.Count + (_content.Bosses.First(b => b.Id == actor.id).PhaseTwoArenaHazards?.Count ?? 0)
                    : actor.kind == CoopActorKind.Elite ? _content.Elites.First(e => e.Id == actor.id).Moveset.Count : 0;
                var animation = _content.AnimationSetFor(actor.id) != null;
                var ok = died && clientSlots == actor.slots;
                rows.Add($"{actor.id},{actor.kind},{actor.id},YES,YES,{actor.slots},{clientSlots},{(animation ? "YES" : "NO")},{(died ? "YES" : "NO")},{(ok ? "PASS" : "FAIL")}");
                Assert.IsTrue(ok, actor.id + $": died={died} slots host {actor.slots} client {clientSlots}");
            }

            Assert.AreEqual(actors.Count, w.Client.EnemyDeaths, "each death plays exactly once");
            System.IO.Directory.CreateDirectory("TestResults/CoopRuntimeCompletion");
            System.IO.File.WriteAllLines("TestResults/CoopRuntimeCompletion/enemy_actor_coverage.csv", rows);
        }

        [Test]
        public void Pickups_RespectEachMembersOwnCapacity_AndWallet()
        {
            var registry = _content.BuildRegistry();
            var loot = new LootAuthorityService(CoopHostAuthority.Instance);
            var full = PlayerInventory.FromRegistry(registry, _content.AmmoBalance);
            var consumable = _content.Items.OfType<RuinRail.Gameplay.Items.Consumables.ConsumableDefinition>().First();
            var weapon = _content.Items.OfType<RangedWeaponDefinition>().First();
            for (var i = 0; i < PlayerInventory.BackpackCapacity; i++) full.TryAddToBackpack(new ItemInstance(weapon.Id, 1));
            Assert.AreEqual(PlayerInventory.BackpackCapacity, full.BackpackSlots.Count(s => s != null));
            loot.RegisterParticipant(new LootParticipant(1, "client", new BackpackContainer(full), new CoinWallet(CoinDomain.Carried, 0)));
            var host = Track(new GameObject("Pickup"));
            var pickup = host.AddComponent<WorldItemPickup>();
            pickup.Hold(new ItemInstance(weapon.Id, 1), weapon.Category);
            var refused = loot.RequestPickup("tx-full", 1, pickup);
            Assert.AreEqual(LootVerdict.Rejected, refused.Verdict, "a full backpack cannot take the item");
            Assert.IsFalse(pickup.IsConsumed, "and the pickup stays on the ground for someone else (never hidden)");

            var poor = new CoinWallet(CoinDomain.Carried, 0);
            loot.RegisterParticipant(new LootParticipant(2, "poor", new BackpackContainer(PlayerInventory.FromRegistry(registry, _content.AmmoBalance)), poor));
            var merchant = Merchant(new CoinWallet(CoinDomain.Carried, 9999));
            var offer = merchant.Offers.First(o => !o.IsSold && o.Price > 0);
            var broke = loot.RequestMerchantBuy("tx-poor", 2, merchant, offer.Index);
            Assert.AreEqual(LootVerdict.Rejected, broke.Verdict);
            Assert.AreEqual(TradeError.InsufficientFunds.ToString(), broke.Detail, "insufficient funds refuses the buyer");
            Assert.IsFalse(offer.IsSold, "and the offer stays for the rest of the party");
        }

        // ---------------------------------------------------------------- messages

        [Test]
        public void ClientToHostKinds_AreTheOnlyOnesAHostAccepts()
        {
            foreach (var kind in new[] { CoopKinds.Hit, CoopKinds.Impact, CoopKinds.Heal, CoopKinds.Vote, CoopKinds.Buy, CoopKinds.Sell, CoopKinds.CacheChoose, CoopKinds.Drop, CoopKinds.InventorySnapshot, CoopKinds.Resync, CoopKinds.DepthReady, CoopKinds.LobbyMember })
                Assert.IsTrue(CoopKinds.IsClientToHost(kind), kind);
            foreach (var kind in new[] { CoopKinds.EnemySpawn, CoopKinds.RoomState, CoopKinds.LootSpawn, CoopKinds.Grant, CoopKinds.TransitResolved, CoopKinds.RunStart, CoopKinds.RunEnded, CoopKinds.GameplayRelease })
                Assert.IsFalse(CoopKinds.IsClientToHost(kind), kind + " can never be authored by a client");
        }

        // ---------------------------------------------------------------- 57.7 Secure Relay

        private static IReadOnlyList<IItemContainer> CarriedOf(PlayerInventory inventory)
        {
            var list = new List<IItemContainer> { new BackpackContainer(inventory) };
            foreach (EquippedSlot slot in Enum.GetValues(typeof(EquippedSlot))) list.Add(new EquippedSlotContainer(inventory, slot));
            return list;
        }

        [Test]
        public void SecureRelay_AcceptedTransfer_IsSecuredEvenWhenTheVerdictIsLost_ReconnectReplaysIt_AndNothingRepeats()
        {
            Func<string, ItemDefinition> resolve = id => _content.Items.FirstOrDefault(i => i.Id == id);
            var registry = _content.BuildRegistry();
            var weapons = _content.Items.OfType<WeaponDefinition>().Where(d => d.Id != StarterKitService.PistolId && d.Id != StarterKitService.KnifeId).Take(2).ToList();
            var armor = _content.Items.First(d => d.Category == ItemCategory.Armor && d.Id != StarterKitService.VestId);

            // The member's own run inventory, and the host's mirrored copy of it (same instances by id, as the mirror applies).
            var memberInventory = PlayerInventory.FromRegistry(registry, _content.AmmoBalance);
            var rifle = new ItemInstance(weapons[0].Id, 1, Rarity.Epic) { IsAtRisk = true };
            var vest = new ItemInstance(armor.Id, 1, Rarity.Rare) { IsAtRisk = true };
            memberInventory.TryEquip(rifle, EquippedSlot.PrimaryWeapon);
            memberInventory.TryAddToBackpack(vest);
            var hostCopy = PlayerInventory.FromRegistry(registry, _content.AmmoBalance);
            hostCopy.RestoreFromSnapshot(memberInventory.ToSnapshot());
            var hostOwn = PlayerInventory.FromRegistry(registry, _content.AmmoBalance);
            var hostWeapon = new ItemInstance(weapons[1].Id, 1, Rarity.Rare) { IsAtRisk = true };
            hostOwn.TryAddToBackpack(hostWeapon);
            var memberStorage = new Storage(resolve, _content.AmmoBalance);

            var loot = new LootAuthorityService(CoopHostAuthority.Instance);
            var hostCarried = CarriedOf(hostOwn);
            var copyCarried = CarriedOf(hostCopy);
            loot.RegisterParticipant(new LootParticipant(0, "host", hostCarried[0], new CoinWallet(CoinDomain.Carried), null, hostCarried));
            loot.RegisterParticipant(new LootParticipant(1, "client", copyCarried[0], new CoinWallet(CoinDomain.Carried), null, copyCarried));
            var w = Compose(resolve: resolve, loot: loot);

            // Both peers compose the same relay in room 4; the host's records each use in the room state (as the composer does).
            var hostRelay = new SecureRelayEvent(new DungeonEventContext(7, 1, 4), resolve);
            hostRelay.Secured += (_, participant, _) => w.HostRoom.State.MarkResolved(SecureRelayEvent.ResolvedIdFor(participant));
            RoomContentBinding.For(w.HostRoom).EventInstance = hostRelay;
            var replica = new SecureRelayEvent(new DungeonEventContext(7, 1, 4), resolve);
            RoomContentBinding.For(w.ClientRoom).EventInstance = replica;

            // The member's side of ExpeditionScene.SecureRelay: the unit is escrowed (out of the run) before the request;
            // a final verdict resolves the escrow once — accepted → Storage, refused → back into the run.
            var escrows = new List<SecureRelayEscrow>();
            void OnVerdict(RelayResultMessage r)
            {
                var escrow = escrows.FirstOrDefault(e => e.TransactionId == r.TransactionId);
                if (escrow == null || escrow.State != SecureRelayEscrowState.Pending || !r.Final) return;
                escrow.State = r.Accepted ? SecureRelayEscrowState.Accepted : SecureRelayEscrowState.Refused;
                escrows.Remove(escrow);
                if (r.Accepted) Assert.IsTrue(SecureRelayEscrows.Release(escrow, memberStorage));
                else Assert.IsTrue(SecureRelayEscrows.Return(escrow, CarriedOf(memberInventory)));
            }

            SecureRelayEscrow Escrow(SecureRelayEvent memberRelay, ItemInstance item, string tx)
            {
                Assert.AreEqual(SecureRelayRefusal.None, SecureRelayEscrows.Open(memberRelay, "client", CarriedOf(memberInventory), item.InstanceId, memberStorage, out var e));
                e.TransactionId = tx;
                escrows.Add(e);
                return e;
            }

            var results = new List<RelayResultMessage>();
            w.Client.RelayResult += results.Add;
            w.Client.RelayResult += OnVerdict;

            // Wrong owner: a request naming the host's own weapon is refused and touches nobody's item.
            w.Client.SendRelaySecure(4, hostWeapon.InstanceId, "tx-wrong");
            Assert.IsFalse(results.Last().Accepted);
            Assert.IsTrue(results.Last().Final);
            Assert.AreEqual(SecureRelayRefusal.NoItem.ToString(), results.Last().Refusal);
            Assert.IsTrue(hostOwn.BackpackSlots.Contains(hostWeapon));
            Assert.IsFalse(hostRelay.HasSecured("client"));

            // The host accepts, but the verdict is lost with the link (never delivered).
            w.Host.HoldRelayResults = true;
            var rifleEscrow = Escrow(replica, rifle, "tx-1");
            Assert.IsNull(memberInventory.GetEquipped(EquippedSlot.PrimaryWeapon), "escrowed: out of the run before the request");
            var delivered = results.Count;
            w.Client.SendRelaySecure(4, rifle.InstanceId, "tx-1");
            Assert.AreEqual(delivered, results.Count, "no verdict reached the member");
            Assert.AreEqual(1, w.Host.RelayResultsHeld);
            Assert.IsTrue(hostRelay.HasSecured("client"), "the host accepted and recorded the use");
            Assert.IsNull(hostCopy.GetEquipped(EquippedSlot.PrimaryWeapon), "and took its copy of the unit");
            Assert.AreEqual(0, memberStorage.OccupiedSlots);
            Assert.Contains(rifleEscrow, escrows, "the member still holds the escrow: not in the run, not lost");

            // Reconnect: the same participant under a new client id, a freshly composed relay; the pending request is re-sent.
            w.Host.HoldRelayResults = false;
            var rejoinBus = LoopbackCoopBus.Join(w.Network, 5);
            loot.UnregisterParticipant(1); // as ExpeditionScene rebinds a reconnecting member: the old client id lets go first
            loot.RegisterParticipant(new LootParticipant(5, "client", copyCarried[0], new CoinWallet(CoinDomain.Carried), null, copyCarried));
            var rejoinRoot = CreateRoom(RoomType.Combat, new Vector2Int(16, 12), new[] { new Vector2Int(2, 2) }, new Vector2(400f, 3000f));
            var rejoinRoom = rejoinRoot.gameObject.AddComponent<RoomRuntime>();
            rejoinRoom.Configure(rejoinRoot, 4, 1, 2);
            var fresh = new SecureRelayEvent(new DungeonEventContext(7, 1, 4), resolve);
            RoomContentBinding.For(rejoinRoom).EventInstance = fresh;
            var rejoined = Own(new CoopClientWorld(rejoinBus));
            rejoined.BindDepth(1, new Dictionary<int, RoomRuntime> { [4] = rejoinRoom }, w.ClientLoot, resolve);
            var rejoinResults = new List<RelayResultMessage>();
            rejoined.RelayResult += rejoinResults.Add;
            rejoined.RelayResult += OnVerdict;
            rejoined.RequestResync();
            Assert.IsTrue(fresh.HasSecured("client"), "the resync restores this member's spent use");

            rejoined.SendRelaySecure(4, rifle.InstanceId, "tx-1");
            Assert.IsTrue(rejoinResults.Last().Accepted && rejoinResults.Last().Final, "the host replays the verdict it already gave");
            Assert.AreEqual(rifle.InstanceId, memberStorage.Find(rifle.InstanceId)?.InstanceId, "the accepted unit is in the member's Storage");
            Assert.IsFalse(memberStorage.Find(rifle.InstanceId).IsAtRisk);
            Assert.IsEmpty(escrows);
            Assert.IsNull(memberInventory.GetEquipped(EquippedSlot.PrimaryWeapon), "never restored to the run");
            Assert.AreEqual(1, hostRelay.SecuredCount, "decided once");

            // Duplicate delivery of the same request: the stored verdict again, nothing moves.
            rejoined.SendRelaySecure(4, rifle.InstanceId, "tx-1");
            Assert.IsTrue(rejoinResults.Last().Accepted);
            Assert.AreEqual(1, memberStorage.OccupiedSlots);

            // A stale copy of the relay lets the member escrow a second item: the host refuses it, and it goes back into the run.
            var stale = new SecureRelayEvent(new DungeonEventContext(7, 1, 4), resolve);
            Escrow(stale, vest, "tx-2");
            Assert.IsFalse(memberInventory.BackpackSlots.Contains(vest));
            rejoined.SendRelaySecure(4, vest.InstanceId, "tx-2");
            Assert.IsFalse(rejoinResults.Last().Accepted);
            Assert.IsTrue(rejoinResults.Last().Final);
            Assert.AreEqual(SecureRelayRefusal.AlreadySecured.ToString(), rejoinResults.Last().Refusal, "one use per member, across the reconnect");
            var vestBack = memberInventory.BackpackSlots.FirstOrDefault(i => i != null && i.InstanceId == vest.InstanceId);
            Assert.IsNotNull(vestBack, "a refused unit returns to the run");
            Assert.IsTrue(vestBack.IsAtRisk);
            Assert.IsNotNull(hostCopy.BackpackSlots.FirstOrDefault(i => i != null && i.InstanceId == vest.InstanceId), "the host's copy never lost it");
            Assert.AreEqual(1, memberStorage.OccupiedSlots);
            Assert.IsFalse(hostRelay.HasSecured("host"), "the host keeps its own independent use");
        }

        [Test]
        public void SecureRelay_RequestTheHostNeverReceived_IsNeverSecured_TheResendIsDecidedByTheHost_AndAnEndedRunDecidesNothing()
        {
            Func<string, ItemDefinition> resolve = id => _content.Items.FirstOrDefault(i => i.Id == id);
            var registry = _content.BuildRegistry();
            var weapon = _content.Items.OfType<WeaponDefinition>().First(d => d.Id != StarterKitService.PistolId && d.Id != StarterKitService.KnifeId);
            var memberInventory = PlayerInventory.FromRegistry(registry, _content.AmmoBalance);
            var rifle = new ItemInstance(weapon.Id, 1, Rarity.Epic) { IsAtRisk = true };
            memberInventory.TryAddToBackpack(rifle);
            var hostCopy = PlayerInventory.FromRegistry(registry, _content.AmmoBalance);
            hostCopy.RestoreFromSnapshot(memberInventory.ToSnapshot());
            var memberStorage = new Storage(resolve, _content.AmmoBalance);
            var loot = new LootAuthorityService(CoopHostAuthority.Instance);
            var copyCarried = CarriedOf(hostCopy);
            loot.RegisterParticipant(new LootParticipant(1, "client", copyCarried[0], new CoinWallet(CoinDomain.Carried), null, copyCarried));
            var expedition = new ExpeditionService(resolve, t => _content.Items.OfType<AmmoItemDefinition>().FirstOrDefault(a => a.AmmoType == t), _content.AmmoBalance);
            expedition.Start(new PlayerProfile(), 7, Biome.RuinedMetro, 2, "host");
            var w = Compose(resolve: resolve, loot: loot, expedition: expedition);
            var hostRelay = new SecureRelayEvent(new DungeonEventContext(7, 1, 4), resolve);
            RoomContentBinding.For(w.HostRoom).EventInstance = hostRelay;
            var replica = new SecureRelayEvent(new DungeonEventContext(7, 1, 4), resolve);

            var results = new List<RelayResultMessage>();
            SecureRelayEscrow escrow = null;
            w.Client.RelayResult += r =>
            {
                results.Add(r);
                if (escrow == null || escrow.TransactionId != r.TransactionId || escrow.State != SecureRelayEscrowState.Pending || !r.Final) return;
                escrow.State = r.Accepted ? SecureRelayEscrowState.Accepted : SecureRelayEscrowState.Refused;
                Assert.IsTrue(r.Accepted ? SecureRelayEscrows.Release(escrow, memberStorage) : SecureRelayEscrows.Return(escrow, CarriedOf(memberInventory)));
            };

            // The member escrows its rifle; the request is lost before the host ever sees it. Its inventory report does
            // arrive (the host's copy follows the member: the rifle is gone from it).
            Assert.AreEqual(SecureRelayRefusal.None, SecureRelayEscrows.Open(replica, "client", CarriedOf(memberInventory), rifle.InstanceId, memberStorage, out escrow));
            escrow.TransactionId = "tx-lost";
            hostCopy.RestoreFromSnapshot(memberInventory.ToSnapshot());
            Assert.AreEqual(SecureRelayEscrowState.Pending, escrow.State, "unresolved, and recorded as such");
            Assert.IsFalse(hostRelay.HasSecured("client"));
            Assert.AreEqual(0, memberStorage.OccupiedSlots, "nothing is secured without the host");

            // The re-send (after the member's reconnect) is a new decision for the host, taken on what the host holds: the
            // rifle is not in the member's carried state as the host knows it, so it is refused — and the unit goes back.
            w.Client.SendRelaySecure(4, rifle.InstanceId, "tx-lost");
            Assert.IsTrue(results.Last().Final);
            Assert.IsFalse(results.Last().Accepted);
            Assert.AreEqual(SecureRelayEscrowState.Refused, escrow.State);
            Assert.AreEqual(0, memberStorage.OccupiedSlots, "never secured");
            var back = memberInventory.BackpackSlots.FirstOrDefault(i => i != null && i.InstanceId == rifle.InstanceId);
            Assert.IsNotNull(back, "the refusal restores the held item to the run");
            Assert.IsTrue(back.IsAtRisk);
            Assert.IsFalse(hostRelay.HasSecured("client"), "the member's one use is untouched");

            // Pressing again works on the host's real decision (the host's copy has the rifle back from the member's report).
            hostCopy.RestoreFromSnapshot(memberInventory.ToSnapshot());
            Assert.AreEqual(SecureRelayRefusal.None, SecureRelayEscrows.Open(replica, "client", CarriedOf(memberInventory), rifle.InstanceId, memberStorage, out escrow));
            escrow.TransactionId = "tx-again";

            // The host's run ends before this request reaches it: nothing is decided after the run is over.
            expedition.Fail();
            var decidedBefore = w.Host.RelaysResolved;
            w.Client.SendRelaySecure(4, rifle.InstanceId, "tx-again");
            Assert.IsFalse(results.Last().Final, "an ended run answers 'not now', never a verdict");
            Assert.AreEqual(decidedBefore, w.Host.RelaysResolved);
            Assert.AreEqual(1, w.Host.RelaysAfterRunEnd);
            Assert.IsFalse(hostRelay.HasSecured("client"));
            Assert.AreEqual(SecureRelayEscrowState.Pending, escrow.State, "still unresolved: the member's own run end returns it to the run (BaseSession)");
            Assert.AreEqual(0, memberStorage.OccupiedSlots);
        }
    }
}

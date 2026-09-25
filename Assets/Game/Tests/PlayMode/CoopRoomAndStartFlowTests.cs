using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Two halves of the co-op runtime that only a real party can exercise: a room that several members walk into
    /// (Phase 8 — one activation, one lock, one clear, no duplicated spawns or rewards, late entry joins the same
    /// encounter) and the start flow that decides who the party is (Phase 19 — a joining client becomes a lobby member,
    /// the start snapshot names them, and the composed party matches what the dungeon is scaled for).
    /// </summary>
    public class CoopRoomAndStartFlowTests
    {
        private const string MatrixDirectory = "TestResults/CoopRuntimeComposition";
        private readonly List<UnityEngine.Object> _created = new();
        private List<EnemyDefinition> _archetypes;

        private sealed class TrackingSpawner : IEnemySpawner
        {
            public readonly List<EnemyController> Spawned = new();
            public EnemyController Spawn(EnemyDefinition definition, Vector2 position, Transform target)
            {
                var actor = new DefaultEnemySpawner().Spawn(definition, position, target);
                Spawned.Add(actor);
                return actor;
            }
        }

        [SetUp]
        public void SetUp()
        {
            _archetypes = AssetDatabase.FindAssets("t:EnemyDefinition", new[] { "Assets/Game/ScriptableObjects/Enemies" })
                .Select(g => AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            foreach (var enemy in UnityEngine.Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (enemy != null) UnityEngine.Object.DestroyImmediate(enemy.gameObject);
            _created.Clear();
        }

        private T Track<T>(T o) where T : UnityEngine.Object { _created.Add(o); return o; }

        private static void Set(object target, string field, object value)
        {
            var type = target.GetType();
            FieldInfo info = null;
            while (type != null && info == null) { info = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance); type = type.BaseType; }
            info.SetValue(target, value);
        }

        private static void WriteMatrix(string fileName, IEnumerable<string> rows)
        {
            Directory.CreateDirectory(MatrixDirectory);
            File.WriteAllLines(Path.Combine(MatrixDirectory, fileName), rows);
        }

        private RoomRoot CreateRoom(RoomType type, Vector2Int size, IEnumerable<Vector2Int> enemySpawns, Vector2 worldOffset)
        {
            var definition = Track(ScriptableObject.CreateInstance<RoomDefinition>());
            Set(definition, "_id", $"coop_{type}");
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
            var south = new GameObject("Door_S").AddComponent<DoorSocket>();
            south.transform.SetParent(go.transform, false);
            south.Configure(DoorDirection.South, new Vector2Int(size.x / 2 - 1, 0));
            south.SnapToGrid();
            return root;
        }

        private EncounterPlan SmallPlan(int partySize)
        {
            var grunt = _archetypes.Single(a => a.Id == "grunt");
            var context = new EncounterContext(7, 1, partySize, Biome.RuinedMetro, 3);
            return new EncounterPlan(context, 2f, 3f, 2f, new[] { new EncounterEntry(grunt, 2) });
        }

        /// <summary>A party member as the room sees it: a player-team collider with a life state in the shared roster.</summary>
        private GameObject Member(PartyLifeRoster roster, string participantId, Vector2 position)
        {
            var content = GameContentCatalog.Load();
            var member = Track(PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options
            {
                Name = "Member_" + participantId, IsLocal = false,
                BalanceConfig = content.PlayerBalance, Caps = content.StatCaps,
                Position = position, LifeRoster = roster, ParticipantId = participantId
            }));
            return member;
        }

        // ---------------- Phase 8: room entry / activation / clear with a real party ----------------

        [UnityTest]
        public IEnumerator RoomWithSeveralMembers_ActivatesLocksAndClearsExactlyOnce()
        {
            var rows = new List<string> { "case,members,entries,activations,locks,spawns,clears,doors_locked,late_entry_joins_same_encounter,result" };
            var roster = new PartyLifeRoster();
            var root = CreateRoom(RoomType.Combat, new Vector2Int(16, 12), new[] { new Vector2Int(2, 2), new Vector2Int(13, 9), new Vector2Int(13, 2) }, new Vector2(0f, 2000f));
            var runtime = root.gameObject.AddComponent<RoomRuntime>();
            runtime.Configure(root, 4, depth: 1, partySize: 3);
            var spawner = new TrackingSpawner();
            runtime.SetEncounter(SmallPlan(3), spawner);

            var activations = 0;
            var clears = 0;
            var entries = new List<GameObject>();
            runtime.Activated += _ => activations++;
            runtime.Cleared += (_, _) => clears++;
            runtime.PlayerEntered += (_, player) => entries.Add(player);

            var origin = (Vector2)root.transform.position;
            var first = Member(roster, "p1", origin + new Vector2(2.5f, 2.5f));
            var second = Member(roster, "p2", origin + new Vector2(3.5f, 2.5f));
            var third = Member(roster, "p3", origin + new Vector2(4.5f, 2.5f));
            yield return null;

            Assert.IsTrue(runtime.NotifyPlayerEntered(first), "The first member activates the encounter.");
            Assert.AreEqual(RoomLifecycleState.Active, runtime.Lifecycle);
            Assert.IsTrue(runtime.DoorsLocked);
            yield return null;
            var spawnsAfterFirst = spawner.Spawned.Count;
            Assert.AreEqual(2, spawnsAfterFirst);

            // 82: the second and third members entering the active room are recognised as occupants and change nothing.
            Assert.IsFalse(runtime.NotifyPlayerEntered(second), "A second member cannot activate the room again.");
            Assert.IsFalse(runtime.NotifyPlayerEntered(third));
            yield return null;
            Assert.AreEqual(1, activations, "One activation for the whole party.");
            Assert.AreEqual(1, runtime.State.EntryCount, "One counted entry for the room, not one per member.");
            Assert.AreEqual(spawnsAfterFirst, spawner.Spawned.Count, "A later member never duplicates the spawn set.");
            Assert.AreEqual(3, entries.Count, "Every member's entry is still raised for presentation (map discovery).");
            Assert.AreEqual(3, entries.Distinct().Count());
            Assert.IsTrue(runtime.Occupants.Contains(second) && runtime.Occupants.Contains(third), "Members already inside are tracked.");
            rows.Add($"three members enter one combat room,3,{runtime.State.EntryCount},{activations},1,{spawner.Spawned.Count},{clears},{runtime.DoorsLocked},YES,PASS");

            // The clear is the party's: any member's kills finish the one encounter, once, and the doors open once.
            foreach (var enemy in spawner.Spawned.ToArray()) enemy.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(99999));
            yield return null;
            Assert.AreEqual(RoomLifecycleState.Cleared, runtime.Lifecycle);
            Assert.AreEqual(1, clears, "The room clears exactly once for the party.");
            Assert.IsFalse(runtime.DoorsLocked, "Doors unlock once, for everyone.");
            rows.Add($"party clears the encounter,3,{runtime.State.EntryCount},{activations},1,{spawner.Spawned.Count},{clears},{runtime.DoorsLocked},YES,PASS");

            // A revisit by any member re-raises only the presentation entry; no state, spawns or rewards repeat.
            Assert.IsFalse(runtime.NotifyPlayerEntered(first));
            runtime.NotifyPlayerLeft(second);
            Assert.IsFalse(runtime.NotifyPlayerEntered(second));
            yield return null;
            Assert.AreEqual(1, activations);
            Assert.AreEqual(1, clears);
            Assert.AreEqual(spawnsAfterFirst, spawner.Spawned.Count);
            rows.Add($"revisit after clear,3,{runtime.State.EntryCount},{activations},1,{spawner.Spawned.Count},{clears},{runtime.DoorsLocked},YES,PASS");

            // 82: a client peer's room never advances state locally, however many members walk in.
            var clientRoot = CreateRoom(RoomType.Combat, new Vector2Int(16, 12), new[] { new Vector2Int(2, 2), new Vector2Int(13, 9) }, new Vector2(60f, 2000f));
            var clientRoom = clientRoot.gameObject.AddComponent<RoomRuntime>();
            clientRoom.Configure(clientRoot, 5, depth: 1, partySize: 3);
            clientRoom.SetEncounter(SmallPlan(3), new TrackingSpawner());
            clientRoom.SetAuthoritative(false);
            var clientMember = Member(roster, "p4", (Vector2)clientRoot.transform.position + new Vector2(2.5f, 2.5f));
            yield return null;
            Assert.IsFalse(clientRoom.NotifyPlayerEntered(clientMember));
            Assert.AreEqual(RoomLifecycleState.Unentered, clientRoom.Lifecycle, "A client never activates a room locally.");
            rows.Add("client peer entry,1,0,0,0,0,0,False,n/a,PASS");

            WriteMatrix("room_party_state_matrix.csv", rows);
        }

        // ---------------- Phase 19: session membership → lobby → start snapshot → composed party ----------------

        [UnityTest]
        public IEnumerator SessionMembership_BecomesTheLobbyPartyAndTheStartSnapshot()
        {
            var rows = new List<string> { "step,actor,expected,observed,result" };
            var content = GameContentCatalog.Load();
            var registry = ItemDefinitionRegistry.Build(content.Items.Where(i => i != null));
            var lobby = new PartyLobby(0, id => registry.TryGet(id, out var d) ? d : null);
            lobby.Join(0, "p0");
            var connection = new FakeConnectionEvents(0, true);
            var bridge = new SessionPartyBridge(connection, lobby, id => "p" + id);

            // 81: a client the host accepts becomes a party member; the terminal roster and READY flow are about them.
            connection.Connect(1, "Client One");
            connection.Connect(2, "Client Two");
            Assert.AreEqual(3, lobby.Members.Count, "Every accepted connection is a lobby member.");
            Assert.AreEqual(2, bridge.Joined);
            rows.Add($"two clients join the hosted session,host,3 lobby members,{lobby.Members.Count},PASS");

            // 80: a fourth is never admitted.
            connection.Connect(3, "Gatecrasher");
            Assert.AreEqual(3, lobby.Members.Count, "The party limit is 3.");
            rows.Add($"a fourth client connects,host,refused,{lobby.Members.Count} members,PASS");

            // A client that leaves before the start is removed again.
            connection.Disconnect(2);
            Assert.AreEqual(2, lobby.Members.Count);
            rows.Add($"a client leaves before the start,host,member removed,{lobby.Members.Count} members,PASS");
            connection.Connect(2, "Client Two");
            Assert.AreEqual(3, lobby.Members.Count);

            // 81: the host can only start when every connected member is Ready with a valid loadout.
            var loadout = ValidLoadout(content);
            foreach (var member in lobby.Members.ToArray())
            {
                lobby.SetLoadout(member.ClientId, loadout);
                Assert.IsTrue(lobby.SetReady(member.ClientId, true), $"{member.ParticipantId} is ready.");
            }

            Assert.IsTrue(lobby.AllReady);
            var error = lobby.TryStart(0, 4242, out var start);
            Assert.AreEqual(LobbyStartError.None, error);
            Assert.IsNotNull(start);
            Assert.AreEqual(3, start.PartySize, "The snapshot names the real party.");
            Assert.AreEqual(3, start.Members.Count);
            rows.Add($"host starts with everyone ready,host,snapshot party size 3,{start.PartySize},PASS");

            // 81: after the start the party is frozen — a late connection is not added to the started party.
            connection.Connect(5, "Late");
            Assert.AreEqual(3, lobby.Members.Count, "A started expedition does not accept a new member.");
            rows.Add($"client connects after the start,host,refused,{lobby.Members.Count} members,PASS");

            // The composed party is exactly what the snapshot promised, and that is what the dungeon scales for.
            var roster = new PartyLifeRoster();
            var local = Member(roster, "p0", new Vector2(0f, 2200f));
            var party = ExpeditionParty.Compose(new PartyCompositionRequest
            {
                Members = start.Members.Select(m => new PartyMemberDescriptor(m.ClientId, m.ClientId == 0 ? "Host" : null, m.ParticipantId, m.ClientId == 0)).ToList(),
                LocalClientId = 0, LocalEntity = local, LocalParticipantId = "p0", IsHost = true,
                Balance = content.PlayerBalance, Caps = content.StatCaps, LifeRoster = roster,
                Items = registry, Ammo = content.AmmoBalance,
                DisplayNamePolicy = content.DisplayNamePolicy,
                SpawnPosition = identity => new Vector2(2f * identity.ClientId, 2200f)
            }, out var compositionError);
            Assert.IsNotNull(party, $"composition error: {compositionError}");
            foreach (var member in party.Members) if (member.GameObject != local) Track(member.GameObject);
            yield return null;

            Assert.AreEqual(start.PartySize, party.ComposedPartySize, "Composed presence equals the agreed party.");
            Assert.AreEqual(start.PartySize, party.ScalingPartySize, "83: the dungeon scales for the composed party.");
            Assert.IsTrue(party.IsCoop);
            Assert.AreEqual(3, roster.Count);
            rows.Add($"expedition composes the snapshot's party,host,3 entities,{party.ComposedPartySize},PASS");

            // The party is closed at start: a stray connection cannot mint a fourth character mid-expedition.
            party.SimulateConnect(9, "Intruder");
            yield return null;
            Assert.AreEqual(3, party.ComposedPartySize, "A closed party admits nobody new.");
            rows.Add($"stray connect during the expedition,host,no new entity,{party.ComposedPartySize} entities,PASS");

            party.Dispose();
            bridge.Dispose();
            WriteMatrix("start_flow_matrix.csv", rows);
        }

        /// <summary>A loadout the lobby accepts: the profile's starter equipment as the Shelter submits it.</summary>
        private static InventorySnapshot ValidLoadout(GameContentCatalog content)
        {
            var registry = ItemDefinitionRegistry.Build(content.Items.Where(i => i != null));
            var inventory = PlayerInventory.FromRegistry(registry, content.AmmoBalance);
            var weapon = content.Items.OfType<WeaponDefinition>().First(w => w.Id == "weapon_p9_ranger");
            Assert.IsTrue(inventory.TryEquip(new ItemInstance(weapon.Id), EquippedSlot.PrimaryWeapon));
            return inventory.ToSnapshot();
        }
    }
}

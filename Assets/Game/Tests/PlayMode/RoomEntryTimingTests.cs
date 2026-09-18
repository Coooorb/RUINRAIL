using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Room entry timing on the shipped room prefabs, one per socket direction and size class: a player can stand in
    /// the doorway and cross it without the room activating or a lock blocking them; activation happens once the
    /// player is inside the interior; the entry lock then closes behind them and is solid; the encounter belongs to
    /// the entered room; a teammate still in the doorway when the room locks is never trapped in the door — the lock
    /// waits until they clear it, then shuts.
    /// </summary>
    public sealed class RoomEntryTimingTests
    {
        private readonly List<Object> _created = new();
        private List<EnemyDefinition> _archetypes;
        private GameContentCatalog _catalog;

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
            _catalog = GameContentCatalog.Load();
            RoomDoorLock.SkinResolver = biome => { var skin = _catalog.DoorSkinFor(biome); return skin != null ? new DoorSkinSprites(skin.Open, skin.Locked) : default; };
            _archetypes = AssetDatabase.FindAssets("t:EnemyDefinition", new[] { "Assets/Game/ScriptableObjects/Enemies" })
                .Select(g => AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var enemy in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (enemy != null) Object.DestroyImmediate(enemy.gameObject);
            _created.Clear();
            RoomDoorLock.SkinResolver = null;
        }

        private EncounterPlan SmallPlan()
        {
            var grunt = _archetypes.Single(a => a.Id == "grunt");
            return new EncounterPlan(new EncounterContext(7, 1, 1, Biome.RuinedMetro, 3), 2f, 3f, 2f, new[] { new EncounterEntry(grunt, 2) });
        }

        private (RoomRuntime runtime, RoomRoot root, TrackingSpawner spawner) CombatRoom(RoomDefinition definition, Vector2 origin)
        {
            var instance = Object.Instantiate(definition.Prefab, new Vector3(origin.x, origin.y, 0f), Quaternion.identity);
            _created.Add(instance);
            var root = instance.GetComponent<RoomRoot>();
            var runtime = instance.AddComponent<RoomRuntime>();
            runtime.Configure(root, 1, 1, 1);
            var spawner = new TrackingSpawner();
            runtime.SetEncounter(SmallPlan(), spawner);
            RoomEntryTrigger.Attach(runtime);
            return (runtime, root, spawner);
        }

        private (GameObject go, Rigidbody2D body) Player(Vector2 position, string name = "Player")
        {
            var player = new GameObject(name);
            _created.Add(player);
            player.transform.position = position;
            var body = player.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.bodyType = RigidbodyType2D.Kinematic;
            player.AddComponent<CircleCollider2D>().radius = 0.4f;
            player.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            player.AddComponent<PlayerMovement>();
            return (player, body);
        }

        /// <summary>World centre of a door cell, and the cell one step *inside* the room from it.</summary>
        private static (Vector2 doorway, Vector2 justInside, Vector2 interior) SocketPoints(RoomRoot root, DoorSocket socket)
        {
            var step = DoorDirections.Step(socket.Direction);
            var doorCell = socket.Cells()[0];
            var doorway = (Vector2)root.transform.TransformPoint(GridCoordinates.CellToWorldCenter(doorCell));
            var justInside = doorway - (Vector2)step * GridConstants.TileWorldSize * 1f;
            var interior = doorway - (Vector2)step * GridConstants.TileWorldSize * 3.5f;
            return (doorway, justInside, interior);
        }

        private IEnumerable<(RoomDefinition room, DoorSocket socket)> Cases()
        {
            // One combat room per size class per biome, every socket direction it has: N/E/S/W across 16x12 and larger.
            foreach (var biome in new[] { Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs })
            foreach (var size in new[] { RoomSizeClass.Small, RoomSizeClass.Medium, RoomSizeClass.Large })
            {
                var room = _catalog.Rooms.FirstOrDefault(r => r.Biome == biome && r.RoomType == RoomType.Combat && r.SizeClass == size);
                if (room == null) continue;
                foreach (var socket in room.Prefab.GetComponent<RoomRoot>().GetSockets()) yield return (room, socket);
            }
        }

        [UnityTest]
        public IEnumerator Player_CrossesTheDoorway_ThenActivatesInsideTheRoom_AndTheLockClosesBehind_ForEverySocketOfEverySizeAndBiome()
        {
            var cases = Cases().ToList();
            Assert.Greater(cases.Count, 20, "every size class × biome × socket direction");
            var index = 0;
            foreach (var (definition, prefabSocket) in cases)
            {
                var origin = new Vector2(200f + (index % 6) * 60f, 200f + (index / 6) * 40f);
                index++;
                var (runtime, root, spawner) = CombatRoom(definition, origin);
                var socket = root.GetSocket(prefabSocket.Direction);
                var (doorway, justInside, interior) = SocketPoints(root, socket);
                var label = $"{definition.Id} {socket.Direction}";

                var (player, body) = Player(doorway + (Vector2)DoorDirections.Step(socket.Direction) * 3f, "Player_" + index);
                yield return new WaitForFixedUpdate();

                // 1–2: the doorway is traversable and standing in it does not activate the room.
                body.position = doorway;
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                Assert.AreEqual(RoomLifecycleState.Unentered, runtime.Lifecycle, $"{label}: in the doorway, not yet activated");
                Assert.IsFalse(runtime.DoorsLocked, $"{label}: nothing locked while the player is in the door");
                var lockOnEntry = runtime.Doors.First(d => d.Socket == socket);
                Assert.IsFalse(lockOnEntry.IsBlocking, $"{label}: the entry is open");

                body.position = justInside;
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                Assert.AreEqual(RoomLifecycleState.Unentered, runtime.Lifecycle, $"{label}: just past the door cell is still the wall ring, not the interior");

                // 3–5: reaching the interior activates exactly once; the entry lock engages behind the player.
                var activations = 0;
                runtime.Activated += _ => activations++;
                body.position = interior;
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                Assert.AreEqual(RoomLifecycleState.Active, runtime.Lifecycle, $"{label}: activated inside the room");
                Assert.AreEqual(1, activations, $"{label}: exactly once");
                Assert.IsTrue(runtime.DoorsLocked, $"{label}: doors locked");
                yield return new WaitForFixedUpdate();
                Assert.IsTrue(lockOnEntry.IsBlocking, $"{label}: the entry lock is solid behind the player");
                Assert.IsFalse(lockOnEntry.IsPending, $"{label}: nobody in the door, so nothing pending");
                yield return null; // the plate mirrors the blocker in LateUpdate
                Assert.IsTrue(lockOnEntry.IsPlateVisible, $"{label}: the locked door is drawn shut");

                var volume = RoomEntryTrigger.InteriorVolume(root.Size);
                var local = (Vector2)root.transform.InverseTransformPoint(body.position);
                Assert.IsTrue(volume.Contains(local), $"{label}: the player stands inside the activation volume");
                var (_, blockerSize) = lockOnEntry.BlockerArea();
                var blocker = new Rect((Vector2)lockOnEntry.transform.position - blockerSize * 0.5f, blockerSize);
                Assert.IsFalse(blocker.Overlaps(new Rect(body.position - Vector2.one * 0.4f, Vector2.one * 0.8f)), $"{label}: the player is clear of the lock, not stuck in it");

                // 6: the encounter belongs to this room: enemies spawned inside its bounds.
                yield return null;
                Assert.Greater(spawner.Spawned.Count, 0, $"{label}: the encounter spawned");
                var bounds = new Rect(origin, (Vector2)root.Size * GridConstants.TileWorldSize);
                foreach (var enemy in spawner.Spawned.Where(e => e != null))
                    Assert.IsTrue(bounds.Contains(enemy.transform.position), $"{label}: enemy at {enemy.transform.position} inside the entered room {bounds}");

                Object.DestroyImmediate(player);
                foreach (var enemy in spawner.Spawned) if (enemy != null) Object.DestroyImmediate(enemy.gameObject);
                Object.DestroyImmediate(root.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator Teammate_StillInTheDoorwayWhenTheRoomLocks_IsNeverTrappedInTheDoor_TheLockWaitsThenShuts()
        {
            var definition = _catalog.Rooms.First(r => r.Biome == Biome.RuinedMetro && r.RoomType == RoomType.Combat && r.SizeClass == RoomSizeClass.Medium);
            var (runtime, root, _) = CombatRoom(definition, new Vector2(300f, 300f));
            var socket = root.GetSockets().First();
            var (doorway, _, interior) = SocketPoints(root, socket);
            var entryLock = runtime.Doors.First(d => d.Socket == socket);

            var (_, first) = Player(interior + Vector2.one * 0.5f, "First");
            var (mate, second) = Player(doorway, "Mate");
            yield return new WaitForFixedUpdate();
            first.position = interior;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(RoomLifecycleState.Active, runtime.Lifecycle);
            Assert.IsTrue(entryLock.IsLocked, "the room wants the door locked");
            Assert.IsTrue(entryLock.IsPending, "…but a teammate is standing in it");
            Assert.IsFalse(entryLock.IsBlocking, "so the door is not solid on top of them");
            Assert.IsFalse(entryLock.IsPlateVisible);

            for (var i = 0; i < 5; i++) yield return new WaitForFixedUpdate();
            Assert.IsFalse(entryLock.IsBlocking, "still waiting while they stand there");

            second.position = interior + Vector2.left * 0.5f; // the mate walks in
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(entryLock.IsBlocking, "the door shuts once the doorway is clear");
            Assert.IsFalse(entryLock.IsPending);
            Assert.IsTrue(runtime.Occupants.Contains(mate), "the teammate is inside with the fight");

            runtime.UnlockDoors();
            Assert.IsFalse(entryLock.IsBlocking);
            Assert.IsFalse(entryLock.IsPending, "unlock clears any pending lock");
        }

        [UnityTest]
        public IEnumerator SealedSpareSocket_StaysSealed_WhileTheRoomLocksAndUnlocks()
        {
            var definition = _catalog.Rooms.First(r => r.Biome == Biome.Rustworks && r.RoomType == RoomType.Combat && r.Prefab.GetComponent<RoomRoot>().GetSockets().Count >= 3);
            var (runtime, root, _) = CombatRoom(definition, new Vector2(400f, 400f));
            var spare = root.GetSockets().Last();
            Assert.IsTrue(RuinRail.Dungeon.Generation.RoomExitSealer.Seal(root, spare));
            runtime.LockDoors();
            yield return new WaitForFixedUpdate();
            runtime.UnlockDoors();
            Assert.IsTrue(RuinRail.Dungeon.Generation.RoomExitSealer.IsSealed(root, spare), "locking never reopens a sealed exit");
        }
    }
}

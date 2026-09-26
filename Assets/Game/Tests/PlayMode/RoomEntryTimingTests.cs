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
using RuinRail.Gameplay.Combat.Projectiles;
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

        /// <summary>
        /// Only a real player body entering activates a combat room. With the player outside the open door, the player's
        /// own projectiles flown through the doorway into the interior, a trigger child of the player reaching inside
        /// (what the hurtbox and every pooled projectile are), an enemy body and a loose dropped-item body placed inside
        /// all leave the room Unentered with its doors open and nothing spawned. The player walking in afterwards
        /// activates, locks and spawns exactly as before, and clearing the encounter unlocks the doors again.
        /// </summary>
        [UnityTest]
        public IEnumerator NonPlayerObjects_EnteringFirst_NeverActivateTheRoom_ThePlayerStillDoes_AndItStillClears()
        {
            foreach (var biome in new[] { Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs })
            {
                var definition = _catalog.Rooms.First(r => r.Biome == biome && r.RoomType == RoomType.Combat && r.SizeClass == RoomSizeClass.Medium);
                var origin = new Vector2(900f + (int)biome * 80f, 900f);
                var (runtime, root, spawner) = CombatRoom(definition, origin);
                var socket = root.GetSockets().First();
                var (doorway, _, interior) = SocketPoints(root, socket);
                var outward = (Vector2)DoorDirections.Step(socket.Direction);
                var label = $"{definition.Id} {socket.Direction}";
                var activations = 0;
                runtime.Activated += _ => activations++;

                var (player, body) = Player(doorway + outward * 3f, "Shooter_" + biome);
                var pool = player.AddComponent<ProjectilePool>();
                yield return new WaitForFixedUpdate();

                // 1. The player's own shots, fired from outside through the open door, fly deep into the interior.
                var projectiles = new List<Projectile>();
                for (var i = 0; i < 4; i++)
                {
                    var from = doorway + outward * 2.5f;
                    var direction = (interior - from).normalized;
                    projectiles.Add(pool.Spawn(from, new ProjectileSpawnData(5, 20f, 12f, 0f, 0f, direction, player, null, 0f, DamageTeam.Player)));
                }

                var until = Time.time + 0.6f;
                var reachedInside = false;
                var volume = RoomEntryTrigger.InteriorVolume(root.Size);
                while (Time.time < until)
                {
                    yield return new WaitForFixedUpdate();
                    reachedInside |= projectiles.Any(p => p != null && p.gameObject.activeInHierarchy && volume.Contains((Vector2)root.transform.InverseTransformPoint(p.transform.position)));
                }

                Assert.IsTrue(reachedInside, $"{label}: a projectile really crossed into the interior (the scenario is real)");
                Assert.AreEqual(RoomLifecycleState.Unentered, runtime.Lifecycle, $"{label}: a projectile entering first does not activate the room");
                Assert.IsFalse(runtime.DoorsLocked, $"{label}: a projectile does not lock the doors");
                Assert.AreEqual(0, spawner.Spawned.Count, $"{label}: nothing spawned");

                // 2. Any trigger parented under the player (the hurtbox, a pooled projectile) reaching into the interior.
                var probe = new GameObject("PlayerChildTrigger");
                probe.transform.SetParent(player.transform, false);
                var probeCollider = probe.AddComponent<BoxCollider2D>();
                probeCollider.isTrigger = true;
                probeCollider.size = Vector2.one * 0.6f;
                probe.transform.position = interior;
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                Assert.AreEqual(RoomLifecycleState.Unentered, runtime.Lifecycle, $"{label}: a player-parented trigger is not the player");
                Object.DestroyImmediate(probe);

                // 3. Other bodies crossing in: an enemy and a loose dropped-item style physics body.
                var enemy = new DefaultEnemySpawner().Spawn(_archetypes.Single(a => a.Id == "grunt"), interior + Vector2.right, null);
                var drop = new GameObject("LooseDrop");
                drop.transform.position = interior + Vector2.left;
                var dropBody = drop.AddComponent<Rigidbody2D>();
                dropBody.gravityScale = 0f;
                drop.AddComponent<CircleCollider2D>().radius = 0.25f;
                _created.Add(drop);
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                Assert.AreEqual(RoomLifecycleState.Unentered, runtime.Lifecycle, $"{label}: an enemy or a dropped item is not an entry");
                Assert.AreEqual(0, activations, $"{label}: never activated by a non-player");
                Object.DestroyImmediate(enemy.gameObject);
                Object.DestroyImmediate(drop);

                // 4. The player walking in afterwards activates, locks and spawns exactly as before.
                body.position = doorway;
                yield return new WaitForFixedUpdate();
                body.position = interior;
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                Assert.AreEqual(RoomLifecycleState.Active, runtime.Lifecycle, $"{label}: the player's entry activates the room");
                Assert.AreEqual(1, activations, $"{label}: exactly once");
                Assert.IsTrue(runtime.DoorsLocked, $"{label}: doors locked behind the player");
                yield return null;
                Assert.Greater(spawner.Spawned.Count, 0, $"{label}: the encounter spawned");

                // 5. Clearing the encounter still unlocks the doors.
                foreach (var spawned in spawner.Spawned.Where(e => e != null))
                    spawned.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(99999));
                var clearBy = Time.time + 3f;
                while (runtime.Lifecycle != RoomLifecycleState.Cleared && Time.time < clearBy) yield return null;
                Assert.AreEqual(RoomLifecycleState.Cleared, runtime.Lifecycle, $"{label}: the room clears");
                Assert.IsFalse(runtime.DoorsLocked, $"{label}: and the doors reopen");

                Object.DestroyImmediate(player);
                foreach (var e in spawner.Spawned) if (e != null) Object.DestroyImmediate(e.gameObject);
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

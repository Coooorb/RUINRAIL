using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Encounter containment: an encounter actor's collider may not cross the legal encounter-room boundary into a
    /// different room. Root cause of the leaks: a chasing body only respected solid geometry, so any open doorway —
    /// a lock still pending on a player standing in it, an event room that never locks, or the Boss arena before the
    /// player has entered it (the boss acquired its target at spawn and walked out to meet it) — was a route out.
    /// Every mover now consults <see cref="EncounterBounds"/> (velocity, dash endpoint, knockback step) and the room
    /// that spawns an actor binds it as owner. The doors' physical blockers are unchanged.
    /// </summary>
    public sealed class EncounterContainmentTests
    {
        private readonly List<Object> _created = new();
        private GameContentCatalog _catalog;
        private const float Tolerance = 0.02f;

        [SetUp]
        public void SetUp()
        {
            DamageAuthority.LocalIsAuthoritative = true;
            CombatLayers.Apply();
            _catalog = GameContentCatalog.Load();
            RoomDoorLock.SkinResolver = biome => { var skin = _catalog.DoorSkinFor(biome); return skin != null ? new DoorSkinSprites(skin.Open, skin.Locked) : default; };
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var e in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (e != null) Object.DestroyImmediate(e.gameObject);
            foreach (var e in Object.FindObjectsByType<MovesetActorController>(FindObjectsSortMode.None)) if (e != null) Object.DestroyImmediate(e.gameObject);
            _created.Clear();
            RoomDoorLock.SkinResolver = null;
        }

        // ---- fixtures -------------------------------------------------------------------------------------------

        private (RoomRuntime runtime, RoomRoot root) Room(RoomDefinition definition, Vector2 origin, int nodeId = 1)
        {
            var instance = Object.Instantiate(definition.Prefab, new Vector3(origin.x, origin.y, 0f), Quaternion.identity);
            _created.Add(instance);
            var root = instance.GetComponent<RoomRoot>();
            var runtime = instance.AddComponent<RoomRuntime>();
            runtime.Configure(root, nodeId, 1, 1);
            return (runtime, root);
        }

        private GameObject Target(Vector2 at)
        {
            var target = new GameObject("Target");
            _created.Add(target);
            target.transform.position = at;
            target.AddComponent<CircleCollider2D>().isTrigger = true;
            target.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            target.AddComponent<TestDamageableTarget>();
            return target;
        }

        /// <summary>A body the door lock recognises as a player (so the lock stays pending while it stands in the doorway).</summary>
        private GameObject PlayerBody(Vector2 at)
        {
            var go = new GameObject("PlayerBody");
            _created.Add(go);
            go.transform.position = at;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            go.AddComponent<CircleCollider2D>().radius = 0.4f;
            var movement = go.AddComponent<PlayerMovement>();
            movement.SetInputReader(new FakePlayerInputReader());
            var config = ScriptableObject.CreateInstance<PlayerBalanceConfig>();
            _created.Add(config);
            movement.SetBalanceConfig(config);
            go.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            go.AddComponent<TestDamageableTarget>();
            return go;
        }

        private EnemyController Enemy(string id, Vector2 at, Transform target)
        {
            var definition = _catalog.Enemies.First(e => e.Id == id);
            var enemy = new DefaultEnemySpawner(_catalog.Stagger).Spawn(definition, at, target);
            _created.Add(enemy.gameObject);
            if (enemy.GetComponent<ProjectilePool>() == null) enemy.gameObject.AddComponent<ProjectilePool>();
            return enemy;
        }

        private static (Vector2 doorway, Vector2 inside, Vector2 outside, Vector2 step) SocketPoints(RoomRoot root, DoorSocket socket)
        {
            var step = (Vector2)DoorDirections.Step(socket.Direction);
            var cells = socket.Cells();
            var centre = Vector2.zero;
            foreach (var cell in cells) centre += (Vector2)root.transform.TransformPoint(GridCoordinates.CellToWorldCenter(cell));
            centre /= cells.Length;
            return (centre, centre - step * 2.5f, centre + step * 3f, step);
        }

        private static Rect RoomRect(RoomRoot root) => new((Vector2)root.transform.position, (Vector2)root.Size * GridConstants.TileWorldSize);

        private static bool ColliderInside(Rect interior, Component actor, float radius, float tolerance = Tolerance)
        {
            var p = (Vector2)actor.transform.position;
            return p.x - radius >= interior.xMin - tolerance && p.x + radius <= interior.xMax + tolerance && p.y - radius >= interior.yMin - tolerance && p.y + radius <= interior.yMax + tolerance;
        }

        private static float Radius(Component actor)
        {
            var circle = actor.GetComponents<CircleCollider2D>().FirstOrDefault(c => !c.isTrigger);
            return circle != null ? circle.radius : DefaultEnemySpawner.BodyRadius;
        }

        private static IEnumerator Steps(int n)
        {
            for (var i = 0; i < n; i++) yield return new WaitForFixedUpdate();
        }

        private IEnumerable<RoomDefinition> CombatRoomsOfEveryBiome() =>
            new[] { Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs }.Select(b => _catalog.Rooms.First(r => r.Biome == b && r.RoomType == RoomType.Combat && r.SizeClass == RoomSizeClass.Medium));

        // ---- the bounds themselves ------------------------------------------------------------------------------

        [Test]
        public void Bounds_ConstrainVelocity_FreeDistance_AndClamp_AreExact()
        {
            var go = new GameObject("Actor");
            _created.Add(go);
            go.AddComponent<CircleCollider2D>().radius = 0.5f;
            go.AddComponent<Rigidbody2D>();
            var bounds = EncounterBounds.Bind(go, new Rect(10f, 20f, 8f, 6f), "room", 3);
            Assert.IsTrue(bounds.IsBound);
            Assert.AreEqual("room", bounds.RoomId);
            Assert.AreEqual(3, bounds.RoomNodeId);
            Assert.AreEqual(0.5f, bounds.BodyRadius, 0.0001f);
            Assert.AreEqual(new Rect(10.5f, 20.5f, 7f, 5f), bounds.Legal, "the centre may go where the whole collider stays inside");

            // Inside, heading for the right edge: the outward component is cut to reach the edge exactly, the tangent is kept.
            var v = bounds.ConstrainVelocity(new Vector2(17.4f, 23f), new Vector2(10f, 2f), 0.02f);
            Assert.AreEqual(0.1f / 0.02f, v.x, 0.001f);
            Assert.AreEqual(2f, v.y, 0.001f);
            // At the edge: no outward motion at all, tangent kept (slide along the boundary).
            v = bounds.ConstrainVelocity(new Vector2(17.5f, 23f), new Vector2(10f, -3f), 0.02f);
            Assert.AreEqual(0f, v.x, 0.001f);
            Assert.AreEqual(-3f, v.y, 0.001f);
            // Heading back inside is never touched.
            v = bounds.ConstrainVelocity(new Vector2(17.5f, 23f), new Vector2(-10f, 0f), 0.02f);
            Assert.AreEqual(-10f, v.x, 0.001f);
            // Far from every edge nothing changes.
            v = bounds.ConstrainVelocity(new Vector2(14f, 23f), new Vector2(3f, 3f), 0.02f);
            Assert.AreEqual(new Vector2(3f, 3f), v);

            Assert.AreEqual(3.5f, bounds.FreeDistance(new Vector2(14f, 23f), Vector2.right, 10f), 0.001f);
            Assert.AreEqual(2.5f, bounds.FreeDistance(new Vector2(14f, 23f), Vector2.up, 10f), 0.001f);
            Assert.AreEqual(1f, bounds.FreeDistance(new Vector2(14f, 23f), Vector2.right, 1f), 0.001f, "capped");
            Assert.AreEqual(0f, bounds.FreeDistance(new Vector2(17.5f, 23f), Vector2.right, 10f), 0.001f, "nothing left at the edge");
            Assert.AreEqual(0f, bounds.FreeDistance(new Vector2(19f, 23f), Vector2.right, 10f), 0.001f, "already outside heading out: nothing");

            Assert.AreEqual(new Vector2(17.5f, 25.5f), bounds.ClampCenter(new Vector2(30f, 40f)));
            Assert.IsTrue(bounds.ContainsCenter(new Vector2(14f, 23f)));
            Assert.IsFalse(bounds.ContainsCenter(new Vector2(17.6f, 23f)));
        }

        [Test]
        public void Bind_PlacesABodyThatStartsOutside_Inside_AndRebindingKeepsOneComponent()
        {
            var go = new GameObject("Actor");
            _created.Add(go);
            go.AddComponent<CircleCollider2D>().radius = 0.35f;
            go.AddComponent<Rigidbody2D>();
            go.transform.position = new Vector2(0f, 0f);
            var first = EncounterBounds.Bind(go, new Rect(10f, 10f, 4f, 4f), "a", 1);
            Assert.AreEqual(new Vector3(10.35f, 10.35f, 0f), go.transform.position);
            var second = EncounterBounds.Bind(go, new Rect(20f, 20f, 4f, 4f), "b", 2);
            Assert.AreSame(first, second);
            Assert.AreEqual(1, go.GetComponents<EncounterBounds>().Length);
            Assert.AreEqual("b", second.RoomId);
        }

        // ---- the room interior ----------------------------------------------------------------------------------

        [Test]
        public void RoomInterior_IsTheRoomMinusTheWallRing_AndEveryDoorCellLiesOutsideIt_InEveryBiome()
        {
            var index = 0;
            var doorsChecked = 0;
            foreach (var biome in new[] { Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs })
            foreach (var definition in _catalog.Rooms.Where(r => r.Biome == biome && r.Prefab != null).Take(6))
            {
                var (runtime, root) = Room(definition, new Vector2(3000f + index++ * 100f, 3000f));
                var rect = RoomRect(root);
                var interior = runtime.InteriorWorldBounds;
                Assert.AreEqual(rect.xMin + 1f, interior.xMin, 0.0001f, definition.Id);
                Assert.AreEqual(rect.yMin + 1f, interior.yMin, 0.0001f, definition.Id);
                Assert.AreEqual(rect.xMax - 1f, interior.xMax, 0.0001f, definition.Id);
                Assert.AreEqual(rect.yMax - 1f, interior.yMax, 0.0001f, definition.Id);
                foreach (var socket in root.GetSockets())
                foreach (var cell in socket.Cells())
                {
                    var centre = (Vector2)root.transform.TransformPoint(GridCoordinates.CellToWorldCenter(cell));
                    Assert.IsFalse(interior.Contains(centre), $"{definition.Id} {socket.Direction}: door cell {cell} is not encounter interior");
                    doorsChecked++;
                }
            }

            Assert.Greater(doorsChecked, 0);
        }

        // ---- pursuit at an open doorway: melee, ranged, dash ----------------------------------------------------

        [UnityTest]
        public IEnumerator MeleePursuit_ThroughAnOpenDoorway_StopsAtTheLegalEdge_InEveryBiomeAndEveryExitDirection()
        {
            var index = 0;
            var checkedDoors = 0;
            var directions = new HashSet<DoorDirection>();
            foreach (var definition in CombatRoomsOfEveryBiome())
            {
                var (runtime, root) = Room(definition, new Vector2(1000f + index++ * 100f, 1000f));
                var interior = runtime.InteriorWorldBounds;
                foreach (var socket in root.GetSockets())
                {
                    if (RoomExitSealer.IsSealed(root, socket)) continue;
                    var (doorway, inside, outside, step) = SocketPoints(root, socket);
                    var target = Target(outside);
                    var enemy = Enemy("grunt", inside, target.transform);
                    runtime.BindEncounterBounds(enemy.gameObject);
                    runtime.UnlockDoors(); // the doorway is physically open: only the bounds hold the enemy
                    yield return new WaitForFixedUpdate();
                    var closest = float.PositiveInfinity;
                    for (var i = 0; i < 120; i++)
                    {
                        yield return new WaitForFixedUpdate();
                        Assert.IsTrue(ColliderInside(interior, enemy, DefaultEnemySpawner.BodyRadius), $"{definition.Id} {socket.Direction}: collider left the interior at {enemy.transform.position} (interior {interior})");
                        closest = Mathf.Min(closest, Vector2.Dot(doorway - (Vector2)enemy.transform.position, step));
                    }

                    Assert.AreEqual(EnemyState.Chase, enemy.State, "still pursuing — it holds at the edge rather than giving up");
                    Assert.Less(closest, 2f, $"{definition.Id} {socket.Direction}: it did walk up to the legal edge (closest approach {closest} tiles from the door line)");
                    // The door cell centre sits 0.5 tiles outside the interior; the body's centre stops a radius short of that.
                    Assert.GreaterOrEqual(closest, 0.5f + DefaultEnemySpawner.BodyRadius - Tolerance, "but never into the door cells");
                    Assert.AreEqual(0, enemy.Bounds.Corrections, "the velocity constraint kept it inside; no pull-back was needed");
                    directions.Add(socket.Direction);
                    checkedDoors++;
                    Object.DestroyImmediate(enemy.gameObject);
                    Object.DestroyImmediate(target);
                }
            }

            Assert.GreaterOrEqual(checkedDoors, 6);
            Assert.AreEqual(4, directions.Count, "north, east, south and west exits were all exercised");
        }

        [UnityTest]
        public IEnumerator RangedEnemy_AtAnOpenDoorway_NeitherAdvancesNorBacksOutOfTheRoom()
        {
            var definition = CombatRoomsOfEveryBiome().First();
            var (runtime, root) = Room(definition, new Vector2(1400f, 1000f));
            var interior = runtime.InteriorWorldBounds;
            var socket = root.GetSockets().First(s => !RoomExitSealer.IsSealed(root, s));
            var (doorway, inside, outside, step) = SocketPoints(root, socket);
            runtime.UnlockDoors();

            // Far target beyond the doorway: the shooter approaches and holds at the edge.
            var far = Target(outside + step * 6f);
            var shooter = Enemy("shooter", inside, far.transform);
            runtime.BindEncounterBounds(shooter.gameObject);
            for (var i = 0; i < 120; i++)
            {
                yield return new WaitForFixedUpdate();
                Assert.IsTrue(ColliderInside(interior, shooter, DefaultEnemySpawner.BodyRadius), $"advance: left the interior at {shooter.transform.position}");
            }

            // A target pressing in from inside the room: the shooter backs off toward the doorway and still stays in.
            var near = Target((Vector2)shooter.transform.position - step * 1.5f);
            shooter.SetTarget(near.transform);
            for (var i = 0; i < 120; i++)
            {
                yield return new WaitForFixedUpdate();
                Assert.IsTrue(ColliderInside(interior, shooter, DefaultEnemySpawner.BodyRadius), $"back-off: left the interior at {shooter.transform.position}");
            }
        }

        [UnityTest]
        public IEnumerator ChargerCharge_AimedThroughAnOpenDoorway_EndsAtTheLegalEdge()
        {
            var definition = CombatRoomsOfEveryBiome().Skip(1).First();
            var (runtime, root) = Room(definition, new Vector2(1500f, 1000f));
            var interior = runtime.InteriorWorldBounds;
            var socket = root.GetSockets().First(s => !RoomExitSealer.IsSealed(root, s));
            var (doorway, inside, outside, step) = SocketPoints(root, socket);
            runtime.UnlockDoors();
            var target = Target(doorway + step * 0.5f);
            var charger = Enemy("charger", doorway - step * 3.5f, target.transform);
            runtime.BindEncounterBounds(charger.gameObject);
            var attack = charger.GetComponent<EnemyChargeAttack>();
            Assert.IsNotNull(attack);
            var deadline = Time.time + 4f;
            while (Time.time < deadline && attack.ChargesStarted == 0)
            {
                yield return new WaitForFixedUpdate();
                Assert.IsTrue(ColliderInside(interior, charger, DefaultEnemySpawner.BodyRadius));
            }

            Assert.AreEqual(1, attack.ChargesStarted, "the charger charged at the target in the doorway");
            for (var i = 0; i < 60; i++)
            {
                yield return new WaitForFixedUpdate();
                Assert.IsTrue(ColliderInside(interior, charger, DefaultEnemySpawner.BodyRadius), $"charge: left the interior at {charger.transform.position}");
            }

            Assert.IsTrue(attack.Resolver.LastDashStoppedByBounds || attack.LastChargeStoppedByWall, "the dash endpoint was constrained to the room");
        }

        // ---- Elite and Boss -------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Elite_BoundByItsRoom_HoldsAtTheOpenDoorway()
        {
            var definition = CombatRoomsOfEveryBiome().First();
            var (runtime, root) = Room(definition, new Vector2(1600f, 1000f));
            var interior = runtime.InteriorWorldBounds;
            var socket = root.GetSockets().First(s => !RoomExitSealer.IsSealed(root, s));
            var (doorway, inside, outside, step) = SocketPoints(root, socket);
            runtime.UnlockDoors();
            var target = Target(outside + step * 2f);
            var stalker = AssetDatabase.LoadAssetAtPath<EliteDefinition>("Assets/Game/ScriptableObjects/Enemies/Elites/Elite_TunnelStalker.asset");
            var encounter = new DefaultEliteSpawner(_catalog.Stagger).Spawn(stalker, inside, root.transform, target.transform);
            _created.Add(encounter.gameObject);
            runtime.BindEncounterBounds(encounter.Elite.gameObject);
            var radius = Radius(encounter.Elite);
            for (var i = 0; i < 150; i++)
            {
                yield return new WaitForFixedUpdate();
                Assert.IsTrue(ColliderInside(interior, encounter.Elite, radius), $"elite left the interior at {encounter.Elite.transform.position}");
            }
        }

        private RoomRuntime BossArena(Biome biome, Vector2 origin)
        {
            var definition = _catalog.Rooms.First(r => r.Biome == biome && r.RoomType == RoomType.Boss && r.Prefab != null);
            var (runtime, _) = Room(definition, origin, nodeId: 9);
            return runtime;
        }

        [UnityTest]
        public IEnumerator Boss_PursuingAPlayerBeyondTheOpenArenaDoor_StopsAtTheArenaEdge_InEveryBiome()
        {
            var index = 0;
            foreach (var (biome, bossPath) in new[]
            {
                (Biome.RuinedMetro, "Assets/Game/ScriptableObjects/Enemies/Bosses/Boss_TunnelMaw.asset"),
                (Biome.Rustworks, "Assets/Game/ScriptableObjects/Enemies/Bosses/Boss_TheFoundryTitan.asset"),
                (Biome.OvergrownLabs, "Assets/Game/ScriptableObjects/Enemies/Bosses/Boss_SubjectOmega.asset")
            })
            {
                var arena = BossArena(biome, new Vector2(4000f + index++ * 200f, 1000f));
                var root = arena.Root;
                var interior = arena.InteriorWorldBounds;
                var socket = root.GetSockets().First(s => !RoomExitSealer.IsSealed(root, s));
                var (doorway, inside, outside, step) = SocketPoints(root, socket);
                arena.UnlockDoors();
                var bossDefinition = AssetDatabase.LoadAssetAtPath<BossDefinition>(bossPath);
                Assert.IsNotNull(bossDefinition, bossPath);
                var target = Target(outside + step * 4f);
                var anchor = root.GetMarkers(RoomMarkerRole.BossAnchor).FirstOrDefault();
                var start = anchor != null ? (Vector2)root.transform.TransformPoint(anchor.WorldCenter) : interior.center;
                var encounter = new DefaultBossSpawner(new[] { bossDefinition }, _catalog.Stagger).Spawn(bossDefinition, start, root.transform, target.transform);
                _created.Add(encounter.gameObject);
                arena.BindEncounterBounds(encounter.Boss.gameObject);
                encounter.Boss.SuppressAttacks = true; // pursuit only: the question is where the body may go, not what it fires
                var radius = Radius(encounter.Boss);
                var closest = float.PositiveInfinity;
                for (var i = 0; i < 400; i++)
                {
                    yield return new WaitForFixedUpdate();
                    Assert.IsTrue(ColliderInside(interior, encounter.Boss, radius), $"{biome}: the boss left the arena at {encounter.Boss.transform.position} (interior {interior})");
                    closest = Mathf.Min(closest, Vector2.Dot(doorway - (Vector2)encounter.Boss.transform.position, step));
                }

                Assert.GreaterOrEqual(closest, 0.5f + radius - Tolerance, $"{biome}: the boss never entered the door cells");
                Assert.Less(closest, 0.5f + radius + 1f, $"{biome}: it pursued all the way to the arena edge (closest {closest})");
                Assert.AreEqual(MovesetActorState.Chase, encounter.Boss.State, $"{biome}: still pursuing, held at the edge");
                Object.DestroyImmediate(encounter.gameObject);
                Object.DestroyImmediate(target);
            }
        }

        [UnityTest]
        public IEnumerator BossDash_AimedThroughTheOpenArenaDoor_EndsAtTheArenaEdge()
        {
            var arena = BossArena(Biome.OvergrownLabs, new Vector2(4600f, 1000f));
            var root = arena.Root;
            var interior = arena.InteriorWorldBounds;
            var socket = root.GetSockets().First(s => !RoomExitSealer.IsSealed(root, s));
            var (doorway, inside, outside, step) = SocketPoints(root, socket);
            arena.UnlockDoors();
            var omega = AssetDatabase.LoadAssetAtPath<BossDefinition>("Assets/Game/ScriptableObjects/Enemies/Bosses/Boss_SubjectOmega.asset");
            var charge = AssetDatabase.LoadAssetAtPath<EnemyAttackDefinition>("Assets/Game/ScriptableObjects/Enemies/Attacks/Attack_Omega_Charge.asset");
            Assert.AreEqual(AttackMotion.Dash, charge.Motion);
            var encounter = new DefaultBossSpawner(new[] { omega }, _catalog.Stagger).Spawn(omega, doorway - step * 4f, root.transform, null);
            _created.Add(encounter.gameObject);
            arena.BindEncounterBounds(encounter.Boss.gameObject);
            var boss = encounter.Boss;
            boss.enabled = false; // the resolver is driven by hand: only the dash itself is under test
            var radius = Radius(boss);
            var resolver = boss.Resolver;
            resolver.Begin(charge, step);
            var steps = 0;
            while (!resolver.Tick(Time.fixedDeltaTime) && steps++ < 200)
            {
                yield return new WaitForFixedUpdate();
                Assert.IsTrue(ColliderInside(interior, boss, radius, 0.1f), $"dash: the boss left the arena at {boss.transform.position}");
            }

            yield return Steps(5);
            Assert.IsTrue(resolver.LastDashStoppedByBounds || resolver.LastDashStoppedByWall, "the dash ended at the arena edge");
            Assert.IsTrue(ColliderInside(interior, boss, radius), $"after the dash the boss is inside at {boss.transform.position}");
            Assert.GreaterOrEqual(Vector2.Dot(doorway - (Vector2)boss.transform.position, step), 0.5f + radius - 0.1f, "not in the door cells");
        }

        // ---- knockback ------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Knockback_TowardAnOpenDoorway_EndsAtTheLegalEdge_WithoutAWallImpactBonus()
        {
            var definition = CombatRoomsOfEveryBiome().Last();
            var (runtime, root) = Room(definition, new Vector2(1700f, 1000f));
            var interior = runtime.InteriorWorldBounds;
            var socket = root.GetSockets().First(s => !RoomExitSealer.IsSealed(root, s));
            var (doorway, inside, outside, step) = SocketPoints(root, socket);
            runtime.UnlockDoors();
            var enemy = Enemy("grunt", doorway - step * 1.8f, null);
            runtime.BindEncounterBounds(enemy.gameObject);
            yield return new WaitForFixedUpdate();
            var impact = enemy.Impact;
            Assert.IsNotNull(impact);
            ImpactDispatcher.Apply(enemy, new ImpactRequest(step, 6f, 0f));
            for (var i = 0; i < 40; i++)
            {
                yield return new WaitForFixedUpdate();
                Assert.IsTrue(ColliderInside(interior, enemy, DefaultEnemySpawner.BodyRadius), $"knockback: left the interior at {enemy.transform.position}");
            }

            Assert.IsFalse(impact.IsKnockbackActive);
            Assert.AreEqual(0, impact.WallImpacts, "an open doorway is not a wall: no wall-impact bonus");
            Assert.GreaterOrEqual(impact.BoundsStops, 1, "the knockback was cut at the legal edge");
        }

        // ---- ownership binding through the room --------------------------------------------------------------------

        [UnityTest]
        public IEnumerator EverythingARoomSpawns_IsBoundToThatRoom_IncludingEventWavesInAnUnlockedRoom()
        {
            var definition = CombatRoomsOfEveryBiome().First();
            var (runtime, root) = Room(definition, new Vector2(1800f, 1000f), nodeId: 5);
            runtime.SetSpawner(new DefaultEnemySpawner(_catalog.Stagger));
            var grunt = _catalog.Enemies.First(e => e.Id == "grunt");
            var context = new EncounterContext(1, 1, 1, definition.Biome, 5, System.Array.Empty<string>(), 4);
            var plan = new EncounterPlan(context, 3f, 6f, 4f, new[] { new EncounterEntry(grunt, 3) });
            var player = Target(runtime.InteriorWorldBounds.center);
            var spawned = new List<EnemyController>();
            runtime.EnemySpawned += (_, e) => spawned.Add(e);
            var wave = runtime.SpawnAdditionalEncounter(plan, player);
            Assert.IsNotNull(wave);
            yield return null;
            Assert.AreEqual(3, spawned.Count);
            foreach (var enemy in spawned)
            {
                Assert.IsNotNull(enemy.Bounds, enemy.name + ": has bounds");
                Assert.IsTrue(enemy.Bounds.IsBound);
                Assert.AreEqual(5, enemy.Bounds.RoomNodeId, "owned by the room that spawned it");
                Assert.AreEqual(runtime.InteriorWorldBounds, enemy.Bounds.Interior);
            }

            Assert.IsFalse(runtime.DoorsLocked, "an event wave does not lock the room — the bounds are what contain it");
        }

        [UnityTest]
        public IEnumerator ComposedBoss_IsBoundToItsArena_AndDoesNotChaseBeforeThePlayerEnters()
        {
            var arena = BossArena(Biome.RuinedMetro, new Vector2(5000f, 1000f));
            var maw = AssetDatabase.LoadAssetAtPath<BossDefinition>("Assets/Game/ScriptableObjects/Enemies/Bosses/Boss_TunnelMaw.asset");
            var services = new DungeonRuntimeServices
            {
                LootCatalog = AssetDatabase.LoadAssetAtPath<LootSourceCatalog>("Assets/Game/ScriptableObjects/Loot/LootSourceCatalog.asset"),
                GroundLoot = new GroundLootRegistry(),
                BossSpawner = new RosterBossSpawner(new DefaultBossSpawner(new[] { maw }))
            };
            // A "player" already exists elsewhere on the depth when the arena is composed — exactly the live situation.
            var player = PlayerBody(new Vector2(5000f - 30f, 1000f));
            player.AddComponent<PlayerInput>();
            var context = new DungeonRuntimeContext(11, 1, 1, System.Array.Empty<EnemyDefinition>(), new DefaultEnemySpawner());
            var binding = RoomCategoryComposer.Compose(arena, context, services);
            Assert.IsNotNull(binding.Boss);
            var boss = binding.Boss.Boss;
            Assert.IsNotNull(boss.Bounds, "the arena owns the boss");
            Assert.IsTrue(boss.Bounds.IsBound);
            Assert.AreEqual(arena.InteriorWorldBounds, boss.Bounds.Interior);
            Assert.IsNull(boss.Target, "no target before the arena is entered: the boss does not leave to meet a player elsewhere");
            var start = boss.transform.position;
            yield return Steps(30);
            Assert.AreEqual(MovesetActorState.Idle, boss.State);
            Assert.IsFalse(boss.EncounterStarted);
            Assert.Less(Vector2.Distance(start, boss.transform.position), 0.05f, "it has not moved");

            Assert.IsTrue(arena.NotifyPlayerEntered(player));
            Assert.AreSame(player.transform, boss.Target, "entering the arena hands the boss its target");
            yield return Steps(3);
            Assert.IsTrue(boss.EncounterStarted);
        }

        // ---- door timing: a pending lock is not a gap ------------------------------------------------------------

        [UnityTest]
        public IEnumerator PendingLock_WithAPlayerStandingInTheDoorway_DoesNotLetTheEnemyOut()
        {
            var definition = CombatRoomsOfEveryBiome().Skip(2).First();
            var (runtime, root) = Room(definition, new Vector2(1900f, 1000f));
            var interior = runtime.InteriorWorldBounds;
            var socket = root.GetSockets().First(s => !RoomExitSealer.IsSealed(root, s));
            var (doorway, inside, outside, step) = SocketPoints(root, socket);
            var blockingPlayer = PlayerBody(doorway + step * 0.2f);
            yield return new WaitForFixedUpdate();
            runtime.LockDoors();
            var door = runtime.Doors.First(d => d.Socket == socket);
            Assert.IsTrue(door.IsPending, "the lock waits for the player to leave the doorway");
            Assert.IsFalse(door.IsBlocking, "so the doorway is physically open right now");

            var enemy = Enemy("grunt", inside, blockingPlayer.transform);
            runtime.BindEncounterBounds(enemy.gameObject);
            for (var i = 0; i < 150; i++)
            {
                yield return new WaitForFixedUpdate();
                Assert.IsTrue(ColliderInside(interior, enemy, DefaultEnemySpawner.BodyRadius), $"pending lock: left the interior at {enemy.transform.position}");
            }

            Assert.IsTrue(door.IsPending, "the player still stands there");
        }

        // ---- network ---------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ReplicatedPosition_OfABoundEnemy_IsAlwaysInsideItsRoom()
        {
            var definition = CombatRoomsOfEveryBiome().First();
            var (runtime, root) = Room(definition, new Vector2(2100f, 1000f));
            var interior = runtime.InteriorWorldBounds;
            var socket = root.GetSockets().First(s => !RoomExitSealer.IsSealed(root, s));
            var (doorway, inside, outside, step) = SocketPoints(root, socket);
            runtime.UnlockDoors();
            var target = Target(outside + step * 3f);
            var host = Enemy("grunt", inside, target.transform);
            runtime.BindEncounterBounds(host.gameObject);
            var registry = new EnemyReplicaRegistry();
            var replica = registry.Spawn(new EnemySpawnRecord { NetId = 3, DefinitionId = "grunt", Position = host.transform.position });
            _created.Add(replica.gameObject);
            uint version = 1;
            for (var i = 0; i < 120; i++)
            {
                yield return new WaitForFixedUpdate();
                var state = AuthoritativeEnemySpawner.Capture(3, host, version++, Time.timeAsDouble);
                Assert.IsTrue(host.Bounds.ColliderInside(state.Position), $"the authoritative network position {state.Position} is inside the room");
                replica.Apply(state);
            }

            Assert.IsTrue(host.Bounds.ColliderInside(replica.SampledPosition(Time.timeAsDouble), 0.1f), "the client sees a position inside the room");
        }
    }
}

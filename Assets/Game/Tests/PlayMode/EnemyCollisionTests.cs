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
using RuinRail.Networking;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Enemies are solid against the world (§6). Root cause of the wall-phasing: normal enemies were spawned with a
    /// Rigidbody2D but no collider at all, so nothing in the physics scene could stop them. These tests pin the body,
    /// the steering (slide, corner, stop when boxed), dashes/lunges/knockback ending at walls, spawn rejection inside
    /// solid geometry, closed doors as walls and open doorways as routes, across the shipped rooms of all three biomes,
    /// plus the invariant that no enemy body ever penetrates solid geometry.
    /// </summary>
    public sealed class EnemyCollisionTests
    {
        private readonly List<Object> _created = new();
        private GameContentCatalog _catalog;

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

        private GameObject Wall(Vector2 centre, Vector2 size, float angle = 0f)
        {
            var wall = new GameObject("Wall");
            _created.Add(wall);
            wall.transform.position = centre;
            wall.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            wall.AddComponent<BoxCollider2D>().size = size;
            wall.AddComponent<EnvironmentObstacle>();
            return wall;
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

        private EnemyController Enemy(string id, Vector2 at, Transform target)
        {
            var definition = _catalog.Enemies.First(e => e.Id == id);
            var enemy = new DefaultEnemySpawner(_catalog.Stagger).Spawn(definition, at, target);
            _created.Add(enemy.gameObject);
            if (enemy.GetComponent<ProjectilePool>() == null) enemy.gameObject.AddComponent<ProjectilePool>();
            return enemy;
        }

        private static IEnumerator Steps(int n)
        {
            for (var i = 0; i < n; i++) yield return new WaitForFixedUpdate();
        }

        private static readonly Collider2D[] Overlaps = new Collider2D[16];

        /// <summary>The body's penetration into any solid EnvironmentObstacle collider (0 = touching or clear).</summary>
        private static float Penetration(Component actor, float radius)
        {
            var position = (Vector2)actor.transform.position;
            var count = Physics2D.OverlapCircle(position, radius, ContactFilter2D.noFilter, Overlaps);
            var worst = 0f;
            for (var i = 0; i < count; i++)
            {
                var c = Overlaps[i];
                if (c == null || c.isTrigger || !c.enabled || c.GetComponentInParent<EnvironmentObstacle>() == null || c.transform.IsChildOf(actor.transform)) continue;
                var closest = c.ClosestPoint(position);
                var distance = Vector2.Distance(closest, position);
                var inside = c.OverlapPoint(position);
                worst = Mathf.Max(worst, inside ? radius + distance : radius - distance);
            }

            return worst;
        }

        private const float Tolerance = 0.12f; // physics contact offset + one solver step

        // ---- normal enemy body vs walls ---------------------------------------------------------------------------

        [Test]
        public void SpawnedNormalEnemy_HasASolidBodyAndAHurtbox_AndItsLayerIsSolidAgainstTheWorld()
        {
            var enemy = Enemy("grunt", new Vector2(100f, 100f), null);
            var body = enemy.GetComponent<Rigidbody2D>();
            Assert.IsNotNull(body);
            Assert.AreEqual(RigidbodyType2D.Dynamic, body.bodyType);
            Assert.IsTrue(body.freezeRotation);
            var circle = enemy.GetComponent<CircleCollider2D>();
            Assert.IsNotNull(circle, "the body collider that was missing");
            Assert.IsFalse(circle.isTrigger);
            Assert.AreEqual(DefaultEnemySpawner.BodyRadius, circle.radius, 0.001f);
            Assert.AreEqual(CombatLayers.Enemy, enemy.gameObject.layer);
            Assert.IsNotNull(enemy.GetComponentInChildren<CombatHurtbox>());
            Assert.IsFalse(Physics2D.GetIgnoreLayerCollision(CombatLayers.Enemy, 0), "enemy bodies collide with world geometry (Default layer)");
            Assert.IsTrue(Physics2D.GetIgnoreLayerCollision(CombatLayers.Enemy, CombatLayers.Player), "enemy bodies never shove player bodies (pre-existing feel preserved)");
        }

        [UnityTest]
        public IEnumerator StraightWall_StopsTheChase_NoPenetration()
        {
            var wall = Wall(new Vector2(103f, 100f), new Vector2(0.5f, 8f));
            var target = Target(new Vector2(108f, 100f));
            var enemy = Enemy("grunt", new Vector2(100f, 100f), target.transform);
            var worst = 0f;
            for (var i = 0; i < 120; i++)
            {
                yield return new WaitForFixedUpdate();
                worst = Mathf.Max(worst, Penetration(enemy, DefaultEnemySpawner.BodyRadius));
            }

            Assert.LessOrEqual(enemy.transform.position.x, 103f - 0.25f - DefaultEnemySpawner.BodyRadius + Tolerance, "stopped on the near side of the wall");
            Assert.LessOrEqual(worst, Tolerance, "never inside the wall");
            Assert.AreEqual(EnemyState.Chase, enemy.State, "still chasing (a wall is not a smoke zone)");
        }

        [UnityTest]
        public IEnumerator DiagonalWall_SlidesAlongIt_WithoutPenetration()
        {
            Wall(new Vector2(104f, 100f), new Vector2(0.5f, 10f), 45f);
            var target = Target(new Vector2(110f, 100f));
            var enemy = Enemy("grunt", new Vector2(100f, 100f), target.transform);
            var worst = 0f;
            var start = (Vector2)enemy.transform.position;
            for (var i = 0; i < 150; i++)
            {
                yield return new WaitForFixedUpdate();
                worst = Mathf.Max(worst, Penetration(enemy, DefaultEnemySpawner.BodyRadius));
            }

            var moved = (Vector2)enemy.transform.position - start;
            Assert.LessOrEqual(worst, Tolerance);
            Assert.Greater(Mathf.Abs(moved.y), 1f, "slid along the diagonal instead of stopping dead or pushing through");
            Assert.Greater(enemy.Steering.Deflections, 0, "the steering deflected the heading");
        }

        [UnityTest]
        public IEnumerator Corner_IsRoundedOrHeldAt_NeverCut()
        {
            // An L-shaped corner between the enemy and its target.
            Wall(new Vector2(103f, 101f), new Vector2(0.5f, 4f));
            Wall(new Vector2(101.25f, 103f), new Vector2(4f, 0.5f));
            var target = Target(new Vector2(106f, 105f));
            var enemy = Enemy("grunt", new Vector2(100f, 100f), target.transform);
            var worst = 0f;
            for (var i = 0; i < 200; i++)
            {
                yield return new WaitForFixedUpdate();
                worst = Mathf.Max(worst, Penetration(enemy, DefaultEnemySpawner.BodyRadius));
            }

            Assert.LessOrEqual(worst, Tolerance, "the corner is never cut through");
        }

        [UnityTest]
        public IEnumerator BoxedIn_StopsInsteadOfJittering()
        {
            var centre = new Vector2(100f, 100f);
            Wall(centre + new Vector2(1.2f, 0f), new Vector2(0.4f, 3f));
            Wall(centre + new Vector2(-1.2f, 0f), new Vector2(0.4f, 3f));
            Wall(centre + new Vector2(0f, 1.2f), new Vector2(3f, 0.4f));
            Wall(centre + new Vector2(0f, -1.2f), new Vector2(3f, 0.4f));
            var target = Target(centre + new Vector2(6f, 0f));
            var enemy = Enemy("grunt", centre, target.transform);
            yield return Steps(90);
            var worst = 0f;
            for (var i = 0; i < 60; i++)
            {
                yield return new WaitForFixedUpdate();
                worst = Mathf.Max(worst, Penetration(enemy, DefaultEnemySpawner.BodyRadius));
            }

            Assert.LessOrEqual(worst, Tolerance);
            Assert.Less(Vector2.Distance(enemy.transform.position, centre), 1.2f, "contained");
        }

        [UnityTest]
        public IEnumerator RangedEnemy_KeepsDistance_AndNeverPhasesThroughTheWallBehindIt()
        {
            // A shooter backing away from a too-close player with a wall right behind it.
            Wall(new Vector2(98.2f, 100f), new Vector2(0.5f, 8f));
            var target = Target(new Vector2(100.8f, 100f));
            var enemy = Enemy("shooter", new Vector2(100f, 100f), target.transform);
            Assume.That(enemy.Definition.KeepsDistance);
            var worst = 0f;
            for (var i = 0; i < 100; i++)
            {
                yield return new WaitForFixedUpdate();
                worst = Mathf.Max(worst, Penetration(enemy, DefaultEnemySpawner.BodyRadius));
            }

            Assert.LessOrEqual(worst, Tolerance);
            Assert.GreaterOrEqual(enemy.transform.position.x, 98.45f + DefaultEnemySpawner.BodyRadius - Tolerance, "backed up to the wall, not through it");
        }

        [UnityTest]
        public IEnumerator ChargerCharge_StopsAtTheWall_BodyNeverInsideIt()
        {
            var wall = Wall(new Vector2(102.5f, 100f), new Vector2(0.5f, 6f));
            var target = Target(new Vector2(104f, 100f));
            var enemy = Enemy("charger", new Vector2(100f, 100f), target.transform);
            var attack = enemy.GetComponent<EnemyChargeAttack>();
            Assert.IsNotNull(attack);
            var worst = 0f;
            var deadline = Time.time + 3f;
            while (Time.time < deadline && attack.ChargesStarted == 0) { yield return new WaitForFixedUpdate(); worst = Mathf.Max(worst, Penetration(enemy, DefaultEnemySpawner.BodyRadius)); }
            Assert.AreEqual(1, attack.ChargesStarted, "the charger charged");
            for (var i = 0; i < 60; i++) { yield return new WaitForFixedUpdate(); worst = Mathf.Max(worst, Penetration(enemy, DefaultEnemySpawner.BodyRadius)); }
            Assert.IsTrue(attack.LastChargeStoppedByWall);
            Assert.LessOrEqual(worst, Tolerance);
            Assert.Less(enemy.transform.position.x, 102.25f, "on the near side of the wall");
        }

        [UnityTest]
        public IEnumerator EliteLunge_AndBossDash_EndAtTheWall_NeverBeyondIt()
        {
            var lunge = AssetDatabase.LoadAssetAtPath<EnemyAttackDefinition>("Assets/Game/ScriptableObjects/Enemies/Attacks/Attack_TunnelStalker_Lunge.asset");
            var charge = AssetDatabase.LoadAssetAtPath<EnemyAttackDefinition>("Assets/Game/ScriptableObjects/Enemies/Attacks/Attack_Omega_Charge.asset");
            Assert.IsNotNull(lunge); Assert.IsNotNull(charge);
            Assert.AreEqual(AttackMotion.Dash, lunge.Motion); Assert.AreEqual(AttackMotion.Dash, charge.Motion);

            var elite = new DefaultEliteSpawner().Spawn(AssetDatabase.LoadAssetAtPath<EliteDefinition>("Assets/Game/ScriptableObjects/Enemies/Elites/Elite_TunnelStalker.asset"), new Vector2(100f, 100f), null, null);
            _created.Add(elite.gameObject);
            var omega = AssetDatabase.LoadAssetAtPath<BossDefinition>("Assets/Game/ScriptableObjects/Enemies/Bosses/Boss_SubjectOmega.asset");
            var boss = new DefaultBossSpawner(new[] { omega }, _catalog.Stagger).Spawn(omega, new Vector2(100f, 120f), null);
            _created.Add(boss.gameObject);
            Wall(new Vector2(101.5f, 100f), new Vector2(0.5f, 6f));
            Wall(new Vector2(103f, 120f), new Vector2(0.5f, 6f));
            yield return new WaitForFixedUpdate();

            foreach (var (actor, attack, wallX) in new (MovesetActorController, EnemyAttackDefinition, float)[] { (elite.Elite, lunge, 101.5f), (boss.Boss, charge, 103f) })
            {
                actor.enabled = false; // the resolver is driven by hand: only the dash itself is under test
                var resolver = actor.Resolver;
                var radius = actor.GetComponent<CircleCollider2D>() != null ? actor.GetComponent<CircleCollider2D>().radius : 0.5f;
                resolver.Begin(attack, Vector2.right);
                var worst = 0f;
                var steps = 0;
                while (!resolver.Tick(Time.fixedDeltaTime) && steps++ < 200)
                {
                    yield return new WaitForFixedUpdate();
                    worst = Mathf.Max(worst, Penetration(actor, radius));
                }

                yield return Steps(5);
                worst = Mathf.Max(worst, Penetration(actor, radius));
                Assert.IsTrue(resolver.LastDashStoppedByWall, actor.name + ": the dash ended at the wall");
                Assert.Less(actor.transform.position.x, wallX - 0.25f, actor.name + ": on the near side");
                Assert.LessOrEqual(worst, Tolerance, actor.name + ": never inside the wall");
            }
        }

        [UnityTest]
        public IEnumerator Knockback_IntoAWall_StopsAtTheWall()
        {
            Wall(new Vector2(102f, 100f), new Vector2(0.5f, 6f));
            var enemy = Enemy("grunt", new Vector2(100.5f, 100f), null);
            yield return new WaitForFixedUpdate();
            ImpactDispatcher.Apply(enemy, new ImpactRequest(Vector2.right, 4f, 0f));
            var worst = 0f;
            for (var i = 0; i < 40; i++) { yield return new WaitForFixedUpdate(); worst = Mathf.Max(worst, Penetration(enemy, DefaultEnemySpawner.BodyRadius)); }
            Assert.LessOrEqual(worst, Tolerance);
            Assert.Less(enemy.transform.position.x, 101.75f - DefaultEnemySpawner.BodyRadius + Tolerance, "a 4-tile knockback stops at the wall 1.5 tiles away");
        }

        [UnityTest]
        public IEnumerator SpawnPoints_InsideSolidGeometry_AreRejected()
        {
            Wall(new Vector2(100f, 100f), new Vector2(2f, 2f));
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(RoomRuntime.IsSpawnClear(new Vector2(100f, 100f)), "inside a wall");
            Assert.IsFalse(RoomRuntime.IsSpawnClear(new Vector2(101.1f, 100f)), "a body radius from the wall face still overlaps");
            Assert.IsTrue(RoomRuntime.IsSpawnClear(new Vector2(101.5f, 100f)), "clear floor");
        }

        // ---- shipped rooms: doors and containment across the biomes ---------------------------------------------

        private (RoomRuntime runtime, RoomRoot root) Room(RoomDefinition definition, Vector2 origin)
        {
            var instance = Object.Instantiate(definition.Prefab, new Vector3(origin.x, origin.y, 0f), Quaternion.identity);
            _created.Add(instance);
            var root = instance.GetComponent<RoomRoot>();
            var runtime = instance.AddComponent<RoomRuntime>();
            runtime.Configure(root, 1, 1, 1);
            return (runtime, root);
        }

        private static (Vector2 doorway, Vector2 inside, Vector2 outside) SocketPoints(RoomRoot root, DoorSocket socket)
        {
            var step = (Vector2)DoorDirections.Step(socket.Direction);
            var cells = socket.Cells();
            var centre = Vector2.zero;
            foreach (var cell in cells) centre += (Vector2)root.transform.TransformPoint(GridCoordinates.CellToWorldCenter(cell));
            centre /= cells.Length;
            return (centre, centre - step * 2.5f, centre + step * 2.5f);
        }

        private static Rect RoomRect(RoomRoot root)
        {
            var min = (Vector2)root.transform.position;
            return new Rect(min, (Vector2)root.Size * GridConstants.TileWorldSize);
        }

        [UnityTest]
        public IEnumerator ClosedDoor_IsAWall_OpenDoorway_IsARoute_InEveryBiome()
        {
            var index = 0;
            foreach (var biome in new[] { Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs })
            {
                var definition = _catalog.Rooms.First(r => r.Biome == biome && r.RoomType == RoomType.Combat && r.SizeClass == RoomSizeClass.Medium);
                var (runtime, root) = Room(definition, new Vector2(1000f + index++ * 80f, 1000f));
                var socket = root.GetSockets().First();
                var (doorway, inside, outside) = SocketPoints(root, socket);
                var target = Target(outside);
                var enemy = Enemy("grunt", inside, target.transform);
                yield return new WaitForFixedUpdate();

                // Locked: the enemy cannot leave.
                runtime.LockDoors();
                yield return new WaitForFixedUpdate();
                var worst = 0f;
                for (var i = 0; i < 100; i++) { yield return new WaitForFixedUpdate(); worst = Mathf.Max(worst, Penetration(enemy, DefaultEnemySpawner.BodyRadius)); }
                var step = (Vector2)DoorDirections.Step(socket.Direction);
                Assert.Less(Vector2.Dot((Vector2)enemy.transform.position - doorway, step), 0f, $"{biome}: held inside by the locked door");
                Assert.LessOrEqual(worst, Tolerance, $"{biome}: never inside the door blocker");

                // Open: the doorway is the route out.
                runtime.UnlockDoors();
                yield return new WaitForFixedUpdate();
                for (var i = 0; i < 150; i++)
                {
                    yield return new WaitForFixedUpdate();
                    worst = Mathf.Max(worst, Penetration(enemy, DefaultEnemySpawner.BodyRadius));
                    if (Vector2.Distance(enemy.transform.position, outside) < 1.2f) break;
                }

                Assert.Less(Vector2.Distance(enemy.transform.position, outside), 1.5f, $"{biome}: walked out through the open doorway");
                Assert.LessOrEqual(worst, Tolerance, $"{biome}: never through a wall on the way");
                Object.DestroyImmediate(enemy.gameObject);
                Object.DestroyImmediate(target);
            }
        }

        [UnityTest]
        public IEnumerator LockedRoom_ContainsEveryChasingEnemy_AndNoBodyPenetratesGeometry_AcrossBiomesAndSizes()
        {
            var index = 0;
            var checkedRooms = 0;
            var ids = new[] { "grunt", "charger", "shooter", "swarm", "brute" };
            foreach (var biome in new[] { Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs })
            foreach (var size in new[] { RoomSizeClass.Small, RoomSizeClass.Medium, RoomSizeClass.Large })
            {
                var definition = _catalog.Rooms.FirstOrDefault(r => r.Biome == biome && r.RoomType == RoomType.Combat && r.SizeClass == size);
                if (definition == null) continue;
                var (runtime, root) = Room(definition, new Vector2(2000f + index++ * 120f, 2000f));
                var rect = RoomRect(root);
                var spawns = runtime.SpawnPointsFor(rect.center);
                Assert.Greater(spawns.Count, 0, definition.Id + ": usable spawn points");
                foreach (var spawn in spawns) Assert.IsTrue(RoomRuntime.IsSpawnClear(spawn), definition.Id + ": every returned spawn point is clear of solid geometry");

                // Targets outside the room on all four sides; each enemy chases the nearest wall from its spawn.
                var targets = new[]
                {
                    Target(new Vector2(rect.xMin - 3f, rect.center.y)), Target(new Vector2(rect.xMax + 3f, rect.center.y)),
                    Target(new Vector2(rect.center.x, rect.yMin - 3f)), Target(new Vector2(rect.center.x, rect.yMax + 3f))
                };
                var enemies = new List<EnemyController>();
                for (var i = 0; i < Mathf.Min(ids.Length, spawns.Count); i++)
                {
                    var spawn = spawns[i % spawns.Count];
                    var nearest = targets.OrderBy(t => Vector2.Distance(t.transform.position, spawn)).First();
                    enemies.Add(Enemy(ids[i], spawn, nearest.transform));
                }

                runtime.LockDoors();
                yield return new WaitForFixedUpdate();
                var worst = 0f;
                for (var i = 0; i < 150; i++)
                {
                    yield return new WaitForFixedUpdate();
                    foreach (var enemy in enemies)
                    {
                        worst = Mathf.Max(worst, Penetration(enemy, DefaultEnemySpawner.BodyRadius));
                        Assert.IsTrue(rect.Contains(enemy.transform.position), $"{definition.Id}: {enemy.Definition.Id} left the room through solid geometry at {enemy.transform.position} (room {rect})");
                    }
                }

                Assert.LessOrEqual(worst, Tolerance, definition.Id + ": no body penetrated a wall or door blocker");
                foreach (var enemy in enemies) Object.DestroyImmediate(enemy.gameObject);
                foreach (var target in targets) Object.DestroyImmediate(target);
                checkedRooms++;
            }

            Assert.GreaterOrEqual(checkedRooms, 9, "three biomes × three size classes");
        }

        [UnityTest]
        public IEnumerator SealedSocket_IsWall_ForEnemiesToo()
        {
            var definition = _catalog.Rooms.First(r => r.Biome == Biome.Rustworks && r.RoomType == RoomType.Combat && r.Prefab.GetComponent<RoomRoot>().GetSockets().Count >= 3);
            var instance = Object.Instantiate(definition.Prefab, new Vector3(3000f, 3000f, 0f), Quaternion.identity);
            _created.Add(instance);
            var root = instance.GetComponent<RoomRoot>();
            var spare = root.GetSockets().Last();
            Assert.IsTrue(RoomExitSealer.Seal(root, spare));
            var runtime = instance.AddComponent<RoomRuntime>();
            runtime.Configure(root, 1, 1, 1);
            var (doorway, inside, outside) = SocketPoints(root, spare);
            var target = Target(outside);
            var enemy = Enemy("grunt", inside, target.transform);
            yield return new WaitForFixedUpdate();
            var worst = 0f;
            for (var i = 0; i < 120; i++) { yield return new WaitForFixedUpdate(); worst = Mathf.Max(worst, Penetration(enemy, DefaultEnemySpawner.BodyRadius)); }
            var step = (Vector2)DoorDirections.Step(spare.Direction);
            Assert.Less(Vector2.Dot((Vector2)enemy.transform.position - doorway, step), 0f, "a sealed socket is wall: the enemy stays inside");
            Assert.LessOrEqual(worst, Tolerance);
        }

        // ---- network ------------------------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ClientReplica_FollowsOnlyTheHostsWallConstrainedPosition_AndHasNoBodyOfItsOwn()
        {
            Wall(new Vector2(103f, 100f), new Vector2(0.5f, 8f));
            var target = Target(new Vector2(108f, 100f));
            var host = Enemy("grunt", new Vector2(100f, 100f), target.transform);
            var registry = new EnemyReplicaRegistry();
            var replica = registry.Spawn(new EnemySpawnRecord { NetId = 7, DefinitionId = "grunt", Position = host.transform.position });
            _created.Add(replica.gameObject);
            Assert.IsNull(replica.GetComponent<Rigidbody2D>(), "a replica has no physics body: it cannot move itself anywhere, let alone through a wall");
            Assert.IsNull(replica.GetComponent<EnemyController>(), "and no AI");

            uint version = 1;
            for (var i = 0; i < 120; i++)
            {
                yield return new WaitForFixedUpdate();
                replica.Apply(AuthoritativeEnemySpawner.Capture(7, host, version++, Time.timeAsDouble));
            }

            var sampled = replica.SampledPosition(Time.timeAsDouble);
            Assert.LessOrEqual(sampled.x, 103f - 0.25f - DefaultEnemySpawner.BodyRadius + Tolerance, "the replicated position is the host's wall-constrained one");
            Assert.Less(Vector2.Distance(sampled, host.transform.position), 0.5f, "the client sees the host position");
        }
    }
}

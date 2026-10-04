using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Networking;
using RuinRail.Presentation.Vfx;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// The danger telegraph tells the truth. For every shipped enemy, Elite and Boss attack the painted marker (the very
    /// pixels <see cref="TelegraphMarkerView"/> draws on the world grid) contains everything the attack really damages:
    /// the resolver's strike areas, the path a real dash body runs (stopping at walls), and the path every real projectile
    /// flies (stopping at walls). Timing follows the committed telegraph (boss phase-two scaling, a moveset enemy's own move
    /// timing), multi-hit moves stay drawn until their last window, a lobbed bomb's ring stays until it lands, and a co-op
    /// client draws the host's shapes and timings for normal enemies too.
    /// </summary>
    public sealed class AttackTelegraphTruthTests
    {
        private readonly List<Object> _created = new();
        private GameContentCatalog _catalog;
        private EffectPool _pool;

        [SetUp]
        public void SetUp()
        {
            DamageAuthority.LocalIsAuthoritative = true;
            _catalog = GameContentCatalog.Load();
            var go = new GameObject("Effects");
            _created.Add(go);
            _pool = go.AddComponent<EffectPool>();
            _pool.Configure(32);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
            foreach (var p in Object.FindObjectsByType<Projectile>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (p != null) Object.DestroyImmediate(p.gameObject);
            foreach (var v in Object.FindObjectsByType<TelegraphMarkerView>(FindObjectsSortMode.None)) if (v != null) Object.DestroyImmediate(v.gameObject);
            Time.timeScale = 1f;
        }

        private T Track<T>(T o) where T : Object { _created.Add(o is Component c ? c.gameObject : o); return o; }

        private static bool Painted(TelegraphMarkerView view, Vector2 p) =>
            view.PaintsPixel(Mathf.FloorToInt(p.x * TelegraphMarkerView.PixelsPerUnit), Mathf.FloorToInt(p.y * TelegraphMarkerView.PixelsPerUnit));

        /// <summary>Every shipped attack with the definition of the actor that owns it.</summary>
        private List<(EnemyAttackDefinition Attack, ScriptableObject Owner)> ShippedAttacks()
        {
            var list = new List<(EnemyAttackDefinition, ScriptableObject)>();
            void Add(EnemyAttackDefinition a, ScriptableObject owner) { if (a != null && list.All(x => x.Item1 != a)) list.Add((a, owner)); }
            foreach (var boss in _catalog.Bosses)
            {
                foreach (var a in boss.Moveset) Add(a, boss);
                foreach (var a in boss.PhaseTwoArenaHazards) Add(a, boss);
                Add(boss.SummonAttack, boss);
            }

            foreach (var elite in _catalog.Elites) foreach (var a in elite.Moveset) Add(a, elite);
            foreach (var enemy in _catalog.Enemies)
            {
                Add(enemy.ChargeAttack, enemy);
                foreach (var a in enemy.Moveset) Add(a, enemy);
            }

            return list;
        }

        /// <summary>The owner as shipped (its real body and collider), with its own brain switched off so the test drives the resolver.</summary>
        private GameObject SpawnOwner(ScriptableObject owner, Vector2 at)
        {
            GameObject go;
            switch (owner)
            {
                case BossDefinition boss:
                {
                    var encounter = new DefaultBossSpawner(_catalog.Bosses, _catalog.Stagger).Spawn(boss, at, null);
                    Track(encounter.gameObject);
                    encounter.Boss.enabled = false;
                    go = encounter.Boss.gameObject;
                    break;
                }
                case EliteDefinition elite:
                {
                    var encounter = new DefaultEliteSpawner(_catalog.Stagger).Spawn(elite, at, null, null);
                    Track(encounter.gameObject);
                    encounter.Elite.enabled = false;
                    go = encounter.Elite.gameObject;
                    break;
                }
                default:
                {
                    var enemy = new DefaultEnemySpawner(_catalog.Stagger).Spawn((EnemyDefinition)owner, at, null);
                    Track(enemy.gameObject);
                    enemy.enabled = false;
                    go = enemy.gameObject;
                    break;
                }
            }

            if (go.GetComponent<TeamMember>() == null) go.AddComponent<TeamMember>().SetTeam(DamageTeam.Enemy);
            if (go.GetComponent<ProjectilePool>() == null) go.AddComponent<ProjectilePool>();
            return go;
        }

        private TelegraphMarkerView Draw(EnemyAttackDefinition attack, Vector2 origin, Vector2 direction, Transform self, List<AttackFootprint.Shape> shapes)
        {
            shapes.Clear();
            TelegraphIndicator.FootprintOf(attack, origin, direction, self, shapes, out var pattern, out var sweep);
            var view = Track(TelegraphMarkerView.Create(null, "Truth_" + attack.name));
            view.SetFootprint(shapes, origin, pattern, sweep, true);
            view.FinishBuild();
            Assert.IsTrue(view.IsBuildComplete);
            view.Paint(new TelegraphLook { Colour = Color.red, Progress = 0.5f, Heavy = true });
            return view;
        }

        /// <summary>Points on the footprint's boundary (just inside it), about one per pixel.</summary>
        private static IEnumerable<Vector2> Boundary(AttackFootprint.Shape s, float inset = 0.01f)
        {
            const float Step = 1f / 32f;
            var n = new Vector2(-s.Direction.y, s.Direction.x);
            switch (s.Form)
            {
                case FootprintForm.Circle:
                {
                    var count = Mathf.Max(16, Mathf.CeilToInt(2f * Mathf.PI * s.Radius / Step));
                    for (var i = 0; i < count; i++)
                    {
                        var a = i * Mathf.PI * 2f / count;
                        yield return s.Centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (s.Radius - inset);
                    }

                    break;
                }
                case FootprintForm.Box:
                {
                    var hx = s.Size.x * 0.5f - inset;
                    var hy = s.Size.y * 0.5f - inset;
                    for (var t = -hx; t <= hx; t += Step) { yield return s.Centre + s.Direction * t + n * hy; yield return s.Centre + s.Direction * t - n * hy; }
                    for (var t = -hy; t <= hy; t += Step) { yield return s.Centre + n * t + s.Direction * hx; yield return s.Centre + n * t - s.Direction * hx; }
                    break;
                }
                default:
                {
                    var length = Vector2.Distance(s.Start, s.End);
                    var r = s.Radius - inset;
                    for (var t = 0f; t <= length; t += Step) { yield return s.Start + s.Direction * t + n * r; yield return s.Start + s.Direction * t - n * r; }
                    var count = Mathf.Max(16, Mathf.CeilToInt(2f * Mathf.PI * s.Radius / Step));
                    for (var i = 0; i < count; i++)
                    {
                        var a = i * Mathf.PI * 2f / count;
                        var o = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                        yield return s.Start + o;
                        yield return s.End + o;
                    }

                    break;
                }
            }
        }

        private static IEnumerable<Vector2> Ring(Vector2 c, float r, int count = 16)
        {
            for (var i = 0; i < count; i++)
            {
                var a = i * Mathf.PI * 2f / count;
                yield return c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
        }

        [UnityTest]
        public IEnumerator EveryShippedAttack_TheDrawnMarker_ContainsEverythingTheAttackReallyHits()
        {
            var attacks = ShippedAttacks();
            Assert.Greater(attacks.Count, 50, "every shipped enemy, Elite and Boss attack");
            var report = new List<string>();
            var shapes = new List<AttackFootprint.Shape>();
            for (var index = 0; index < attacks.Count; index++)
            {
                var (attack, owner) = attacks[index];
                var origin = new Vector2(3000f + (index % 8) * 90f, 3000f + (index / 8) * 90f);
                // Off-axis on purpose: the marker is rasterised on the world grid at any angle.
                var direction = (Vector2)(Quaternion.Euler(0f, 0f, 23f + index * 37f) * Vector2.right);
                var go = SpawnOwner(owner, origin);
                Physics2D.SyncTransforms();
                var view = Draw(attack, origin, direction, go.transform, shapes);
                Assert.IsTrue(view.IsVisible, attack.name + ": drawn");

                // 1. The raster never lies inside the footprint it stands for.
                foreach (var s in shapes)
                foreach (var p in Boundary(s))
                    Assert.IsTrue(Painted(view, p), $"{attack.name} ({attack.Motion}): footprint edge point {p - origin} is not painted");

                // 2. What the real attack does at runtime is inside the painted marker.
                var body = go.GetComponent<Rigidbody2D>();
                var resolver = new AttackResolver(go.transform, body, new FixedDamageRoller(), go.GetComponent<ProjectilePool>());
                var samples = 0;
                switch (attack.Motion)
                {
                    case AttackMotion.Dash:
                    {
                        resolver.Begin(attack, direction);
                        var until = Time.time + 5f; // batch PlayMode is uncapped: bound by time, not frames
                        while (resolver.IsRunning && Time.time < until)
                        {
                            resolver.Tick(Time.deltaTime);
                            foreach (var p in Ring(go.transform.position, attack.HitRadius * 0.98f)) { samples++; Assert.IsTrue(Painted(view, p), $"{attack.name}: the dash body's strike circle at {(Vector2)go.transform.position - origin} reaches past the lane"); }
                            yield return null;
                        }

                        for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
                        foreach (var p in Ring(go.transform.position, attack.HitRadius * 0.98f)) Assert.IsTrue(Painted(view, p), $"{attack.name}: where the dash came to rest is inside the lane");
                        Assert.IsFalse(resolver.IsRunning, attack.name + " finished");
                        break;
                    }
                    case AttackMotion.Projectile:
                    {
                        resolver.Begin(attack, direction);
                        var tracked = new HashSet<Projectile>();
                        var deadline = Time.time + attack.HitCount * attack.HitIntervalSeconds + attack.ProjectileRange / Mathf.Max(1f, attack.ProjectileSpeed) + 1.5f;
                        while (Time.time < deadline)
                        {
                            if (resolver.IsRunning) resolver.Tick(Time.deltaTime);
                            foreach (var shot in resolver.SpawnedProjectiles) tracked.Add(shot);
                            var flying = 0;
                            foreach (var shot in tracked)
                            {
                                if (shot == null || !shot.gameObject.activeInHierarchy) continue;
                                flying++;
                                var radius = shot.GetComponent<CircleCollider2D>().radius * 0.98f;
                                foreach (var p in Ring(shot.transform.position, radius, 8)) { samples++; Assert.IsTrue(Painted(view, p), $"{attack.name}: a real shot at {(Vector2)shot.transform.position - origin} flies outside its lane"); }
                            }

                            if (!resolver.IsRunning && flying == 0 && tracked.Count > 0) break;
                            yield return new WaitForFixedUpdate();
                        }

                        Assert.AreEqual(attack.HitCount * attack.ProjectileCount, resolver.SpawnedProjectiles.Count, attack.name + ": every volley fired");
                        break;
                    }
                    default:
                    {
                        // Point probes just inside the footprint edge and at its centre: every one is struck, and every
                        // struck one stands on painted pixels.
                        var strike = AttackFootprint.Strike(attack, origin, direction);
                        var edge = Boundary(strike, 0.04f).ToList();
                        var stride = Mathf.Max(1, Mathf.CeilToInt(edge.Count / 48f));
                        var points = edge.Where((_, i) => i % stride == 0).Append(strike.Centre).ToList();
                        var probes = new List<(Vector2 Point, TestDamageableTarget Probe)>();
                        foreach (var p in points)
                        {
                            var target = Track(new GameObject("Probe"));
                            target.transform.position = p;
                            var circle = target.AddComponent<CircleCollider2D>();
                            circle.radius = 0.02f;
                            circle.isTrigger = true;
                            target.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
                            probes.Add((p, target.AddComponent<TestDamageableTarget>()));
                        }

                        Physics2D.SyncTransforms();
                        resolver.Begin(attack, direction);
                        for (var guard = 0; guard < 400 && resolver.IsRunning; guard++) { resolver.Tick(Time.fixedDeltaTime); yield return new WaitForFixedUpdate(); }
                        foreach (var (p, probe) in probes)
                        {
                            samples++;
                            Assert.AreEqual(attack.HitCount, probe.HitCount, $"{attack.name}: a probe inside the footprint at {p - origin} is struck by every window");
                            Assert.IsTrue(Painted(view, p), $"{attack.name}: the struck probe at {p - origin} is on the marker");
                        }

                        foreach (var (_, probe) in probes) Object.DestroyImmediate(probe.gameObject);
                        break;
                    }
                }

                report.Add($"{attack.name} {attack.Motion}: {shapes.Count} shape(s), {view.CoveredPixels} px, {samples} runtime samples inside");
                Object.DestroyImmediate(view.gameObject);
                Object.DestroyImmediate(go.transform.root.gameObject);
            }

            Debug.Log("[TELEGRAPH-TRUTH]\n" + string.Join("\n", report));
        }

        /// <summary>
        /// A heavy footprint is rasterised over several frames (a per-frame budget): built in many tiny slices it paints
        /// exactly the pixels and colours of the same footprint built at once, and nothing shows until it is complete.
        /// </summary>
        [Test]
        public void HeavyFootprint_BuiltInSlices_PaintsExactlyWhatAOneShotBuildPaints()
        {
            var shapes = new List<AttackFootprint.Shape>();
            foreach (var (attack, _) in ShippedAttacks().OrderByDescending(a => a.Attack.ProjectileCount).Take(3))
            {
                var origin = new Vector2(7000f, 7000f);
                var direction = (Vector2)(Quaternion.Euler(0f, 0f, 41f) * Vector2.right);
                shapes.Clear();
                TelegraphIndicator.FootprintOf(attack, origin, direction, null, shapes, out var pattern, out var sweep);
                var whole = Track(TelegraphMarkerView.Create(null, "Whole"));
                whole.SetFootprint(shapes, origin, pattern, sweep, true, float.MaxValue);
                var sliced = Track(TelegraphMarkerView.Create(null, "Sliced"));
                var slices = 0;
                do
                {
                    sliced.SetFootprint(shapes, origin, pattern, sweep, true, 0.02f);
                    if (!sliced.IsBuildComplete)
                    {
                        sliced.Paint(new TelegraphLook { Colour = Color.red, Progress = 0.4f, Heavy = true });
                        Assert.IsFalse(sliced.IsVisible, attack.name + ": nothing half-built is shown");
                    }

                    slices++;
                }
                while (!sliced.IsBuildComplete && slices < 100000);

                Assert.IsTrue(sliced.IsBuildComplete);
                Assert.Greater(slices, 3, attack.name + ": the build really was sliced");
                Assert.AreEqual(whole.CoveredPixels, sliced.CoveredPixels, attack.name + ": same pixel count");
                var look = new TelegraphLook { Colour = Color.red, Progress = 0.55f, Heavy = true };
                whole.Paint(look);
                sliced.Paint(look);
                var a = whole.Renderer.sprite.texture.GetPixelData<Color32>(0);
                var b = sliced.Renderer.sprite.texture.GetPixelData<Color32>(0);
                Assert.AreEqual(a.Length, b.Length);
                for (var i = 0; i < a.Length; i++)
                    Assert.IsTrue(a[i].r == b[i].r && a[i].g == b[i].g && a[i].b == b[i].b && a[i].a == b[i].a, $"{attack.name}: pixel {i} differs after a sliced build");
            }
        }

        /// <summary>Edge positions: a dash or a shot that meets a wall stops there, and so does its lane — and the real motion still never leaves it.</summary>
        [UnityTest]
        public IEnumerator DashAndShotLanes_EndAtTheWallTheyReallyStopAt_AndStillContainTheRealMotion()
        {
            var attacks = ShippedAttacks();
            var dash = attacks.First(a => a.Attack.Motion == AttackMotion.Dash && a.Owner is EliteDefinition);
            var volley = attacks.First(a => a.Attack.Motion == AttackMotion.Projectile && a.Attack.ProjectileCount == 1 && a.Owner is EliteDefinition);
            var shapes = new List<AttackFootprint.Shape>();
            foreach (var (attack, owner) in new[] { dash, volley })
            {
                var origin = new Vector2(-4000f, 2000f);
                var direction = new Vector2(1f, 0.35f).normalized;
                var reach = attack.Motion == AttackMotion.Dash ? attack.DashDistance : attack.ProjectileRange;
                var wall = Track(new GameObject("Wall"));
                wall.transform.position = origin + direction * (reach * 0.55f + 1f);
                wall.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
                wall.AddComponent<BoxCollider2D>().size = new Vector2(1f, 8f);
                wall.AddComponent<EnvironmentObstacle>();
                var go = SpawnOwner(owner, origin);
                Physics2D.SyncTransforms();

                var view = Draw(attack, origin, direction, go.transform, shapes);
                var open = TelegraphIndicator.ShapeFor(attack, direction).size;
                Assert.Less(shapes[0].Size.x, open.x - 1f, $"{attack.name}: the lane stops at the wall ({shapes[0].Size.x:0.00} < {open.x:0.00})");

                var resolver = new AttackResolver(go.transform, go.GetComponent<Rigidbody2D>(), new FixedDamageRoller(), go.GetComponent<ProjectilePool>());
                resolver.Begin(attack, direction);
                var deadline = Time.time + 3f;
                var checkedPoints = 0;
                while (Time.time < deadline)
                {
                    if (resolver.IsRunning) resolver.Tick(Time.deltaTime);
                    if (attack.Motion == AttackMotion.Dash)
                        foreach (var p in Ring(go.transform.position, attack.HitRadius * 0.98f)) { checkedPoints++; Assert.IsTrue(Painted(view, p), $"{attack.name}: dash body outside the clipped lane at {(Vector2)go.transform.position - origin}"); }
                    else
                        foreach (var shot in resolver.SpawnedProjectiles.Where(s => s != null && s.gameObject.activeInHierarchy))
                        foreach (var p in Ring(shot.transform.position, AttackFootprint.EnemyProjectileRadius * 0.98f, 8)) { checkedPoints++; Assert.IsTrue(Painted(view, p), $"{attack.name}: shot outside the clipped lane at {(Vector2)shot.transform.position - origin}"); }
                    if (!resolver.IsRunning && (attack.Motion == AttackMotion.Dash || resolver.SpawnedProjectiles.All(s => s == null || !s.gameObject.activeInHierarchy))) break;
                    yield return new WaitForFixedUpdate();
                }

                Assert.Greater(checkedPoints, 0);
                Object.DestroyImmediate(wall);
                Object.DestroyImmediate(go.transform.root.gameObject);
            }
        }

        // ---- Timing ----

        private GameObject Dummy(Vector2 at)
        {
            var dummy = Track(new GameObject("PlayerDummy"));
            dummy.transform.position = at;
            dummy.AddComponent<CircleCollider2D>().radius = 0.4f;
            dummy.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            dummy.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            dummy.AddComponent<HealthComponent>().SetMaxHealth(1000000);
            return dummy;
        }

        private static T WithMoveset<T>(T definition, params EnemyAttackDefinition[] moves) where T : ScriptableObject
        {
            var copy = Object.Instantiate(definition);
            var so = new SerializedObject(copy);
            var list = so.FindProperty("_moveset");
            list.arraySize = moves.Length;
            for (var i = 0; i < moves.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = moves[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            return copy;
        }

        /// <summary>
        /// A phase-two boss telegraphs at 80 % of the authored time: the marker's fill and countdown follow the real
        /// remaining time (it used to fill over the authored time and be struck at 80 %), and the impact flash plays on the
        /// commit.
        /// </summary>
        [UnityTest]
        public IEnumerator PhaseTwoBoss_FillAndCountdown_FollowTheScaledTelegraph_AndTheImpactLandsOnTheCommit()
        {
            var definition = _catalog.Bosses.First(b => b.PhaseTwoTimingMultiplier < 0.99f);
            var dummy = Dummy(new Vector2(-1998f, -2000f));
            var encounter = new DefaultBossSpawner(_catalog.Bosses, _catalog.Stagger).Spawn(definition, new Vector2(-2000f, -2000f), null);
            Track(encounter.gameObject);
            var boss = encounter.Boss;
            var indicator = boss.gameObject.AddComponent<TelegraphIndicator>();
            indicator.Configure(_catalog.Feedback, _pool, null, boss);
            boss.Health.TryApplyDamage(new DamageRequest(Mathf.CeilToInt(boss.Health.MaxHealth * (1f - definition.PhaseTwoHealthFraction)) + 1));
            Assert.IsTrue(boss.IsPhaseTwo, "phase two");
            boss.SetTarget(dummy.transform);

            var checkedTelegraphs = 0;
            var deadline = Time.time + 20f;
            while (checkedTelegraphs < 3 && Time.time < deadline)
            {
                dummy.GetComponent<HealthComponent>().Heal(1000000);
                if (boss.State != MovesetActorState.Telegraph) { yield return null; continue; }
                var attack = boss.CurrentAttack;
                Assert.AreEqual(attack.TelegraphSeconds * definition.PhaseTwoTimingMultiplier, boss.TelegraphDuration, 0.0001f, attack.name + ": the committed telegraph is scaled");
                var impactsBefore = indicator.Impacts;
                var lastFill = 0f;
                while (boss.State == MovesetActorState.Telegraph)
                {
                    indicator.Tick(0f);
                    Assert.AreEqual(boss.TelegraphRemaining, indicator.SecondsToImpact, 0.0001f, attack.name + ": the countdown is the real one");
                    Assert.AreEqual(1f - boss.TelegraphRemaining / boss.TelegraphDuration, indicator.Fill01, 0.0001f, attack.name + ": the fill tracks the real telegraph");
                    lastFill = indicator.Fill01;
                    yield return null;
                }

                Assert.Greater(lastFill, 0.9f, attack.name + ": the fill was all but complete on the last telegraph frame");
                indicator.Tick(0f);
                Assert.AreEqual(impactsBefore + 1, indicator.Impacts, attack.name + ": the impact flash plays on the commit");
                checkedTelegraphs++;
            }

            Assert.AreEqual(3, checkedTelegraphs, "three phase-two telegraphs observed");
        }

        /// <summary>A normal moveset enemy (Brute) telegraphs each move for that move's own time, and the marker uses it.</summary>
        [UnityTest]
        public IEnumerator MovesetEnemy_MarkerUsesTheMovesOwnTelegraphTime()
        {
            var brute = _catalog.Enemies.First(e => e.AttackKind == EnemyAttackKind.Moveset && e.Moveset.Any(m => m != null && Mathf.Abs(m.TelegraphSeconds - e.AttackTelegraphSeconds) > 0.05f));
            var dummy = Dummy(new Vector2(-1001.2f, -1000f));
            var enemy = Track(new DefaultEnemySpawner(_catalog.Stagger).Spawn(brute, new Vector2(-1000f, -1000f), dummy.transform));
            var indicator = enemy.gameObject.AddComponent<TelegraphIndicator>();
            indicator.Configure(_catalog.Feedback, _pool, enemy, null);
            var deadline = Time.time + 8f;
            while (enemy.State != EnemyState.Telegraph && Time.time < deadline) yield return null;
            Assert.AreEqual(EnemyState.Telegraph, enemy.State);
            var move = ((EnemyMovesetAttack)enemy.Attack).PendingAttack;
            Assert.IsNotNull(move);
            Assert.AreEqual(move.TelegraphSeconds / (brute.AttackTelegraphSeconds / enemy.CurrentTelegraphSeconds), enemy.TelegraphDuration, 0.0001f, "the move's own telegraph, attack-speed scaled");
            indicator.Tick(0f);
            Assert.AreEqual(1f - enemy.TelegraphRemaining / enemy.TelegraphDuration, indicator.Fill01, 0.0001f, "the marker fills over the move's telegraph");
            Assert.AreEqual(enemy.TelegraphRemaining, indicator.SecondsToImpact, 0.0001f);
            Assert.AreEqual(TelegraphIndicator.KindOf(move.Motion), indicator.MarkerKind, "the move's own shape family");
        }

        /// <summary>
        /// A multi-hit move keeps its footprint drawn between windows (with the countdown to the next one), flashes once per
        /// window, and every window's damage lands inside it; the marker is gone once the last window has struck.
        /// </summary>
        [UnityTest]
        public IEnumerator MultiHitMove_StaysDrawnUntilItsLastWindow_FlashingOncePerWindow()
        {
            var (attack, owner) = ShippedAttacks().First(a => a.Attack.HitCount >= 3 && a.Attack.Motion == AttackMotion.Slam && a.Owner is not EnemyDefinition);
            var eliteSource = _catalog.Elites.First();
            var elite = WithMoveset(eliteSource, attack);
            Track(elite);
            var dummy = Dummy(new Vector2(500f + attack.HitRadius * 0.5f, 500f));
            var encounter = new DefaultEliteSpawner(_catalog.Stagger).Spawn(elite, new Vector2(500f, 500f), null, null);
            Track(encounter.gameObject);
            var actor = encounter.Elite;
            var indicator = actor.gameObject.AddComponent<TelegraphIndicator>();
            indicator.Configure(_catalog.Feedback, _pool, null, actor);
            var health = dummy.GetComponent<HealthComponent>();
            actor.SetTarget(dummy.transform);
            var deadline = Time.time + 8f;
            while (actor.State != MovesetActorState.Telegraph && Time.time < deadline) yield return null;
            Assert.AreEqual(MovesetActorState.Telegraph, actor.State, $"{attack.name} ({owner.name}) telegraphs");
            while (actor.State == MovesetActorState.Telegraph) yield return null;
            var impacts = indicator.Impacts;
            var hpBefore = health.CurrentHealth;
            var liveFrames = 0;
            while (actor.State == MovesetActorState.Attacking)
            {
                indicator.Tick(0f);
                if (actor.Resolver.WindowsFired >= 1 && actor.Resolver.WindowsRemaining > 0)
                {
                    liveFrames++;
                    Assert.IsTrue(indicator.IsShowing && indicator.IsLive, $"{attack.name}: drawn between windows ({actor.Resolver.WindowsFired}/{attack.HitCount} struck)");
                    Assert.AreEqual(actor.Resolver.SecondsToNextWindow, indicator.SecondsToImpact, 0.0001f, "counting down to the next window");
                }

                yield return null;
            }

            indicator.Tick(0f);
            Assert.Greater(liveFrames, 5, "the lingering danger was on screen");
            Assert.AreEqual(impacts + attack.HitCount - 1, indicator.Impacts, "one flash per later window (the first flashed on the commit)");
            Assert.IsFalse(indicator.IsShowing, "gone once the last window has struck");
            Assert.Less(health.CurrentHealth, hpBefore, "the windows hurt the target standing in the marker");
        }

        /// <summary>A lobbed bomb: the ring stays on the real landing point while it flies, counting down to the blast, and flashes when it lands.</summary>
        [UnityTest]
        public IEnumerator LobbedBomb_RingStaysOnTheRealLandingUntilTheBlast()
        {
            var bomber = _catalog.Enemies.First(e => e.AttackKind == EnemyAttackKind.Lob);
            var dummy = Dummy(new Vector2(-600f + Mathf.Min(4f, bomber.AttackRange - 0.5f), -600f));
            var enemy = Track(new DefaultEnemySpawner(_catalog.Stagger).Spawn(bomber, new Vector2(-600f, -600f), dummy.transform));
            var indicator = enemy.gameObject.AddComponent<TelegraphIndicator>();
            indicator.Configure(_catalog.Feedback, _pool, enemy, null);
            var lob = (EnemyLobAttack)enemy.Attack;
            var deadline = Time.time + 8f;
            while (enemy.State != EnemyState.Telegraph && Time.time < deadline) yield return null;
            yield return null;
            Assert.IsTrue(indicator.IsShowing, "the landing ring telegraphs");
            var planned = lob.PlannedLanding;
            while (enemy.State == EnemyState.Telegraph) yield return null;
            Assert.IsTrue(lob.IsBombInFlight, "the bomb is in the air");
            var bomb = lob.LastGrenade;
            Assert.AreEqual(planned, bomb.LandingPoint, "the telegraphed landing is where it comes down");
            var flightFrames = 0;
            var impacts = indicator.Impacts;
            while (lob.IsBombInFlight)
            {
                indicator.Tick(0f);
                flightFrames++;
                Assert.IsTrue(indicator.IsShowing && indicator.IsLive, "the ring stays while the bomb flies");
                Assert.AreEqual(bomb.SecondsToLanding, indicator.SecondsToImpact, 0.0001f, "counting down to the blast");
                foreach (var p in Ring(bomb.LandingPoint, bomber.BombRadiusTiles * 0.98f)) Assert.IsTrue(Painted(indicator.View, p), "the drawn ring holds the whole blast radius");
                yield return null;
            }

            indicator.Tick(0f);
            Assert.Greater(flightFrames, 2);
            Assert.AreEqual(impacts + 1, indicator.Impacts, "the blast flashes");
            Assert.IsFalse(indicator.IsShowing, "and the ring is gone with it");
        }

        /// <summary>
        /// Plain shooters, with the target strafing through the telegraph: whether the enemy tracks then locks (Sniper) or
        /// aims at the moment it fires (Shooter, Summoner), the real shot flies inside the lane drawn at that moment.
        /// </summary>
        [UnityTest]
        public IEnumerator NormalShooters_RealShotsFlyInsideTheLaneDrawnAtTheMomentOfFire_EvenAgainstAStrafingTarget()
        {
            var shooters = _catalog.Enemies.Where(e => e.AttackKind == EnemyAttackKind.Projectile).ToList();
            Assert.GreaterOrEqual(shooters.Count, 2);
            var index = 0;
            foreach (var definition in shooters)
            {
                var origin = new Vector2(-2500f, 1500f + index++ * 60f);
                var dummy = Dummy(origin + new Vector2(Mathf.Min(5f, definition.AttackRange - 1f), -1.5f));
                var enemy = Track(new DefaultEnemySpawner(_catalog.Stagger).Spawn(definition, origin, dummy.transform));
                if (enemy.GetComponent<ProjectilePool>() == null) enemy.gameObject.AddComponent<ProjectilePool>();
                var indicator = enemy.gameObject.AddComponent<TelegraphIndicator>();
                indicator.Configure(_catalog.Feedback, _pool, enemy, null);
                var shot = (EnemyProjectileAttack)enemy.Attack;
                var deadline = Time.time + 8f;
                while (enemy.State != EnemyState.Telegraph && Time.time < deadline) yield return null;
                Assert.AreEqual(EnemyState.Telegraph, enemy.State, definition.Id + " telegraphs");
                // Strafe across the lane for the whole telegraph.
                var start = dummy.transform.position;
                var since = 0f;
                while (enemy.State == EnemyState.Telegraph && Time.time < deadline)
                {
                    since += Time.deltaTime;
                    dummy.transform.position = start + new Vector3(0f, since * 3f, 0f);
                    yield return null;
                }

                deadline = Time.time + 3f;
                var samples = 0;
                while (Time.time < deadline && shot.SpawnedProjectiles.Count == 0) yield return null;
                Assert.Greater(shot.SpawnedProjectiles.Count, 0, definition.Id + " fired");
                var view = indicator.View;
                Assert.IsNotNull(view);
                while (Time.time < deadline && shot.SpawnedProjectiles.Any(p => p != null && p.gameObject.activeInHierarchy))
                {
                    foreach (var p in shot.SpawnedProjectiles.Where(p => p != null && p.gameObject.activeInHierarchy))
                    foreach (var point in Ring(p.transform.position, AttackFootprint.EnemyProjectileRadius * 0.98f, 8))
                    {
                        samples++;
                        Assert.IsTrue(Painted(view, point), $"{definition.Id}: the shot at {(Vector2)p.transform.position - origin} left the lane drawn when it fired");
                    }

                    yield return new WaitForFixedUpdate();
                }

                Assert.Greater(samples, 0, definition.Id + ": shot path sampled");
                Object.DestroyImmediate(enemy.gameObject);
            }
        }

        // ---- Co-op ----

        /// <summary>
        /// Normal enemies on a co-op client: fed only the host's snapshots, the client draws the same shape in the same
        /// place and direction (the locked charge lane and aim, the moveset enemy's current move, the bomb's real landing)
        /// and runs the same telegraph length.
        /// </summary>
        [UnityTest]
        public IEnumerator CoopClient_NormalEnemyMarkers_MatchTheHostShapesAndTiming([Values(EnemyAttackKind.Charge, EnemyAttackKind.Projectile, EnemyAttackKind.Lob, EnemyAttackKind.Moveset)] EnemyAttackKind kind)
        {
            var definition = _catalog.Enemies.First(e => e.AttackKind == kind);
            var origin = new Vector2(800f, -800f);
            var dummy = Dummy(origin + new Vector2(2.6f, 1.3f));
            var enemy = Track(new DefaultEnemySpawner(_catalog.Stagger).Spawn(definition, origin, dummy.transform));
            var host = enemy.gameObject.AddComponent<TelegraphIndicator>();
            host.Configure(_catalog.Feedback, _pool, enemy, null);
            var clientGo = Track(new GameObject("ClientReplica"));
            var replica = clientGo.AddComponent<EnemyReplica>();
            replica.Initialize(new EnemySpawnRecord { NetId = 7, Position = origin });
            replica.ConfigureCombatPresence(CoopActorKind.Normal, -1, definition, null);
            var client = clientGo.AddComponent<TelegraphIndicator>();
            client.ConfigureReplica(_catalog.Feedback, null, replica);
            uint version = 0;
            var compared = 0;
            var deadline = Time.time + 10f;
            while (compared < 10 && Time.time < deadline)
            {
                dummy.GetComponent<HealthComponent>().Heal(1000000);
                Assert.IsTrue(replica.Apply(AuthoritativeEnemySpawner.Capture(7, enemy, ++version, Time.timeAsDouble)));
                clientGo.transform.position = enemy.transform.position;
                host.Tick(0f);
                client.Tick(0f);
                if (enemy.State == EnemyState.Telegraph)
                {
                    var hostShape = TelegraphIndicator.ShapeOf(enemy, enemy.transform.position);
                    var clientShape = TelegraphIndicator.ShapeOf(replica, clientGo.transform.position);
                    Assert.AreEqual(hostShape.Kind, clientShape.Kind, $"{definition.Id}: shape family");
                    Assert.Less(Vector2.Distance(hostShape.Centre, clientShape.Centre), 0.01f, $"{definition.Id}: same place");
                    Assert.Less(Vector2.Distance(hostShape.Size, clientShape.Size), 0.01f, $"{definition.Id}: same footprint");
                    if (hostShape.Kind != TelegraphIndicator.KindSlam && hostShape.Kind != TelegraphIndicator.KindStationary)
                        Assert.AreEqual(hostShape.AngleDegrees, clientShape.AngleDegrees, 0.5f, $"{definition.Id}: same direction");
                    Assert.AreEqual(enemy.TelegraphDuration, replica.TelegraphSeconds, 0.0001f, $"{definition.Id}: the client runs the host's telegraph length");
                    Assert.IsTrue(client.IsShowing, $"{definition.Id}: the client shows the marker");
                    Assert.AreEqual(host.MarkerKind, client.MarkerKind);
                    compared++;
                }

                yield return null;
            }

            Assert.GreaterOrEqual(compared, 3, $"{definition.Id}: telegraph frames compared");
        }
    }
}

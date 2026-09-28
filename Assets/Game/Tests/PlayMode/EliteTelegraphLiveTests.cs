using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Expedition;
using RuinRail.Networking;
using RuinRail.Presentation.Vfx;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Elite ground telegraphs in the running game (boot → Shelter → generated dungeon): the Elite a real Elite room
    /// spawns carries the same ground danger marker as a boss, and every shipped Elite — spawned into that live arena
    /// through the run's own presentation seam — shows, for each attack it telegraphs, the attack's shape (kind, size,
    /// projectile lanes) in the Elite colour for exactly the attack's telegraph time, gone the moment it resolves.
    /// Captures: TestResults/RegressionProof/elite_telegraph_*.png.
    /// </summary>
    public sealed class EliteTelegraphLiveTests
    {
        private const string Folder = "TestResults/RegressionProof";
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ruinrail_elite_telegraph_" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(_saveDir);
            System.IO.Directory.CreateDirectory(Folder);
            RuinRail.Core.Input.GameplayInputGate.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            foreach (var scene in Object.FindObjectsByType<ExpeditionScene>(FindObjectsSortMode.None)) Object.DestroyImmediate(scene.gameObject);
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == null || root.name.IndexOf("tests runner", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (root.GetComponents<Component>().Any(c => c != null && (c.GetType().Namespace ?? string.Empty).StartsWith("UnityEngine.TestTools"))) continue;
                Object.DestroyImmediate(root);
            }

            Time.timeScale = 1f;
            RuinRail.Networking.NetworkPlayerObject.VisualComposer = null;
            try { System.IO.Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 30f;
            while (_app.ComposedScene != scene)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed in time");
                yield return null;
            }
        }

        private static IEnumerator Teleport(ExpeditionScene run, Vector2 position)
        {
            var body = run.Rig.Player.GetComponent<Rigidbody2D>();
            run.Rig.Player.transform.position = position;
            body.position = position;
            body.linearVelocity = Vector2.zero;
            for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            yield return null;
        }

        [UnityTest]
        public IEnumerator LiveRun_EveryShippedElite_TelegraphsItsRealAttackShapeOnTheGround_ForExactlyTheTelegraphTime()
        {
            var content = GameContentCatalog.Load();
            var pools = BiomeRoomPools.Build(content.Rooms);
            var seed = 0;
            for (var s = 1; s < 300 && seed == 0; s++)
            {
                var generation = DungeonGenerationPipeline.Generate(new DungeonGraphGenerator(DungeonGraphRules.CreateDefault()), pools.PoolFor(BiomeSelector.SelectFirst(s)), s, 1);
                if (generation.Success && generation.Graph.Nodes.Any(n => n.IsElite)) seed = s;
            }

            Assert.Greater(seed, 0, "a seed whose first depth has an Elite room");
            _app = GameApp.Ensure(content, _saveDir);
            _app.SetRunSeedOverride(seed);
            UnityEngine.SceneManagement.SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Telegraph Reader");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(RuinRail.UI.Base.BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 12; i++) yield return null;

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var player = run.Rig.Player;
            var health = player.GetComponent<HealthComponent>();
            var eliteColour = content.Feedback.EliteBossTelegraphColor;
            var eliteNode = run.Generation.Graph.Nodes.First(n => n.IsElite).Id;
            var eliteRoom = run.Rooms[eliteNode];
            var engagement = eliteRoom.Engagement as EliteEngagement;
            Assert.IsNotNull(engagement, "the live run composed the Elite encounter");
            var centre = EncounterRewardPlacement.WorldCenter(eliteRoom.Root).Value;
            var report = new List<string>();

            void LookAt(Vector2 a, Vector2 b)
            {
                var cam = run.Camera.Camera.transform;
                var mid = (a + b) * 0.5f;
                cam.position = new Vector3(mid.x, mid.y, cam.position.z);
            }

            // One Elite: observe its telegraphs, alternating the player near / far so its range bands pick different moves.
            IEnumerator Observe(EliteController elite, string label, int wantDistinct, float seconds)
            {
                var indicator = elite.GetComponent<TelegraphIndicator>();
                Assert.IsNotNull(indicator, label + ": the run composed the ground telegraph marker on the Elite");
                var seen = new HashSet<EnemyAttackDefinition>();
                // A co-op client's view of this same Elite: fed only the host's own replication snapshot.
                var clientGo = new GameObject("ClientReplica_" + label);
                var replica = clientGo.AddComponent<EnemyReplica>();
                replica.Initialize(new EnemySpawnRecord { NetId = 1, Position = elite.transform.position, IsBossOrElite = true });
                replica.ConfigureCombatPresence(CoopActorKind.Elite, -1, null, elite.Definition.Moveset);
                var clientMarker = clientGo.AddComponent<TelegraphIndicator>();
                clientMarker.ConfigureReplica(content.Feedback, null, replica);
                uint version = 0;
                var deadline = Time.time + seconds;
                var near = true;
                while (Time.time < deadline && seen.Count < wantDistinct && elite != null && elite.Health.IsAlive)
                {
                    health.Heal(100000);
                    if (elite.State != MovesetActorState.Telegraph) { yield return null; continue; }

                    var attack = elite.CurrentAttack;
                    var started = Time.time;
                    yield return null;
                    if (elite.State != MovesetActorState.Telegraph) continue;
                    Assert.IsTrue(indicator.IsShowing, $"{label} {attack.name}: a ground marker while it telegraphs");
                    Assert.AreEqual(eliteColour, indicator.MarkerColor, $"{label}: the Elite / Boss telegraph colour");
                    var lanes = new List<TelegraphIndicator.DangerShape>();
                    TelegraphIndicator.LanesFor(attack, elite.transform.position, elite.LockedDirection, lanes);
                    var expected = lanes.Count > 0 ? lanes[0] : TelegraphIndicator.ShapeOf(elite);
                    Assert.AreEqual(TelegraphIndicator.KindOf(attack.Motion), indicator.MarkerKind, $"{label} {attack.name}: the art of its motion");
                    Assert.AreEqual(expected.Size, indicator.MarkerScale, $"{label} {attack.name}: the attack's own footprint");
                    Assert.AreEqual(Mathf.Max(1, lanes.Count), indicator.MarkerCount, $"{label} {attack.name}: one marker per projectile lane");

                    // Co-op: the client draws the same danger shape from the host's snapshot of this authoritative attack.
                    Assert.IsTrue(replica.Apply(AuthoritativeEnemySpawner.Capture(1, elite, elite.Definition.Moveset, ++version, Time.timeAsDouble)));
                    clientGo.transform.position = elite.transform.position;
                    clientMarker.Tick(Time.deltaTime);
                    Assert.AreSame(attack, replica.CurrentAttack, $"{label}: the client knows which attack is coming");
                    Assert.AreEqual(indicator.MarkerKind, clientMarker.MarkerKind, $"{label} {attack.name}: client marker art");
                    Assert.AreEqual(indicator.MarkerScale, clientMarker.MarkerScale, $"{label} {attack.name}: client marker footprint");
                    Assert.AreEqual(eliteColour, clientMarker.MarkerColor, $"{label}: client Elite colour");
                    var hostShape = TelegraphIndicator.ShapeOf(elite);
                    var clientShape = TelegraphIndicator.ShapeOf(replica, elite.transform.position);
                    Assert.Less(Vector2.Distance(hostShape.Centre, clientShape.Centre), 0.01f, $"{label} {attack.name}: client marker in the same place");
                    Assert.AreEqual(hostShape.AngleDegrees, clientShape.AngleDegrees, 0.01f, $"{label} {attack.name}: client marker in the same direction");

                    var first = seen.Add(attack);
                    var captured = false;
                    while (elite != null && elite.State == MovesetActorState.Telegraph)
                    {
                        health.Heal(100000);
                        if (first && !captured && Time.time - started >= attack.TelegraphSeconds * 0.5f)
                        {
                            LookAt(elite.transform.position, player.transform.position);
                            LiveDungeonCapture.Capture(Folder, $"elite_telegraph_{label}_{attack.name}", run.Camera.Camera, run.Camera.Config.PixelsPerUnit, includeUi: true);
                            captured = true;
                        }

                        yield return null;
                    }

                    var lasted = Time.time - started;
                    var authored = attack.TelegraphSeconds * elite.TimingMultiplier;
                    Assert.AreEqual(authored, lasted, 0.1f, $"{label} {attack.name}: the marker lives for the real telegraph ({authored:0.00}s)");
                    Assert.IsFalse(indicator.IsShowing, $"{label} {attack.name}: gone the moment the attack resolves");
                    if (first) report.Add($"{label}: {attack.name} {attack.Motion} kind {indicator.MarkerKind} size {expected.Size} lanes {Mathf.Max(1, lanes.Count)} telegraph {lasted:0.00}s (authored {authored:0.00}s)");

                    // Next move from the other range band.
                    near = !near;
                    var toward = ((Vector2)player.transform.position - (Vector2)elite.transform.position).normalized;
                    if (toward.sqrMagnitude < 0.01f) toward = Vector2.left;
                    yield return Teleport(run, (Vector2)elite.transform.position + toward * (near ? 1.6f : 5.5f));
                }

                Object.DestroyImmediate(clientGo);
                Assert.GreaterOrEqual(seen.Count, wantDistinct, $"{label}: telegraphed {seen.Count} distinct attacks ({string.Join(", ", seen.Select(a => a.name))}); state {elite?.State}, target {(elite != null && elite.Target != null ? elite.Target.name : "none")}, distance {(elite != null ? Vector2.Distance(elite.transform.position, player.transform.position) : -1f):0.0}, alive {elite != null && elite.Health.IsAlive}, room {eliteRoom.Lifecycle}");
            }

            // The real Elite room: entering spawns its Elite through the engagement, and the run composes the marker.
            yield return Teleport(run, centre);
            Assert.IsNotNull(engagement.Encounter?.Elite, "the Elite is in the room");
            // Stand within reach of it (the spawn can sit behind cover from the centre), on the room-centre side.
            var spawned = (Vector2)engagement.Encounter.Elite.transform.position;
            yield return Teleport(run, spawned + (centre - spawned).normalized * 3f);
            yield return Observe(engagement.Encounter.Elite, "room_" + engagement.Definition.Id, 1, 12f);
            engagement.Encounter.Elite.Health.TryApplyDamage(new DamageRequest(100000000));
            for (var i = 0; i < 30; i++) yield return null;

            // Every shipped Elite, spawned into the live arena through the run's own presentation seam (the one the
            // engagement's Spawned event calls).
            Assert.AreEqual(6, content.Elites.Count, "the six shipped Elites");
            foreach (var definition in content.Elites)
            {
                yield return Teleport(run, centre);
                var encounter = new DefaultEliteSpawner(content.Stagger).Spawn(definition, centre + Vector2.right * 3f, eliteRoom.Root.transform, player.transform);
                run.BindActorPresentation(encounter.Elite, definition.Id, isElite: true);
                yield return Observe(encounter.Elite, definition.Id, Mathf.Min(2, definition.Moveset.Count), 25f);
                Object.DestroyImmediate(encounter.gameObject);
                for (var i = 0; i < 3; i++) yield return null;
            }

            Debug.Log("[PROOF] elite telegraphs (seed " + seed + ")\n" + string.Join("\n", report));
        }
    }
}

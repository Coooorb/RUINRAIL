using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Networking;
using RuinRail.Presentation.Animation;
using RuinRail.Presentation.Vfx;
using RuinRail.UI.Base;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Every shipped boss, in its real boss room on a live expedition (pinned seeds): the room entry, then a fight
    /// against the real player (healed each frame, moved around the arena so every range band comes up). For each
    /// authored attack it records the telegraph (marker shown, its shape and lead time before the damaging phase), the
    /// active phase, and the boss's animation state while it moves. Captures go to <c>TestResults/BossPresentationProof</c>.
    /// </summary>
    public sealed class BossPresentationProofTests
    {
        private const string Folder = "TestResults/BossPresentationProof";
        private readonly StringBuilder _evidence = new();
        private GameApp _app;
        private string _saveDir;

        /// <summary>Run seeds whose depth-1 boss room holds each shipped boss.</summary>
        public static readonly (string Boss, int Seed)[] Bosses =
        {
            ("boss_tunnel_maw", 2), ("boss_the_conductor", 14),
            ("boss_scrap_king", 13), ("boss_the_foundry_titan", 7),
            ("boss_subject_omega", 3), ("boss_aegis_core", 1)
        };

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            Directory.CreateDirectory(Folder);
            foreach (var stale in Directory.GetFiles(Folder)) File.Delete(stale);
        }

        [TearDown]
        public void TearDown()
        {
            File.AppendAllText(Path.Combine(Folder, "boss_evidence.txt"), _evidence.ToString());
            _evidence.Clear();
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            foreach (var scene in Object.FindObjectsByType<ExpeditionScene>(FindObjectsSortMode.None)) Object.DestroyImmediate(scene.gameObject);
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == null || root.name.IndexOf("tests runner", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (root.GetComponents<Component>().Any(c => c != null && (c.GetType().Namespace ?? string.Empty).StartsWith("UnityEngine.TestTools"))) continue;
                Object.DestroyImmediate(root);
            }

            Time.timeScale = 1f;
            NetworkPlayerObject.VisualComposer = null;
            RoomDoorLock.SkinResolver = null;
            try { Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private void Note(string line)
        {
            _evidence.AppendLine(line);
            Debug.Log("[BOSS-PROOF] " + line);
        }

        private IEnumerator EnterRun(int seed)
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_bossproof_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(seed);
            SceneManager.LoadScene(SceneNames.MainMenu);
            var deadline = Time.realtimeSinceStartup + 90f;
            while (_app.ComposedScene != SceneNames.MainMenu) { Assert.Less(Time.realtimeSinceStartup, deadline); yield return null; }
            _app.Menu.Play();
            while (_app.ComposedScene != SceneNames.Base) { Assert.Less(Time.realtimeSinceStartup, deadline); yield return null; }
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Boss Proof");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            while (_app.ComposedScene != SceneNames.Dungeon) { Assert.Less(Time.realtimeSinceStartup, deadline); yield return null; }
            for (var i = 0; i < 12; i++) yield return null;
        }

        private static IEnumerator Teleport(ExpeditionScene run, Vector2 position)
        {
            var body = run.Rig.Player.GetComponent<Rigidbody2D>();
            run.Rig.Player.transform.position = position;
            body.position = position;
            body.linearVelocity = Vector2.zero;
            yield return new WaitForFixedUpdate();
            yield return null;
        }

        private static void KeepAlive(GameObject player)
        {
            var health = player.GetComponent<HealthComponent>();
            if (health != null && health.IsAlive && health.CurrentHealth < health.MaxHealth) health.Heal(health.MaxHealth);
        }

        [UnityTest]
        public IEnumerator EveryBoss_EntersWithAnIntro_TelegraphsEveryAttack_AndAnimatesWhileMoving([ValueSource(nameof(BossIds))] string bossId)
        {
            var seed = Bosses.First(b => b.Boss == bossId).Seed;
            yield return EnterRun(seed);
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var boss = Object.FindFirstObjectByType<BossController>();
            Assert.IsNotNull(boss, "no boss composed");
            Assert.AreEqual(bossId, boss.Definition.Id, $"seed {seed} no longer composes {bossId}");
            var room = run.Rooms.Values.First(r => r.State.RoomType == RoomType.Boss);
            var interior = room.InteriorWorldBounds;
            var camera = run.Camera.Camera;
            var ppu = run.Camera.Config.PixelsPerUnit;
            var player = run.Rig.Player;
            var playerHealth = player.GetComponent<HealthComponent>();
            var telegraph = boss.GetComponent<TelegraphIndicator>();
            var animation = boss.GetComponent<EnemyAnimationDriver>();
            Assert.IsNotNull(telegraph, "boss has no telegraph indicator");
            Assert.IsNotNull(animation, "boss has no animation driver");

            // ---- entry ----
            var entry = new Vector2(interior.center.x, interior.yMin + 1.2f);
            yield return Teleport(run, entry);
            var enteredAt = Time.time;
            var hpAtEntry = playerHealth.CurrentHealth;
            var firstTelegraphAt = -1f;
            boss.AttackTelegraphStarted += (_, _) => { if (firstTelegraphAt < 0f) firstTelegraphAt = Time.time; };
            foreach (var at in new[] { 0.15f, 0.8f, 1.6f })
            {
                while (Time.time < enteredAt + at) yield return null;
                LiveDungeonCapture.Capture(Folder, $"{bossId}_entry_{at:0.00}s", camera, ppu, includeUi: true);
            }

            // The introduction: plays on entry, holds input and the boss, hands control back, and nothing hits before.
            var intro = BossIntroSequence.Current;
            Assert.IsNotNull(intro, "no boss introduction started on entry");
            StringAssert.AreEqualIgnoringCase(boss.Definition.DisplayName, intro.Title);
            var introDeadline = Time.time + 4f;
            while (intro != null && intro.IsPlaying && Time.time < introDeadline)
            {
                Assert.IsTrue(RuinRail.Core.Input.GameplayInputGate.IsHeld, "gameplay input is held during the introduction");
                Assert.IsNull(boss.Target, "the boss has no target during the introduction");
                yield return null;
            }

            var introSeconds = Time.time - enteredAt;
            yield return null;
            Assert.IsFalse(RuinRail.Core.Input.GameplayInputGate.IsHeld, "control is handed back after the introduction");
            Assert.IsNotNull(boss.Target, "the boss engages once the introduction ends");
            Assert.IsNull(run.Camera.FocusOverride, "the camera follows the survivor again");
            Assert.IsTrue(run.HudView == null || run.HudView.GetComponent<Canvas>().enabled, "the run HUD is back after the introduction");
            Assert.AreEqual(hpAtEntry, playerHealth.CurrentHealth, "the boss dealt no damage before control returned");
            Assert.IsTrue(firstTelegraphAt < 0f || firstTelegraphAt - enteredAt >= introSeconds - 0.05f, "no attack was telegraphed during the introduction");
            Assert.LessOrEqual(introSeconds, 2.6f, "the introduction stays short");
            Note($"{bossId} (seed {seed}, {boss.Definition.Biome}): intro '{intro?.Title}' / '{intro?.Subtitle}' {introSeconds:0.00}s, hp lost 0, first telegraph {(firstTelegraphAt < 0 ? "after intro" : $"+{firstTelegraphAt - enteredAt:0.00}s")}");

            // ---- the fight ----
            var seen = new Dictionary<string, (float TelegraphAt, bool MarkerShown, string Kind, Vector2 Size, int Markers)>();
            var pendingTelegraphShot = (Attack: (EnemyAttackDefinition)null, At: 0f);
            var pendingActiveShot = (Attack: (EnemyAttackDefinition)null, At: 0f);
            var leads = new List<string>();
            boss.AttackTelegraphStarted += (_, attack) =>
            {
                if (attack == null || seen.ContainsKey(attack.Id)) return;
                seen[attack.Id] = (Time.time, false, string.Empty, Vector2.zero, 0);
                pendingTelegraphShot = (attack, Time.time + attack.TelegraphSeconds * 0.6f);
            };
            boss.AttackResolved += (_, attack) =>
            {
                if (attack == null || !seen.TryGetValue(attack.Id, out var s)) return;
                leads.Add($"{attack.Id} lead {Time.time - s.TelegraphAt:0.00}s (authored {attack.TelegraphSeconds:0.00}) marker={s.MarkerShown} kind={s.Kind} size={s.Size} markers={s.Markers}");
                if (pendingActiveShot.Attack == null) pendingActiveShot = (attack, Time.time + 0.12f);
            };

            var moveFrames = 0;
            var movingFrames = 0;
            var slidingIdle = 0;
            var distances = new[] { 2f, 5f, 8f, 3.5f, 11f, 6.5f };
            var step = 0;
            var until = Time.time + 26f;
            var reposition = 0f;
            while (Time.time < until && boss.IsAlive)
            {
                KeepAlive(player);
                if (Time.time >= reposition)
                {
                    var angle = step * 137f * Mathf.Deg2Rad;
                    var want = (Vector2)boss.transform.position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distances[step % distances.Length];
                    want.x = Mathf.Clamp(want.x, interior.xMin + 1f, interior.xMax - 1f);
                    want.y = Mathf.Clamp(want.y, interior.yMin + 1f, interior.yMax - 1f);
                    yield return Teleport(run, want);
                    step++;
                    reposition = Time.time + 2.2f;
                }

                var current = boss.CurrentAttack;
                if (boss.State == MovesetActorState.Telegraph && current != null && seen.TryGetValue(current.Id, out var record) && !record.MarkerShown && telegraph.IsShowing)
                    seen[current.Id] = (record.TelegraphAt, true, telegraph.MarkerKind, telegraph.MarkerScale, telegraph.MarkerCount);

                var speed = boss.GetComponent<Rigidbody2D>().linearVelocity.magnitude;
                if (speed > 0.3f && (boss.State == MovesetActorState.Chase || boss.State == MovesetActorState.Idle))
                {
                    movingFrames++;
                    if (animation.State == EnemyAnimState.Move) moveFrames++;
                    else slidingIdle++;
                }

                if (pendingTelegraphShot.Attack != null && Time.time >= pendingTelegraphShot.At)
                {
                    var mid = ((Vector2)boss.transform.position + (Vector2)player.transform.position) * 0.5f;
                    LiveDungeonCapture.Capture(Folder, $"{bossId}_{pendingTelegraphShot.Attack.Id}_telegraph", camera, mid, LiveDungeonCapture.Height / (2f * ppu), ppu, includeUi: false);
                    pendingTelegraphShot = (null, 0f);
                }

                if (pendingActiveShot.Attack != null && Time.time >= pendingActiveShot.At)
                {
                    var mid = ((Vector2)boss.transform.position + (Vector2)player.transform.position) * 0.5f;
                    LiveDungeonCapture.Capture(Folder, $"{bossId}_{pendingActiveShot.Attack.Id}_active", camera, mid, LiveDungeonCapture.Height / (2f * ppu), ppu, includeUi: false);
                    pendingActiveShot = (null, 0f);
                }

                yield return null;
            }

            foreach (var line in leads.Distinct()) Note($"  {line}");
            Note($"  moving frames {movingFrames}: Move clip {moveFrames}, other clip while moving {slidingIdle}");
            Note($"  attacks seen {seen.Count}/{boss.Definition.Moveset.Count}: {string.Join(", ", seen.Keys)}");

            Assert.GreaterOrEqual(seen.Count, 2, $"{bossId}: fewer than two attacks came up in the fight window");
            foreach (var kv in seen)
                Assert.IsTrue(kv.Value.MarkerShown, $"{bossId}: {kv.Key} had no visible telegraph marker");
        }

        [UnityTest]
        public IEnumerator BossIntro_CanBeSkipped_AndHandsControlBackAtOnce()
        {
            yield return EnterRun(2);
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var boss = Object.FindFirstObjectByType<BossController>();
            var room = run.Rooms.Values.First(r => r.State.RoomType == RoomType.Boss);
            yield return Teleport(run, new Vector2(room.InteriorWorldBounds.center.x, room.InteriorWorldBounds.yMin + 1.2f));
            var intro = BossIntroSequence.Current;
            Assert.IsNotNull(intro);
            var until = Time.time + 0.4f;
            while (Time.time < until) yield return null;
            intro.Finish(); // the skip path (Confirm) ends the intro through the same call
            yield return null;
            Assert.IsFalse(RuinRail.Core.Input.GameplayInputGate.IsHeld, "skip returns control at once");
            Assert.IsNotNull(boss.Target, "skip starts the fight at once");
            Assert.IsNull(run.Camera.FocusOverride);
        }

        public static IEnumerable<string> BossIds() => Bosses.Select(b => b.Boss);
    }
}

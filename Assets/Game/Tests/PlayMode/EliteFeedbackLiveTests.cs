using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Networking;
using RuinRail.Presentation.Vfx;
using RuinRail.UI.Base;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Every shipped Elite in a live run, spawned by the real spawner into an active combat room the player stands in and
    /// bound by the shipping presentation seam: each hit shows one damage number with the applied amount at the Elite,
    /// heals too, and the top-screen bar (91) shows the Elite variant with its name and exact HP, then hides on death; two
    /// live Elites hand the bar over cleanly. Captures: TestResults/EliteFeedback.
    /// </summary>
    public sealed class EliteFeedbackLiveTests
    {
        private const string Folder = "TestResults/EliteFeedback";
        private GameApp _app;
        private string _saveDir;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_elitefb_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            Directory.CreateDirectory(Folder);
        }

        [TearDown]
        public void TearDown()
        {
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

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 90f;
            while (_app.ComposedScene != scene) { Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed"); yield return null; }
        }

        private static IEnumerator Frames(int n) { for (var i = 0; i < n; i++) yield return null; }

        private static Vector2 InteriorCentre(ExpeditionScene run, int nodeId)
        {
            var root = run.Rooms[nodeId].Root;
            return root.transform.TransformPoint(RoomEntryTrigger.InteriorVolume(root.Size).center);
        }

        private static void Crop(LiveDungeonCapture.Result shot, RectInt rect, string name)
        {
            const int zoom = 3;
            var texture = new Texture2D(rect.width * zoom, rect.height * zoom, TextureFormat.RGBA32, false);
            var pixels = new Color32[texture.width * texture.height];
            for (var y = 0; y < texture.height; y++)
            for (var x = 0; x < texture.width; x++)
                pixels[y * texture.width + x] = shot.At(rect.x + x / zoom, rect.y + y / zoom);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        [UnityTest]
        public IEnumerator EveryElite_ShowsDamageNumbers_AndTheEliteTopBar_InALiveCombatRoom()
        {
            RuinRail.Core.Input.GameplayInputGate.Reset();
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(11);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Elite Feedback");
            hub.Onboarding.AcknowledgeStarterKit();
            _app.Settings.Current.Tutorial.ShowPrompts = false;
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            yield return Frames(12);

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var content = _app.Content;
            var player = run.Rig.Player;
            var playerHealth = player.GetComponent<HealthComponent>();
            var numbers = Object.FindObjectsByType<DamageNumberPool>(FindObjectsSortMode.None).Single();
            var hud = run.Hud;
            var cam = run.Camera.Camera;
            var ppu = run.Camera.Config.PixelsPerUnit;

            // An active combat room with the player inside; its own encounter frozen so the room stays active and quiet.
            var combatNode = run.Generation.Graph.Nodes.First(n => n.Type == RoomType.Combat && !n.IsElite);
            var room = run.Rooms[combatNode.Id];
            var centre = InteriorCentre(run, combatNode.Id);
            var body = player.GetComponent<Rigidbody2D>();
            player.transform.position = centre;
            body.position = centre;
            for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            yield return Frames(20);
            Assert.AreEqual(RoomLifecycleState.Active, room.Lifecycle);
            Assert.AreSame(room, run.CurrentRoom);
            foreach (var e in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
            {
                e.enabled = false;
                var rb = e.GetComponent<Rigidbody2D>();
                if (rb != null) { rb.linearVelocity = Vector2.zero; rb.bodyType = RigidbodyType2D.Kinematic; }
            }

            Assert.AreEqual(8, content.Elites.Count(e => e != null), "eight shipped Elites");
            EliteController Spawn(EliteDefinition definition, Vector2 at)
            {
                var encounter = new DefaultEliteSpawner(content.Stagger).Spawn(definition, at, room.transform, player.transform);
                var elite = encounter.Elite;
                run.BindActorPresentation(elite, definition.Id, isElite: true);
                elite.enabled = false; // frozen: a still subject for the capture
                var rb = elite.GetComponent<Rigidbody2D>();
                if (rb != null) { rb.linearVelocity = Vector2.zero; rb.bodyType = RigidbodyType2D.Kinematic; }
                return elite;
            }

            foreach (var definition in content.Elites.Where(e => e != null))
            {
                playerHealth.Heal(playerHealth.MaxHealth);
                var elite = Spawn(definition, centre + new Vector2(2.5f, 0.5f));
                yield return Frames(2);
                Assert.IsNull(elite.GetComponent<WorldHealthBar>(), $"{definition.Id}: one HP read — the top-screen bar, no world bar");
                Assert.IsTrue(hud.Snapshot.BossVisible && hud.Snapshot.BossIsElite, $"{definition.Id}: the Elite top bar is up");
                Assert.AreEqual(definition.DisplayName, hud.Snapshot.BossName);
                Assert.AreEqual(elite.Health.MaxHealth, hud.Snapshot.BossHp);
                Assert.IsTrue(run.HudView.BossBar.IsEliteStyle, "the Elite variant");
                StringAssert.StartsWith("ELITE " + definition.DisplayName, run.HudView.BossNameText);

                // A real hit: one number, the applied amount, at the Elite; the bar reads the exact HP.
                var shownBefore = numbers.Shown;
                var hpBefore = elite.Health.CurrentHealth;
                Assert.IsTrue(elite.Health.TryApplyDamage(new DamageRequest(Mathf.Max(1, elite.Health.MaxHealth * 3 / 10))));
                var applied = hpBefore - elite.Health.CurrentHealth;
                Assert.AreEqual(shownBefore + 1, numbers.Shown, $"{definition.Id}: exactly one damage number per hit");
                var number = numbers.LiveNumbers.Last();
                Assert.AreEqual(applied, number.Value, "the applied damage");
                Assert.AreEqual(DamageNumberKind.Dealt, number.Kind);
                Assert.Less(Vector2.Distance(number.transform.position, elite.transform.position), 1.6f, "at the Elite");
                yield return Frames(2);
                Assert.AreEqual(elite.Health.CurrentHealth, hud.Snapshot.BossHp);
                Assert.AreEqual(elite.Health.CurrentHealth / (float)elite.Health.MaxHealth, run.HudView.BossFill, 0.0001f);
                var shot = LiveDungeonCapture.Capture(Folder, $"elite_{definition.Id}", cam, ppu, includeUi: true);
                Crop(shot, new RectInt(260, 310, 250, 46), $"elite_{definition.Id}_bar");

                // Healing reads too.
                Assert.IsTrue(elite.Health.Heal(10));
                Assert.AreEqual(shownBefore + 2, numbers.Shown);
                Assert.AreEqual(DamageNumberKind.Heal, numbers.LiveNumbers.Last().Kind);
                yield return Frames(2);
                Assert.AreEqual(elite.Health.CurrentHealth, hud.Snapshot.BossHp);

                // Death: the bar goes; nothing stale stays up.
                Assert.IsTrue(elite.Health.TryApplyDamage(new DamageRequest(elite.Health.CurrentHealth + 1000)));
                yield return Frames(3);
                Assert.IsFalse(hud.Snapshot.BossVisible, $"{definition.Id}: the bar hides on death");
                if (elite != null) Object.Destroy(elite.gameObject);
                yield return Frames(3);
            }

            // Two Elites alive: the bar shows one, and hands over to the other when it dies (no overlap, no stale values).
            var defs = content.Elites.Where(e => e != null).Take(2).ToArray();
            var first = Spawn(defs[0], centre + new Vector2(2.5f, 1f));
            var second = Spawn(defs[1], centre + new Vector2(-2.5f, 1f));
            yield return Frames(2);
            Assert.AreEqual(defs[0].DisplayName, hud.Snapshot.BossName);
            Assert.IsTrue(first.Health.TryApplyDamage(new DamageRequest(first.Health.CurrentHealth + 1000)));
            yield return Frames(3);
            Assert.IsTrue(hud.Snapshot.BossVisible && hud.Snapshot.BossIsElite);
            Assert.AreEqual(defs[1].DisplayName, hud.Snapshot.BossName, "the bar moves to the Elite still standing");
            Assert.AreEqual(second.Health.CurrentHealth, hud.Snapshot.BossHp);
            LiveDungeonCapture.Capture(Folder, "elite_handover", cam, ppu, includeUi: true);

            // Leaving the Elite's room hides its bar even while it lives.
            var startCentre = InteriorCentre(run, run.Generation.Graph.StartId);
            player.transform.position = startCentre;
            body.position = startCentre;
            for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            yield return Frames(20);
            Assert.AreNotSame(room, run.CurrentRoom);
            Assert.IsFalse(hud.Snapshot.BossVisible, "outside the Elite's room: no Elite bar");
        }
    }
}

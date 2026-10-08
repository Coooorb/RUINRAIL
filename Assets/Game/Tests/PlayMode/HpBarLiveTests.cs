using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Networking;
using RuinRail.UI.Base;
using RuinRail.UI.Hud;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// The HUD health bar in a live run at 640×360 (shipping HUD composition): full, hit (damage chip), draining, low HP,
    /// healing, and the boss bar drawn by the same view — captured full-frame plus ×4 crops of the bars. HP itself is the
    /// real HealthComponent's: the readout and the bar's exact share always equal it. Captures: TestResults/HpBar.
    /// </summary>
    public sealed class HpBarLiveTests
    {
        private const string Folder = "TestResults/HpBar";
        private GameApp _app;
        private string _saveDir;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_hpbar_" + System.Guid.NewGuid().ToString("N"));
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

        private static IEnumerator Wait(float seconds)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        /// <summary>A ×4 crop of a capture (pixel space, bottom-left origin) for close inspection.</summary>
        private static void Crop(LiveDungeonCapture.Result shot, RectInt rect, string name)
        {
            const int zoom = 4;
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
        public IEnumerator LiveRun_HpBar_FullHitDrainLowHealAndBoss_ReadExactly([Values(11, 27)] int seed)
        {
            RuinRail.Core.Input.GameplayInputGate.Reset();
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(seed);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Hp Bar");
            hub.Onboarding.AcknowledgeStarterKit();
            _app.Settings.Current.Tutorial.ShowPrompts = false;
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            yield return Wait(0.5f);

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var biome = run.Expedition.State.Biome;
            var health = run.Rig.Player.GetComponent<HealthComponent>();
            var view = run.HudView;
            var bar = view.HpBar;
            Assert.IsNotNull(bar, "the shipping HUD composes the health bar view");
            var cam = run.Camera.Camera;
            var ppu = run.Camera.Config.PixelsPerUnit;
            var hpCrop = new RectInt(0, 0, 200, 40);

            void Exact(string state)
            {
                Assert.AreEqual($"{health.CurrentHealth} / {health.MaxHealth}", view.HpText.Replace("  [SHIELD]", ""), state + ": readout");
                Assert.AreEqual(health.CurrentHealth / (float)health.MaxHealth, bar.Target01, 0.0001f, state + ": exact share");
            }

            void Shot(string state)
            {
                var shot = LiveDungeonCapture.Capture(Folder, $"hp_{biome}_{state}", cam, ppu, includeUi: true);
                Crop(shot, hpCrop, $"hp_{biome}_{state}_crop");
            }

            Exact("full");
            Assert.AreEqual(1f, bar.Fill01, 0.0001f);
            Assert.IsFalse(bar.ChipVisible || bar.HealVisible || bar.IsLow);
            Assert.AreEqual((health.MaxHealth - 1) / DungeonHudView.HpNotchHp, bar.NotchCount, "one notch per 20 HP");
            Shot("1_full");

            // A hit: the fill drops at once to the exact HP, the lost span stays as a chip, holds, then drains.
            Assert.IsTrue(health.TryApplyDamage(new DamageRequest(30)));
            yield return null;
            Exact("hit");
            Assert.AreEqual(bar.Target01, bar.Fill01, 0.0001f, "damage shows at once");
            Assert.IsTrue(bar.ChipVisible, "the damage chip marks the lost span");
            Shot("2_hit");
            // Held, then draining: wait for the drain to start (frame time here includes capture stalls).
            var chipStart = bar.Chip01;
            var deadline = Time.realtimeSinceStartup + 2f;
            while (bar.Chip01 >= chipStart - 0.01f && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.Greater(bar.Chip01, bar.Fill01, "draining");
            Shot("3_draining");
            yield return Wait(1.2f);
            Assert.IsFalse(bar.ChipVisible, "the chip has drained");
            Shot("4_settled");

            // Low HP: a hot fill that breathes, and warm ink on the number.
            yield return Wait(0.6f);
            Assert.IsTrue(health.TryApplyDamage(new DamageRequest(health.CurrentHealth - Mathf.RoundToInt(health.MaxHealth * 0.2f))));
            yield return Wait(2f);
            Exact("low");
            Assert.IsTrue(bar.IsLow);
            Shot("5_low_a");
            yield return Wait(0.5f);
            Shot("6_low_b");

            // Healing: the gained span shows in green while the fill grows into it, then the bar is exact.
            Assert.IsTrue(health.Heal(40));
            yield return null;
            Exact("healing");
            Assert.IsTrue(bar.HealVisible, "the healed span shows");
            Assert.Less(bar.Fill01, bar.Target01, "the fill grows into it");
            Shot("7_healing");
            yield return Wait(0.6f);
            Assert.IsFalse(bar.HealVisible);
            Assert.AreEqual(bar.Target01, bar.Fill01, 0.0001f);
            Assert.IsFalse(bar.IsLow, "healed past the threshold");
            Shot("8_healed");

            // The boss bar is the same view: a stand-in boss health bound to the run's own HUD.
            var bossGo = new GameObject("StandInBoss");
            var boss = bossGo.AddComponent<HealthComponent>();
            boss.SetMaxHealth(2750);
            run.Hud.BindBoss("Subject Omega", boss, () => true);
            yield return Wait(0.3f);
            Assert.IsTrue(view.BossVisible);
            Assert.IsTrue(boss.TryApplyDamage(new DamageRequest(700)));
            yield return null;
            Assert.AreEqual(boss.CurrentHealth / (float)boss.MaxHealth, view.BossFill, 0.0001f);
            Assert.IsTrue(view.BossBar.ChipVisible);
            Assert.AreEqual(0, view.BossBar.NotchCount);
            yield return Wait(0.15f); // past the hit flash, the chip still holding
            var bossShot = LiveDungeonCapture.Capture(Folder, $"hp_{biome}_9_boss_hit", cam, ppu, includeUi: true);
            Crop(bossShot, new RectInt(270, 312, 240, 44), $"hp_{biome}_9_boss_hit_crop");
            run.Hud.BindBoss(null, null, null);
            Object.DestroyImmediate(bossGo);
        }
    }
}

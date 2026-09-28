using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Audio;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Expedition;
using RuinRail.Networking;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// The dungeon's persistent audio layer in real runs of each biome: arrival, walking rooms, a combat room and its
    /// clear, and the descend into the next depth never stack playback — exactly one ambience loop (at the ambience
    /// ceiling), never more than two music beds (only while crossfading), and no looping SFX left running — so nothing
    /// accumulates into a noise floor. The per-phase source table is logged as evidence ([NOISEFLOOR]).
    /// </summary>
    public sealed class AudioNoiseFloorLiveTests
    {
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ruinrail_noisefloor_" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(_saveDir);
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
            NetworkPlayerObject.VisualComposer = null;
            try { System.IO.Directory.Delete(_saveDir, true); } catch { }
        }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 30f;
            while (_app.ComposedScene != scene) { Assert.Less(Time.realtimeSinceStartup, deadline); yield return null; }
        }

        private static int SeedFor(Biome biome)
        {
            for (var seed = 1; seed < 500; seed++) if (BiomeSelector.SelectFirst(seed) == biome) return seed;
            return 1;
        }

        [UnityTest]
        public IEnumerator LiveRun_DungeonAudio_NeverStacksAmbienceMusicOrLoops_AcrossRoomsCombatAndDepths([Values(Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs)] Biome biome)
        {
            var log = new StringBuilder();
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(SeedFor(biome));
            UnityEngine.SceneManagement.SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Noise Probe");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(RuinRail.UI.Base.BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var health = run.Rig.Player.GetComponent<HealthComponent>();
            var audio = _app.Audio;

            IEnumerator Sample(string phase, float seconds)
            {
                var maxByKey = new Dictionary<string, int>();
                var volByKey = new Dictionary<string, float>();
                var before = AudioEventIds.Required.ToDictionary(r => r.id, r => audio.PlayedCount(r.id));
                var until = Time.unscaledTime + seconds;
                var frames = 0;
                while (Time.unscaledTime < until)
                {
                    health.Heal(100000);
                    frames++;
                    var playing = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Where(s => s.isPlaying).ToList();
                    var ambience = playing.Where(s => s.name == "Ambience").ToList();
                    Assert.AreEqual(1, ambience.Count, phase + ": exactly one ambience loop");
                    Assert.LessOrEqual(ambience[0].volume, MusicDirector.AmbienceGain() + 1e-4f, phase + ": ambience at its ceiling");
                    Assert.LessOrEqual(playing.Count(s => s.name.StartsWith("Music") && s.clip != null), 2, phase + ": at most two music beds (crossfade)");
                    Assert.IsFalse(playing.Any(s => s.loop && !s.name.StartsWith("Music") && s.name != "Ambience"), phase + ": no looping SFX left running");
                    foreach (var g in playing.GroupBy(s => $"{s.name}|{(s.clip != null ? s.clip.name : "null")}|loop={s.loop}"))
                    {
                        maxByKey[g.Key] = Mathf.Max(maxByKey.TryGetValue(g.Key, out var m) ? m : 0, g.Count());
                        volByKey[g.Key] = Mathf.Max(volByKey.TryGetValue(g.Key, out var v) ? v : 0f, g.Sum(s => s.volume));
                    }

                    yield return null;
                }

                log.AppendLine($"  [{phase}] {frames} frames; sources (max concurrent, max summed volume):");
                foreach (var k in maxByKey.Keys.OrderBy(k => k)) log.AppendLine($"    {k}: x{maxByKey[k]} vol {volByKey[k]:0.000}");
                var fired = AudioEventIds.Required.Select(r => (r.id, n: audio.PlayedCount(r.id) - before[r.id])).Where(x => x.n > 0).OrderByDescending(x => x.n).ToList();
                log.AppendLine("    sfx fired: " + string.Join(", ", fired.Select(x => $"{x.id} {x.n}")));
            }

            log.AppendLine($"{biome}: gains music {AudioService.GainFor(AudioBus.Music):0.00} sfx {AudioService.GainFor(AudioBus.Weapons):0.00} ambience {MusicDirector.AmbienceGain():0.000}");
            yield return Sample("arrival idle", 4f);
            var start = run.Rooms[run.Generation.Graph.StartId];
            foreach (var room in run.Rooms.Values.Where(r => r.State.RoomType != RoomType.Boss && r.State.RoomType != RoomType.Combat).Take(3))
            {
                run.Rig.Player.transform.position = room.InteriorWorldBounds.center;
                run.Rig.Player.GetComponent<Rigidbody2D>().position = room.InteriorWorldBounds.center;
                yield return Sample("walk " + room.State.RoomType, 1.5f);
            }

            var combat = run.Rooms.Values.First(r => r.State.RoomType == RoomType.Combat && !r.State.IsElite && r.HasEncounter && r.Lifecycle == RoomLifecycleState.Unentered);
            run.Rig.Player.transform.position = combat.InteriorWorldBounds.center;
            run.Rig.Player.GetComponent<Rigidbody2D>().position = combat.InteriorWorldBounds.center;
            yield return Sample("combat", 3f);
            for (var guard = 0; guard < 120 && combat.Lifecycle != RoomLifecycleState.Cleared; guard++)
            {
                foreach (var e in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (e != null && e.IsAlive && combat.InteriorWorldBounds.Contains(e.transform.position)) e.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(999999));
                yield return null;
            }

            yield return Sample("after combat", 3f);
            var bossRoom = run.Rooms[run.Generation.Graph.BossId];
            var centre = EncounterRewardPlacement.WorldCenter(bossRoom.Root).Value;
            run.Rig.Player.transform.position = centre;
            run.Rig.Player.GetComponent<Rigidbody2D>().position = centre;
            for (var i = 0; i < 6; i++) yield return new WaitForFixedUpdate();
            BossIntroSequence.Current?.Finish();
            bossRoom.GetComponent<RoomContentBinding>().Boss.Boss.Health.TryApplyDamage(new DamageRequest(100000000));
            var deadline = Time.realtimeSinceStartup + 20f;
            while (run.TransitDecisionView == null) { Assert.Less(Time.realtimeSinceStartup, deadline, "the transit decision opened"); yield return null; }
            var built = run.DepthsBuilt;
            run.TransitDecisionView.Buttons[RuinRail.UI.Hud.TransitDecisionView.DescendId].SimulateClick();
            deadline = Time.realtimeSinceStartup + 30f;
            while (run.DepthsBuilt == built) { Assert.Less(Time.realtimeSinceStartup, deadline, "the next depth was built"); yield return null; }
            for (var i = 0; i < 20; i++) yield return null;
            yield return Sample("depth 2 arrival (" + run.Expedition.State.Biome + ")", 4f);
            Debug.Log("[NOISEFLOOR]\n" + log);
        }
    }
}

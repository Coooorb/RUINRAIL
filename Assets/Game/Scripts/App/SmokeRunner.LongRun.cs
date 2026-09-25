using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Expedition;
using RuinRail.UI.Base;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

namespace RuinRail.App
{
    /// <summary>
    /// The long-run release scenario (`-smoke -smoke-scenario longrun -seed 11 -savedir fresh-dir`): one solo expedition
    /// in the shipped player descends <see cref="LongRunDepths"/> times through the real boss → Transit vote path and
    /// samples every depth — rebuild time, live GameObjects, Mono heap, pooled projectiles, cameras, listeners, network
    /// objects, stale enemies, and the static subscriber count of the projectile channel — then churns the inventory and
    /// pause UI, returns, and times the save and the load. It measures; the bounds it asserts are leak bounds (flat after
    /// warm-up), not performance targets.
    /// </summary>
    public sealed partial class SmokeRunner
    {
        public const string LongRunScenario = "longrun";
        public const int LongRunDepths = 12;

        private IEnumerator RunLongRun()
        {
            _result.Scenario = LongRunScenario;
            Application.logMessageReceived += OnLog;
            var passed = new List<string>();
            var samples = new List<string> { "depth,rebuild_ms,game_objects,mono_mb,allocated_mb,projectiles_pooled,cameras,listeners,network_objects,stale_enemies,launch_subscribers" };
            void Check(string what, bool ok) { if (ok) passed.Add(what); else if (string.IsNullOrEmpty(_result.Error)) Fail("longrun: " + what); }
            try
            {
                yield return WaitFor(() => _composed.Contains(SceneNames.MainMenu), "main menu composed");
                var menu = _app.Menu;
                menu.Play();
                yield return WaitFor(() => _composed.Contains(SceneNames.Base), "base composed");
                var screen = FindFirstObjectByType<BaseHubScreen>();
                if (screen == null) { Fail("longrun: no Shelter"); yield break; }
                screen.Onboarding?.SubmitDisplayName(SmokeDisplayName);
                screen.Onboarding?.AcknowledgeStarterKit();
                if (!screen.Hub.Multiplayer.SetReady(true)) { Fail("longrun: ready refused"); yield break; }
                screen.Hub.Open(BaseStation.Transit);
                var generation = Stopwatch.StartNew();
                if (!screen.Hub.Transit.StartExpedition()) { Fail("longrun: start refused"); yield break; }
                yield return WaitFor(() => _composed.EndsWith(SceneNames.Dungeon + ";"), "dungeon composed");
                generation.Stop();
                for (var i = 0; i < 10; i++) yield return null;
                var run = FindFirstObjectByType<ExpeditionScene>();
                if (run == null || run.Rig?.Player == null) { Fail("longrun: no player"); yield break; }
                _result.RunSeed = run.Expedition.State.RunSeed;
                _result.Biome = run.Expedition.State.Biome.ToString();
                var player = run.Rig.Player;
                player.GetComponent<HealthComponent>().SetInvulnerabilityState(new SmokeGuard());
                Stage("longrun");

                int Launchers()
                {
                    var field = typeof(ProjectilePool).GetField("Launched", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    return (field?.GetValue(null) as System.Delegate)?.GetInvocationList().Length ?? 0;
                }

                string Sample(int depth, double rebuildMs)
                {
                    System.GC.Collect();
                    var stale = FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Count(e => e != null && e.IsAlive && !run.Rooms.Values.Any(r => r.InteriorWorldBounds.Contains(e.transform.position)));
                    return string.Join(",", depth, rebuildMs.ToString("0"), FindObjectsByType<Transform>(FindObjectsSortMode.None).Length,
                        (Profiler.GetMonoUsedSizeLong() / 1048576f).ToString("0.00"), (Profiler.GetTotalAllocatedMemoryLong() / 1048576f).ToString("0.0"),
                        FindObjectsByType<Projectile>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Camera.allCamerasCount,
                        FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length, FindObjectsByType<Unity.Netcode.NetworkObject>(FindObjectsSortMode.None).Length, stale, Launchers());
                }

                samples.Add(Sample(1, generation.Elapsed.TotalMilliseconds));
                var objects = new List<int>();
                var mono = new List<float>();
                for (var step = 0; step < LongRunDepths && string.IsNullOrEmpty(_result.Error); step++)
                {
                    var bossRoom = run.Rooms.Values.FirstOrDefault(r => r.State.RoomType == RoomType.Boss);
                    var boss = bossRoom != null ? bossRoom.GetComponent<RoomContentBinding>()?.Boss : null;
                    if (boss?.Boss == null) { Fail($"longrun: depth {run.Expedition.State.Depth} has no boss"); yield break; }
                    var centre = bossRoom.InteriorWorldBounds.center;
                    player.transform.position = centre;
                    player.GetComponent<Rigidbody2D>().position = centre;
                    for (var i = 0; i < 5; i++) yield return new WaitForFixedUpdate();
                    boss.Boss.Health.TryApplyDamage(new DamageRequest(100000000));
                    yield return WaitFor(() => run.Vote != null && run.Expedition.Transit?.State == TransitDecisionState.Open, $"transit open on depth {run.Expedition.State.Depth}");
                    if (!string.IsNullOrEmpty(_result.Error)) yield break;
                    var built = run.DepthsBuilt;
                    var rebuild = Stopwatch.StartNew();
                    if (!run.Vote.Vote(TransitChoice.DescendDeeper)) { Fail($"longrun: descend vote refused on depth {run.Expedition.State.Depth}"); yield break; }
                    yield return WaitFor(() => run.DepthsBuilt > built, $"depth {run.Expedition.State.Depth + 1} built");
                    rebuild.Stop();
                    for (var i = 0; i < 10; i++) yield return null;
                    var line = Sample(run.Expedition.State.Depth, rebuild.Elapsed.TotalMilliseconds);
                    samples.Add(line);
                    var cells = line.Split(',');
                    objects.Add(int.Parse(cells[2]));
                    mono.Add(float.Parse(cells[3], System.Globalization.CultureInfo.InvariantCulture));
                    Check($"depth {run.Expedition.State.Depth}: one camera, one listener, no network objects, no stale enemies ({line})",
                        cells[6] == "1" && cells[7] == "1" && cells[8] == "0" && cells[9] == "0");
                }

                if (!string.IsNullOrEmpty(_result.Error)) yield break;
                Check($"{LongRunDepths} consecutive descends reached depth {run.Expedition.State.Depth}", run.Expedition.State.Depth == LongRunDepths + 1);
                // Leak bounds: after the first three depths warm the pools, objects and heap stay flat.
                var warm = objects.Skip(2).ToList();
                Check($"live GameObjects stay flat after warm-up ({warm.Min()}–{warm.Max()})", warm.Max() - warm.Min() <= 250);
                var heap = mono.Skip(2).ToList();
                Check($"Mono heap stays bounded after warm-up ({heap.First():0.0} → {heap.Last():0.0} MB, max {heap.Max():0.0})", heap.Last() - heap.First() < 24f);
                var launchers = samples.Skip(2).Select(s => s.Split(',')[10]).Distinct().ToList();
                Check($"the projectile channel's subscriber count does not grow with depth ({string.Join("/", launchers)})", launchers.Count == 1);

                // UI churn: inventory and pause opened and closed 50 times each leave nothing behind.
                System.GC.Collect();
                var before = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                for (var i = 0; i < 50; i++)
                {
                    run.Inventory.Toggle(); yield return null; run.Inventory.Toggle(); yield return null;
                    run.Pause.Open(); yield return null; run.Pause.Close(); yield return null;
                }

                var after = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                Check($"50 inventory + 50 pause open/close cycles leave the object count unchanged ({before} → {after})", after - before <= 4 && !run.Inventory.IsOpen && !run.Pause.IsOpen);
                samples.Add($"ui_churn,,{before}->{after},,,,,,,,");

                // Return, then time the save and the load of the profile this run produced.
                run.Expedition.AddCarriedCoins(10);
                run.Vote?.Vote(TransitChoice.ReturnToShelter);
                if (run.Vote != null && run.Vote.AwaitingReturnConfirmation) run.Vote.ConfirmReturn();
                if (run.Expedition.IsExpeditionActive) run.Expedition.Return();
                yield return WaitFor(() => _composed.EndsWith(SceneNames.Base + ";"), "base composed after return");
                var save = Stopwatch.StartNew();
                var saved = menu.Session.SaveNow("smoke_longrun");
                save.Stop();
                var load = Stopwatch.StartNew();
                var loaded = _app.Saves.Load();
                load.Stop();
                samples.Add($"save_ms,{save.Elapsed.TotalMilliseconds:0.0},,,,,,,,,");
                samples.Add($"load_ms,{load.Elapsed.TotalMilliseconds:0.0},,,,,,,,,");
                Check($"save {save.Elapsed.TotalMilliseconds:0.0} ms and load {load.Elapsed.TotalMilliseconds:0.0} ms of the long-run profile succeed; deepest D{loaded.Slot?.Profile.DeepestDepthReached}",
                    saved == RuinRail.Persistence.SaveError.None && loaded.Success && loaded.Slot.Profile.DeepestDepthReached == LongRunDepths + 1);
                _result.DeepestDepthReached = loaded.Success ? loaded.Slot.Profile.DeepestDepthReached : 0;
                if (string.IsNullOrEmpty(_result.Error))
                {
                    _result.Success = true;
                    Stage("done");
                }
            }
            finally
            {
                _result.ReturningChecks = passed.ToArray();
                _result.LongRunSamples = samples.ToArray();
                Debug.Log("[SMOKE] longrun: " + string.Join("; ", passed));
                _result.ScenesComposed = _composed;
                Write();
                Application.logMessageReceived -= OnLog;
                Application.Quit(_result.Success ? 0 : 1);
            }
        }
    }
}

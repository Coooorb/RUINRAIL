using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuinRail.Core;
using RuinRail.Gameplay.Base;
using RuinRail.UI.Base;
using UnityEngine;
using UnityEngine.Profiling;

namespace RuinRail.App
{
    /// <summary>
    /// TASK 183 — profiles the actual Windows player, not the editor.
    ///
    /// The TASK-143 numbers came from the editor test runner and its managed-allocation counter reported zeroes, so
    /// they cannot carry a release decision. This runs inside the built player with graphics on, drives the real
    /// expedition loop, and measures what the player's machine actually does: the frame-time
    /// distribution (not just an average, which hides hitches), heap growth over a long session, and how long scene
    /// composition and dungeon generation take.
    ///
    /// It reports percentiles because a 60 fps average with a 120 ms p99 is a game that stutters, and an average
    /// alone cannot tell you that.
    /// </summary>
    public sealed class PlayerProfileRunner : MonoBehaviour
    {
        public const string ProfileArgument = "-profile";
        public const string SecondsArgument = "-profileseconds";
        public const string ResultFile = "profile_result.json";

        /// <summary>Frames discarded at the start of a measurement window: shader/asset warm-up is not steady state.</summary>
        private const int WarmupFrames = 120;

        [Serializable]
        public sealed class DepthSample
        {
            public int Depth;
            public int Rooms;
            public float GenerationMs;
            public float HeapMb;
            public int LiveGameObjects;
        }

        [Serializable]
        public sealed class Result
        {
            public bool Success;
            public string Stage = "boot";
            public string Error = "";

            public string UnityVersion = Application.unityVersion;
            public string Device = SystemInfo.deviceModel;
            public string Cpu = SystemInfo.processorType;
            public int CpuCores = SystemInfo.processorCount;
            public int SystemMemoryMb = SystemInfo.systemMemorySize;
            public string Gpu = SystemInfo.graphicsDeviceName;
            public string GraphicsApi = SystemInfo.graphicsDeviceType.ToString();
            public string Resolution = "";
            public string FullScreenMode = "";
            public int TargetFrameRate;
            public int VSyncCount;
            public bool IsDevelopmentBuild = Debug.isDebugBuild;

            public int MeasuredFrames;
            public float DurationSeconds;
            public float AvgFrameMs;
            public float P50FrameMs;
            public float P95FrameMs;
            public float P99FrameMs;
            public float WorstFrameMs;
            public float AvgFps;
            public float OnePercentLowFps;
            public int FramesOver33Ms;
            public int FramesOver16Ms;

            public float HeapStartMb;
            public float HeapEndMb;
            public float HeapGrowthMb;
            public long TotalAllocatedBytesDelta;

            public float MainMenuComposeMs;
            public float BaseComposeMs;
            public float FirstDungeonComposeMs;
            public int HeapSamples;
            public List<DepthSample> Depths = new();
        }

        private GameApp _app;
        private readonly Result _result = new();
        private readonly List<float> _frameMs = new(64 * 1024);
        private bool _sampling;
        private int _warmupRemaining;

        public Result Current => _result;

        public static bool Requested(string[] args) => args != null && args.Contains(ProfileArgument);

        public static float SecondsFrom(string[] args)
        {
            var i = Array.IndexOf(args, SecondsArgument);
            return i >= 0 && i + 1 < args.Length && float.TryParse(args[i + 1], out var s) ? Mathf.Clamp(s, 5f, 600f) : 60f;
        }

        public void Begin(GameApp app, float seconds)
        {
            _app = app;
            StartCoroutine(Run(seconds));
        }

        private void Update()
        {
            if (!_sampling) return;
            if (_warmupRemaining > 0) { _warmupRemaining--; return; }
            _frameMs.Add(Time.unscaledDeltaTime * 1000f);
        }

        private IEnumerator Run(float seconds)
        {
            Application.logMessageReceived += OnLog;
            var watch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                _result.Resolution = $"{Screen.width}x{Screen.height}";
                _result.FullScreenMode = Screen.fullScreenMode.ToString();
                _result.TargetFrameRate = Application.targetFrameRate;
                _result.VSyncCount = QualitySettings.vSyncCount;

                yield return WaitFor(() => _app.ComposedScene == SceneNames.MainMenu, "main menu");
                _result.MainMenuComposeMs = (float)watch.Elapsed.TotalMilliseconds;
                _result.Stage = "menu";

                var menu = _app.Menu;
                if (menu.Play() == PlayOutcome.Failed) { Fail("PLAY failed: " + menu.Message); yield break; }
                yield return WaitFor(() => _app.ComposedScene == SceneNames.Base, "shelter");
                _result.BaseComposeMs = (float)watch.Elapsed.TotalMilliseconds - _result.MainMenuComposeMs;
                _result.Stage = "base";

                var screen = FindFirstObjectByType<BaseHubScreen>();
                if (screen == null) { Fail("no BaseHubScreen"); yield break; }
                if (!screen.Hub.Multiplayer.SetReady(true)) { Fail("ready refused"); yield break; }
                screen.Hub.Open(BaseStation.Transit);

                var dungeonWatch = System.Diagnostics.Stopwatch.StartNew();
                if (!screen.Hub.Transit.StartExpedition()) { Fail("start refused"); yield break; }
                yield return WaitFor(() => _app.ComposedScene == SceneNames.Dungeon, "dungeon");
                _result.FirstDungeonComposeMs = (float)dungeonWatch.Elapsed.TotalMilliseconds;
                _result.Stage = "dungeon";
                yield return null;

                var run = FindFirstObjectByType<ExpeditionScene>();
                if (run == null || run.Rooms == null || run.Rooms.Count == 0) { Fail("no rooms composed"); yield break; }

                // Steady state from here: warm up, then sample every frame.
                _result.HeapStartMb = HeapMb();
                var allocStart = GC.GetTotalMemory(false);
                _warmupRemaining = WarmupFrames;
                _sampling = true;

                // Descent is deliberately not forced: ExpeditionService.Descend requires the depth's boss to be
                // defeated, and driving that needs real combat input. The run therefore profiles steady-state
                // gameplay at one depth, and samples the heap periodically so growth over a long session is visible.
                var deadline = Time.realtimeSinceStartup + seconds;
                var nextSample = Time.realtimeSinceStartup + Mathf.Max(5f, seconds / 6f);
                RecordDepth(run, _result.FirstDungeonComposeMs);

                while (Time.realtimeSinceStartup < deadline)
                {
                    if (Time.realtimeSinceStartup >= nextSample)
                    {
                        RecordDepth(run, 0f);
                        nextSample = Time.realtimeSinceStartup + Mathf.Max(5f, seconds / 6f);
                    }

                    yield return null;
                }

                _sampling = false;
                _result.HeapEndMb = HeapMb();
                _result.HeapGrowthMb = _result.HeapEndMb - _result.HeapStartMb;
                _result.TotalAllocatedBytesDelta = GC.GetTotalMemory(false) - allocStart;
                _result.DurationSeconds = seconds;
                Summarise();

                // Prove the candidate still completes the loop under load, not just that it renders fast.
                run.Expedition.AddCarriedCoins(10);
                if (!run.Expedition.Return().IsSuccess) { Fail("return failed under load"); yield break; }
                yield return WaitFor(() => _app.ComposedScene == SceneNames.Base, "shelter after return");
                if (menu.Session.SaveNow("profile") != RuinRail.Persistence.SaveError.None) { Fail("save failed under load"); yield break; }

                _result.Success = true;
                _result.Stage = "done";
            }
            finally
            {
                _sampling = false;
                Write();
                Application.logMessageReceived -= OnLog;
                Application.Quit(_result.Success ? 0 : 1);
            }
        }

        private void RecordDepth(ExpeditionScene run, float generationMs)
        {
            _result.Depths.Add(new DepthSample
            {
                Depth = run.Expedition.State.Depth,
                Rooms = run.Rooms?.Count ?? 0,
                GenerationMs = generationMs,
                HeapMb = HeapMb(),
                LiveGameObjects = FindObjectsByType<Transform>(FindObjectsSortMode.None).Length
            });
            _result.HeapSamples = _result.Depths.Count;
        }

        private void Summarise()
        {
            if (_frameMs.Count == 0) { _result.Error = "no frames sampled"; return; }

            var sorted = _frameMs.ToArray();
            Array.Sort(sorted);

            _result.MeasuredFrames = sorted.Length;
            _result.AvgFrameMs = _frameMs.Average();
            _result.P50FrameMs = Percentile(sorted, 0.50f);
            _result.P95FrameMs = Percentile(sorted, 0.95f);
            _result.P99FrameMs = Percentile(sorted, 0.99f);
            _result.WorstFrameMs = sorted[^1];
            _result.AvgFps = _result.AvgFrameMs > 0f ? 1000f / _result.AvgFrameMs : 0f;
            // The 1% low is the mean of the worst 1% of frames: what a stutter actually feels like.
            var tail = Mathf.Max(1, sorted.Length / 100);
            var tailMean = sorted.Skip(sorted.Length - tail).Average();
            _result.OnePercentLowFps = tailMean > 0f ? 1000f / tailMean : 0f;
            _result.FramesOver33Ms = sorted.Count(f => f > 33.3f);
            _result.FramesOver16Ms = sorted.Count(f => f > 16.7f);
        }

        private static float Percentile(float[] sorted, float p) =>
            sorted[Mathf.Clamp(Mathf.CeilToInt(p * sorted.Length) - 1, 0, sorted.Length - 1)];

        private static float HeapMb() => Profiler.GetMonoUsedSizeLong() / (1024f * 1024f);

        private IEnumerator WaitFor(Func<bool> condition, string what)
        {
            var deadline = Time.realtimeSinceStartup + 60f;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline) { Fail($"timed out waiting for {what}"); yield break; }
                yield return null;
            }
        }

        private void Fail(string error)
        {
            _result.Success = false;
            if (_result.Error.Length == 0) _result.Error = error;
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type is LogType.Exception or LogType.Error && _result.Error.Length == 0)
                _result.Error = $"{type}: {condition}";
        }

        private void Write()
        {
            try
            {
                var path = Path.Combine(_app != null ? _app.SaveDirectory : Application.persistentDataPath, ResultFile);
                File.WriteAllText(path, JsonUtility.ToJson(_result, true));
            }
            catch (IOException e)
            {
                Debug.LogError("profile result could not be written: " + e.Message);
            }
        }
    }
}

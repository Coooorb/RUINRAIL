using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using RuinRail.Presentation.Animation;
using RuinRail.Presentation.Vfx;
using Unity.Netcode;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// The <c>dashfeel</c> scenario: every member in turn dashes through the shipping input path — six dashes with
    /// direction changes, then one more in a live fight — while every peer records how it draws that player's dash: the
    /// dash animation, the launch burst, the afterimages (and how far each sits from the body path it actually drew), the
    /// presented direction and start (server time), and the effect pool emptying afterwards. The host requires the
    /// presentation exactly once on every peer, pure replicas to mirror the dash without ever simulating it, and the
    /// authoritative dash itself unchanged (one host-side dash, the approved distance). With a graphics device each
    /// peer also renders 640×360 frames of its own view mid-dash next to its report.
    /// </summary>
    public sealed partial class CoopExpeditionProof
    {
        private static readonly Vector2[] DashFeelDirections =
        {
            Vector2.right, Vector2.left, Vector2.up, Vector2.down, new Vector2(1f, 1f).normalized, new Vector2(-1f, -1f).normalized
        };

        private const float DashWindowSeconds = 1.8f;
        private const float DashPressAt = 0.5f;
        private bool _dashCombat;

        [Serializable]
        public sealed class DashFeelLog
        {
            public string Role = "";
            public bool Found;
            public bool Pressed;
            public int RisingEdges;
            public int Dashes;
            public int Bursts;
            public int Ghosts;
            public int AnimDashFrames;
            public bool SimulatedSeen;
            public int StartedDelta;
            public float DirX;
            public float DirY;
            public double StartServerTime = -1;
            public float PresentedSeconds;
            public float MaxOffPath;
            public int LiveAfter = -1;
            public float Travel;
            public string Capture = "";
        }

        /// <summary>The first render into a texture stalls a frame for ~0.5 s (pipeline/shader warm-up): pay it before any measured dash.</summary>
        private IEnumerator WarmDashCapture()
        {
            var log = new DashFeelLog();
            CaptureDashFrame("warmup", log);
            if (log.Capture.EndsWith(".png", StringComparison.Ordinal))
                File.Delete(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_out)) ?? ".", "dashfeel", log.Capture));
            yield return Seconds(0.5f);
        }

        private IEnumerator ClientDashWarm(ProofMessage command)
        {
            yield return WarmDashCapture();
            Ack(command);
        }

        private IEnumerator ClientDashWatch(ProofMessage command)
        {
            var log = new DashFeelLog { Role = "client" };
            yield return RecordDash(ulong.Parse(command.Arg), new Vector2(command.X, command.Y), command.Number == 1, command.Json, log);
            Ack(command, JsonUtility.ToJson(log));
        }

        /// <summary>The dasher as this peer has it: its own player, the host's simulation of a member, or a pure replica.</summary>
        private GameObject DasherOnThisPeer(ulong dasherId)
        {
            var network = NetworkManager.Singleton;
            if (network != null && network.LocalClientId == dasherId) return LocalPlayer;
            if (_run?.CoopHost != null) return _run.MemberEntity(dasherId);
            return UnityEngine.Object.FindObjectsByType<NetworkPlayerObject>(FindObjectsSortMode.None).FirstOrDefault(p => p.OwnerClientId == dasherId)?.gameObject;
        }

        /// <summary>One dash window on this peer: when <paramref name="press"/>, this peer's player dashes along <paramref name="direction"/>.</summary>
        private IEnumerator RecordDash(ulong dasherId, Vector2 direction, bool press, string captureTag, DashFeelLog log)
        {
            var dasher = DasherOnThisPeer(dasherId);
            var dash = dasher != null ? dasher.GetComponent<PlayerDash>() : null;
            var trail = dasher != null ? dasher.GetComponent<DashTrailVfx>() : null;
            var animation = dasher != null ? dasher.GetComponent<PlayerAnimationDriver>() : null;
            var bodyRenderer = dasher != null ? CharacterVisual.RendererOf(dasher) : null;
            log.Found = dash != null && trail != null && animation != null && bodyRenderer != null;
            if (!log.Found) { yield return Seconds(DashWindowSeconds); yield break; }

            var dashes = trail.DashesShown;
            var bursts = trail.BurstsShown;
            var ghosts = trail.GhostsShown;
            var started = dash.DashesStarted;
            var from = (Vector2)dasher.transform.position;
            var path = new List<Vector2>();
            var seen = new HashSet<PooledEffect>();
            var ghostAt = new List<Vector2>();
            var wasPresented = dash.IsDashingPresented;
            var risenAt = -1f;
            var captured = string.IsNullOrEmpty(captureTag);
            var windowStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup < windowStart + DashWindowSeconds)
            {
                if (_dashCombat && _run?.CoopHost != null) KeepPartyAlive();
                if (press && !log.Pressed && Time.realtimeSinceStartup >= windowStart + DashPressAt)
                {
                    // The shipping input path: the move the dash reads, one Dash press, then hands off the stick.
                    _reader.SetMove(direction);
                    _reader.PressDash();
                    _reader.SetMove(Vector2.zero);
                    log.Pressed = true;
                }

                var presented = dash.IsDashingPresented;
                if (presented && !wasPresented)
                {
                    log.RisingEdges++;
                    log.DirX = dash.DashDirectionPresented.x;
                    log.DirY = dash.DashDirectionPresented.y;
                    log.StartServerTime = NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : -1;
                    risenAt = Time.realtimeSinceStartup;
                }
                else if (!presented && wasPresented && risenAt >= 0f) log.PresentedSeconds = Time.realtimeSinceStartup - risenAt;

                wasPresented = presented;
                if (dash.IsDashing) log.SimulatedSeen = true;
                if (animation.State == PlayerAnimState.Dash) log.AnimDashFrames++;
                path.Add(bodyRenderer.transform.position);
                if (trail.Pool != null)
                    foreach (Transform child in trail.Pool.transform)
                    {
                        var effect = child.GetComponent<PooledEffect>();
                        if (effect == null || !effect.IsActive || effect.Kind != "dash_ghost") { if (effect != null) seen.Remove(effect); continue; }
                        if (seen.Add(effect)) ghostAt.Add(effect.transform.position);
                    }

                if (!captured && risenAt >= 0f && Time.realtimeSinceStartup >= risenAt + 0.09f)
                {
                    captured = true;
                    CaptureDashFrame(captureTag, log);
                }

                yield return null;
            }

            log.Dashes = trail.DashesShown - dashes;
            log.Bursts = trail.BurstsShown - bursts;
            log.Ghosts = trail.GhostsShown - ghosts;
            log.StartedDelta = dash.DashesStarted - started;
            log.Travel = Vector2.Distance(from, dasher.transform.position);
            log.LiveAfter = trail.Pool != null ? trail.Pool.Live : -1;
            log.MaxOffPath = ghostAt.Count == 0 ? 0f : ghostAt.Max(g => DistanceToPath(g, path));
        }

        private static float DistanceToPath(Vector2 point, IReadOnlyList<Vector2> path)
        {
            var best = float.MaxValue;
            for (var i = 0; i < path.Count; i++)
            {
                var a = path[i];
                var b = i + 1 < path.Count ? path[i + 1] : a;
                var ab = b - a;
                var t = ab.sqrMagnitude < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(point - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(point, a + ab * t));
            }

            return best;
        }

        /// <summary>
        /// This peer's 640×360 view (world camera position and size, HUD on top) rendered into a texture — needs only a
        /// graphics device, not a screen, so it works in -batchmode (the LiveDungeonCapture technique of the PlayMode suite).
        /// </summary>
        private void CaptureDashFrame(string tag, DashFeelLog log)
        {
            var world = _run?.Camera != null ? _run.Camera.Camera : null;
            if (world == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { log.Capture = "no graphics device"; return; }
            const int width = 640, height = 360;
            var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).OrderBy(c => c.sortingOrder).ToArray();
            var go = new GameObject("DashFeelCaptureCamera");
            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = height / (2f * _run.Camera.Config.PixelsPerUnit);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = world.backgroundColor;
            camera.cullingMask = world.cullingMask;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 100f;
            go.transform.position = new Vector3(world.transform.position.x, world.transform.position.y, -10f);
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            camera.targetTexture = target;
            var restore = canvases.Select(c => (canvas: c, scaler: c.GetComponent<UnityEngine.UI.CanvasScaler>())).Select(x => (x.canvas, x.scaler, mode: x.scaler != null ? x.scaler.uiScaleMode : default, factor: x.scaler != null ? x.scaler.scaleFactor : 1f, layer: x.canvas.sortingLayerID)).ToArray();
            try
            {
                for (var i = 0; i < canvases.Length; i++)
                {
                    canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
                    canvases[i].worldCamera = camera;
                    canvases[i].planeDistance = 1f + (canvases.Length - 1 - i) * 0.01f;
                    canvases[i].sortingLayerName = RuinRail.Core.Rendering.SortingLayers.ScreenUI;
                    if (restore[i].scaler != null) { restore[i].scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize; restore[i].scaler.scaleFactor = 1f; }
                }

                Canvas.ForceUpdateCanvases();
                camera.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                RenderTexture.active = previous;
                var dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_out)) ?? ".", "dashfeel");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"{(_run.CoopHost != null ? "host" : "client" + NetworkManager.Singleton?.LocalClientId)}_{tag}.png");
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Destroy(texture);
                log.Capture = Path.GetFileName(path);
            }
            finally
            {
                foreach (var (canvas, scaler, mode, factor, layer) in restore)
                {
                    if (canvas == null) continue;
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.worldCamera = null;
                    canvas.sortingLayerID = layer;
                    if (scaler != null) { scaler.uiScaleMode = mode; scaler.scaleFactor = factor; }
                }

                camera.targetTexture = null;
                Destroy(go);
                target.Release();
                Destroy(target);
            }
        }

        // ---------------------------------------------------------------- host side

        private IEnumerator DashFeelScenario(List<ulong> clients)
        {
            yield return ReportAll();
            var hostView = View("host");
            _report.Views.Add(hostView);
            Record("identical D1 on every peer", clients.All(c => ViewOf(c) is { } v && v.LayoutFingerprint == hostView.LayoutFingerprint && v.RoomSignature == hostView.RoomSignature),
                $"host={hostView.Seed}/{hostView.LayoutFingerprint} " + string.Join(" ", clients.Select(c => $"c{c}={ViewOf(c)?.LayoutFingerprint}")));

            // An open spot in the start room: 3.4 tiles clear in all eight dash directions, the other members 4.5 tiles to either side.
            var room = _run.Rooms[_run.Generation.Graph.StartId];
            var interior = room.InteriorWorldBounds;
            Vector2? centre = null;
            bool Static(RaycastHit2D h) => h.collider != null && !h.collider.isTrigger && (h.collider.attachedRigidbody == null || h.collider.attachedRigidbody.bodyType == RigidbodyType2D.Static);
            var compass = new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down, new Vector2(1f, 1f).normalized, new Vector2(-1f, -1f).normalized, new Vector2(1f, -1f).normalized, new Vector2(-1f, 1f).normalized };
            for (var y = interior.center.y; centre == null && y < interior.yMax - 3.5f; y += 0.5f)
            for (var sign = -1; centre == null && sign <= 1; sign += 2)
            for (var x = interior.xMin + 5f; centre == null && x < interior.xMax - 5f; x += 0.5f)
            {
                var c = new Vector2(x, interior.center.y + (y - interior.center.y) * sign);
                if (!RoomRuntime.IsSpawnClear(c) || !RoomRuntime.IsSpawnClear(c + Vector2.left * 4.5f) || !RoomRuntime.IsSpawnClear(c + Vector2.right * 4.5f)) continue;
                if (compass.Any(d => Physics2D.CircleCastAll(c, 0.45f, d, 3.4f).Any(Static))) continue;
                centre = c;
            }

            if (centre == null) { Record("an open dash spot in the start room", false, "none"); Finish("dashfeel", "no dash spot"); yield break; }
            yield return WarmDashCapture();
            yield return Command(new ProofMessage { Step = "dash-warm" }, clients, 20f);
            var members = new List<ulong> { NetworkManager.ServerClientId };
            members.AddRange(clients);
            var replicaWindows = 0;
            var replicaSimulated = 0;

            IEnumerator Stage(ulong dasher)
            {
                // The dasher in the middle, everyone else to the sides: in every peer's 640×360 view, never in the dash's path.
                var others = members.Where(m => m != dasher).ToList();
                Teleport(dasher == NetworkManager.ServerClientId ? HostPlayer : _run.MemberEntity(dasher), centre.Value);
                for (var i = 0; i < others.Count; i++)
                    Teleport(others[i] == NetworkManager.ServerClientId ? HostPlayer : _run.MemberEntity(others[i]), centre.Value + (i % 2 == 0 ? Vector2.left : Vector2.right) * 4.5f);
                yield return Seconds(1.2f);
            }

            IEnumerator Window(ulong dasher, Vector2 direction, int index, string capture, bool combat)
            {
                var hostLog = new DashFeelLog { Role = "host" };
                foreach (var c in clients) _acks.Remove(c);
                foreach (var c in clients)
                    SendProof(c, new ProofMessage { Step = "dash-watch", Arg = dasher.ToString(), X = direction.x, Y = direction.y, Number = c == dasher ? 1 : 0, Json = capture });
                yield return RecordDash(dasher, direction, dasher == NetworkManager.ServerClientId, capture, hostLog);
                yield return WaitFor(() => clients.All(c => _acks.TryGetValue(c, out var a) && a.Step == "ack:dash-watch"), 20f);

                var logs = new Dictionary<ulong, DashFeelLog> { [NetworkManager.ServerClientId] = hostLog };
                foreach (var c in clients)
                    logs[c] = _acks.TryGetValue(c, out var ack) && !string.IsNullOrEmpty(ack.Arg) ? JsonUtility.FromJson<DashFeelLog>(ack.Arg) : null;
                var who = dasher == NetworkManager.ServerClientId ? "host" : $"client {dasher}";
                var label = $"dash presentation exactly once on every peer: {who} #{index} ({direction.x:0.##},{direction.y:0.##}){(combat ? " in a live fight" : "")}";
                var problems = new List<string>();
                var dasherStart = logs.TryGetValue(dasher, out var own) && own != null ? own.StartServerTime : -1;
                foreach (var (peer, log) in logs)
                {
                    var name = peer == NetworkManager.ServerClientId ? "host" : $"client {peer}";
                    if (log == null) { problems.Add($"{name}: no log"); continue; }
                    if (!log.Found) { problems.Add($"{name}: dasher/presentation not found"); continue; }
                    if (log.RisingEdges != 1 || log.Dashes != 1 || log.Bursts != 1) problems.Add($"{name}: edges {log.RisingEdges} dashes {log.Dashes} bursts {log.Bursts}");
                    if (log.Ghosts < 3 || log.Ghosts > DashTrailVfx.MaxGhostsPerDash) problems.Add($"{name}: {log.Ghosts} afterimages");
                    if (log.AnimDashFrames == 0) problems.Add($"{name}: no dash animation");
                    if (log.MaxOffPath > 0.2f) problems.Add($"{name}: an afterimage {log.MaxOffPath:0.00} tiles off the drawn path");
                    if (Vector2.Angle(new Vector2(log.DirX, log.DirY), direction) > 5f) problems.Add($"{name}: presented direction ({log.DirX:0.00},{log.DirY:0.00})");
                    if (log.PresentedSeconds < 0.1f || log.PresentedSeconds > 0.45f) problems.Add($"{name}: presented for {log.PresentedSeconds:0.000} s");
                    if (log.LiveAfter != 0) problems.Add($"{name}: {log.LiveAfter} dash effects still live");
                    var pureReplica = peer != dasher && peer != NetworkManager.ServerClientId;
                    if (pureReplica)
                    {
                        replicaWindows++;
                        if (log.SimulatedSeen) { replicaSimulated++; problems.Add($"{name}: the replica simulated the dash"); }
                        if (log.StartedDelta != 0) problems.Add($"{name}: the replica started {log.StartedDelta} dashes");
                        var lag = log.StartServerTime - dasherStart;
                        if (dasherStart < 0 || lag < -0.02 || lag > 0.3) problems.Add($"{name}: replica starts {lag:0.000} s after the dasher");
                    }
                    else if (log.StartedDelta != 1) problems.Add($"{name}: {log.StartedDelta} simulated dashes (expected exactly one)");
                }

                // The authoritative dash is the approved one: the host moved the dasher ~3.06 tiles (unobstructed lanes only).
                if (!combat && (hostLog.Travel < 2.8f || hostLog.Travel > 3.3f)) problems.Add($"host travel {hostLog.Travel:0.00} tiles");
                Record(label, problems.Count == 0,
                    string.Join(" | ", logs.Select(kv => kv.Value == null ? $"{kv.Key}: -" :
                        $"{(kv.Key == NetworkManager.ServerClientId ? "host" : "c" + kv.Key)}: dash {kv.Value.Dashes} burst {kv.Value.Bursts} ghosts {kv.Value.Ghosts} anim {kv.Value.AnimDashFrames} sim {kv.Value.SimulatedSeen} started {kv.Value.StartedDelta} t0 {kv.Value.StartServerTime:0.000} for {kv.Value.PresentedSeconds:0.000}s off {kv.Value.MaxOffPath:0.00} live {kv.Value.LiveAfter} travel {kv.Value.Travel:0.00} {kv.Value.Capture}"))
                    + $"; problems: {(problems.Count == 0 ? "none" : string.Join(" | ", problems))}");
                yield return Seconds(0.2f);
            }

            // Repeated dashes with direction changes, every member in turn.
            foreach (var dasher in members)
            {
                yield return Stage(dasher);
                for (var i = 0; i < DashFeelDirections.Length; i++)
                    yield return Window(dasher, DashFeelDirections[i], i + 1, i == 0 ? $"dasher{dasher}_lane" : "", false);
            }

            // Active combat: two live attackers on the spot, each member dashes once through the fight.
            var content = _app.Content;
            var definition = content.Enemies.First(e => e.AttackKind == EnemyAttackKind.Moveset);
            var attackers = new List<GameObject>();
            foreach (var at in new[] { centre.Value + new Vector2(1.5f, 2.6f), centre.Value + new Vector2(-1.5f, -2.6f) })
            {
                var enemy = new DefaultEnemySpawner(content.Stagger).Spawn(definition, at, HostPlayer.transform);
                room.BindEncounterBounds(enemy.gameObject);
                _run.BindEnemyPresentation(enemy);
                attackers.Add(enemy.gameObject);
            }

            _dashCombat = true;
            foreach (var dasher in members)
            {
                yield return Stage(dasher);
                yield return Window(dasher, new Vector2(1f, -1f).normalized, 7, $"dasher{dasher}_fight", true);
            }

            _dashCombat = false;
            Record("in the fight the attackers were live on the dash spot", attackers.Count(a => a != null && a.GetComponent<HealthComponent>() is { IsAlive: true }) > 0, $"{attackers.Count(a => a != null)} attackers");
            foreach (var a in attackers) a?.GetComponent<HealthComponent>()?.TryApplyDamage(new DamageRequest(1000000));
            Record("pure replicas mirror remote dashes without simulating them (before the fix their presentation read the never-set simulated flag)",
                replicaWindows > 0 && replicaSimulated == 0, $"replica windows {replicaWindows}, simulated {replicaSimulated}");
            yield return Seconds(1f);

            // ---- end as every scenario does: the boss falls, the party returns ----
            yield return BossSteps(Clients, true);
            yield return Command(new ProofMessage { Step = "vote", Arg = "return" }, clients, 20f);
            yield return WaitFor(() => _summary != null, 20f);
            foreach (var member in Clients) SendProof(member, new ProofMessage { Step = "finish" });
            yield return HostEnd(Clients);
        }
    }
}

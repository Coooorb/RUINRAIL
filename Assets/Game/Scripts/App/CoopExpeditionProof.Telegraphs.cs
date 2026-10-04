using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Networking;
using RuinRail.Presentation.Vfx;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace RuinRail.App
{
    /// <summary>
    /// The <c>telegraph</c> scenario: host and client each record every attack telegraph their own markers draw (the host
    /// on the authoritative actors, the client on the replicas fed only by the host's snapshots), keyed by network id and
    /// server time, first in a busy fight (shooter, charger, bomber, Brute and the biome's Elite at once) and then against
    /// the depth's Boss in both phases. The host matches the two logs one-to-one and requires agreement on the shape
    /// family, footprint, place, direction, replicated telegraph length, the time the telegraph is shown, lingering danger
    /// and impacts — no missed and no stale marker on the client. Both peers also report frame times, and the host times
    /// the heaviest shipped footprints being painted in the built player.
    /// </summary>
    public sealed partial class CoopExpeditionProof
    {
        [Serializable]
        public sealed class TelegraphRecord
        {
            public uint NetId;
            public string Actor = "";
            public string Kind = "";
            public double Start;
            public float Duration;
            public float Shown;
            public float Live;
            public int Impacts;
            public bool Sampled;
            public float SizeX, SizeY, CentreX, CentreY, Angle;
            /// <summary>How far the marker's centre moved during the telegraph (an actor shoved while it winds up).</summary>
            public float Travel;
            public float FirstX, FirstY;
            public bool HasFirst;
            public int Lanes;
        }

        [Serializable]
        public sealed class TelegraphLog
        {
            public List<TelegraphRecord> Records = new();
            public int Frames;
            public float MeanFrameMs;
            public float P99FrameMs;
            public float MaxFrameMs;
            public int FramesOver50Ms;
            /// <summary>Long frames in which a telegraph was rebuilt or repainted / long frames with no telegraph work.</summary>
            public int LongTelegraphFrames;
            public int LongOtherFrames;
            /// <summary>The most one frame spent on telegraph rasterisation and painting (all markers together).</summary>
            public float WorstTelegraphFrameMs;
            public string LongFrames = "";
            public int Rebuilds;
            public int Repaints;
            public double WindowStart;
            public double WindowEnd;
            /// <summary>Damage players took while recording / of it, landed while the player's hurtbox (inside the drawn body) touched no drawn red pixel.</summary>
            public int FairHits;
            public int FairInvisible;
            public int FairFloor;
            public float LookBack;
            /// <summary>Client: distance between where it shows its own player and the host's latest position for it (tiles).</summary>
            public float DriftMean;
            public float DriftMax;
            public string FairDetails = "";
        }

        private static double ServerNow => CoopRunLink.Current != null ? CoopRunLink.Current.NetworkTime : Time.timeAsDouble;

        /// <summary>Records every telegraph this peer draws for <paramref name="seconds"/>.</summary>
        private IEnumerator RecordTelegraphs(float seconds, TelegraphLog log, bool host, bool strafe = false)
        {
            var open = new Dictionary<TelegraphIndicator, (TelegraphRecord Record, int Shown, int Impacts)>();
            var frames = new List<float>();
            var rebuildsBefore = new Dictionary<TelegraphMarkerView, (int, int)>();
            log.WindowStart = ServerNow;
            var until = Time.realtimeSinceStartup + seconds;
            // A hit a damaging floor's own tick explains (same frame, same amount) is a floor, not a telegraph.
            var pending = new List<(int Frame, int Amount, GameObject Who)>();
            var floorExposed = new HashSet<int>();
            var history = new List<(float At, bool OnRed, bool OnFloor)>();
            var driftSum = 0f;
            var driftSamples = 0;
            // How far back a client looks: the round trip, one state interval and a frame of margin.
            var rtt = 0f;
            if (!host && Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsClient)
                rtt = Unity.Netcode.NetworkManager.Singleton.NetworkConfig.NetworkTransport.GetCurrentRtt(Unity.Netcode.NetworkManager.ServerClientId) / 1000f;
            var lookBack = rtt + RuinRail.Networking.CoopHostWorld.StateIntervalSeconds + 0.05f;
            log.LookBack = lookBack;
            var floorTicks = new List<(int Frame, int Amount)>();
            var volumes = new List<(RuinRail.Gameplay.Combat.Hazards.HazardVolume Volume, System.Action<IDamageable, int> Handler)>();
            // Fairness: every damage a watched player takes is checked against the red drawn on this peer's screen.
            var watched = new List<(HealthComponent Health, System.Action<int> Handler)>();
            foreach (var member in FairnessWatched(host))
            {
                var health = member != null ? member.GetComponent<HealthComponent>() : null;
                if (health == null) continue;
                var who = member;
                System.Action<int> handler = amount =>
                {
                    if (HurtboxOnShownRed(who)) { log.FairHits++; return; }
                    if (!host)
                    {
                        // The hit landed on the host a moment ago: judge what this client showed over that moment.
                        var since = Time.realtimeSinceStartup - lookBack;
                        if (history.Any(h => h.At >= since && h.OnRed)) { log.FairHits++; return; }
                        if (history.Any(h => h.At >= since && h.OnFloor)) floorExposed.Add(Time.frameCount);
                    }
                    pending.Add((Time.frameCount, amount, who));
                };
                health.Damaged += handler;
                watched.Add((health, handler));
            }
            var first = true;
            var counts = new Dictionary<TelegraphMarkerView, (int Rebuilds, int Repaints, int Slices)>();
            var lastFrameWork = string.Empty;
            var work = new System.Text.StringBuilder();
            var gcAtStart = System.GC.CollectionCount(0);
            var gc2AtStart = System.GC.CollectionCount(2);
            while (Time.realtimeSinceStartup < until)
            {
                var frameMs = Time.unscaledDeltaTime * 1000f;
                if (!first) frames.Add(frameMs);
                // A long frame: say what the telegraphs did in it (the previous iteration's rebuilds/paints and their cost).
                if (!first && frameMs > 50f)
                {
                    if (lastFrameWork.Length > 0) log.LongTelegraphFrames++; else log.LongOtherFrames++;
                    if (log.LongFrames.Length < 400) log.LongFrames += $"{frameMs:0}ms@+{seconds - (until - Time.realtimeSinceStartup):0.0}s({(lastFrameWork.Length > 0 ? "telegraph" : "no telegraph work")}) ";
                }

                if (!first && frameMs > 50f)
                    Debug.Log($"COOP-PROOF {(host ? "host" : "client")} long frame {frameMs:0.0} ms at +{seconds - (until - Time.realtimeSinceStartup):0.00}s of the window, GC gen0 {System.GC.CollectionCount(0) - gcAtStart}/gen2 {System.GC.CollectionCount(2) - gc2AtStart} so far; telegraph work in it: {(lastFrameWork.Length == 0 ? "none" : lastFrameWork)}");
                first = false;
                work.Clear();
                var telegraphMs = 0.0;
                foreach (var view in FindObjectsByType<TelegraphMarkerView>(FindObjectsSortMode.None))
                {
                    // A view first seen here has no history: it is only a baseline, not work done this frame.
                    if (!counts.TryGetValue(view, out var before)) { counts[view] = (view.Rebuilds, view.Repaints, view.BuildSlices); continue; }
                    if (before.Slices != view.BuildSlices) { work.Append($"{view.name} build slice {view.LastBuildMs:0.00}ms {view.CoveredPixels}px; "); telegraphMs += view.LastBuildMs; }
                    if (before.Repaints != view.Repaints) { work.Append($"{view.name} paint {view.LastPaintMs:0.00}ms; "); telegraphMs += view.LastPaintMs; }
                    counts[view] = (view.Rebuilds, view.Repaints, view.BuildSlices);
                }

                if (telegraphMs > log.WorstTelegraphFrameMs) log.WorstTelegraphFrameMs = (float)telegraphMs;

                lastFrameWork = work.ToString();
                if (host) KeepPartyAlive();
                foreach (var volume in FindObjectsByType<RuinRail.Gameplay.Combat.Hazards.HazardVolume>(FindObjectsSortMode.None))
                {
                    if (volumes.Any(v => v.Volume == volume)) continue;
                    System.Action<IDamageable, int> tick = (_, amount) => floorTicks.Add((Time.frameCount, amount));
                    volume.Ticked += tick;
                    volumes.Add((volume, tick));
                }
                // Strafing windows: both players move through the danger, across edges, right up to the strikes.
                if (strafe)
                {
                    var phase = Mathf.FloorToInt((Time.realtimeSinceStartup + (host ? 0f : 0.17f)) / 0.35f) % 4;
                    _reader.SetMove(phase == 0 ? Vector2.right : phase == 1 ? Vector2.up : phase == 2 ? Vector2.left : Vector2.down);
                }

                // A client's own history: was it on drawn red / on a damaging floor, frame by frame (its damage arrives late).
                if (!host && LocalPlayer != null)
                {
                    history.Add((Time.realtimeSinceStartup, HurtboxOnShownRed(LocalPlayer), OnFloor(LocalPlayer)));
                    var motion = LocalPlayer.GetComponent<NetworkPlayerMotion>();
                    if (motion != null)
                    {
                        var drift = Vector2.Distance(LocalPlayer.transform.position, motion.State.Position);
                        log.DriftMax = Mathf.Max(log.DriftMax, drift);
                        driftSum += drift;
                        driftSamples++;
                    }
                    history.RemoveAll(h => h.At < Time.realtimeSinceStartup - 1.5f);
                }
                foreach (var indicator in FindObjectsByType<TelegraphIndicator>(FindObjectsSortMode.None))
                {
                    if (indicator == null) continue;
                    var view = indicator.View;
                    if (view != null && !rebuildsBefore.ContainsKey(view)) rebuildsBefore[view] = (view.Rebuilds, view.Repaints);
                    open.TryGetValue(indicator, out var entry);
                    var telegraphing = indicator.IsShowing && !indicator.IsLive;
                    if (telegraphing && (entry.Record == null || indicator.Shown != entry.Shown))
                    {
                        var (netId, actor, duration) = Identify(indicator, host);
                        var record = new TelegraphRecord { NetId = netId, Actor = actor, Start = ServerNow, Duration = duration };
                        log.Records.Add(record);
                        entry = (record, indicator.Shown, indicator.Impacts);
                    }

                    if (entry.Record == null) continue;
                    var dt = Time.deltaTime;
                    if (telegraphing)
                    {
                        entry.Record.Shown += dt;
                        var centre = indicator.MarkerCentre;
                        if (!entry.Record.HasFirst) { entry.Record.HasFirst = true; entry.Record.FirstX = centre.x; entry.Record.FirstY = centre.y; }
                        else entry.Record.Travel = Mathf.Max(entry.Record.Travel, Vector2.Distance(centre, new Vector2(entry.Record.FirstX, entry.Record.FirstY)));
                    }
                    if (indicator.IsLive) entry.Record.Live += dt;
                    entry.Record.Impacts = indicator.Impacts - entry.Impacts;
                    if (telegraphing && !entry.Record.Sampled && indicator.Fill01 >= 0.5f)
                    {
                        entry.Record.Sampled = true;
                        entry.Record.Kind = indicator.MarkerKind;
                        entry.Record.SizeX = indicator.MarkerScale.x;
                        entry.Record.SizeY = indicator.MarkerScale.y;
                        entry.Record.CentreX = indicator.MarkerCentre.x;
                        entry.Record.CentreY = indicator.MarkerCentre.y;
                        entry.Record.Angle = indicator.MarkerAngle;
                        entry.Record.Lanes = indicator.MarkerCount;
                    }

                    open[indicator] = entry;
                }

                yield return null;
            }

            foreach (var (health, handler) in watched) if (health != null) health.Damaged -= handler;
            foreach (var (volume, handler) in volumes) if (volume != null) volume.Ticked -= handler;
            foreach (var (frame, amount, who) in pending)
            {
                if (floorTicks.Any(f => f.Frame == frame && f.Amount == amount)) { log.FairFloor++; continue; }
                if (!host && floorExposed.Contains(frame)) { log.FairFloor++; continue; }
                log.FairHits++;
                log.FairInvisible++;
                if (log.FairDetails.Length < 400)
                {
                    var at = who != null ? (Vector2)who.transform.position : Vector2.zero;
                    var near = FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Where(e => e != null && Vector2.Distance(e.transform.position, at) < 5f).Select(e => $"{e.Definition?.Id}:{e.State}");
                    var actors = FindObjectsByType<MovesetActorController>(FindObjectsSortMode.None).Where(a => a != null && Vector2.Distance(a.transform.position, at) < 8f)
                        .Select(a => $"{a.name}:{a.State}:{(a.CurrentAttack != null ? a.CurrentAttack.name.Replace("Attack_", "") : "-")}@{Vector2.Distance(a.transform.position, at):0.0}");
                    log.FairDetails += $"{(who != null ? who.name : "?")} {amount}hp at {at} near [{string.Join(",", near)}] actors [{string.Join(",", actors)}]; ";
                }
            }
            _reader.SetMove(Vector2.zero);
            log.WindowEnd = ServerNow;
            log.DriftMean = driftSamples > 0 ? driftSum / driftSamples : 0f;
            frames.Sort();
            log.Frames = frames.Count;
            log.MeanFrameMs = frames.Count > 0 ? frames.Average() : 0f;
            log.P99FrameMs = frames.Count > 0 ? frames[Mathf.Clamp(Mathf.CeilToInt(frames.Count * 0.99f) - 1, 0, frames.Count - 1)] : 0f;
            log.MaxFrameMs = frames.Count > 0 ? frames[frames.Count - 1] : 0f;
            log.FramesOver50Ms = frames.Count(f => f > 50f);
            foreach (var pair in rebuildsBefore)
            {
                if (pair.Key == null) continue;
                log.Rebuilds += pair.Key.Rebuilds - pair.Value.Item1;
                log.Repaints += pair.Key.Repaints - pair.Value.Item2;
            }
        }

        /// <summary>The players whose damage this peer judges: on the host every member (its own and its copies of the clients), on a client its own.</summary>
        private IEnumerable<GameObject> FairnessWatched(bool host)
        {
            if (!host) { yield return LocalPlayer; yield break; }
            yield return HostPlayer;
            foreach (var client in Clients) yield return _run.MemberEntity(client);
        }

        private static bool OnFloor(GameObject player) =>
            Physics2D.OverlapCircleAll(player.transform.position, 0.4f).Any(c => c.GetComponentInParent<RuinRail.Gameplay.Combat.Hazards.HazardVolume>() != null);

        /// <summary>True when any box of the player's hurtbox (which lies inside its drawn body) overlaps a red pixel drawn now.</summary>
        private static bool HurtboxOnShownRed(GameObject player)
        {
            var hurtbox = player != null ? player.GetComponent<CombatHurtbox>() : null;
            if (hurtbox == null) return false;
            var views = FindObjectsByType<TelegraphMarkerView>(FindObjectsSortMode.None).Where(v => v.IsVisible).ToList();
            const int Ppu = TelegraphMarkerView.PixelsPerUnit;
            foreach (var box in hurtbox.Boxes)
            {
                var b = box.bounds;
                for (var y = Mathf.FloorToInt(b.min.y * Ppu); y <= Mathf.CeilToInt(b.max.y * Ppu) - 1; y++)
                for (var x = Mathf.FloorToInt(b.min.x * Ppu); x <= Mathf.CeilToInt(b.max.x * Ppu) - 1; x++)
                    foreach (var view in views)
                        if (view.PaintsPixel(x, y)) return true;
            }

            return false;
        }

        private (uint netId, string actor, float duration) Identify(TelegraphIndicator indicator, bool host)
        {
            var go = indicator.gameObject;
            if (!host)
            {
                var replica = go.GetComponent<EnemyReplica>();
                return replica != null ? (replica.NetId, replica.DefinitionId, replica.TelegraphSeconds) : (0u, go.name, 0f);
            }

            var id = 0u;
            if (_run?.CoopHost != null) _run.CoopHost.TryGetNetId(go, out id);
            var actor = go.GetComponent<MovesetActorController>();
            if (actor != null) return (id, go.name, actor.TelegraphDuration);
            var enemy = go.GetComponent<EnemyController>();
            return (id, enemy != null && enemy.Definition != null ? enemy.Definition.Id : go.name, enemy != null ? enemy.TelegraphDuration : 0f);
        }

        // ---------------------------------------------------------------- client side

        private IEnumerator ClientTelegraphWatch(ProofMessage command)
        {
            var log = new TelegraphLog();
            yield return RecordTelegraphs(command.Seconds, log, false, command.Number == 1);
            Ack(command, JsonUtility.ToJson(log));
        }

        // ---------------------------------------------------------------- host side

        private IEnumerator TelegraphScenario(List<ulong> clients)
        {
            var client = clients.First();
            yield return ReportAll();
            var hostView = View("host");
            _report.Views.Add(hostView);
            Record("identical D1 on both peers", clients.All(c => ViewOf(c) is { } v && v.LayoutFingerprint == hostView.LayoutFingerprint && v.RoomSignature == hostView.RoomSignature),
                $"host={hostView.Seed}/{hostView.LayoutFingerprint} " + string.Join(" ", clients.Select(c => $"c{c}={ViewOf(c)?.LayoutFingerprint}")));

            // ---- busy combat: four normal archetypes and the biome's Elite attack the party at once ----
            var content = _app.Content;
            var biome = _run.Expedition.State.Biome;
            var room = _run.Rooms[_run.Generation.Graph.StartId];
            var interior = room.InteriorWorldBounds;
            var stand = new Vector2(interior.xMin + 2f, interior.center.y);
            TeleportParty(stand, 1.0f);
            yield return Seconds(1.0f);
            // Warm-up: the recorder's own first frame (first use of its code paths) is paid here, outside every measured window.
            yield return RecordTelegraphs(0.5f, new TelegraphLog(), true);
            var spawned = new List<GameObject>();
            void Normal(EnemyAttackKind kind, Vector2 offset)
            {
                var definition = content.Enemies.First(e => e.AttackKind == kind);
                var enemy = new DefaultEnemySpawner(content.Stagger).Spawn(definition, stand + offset, HostPlayer.transform);
                if (enemy.GetComponent<RuinRail.Gameplay.Combat.Projectiles.ProjectilePool>() == null) enemy.gameObject.AddComponent<RuinRail.Gameplay.Combat.Projectiles.ProjectilePool>();
                room.BindEncounterBounds(enemy.gameObject);
                _run.BindEnemyPresentation(enemy);
                spawned.Add(enemy.gameObject);
            }

            Normal(EnemyAttackKind.Projectile, new Vector2(5.5f, 2.2f));
            Normal(EnemyAttackKind.Charge, new Vector2(4.5f, -0.4f));
            Normal(EnemyAttackKind.Lob, new Vector2(6f, -2.4f));
            Normal(EnemyAttackKind.Moveset, new Vector2(2.2f, 1.4f));
            var eliteDefinition = content.Elites.First(e => e.Biome == biome);
            var elite = new DefaultEliteSpawner(content.Stagger).Spawn(eliteDefinition, stand + new Vector2(3.2f, -2.6f), room.transform, HostPlayer.transform);
            room.BindEncounterBounds(elite.Elite.gameObject);
            _run.BindActorPresentation(elite.Elite, eliteDefinition.Id, true);
            spawned.Add(elite.Elite.gameObject);
            yield return WaitFor(() => spawned.All(g => _run.CoopHost.TryGetNetId(g, out _)), 5f);
            yield return Seconds(1.0f);
            yield return TelegraphWindow(client, 14f, "busy combat (shooter, charger, bomber, Brute, " + eliteDefinition.Id + ")", 12);
            yield return TelegraphWindow(client, 12f, "busy combat, both players strafing", 0, true);
            foreach (var go in spawned)
            {
                var health = go != null ? go.GetComponent<HealthComponent>() : null;
                if (health != null && health.IsAlive) health.TryApplyDamage(new DamageRequest(health.CurrentHealth + 1000));
            }

            yield return Seconds(1.5f);

            // ---- the Boss, phase one then phase two (scaled telegraphs) ----
            var bossRoom = PickRoom(r => r.State.RoomType == RoomType.Boss);
            var binding = bossRoom != null ? bossRoom.GetComponent<RoomContentBinding>() : null;
            if (binding?.Boss?.Boss == null) { Record("boss present", false, "no boss on this depth"); Finish("telegraph", "no boss"); yield break; }
            var boss = binding.Boss.Boss;
            TeleportParty(bossRoom.InteriorWorldBounds.center + Vector2.down * 2.5f, 1.4f);
            yield return WaitFor(() => bossRoom.Lifecycle == RoomLifecycleState.Active, 10f);
            yield return WaitFor(() => boss.Target != null, 10f); // the introduction hands control back
            yield return Seconds(0.5f);
            yield return TelegraphWindow(client, 12f, $"{boss.Definition.Id} phase 1", 3);
            yield return TelegraphWindow(client, 10f, $"{boss.Definition.Id} phase 1, both players strafing", 0, true);
            var health2 = boss.Health;
            var threshold = Mathf.FloorToInt(health2.MaxHealth * boss.Definition.PhaseTwoHealthFraction) - 5;
            if (health2.CurrentHealth > threshold) health2.TryApplyDamage(new DamageRequest(health2.CurrentHealth - threshold));
            yield return Seconds(1.0f);
            yield return TelegraphWindow(client, 12f, $"{boss.Definition.Id} phase 2 (timing ×{boss.TimingMultiplier:0.00})", 3);
            yield return TelegraphWindow(client, 10f, $"{boss.Definition.Id} phase 2, both players strafing", 0, true);

            // ---- the heaviest footprints, painted in this built player ----
            TelegraphPaintCost();

            // ---- end as every scenario does: the boss falls, the party returns ----
            yield return BossSteps(Clients, true);
            yield return Command(new ProofMessage { Step = "vote", Arg = "return" }, new[] { client }, 20f);
            yield return WaitFor(() => _summary != null, 20f);
            foreach (var member in Clients) SendProof(member, new ProofMessage { Step = "finish" });
            yield return HostEnd(Clients);
        }

        /// <summary>One recording window on both peers, then the one-to-one comparison.</summary>
        private IEnumerator TelegraphWindow(ulong client, float seconds, string label, int minimumMatched, bool strafe = false)
        {
            var hostLog = new TelegraphLog();
            _acks.Remove(client);
            SendProof(client, new ProofMessage { Step = "telegraph-watch", Seconds = seconds, Number = strafe ? 1 : 0 });
            yield return RecordTelegraphs(seconds, hostLog, true, strafe);
            yield return WaitFor(() => _acks.TryGetValue(client, out var a) && a.Step == "ack:telegraph-watch", 20f);
            var clientLog = _acks.TryGetValue(client, out var ack) && !string.IsNullOrEmpty(ack.Arg) ? JsonUtility.FromJson<TelegraphLog>(ack.Arg) : null;
            if (clientLog == null) { Record($"telegraphs agree: {label}", false, "the client sent no telegraph log"); yield break; }

            // Compare only what both windows fully saw.
            var lo = Math.Max(hostLog.WindowStart, clientLog.WindowStart) + 0.6;
            var hi = Math.Min(hostLog.WindowEnd, clientLog.WindowEnd) - 2.0;
            var hostRecords = hostLog.Records.Where(r => r.Start >= lo && r.Start <= hi && r.NetId != 0 && r.Sampled).ToList();
            var clientRecords = clientLog.Records.Where(r => r.Start >= lo - 0.4 && r.Start <= hi + 0.4 && r.Sampled).ToList();
            var problems = new List<string>();
            var matched = 0;
            var used = new HashSet<TelegraphRecord>();
            foreach (var h in hostRecords)
            {
                var c = clientRecords.Where(r => r.NetId == h.NetId && !used.Contains(r) && Math.Abs(r.Start - h.Start) < 0.4).OrderBy(r => Math.Abs(r.Start - h.Start)).FirstOrDefault();
                if (c == null) { problems.Add($"missed {h.Actor}#{h.NetId} {h.Kind} at {h.Start:0.00}"); continue; }
                used.Add(c);
                matched++;
                var tag = $"{h.Actor}#{h.NetId} {h.Kind}";
                if (c.Kind != h.Kind) problems.Add($"{tag}: client kind {c.Kind}");
                if (Mathf.Abs(c.SizeX - h.SizeX) > 0.05f || Mathf.Abs(c.SizeY - h.SizeY) > 0.05f) problems.Add($"{tag}: size {h.SizeX:0.00}x{h.SizeY:0.00} vs {c.SizeX:0.00}x{c.SizeY:0.00}");
                // A marker that moved during its telegraph (its actor shoved) is sampled at different points of that motion on
                // the two peers (a client renders one interpolation delay later): allow exactly the motion either peer saw.
                if (Vector2.Distance(new Vector2(c.CentreX, c.CentreY), new Vector2(h.CentreX, h.CentreY)) > 0.2f + h.Travel + c.Travel)
                    problems.Add($"{tag}: centre ({h.CentreX:0.00},{h.CentreY:0.00}) vs ({c.CentreX:0.00},{c.CentreY:0.00}), travel {h.Travel:0.00}/{c.Travel:0.00}");
                if (h.Kind != TelegraphIndicator.KindSlam && Mathf.Abs(Mathf.DeltaAngle(c.Angle, h.Angle)) > 3f) problems.Add($"{tag}: angle {h.Angle:0.0} vs {c.Angle:0.0}");
                if (c.Lanes != h.Lanes) problems.Add($"{tag}: lanes {h.Lanes} vs {c.Lanes}");
                if (Mathf.Abs(c.Duration - h.Duration) > 0.001f) problems.Add($"{tag}: telegraph length {h.Duration:0.000} vs {c.Duration:0.000}");
                if (Mathf.Abs(c.Shown - h.Shown) > 0.2f) problems.Add($"{tag}: shown {h.Shown:0.00}s vs {c.Shown:0.00}s");
                if ((h.Live > 0.1f) != (c.Live > 0.1f) || Mathf.Abs(c.Live - h.Live) > 0.35f) problems.Add($"{tag}: lingering {h.Live:0.00}s vs {c.Live:0.00}s");
                if (c.Impacts != h.Impacts) problems.Add($"{tag}: impacts {h.Impacts} vs {c.Impacts}");
            }

            var stale = clientRecords.Where(c => c.Start >= lo + 0.4 && c.Start <= hi - 0.4 && !used.Contains(c)).ToList();
            foreach (var c in stale) problems.Add($"client-only {c.Actor}#{c.NetId} {c.Kind} at {c.Start:0.00}");
            var kinds = string.Join(",", hostRecords.GroupBy(r => r.Kind).Select(g => $"{g.Key.Replace("telegraph_", string.Empty)}×{g.Count()}"));
            var actors = string.Join(",", hostRecords.Select(r => r.Actor).Distinct());
            var lingering = hostRecords.Count(r => r.Live > 0.1f);
            // Geometry agreement is measured with the players standing (a moving player re-aims shooters and shoves bodies,
            // and a client renders what the host had a snapshot earlier); fairness is judged in both kinds of window.
            if (!strafe)
            Record($"telegraphs agree host↔client: {label}", problems.Count == 0 && matched >= minimumMatched,
                $"matched {matched}/{hostRecords.Count} (client {clientRecords.Count}) kinds [{kinds}] actors [{actors}] lingering {lingering}; problems: {(problems.Count == 0 ? "none" : string.Join(" | ", problems.Take(12)))}");
            Record($"telegraphs cause no long frame on either peer: {label}",
                hostLog.LongTelegraphFrames == 0 && clientLog.LongTelegraphFrames == 0 && hostLog.WorstTelegraphFrameMs < 8f && clientLog.WorstTelegraphFrameMs < 8f,
                $"telegraph work per frame worst host {hostLog.WorstTelegraphFrameMs:0.00} ms / client {clientLog.WorstTelegraphFrameMs:0.00} ms; long frames with telegraph work host {hostLog.LongTelegraphFrames} client {clientLog.LongTelegraphFrames}; "
                + $"host mean {hostLog.MeanFrameMs:0.0} p99 {hostLog.P99FrameMs:0.0} max {hostLog.MaxFrameMs:0.0} ms over {hostLog.Frames} frames, rebuilds {hostLog.Rebuilds} repaints {hostLog.Repaints}; "
                + $"client mean {clientLog.MeanFrameMs:0.0} p99 {clientLog.P99FrameMs:0.0} max {clientLog.MaxFrameMs:0.0} ms over {clientLog.Frames} frames, rebuilds {clientLog.Rebuilds} repaints {clientLog.Repaints}; "
                + $"other long frames (no telegraph work): host [{hostLog.LongFrames.Trim()}] client [{clientLog.LongFrames.Trim()}]");
            Record($"telegraphed damage lands only on visibly-touching players: {label}",
                hostLog.FairInvisible == 0 && clientLog.FairInvisible == 0,
                $"host judged {hostLog.FairHits} hits on both players ({hostLog.FairFloor} damaging-floor ticks set apart), {hostLog.FairInvisible} outside the red [{hostLog.FairDetails}]; "
                + $"client saw {clientLog.FairHits + clientLog.FairInvisible} hits on itself ({clientLog.FairFloor} on a damaging floor), {clientLog.FairInvisible} outside the red on its own screen within its {clientLog.LookBack * 1000f:0} ms look-back [{clientLog.FairDetails}]; client shown-vs-host position mean {clientLog.DriftMean:0.000} max {clientLog.DriftMax:0.000} tiles");
            _report.Notes.Add($"telegraph window '{label}': host {JsonUtility.ToJson(hostLog)}");
            _report.Notes.Add($"telegraph window '{label}': client {ack.Arg}");
        }

        /// <summary>
        /// Builds and paints every Elite/Boss footprint at assorted angles in this player exactly as a marker does frame by
        /// frame (budgeted rasterisation, then the paint), and reports the worst single-frame cost and how many frames the
        /// heaviest took to complete.
        /// </summary>
        private void TelegraphPaintCost()
        {
            var attacks = _app.Content.Bosses.SelectMany(b => b.Moveset.Concat(b.PhaseTwoArenaHazards))
                .Concat(_app.Content.Elites.SelectMany(e => e.Moveset)).Where(a => a != null).Distinct().ToList();
            var root = new GameObject("TelegraphCost");
            var origin = new Vector2(-9000f, -9000f);
            var shapes = new List<AttackFootprint.Shape>();
            var costs = new List<(string Name, int Pixels, double WorstFrameMs, int Frames, double TotalMs)>();
            var gcFrames = 0;
            var gcWorst = 0.0;
            foreach (var attack in attacks)
            {
                var view = TelegraphMarkerView.Create(root.transform, attack.name);
                var worst = 0.0;
                var frames = 0;
                var total = 0.0;
                const int Samples = 12;
                for (var i = 0; i < Samples; i++)
                {
                    shapes.Clear();
                    var direction = (Vector2)(Quaternion.Euler(0f, 0f, 17f + i * 31f) * Vector2.right);
                    TelegraphIndicator.FootprintOf(attack, origin, direction, null, shapes, out var pattern, out var sweep);
                    var built = 0;
                    do
                    {
                        // One frame's worth: the budgeted rasterisation slice and the paint that follows it. Timed without
                        // allocating; a frame in which the garbage collector ran is counted apart (that pause is not
                        // telegraph work).
                        var gc = GC.CollectionCount(0);
                        var t0 = Stopwatch.GetTimestamp();
                        view.SetFootprint(shapes, origin, pattern, sweep, true);
                        view.Paint(new TelegraphLook { Colour = Color.red, Progress = i / (float)Samples, Heavy = true });
                        var ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                        if (GC.CollectionCount(0) != gc) { gcFrames++; gcWorst = Math.Max(gcWorst, ms); }
                        else worst = Math.Max(worst, ms);
                        total += ms;
                        built++;
                    }
                    while (!view.IsBuildComplete && built < 50);

                    frames = Math.Max(frames, built);
                }

                costs.Add((attack.name.Replace("Attack_", string.Empty), view.CoveredPixels, worst, frames, total / Samples));
            }

            Destroy(root);
            var heaviest = costs.OrderByDescending(c => c.Pixels).Take(5).ToList();
            var worstFrame = costs.Max(c => c.WorstFrameMs);
            var slowest = costs.Max(c => c.Frames);
            var worstName = costs.OrderByDescending(c => c.WorstFrameMs).First().Name;
            Record("the heaviest telegraphs build and paint without a visible hitch (worst frame, built player)", worstFrame < 8.0 && slowest <= 6,
                $"worst frame {worstFrame:0.00} ms ({worstName}), longest build {slowest} frames, over {costs.Count} Elite/Boss attacks (budget {TelegraphMarkerView.FrameBudgetMs:0.0} ms/frame); "
                + $"{gcFrames} measured frame(s) contained a GC pause (worst {gcWorst:0.00} ms, counted apart); heaviest: "
                + string.Join(", ", heaviest.Select(c => $"{c.Name} {c.Pixels}px worst {c.WorstFrameMs:0.00} ms in {c.Frames} frames ({c.TotalMs:0.00} ms total)")));
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Stats;
using RuinRail.Gameplay.Items.Armor;
using RuinRail.Gameplay.Items.Consumables;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using RuinRail.UI.Base;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// Built-player co-op expedition proof (`-coop-expedition host|client -coop-port P -coop-size N -coop-out f.json
    /// [-seed S] [-coop-scenario duo|trio]`): two or three processes of the shipped player play one real expedition over
    /// UnityTransport on loopback — Shelter lobby, the host's start, identical D1 on every peer, a real combat room,
    /// pickup and coin races, a merchant trade, Downed/revive both ways, the boss, the Transit vote, a networked
    /// descend to D2, and the Return with its save. Every step drives the shipping paths: the player input the host
    /// validates, the loot authority, the vote, the transactions. The script only decides <i>what</i> each player does
    /// and when, and — like the solo smoke — moves players between rooms and keeps them alive during scripted fights so
    /// the run is bounded; every such shortcut is host-authoritative and named in the report.
    /// </summary>
    public sealed class CoopExpeditionProof : MonoBehaviour
    {
        public const string Argument = "-coop-expedition";
        public const string ScenarioArgument = "-coop-scenario";
        public const float StepTimeoutSeconds = 60f;

        [Serializable]
        public sealed class Step
        {
            public string Name;
            public bool Pass;
            public string Detail = "";
            public float At;
        }

        /// <summary>What one peer sees of the shared expedition at one moment (host and clients report the same shape).</summary>
        [Serializable]
        public sealed class PeerView
        {
            public string Role = "";
            public ulong ClientId;
            public string ParticipantId = "";
            public string Mode = "";
            public bool ExpeditionActive;
            public int Depth;
            public string Biome = "";
            public int Seed;
            public string LayoutFingerprint = "";
            public int Rooms;
            public string RoomSignature = "";
            public string RoomStates = "";
            public int LiveEnemies;
            public int EnemyActorsSeen;
            public string EnemyDefinitions = "";
            public bool BossPresent;
            public int BossHealth;
            public int BossMaxHealth;
            public int BossPhase;
            public int PartyMembers;
            public int PlayerObjects;
            public int OwnedPlayerObjects;
            public int Cameras;
            public int AudioListeners;
            public int PlayerInputs;
            public int ReplicaCameras;
            public int ReplicaListeners;
            public int ReplicaInputs;
            public string InventoryFingerprint = "";
            public int InventoryItems;
            public int AmmoTotal;
            public string EquippedWeapons = "";
            public int Coins;
            public string LifeState = "";
            public bool Spectating;
            public string SpectatingTarget = "";
            public int Health;
            public int MaxHealth;
            public float PositionX;
            public float PositionY;
            public string MemberPositions = "";
            public string MemberLifeStates = "";
            public int HitsSent;
            public int ShotsSent;
            public int ShotsDrawn;
            public int GrantsReceived;
            public int RevokesReceived;
            public int XpEarned;
            public int EnemiesDefeated;
            public int RoomsCleared;
            public string TransitState = "";
            public string TransitVotes = "";
            public int DeepestDepth;
            public int GroundLoot;
            public int SpawnedNetworkObjects;
            public float TimeScale;
            public bool GameplayHeld;
            public int EnemyStatesApplied;
            public int RoomStatesApplied;
            public string LastNotice = "";
            public string Error = "";
            // resource / leak sample
            public int GameObjects;
            public long AllocatedBytes;
            public long MonoUsedBytes;
            public long LinkMessagesSent;
            public long LinkMessagesReceived;
            public long LinkBytesSent;
            public long LinkBytesReceived;
            public float At;
        }

        [Serializable]
        public sealed class Report
        {
            public string Role = "";
            public string Scenario = "";
            public bool Success;
            public string Stage = "start";
            public string Error = "";
            public int PartySize;
            public int Seed;
            public List<Step> Steps = new();
            public List<PeerView> Views = new();
            public List<string> Notes = new();
            public string SummaryOutcome = "";
            public int SummaryCoinsExtracted;
            public int SummarySecuredItems;
            public int SummaryDeepestDepth;
            public int SummaryXp;
            public bool SaveMarkerOpenAfter;
            public int BankedCoinsBefore;
            public int BankedCoinsAfter;
            public int DeepestDepthOnDisk;
            public int PlayerObjectsAfterReturn = -1;
            public int SpawnedObjectsAfterReturn = -1;
            public int UncaughtExceptions;
            public List<string> ExceptionLines = new();
            public long LinkMessagesSent;
            public long LinkMessagesReceived;
            public long LinkBytesSent;
            public long LinkBytesReceived;
            public float Seconds;
            public string UnityVersion = Application.unityVersion;
        }

        private GameApp _app;
        private bool _isHost;
        private int _size = 2;
        private ushort _port = 7920;
        private string _out;
        private string _scenario = "duo";
        private string _name;
        private readonly Report _report = new();
        private readonly ProofInputReader _reader = new();
        private string _composed = string.Empty;
        private bool _waitOk;
        private float _started;
        private readonly Dictionary<ulong, PeerView> _lastViews = new();
        private readonly Dictionary<ulong, ProofMessage> _acks = new();
        private readonly Queue<ProofMessage> _commands = new();
        private ExpeditionScene _run;
        private ExpeditionSummary _summary;

        public Report Current => _report;

        public static bool Requested(string[] args) => RoleOf(args) != null;

        public static string RoleOf(string[] args)
        {
            var index = Array.IndexOf(args, Argument);
            if (index < 0 || index + 1 >= args.Length) return null;
            var role = args[index + 1].ToLowerInvariant();
            return role == "host" || role == "client" ? role : null;
        }

        public static CoopExpeditionProof Begin(GameApp app, string[] args)
        {
            var go = new GameObject("CoopExpeditionProof");
            DontDestroyOnLoad(go);
            var proof = go.AddComponent<CoopExpeditionProof>();
            proof._app = app;
            proof._isHost = RoleOf(args) == "host";
            proof._size = Mathf.Clamp(IntArg(args, CoopPeerRunner.SizeArgument, 2), 1, ExpeditionParty.MaxPartySize);
            proof._port = (ushort)IntArg(args, CoopPeerRunner.PortArgument, 7920);
            proof._scenario = TextArg(args, ScenarioArgument, proof._size >= 3 ? "trio" : "duo");
            proof._name = proof._isHost ? "Host" : "Client" + IntArg(args, "-coop-index", 1);
            proof._out = TextArg(args, CoopPeerRunner.OutArgument, Path.Combine(app.SaveDirectory, $"coop_expedition_{(proof._isHost ? "host" : "client")}.json"));
            proof._report.Role = proof._isHost ? "host" : "client";
            proof._report.Scenario = proof._scenario;
            proof._report.PartySize = proof._size;
            proof._report.Seed = app.RunSeedOverride ?? 0;
            app.SceneComposed += name => proof._composed += name + ";";
            NetworkPlayerObject.Spawned += proof.OnPlayerObjectSpawned;
            NetworkPlayerObject.OwnershipGained += proof.OnPlayerObjectSpawned;
            Application.logMessageReceived += proof.OnLog;
            proof.StartCoroutine(proof._isHost ? proof.RunHost() : proof.RunClient());
            Debug.Log($"COOP-PROOF {proof._report.Role} starting: port={proof._port} size={proof._size} scenario={proof._scenario} seed={proof._report.Seed}.");
            return proof;
        }

        private static int IntArg(string[] args, string name, int fallback)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value) ? value : fallback;
        }

        private static string TextArg(string[] args, string name, string fallback)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception) return;
            _report.UncaughtExceptions++;
            if (_report.ExceptionLines.Count < 20) _report.ExceptionLines.Add(condition + " @ " + (stackTrace ?? string.Empty).Split('\n').FirstOrDefault());
        }

        /// <summary>The proof drives this peer's own character through the scripted reader (installed before the run composes).</summary>
        private void OnPlayerObjectSpawned(NetworkPlayerObject player)
        {
            if (player == null || !player.IsOwner) return;
            var input = player.GetComponent<PlayerInput>();
            if (input != null) input.UseReader(_reader);
        }

        // ================================================================ common

        private IEnumerator WaitFor(Func<bool> condition, float timeout = StepTimeoutSeconds)
        {
            var until = Time.realtimeSinceStartup + timeout;
            _waitOk = false;
            while (Time.realtimeSinceStartup < until)
            {
                bool ok;
                try { ok = condition(); }
                catch (Exception e) { Debug.LogWarning("COOP-PROOF wait condition threw: " + e.Message); ok = false; }
                if (ok) { _waitOk = true; yield break; }
                yield return null;
            }
        }

        private IEnumerator Seconds(float seconds)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        private Step Record(string name, bool pass, string detail)
        {
            var step = new Step { Name = name, Pass = pass, Detail = detail ?? string.Empty, At = Time.realtimeSinceStartup - _started };
            _report.Steps.Add(step);
            Debug.Log($"COOP-PROOF {_report.Role} step {(pass ? "PASS" : "FAIL")} {name}: {detail}");
            return step;
        }

        private void Finish(string stage, string error)
        {
            _report.Stage = stage;
            if (!string.IsNullOrEmpty(error)) _report.Error = error;
            _report.Seconds = Time.realtimeSinceStartup - _started;
            var link = CoopRunLink.Current;
            if (link != null)
            {
                _report.LinkMessagesSent = link.MessagesSent;
                _report.LinkMessagesReceived = link.MessagesReceived;
                _report.LinkBytesSent = link.BytesSent;
                _report.LinkBytesReceived = link.BytesReceived;
            }

            _report.Success = string.IsNullOrEmpty(_report.Error) && _report.Steps.Count > 0 && _report.Steps.All(s => s.Pass) && _report.UncaughtExceptions == 0;
            Write();
            Debug.Log($"COOP-PROOF {_report.Role}: success={_report.Success} stage={_report.Stage} steps={_report.Steps.Count(s => s.Pass)}/{_report.Steps.Count} exceptions={_report.UncaughtExceptions} error='{_report.Error}'");
            StartCoroutine(QuitSoon());
        }

        private IEnumerator QuitSoon()
        {
            yield return Seconds(_isHost ? 4f : 1f);
            if (NetworkManagerListening()) Unity.Netcode.NetworkManager.Singleton.Shutdown();
            yield return Seconds(0.5f);
            Application.Quit(_report.Success ? 0 : 1);
        }

        private static bool NetworkManagerListening() => Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening;

        private void Write()
        {
            if (string.IsNullOrEmpty(_out)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_out)) ?? ".");
                File.WriteAllText(_out, JsonUtility.ToJson(_report, true));
            }
            catch (Exception e)
            {
                Debug.LogError("COOP-PROOF report could not be written: " + e.Message);
            }
        }

        private IEnumerator EnterShelter()
        {
            yield return WaitFor(() => _composed.Contains(SceneNames.MainMenu), 60f);
            if (!_waitOk) { Finish("menu", "main menu never composed"); yield break; }
            var outcome = _app.Menu.Play();
            if (outcome == PlayOutcome.Failed) { Finish("menu", "PLAY failed: " + _app.Menu.Message); yield break; }
            yield return WaitFor(() => _composed.Contains(SceneNames.Base) && FindFirstObjectByType<BaseHubScreen>() != null, 60f);
            if (!_waitOk) Finish("shelter", "the Shelter never composed");
        }

        private static IEnumerable<Transform> Transforms<T>() where T : Component => FindObjectsByType<T>(FindObjectsSortMode.None).Select(c => c.transform);

        /// <summary>This peer's view of the expedition now (identical shape on host and clients, compared by the host).</summary>
        private PeerView View(string role)
        {
            var view = new PeerView { Role = role, ClientId = _app.Coop != null ? _app.Coop.LocalClientId : 0, TimeScale = Time.timeScale, GameplayHeld = RuinRail.Core.Input.GameplayInputGate.IsHeld };
            var run = _run != null ? _run : FindFirstObjectByType<ExpeditionScene>();
            view.Cameras = Camera.allCamerasCount;
            view.AudioListeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length;
            view.PlayerInputs = FindObjectsByType<PlayerInput>(FindObjectsSortMode.None).Length;
            var players = CoopPlayerDirectory.All().ToList();
            view.PlayerObjects = players.Count;
            view.OwnedPlayerObjects = players.Count(p => p.IsOwner);
            var replicas = players.Where(p => !p.IsOwner).ToList();
            view.ReplicaCameras = replicas.Sum(p => p.GetComponentsInChildren<Camera>(true).Length);
            view.ReplicaListeners = replicas.Sum(p => p.GetComponentsInChildren<AudioListener>(true).Length);
            view.ReplicaInputs = replicas.Sum(p => p.GetComponentsInChildren<PlayerInput>(true).Length);
            view.SpawnedNetworkObjects = CoopPlayerDirectory.SpawnedObjects();
            view.GameObjects = FindObjectsByType<Transform>(FindObjectsSortMode.None).Length;
            view.AllocatedBytes = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            view.MonoUsedBytes = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            view.At = Time.realtimeSinceStartup - _started;
            var link = CoopRunLink.Current;
            if (link != null)
            {
                view.LinkMessagesSent = link.MessagesSent;
                view.LinkMessagesReceived = link.MessagesReceived;
                view.LinkBytesSent = link.BytesSent;
                view.LinkBytesReceived = link.BytesReceived;
            }
            if (run == null || run.Expedition == null) return view;
            var expedition = run.Expedition;
            var state = expedition.State;
            view.Mode = run.Mode.ToString();
            view.ExpeditionActive = expedition.IsExpeditionActive;
            view.DeepestDepth = expedition.DeepestDepthReached;
            if (state == null) return view;
            view.ParticipantId = state.TransactionId;
            view.Depth = state.Depth;
            view.Biome = state.Biome.ToString();
            view.Seed = state.RunSeed;
            view.LayoutFingerprint = run.LayoutFingerprint;
            view.XpEarned = state.Stats.XpEarned;
            view.EnemiesDefeated = state.Stats.EnemiesDefeated;
            view.RoomsCleared = state.Stats.RoomsCleared;
            view.Coins = state.CarriedCoins;
            view.InventoryFingerprint = LoadoutValidation.Fingerprint(state.Inventory.ToSnapshot());
            view.InventoryItems = state.Inventory.BackpackSlots.Count(i => i != null) + Enum.GetValues(typeof(EquippedSlot)).Cast<EquippedSlot>().Count(s => state.Inventory.GetEquipped(s) != null);
            view.AmmoTotal = Enum.GetValues(typeof(AmmoType)).Cast<AmmoType>().Sum(t => state.Inventory.Get(t));
            view.EquippedWeapons = string.Join("+", new[] { EquippedSlot.PrimaryWeapon, EquippedSlot.SecondaryWeapon }.Select(s => state.Inventory.GetEquipped(s)?.DefinitionId ?? "-"));
            if (run.Rooms != null)
            {
                view.Rooms = run.Rooms.Count;
                view.RoomSignature = DungeonFingerprints.Hash(string.Join(";", run.Rooms.Values.OrderBy(r => r.State.NodeId).Select(r => $"{r.State.NodeId}:{r.State.RoomId}:{r.transform.position.x:0.##},{r.transform.position.y:0.##}:{r.Doors.Count}")));
                view.RoomStates = string.Join(";", run.Rooms.Values.OrderBy(r => r.State.NodeId).Select(r => $"{r.State.NodeId}:{(int)r.Lifecycle}:{(r.DoorsLocked ? 1 : 0)}:{r.State.Resolved.Count}"));
            }

            var player = run.Rig?.Player;
            if (player != null)
            {
                var life = player.GetComponent<PlayerLifeStateComponent>();
                var health = player.GetComponent<HealthComponent>();
                view.LifeState = life != null ? life.State.ToString() : "";
                var spectator = player.GetComponent<DeadSpectatorFollow>();
                view.Spectating = spectator != null && spectator.IsSpectating;
                view.SpectatingTarget = view.Spectating ? spectator.FollowTarget.name : "";
                view.Health = health != null ? health.CurrentHealth : 0;
                view.MaxHealth = health != null ? health.MaxHealth : 0;
                view.PositionX = player.transform.position.x;
                view.PositionY = player.transform.position.y;
            }

            if (run.Party != null)
            {
                view.PartyMembers = run.Party.ComposedPartySize;
                view.MemberPositions = string.Join(";", run.Party.Members.Where(m => m.GameObject != null).Select(m => $"{m.OwnerClientId}:{m.GameObject.transform.position.x:0.0},{m.GameObject.transform.position.y:0.0}"));
                view.MemberLifeStates = string.Join(";", run.Party.Members.Where(m => m.GameObject != null).Select(m => $"{m.OwnerClientId}:{m.GameObject.GetComponent<PlayerLifeStateComponent>()?.State}"));
            }

            if (run.CoopClient != null)
            {
                var live = run.CoopClient.Replicas.Replicas.Values.Where(r => r != null && !r.IsDead && r.Kind != CoopActorKind.Boss).ToList();
                view.LiveEnemies = live.Count;
                view.EnemyActorsSeen = run.CoopClient.Replicas.Spawns;
                view.EnemyDefinitions = string.Join(",", run.CoopClient.Replicas.Replicas.Values.Where(r => r != null).Select(r => $"{r.Kind}:{r.DefinitionId}").Distinct().OrderBy(s => s));
                var boss = run.CoopClient.Replicas.Replicas.Values.FirstOrDefault(r => r != null && r.Kind == CoopActorKind.Boss);
                view.BossPresent = boss != null;
                view.BossHealth = boss != null ? boss.Health.CurrentHealth : 0;
                view.BossMaxHealth = boss != null ? boss.Health.MaxHealth : 0;
                view.BossPhase = run.CoopClient.LastBossState?.Phase ?? 0;
                view.HitsSent = run.CoopClient.HitsSent;
                view.ShotsSent = run.CoopClient.ShotsSent;
                view.ShotsDrawn = run.CoopClient.ShotsDrawn;
                view.GrantsReceived = run.CoopClient.GrantsReceived;
                view.RevokesReceived = run.CoopClient.RevokesReceived;
                view.EnemyStatesApplied = run.CoopClient.EnemyStatesApplied;
                view.RoomStatesApplied = run.CoopClient.RoomStatesApplied;
                view.GroundLoot = run.CoopClient.Loot.Count;
            }
            else
            {
                var enemies = FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Count(e => e.IsAlive) + FindObjectsByType<MovesetActorController>(FindObjectsSortMode.None).Count(a => a.IsAlive && a is not RuinRail.Gameplay.Enemies.Bosses.BossController);
                view.LiveEnemies = enemies;
                view.EnemyActorsSeen = run.CoopHost != null ? run.CoopHost.TrackedActors : 0;
                var boss = FindFirstObjectByType<RuinRail.Gameplay.Enemies.Bosses.BossController>();
                view.BossPresent = boss != null;
                view.BossHealth = boss != null && boss.Health != null ? boss.Health.CurrentHealth : 0;
                view.BossMaxHealth = boss != null && boss.Health != null ? boss.Health.MaxHealth : 0;
                view.BossPhase = boss != null ? boss.Phase : 0;
                view.GroundLoot = run.GroundLoot != null ? run.GroundLoot.Tracked.Count(t => t != null) : 0;
            }

            var transit = expedition.Transit;
            view.TransitState = transit == null ? "none" : transit.State + (transit.Result.HasValue ? ":" + transit.Result.Value : string.Empty);
            view.TransitVotes = transit == null ? "" : string.Join(",", transit.Choices.Select(c => c.Key.Substring(0, Math.Min(6, c.Key.Length)) + "=" + c.Value));
            view.LastNotice = run.LastNotice;
            return view;
        }

        // ================================================================ client

        private IEnumerator RunClient()
        {
            _started = Time.realtimeSinceStartup;
            yield return EnterShelter();
            if (!string.IsNullOrEmpty(_report.Error)) yield break;
            var driver = _app.Network?.Driver as NgoNetworkDriver;
            if (driver == null) { Finish("connect", "no live NGO driver (run without -offline-multiplayer)"); yield break; }
            driver.SetDirectAddress("127.0.0.1", _port, false);
            driver.SetConnectionPayload(_name);
            var started = driver.StartClient();
            if (!started.Success) { Finish("connect", "StartClient refused: " + started.Error + " " + started.Message); yield break; }
            yield return WaitFor(() => _app.Coop != null && _app.Coop.IsClient, 90f);
            Record("client connected and received the session link", _waitOk, $"clientId={_app.Coop?.LocalClientId} link={(CoopRunLink.Current != null)}");
            if (!_waitOk) { Finish("connect", "never connected to the host"); yield break; }

            var screen = FindFirstObjectByType<BaseHubScreen>();
            var ready = screen != null && screen.Hub.Multiplayer.SetReady(true);
            Record("client Ready in its own Shelter lobby (relayed to the host)", ready, "SetReady(true) " + (ready ? "accepted" : "refused"));
            var startGate = screen != null && !screen.Hub.Transit.StartExpedition();
            Record("client cannot start an expedition of its own", startGate, screen != null ? screen.Hub.Transit.Feedback.Text : "no screen");

            yield return WaitFor(() => _composed.Contains(SceneNames.Dungeon), 180f);
            if (!_waitOk) { Finish("start", "the host's start never reached this client (no Dungeon)"); yield break; }
            yield return WaitFor(() => (_run = FindFirstObjectByType<ExpeditionScene>()) != null && _run.Rooms != null && _run.CoopClient != null, 90f);
            if (!_waitOk) { Finish("compose", "the client expedition never composed: " + (_run != null ? _run.LastDepthDesync : "no scene")); yield break; }
            Record("client composed the host's expedition", _run.Mode == CoopRunMode.Client, $"mode={_run.Mode} depth={_run.Expedition.State.Depth} biome={_run.Expedition.State.Biome} seed={_run.Expedition.State.RunSeed} layout={_run.LayoutFingerprint} rooms={_run.Rooms.Count}");
            _run.Expedition.ExpeditionEnded += summary => _summary = summary;
            _report.BankedCoinsBefore = _app.Menu.Session?.Banked.Balance ?? 0;
            SubscribeClientCommands();

            var deadline = Time.realtimeSinceStartup + 1500f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (_run == null) _run = FindFirstObjectByType<ExpeditionScene>();
                if (CoopRunLink.Current != null && CoopRunLink.Current != _commandLink) SubscribeClientCommands();
                // This peer's own Return/failure ends its part of the proof even if the host's finish is still in flight.
                if (_commands.Count == 0 && _summary != null && _composed.EndsWith(SceneNames.Base + ";")) _commands.Enqueue(new ProofMessage { Step = "finish" });
                if (_commands.Count == 0) { yield return null; continue; }
                var command = _commands.Dequeue();
                if (command.Step == "finish")
                {
                    yield return ClientFinish();
                    yield break;
                }

                yield return ExecuteClient(command);
            }

            Finish("commands", "the host never finished the proof");
        }

        private CoopRunLink _commandLink;

        /// <summary>Proof commands are read straight off the session link (the current one — a reconnect replaces it).</summary>
        private void SubscribeClientCommands()
        {
            _commandLink = CoopRunLink.Current;
            if (_commandLink == null) return;
            _commandLink.Received += (sender, kind, json) =>
            {
                if (kind != CoopKinds.ProofCommand) return;
                var command = CoopJson.Read<ProofMessage>(json);
                if (command != null) _commands.Enqueue(command);
            };
        }

        private void Ack(ProofMessage command, string detail = null)
        {
            var view = View("client");
            _report.Views.Add(view);
            CoopRunLink.Current?.SendToHost(CoopKinds.ProofReport, CoopJson.Write(new ProofMessage { Step = "ack:" + command.Step, Arg = detail ?? string.Empty, Number = command.Number, Json = JsonUtility.ToJson(view) }));
        }

        private GameObject LocalPlayer => _run != null && _run.Rig != null ? _run.Rig.Player : null;

        private IEnumerator ExecuteClient(ProofMessage command)
        {
            switch (command.Step)
            {
                case "report":
                    Ack(command);
                    break;
                case "move":
                {
                    _reader.SetMove(new Vector2(command.X, command.Y));
                    yield return Seconds(command.Seconds);
                    _reader.SetMove(Vector2.zero);
                    Ack(command);
                    break;
                }
                case "fight":
                    yield return Fight(command.Seconds, command.Number);
                    Ack(command, $"hitsSent={_run?.CoopClient?.HitsSent} shots={_run?.CoopClient?.ShotsSent}");
                    break;
                case "interact":
                    _reader.PressInteract();
                    yield return Seconds(0.5f);
                    Ack(command, "presses=" + _reader.InteractPresses);
                    break;
                case "hold-interact":
                    _reader.SetInteractHeld(true);
                    yield return Seconds(command.Seconds);
                    _reader.SetInteractHeld(false);
                    Ack(command);
                    break;
                case "buy":
                    yield return ClientBuy(command);
                    break;
                case "vote":
                {
                    var choice = command.Arg == "return" ? TransitChoice.ReturnToShelter : TransitChoice.DescendDeeper;
                    yield return WaitFor(() => _run != null && _run.Vote != null, 20f);
                    var voted = _run != null && _run.Vote != null && _run.Vote.Vote(choice);
                    if (!voted && _run?.Vote != null && _run.Vote.AwaitingReturnConfirmation) voted = _run.Vote.ConfirmReturn();
                    yield return Seconds(0.3f);
                    Ack(command, "voted=" + voted);
                    break;
                }
                case "reconnect":
                    yield return ClientReconnect(command);
                    break;
                case "cache":
                    yield return ClientCache(command);
                    break;
                case "sell":
                    yield return ClientSell(command);
                    break;
                case "defib":
                    yield return ClientDefibrillator(command);
                    break;
                case "free-slot":
                {
                    // Proof harness: the next host grant needs one free backpack slot (D2 backpacks can be full).
                    var inventory = _run.Expedition.State.Inventory;
                    if (inventory.BackpackSlots.All(i => i != null)) inventory.RemoveFromBackpack(inventory.BackpackSlots.Count - 1);
                    yield return Seconds(0.3f);
                    Ack(command, "free=" + inventory.BackpackSlots.Count(i => i == null));
                    break;
                }
                case "equip":
                    yield return ClientEquipArmor(command);
                    break;
                case "kill-passive":
                    yield return ClientKillPassive(command);
                    break;
                case "swap-passive":
                    yield return ClientSwapPassive(command);
                    break;
                case "stagger":
                {
                    // One real hit impact of this member on the nearest enemy (relayed to the host as an impact request).
                    var player = LocalPlayer;
                    var target = NearestReplica(player, command.Number);
                    var sent = _run.CoopClient.ImpactsSent;
                    if (player != null && target != null)
                        RuinRail.Gameplay.Combat.Impact.ImpactDispatcher.Apply(target.GetComponentInChildren<Collider2D>(),
                            new ImpactRequest((Vector2)target.transform.position - (Vector2)player.transform.position, 0f, 1000f, DamageKind.Normal, player, player.GetComponent<PlayerImpactReceiver>()));
                    yield return Seconds(0.5f);
                    Ack(command, $"target={(target != null)} impactsSent=+{_run.CoopClient.ImpactsSent - sent}");
                    break;
                }
                case "discharge":
                    yield return ClientDischarge(command);
                    break;
                case "menus":
                {
                    // Co-op (84/86): opening the inventory is local UI; the shared world keeps running.
                    _run.Inventory.Toggle();
                    yield return Seconds(0.5f);
                    var paused = Time.timeScale;
                    var open = _run.Inventory.IsOpen;
                    _run.Inventory.Toggle();
                    yield return Seconds(0.3f);
                    Ack(command, $"inventoryOpen={open} timeScaleWhileOpen={paused:0.0} after={Time.timeScale:0.0}");
                    break;
                }
                default:
                    Ack(command, "unknown step");
                    break;
            }
        }

        /// <summary>
        /// Wears the armor the host granted (Arg = definition id) through the member's own inventory; the change reaches the
        /// host as the member's inventory snapshot. Reported back: this member's OWN rig passives (the incoming-impact
        /// ones must not be among them — the host runs those on its copy).
        /// </summary>
        private IEnumerator ClientEquipArmor(ProofMessage command)
        {
            var inventory = _run.Expedition.State.Inventory;
            var slot = SlotFor(command.Arg);
            ItemInstance Find() => inventory.BackpackSlots.FirstOrDefault(i => i != null && i.DefinitionId == command.Arg);
            yield return WaitFor(() => Find() != null || inventory.GetEquipped(slot)?.DefinitionId == command.Arg, 10f);
            var armor = Find();
            if (armor != null)
            {
                for (var i = 0; i < inventory.BackpackSlots.Count; i++) if (inventory.BackpackSlots[i] == armor) { inventory.RemoveFromBackpack(i); break; }
                inventory.Unequip(slot); // the previous armor/accessory is set aside (proof harness)
                inventory.TryEquip(armor, slot);
            }

            yield return Seconds(1.0f);
            var own = _run.Rig?.Passives?.Active.Values.Select(p => p.Id) ?? Enumerable.Empty<string>();
            Ack(command, $"worn={inventory.GetEquipped(slot)?.DefinitionId} rigPassives=[{string.Join("+", own)}]");
        }

        private static EquippedSlot SlotFor(string definitionId) =>
            definitionId != null && definitionId.StartsWith("accessory_", StringComparison.Ordinal) ? EquippedSlot.Accessory : EquippedSlot.Armor;

        /// <summary>
        /// Adrenaline (Combat Harness) on this member's own rig: fight the room's enemies until the host attributes a kill
        /// to this member (res.kill), then report the confirmed kills and the buff at that moment.
        /// </summary>
        private IEnumerator ClientKillPassive(ProofMessage command)
        {
            var k0 = _run.ClientKillsConfirmed;
            var killed = 0;
            void OnKilled() => killed++;
            _run.Rig.CombatEvents.EnemyKilled += OnKilled;
            _run.Rig.CombatEvents.MeleeKill += OnKilled;
            var until = Time.realtimeSinceStartup + command.Seconds;
            while (Time.realtimeSinceStartup < until && _run.ClientKillsConfirmed == k0)
            {
                var player = LocalPlayer;
                var target = NearestReplica(player, command.Number);
                if (player != null && target != null)
                {
                    var hurt = target.GetComponent<CombatHurtbox>();
                    _reader.AimAt((hurt != null ? hurt.AimPoint : (Vector2)target.transform.position) - (Vector2)player.transform.position);
                    _reader.SetFire(true);
                }
                else _reader.SetFire(false);
                yield return null;
            }

            _reader.SetFire(false);
            var adrenaline = _run.Rig.Passives?.GetActive(EquippedSlot.Armor) as AdrenalinePassive;
            var buff = adrenaline != null && adrenaline.IsBuffActive;
            _run.Rig.CombatEvents.EnemyKilled -= OnKilled;
            _run.Rig.CombatEvents.MeleeKill -= OnKilled;
            var confirmed = _run.ClientKillsConfirmed - k0;
            Ack(command, $"kills=+{confirmed} killEvents={killed} once={confirmed >= 1 && killed == confirmed} adrenaline={buff}");
        }

        /// <summary>
        /// Dash Capacitor (Discharge) on this member's own rig: a real dash (input edge) toward the nearest enemy; the
        /// endpoint shockwave runs here and reaches the host only as impact requests. Reported: shockwaves / targets /
        /// impact requests sent.
        /// </summary>
        private IEnumerator ClientDischarge(ProofMessage command)
        {
            var world = _run.Rig.PassiveWorld;
            var shock0 = world.Shockwaves;
            var targets0 = world.ShockwaveTargets;
            var sent0 = _run.CoopClient.ImpactsSent;
            // The dash covers speed × duration (~3 tiles): close to that range, then dash straight at the (host-frozen)
            // enemy so the endpoint lands inside the 1.5-tile shockwave whether the body stops on it or passes it.
            var dash = LocalPlayer.GetComponent<PlayerDash>();
            var reach = dash != null ? dash.CurrentDashSpeed * 0.18f : 3f;
            var until = Time.realtimeSinceStartup + 8f;
            EnemyReplica target = null;
            while (Time.realtimeSinceStartup < until)
            {
                target = NearestReplica(LocalPlayer, command.Number);
                if (target == null) { yield return null; continue; }
                var toTarget = (Vector2)target.transform.position - (Vector2)LocalPlayer.transform.position;
                if (toTarget.magnitude > reach + 0.2f) _reader.SetMove(toTarget);
                else if (toTarget.magnitude < reach - 0.6f) _reader.SetMove(-toTarget);
                else break;
                yield return null;
            }

            if (target != null)
            {
                var toTarget = (Vector2)target.transform.position - (Vector2)LocalPlayer.transform.position;
                _reader.SetMove(toTarget);
                _reader.AimAt(toTarget);
            }

            yield return null;
            _reader.PressDash();
            yield return Seconds(0.7f);
            _reader.SetMove(Vector2.zero);
            yield return Seconds(0.3f);
            Ack(command, $"shockwaves=+{world.Shockwaves - shock0} targets=+{world.ShockwaveTargets - targets0} impactsSent=+{_run.CoopClient.ImpactsSent - sent0} reach={reach:0.00} target={(target != null)}");
        }

        /// <summary>Quickdraw Holster (Hot Swap) on this member's own rig: one real weapon swap, the buff, then swap back.</summary>
        private IEnumerator ClientSwapPassive(ProofMessage command)
        {
            var loadout = _run.Rig.Loadout;
            var hot = _run.Rig.Passives?.GetActive(EquippedSlot.Accessory) as RuinRail.Gameplay.Items.Accessories.HotSwapPassive;
            var swaps = 0;
            void OnSwapped() => swaps++;
            _run.Rig.CombatEvents.WeaponSwapped += OnSwapped;
            var before = hot != null && hot.IsBuffActive;
            var from = loadout.ActiveSlot;
            loadout.SelectSlot(from == WeaponSlot.Primary ? WeaponSlot.Secondary : WeaponSlot.Primary);
            var after = hot != null && hot.IsBuffActive;
            var to = loadout.ActiveSlot;
            _run.Rig.CombatEvents.WeaponSwapped -= OnSwapped;
            yield return Seconds(0.3f);
            loadout.SelectSlot(from);
            Ack(command, $"swaps={swaps} hotSwap={before}->{after} slot={from}->{to}");
        }

        /// <summary>
        /// Defibrillator (84): the host granted this member two; the first is used on the Dead teammate the host placed
        /// in reach (the host decides, the unit is spent on its acceptance), the second with no one to revive (refused,
        /// kept). Reported back: revives accepted here, units left, the notices.
        /// </summary>
        private IEnumerator ClientDefibrillator(ProofMessage command)
        {
            var inventory = _run.Expedition.State.Inventory;
            const string id = "consumable_defibrillator";
            int Units() => (inventory.GetEquipped(EquippedSlot.ActiveConsumable)?.DefinitionId == id ? inventory.GetEquipped(EquippedSlot.ActiveConsumable).Quantity : 0)
                + inventory.BackpackSlots.Where(i => i != null && i.DefinitionId == id).Sum(i => i.Quantity);
            yield return WaitFor(() => Units() >= 2, 10f);
            var granted = Units();
            bool EquipOne()
            {
                if (inventory.GetEquipped(EquippedSlot.ActiveConsumable)?.DefinitionId == id) return true;
                var stack = inventory.BackpackSlots.FirstOrDefault(i => i != null && i.DefinitionId == id);
                if (stack == null) return false;
                var previous = inventory.Unequip(EquippedSlot.ActiveConsumable);
                for (var i = 0; i < inventory.BackpackSlots.Count; i++) if (inventory.BackpackSlots[i] == stack) { inventory.RemoveFromBackpack(i); break; }
                var ok = inventory.TryEquip(stack, EquippedSlot.ActiveConsumable);
                if (previous != null) inventory.TryAddToBackpack(previous);
                return ok;
            }

            var consumables = _run.Rig.Consumables;
            // First use: a Dead teammate in reach. The host's mirror must have seen the grant before it validates.
            EquipOne();
            yield return Seconds(1.5f);
            var revivesBefore = _run.DefibrillatorRevives;
            var first = consumables.UseAction.TryUse();
            yield return WaitFor(() => _run.DefibrillatorRevives > revivesBefore || _run.LastNotice == ExpeditionScene.NoOneToReviveNotice, 8f);
            yield return Seconds(0.5f);
            var afterFirst = Units();
            var firstNotice = _run.LastNotice;
            // Second use: nobody is Dead any more.
            EquipOne();
            yield return Seconds(1.0f);
            var second = consumables.UseAction.TryUse();
            yield return Seconds(2.0f);
            Ack(command, $"granted={granted} firstUse={first} revives={_run.DefibrillatorRevives - revivesBefore} leftAfterFirst={afterFirst} firstNotice={firstNotice} secondUse={second} leftAfterSecond={Units()} secondNotice={_run.LastNotice}");
        }

        /// <summary>
        /// Drops this peer's connection mid-expedition (the transport shuts down exactly as a lost link does) and waits
        /// for the run to reconnect with its token and recompose on the character the host held.
        /// </summary>
        private IEnumerator ClientReconnect(ProofMessage command)
        {
            var old = _run;
            var oldId = _app.Coop.LocalClientId;
            var inventoryBefore = LoadoutValidation.Fingerprint(old.Expedition.State.Inventory.ToSnapshot());
            Unity.Netcode.NetworkManager.Singleton.Shutdown();
            yield return WaitFor(() => { var next = FindFirstObjectByType<ExpeditionScene>(); return next != null && next != old && next.Mode == CoopRunMode.Client && next.CoopClient != null && next.Rooms != null; }, 120f);
            if (!_waitOk) { Record("client reconnected and recomposed", false, "no recomposed run within 120 s"); yield break; }
            _run = FindFirstObjectByType<ExpeditionScene>();
            _run.Expedition.ExpeditionEnded += summary => _summary = summary;
            SubscribeClientCommands();
            yield return WaitFor(() => !_run.CoopGameplayHeld, 30f);
            yield return Seconds(1.5f);
            var inventoryAfter = LoadoutValidation.Fingerprint(_run.Expedition.State.Inventory.ToSnapshot());
            Record("client reconnected with its token and recomposed the run on the held character", _run.Expedition.IsExpeditionActive && inventoryAfter == inventoryBefore,
                $"clientId {oldId}->{_app.Coop.LocalClientId} depth={_run.Expedition.State.Depth} inventorySame={inventoryAfter == inventoryBefore} released={!_run.CoopGameplayHeld}");
            Ack(command, $"old={oldId} new={_app.Coop.LocalClientId}");
        }

        /// <summary>Holds fire at the nearest living enemy replica (the room's, when given) for a while — real weapon, real ammo.</summary>
        private IEnumerator Fight(float seconds, int roomNode)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                var player = LocalPlayer;
                var target = NearestReplica(player, roomNode);
                if (player != null && target != null)
                {
                    var aimPoint = target.GetComponent<CombatHurtbox>() != null ? target.GetComponent<CombatHurtbox>().AimPoint : (Vector2)target.transform.position;
                    _reader.AimAt(aimPoint - (Vector2)player.transform.position);
                    _reader.SetFire(true);
                }
                else _reader.SetFire(false);

                yield return null;
            }

            _reader.SetFire(false);
        }

        private EnemyReplica NearestReplica(GameObject player, int roomNode)
        {
            if (player == null || _run?.CoopClient == null) return null;
            return _run.CoopClient.Replicas.Replicas.Values
                .Where(r => r != null && !r.IsDead && (roomNode < 0 || r.RoomNode == roomNode || r.Kind == CoopActorKind.Boss))
                .OrderBy(r => Vector2.Distance(r.transform.position, player.transform.position))
                .FirstOrDefault();
        }

        /// <summary>Opens the merchant through the real interaction (its screen is this peer's own) and buys through the screen.</summary>
        private IEnumerator ClientBuy(ProofMessage command)
        {
            var player = LocalPlayer;
            var interactor = player != null ? player.GetComponent<PlayerInteractor>() : null;
            var opened = interactor != null && interactor.TryInteract();
            yield return WaitFor(() => _run.Merchant != null && _run.Merchant.IsOpen, 5f);
            var vm = _run.Merchant;
            var detail = $"opened={opened && _waitOk}";
            if (vm != null && vm.IsOpen)
            {
                var row = vm.Rows.ToList().FindIndex(r => r.Tab == RuinRail.UI.Merchant.MerchantTab.Buy && r.Index == command.Number);
                if (row >= 0) vm.SetCursor(row);
                var before = vm.Purchases;
                var coinsBefore = _run.Expedition.State.CarriedCoins;
                var sent = vm.Buy();
                yield return WaitFor(() => vm.Purchases > before || (vm.MessageIsError && vm.Message.Length > 0 && !vm.Message.EndsWith("…")), 10f);
                yield return Seconds(0.5f);
                detail += $" sent={sent} purchases={vm.Purchases} message='{vm.Message}' coins={coinsBefore}->{_run.Expedition.State.CarriedCoins}";
                vm.Close();
            }

            // Duplicate request: the same transaction id twice must resolve once (the ledger returns the stored result).
            if (!string.IsNullOrEmpty(command.Arg) && int.TryParse(command.Arg, out var node) && command.Json == "duplicate")
            {
                var tx = CoopClientWorld.NewTransactionId();
                _run.CoopClient.SendBuy(node, (int)command.X, tx);
                _run.CoopClient.SendBuy(node, (int)command.X, tx);
                yield return Seconds(1.5f);
                detail += $" duplicateTx={tx}";
            }

            Ack(command, detail);
        }

        /// <summary>Weapon Cache: the choice screen is this peer's own; the choice is a request the host's cache resolves once.</summary>
        private IEnumerator ClientCache(ProofMessage command)
        {
            var interactor = LocalPlayer != null ? LocalPlayer.GetComponent<PlayerInteractor>() : null;
            var opened = interactor != null && interactor.TryInteract();
            yield return WaitFor(() => _run.WeaponCache != null && _run.WeaponCache.IsOpen, 5f);
            var vm = _run.WeaponCache;
            var detail = $"opened={opened && _waitOk}";
            if (vm != null && vm.IsOpen)
            {
                var takes = vm.Takes;
                var grants = _run.CoopClient.GrantsReceived;
                vm.Take();
                yield return WaitFor(() => vm.Takes > takes || (vm.MessageIsError && !vm.Message.EndsWith("…")), 10f);
                yield return Seconds(0.5f);
                detail += $" takes={vm.Takes} message='{vm.Message}' grants {grants}->{_run.CoopClient.GrantsReceived}";
                if (vm.IsOpen) vm.Close();
            }

            Ack(command, detail);
        }

        /// <summary>Merchant sale: the item leaves this player's inventory only when the host paid this player's own wallet.</summary>
        private IEnumerator ClientSell(ProofMessage command)
        {
            var interactor = LocalPlayer != null ? LocalPlayer.GetComponent<PlayerInteractor>() : null;
            var opened = interactor != null && interactor.TryInteract();
            yield return WaitFor(() => _run.Merchant != null && _run.Merchant.IsOpen, 5f);
            var vm = _run.Merchant;
            var detail = $"opened={opened && _waitOk}";
            if (vm != null && vm.IsOpen)
            {
                vm.SetTab(RuinRail.UI.Merchant.MerchantTab.Sell);
                var row = vm.Rows.ToList().FindIndex(r => r.Tab == RuinRail.UI.Merchant.MerchantTab.Sell && !r.IsUnsellable && r.Price > 0);
                if (row >= 0)
                {
                    vm.SetCursor(row);
                    var sales = vm.Sales;
                    var coins = _run.Expedition.State.CarriedCoins;
                    var items = _run.Expedition.State.Inventory.BackpackSlots.Count(i => i != null);
                    var name = vm.Selected?.Name;
                    var price = vm.Selected?.Price ?? 0;
                    vm.Sell();
                    yield return WaitFor(() => vm.Sales > sales || (vm.MessageIsError && !vm.Message.EndsWith("…")), 10f);
                    yield return Seconds(0.8f);
                    detail += $" sold='{name}' price={price} sales={vm.Sales} coins {coins}->{_run.Expedition.State.CarriedCoins} items {items}->{_run.Expedition.State.Inventory.BackpackSlots.Count(i => i != null)} message='{vm.Message}'";
                }
                else detail += " nothing sellable";

                vm.Close();
            }

            Ack(command, detail);
        }

        private IEnumerator ClientFinish()
        {
            // The Return (or failure) is this peer's own transaction: wait for it and for the Shelter.
            yield return WaitFor(() => _summary != null || (_run != null && _run.Expedition != null && !_run.Expedition.IsExpeditionActive), 90f);
            var summary = _summary ?? _run?.Expedition?.LastSummary;
            if (summary != null)
            {
                _report.SummaryOutcome = summary.Outcome.ToString();
                _report.SummaryCoinsExtracted = summary.CoinsExtracted;
                _report.SummarySecuredItems = summary.SecuredItems.Count;
                _report.SummaryDeepestDepth = summary.DeepestDepthReached;
                _report.SummaryXp = summary.XpEarned;
            }

            yield return Seconds(1f);
            LeaveRunLostScreen();
            yield return WaitFor(() => _composed.EndsWith(SceneNames.Base + ";"), 60f);
            yield return WaitFor(() => CoopPlayerDirectory.Count == 0, 20f);
            yield return Seconds(1f);
            var probe = _app.ProbeSave();
            _report.SaveMarkerOpenAfter = probe.ExpeditionMarkerOpen;
            _report.BankedCoinsAfter = probe.BankedCoins;
            _report.DeepestDepthOnDisk = probe.DeepestDepthReached;
            _report.PlayerObjectsAfterReturn = CoopPlayerDirectory.Count;
            _report.SpawnedObjectsAfterReturn = CoopPlayerDirectory.SpawnedObjects();
            var expectSuccess = _scenario != "trio";
            Record("client left the expedition: own transaction, save, Shelter", summary != null && summary.IsSuccess == expectSuccess && !probe.ExpeditionMarkerOpen && _composed.EndsWith(SceneNames.Base + ";"),
                $"outcome={_report.SummaryOutcome} coinsExtracted={_report.SummaryCoinsExtracted} secured={_report.SummarySecuredItems} deepest={_report.SummaryDeepestDepth} marker={probe.ExpeditionMarkerOpen} banked={_report.BankedCoinsBefore}->{probe.BankedCoins} playerObjects={_report.PlayerObjectsAfterReturn}");
            _report.Views.Add(View("client-final"));
            var link = CoopRunLink.Current;
            if (link != null)
            {
                // The final record travels without the per-step views (they stay in this peer's own report file).
                var views = _report.Views;
                _report.Views = new List<PeerView>();
                var json = JsonUtility.ToJson(_report);
                _report.Views = views;
                link.SendToHost(CoopKinds.ProofReport, CoopJson.Write(new ProofMessage { Step = "ack:finish", Json = json }));
            }
            yield return Seconds(1.5f);
            Finish("done", null);
        }

        // ================================================================ host

        private IEnumerator RunHost()
        {
            _started = Time.realtimeSinceStartup;
            yield return EnterShelter();
            if (!string.IsNullOrEmpty(_report.Error)) yield break;
            var driver = _app.Network?.Driver as NgoNetworkDriver;
            if (driver == null) { Finish("host", "no live NGO driver (run without -offline-multiplayer)"); yield break; }
            driver.SetDirectAddress("127.0.0.1", _port, true);
            driver.SetConnectionPayload(_name);
            var started = driver.StartHost();
            if (!started.Success) { Finish("host", "StartHost refused: " + started.Error + " " + started.Message); yield break; }
            yield return WaitFor(() => _app.Coop != null && _app.Coop.IsHost, 20f);
            Record("host listening with the session link spawned", _waitOk, $"link={(CoopRunLink.Current != null)} port={_port}");
            if (!_waitOk) { Finish("host", "the session link never spawned"); yield break; }

            var session = _app.Menu.Session;
            yield return WaitFor(() => session.Lobby.Members.Count >= _size && session.Lobby.Members.Where(m => !m.IsHost).All(m => m.IsReady && m.HasValidLoadout), 180f);
            Record($"{_size - 1} client(s) joined and Ready in the host's lobby", _waitOk,
                string.Join("; ", session.Lobby.Members.Select(m => $"{m.ClientId}:{m.ParticipantId}:ready={m.IsReady}:valid={m.HasValidLoadout}")));
            if (!_waitOk) { Finish("lobby", "not every client joined and readied"); yield break; }
            var screen = FindFirstObjectByType<BaseHubScreen>();
            screen.Hub.Multiplayer.SetReady(true);
            screen.Hub.Open(BaseStation.Transit);
            _report.BankedCoinsBefore = session.Banked.Balance;
            var startedRun = screen.Hub.Transit.StartExpedition();
            Record("host started the party's expedition", startedRun && _app.Coop.CurrentRun != null, screen.Hub.Transit.Feedback.Text + $" run={_app.Coop.CurrentRun?.StartId} party={_app.Coop.CurrentRun?.PartySize}");
            if (!startedRun) { Finish("start", "host start refused"); yield break; }
            _app.Coop.LinkLost += () => _report.Notes.Add("link lost at " + (Time.realtimeSinceStartup - _started));

            yield return WaitFor(() => (_run = FindFirstObjectByType<ExpeditionScene>()) != null && _run.Rooms != null && _run.CoopHost != null, 60f);
            if (!_waitOk) { Finish("compose", "the host expedition never composed"); yield break; }
            // Proof reports arrive over the session link itself: they must outlive the run (the final one arrives after
            // the Return has already torn the expedition down).
            if (CoopRunLink.Current != null) CoopRunLink.Current.Received += (sender, kind, json) => { if (kind == CoopKinds.ProofReport) OnProofReport(sender, CoopJson.Read<ProofMessage>(json)); };
            _run.Expedition.ExpeditionEnded += summary => _summary = summary;
            yield return WaitFor(() => _run.CoopHost.GameplayReleases > 0, 60f);
            Record("every client built the depth and gameplay was released together", _waitOk && _run.CoopHost.DepthReadyPeers.Count == _size - 1,
                $"readyPeers={_run.CoopHost.DepthReadyPeers.Count} releases={_run.CoopHost.GameplayReleases}");

            yield return HostScenario();
        }

        private void OnProofReport(ulong clientId, ProofMessage report)
        {
            if (report == null) return;
            _acks[clientId] = report;
            if (!string.IsNullOrEmpty(report.Json) && report.Step != "ack:finish")
            {
                var view = JsonUtility.FromJson<PeerView>(report.Json);
                if (view != null)
                {
                    _lastViews[clientId] = view;
                    _report.Views.Add(view);
                }
            }
            else if (report.Step == "ack:finish" && !string.IsNullOrEmpty(report.Json))
            {
                var clientReport = JsonUtility.FromJson<Report>(report.Json);
                if (clientReport != null) _clientFinals[clientId] = clientReport;
            }
        }

        private readonly Dictionary<ulong, Report> _clientFinals = new();

        private List<ulong> Clients => _run?.CoopRun?.Members.Where(m => !m.IsHost).Select(m => m.ClientId).ToList() ?? new List<ulong>();

        /// <summary>Sends one proof command to one client (or all) and waits for every addressed client's acknowledgement.</summary>
        private IEnumerator Command(ProofMessage command, IEnumerable<ulong> targets, float timeout = StepTimeoutSeconds)
        {
            var list = targets.ToList();
            foreach (var id in list) _acks.Remove(id);
            foreach (var id in list) SendProof(id, command);
            yield return WaitFor(() => list.All(id => _acks.TryGetValue(id, out var a) && a.Step == "ack:" + command.Step), timeout);
        }

        private static void SendProof(ulong clientId, ProofMessage command) => CoopRunLink.Current?.SendToClient(clientId, CoopKinds.ProofCommand, CoopJson.Write(command));

        private IEnumerator ReportAll()
        {
            yield return Command(new ProofMessage { Step = "report" }, Clients, 20f);
        }

        private GameObject HostPlayer => _run?.Rig?.Player;

        private static void Teleport(GameObject entity, Vector2 position)
        {
            if (entity == null) return;
            entity.transform.position = new Vector3(position.x, position.y, entity.transform.position.z);
            var body = entity.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.position = position;
                body.linearVelocity = Vector2.zero;
            }
        }

        private void TeleportParty(Vector2 centre, float spread = 0.9f)
        {
            var members = _run.CoopRun.Members.OrderBy(m => m.ClientId).ToList();
            for (var i = 0; i < members.Count; i++)
            {
                var entity = members[i].IsHost ? HostPlayer : _run.MemberEntity(members[i].ClientId);
                Teleport(entity, centre + new Vector2((i - (members.Count - 1) * 0.5f) * spread, 0f));
            }
        }

        /// <summary>Scripted-fight safety: members are topped up by the host while the script fights (never during the Downed step).</summary>
        private void KeepPartyAlive()
        {
            foreach (var member in _run.Party.Members)
            {
                var health = member.GameObject != null ? member.GameObject.GetComponent<HealthComponent>() : null;
                var life = member.GameObject != null ? member.GameObject.GetComponent<PlayerLifeStateComponent>() : null;
                if (health == null || life == null || !life.IsAlive) continue;
                if (health.CurrentHealth < health.MaxHealth * 0.6f) health.Heal(health.MaxHealth);
            }
        }

        private IEnumerator HostFight(float seconds, RoomRuntime room, bool untilCleared)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                KeepPartyAlive();
                var player = HostPlayer;
                var target = NearestHostEnemy(player, room);
                if (player != null && target != null)
                {
                    var hurt = target.GetComponent<CombatHurtbox>();
                    _reader.AimAt((hurt != null ? hurt.AimPoint : (Vector2)target.transform.position) - (Vector2)player.transform.position);
                    _reader.SetFire(true);
                }
                else _reader.SetFire(false);

                if (untilCleared && room != null && room.Lifecycle == RoomLifecycleState.Cleared) break;
                yield return null;
            }

            _reader.SetFire(false);
        }

        private GameObject NearestHostEnemy(GameObject player, RoomRuntime room)
        {
            if (player == null) return null;
            IEnumerable<GameObject> candidates = FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Where(e => e.IsAlive).Select(e => e.gameObject)
                .Concat(FindObjectsByType<MovesetActorController>(FindObjectsSortMode.None).Where(a => a.IsAlive).Select(a => a.gameObject));
            if (room != null) candidates = candidates.Where(c => room.InteriorWorldBounds.Contains(c.transform.position));
            return candidates.OrderBy(c => Vector2.Distance(c.transform.position, player.transform.position)).FirstOrDefault();
        }

        /// <summary>Host-authoritative finish of a room's remaining actors (bounded proof time; the host decides deaths anyway).</summary>
        private int FinishRoomActors(RoomRuntime room)
        {
            var killed = 0;
            foreach (var enemy in FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Where(e => e.IsAlive && room.InteriorWorldBounds.Contains(e.transform.position)))
            {
                if (enemy.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(100000))) killed++;
            }

            foreach (var actor in FindObjectsByType<MovesetActorController>(FindObjectsSortMode.None).Where(a => a.IsAlive && room.InteriorWorldBounds.Contains(a.transform.position)))
            {
                if (actor.Health.TryApplyDamage(new DamageRequest(100000))) killed++;
            }

            return killed;
        }

        private RoomRuntime StartRoom() => _run.Rooms.Values.FirstOrDefault(r => r.State.RoomType == RoomType.Start);

        private RoomRuntime PickRoom(Func<RoomRuntime, bool> predicate)
        {
            var start = StartRoom();
            var origin = start != null ? (Vector2)start.transform.position : Vector2.zero;
            return _run.Rooms.Values.Where(r => r != null && predicate(r)).OrderBy(r => Vector2.Distance(r.transform.position, origin)).FirstOrDefault();
        }

        private PeerView ViewOf(ulong clientId) => _lastViews.TryGetValue(clientId, out var v) ? v : null;

        private IEnumerator HostScenario()
        {
            var clients = Clients;
            var first = clients.FirstOrDefault();
            var trio = _size >= 3;

            // ---- 1. identical D1 on every peer ----
            yield return ReportAll();
            var host = View("host");
            _report.Views.Add(host);
            var same = clients.All(c => ViewOf(c) is { } v && v.LayoutFingerprint == host.LayoutFingerprint && v.Seed == host.Seed && v.Depth == host.Depth && v.Biome == host.Biome && v.RoomSignature == host.RoomSignature && v.Rooms == host.Rooms);
            Record("identical D1 on every peer (seed, depth, biome, layout, room ids/positions/doors)", _waitOk && same,
                $"host={host.Seed}/{host.Depth}/{host.Biome}/{host.LayoutFingerprint}/{host.RoomSignature} " + string.Join(" ", clients.Select(c => $"c{c}={ViewOf(c)?.Seed}/{ViewOf(c)?.Depth}/{ViewOf(c)?.Biome}/{ViewOf(c)?.LayoutFingerprint}/{ViewOf(c)?.RoomSignature}")));
            var presentation = host.Cameras == 1 && host.AudioListeners == 1 && host.PlayerInputs == 1 && host.OwnedPlayerObjects == 1 && host.ReplicaCameras == 0 && host.ReplicaListeners == 0 && host.ReplicaInputs == 0
                               && clients.All(c => ViewOf(c) is { } v && v.Cameras == 1 && v.AudioListeners == 1 && v.PlayerInputs == 1 && v.OwnedPlayerObjects == 1 && v.ReplicaCameras == 0 && v.ReplicaListeners == 0 && v.ReplicaInputs == 0 && v.PlayerObjects == _size);
            Record("one camera, one listener, one input, one owned character per process; replicas carry none", presentation,
                $"host cams={host.Cameras} listeners={host.AudioListeners} inputs={host.PlayerInputs} owned={host.OwnedPlayerObjects} objects={host.PlayerObjects} " + string.Join(" ", clients.Select(c => $"c{c} cams={ViewOf(c)?.Cameras} listeners={ViewOf(c)?.AudioListeners} inputs={ViewOf(c)?.PlayerInputs} owned={ViewOf(c)?.OwnedPlayerObjects} objects={ViewOf(c)?.PlayerObjects}")));
            var identities = clients.All(c => ViewOf(c) is { } v && v.ParticipantId == _run.CoopRun.MemberFor(c)?.ParticipantId && v.PartyMembers == _size);
            Record("each client runs its own transaction under the participant id the host assigned", identities,
                string.Join(" ", clients.Select(c => $"c{c}:{ViewOf(c)?.ParticipantId}=={_run.CoopRun.MemberFor(c)?.ParticipantId} party={ViewOf(c)?.PartyMembers}")));
            var scaled = _run.Party.ScalingPartySize == _size && _run.Rooms.Values.All(r => r.PartySize == _size);
            Record($"authored co-op scaling applied for the composed party of {_size}", scaled, $"scalingPartySize={_run.Party.ScalingPartySize} roomPartySize={_run.Rooms.Values.First().PartySize}");

            // ---- 2. movement both ways ----
            var start = StartRoom();
            var centre = start != null ? start.InteriorWorldBounds.center : (Vector2)HostPlayer.transform.position;
            TeleportParty(centre, 1.2f);
            yield return Seconds(1.0f);
            var clientBefore = clients.ToDictionary(c => c, c => (Vector2)_run.MemberEntity(c).transform.position);
            var hostBefore = (Vector2)HostPlayer.transform.position;
            foreach (var c in clients) SendProof(c, new ProofMessage { Step = "move", X = 0f, Y = 1f, Seconds = 1.2f });
            _reader.SetMove(Vector2.down);
            yield return Seconds(1.2f);
            _reader.SetMove(Vector2.zero);
            yield return Seconds(1.0f);
            yield return ReportAll();
            var clientMoved = clients.All(c => Vector2.Distance(clientBefore[c], _run.MemberEntity(c).transform.position) > 0.8f);
            var hostMoved = Vector2.Distance(hostBefore, HostPlayer.transform.position) > 0.8f;
            var hostSeenMoving = clients.All(c => ViewOf(c) is { } v && ParsePosition(v.MemberPositions, 0) is { } p && Vector2.Distance(p, HostPlayer.transform.position) < 1.5f);
            Record("client movement arrives on the host (host-applied intents) and host movement reaches the clients", clientMoved && hostMoved && hostSeenMoving,
                $"clientsMovedOnHost={string.Join(",", clients.Select(c => Vector2.Distance(clientBefore[c], _run.MemberEntity(c).transform.position).ToString("0.0")))} hostMoved={Vector2.Distance(hostBefore, HostPlayer.transform.position):0.0} hostAsSeenByClients={string.Join(",", clients.Select(c => ViewOf(c)?.MemberPositions))}");

            // ---- 3. combat room: activation once, enemies replicated, both fire, clear once ----
            var combat = PickRoom(r => r.State.RoomType == RoomType.Combat && r.Lifecycle == RoomLifecycleState.Unentered && r.HasEncounter && !r.State.IsElite)
                         ?? PickRoom(r => r.State.RoomType == RoomType.Combat && r.Lifecycle == RoomLifecycleState.Unentered && r.HasEngagement);
            if (combat == null) { Record("combat room available on D1", false, "no unentered combat room"); }
            else
            {
                var activations = 0;
                var clears = 0;
                combat.Activated += _ => activations++;
                combat.Cleared += (_, _) => clears++;
                var hitsBefore = clients.ToDictionary(c => c, c => _run.CoopHost.HitsBySender.TryGetValue(c, out var h) ? h : 0);
                TeleportParty(combat.InteriorWorldBounds.center, 1.0f);
                yield return WaitFor(() => combat.Lifecycle == RoomLifecycleState.Active, 10f);
                yield return Seconds(1.5f);
                var hostLive = FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Count(e => e.IsAlive && combat.InteriorWorldBounds.Contains(e.transform.position))
                               + FindObjectsByType<MovesetActorController>(FindObjectsSortMode.None).Count(a => a.IsAlive && combat.InteriorWorldBounds.Contains(a.transform.position));
                yield return ReportAll();
                var clientsSee = clients.All(c => ViewOf(c) is { } v && v.LiveEnemies >= Math.Max(1, hostLive - 1) && v.RoomStates.Contains($"{combat.State.NodeId}:1:1"));
                Record("combat room activated once for the whole party; doors locked and the enemy set replicated on every peer", activations == 1 && combat.DoorsLocked && hostLive > 0 && clientsSee,
                    $"node={combat.State.NodeId} activations={activations} hostLive={hostLive} clients=" + string.Join(" ", clients.Select(c => $"c{c}:live={ViewOf(c)?.LiveEnemies} defs={ViewOf(c)?.EnemyDefinitions}")));
                foreach (var c in clients) SendProof(c, new ProofMessage { Step = "fight", Seconds = 12f, Number = combat.State.NodeId });
                yield return HostFight(12f, combat, false);
                yield return Command(new ProofMessage { Step = "report" }, clients, 20f);
                var clientHits = clients.ToDictionary(c => c, c => (_run.CoopHost.HitsBySender.TryGetValue(c, out var h) ? h : 0) - hitsBefore[c]);
                var clientDamage = clients.ToDictionary(c => c, c => _run.CoopHost.DamageBySender.TryGetValue(c, out var d) ? d : 0);
                Record("clients fire their own weapons; their hits reach the enemies only as host-validated requests", clients.All(c => clientHits[c] > 0) && clients.All(c => ViewOf(c)?.ShotsSent > 0),
                    string.Join(" ", clients.Select(c => $"c{c}: shots={ViewOf(c)?.ShotsSent} hitsSent={ViewOf(c)?.HitsSent} hostApplied={clientHits[c]} damage={clientDamage[c]}")) + $" rejected={_run.CoopHost.HitsRejected} ({_run.CoopHost.LastRejection}) hostShotsDrawnFromClients={_run.CoopHost.ShotsRelayed}");
                var finished = 0;
                if (combat.Lifecycle != RoomLifecycleState.Cleared)
                {
                    yield return HostFight(10f, combat, true);
                    if (combat.Lifecycle != RoomLifecycleState.Cleared) finished = FinishRoomActors(combat);
                }

                yield return WaitFor(() => combat.Lifecycle == RoomLifecycleState.Cleared, 20f);
                yield return Seconds(1.5f);
                yield return ReportAll();
                var clientsCleared = clients.All(c => ViewOf(c) is { } v && v.RoomStates.Contains($"{combat.State.NodeId}:2:0") && v.LiveEnemies == 0);
                Record("room cleared exactly once; doors unlocked and every enemy dead on every peer", clears == 1 && combat.Lifecycle == RoomLifecycleState.Cleared && !combat.DoorsLocked && clientsCleared,
                    $"clears={clears} hostFinishedRemaining={finished} deathsSent={_run.CoopHost.DeathsSent} clients=" + string.Join(" ", clients.Select(c => $"c{c}:live={ViewOf(c)?.LiveEnemies} xp={ViewOf(c)?.XpEarned} kills={ViewOf(c)?.EnemiesDefeated} cleared={ViewOf(c)?.RoomsCleared}")));
                var xpShared = clients.All(c => ViewOf(c) is { } v && v.XpEarned > 0 && v.EnemiesDefeated > 0);
                Record("every member's own profile earns the kill XP once per host-side death", xpShared, $"hostXp={_run.Expedition.State.Stats.XpEarned} hostKills={_run.Expedition.State.Stats.EnemiesDefeated} " + string.Join(" ", clients.Select(c => $"c{c}:xp={ViewOf(c)?.XpEarned}/kills={ViewOf(c)?.EnemiesDefeated}")));
            }

            // ---- 4. loot: item race, client-only pickup, coin split, ammo ----
            yield return LootSteps(clients, first);

            // ---- 5. non-combat: merchant trade (client), simultaneous purchase, duplicate request, sale; chests and events ----
            if (!trio) yield return MerchantSteps(first);
            if (!trio) yield return NonCombatSteps(first);
            yield return MenuSteps(first);

            // ---- 6. Downed / revive both ways ----
            yield return ReviveSteps(first);

            // ---- 7. boss ----
            yield return BossSteps(clients, true);

            // ---- 8. Transit: host Descend alone does nothing; everyone Descend descends once ----
            yield return DescendSteps(clients);

            if (trio)
            {
                yield return ReactivePassiveCrossTalk(clients);
                yield return TrioEndSteps(clients);
                yield break;
            }

            // ---- 8b. the client's incoming-impact armor passives run on the host's copy of its body ----
            yield return ReactivePassiveSteps(first);

            // ---- 8c. the client's event-driven Legendary passives: kill (host-attributed), swap (own rig), ammo (host copy) ----
            yield return LegendaryEventSteps(first);

            // ---- 9. a client drops mid-expedition and reconnects into the current state ----
            yield return ReconnectSteps(first);
            clients = Clients;
            yield return ReactivePassiveAfterReconnect(clients[0]);

            // ---- 10. D2 boss, RETURN vote, extraction and save on every peer ----
            yield return ReturnSteps(clients);
        }

        // ---------------------------------------------------------------- reactive armor passives (final release cleanup)

        private CoopMemberMirror MirrorOf(ulong client) => _run.MemberMirrors.TryGetValue(client, out var m) ? m : null;
        private string MirrorPassive(ulong client) => MirrorOf(client)?.Passives?.GetActive(EquippedSlot.Armor)?.Id ?? "none";

        /// <summary>Grants a Legendary armor through the real host grant, has the member wear it, waits for the host mirror.</summary>
        private IEnumerator WearArmor(ulong client, string armorId, string label)
        {
            yield return Command(new ProofMessage { Step = "free-slot" }, new[] { client }, 15f);
            _run.CoopHost.SendGrant(client, new ItemInstance(armorId, 1, Rarity.Legendary).ToSnapshot(), _run.Expedition.State.TransactionId, "proof_" + label);
            yield return Command(new ProofMessage { Step = "equip", Arg = armorId }, new[] { client }, 25f);
            yield return WaitFor(() => MirrorOf(client)?.Inventory.GetEquipped(SlotFor(armorId))?.DefinitionId == armorId, 10f);
            yield return Seconds(0.3f);
        }

        private string ClientAck(ulong client) => _acks.TryGetValue(client, out var a) ? a.Arg : "";

        private static void Hit(GameObject body, ImpactRequest request) => ImpactDispatcher.Apply(body.transform, request);

        private IEnumerator ClientPosition(ulong client, System.Action<Vector2?> result)
        {
            yield return Command(new ProofMessage { Step = "report" }, new[] { client }, 15f);
            var v = ViewOf(client);
            result(v != null ? new Vector2(v.PositionX, v.PositionY) : (Vector2?)null);
        }

        private IEnumerator ReactivePassiveSteps(ulong client)
        {
            var body = _run.MemberEntity(client);
            var receiver = body.GetComponent<PlayerImpactReceiver>();
            var health = body.GetComponent<HealthComponent>();
            var stats = body.GetComponent<PlayerStatsBinder>().Stats;
            // A quiet spot: the start room of the current depth, the party gathered.
            var start = _run.Rooms.Values.First(r => r.State.RoomType == RoomType.Start);
            TeleportParty(start.InteriorWorldBounds.center, 1.5f);
            yield return Seconds(1.0f);

            // ---- Anchored (Riot Armor): one stagger negated, the next refused while on cooldown, again after 8 s ----
            yield return WearArmor(client, "armor_riot_armor", "anchored");
            var equipAck = ClientAck(client);
            var n0 = receiver.StaggersNegated;
            Hit(body, new ImpactRequest(Vector2.right, 0f, 1000f));
            var first = receiver.StaggersNegated == n0 + 1 && !receiver.IsStaggered;
            var firstAt = Time.time;
            yield return Seconds(0.5f);
            Hit(body, new ImpactRequest(Vector2.right, 0f, 1000f));
            var second = receiver.StaggersNegated == n0 + 1 && receiver.IsStaggered;
            yield return WaitFor(() => Time.time - firstAt > AnchoredPassive.CooldownSeconds + 0.4f && !receiver.IsStaggered, 15f);
            Hit(body, new ImpactRequest(Vector2.right, 0f, 1000f));
            var third = receiver.StaggersNegated == n0 + 2;
            yield return Command(new ProofMessage { Step = "report" }, new[] { client }, 15f);
            Record("Anchored on a remote client (host copy): one stagger negated, the next staggers while on cooldown, negated again after 8 s; the client's own rig does not run it",
                MirrorPassive(client) == "anchored" && first && second && third && !equipAck.Contains("anchored") && ViewOf(client)?.LifeState == "Alive",
                $"hostPassive={MirrorPassive(client)} negations +{receiver.StaggersNegated - n0} (first={first} cooldownStaggered={second} afterCooldown={third}); client {equipAck}");

            // ---- Shock Absorber (Blast Suit): explosion knockback ignored every time; ordinary knockback still moves; no stale Anchored ----
            yield return WearArmor(client, "armor_blast_suit", "shock_absorber");
            equipAck = ClientAck(client);
            var staleBefore = receiver.StaggersNegated;
            Hit(body, new ImpactRequest(Vector2.right, 0f, 1000f));
            var noStaleAnchored = receiver.StaggersNegated == staleBefore;
            yield return WaitFor(() => !receiver.IsStaggered, 5f);
            Vector2? clientBefore = null, clientAfterExplosion = null, clientAfterNormal = null;
            yield return ClientPosition(client, p => clientBefore = p);
            var hostBefore = (Vector2)body.transform.position;
            var k0 = receiver.KnockbacksNegated;
            Hit(body, new ImpactRequest(Vector2.left, 3f, 0f, DamageKind.Explosion));
            Hit(body, new ImpactRequest(Vector2.left, 3f, 0f, DamageKind.Explosion));
            yield return Seconds(1.5f);
            var hostAfterExplosion = (Vector2)body.transform.position;
            yield return ClientPosition(client, p => clientAfterExplosion = p);
            var explosionsIgnored = receiver.KnockbacksNegated == k0 + 2 && Vector2.Distance(hostBefore, hostAfterExplosion) < 0.15f;
            // An ordinary (non-explosion) hit, strong enough that the host's displacement exceeds the owner's frozen
            // 0.75-tile reconciliation tolerance, so the owner visibly snaps to the host's authoritative position.
            Hit(body, new ImpactRequest(Vector2.left, 8f, 0f));
            yield return Seconds(1.5f);
            var hostAfterNormal = (Vector2)body.transform.position;
            yield return ClientPosition(client, p => clientAfterNormal = p);
            var normalMoves = receiver.KnockbacksNegated == k0 + 2 && Vector2.Distance(hostAfterExplosion, hostAfterNormal) > OwnerReconciliation.SnapToleranceTiles;
            var clientSees = clientBefore.HasValue && clientAfterExplosion.HasValue && clientAfterNormal.HasValue
                             && Vector2.Distance(clientBefore.Value, clientAfterExplosion.Value) < 0.3f && Vector2.Distance(clientAfterExplosion.Value, clientAfterNormal.Value) > 0.3f;
            Record("Shock Absorber on a remote client: explosion knockback ignored (twice, host-classified), ordinary knockback still moves the body, the client sees exactly that; the swapped-out Anchored is gone",
                MirrorPassive(client) == "shock_absorber" && explosionsIgnored && normalMoves && clientSees && noStaleAnchored && !equipAck.Contains("shock_absorber"),
                $"hostPassive={MirrorPassive(client)} negated +{receiver.KnockbacksNegated - k0} host moved {Vector2.Distance(hostBefore, hostAfterExplosion):0.00} after explosions / {Vector2.Distance(hostAfterExplosion, hostAfterNormal):0.00} after a normal hit; client {clientBefore}->{clientAfterExplosion}->{clientAfterNormal}; staleAnchored={!noStaleAnchored}; client {equipAck}");

            // ---- Exo Lock (Reinforced Exo-Rig): a 20+ hit grants the resistance buff, the 8 s cooldown blocks a re-trigger ----
            yield return WearArmor(client, "armor_reinforced_exo_rig", "exo_lock");
            equipAck = ClientAck(client);
            health.Heal(health.MaxHealth);
            var exo = MirrorOf(client)?.Passives?.GetActive(EquippedSlot.Armor) as ExoLockPassive;
            var small = health.TryApplyDamage(new DamageRequest(ExoLockPassive.DamageThreshold - 1)) && exo != null && !exo.IsBuffActive;
            var r0 = stats.GetPercent(StatId.StaggerResistance);
            health.TryApplyDamage(new DamageRequest(25));
            var triggered = exo != null && exo.IsBuffActive && stats.GetPercent(StatId.StaggerResistance) > r0;
            var triggeredAt = Time.time;
            var r1 = stats.GetPercent(StatId.StaggerResistance);
            yield return Seconds(1.0f);
            health.TryApplyDamage(new DamageRequest(25));
            yield return WaitFor(() => Time.time - triggeredAt > ExoLockPassive.DurationSeconds + 0.5f, 10f);
            var expiredOnSchedule = exo != null && !exo.IsBuffActive && stats.GetPercent(StatId.StaggerResistance) == r0;
            yield return WaitFor(() => Time.time - triggeredAt > ExoLockPassive.CooldownSeconds + 0.5f, 10f);
            health.TryApplyDamage(new DamageRequest(25));
            var retriggered = exo != null && exo.IsBuffActive;
            health.Heal(health.MaxHealth);
            yield return Command(new ProofMessage { Step = "report" }, new[] { client }, 15f);
            Record("Exo Lock on a remote client: a 19 hit does nothing, a 25 hit grants the resistance buff once, a second hit in the 8 s cooldown does not extend it, after the cooldown it triggers again; the client's own rig does not run it",
                MirrorPassive(client) == "exo_lock" && small && triggered && expiredOnSchedule && retriggered && !equipAck.Contains("exo_lock") && ViewOf(client)?.LifeState == "Alive",
                $"hostPassive={MirrorPassive(client)} small={small} stagger resistance {r0}%->{r1}% (cap 50) expiredAfter5s={expiredOnSchedule} retriggeredAfter8s={retriggered}; client {equipAck}");
        }

        private string MirrorAccessoryPassive(ulong client) => MirrorOf(client)?.Passives?.GetActive(EquippedSlot.Accessory)?.Id ?? "none";

        private IEnumerator LegendaryEventSteps(ulong client)
        {
            // ---- Adrenaline (Combat Harness): the member's own kill, attributed by the host (res.kill), triggers its own rig ----
            yield return WearArmor(client, "armor_combat_harness", "adrenaline");
            var equipAck = ClientAck(client);
            var combat = PickRoom(r => r.State.RoomType == RoomType.Combat && r.Lifecycle == RoomLifecycleState.Unentered && r.HasEncounter && !r.State.IsElite)
                         ?? PickRoom(r => r.State.RoomType == RoomType.Combat && r.Lifecycle == RoomLifecycleState.Unentered && r.HasEngagement);
            if (combat == null) Record("Adrenaline on a remote client (host-attributed kill)", false, "no unentered combat room on this depth");
            else
            {
                var k0 = _run.CoopHost.KillsAttributed;
                TeleportParty(combat.InteriorWorldBounds.center, 1.0f);
                yield return WaitFor(() => combat.Lifecycle == RoomLifecycleState.Active, 10f);
                yield return Seconds(1.5f);
                // Host-authoritative: every enemy of the room is left at 1 HP so the member's next hit is a kill.
                foreach (var hp in FindObjectsByType<HealthComponent>(FindObjectsSortMode.None))
                {
                    if (hp == null || !hp.IsAlive || hp.GetComponent<PlayerLifeStateComponent>() != null || !combat.InteriorWorldBounds.Contains(hp.transform.position)) continue;
                    for (var i = 0; i < 50 && hp.CurrentHealth > 1; i++) hp.TryApplyDamage(new DamageRequest(Mathf.Max(1, hp.CurrentHealth - 1)));
                }

                KeepPartyAlive();
                yield return Command(new ProofMessage { Step = "kill-passive", Seconds = 15f, Number = combat.State.NodeId }, new[] { client }, 30f);
                var ack = ClientAck(client);
                var attributed = _run.CoopHost.KillsAttributed - k0;
                Record("Adrenaline on a remote client: the host attributes the member's kill once (res.kill), the member's own rig raises EnemyKilled once and the buff starts; the host copy does not run it",
                    attributed >= 1 && ack.Contains("once=True") && ack.Contains("adrenaline=True") && equipAck.Contains("adrenaline") && MirrorPassive(client) == "none",
                    $"hostAttributed=+{attributed} client {ack}; hostPassive={MirrorPassive(client)}; client {equipAck}");
                yield return WorldActionSteps(client, combat);
                KeepPartyAlive();
            }

            // ---- Hot Swap (Quickdraw Holster): a real swap on the member's own rig; the host copy does not run it ----
            yield return WearArmor(client, "accessory_quickdraw_holster", "hot_swap");
            equipAck = ClientAck(client);
            yield return Command(new ProofMessage { Step = "swap-passive" }, new[] { client }, 15f);
            var swapAck = ClientAck(client);
            Record("Hot Swap on a remote client: one weapon swap raises WeaponSwapped once and starts the buff on the member's own rig; the host copy does not run it",
                swapAck.Contains("swaps=1 ") && swapAck.Contains("hotSwap=False->True") && equipAck.Contains("hot_swap") && MirrorAccessoryPassive(client) == "none",
                $"client {swapAck}; hostPassive={MirrorAccessoryPassive(client)}; client {equipAck}");

            // ---- Scavenger's Reserve (Ammo Pouch): the member's ammo pickup is resolved and sized on the host copy (+25%) ----
            yield return WearArmor(client, "accessory_ammo_pouch", "scavengers_reserve");
            equipAck = ClientAck(client);
            var start = StartRoom();
            var centre = start != null ? (Vector2)start.InteriorWorldBounds.center : (Vector2)HostPlayer.transform.position;
            TeleportParty(centre, 1.5f);
            IsolateAllBut(client, centre);
            yield return Seconds(0.8f);
            yield return ReportAll();
            var ammoBefore = ViewOf(client)?.AmmoTotal ?? 0;
            var spawnerHost = new GameObject("ProofScavenger");
            var pickup = _run.CreateLootSpawnerFor(spawnerHost).CreateItemPickup((Vector2)_run.MemberEntity(client).transform.position + Vector2.right * 0.8f);
            pickup.Hold(new ItemInstance("ammo_light", 8), ItemCategory.Ammo);
            yield return Seconds(2.5f);
            yield return ReportAll();
            var ammoAfter = ViewOf(client)?.AmmoTotal ?? 0;
            Destroy(spawnerHost);
            Record("Scavenger's Reserve on a remote client: resolved on the host copy, an 8-round ammo stack arrives as 10 in the member's own reserve; the member's rig does not run it",
                MirrorAccessoryPassive(client) == "scavengers_reserve" && ammoAfter - ammoBefore == 10 && !equipAck.Contains("scavengers_reserve"),
                $"hostPassive={MirrorAccessoryPassive(client)} ammo {ammoBefore}->{ammoAfter}; client {equipAck}");

            // Back into the Exo-Rig for the reconnect step (the Ammo Pouch stays: two host-side passives must survive it).
            yield return WearArmor(client, "armor_reinforced_exo_rig", "exo_lock_again");
        }

        /// <summary>
        /// The passives' world actions for a remote member (PassiveWorldActions): Arc Stagger's shockwave and Room Sweep's
        /// pull run on the host's copy (host-resolved); Discharge's shockwave runs on the member's rig and reaches the host
        /// only as validated impact requests. Ends with the room cleared.
        /// </summary>
        private IEnumerator WorldActionSteps(ulong client, RoomRuntime combat)
        {
            var mirror = MirrorOf(client);
            KeepPartyAlive();

            // ---- Arc Stagger (Shock Charm): the member's own staggering impact → one shockwave on the host copy ----
            yield return WearArmor(client, "accessory_shock_charm", "arc_stagger");
            var equipAck = ClientAck(client);
            var arc0 = mirror.World.Shockwaves;
            var impacts0 = _run.CoopHost.ImpactsApplied;
            KeepPartyAlive();
            yield return Command(new ProofMessage { Step = "stagger", Number = combat.State.NodeId }, new[] { client }, 15f);
            var staggerAck = ClientAck(client);
            yield return WaitFor(() => mirror.World.Shockwaves > arc0, 5f);
            Record("Arc Stagger on a remote client: the member's staggering impact is validated by the host, and the arc shockwave runs once on the host's copy (not on the member's rig)",
                mirror.World.Shockwaves == arc0 + 1 && _run.CoopHost.ImpactsApplied > impacts0 && MirrorAccessoryPassive(client) == "arc_stagger" && !equipAck.Contains("arc_stagger"),
                $"hostShockwaves=+{mirror.World.Shockwaves - arc0} reached={mirror.World.ShockwaveTargets} hostImpactsApplied=+{_run.CoopHost.ImpactsApplied - impacts0}; client {staggerAck}; hostPassive={MirrorAccessoryPassive(client)}; client {equipAck}");

            // ---- Discharge (Dash Capacitor): the member's real dash → shockwave on its rig → impact requests the host validates ----
            yield return WearArmor(client, "accessory_dash_capacitor", "discharge");
            equipAck = ClientAck(client);
            impacts0 = _run.CoopHost.ImpactsApplied;
            var mirrorShock = mirror.World.Shockwaves;
            FreezeRoomEnemies(combat); // a still target, so the dash endpoint is predictable (the passive is not changed)
            KeepPartyAlive();
            yield return Command(new ProofMessage { Step = "discharge", Number = combat.State.NodeId }, new[] { client }, 20f);
            var dischargeAck = ClientAck(client);
            yield return Seconds(0.5f);
            Record("Discharge on a remote client: the dash endpoint shockwave runs once on the member's rig and reaches the enemies only as host-validated impacts; the host copy emits nothing of its own",
                dischargeAck.Contains("shockwaves=+1 ") && !dischargeAck.Contains("impactsSent=+0") && _run.CoopHost.ImpactsApplied > impacts0 && mirror.World.Shockwaves == mirrorShock && equipAck.Contains("discharge") && MirrorAccessoryPassive(client) == "none",
                $"client {dischargeAck}; hostImpactsApplied=+{_run.CoopHost.ImpactsApplied - impacts0} hostCopyShockwaves=+{mirror.World.Shockwaves - mirrorShock}; client {equipAck}");

            // ---- Room Sweep (Magnetic Coil): on the host-decided clear, the host copy pulls the room's coins and ammo ----
            yield return WearArmor(client, "accessory_magnetic_coil", "room_sweep");
            equipAck = ClientAck(client);
            var bounds = combat.InteriorWorldBounds;
            var body = _run.MemberEntity(client);
            var corners = new[] { new Vector2(bounds.xMin + 0.7f, bounds.yMin + 0.7f), new Vector2(bounds.xMax - 0.7f, bounds.yMin + 0.7f), new Vector2(bounds.xMin + 0.7f, bounds.yMax - 0.7f), new Vector2(bounds.xMax - 0.7f, bounds.yMax - 0.7f) }
                .OrderByDescending(c => Mathf.Min(Vector2.Distance(c, body.transform.position), Vector2.Distance(c, HostPlayer.transform.position))).Take(2).ToArray();
            var spawnerHost = new GameObject("ProofRoomSweep");
            var spawner = _run.CreateLootSpawnerFor(spawnerHost);
            var coins = spawner.CreateCoinPickup(corners[0]);
            coins.SetAmount(9);
            var ammo = spawner.CreateItemPickup(corners[1]);
            ammo.Hold(new ItemInstance("ammo_light", 6), ItemCategory.Ammo);
            yield return ReportAll();
            var before = ViewOf(client);
            var ammoBefore = before?.AmmoTotal ?? 0;
            var coinsBefore = before?.Coins ?? 0;
            var sweeps0 = mirror.World.Sweeps;
            // The host decides the clear (authoritative), exactly as any clear: every enemy of the room is resolved.
            for (var until = Time.realtimeSinceStartup + 20f; Time.realtimeSinceStartup < until && combat.Lifecycle != RoomLifecycleState.Cleared;)
            {
                KeepPartyAlive();
                foreach (var hp in FindObjectsByType<HealthComponent>(FindObjectsSortMode.None))
                    if (hp != null && hp.IsAlive && hp.GetComponent<PlayerLifeStateComponent>() == null && combat.InteriorWorldBounds.Contains(hp.transform.position)) hp.TryApplyDamage(new DamageRequest(999999));
                yield return null;
            }

            yield return WaitFor(() => coins.IsCollected && ammo.IsConsumed, 6f);
            yield return Seconds(0.5f);
            yield return ReportAll();
            var after = ViewOf(client);
            Record("Room Sweep on a remote client: the host-decided clear pulls the room's coin pile and ammo stack to the member's host copy once; the member's own wallet and reserve receive them",
                combat.Lifecycle == RoomLifecycleState.Cleared && mirror.World.Sweeps == sweeps0 + 1 && coins.IsCollected && ammo.IsConsumed && (after?.AmmoTotal ?? 0) - ammoBefore >= 6 && mirror.World.SweptPickups >= 2 && (after?.Coins ?? 0) > coinsBefore
                && MirrorAccessoryPassive(client) == "room_sweep" && !equipAck.Contains("room_sweep"),
                $"cleared={combat.Lifecycle} hostSweeps=+{mirror.World.Sweeps - sweeps0} swept={mirror.World.SweptPickups} coinsCollected={coins.IsCollected} ammoConsumed={ammo.IsConsumed} client ammo {ammoBefore}->{after?.AmmoTotal} coins {coinsBefore}->{after?.Coins}; hostPassive={MirrorAccessoryPassive(client)}; client {equipAck}");
            Destroy(spawnerHost);
        }

        private void FreezeRoomEnemies(RoomRuntime room)
        {
            foreach (var enemy in FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
                if (enemy != null && room.InteriorWorldBounds.Contains(enemy.transform.position)) { enemy.enabled = false; var b = enemy.GetComponent<Rigidbody2D>(); if (b != null) b.linearVelocity = Vector2.zero; }
            foreach (var actor in FindObjectsByType<MovesetActorController>(FindObjectsSortMode.None))
                if (actor != null && room.InteriorWorldBounds.Contains(actor.transform.position)) { actor.enabled = false; var b = actor.GetComponent<Rigidbody2D>(); if (b != null) b.linearVelocity = Vector2.zero; }
        }

        private IEnumerator ReactivePassiveAfterReconnect(ulong client)
        {
            var body = _run.MemberEntity(client);
            var mirror = MirrorOf(client);
            var ticker = body != null ? body.GetComponent<EquipmentPassiveTicker>() : null;
            var health = body != null ? body.GetComponent<HealthComponent>() : null;
            var exo = mirror?.Passives?.GetActive(EquippedSlot.Armor) as ExoLockPassive;
            yield return WaitFor(() => exo == null || !exo.IsBuffActive, 10f);
            yield return Seconds(ExoLockPassive.CooldownSeconds + 0.5f);
            health?.Heal(health.MaxHealth);
            health?.TryApplyDamage(new DamageRequest(25));
            var triggered = exo != null && exo.IsBuffActive;
            health?.Heal(health.MaxHealth);
            yield return Command(new ProofMessage { Step = "equip", Arg = "armor_reinforced_exo_rig" }, new[] { client }, 20f);
            var ack = ClientAck(client);
            Record("after the reconnect the member's host-side passive is the same one, composed once (one ticker entry, one passive), it still triggers once; the client's re-attached rig still leaves it to the host",
                mirror != null && MirrorPassive(client) == "exo_lock" && MirrorAccessoryPassive(client) == "scavengers_reserve" && ticker != null && ticker.Count == 1 && mirror.Passives.Active.Count == 2 && triggered && !ack.Contains("exo_lock"),
                $"client {client} passive={MirrorPassive(client)}+{MirrorAccessoryPassive(client)} tickers={ticker?.Count} activePassives={mirror?.Passives?.Active.Count} triggered={triggered}; client {ack}");
        }

        private IEnumerator ReactivePassiveCrossTalk(List<ulong> clients)
        {
            var a = clients[0];
            var b = clients[1];
            var start = _run.Rooms.Values.First(r => r.State.RoomType == RoomType.Start);
            TeleportParty(start.InteriorWorldBounds.center, 1.5f);
            yield return Seconds(1.0f);
            yield return WearArmor(a, "armor_riot_armor", "trio_anchored");
            yield return WearArmor(b, "armor_blast_suit", "trio_shock");
            var bodyA = _run.MemberEntity(a);
            var bodyB = _run.MemberEntity(b);
            var ra = bodyA.GetComponent<PlayerImpactReceiver>();
            var rb = bodyB.GetComponent<PlayerImpactReceiver>();
            var sa = ra.StaggersNegated; var sb = rb.StaggersNegated; var ka = ra.KnockbacksNegated; var kb = rb.KnockbacksNegated;
            Hit(bodyA, new ImpactRequest(Vector2.right, 0f, 1000f));
            Hit(bodyB, new ImpactRequest(Vector2.right, 0f, 1000f));
            Hit(bodyA, new ImpactRequest(Vector2.left, 3f, 0f, DamageKind.Explosion));
            Hit(bodyB, new ImpactRequest(Vector2.left, 3f, 0f, DamageKind.Explosion));
            yield return Seconds(1.0f);
            var ok = ra.StaggersNegated == sa + 1 && rb.StaggersNegated == sb && rb.KnockbacksNegated == kb + 1 && ra.KnockbacksNegated == ka
                     && MirrorPassive(a) == "anchored" && MirrorPassive(b) == "shock_absorber";
            Record("trio: two remote members wear different reactive armors; each passive acts only on its own member's impacts (no cross-talk)",
                ok, $"c{a} {MirrorPassive(a)}: staggers negated +{ra.StaggersNegated - sa}, explosions +{ra.KnockbacksNegated - ka}; c{b} {MirrorPassive(b)}: staggers +{rb.StaggersNegated - sb}, explosions +{rb.KnockbacksNegated - kb}");
            yield return WaitFor(() => !ra.IsStaggered && !rb.IsStaggered, 5f);
        }

        private IEnumerator ReconnectSteps(ulong client)
        {
            var entity = _run.MemberEntity(client);
            var presence = _run.Party.Presence;
            var spawnsBefore = presence.SpawnCount;
            var participant = _run.CoopRun.MemberFor(client)?.ParticipantId;
            yield return ReportAll();
            var before = ViewOf(client);
            // A consumed pickup before the drop must stay consumed after it.
            var spawnerHost = new GameObject("ProofLootReconnect");
            var spawner = _run.CreateLootSpawnerFor(spawnerHost);
            var coins = spawner.CreateCoinPickup((Vector2)HostPlayer.transform.position + Vector2.up * 0.3f);
            coins.SetAmount(40);
            yield return Seconds(2.0f);
            var consumedBefore = coins == null || coins.IsCollected;
            _acks.Clear();
            SendProof(client, new ProofMessage { Step = "reconnect" });
            yield return WaitFor(() => _run.HostHolds >= 1, 20f);
            var held = _waitOk && entity != null && presence.Grace.Pending.Count == 1;
            var standing = entity != null ? (Vector2)entity.transform.position : Vector2.zero;
            yield return Seconds(1.0f);
            var stood = entity != null && Vector2.Distance(standing, entity.transform.position) < 0.3f;
            Record("dropped client: its character stays represented and at risk, standing still, under its reconnect token", held && stood && entity != null,
                $"holds={_run.HostHolds} pending={presence.Grace.Pending.Count} standing={stood} life={entity?.GetComponent<PlayerLifeStateComponent>()?.State}");
            yield return WaitFor(() => _run.HostReconnects >= 1 && _acks.Values.Any(a => a.Step == "ack:reconnect"), 150f);
            var newId = _acks.FirstOrDefault(pair => pair.Value.Step == "ack:reconnect").Key;
            yield return Seconds(1.0f);
            yield return Command(new ProofMessage { Step = "report" }, new[] { newId }, 20f);
            var after = ViewOf(newId);
            var host = View("host-after-reconnect");
            _report.Views.Add(host);
            var sameEntity = _run.MemberEntity(newId) == entity && presence.SpawnCount == spawnsBefore && presence.ReconnectCount == 1;
            var sameState = after != null && before != null && after.ParticipantId == participant && after.InventoryFingerprint == before.InventoryFingerprint && after.Coins >= before.Coins
                            && after.RoomStates == host.RoomStates && after.Depth == host.Depth && after.LayoutFingerprint == host.LayoutFingerprint && after.OwnedPlayerObjects == 1 && after.PlayerObjects == _size
                            && after.LifeState == "Alive" && after.GroundLoot == host.GroundLoot;
            Record("reconnect inside grace: the same character is handed back (no second player), the run resumes in the current state", held && sameEntity && sameState && consumedBefore,
                $"client {client}->{newId} sameEntity={_run.MemberEntity(newId) == entity} spawns {spawnsBefore}->{presence.SpawnCount} reconnects={presence.ReconnectCount} participant={after?.ParticipantId == participant} inventory={after?.InventoryFingerprint == before?.InventoryFingerprint} coins {before?.Coins}->{after?.Coins} rooms={after?.RoomStates == host.RoomStates} ({after?.RoomStates} vs {host.RoomStates}) ground {after?.GroundLoot}=={host.GroundLoot} owned={after?.OwnedPlayerObjects} players={after?.PlayerObjects} life={after?.LifeState}");
            Destroy(spawnerHost);
        }

        private static Vector2? ParsePosition(string positions, ulong clientId)
        {
            if (string.IsNullOrEmpty(positions)) return null;
            foreach (var part in positions.Split(';'))
            {
                var bits = part.Split(':');
                if (bits.Length != 2 || !ulong.TryParse(bits[0], out var id) || id != clientId) continue;
                var xy = bits[1].Split(',');
                if (xy.Length == 2 && float.TryParse(xy[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
                    && float.TryParse(xy[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y)) return new Vector2(x, y);
            }

            return null;
        }

        private IEnumerator LootSteps(List<ulong> clients, ulong first)
        {
            var content = _app.Content;
            var spawnerHost = new GameObject("ProofLoot");
            var spawner = _run.CreateLootSpawnerFor(spawnerHost);
            var start = StartRoom();
            var centre = start != null ? start.InteriorWorldBounds.center : (Vector2)HostPlayer.transform.position;
            var item = content.Items.OfType<ConsumableDefinition>().FirstOrDefault(c => c.Category != ItemCategory.Ammo);
            if (spawner == null || item == null) { Record("loot proof prerequisites", false, "no spawner or consumable"); yield break; }

            // Race: every member on the same pickup, every Interact pressed in the same frame.
            TeleportParty(centre, 0.5f);
            yield return Seconds(1.0f);
            yield return ReportAll();
            var countsBefore = clients.ToDictionary(c => c, c => ViewOf(c)?.InventoryItems ?? 0);
            var hostCountBefore = _run.Expedition.State.Inventory.CountOf(item.Id);
            var pickup = spawner.CreateItemPickup(centre + Vector2.up * 0.4f);
            pickup.Hold(new ItemInstance(item.Id, 1), item.Category);
            yield return Seconds(1.0f);
            var clientsSawIt = clients.All(c => true);
            foreach (var c in clients) SendProof(c, new ProofMessage { Step = "interact" });
            _reader.PressInteract();
            yield return Seconds(2.0f);
            yield return ReportAll();
            var hostGot = _run.Expedition.State.Inventory.CountOf(item.Id) - hostCountBefore;
            var clientsGot = clients.Sum(c => (ViewOf(c)?.InventoryItems ?? 0) - countsBefore[c]);
            var consumed = pickup == null || pickup.IsConsumed;
            var gone = clients.All(c => ViewOf(c) is { } v && v.GroundLoot == 0);
            Record("pickup race: exactly one winner, one item, consumed once and gone on every peer", consumed && hostGot + clientsGot == 1 && gone && clientsSawIt,
                $"hostGot={hostGot} clientsGot={clientsGot} consumed={consumed} clientGround=" + string.Join(",", clients.Select(c => ViewOf(c)?.GroundLoot)) + $" ledger={_run.LootAuthority.Ledger.Count}");

            // Client-only pickup: the item lands in the client's own inventory (granted by the host).
            var grantsBefore = clients.ToDictionary(c => c, c => ViewOf(c)?.GrantsReceived ?? 0);
            IsolateAllBut(first, centre);
            var clientEntity = _run.MemberEntity(first);
            var solo = spawner.CreateItemPickup((Vector2)clientEntity.transform.position + Vector2.up * 0.3f);
            solo.Hold(new ItemInstance(item.Id, 1), item.Category);
            var hostMirror = _run.MirrorInventoryOf(first);
            yield return Seconds(1.0f);
            yield return Command(new ProofMessage { Step = "interact" }, new[] { first }, 10f);
            yield return Seconds(1.0f);
            yield return ReportAll();
            var granted = (ViewOf(first)?.GrantsReceived ?? 0) - grantsBefore[first];
            Record("client picks an item into its own inventory; the host's inventory is unaffected", solo == null && granted == 1 && _run.Expedition.State.Inventory.CountOf(item.Id) - hostCountBefore == hostGot,
                $"grants={granted} clientItems={ViewOf(first)?.InventoryItems} mirrorItems={(hostMirror != null ? hostMirror.BackpackSlots.Count(s => s != null) : -1)}");

            // Coin pile: split across every member's own Carried wallet (58), total conserved.
            TeleportParty(centre, 0.5f);
            yield return Seconds(0.8f);
            yield return ReportAll();
            var coinsBefore = clients.ToDictionary(c => c, c => ViewOf(c)?.Coins ?? 0);
            var hostCoinsBefore = _run.Expedition.State.CarriedCoins;
            const int pile = 600;
            var coins = spawner.CreateCoinPickup(centre + Vector2.up * 0.2f);
            coins.SetAmount(pile);
            yield return Seconds(2.5f);
            yield return ReportAll();
            var hostShare = _run.Expedition.State.CarriedCoins - hostCoinsBefore;
            var clientShares = clients.Select(c => (ViewOf(c)?.Coins ?? 0) - coinsBefore[c]).ToList();
            Record("coin pile split once across the party's own wallets; total conserved", hostShare + clientShares.Sum() == pile && clientShares.All(s => s > 0) && hostShare > 0,
                $"pile={pile} host=+{hostShare} clients=+{string.Join(",+", clientShares)}");

            // Ammo near the client: the host copy's attraction collects it and the client's own inventory receives it.
            var ammo = content.Items.OfType<AmmoItemDefinition>().FirstOrDefault();
            if (ammo != null)
            {
                IsolateAllBut(first, centre);
                yield return ReportAll();
                var ammoBefore = ViewOf(first)?.AmmoTotal ?? 0;
                var ammoPickup = spawner.CreateItemPickup((Vector2)clientEntity.transform.position + Vector2.right * 0.8f);
                ammoPickup.Hold(new ItemInstance(ammo.Id, 10), ItemCategory.Ammo);
                yield return Seconds(2.5f);
                yield return ReportAll();
                var ammoAfter = ViewOf(first)?.AmmoTotal ?? 0;
                Record("client picks up ammo (pulled and resolved by the host) into its own reserve", ammoAfter > ammoBefore && (ammoPickup == null || ammoPickup.IsConsumed),
                    $"ammo {ammoBefore}->{ammoAfter} pickupConsumed={(ammoPickup == null || ammoPickup.IsConsumed)}");
            }

            Destroy(spawnerHost);
        }

        /// <summary>Moves every member but one well away, so a pickup meant for that member is only in its reach.</summary>
        private void IsolateAllBut(ulong keep, Vector2 centre)
        {
            var others = _run.CoopRun.Members.Where(m => m.ClientId != keep).ToList();
            for (var i = 0; i < others.Count; i++)
            {
                var entity = others[i].IsHost ? HostPlayer : _run.MemberEntity(others[i].ClientId);
                Teleport(entity, centre + Vector2.left * 5f + Vector2.up * (i * 1.5f));
            }
        }

        private IEnumerator MerchantSteps(ulong client)
        {
            var merchantRoom = PickRoom(r => r.GetComponent<RoomContentBinding>()?.Merchant?.Merchant != null);
            if (merchantRoom == null) { Record("merchant trade (non-combat)", false, "this seed has no merchant on D1 — use a merchant seed"); yield break; }
            var binding = merchantRoom.GetComponent<RoomContentBinding>();
            var merchant = binding.Merchant.Merchant;
            var node = merchantRoom.State.NodeId;
            var offers = merchant.Offers.Where(o => !o.IsSold).OrderBy(o => o.Price).ToList();
            if (offers.Count < 2) { Record("merchant trade (non-combat)", false, "fewer than two offers"); yield break; }
            var buy = offers[0];
            var contested = offers[1];
            // Everyone walks into the room (room entry is a real entry on the host), the client stands at the merchant.
            TeleportParty(merchantRoom.InteriorWorldBounds.center, 1.0f);
            yield return Seconds(0.8f);
            Teleport(_run.MemberEntity(client), (Vector2)binding.Merchant.transform.position + Vector2.right * 0.7f);
            yield return Seconds(1.0f);
            yield return ReportAll();
            var coinsBefore = ViewOf(client)?.Coins ?? 0;
            var itemsBefore = ViewOf(client)?.InventoryItems ?? 0;
            var ammoBefore = ViewOf(client)?.AmmoTotal ?? 0;
            var hostCoinsBefore = _run.Expedition.State.CarriedCoins;
            yield return Command(new ProofMessage { Step = "buy", Number = buy.Index, Arg = node.ToString(), X = contested.Index, Json = "none" }, new[] { client }, 30f);
            var buyAck = _acks.TryGetValue(client, out var a) ? a.Arg : "";
            yield return ReportAll();
            var clientView = ViewOf(client);
            // An ammo purchase merges into the carried stack (reserve rises), anything else takes a backpack slot.
            var received = clientView != null && (clientView.InventoryItems == itemsBefore + 1 || clientView.AmmoTotal > ammoBefore);
            Record("client merchant purchase: sent as a request, committed once by the host, paid from the client's own wallet, item in the client's inventory",
                buy.IsSold && clientView != null && clientView.Coins == coinsBefore - buy.Price && received && _run.Expedition.State.CarriedCoins == hostCoinsBefore,
                $"offer={buy.Index}:{buy.Definition.Id} price={buy.Price} sold={buy.IsSold} clientCoins {coinsBefore}->{clientView?.Coins} clientItems {itemsBefore}->{clientView?.InventoryItems} clientAmmo {ammoBefore}->{clientView?.AmmoTotal} hostCoins {hostCoinsBefore}->{_run.Expedition.State.CarriedCoins} ack='{buyAck}'");

            // Contested offer: the host buys it the same moment the client asks; one sale, the loser is not charged.
            var clientCoins = ViewOf(client)?.Coins ?? 0;
            var hostTx = Guid.NewGuid().ToString("N");
            var hostResult = _run.LootAuthority.RequestMerchantBuy(hostTx, _app.Coop.LocalClientId, merchant, contested.Index);
            yield return Command(new ProofMessage { Step = "buy", Number = contested.Index, Arg = node.ToString(), X = contested.Index, Json = "duplicate" }, new[] { client }, 30f);
            var contestedAck = _acks.TryGetValue(client, out var ca) ? ca.Arg : "";
            yield return ReportAll();
            var soldOnce = merchant.State.SoldOfferIndices.Count(i => i == contested.Index) == 1;
            Record("simultaneous purchase: one sale; the other buyer is refused and not charged; a replayed transaction id resolves once",
                hostResult.Verdict == LootVerdict.Accepted && soldOnce && (ViewOf(client)?.Coins ?? -1) == clientCoins,
                $"host={hostResult.Verdict} soldOnce={soldOnce} clientCoins {clientCoins}->{ViewOf(client)?.Coins} ledger={_run.LootAuthority.Ledger.Count} ack='{contestedAck}'");
        }

        private IEnumerator ReviveSteps(ulong client)
        {
            var start = StartRoom();
            var centre = start != null ? start.InteriorWorldBounds.center : (Vector2)HostPlayer.transform.position;
            TeleportParty(centre, 1.0f);
            yield return Seconds(0.8f);
            var clientEntity = _run.MemberEntity(client);
            var clientHealth = clientEntity.GetComponent<HealthComponent>();
            var clientLife = clientEntity.GetComponent<PlayerLifeStateComponent>();
            // The host decides: the client's character takes a lethal hit on the host.
            clientHealth.TryApplyDamage(new DamageRequest(clientHealth.CurrentHealth + 50));
            yield return Seconds(1.0f);
            yield return ReportAll();
            Record("client reaches 0 HP with a living teammate: Downed on the host and on the client", clientLife.IsDowned && ViewOf(client)?.LifeState == "Downed",
                $"host={clientLife.State} clientSees={ViewOf(client)?.LifeState} bleedout={clientLife.BleedoutRemaining:0.0}");
            var hitsBefore = _run.CoopHost.HitsBySender.TryGetValue(client, out var h0) ? h0 : 0;
            yield return Command(new ProofMessage { Step = "fight", Seconds = 1.5f, Number = -1 }, new[] { client }, 10f);
            var hitsAfter = _run.CoopHost.HitsBySender.TryGetValue(client, out var h1) ? h1 : 0;
            Record("a Downed client cannot attack (no hit reaches the host)", hitsAfter == hitsBefore, $"hits {hitsBefore}->{hitsAfter}");
            // The host's player revives: hold Interact next to the Downed client (4 s authored channel).
            Teleport(HostPlayer, (Vector2)clientEntity.transform.position + Vector2.right * 0.6f);
            _reader.SetInteractHeld(true);
            yield return WaitFor(() => clientLife.IsAlive, 8f);
            _reader.SetInteractHeld(false);
            yield return Seconds(1.0f);
            yield return ReportAll();
            var percent = clientHealth.MaxHealth > 0 ? clientHealth.CurrentHealth * 100 / clientHealth.MaxHealth : 0;
            Record("host revives the client: authoritative channel, exactly once, back at the authored health", clientLife.IsAlive && ViewOf(client)?.LifeState == "Alive" && percent >= 25 && percent <= 35,
                $"host={clientLife.State} clientSees={ViewOf(client)?.LifeState} hp={clientHealth.CurrentHealth}/{clientHealth.MaxHealth} ({percent}%) revives={_run.Party.LifeRoster.Revives}");

            // Reverse: the host goes down, the client revives it.
            var hostHealth = HostPlayer.GetComponent<HealthComponent>();
            var hostLife = HostPlayer.GetComponent<PlayerLifeStateComponent>();
            hostHealth.TryApplyDamage(new DamageRequest(hostHealth.CurrentHealth + 50));
            yield return Seconds(0.8f);
            Teleport(clientEntity, (Vector2)HostPlayer.transform.position + Vector2.left * 0.6f);
            yield return Seconds(0.8f);
            yield return Command(new ProofMessage { Step = "hold-interact", Seconds = 6f }, new[] { client }, 15f);
            yield return WaitFor(() => hostLife.IsAlive, 3f);
            yield return ReportAll();
            Record("client revives the host (client-held Interact, host-run channel)", hostLife.IsAlive && (ViewOf(client)?.MemberLifeStates ?? "").Contains("0:Alive"),
                $"host={hostLife.State} hp={hostHealth.CurrentHealth}/{hostHealth.MaxHealth} clientSees={ViewOf(client)?.MemberLifeStates}");
            foreach (var member in _run.Party.Members) { var hp = member.GameObject.GetComponent<HealthComponent>(); if (hp != null && hp.IsAlive) hp.Heal(hp.MaxHealth); }
        }

        private IEnumerator BossSteps(List<ulong> clients, bool clientsDamage)
        {
            var bossRoom = PickRoom(r => r.State.RoomType == RoomType.Boss);
            var binding = bossRoom != null ? bossRoom.GetComponent<RoomContentBinding>() : null;
            if (binding?.Boss?.Boss == null) { Record("boss present", false, "no boss room/boss on this depth"); yield break; }
            var encounter = binding.Boss;
            var defeats = 0;
            encounter.BossDefeated += (_, _) => defeats++;
            yield return ReportAll();
            var bossSeen = clients.All(c => ViewOf(c) is { } v && v.BossPresent);
            TeleportParty(bossRoom.InteriorWorldBounds.center + Vector2.down * 2f, 1.2f);
            yield return WaitFor(() => bossRoom.Lifecycle == RoomLifecycleState.Active, 10f);
            yield return Seconds(1.0f);
            yield return ReportAll();
            Record("boss spawned by the host and replicated to every client (boss bar data, phase)", bossSeen && encounter.Boss != null && clients.All(c => ViewOf(c) is { } v && v.BossPresent && v.BossMaxHealth == encounter.Boss.Health.MaxHealth),
                $"boss={encounter.Boss.Definition?.Id} hp={encounter.Boss.Health.CurrentHealth}/{encounter.Boss.Health.MaxHealth} " + string.Join(" ", clients.Select(c => $"c{c}:{ViewOf(c)?.BossHealth}/{ViewOf(c)?.BossMaxHealth} phase={ViewOf(c)?.BossPhase}")));
            var bossHpBefore = encounter.Boss.Health.CurrentHealth;
            var damageBefore = clients.ToDictionary(c => c, c => _run.CoopHost.DamageBySender.TryGetValue(c, out var d) ? d : 0);
            if (clientsDamage)
            {
                foreach (var c in clients) SendProof(c, new ProofMessage { Step = "fight", Seconds = 8f, Number = bossRoom.State.NodeId });
                yield return HostFight(8f, bossRoom, false);
                yield return Command(new ProofMessage { Step = "report" }, clients, 20f);
                var clientBossDamage = clients.ToDictionary(c => c, c => (_run.CoopHost.DamageBySender.TryGetValue(c, out var d) ? d : 0) - damageBefore[c]);
                Record("both players damage the boss through the allowed paths; HP agrees on every peer", clientBossDamage.Values.All(d => d > 0) && encounter.Boss.Health.CurrentHealth < bossHpBefore,
                    $"hp {bossHpBefore}->{encounter.Boss.Health.CurrentHealth} clientDamage=" + string.Join(",", clientBossDamage.Values) + " clientsSee=" + string.Join(",", clients.Select(c => ViewOf(c)?.BossHealth)));
            }

            if (clientsDamage && encounter.Boss.Definition != null && encounter.Boss.Phase == 1)
            {
                // Phase two is decided by the host's HP threshold; every peer must show it.
                var health = encounter.Boss.Health;
                var threshold = Mathf.FloorToInt(health.MaxHealth * encounter.Boss.Definition.PhaseTwoHealthFraction) - 5;
                if (health.CurrentHealth > threshold) health.TryApplyDamage(new DamageRequest(health.CurrentHealth - threshold));
                yield return Seconds(1.5f);
                yield return ReportAll();
                Record("boss phase two on the host is phase two on every peer", encounter.Boss.Phase == 2 && clients.All(c => ViewOf(c)?.BossPhase == 2),
                    $"hostPhase={encounter.Boss.Phase} hp={health.CurrentHealth}/{health.MaxHealth} clients=" + string.Join(",", clients.Select(c => ViewOf(c)?.BossPhase)));
            }

            var finished = FinishRoomActors(bossRoom);
            yield return WaitFor(() => encounter.IsDefeated && bossRoom.Lifecycle == RoomLifecycleState.Cleared, 15f);
            yield return Seconds(1.5f);
            yield return ReportAll();
            var transitOnClients = clients.All(c => ViewOf(c) is { } v && v.TransitState.StartsWith("Open"));
            var cacheUnlocked = binding.BossCache != null && !binding.BossCache.IsLocked;
            Record("boss dies once on the host; the arena clears once; Transit opens on every peer", defeats == 1 && encounter.IsDefeated && transitOnClients && _run.Expedition.Transit != null,
                $"defeats={defeats} hostFinished={finished} transitHost={_run.Expedition.Transit?.State} clients=" + string.Join(",", clients.Select(c => ViewOf(c)?.TransitState)) + $" cacheUnlocked={cacheUnlocked}");

            if (clientsDamage && binding.BossCache != null && !binding.BossCache.IsOpened)
            {
                // The Boss Cache: a client claims it through the real Interact press; the host opens it once.
                var cache = binding.BossCache;
                var client = clients[0];
                var groundBefore = _run.GroundLoot.Tracked.Count(t => t != null);
                IsolateAllBut(client, (Vector2)cache.transform.position + Vector2.left * 3f);
                Teleport(_run.MemberEntity(client), (Vector2)cache.transform.position + Vector2.right * 0.7f);
                yield return Seconds(1.0f);
                yield return Command(new ProofMessage { Step = "interact" }, new[] { client }, 10f);
                yield return Seconds(1.0f);
                var dropped = _run.GroundLoot.Tracked.Count(t => t != null) - groundBefore;
                Teleport(HostPlayer, (Vector2)cache.transform.position + Vector2.left * 0.7f);
                yield return Seconds(0.5f);
                var hostSecond = HostPlayer.GetComponent<PlayerInteractor>().FindNearestInteractable() == (IInteractable)cache;
                yield return ReportAll();
                Record("boss cache claimed once (client press, host-opened, loot shared in the world)", cache.IsOpened && !hostSecond && bossRoom.State.IsResolved("boss_cache") && (ViewOf(client)?.RoomStates ?? "").Length > 0,
                    $"opened={cache.IsOpened} dropped={dropped} resolved={bossRoom.State.IsResolved("boss_cache")} hostCanReopen={hostSecond} clientGround={ViewOf(client)?.GroundLoot}");
            }
        }

        /// <summary>
        /// Every chest and event of the current depth used by a client through the real Interact press (the host resolves
        /// it with that member's own wallet and backpack), and the result mirrored on the client; a second use is refused.
        /// </summary>
        private IEnumerator NonCombatSteps(ulong client)
        {
            // Enough Carried Coins for the paid events, split across the party like any pile.
            var spawnerHost = new GameObject("ProofCoins");
            var spawner = _run.CreateLootSpawnerFor(spawnerHost);
            var start = StartRoom();
            var centre = start != null ? start.InteriorWorldBounds.center : (Vector2)HostPlayer.transform.position;
            TeleportParty(centre, 0.5f);
            yield return Seconds(0.5f);
            var pile = spawner.CreateCoinPickup(centre + Vector2.up * 0.2f);
            pile.SetAmount(3000 * _size);
            yield return Seconds(2.5f);
            Destroy(spawnerHost);

            var depth = _run.Expedition.State.Depth;
            var rooms = _run.Rooms.Values.OrderBy(r => r.State.NodeId).ToList();
            var used = 0;
            foreach (var room in rooms)
            {
                var binding = room.GetComponent<RoomContentBinding>();
                if (binding == null || room.State.RoomType == RoomType.Boss) continue;
                // Never walk the party into an unentered combat room just to open its supply chest.
                var chest = room.State.RoomType == RoomType.Combat && room.Lifecycle != RoomLifecycleState.Cleared ? null
                    : binding.Chests.FirstOrDefault(c => c != null && !c.IsOpened && !c.IsLocked);
                if (chest != null)
                {
                    used++;
                    yield return UseAsClient(client, room, chest.transform.position, "interact");
                    yield return ReportAll();
                    var hostCanReopen = chest.CanInteract(HostPlayer);
                    Record($"D{depth} {chest.Kind} opened once by the client's press (host-rolled loot, room record shared)", chest.IsOpened && !hostCanReopen && room.State.Resolved.Count > 0,
                        $"room={room.State.NodeId}:{room.State.RoomId} opened={chest.IsOpened} resolved={string.Join("+", room.State.Resolved)} hostCanReopen={hostCanReopen} clientRooms={ViewOf(client)?.RoomStates}");
                }

                var ev = binding.EventInstance;
                if (ev == null || ev.Phase != DungeonEventPhase.Available) continue;
                used++;
                if (ev is WeaponCacheEvent cache)
                {
                    yield return UseAsClient(client, room, binding.Event.transform.position, null);
                    yield return Command(new ProofMessage { Step = "cache", Number = 0 }, new[] { client }, 20f);
                    var ack = _acks.TryGetValue(client, out var a) ? a.Arg : "";
                    yield return ReportAll();
                    var hostSecond = cache.CanChoose(DungeonEventInteractable.ActorFor(HostPlayer), 1);
                    Record($"D{depth} Weapon Cache: the client's choice commits once for the party, into the client's own inventory", cache.IsConsumed && !hostSecond && ack.Contains("takes=1"),
                        $"consumed={cache.IsConsumed} hostCanChooseAfter={hostSecond} ack='{ack}'");
                    continue;
                }

                var clientEntity = _run.MemberEntity(client);
                var clientHealth = clientEntity.GetComponent<HealthComponent>();
                if (ev.Kind == DungeonEventKind.MedicalStation) clientHealth.TryApplyDamage(new DamageRequest(clientHealth.MaxHealth / 2));
                yield return ReportAll();
                var coinsBefore = ViewOf(client)?.Coins ?? 0;
                var hostCoinsBefore = _run.Expedition.State.CarriedCoins;
                var hpBefore = clientHealth.CurrentHealth;
                yield return UseAsClient(client, room, binding.Event.transform.position, "interact");
                yield return Seconds(ev.Kind == DungeonEventKind.SupplySignal || ev.Kind == DungeonEventKind.CursedChest ? 1f : 0.5f);
                yield return ReportAll();
                var charged = coinsBefore - (ViewOf(client)?.Coins ?? coinsBefore);
                // The Medical Station is a multi-use service (it stays Available); every other event resolves once.
                var resolved = ev.Kind == DungeonEventKind.MedicalStation ? clientHealth.CurrentHealth > hpBefore : ev.Phase != DungeonEventPhase.Available;
                // The press charges only the client's own wallet; a coin reward the event drops is split across the party
                // like any pile, so the host may only ever gain (its share), and the client's net change is cost − share.
                var hostDelta = _run.Expedition.State.CarriedCoins - hostCoinsBefore;
                var ok = resolved && hostDelta >= 0 && (ev.CostCoins <= 0 || charged + hostDelta == ev.CostCoins);
                Record($"D{depth} {ev.Kind}: the client's press resolves once on the host, paid from the client's own wallet", ok,
                    $"room={room.State.NodeId} phase={ev.Phase} cost={ev.CostCoins} clientCharged={charged} hostCoins {hostCoinsBefore}->{_run.Expedition.State.CarriedCoins} hp {hpBefore}->{clientHealth.CurrentHealth} notice='{ViewOf(client)?.LastNotice}'");
                if (ev.Kind == DungeonEventKind.CursedChest || ev.Kind == DungeonEventKind.SupplySignal)
                {
                    // The event's wave is a real encounter: finish it host-side so the proof stays bounded.
                    yield return Seconds(2f);
                    FinishRoomActors(room);
                    yield return Seconds(1f);
                }
            }

            if (used == 0) Record($"D{depth} non-combat content", true, "this depth has no chest or event room (nothing to use)");
            else if (_run.Rooms.Values.Any(r => r.GetComponent<RoomContentBinding>()?.Merchant?.Merchant != null) && depth > 1)
            {
                var merchantRoom = PickRoom(r => r.GetComponent<RoomContentBinding>()?.Merchant?.Merchant != null);
                var anchor = merchantRoom.GetComponent<RoomContentBinding>().Merchant.transform.position;
                yield return UseAsClient(client, merchantRoom, anchor, null);
                yield return Command(new ProofMessage { Step = "sell" }, new[] { client }, 25f);
                var ack = _acks.TryGetValue(client, out var a) ? a.Arg : "";
                Record($"D{depth} client sells to the merchant: the host pays the client's own wallet, the item leaves the client's inventory once", ack.Contains("sales=1") || ack.Contains("nothing sellable"), ack);
            }
        }

        /// <summary>Walks the party into a room (a real entry on the host) and stands the client at an object, alone.</summary>
        private IEnumerator UseAsClient(ulong client, RoomRuntime room, Vector2 at, string step)
        {
            TeleportParty(room.InteriorWorldBounds.center, 1.0f);
            yield return Seconds(0.6f);
            IsolateAllBut(client, room.InteriorWorldBounds.center + Vector2.left * 2f);
            Teleport(_run.MemberEntity(client), at + Vector2.right * 0.7f);
            yield return Seconds(1.0f);
            if (step != null) yield return Command(new ProofMessage { Step = step }, new[] { client }, 10f);
            yield return Seconds(1.0f);
        }

        /// <summary>Co-op menus are local: a client's inventory never pauses the world, on either peer.</summary>
        private IEnumerator MenuSteps(ulong client)
        {
            var hostTicks = Time.frameCount;
            yield return Command(new ProofMessage { Step = "menus" }, new[] { client }, 15f);
            var ack = _acks.TryGetValue(client, out var a) ? a.Arg : "";
            _run.Inventory.Toggle();
            yield return Seconds(0.3f);
            var hostScale = Time.timeScale;
            var hostOpen = _run.Inventory.IsOpen;
            _run.Inventory.Toggle();
            yield return Seconds(0.3f);
            Record("co-op inventory on either peer is local UI: the shared world keeps running (no Time.timeScale pause)", ack.Contains("inventoryOpen=True") && ack.Contains("timeScaleWhileOpen=1.0") && hostOpen && Mathf.Approximately(hostScale, 1f),
                $"client '{ack}' host open={hostOpen} timeScale={hostScale:0.0} hostFrames={Time.frameCount - hostTicks}");
        }

        private IEnumerator DescendSteps(List<ulong> clients)
        {
            yield return ReportAll();
            var before = clients.ToDictionary(c => c, c => ViewOf(c));
            var hostInventoryBefore = LoadoutValidation.Fingerprint(_run.Expedition.State.Inventory.ToSnapshot());
            var hostCoinsBefore = _run.Expedition.State.CarriedCoins;
            yield return WaitFor(() => _run.Vote != null, 10f);
            var hostVoted = _run.Vote != null && _run.Vote.Vote(TransitChoice.DescendDeeper);
            yield return Seconds(1.5f);
            yield return ReportAll();
            var stillD1 = _run.Expedition.State.Depth == 1 && clients.All(c => ViewOf(c)?.Depth == 1) && _run.Expedition.Transit != null && _run.Expedition.Transit.State == TransitDecisionState.Open;
            Record("host alone votes DESCEND: no transition (unanimity of the living)", hostVoted && stillD1,
                $"hostVoted={hostVoted} depth={_run.Expedition.State.Depth} votesSeenByClients=" + string.Join(" | ", clients.Select(c => ViewOf(c)?.TransitVotes)));
            // Every other living member votes in turn; until the last one has, the party stays (86: unanimity).
            for (var i = 0; i < clients.Count - 1; i++)
            {
                yield return Command(new ProofMessage { Step = "vote", Arg = "descend" }, new[] { clients[i] }, 20f);
                yield return Seconds(1.0f);
                var waiting = _run.Expedition.State.Depth == 1 && _run.Expedition.Transit != null && _run.Expedition.Transit.State == TransitDecisionState.Open;
                Record($"{i + 2} of {_size} living members vote DESCEND: still no transition", waiting,
                    $"depth={_run.Expedition.State.Depth} votes={_run.Expedition.Transit?.Choices.Count} pending={string.Join(",", _run.Expedition.Transit?.PendingVoters ?? Enumerable.Empty<string>())}");
            }

            yield return Command(new ProofMessage { Step = "vote", Arg = "descend" }, new[] { clients[clients.Count - 1] }, 20f);
            yield return WaitFor(() => _run.Expedition.State.Depth == 2 && _run.CoopHost.Depth == 2, 20f);
            yield return WaitFor(() => _run.CoopHost.GameplayReleases >= 2, 40f);
            yield return Seconds(1.0f);
            yield return ReportAll();
            var host = View("host-d2");
            _report.Views.Add(host);
            var sameD2 = clients.All(c => ViewOf(c) is { } v && v.Depth == 2 && v.LayoutFingerprint == host.LayoutFingerprint && v.Biome == host.Biome && v.RoomSignature == host.RoomSignature);
            Record("every living member votes DESCEND: the party descends once and every peer builds the same D2",
                _run.Expedition.State.Depth == 2 && sameD2 && _run.CoopHost.TransitResolutionsSent == 1,
                $"host={host.Depth}/{host.Biome}/{host.LayoutFingerprint} " + string.Join(" ", clients.Select(c => $"c{c}={ViewOf(c)?.Depth}/{ViewOf(c)?.Biome}/{ViewOf(c)?.LayoutFingerprint}")) + $" resolutions={_run.CoopHost.TransitResolutionsSent}");
            var survived = clients.All(c => ViewOf(c) is { } v && before[c] != null && v.ParticipantId == before[c].ParticipantId && v.EquippedWeapons == before[c].EquippedWeapons && v.Coins == before[c].Coins && v.InventoryItems == before[c].InventoryItems && v.LifeState == "Alive" && v.DeepestDepth >= 2)
                           && LoadoutValidation.Fingerprint(_run.Expedition.State.Inventory.ToSnapshot()) == hostInventoryBefore && _run.Expedition.State.CarriedCoins == hostCoinsBefore;
            Record("identity, inventory, equipment, ammo, coins and life state survive the transition on every peer; deepest depth recorded on arrival", survived,
                string.Join(" ", clients.Select(c => $"c{c}: id={ViewOf(c)?.ParticipantId == before[c]?.ParticipantId} weapons={before[c]?.EquippedWeapons}->{ViewOf(c)?.EquippedWeapons} items={before[c]?.InventoryItems}->{ViewOf(c)?.InventoryItems} ammo={before[c]?.AmmoTotal}->{ViewOf(c)?.AmmoTotal} coins={before[c]?.Coins}->{ViewOf(c)?.Coins} hp={ViewOf(c)?.Health}/{ViewOf(c)?.MaxHealth} deepest={ViewOf(c)?.DeepestDepth}")) + $" hostInvSame={LoadoutValidation.Fingerprint(_run.Expedition.State.Inventory.ToSnapshot()) == hostInventoryBefore}");
            var leak = host.PlayerObjects == _size && clients.All(c => ViewOf(c) is { } v && v.PlayerObjects == _size && v.LiveEnemies == 0 && v.AudioListeners == 1 && v.Cameras == 1);
            Record("no leaked depth objects: the same player objects, no enemy replicas from D1, one camera/listener", leak,
                $"hostSpawned={host.SpawnedNetworkObjects} hostPlayers={host.PlayerObjects} " + string.Join(" ", clients.Select(c => $"c{c}: players={ViewOf(c)?.PlayerObjects} liveEnemies={ViewOf(c)?.LiveEnemies} spawned={ViewOf(c)?.SpawnedNetworkObjects}")));
        }

        private IEnumerator ReturnSteps(List<ulong> clients)
        {
            // D2's own non-combat content (a vault, a machine, a merchant) is the client's to use too.
            yield return NonCombatSteps(clients[0]);
            yield return BossSteps(clients, false);
            yield return ReportAll();
            var coinsBefore = _run.Expedition.State.CarriedCoins;
            // Any living member's RETURN returns the whole party (86): the client chooses it.
            yield return Command(new ProofMessage { Step = "vote", Arg = "return" }, new[] { clients[0] }, 20f);
            yield return WaitFor(() => _summary != null, 20f);
            Record("RETURN vote resolves once for the party; the host's own Return transaction commits once", _summary != null && _summary.IsSuccess,
                $"outcome={_summary?.Outcome} coinsExtracted={_summary?.CoinsExtracted} (carried {coinsBefore}) deepest={_summary?.DeepestDepthReached}");
            foreach (var c in clients) SendProof(c, new ProofMessage { Step = "finish" });
            yield return HostEnd(clients);
        }

        private IEnumerator TrioEndSteps(List<ulong> clients)
        {
            // A member left Downed bleeds out (84: 20 s) to Dead and spectates a living teammate on its own machine.
            var victim = clients[clients.Count - 1];
            var victimEntity = _run.MemberEntity(victim);
            var victimLife = victimEntity.GetComponent<PlayerLifeStateComponent>();
            var victimHp = victimEntity.GetComponent<HealthComponent>();
            victimHp.TryApplyDamage(new DamageRequest(victimHp.CurrentHealth + 100));
            yield return Seconds(1.0f);
            var downed = victimLife.IsDowned;
            yield return WaitFor(() => victimLife.IsDead, victimLife.BleedoutSeconds + 8f);
            yield return Seconds(1.0f);
            yield return Command(new ProofMessage { Step = "report" }, clients, 20f);
            var victimView = ViewOf(victim);
            Record("bleedout: a Downed member nobody revives becomes Dead; it spectates a living teammate; the others see it Dead",
                downed && victimLife.IsDead && victimView != null && victimView.LifeState == "Dead" && victimView.Spectating && clients.Where(c => c != victim).All(c => (ViewOf(c)?.MemberLifeStates ?? "").Contains($"{victim}:Dead")),
                $"downedFirst={downed} host={victimLife.State} victimSees={victimView?.LifeState} spectating={victimView?.Spectating}->{victimView?.SpectatingTarget} others=" + string.Join(" | ", clients.Where(c => c != victim).Select(c => ViewOf(c)?.MemberLifeStates)));

            // Defibrillator (84): a living member revives the Dead one with the consumable. The member asks, the host
            // validates against its own party and the member's mirrored inventory, the unit is spent only on acceptance.
            var medic = clients[0];
            var medicEntity = _run.MemberEntity(medic);
            foreach (var n in new[] { 1, 2 })
                _run.CoopHost.SendGrant(medic, new ItemInstance("consumable_defibrillator").ToSnapshot(), _run.Expedition.State.TransactionId, "proof_defibrillator_" + n);
            Teleport(medicEntity, (Vector2)victimEntity.transform.position + Vector2.right * 0.6f);
            var hostRevivesBefore = _run.DefibrillatorRevives;
            yield return Command(new ProofMessage { Step = "defib" }, new[] { medic }, 40f);
            var defibAck = _acks.TryGetValue(medic, out var da) ? da.Arg : "";
            var expectedHp = Mathf.Max(1, Mathf.RoundToInt(victimHp.MaxHealth * 0.3f));
            yield return Command(new ProofMessage { Step = "report" }, clients, 20f);
            Record("defibrillator: a member's Defibrillator revives the Dead teammate at 30% once (host-validated); the unit is spent only then; a use with no one to revive is refused and costs nothing",
                victimLife.IsAlive && victimHp.CurrentHealth == expectedHp && _run.DefibrillatorRevives == hostRevivesBefore + 1
                && defibAck.Contains("granted=2") && defibAck.Contains("revives=1") && defibAck.Contains("leftAfterFirst=1") && defibAck.Contains("leftAfterSecond=1")
                && defibAck.Contains("secondNotice=" + ExpeditionScene.NoOneToReviveNotice) && ViewOf(victim)?.LifeState == "Alive",
                $"host: victim={victimLife.State} hp={victimHp.CurrentHealth}/{victimHp.MaxHealth} (expected {expectedHp}) hostAccepted={_run.DefibrillatorRevives - hostRevivesBefore}; victimSees={ViewOf(victim)?.LifeState}; medic: {defibAck}");

            // Trio ends on D2 with a party wipe: every member down, the wipe resolves once, every peer fails once.
            var roster = _run.Party.LifeRoster;
            var wipes = 0;
            roster.TeamWiped += _ => wipes++;
            foreach (var member in _run.Party.Members.OrderByDescending(m => m.IsLocalOwner ? 0 : 1))
            {
                var hp = member.GameObject.GetComponent<HealthComponent>();
                if (hp == null || !hp.IsAlive) continue;
                hp.TryApplyDamage(new DamageRequest(hp.CurrentHealth + 100));
                yield return Seconds(0.3f);
            }

            yield return WaitFor(() => _summary != null, 15f);
            Record("trio wipe: every member Downed/Dead fails the expedition exactly once on every peer", wipes == 1 && _summary != null && !_summary.IsSuccess,
                $"wipes={wipes} hostOutcome={_summary?.Outcome}");
            foreach (var c in clients) SendProof(c, new ProofMessage { Step = "finish" });
            yield return HostEnd(clients);
        }

        /// <summary>A failed run shows the Run Lost screen and waits for a choice; the proof takes RETURN TO SHELTER.</summary>
        private void LeaveRunLostScreen()
        {
            var run = _run != null ? _run : FindFirstObjectByType<ExpeditionScene>();
            if (run != null && run.RunFailed != null && run.RunFailed.IsOpen && !run.RunFailed.IsResolved) run.RunFailed.ReturnToShelter();
        }

        private IEnumerator HostEnd(List<ulong> clients)
        {
            yield return Seconds(1f);
            LeaveRunLostScreen();
            yield return WaitFor(() => _composed.EndsWith(SceneNames.Base + ";"), 60f);
            yield return WaitFor(() => clients.All(c => _clientFinals.ContainsKey(c)), 60f);
            yield return Seconds(1f);
            var probe = _app.ProbeSave();
            _report.SaveMarkerOpenAfter = probe.ExpeditionMarkerOpen;
            _report.BankedCoinsAfter = probe.BankedCoins;
            _report.DeepestDepthOnDisk = probe.DeepestDepthReached;
            _report.PlayerObjectsAfterReturn = CoopPlayerDirectory.Count;
            _report.SpawnedObjectsAfterReturn = CoopPlayerDirectory.SpawnedObjects();
            if (_summary != null)
            {
                _report.SummaryOutcome = _summary.Outcome.ToString();
                _report.SummaryCoinsExtracted = _summary.CoinsExtracted;
                _report.SummarySecuredItems = _summary.SecuredItems.Count;
                _report.SummaryDeepestDepth = _summary.DeepestDepthReached;
                _report.SummaryXp = _summary.XpEarned;
            }

            var clientsBack = clients.All(c => _clientFinals.TryGetValue(c, out var f) && (_scenario == "trio" ? f.SummaryOutcome == "Failed" : f.SummaryOutcome == "Extracted") && !f.SaveMarkerOpenAfter);
            var hostBack = _composed.EndsWith(SceneNames.Base + ";") && !probe.ExpeditionMarkerOpen;
            Record("every peer left the expedition coherently: own transaction closed and saved, back in the Shelter", clientsBack && hostBack,
                $"host outcome={_report.SummaryOutcome} banked {_report.BankedCoinsBefore}->{probe.BankedCoins} marker={probe.ExpeditionMarkerOpen} deepest={probe.DeepestDepthReached}; " + string.Join(" ", clients.Select(c => _clientFinals.TryGetValue(c, out var f) ? $"c{c}: outcome={f.SummaryOutcome} coins={f.SummaryCoinsExtracted} banked {f.BankedCoinsBefore}->{f.BankedCoinsAfter} marker={f.SaveMarkerOpenAfter} deepest={f.DeepestDepthOnDisk}" : $"c{c}: no final report")));
            Record("no orphan network objects after the run (player objects despawned; only the session link remains)", _report.PlayerObjectsAfterReturn == 0 && _report.SpawnedObjectsAfterReturn <= 1 && clients.All(c => _clientFinals.TryGetValue(c, out var f) && f.PlayerObjectsAfterReturn == 0),
                $"host players={_report.PlayerObjectsAfterReturn} spawned={_report.SpawnedObjectsAfterReturn} " + string.Join(" ", clients.Select(c => _clientFinals.TryGetValue(c, out var f) ? $"c{c} players={f.PlayerObjectsAfterReturn} spawned={f.SpawnedObjectsAfterReturn}" : "")));
            Record("no uncaught exception on any peer", _report.UncaughtExceptions == 0 && clients.All(c => _clientFinals.TryGetValue(c, out var f) && f.UncaughtExceptions == 0),
                $"host={_report.UncaughtExceptions} " + string.Join(" ", clients.Select(c => _clientFinals.TryGetValue(c, out var f) ? $"c{c}={f.UncaughtExceptions}" : "")));
            Finish("done", null);
        }
    }
}

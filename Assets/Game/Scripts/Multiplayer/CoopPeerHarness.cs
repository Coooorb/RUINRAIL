using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Player;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace RuinRail.Networking
{
    /// <summary>
    /// A real co-op peer for runtime proof: one built process hosts, another joins over a real UDP socket on the
    /// loopback interface, and both compose the shipping session path — <see cref="NgoConnectionEvents"/>,
    /// <see cref="SessionRoster"/>, <see cref="NgoPlayerEntityFactory"/> and <see cref="PlayerPresenceService"/> over a
    /// live <see cref="NetworkManager"/> with <see cref="UnityTransport"/> and the release network player prefab.
    ///
    /// This is deliberately not a mock: the player objects are real <see cref="NetworkObject"/>s, ownership is NGO's,
    /// the movement is replicated by <see cref="NetworkPlayerMotion"/>'s intents and published state, and the two peers
    /// are separate operating-system processes. What it does not cover is the live service path: the peers exchange a
    /// direct address instead of a UGS Relay allocation and a join code, which needs project credentials.
    /// </summary>
    public sealed class CoopPeerHarness : MonoBehaviour
    {
        public sealed class Options
        {
            public bool IsHost;
            public string Address = "127.0.0.1";
            public ushort Port = 7787;
            /// <summary>Total party size the host waits for (including itself).</summary>
            public int ExpectedPeers = 2;
            public float TimeoutSeconds = 60f;
            public GameObject PlayerPrefab;
            /// <summary>The co-op expedition channel prefab; registered so both peers declare the same prefab list.</summary>
            public GameObject LinkPrefab;
            public DisplayNamePolicy NamePolicy;
            public string DisplayName = "Peer";
            public string OutputPath;
            /// <summary>How long the peer drives its own player to prove replicated motion.</summary>
            public float DriveSeconds = 2f;
        }

        /// <summary>One peer's observation of the live session, written as JSON for the proof matrix.</summary>
        [Serializable]
        public sealed class PeerReport
        {
            public string Role = "";
            public bool Success;
            public string Stage = "start";
            public string Error = "";
            public bool Listening;
            public bool Connected;
            public ulong LocalClientId;
            public int ConnectedClientCount;
            public int PartySize;
            /// <summary>The complete party as it stood the moment every expected member had joined (peers leave at their own pace).</summary>
            public int PartySizeWhenComplete;
            public int PlayerObjectsWhenComplete;
            public string RosterNames = "";
            /// <summary>Real spawned NetworkObjects marked as player objects, as this peer sees them.</summary>
            public int PlayerObjects;
            public int OwnedPlayerObjects;
            public int RemoteReplicas;
            public string OwnedPlayerEntityId = "";
            public string RemotePlayerEntityIds = "";
            /// <summary>Local presentation a replica must never mint on this peer.</summary>
            public int ReplicaCameras;
            public int ReplicaAudioListeners;
            public int ReplicaLocalInputs;
            public bool ReplicaKinematic;
            /// <summary>How far this peer moved its own player (owned motion, host-validated).</summary>
            public float OwnedMotionDistance;
            /// <summary>How far the other peer's replica travelled here (replicated motion).</summary>
            public float RemoteMotionDistance;
            public bool HostAppliesRemoteIntents;
            /// <summary>Phase 7: exactly one gameplay camera and one AudioListener per process, whatever the party size.</summary>
            public int ProcessCameras;
            public int ProcessAudioListeners;
            /// <summary>The owned character's life state and action gate on this peer.</summary>
            public string OwnedLifeState = "";
            public bool OwnedCanAct;
            public bool OwnedCanFire;
            public string UnityVersion = Application.unityVersion;
        }

        private Options _options;
        private readonly PeerReport _report = new();
        private NetworkManager _manager;
        private PlayerPresenceService _presence;
        private SessionRoster _roster;

        public PeerReport Report => _report;

        public static CoopPeerHarness Begin(Options options)
        {
            var go = new GameObject("CoopPeerHarness");
            // The proof outlives the boot scene: without this the scene load after boot destroys the harness mid-run.
            DontDestroyOnLoad(go);
            var harness = go.AddComponent<CoopPeerHarness>();
            harness._options = options ?? throw new ArgumentNullException(nameof(options));
            harness._report.Role = options.IsHost ? "host" : "client";
            Debug.Log($"COOP-PEER starting as {harness._report.Role} on {options.Address}:{options.Port} (expecting {options.ExpectedPeers} member(s)).");
            harness.StartCoroutine(harness.Run());
            return harness;
        }

        private IEnumerator Run()
        {
            yield return null;
            NetworkManager manager = null;
            try
            {
                manager = BuildManager();
            }
            catch (Exception e)
            {
                Finish("network manager", e.Message);
                yield break;
            }

            _manager = manager;
            // Presence and approval are composed before the peer starts: the host's own connection callback fires
            // inside StartHost, and a client is refused outright if the approval hook is not in place yet.
            if (_options.IsHost) ComposePresence();
            _report.Stage = _options.IsHost ? "start host" : "start client";
            var started = _options.IsHost ? manager.StartHost() : manager.StartClient();
            if (!started) { Finish(_report.Stage, "NGO refused to start."); yield break; }
            _report.Listening = manager.IsListening;

            if (_options.IsHost) yield return HostSession();
            else yield return ClientSession();
        }

        private NetworkManager BuildManager()
        {
            var prefab = _options.PlayerPrefab;
            if (prefab == null) throw new InvalidOperationException("No network player prefab (catalog reference missing).");
            var networkObject = prefab.GetComponent<NetworkObject>();
            if (networkObject == null) throw new InvalidOperationException("The network player prefab has no NetworkObject.");

            // The app's own live composition already built the process NetworkManager; NGO allows exactly one, so the
            // proof configures that one rather than adding a second.
            var manager = NetworkManager.Singleton != null ? NetworkManager.Singleton : FindFirstObjectByType<NetworkManager>();
            if (manager == null)
            {
                var go = new GameObject("CoopNetwork");
                DontDestroyOnLoad(go);
                manager = go.AddComponent<NetworkManager>();
            }

            var transport = manager.GetComponent<UnityTransport>() ?? manager.gameObject.AddComponent<UnityTransport>();
            transport.SetConnectionData(_options.Address, _options.Port, _options.IsHost ? "0.0.0.0" : null);
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                // The proof composes its own objects: no scene synchronisation and no automatic player object, because
                // the presence service is the one thing allowed to create a member's entity (82).
                EnableSceneManagement = false,
                // NGO compares the whole config between peers: both sides must declare approval identically, or the
                // host rejects the request as a config mismatch before the approval hook is ever reached.
                ConnectionApproval = true,
                PlayerPrefab = null,
                TickRate = 30
            };
            if (!manager.NetworkConfig.Prefabs.Contains(prefab)) manager.AddNetworkPrefab(prefab);
            CoopLinkSpawner.Register(manager, _options.LinkPrefab);
            if (!_options.IsHost)
                manager.NetworkConfig.ConnectionData = System.Text.Encoding.UTF8.GetBytes(ConnectionPayload.Encode(_options.DisplayName));
            return manager;
        }

        // ---------------- host ----------------

        /// <summary>The shipping host composition: roster, approval, the NGO spawn factory and the presence service.</summary>
        private void ComposePresence()
        {
            var connection = new NgoConnectionEvents(_manager);
            _roster = new SessionRoster(_options.NamePolicy);
            // Approval records the name a joining client sent and refuses a fourth member (80/81).
            connection.ConfigureApproval(_roster);
            NgoConnectionEvents.PendingNames.Store(NetworkManager.ServerClientId, ConnectionPayload.Encode(_options.DisplayName));
            var factory = new NgoPlayerEntityFactory(_options.PlayerPrefab.GetComponent<NetworkObject>(),
                identity => new Vector3(identity.ClientId * 2f, 0f, 0f));
            // The host spawns exactly one entity per connected member, its own included (82).
            _presence = new PlayerPresenceService(connection, factory, _roster,
                new ReconnectGraceService(ReconnectGraceService.DefaultGraceSeconds, () => true));
        }

        private IEnumerator HostSession()
        {
            _report.LocalClientId = _manager.LocalClientId;
            _report.Stage = "await peers";
            var deadline = Time.realtimeSinceStartup + _options.TimeoutSeconds;
            while (_presence.Entities.Count < _options.ExpectedPeers && Time.realtimeSinceStartup < deadline) yield return null;
            if (_presence.Entities.Count < _options.ExpectedPeers)
            {
                Sample();
                Finish("await peers", $"only {_presence.Entities.Count} of {_options.ExpectedPeers} member(s) joined before the timeout.");
                yield break;
            }

            // The party as it stood the instant it was whole: peers finish their own measurements and leave at their
            // own pace, so the later sample is about motion, not about how many members ever joined.
            _report.PartySizeWhenComplete = _presence.Entities.Count;
            _report.PlayerObjectsWhenComplete = PlayerObjects().Count();
            _report.Stage = "drive motion";
            var remoteBefore = RemoteReplicaPosition();
            yield return DriveOwnedPlayer();
            // Give the joined client time to send its own intents, to observe this peer's motion and to take its own
            // sample: the host must not tear the session down while the other peer is still measuring it.
            var settle = Time.realtimeSinceStartup + 9f;
            while (Time.realtimeSinceStartup < settle) yield return null;
            var remoteAfter = RemoteReplicaPosition();
            // On the host this is the joined member's character moving under the intents it sent (82: host-applied).
            _report.RemoteMotionDistance = remoteBefore.HasValue && remoteAfter.HasValue ? Vector2.Distance(remoteBefore.Value, remoteAfter.Value) : 0f;
            Sample();
            // A solo host has no peer to replicate to or from, so only the co-op scenarios assert replicated motion.
            var coop = _options.ExpectedPeers > 1;
            _report.Success = string.IsNullOrEmpty(_report.Error) && _report.PartySizeWhenComplete >= _options.ExpectedPeers
                              && _report.OwnedPlayerObjects == 1 && _report.PlayerObjectsWhenComplete == _options.ExpectedPeers
                              && _report.OwnedMotionDistance > 0.5f
                              && (!coop || (_report.RemoteMotionDistance > 0.5f && _report.HostAppliesRemoteIntents));
            Finish("done", _report.Error);
        }

        // ---------------- client ----------------

        private IEnumerator ClientSession()
        {
            _report.Stage = "connect";
            var deadline = Time.realtimeSinceStartup + _options.TimeoutSeconds;
            while (!_manager.IsConnectedClient && Time.realtimeSinceStartup < deadline) yield return null;
            if (!_manager.IsConnectedClient) { Finish("connect", "the client never connected to the host."); yield break; }
            _report.Connected = true;
            _report.LocalClientId = _manager.LocalClientId;

            // Wait for the whole party, not just for this peer's own object: every member measures the same session,
            // so an early joiner must not start its window before the last member is in.
            _report.Stage = "await replication";
            while (PlayerObjects().Count() < Mathf.Max(2, _options.ExpectedPeers) && Time.realtimeSinceStartup < deadline) yield return null;
            if (!PlayerObjects().Any(o => o.IsOwner)) { Sample(); Finish("await replication", "the client never received its own player object."); yield break; }

            _report.Stage = "drive motion";
            var remoteBefore = RemoteReplicaPosition();
            yield return DriveOwnedPlayer();
            // Long enough for the host's character to visibly travel here (replicated motion, not a local guess).
            var settle = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < settle) yield return null;
            var remoteAfter = RemoteReplicaPosition();
            _report.RemoteMotionDistance = remoteBefore.HasValue && remoteAfter.HasValue ? Vector2.Distance(remoteBefore.Value, remoteAfter.Value) : 0f;
            Sample();
            _report.PartySizeWhenComplete = _report.PartySize;
            _report.PlayerObjectsWhenComplete = _report.PlayerObjects;
            _report.Success = string.IsNullOrEmpty(_report.Error) && _report.OwnedPlayerObjects == 1 && _report.RemoteReplicas >= 1
                              && _report.ReplicaCameras == 0 && _report.ReplicaAudioListeners == 0 && _report.ReplicaLocalInputs == 0
                              && _report.ReplicaKinematic && _report.OwnedMotionDistance > 0.5f && _report.RemoteMotionDistance > 0.5f;
            // Everything is observed; stay connected a little longer so the host can sample a complete party, then
            // leave. Nothing after this point changes what this peer reports.
            var linger = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < linger) yield return null;
            Finish("done", _report.Error);
        }

        private Vector2? RemoteReplicaPosition()
        {
            var replica = PlayerObjects().FirstOrDefault(o => !o.IsOwner);
            return replica != null ? (Vector2)replica.transform.position : (Vector2?)null;
        }

        // ---------------- shared ----------------

        private IEnumerable<NetworkObject> PlayerObjects()
        {
            if (_manager == null || _manager.SpawnManager == null) return Array.Empty<NetworkObject>();
            return _manager.SpawnManager.SpawnedObjectsList.Where(o => o != null && o.GetComponent<NetworkPlayerObject>() != null);
        }

        /// <summary>
        /// Drives this peer's own player with a scripted reader: on the host that is local authoritative movement, on a
        /// client it is the real intent path (owner predicts, host applies and publishes, owner reconciles).
        /// </summary>
        private IEnumerator DriveOwnedPlayer()
        {
            var owned = PlayerObjects().FirstOrDefault(o => o.IsOwner);
            if (owned == null) yield break;
            var reader = new ScriptedPeerInput { Move = Vector2.right, Aim = (Vector2)owned.transform.position + Vector2.right * 5f };
            var input = owned.GetComponent<PlayerInput>();
            if (input != null) input.UseReader(reader);
            owned.GetComponent<PlayerMovement>()?.SetInputReader(reader);
            owned.GetComponent<PlayerAiming>()?.SetInputReader(reader);
            var start = (Vector2)owned.transform.position;
            var until = Time.realtimeSinceStartup + Mathf.Max(0.25f, _options.DriveSeconds);
            while (Time.realtimeSinceStartup < until) yield return null;
            _report.OwnedMotionDistance = Vector2.Distance(start, owned.transform.position);
        }

        private void Sample()
        {
            if (_manager == null) return;
            _report.Listening = _manager.IsListening;
            _report.Connected = _manager.IsConnectedClient || _manager.IsHost;
            _report.ConnectedClientCount = _manager.IsServer ? _manager.ConnectedClientsIds.Count : 1;
            var players = PlayerObjects().ToList();
            _report.PlayerObjects = players.Count;
            _report.OwnedPlayerObjects = players.Count(o => o.IsOwner);
            var replicas = players.Where(o => !o.IsOwner).ToList();
            _report.RemoteReplicas = replicas.Count;
            _report.OwnedPlayerEntityId = string.Join(";", players.Where(o => o.IsOwner).Select(o => o.NetworkObjectId.ToString()));
            _report.RemotePlayerEntityIds = string.Join(";", replicas.Select(o => o.NetworkObjectId.ToString()));
            _report.ReplicaCameras = replicas.Sum(o => o.GetComponentsInChildren<Camera>(true).Length);
            _report.ReplicaAudioListeners = replicas.Sum(o => o.GetComponentsInChildren<AudioListener>(true).Length);
            _report.ReplicaLocalInputs = replicas.Sum(o => o.GetComponentsInChildren<PlayerInput>(true).Length);
            _report.ReplicaKinematic = replicas.Count == 0 || replicas.All(o =>
            {
                var body = o.GetComponent<Rigidbody2D>();
                return _manager.IsServer || body == null || body.bodyType == RigidbodyType2D.Kinematic;
            });
            _report.HostAppliesRemoteIntents = _manager.IsServer && replicas.Any(o => o.GetComponent<NetworkPlayerMotion>()?.RemoteReader != null);
            _report.ProcessCameras = Camera.allCamerasCount;
            _report.ProcessAudioListeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length;
            var owned = players.FirstOrDefault(o => o.IsOwner);
            if (owned != null)
            {
                var life = owned.GetComponent<RuinRail.Gameplay.Player.PlayerLifeStateComponent>();
                _report.OwnedLifeState = life != null ? life.State.ToString() : "n/a";
                var gate = owned.GetComponent<RuinRail.Gameplay.Combat.IPlayerActionGate>();
                _report.OwnedCanAct = gate == null || gate.CanAct;
                // Firing is owner-driven and host-validated; what a peer can assert locally is that its own character
                // carries the combat path (the shot result itself is proven by the network combat suites).
                _report.OwnedCanFire = owned.GetComponent<NetworkPlayerCombat>() != null && _report.OwnedCanAct;
            }

            _report.PartySize = _presence != null ? _presence.Entities.Count : players.Count;
            _report.RosterNames = _roster != null ? string.Join(";", _roster.Members.Select(m => m.DisplayName)) : string.Join(";", players.Select(o => o.GetComponent<NetworkPlayerObject>().DisplayName));
        }

        private void Finish(string stage, string error)
        {
            _report.Stage = stage;
            if (!string.IsNullOrEmpty(error)) _report.Error = error;
            if (!string.IsNullOrEmpty(_options?.OutputPath))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_options.OutputPath) ?? ".");
                    File.WriteAllText(_options.OutputPath, JsonUtility.ToJson(_report, true));
                }
                catch (Exception e)
                {
                    Debug.LogError($"Co-op peer report could not be written: {e.Message}");
                }
            }

            Debug.Log($"COOP-PEER {_report.Role}: success={_report.Success} stage={_report.Stage} party={_report.PartySize} players={_report.PlayerObjects} owned={_report.OwnedPlayerObjects} replicas={_report.RemoteReplicas} ownedMove={_report.OwnedMotionDistance:0.00} remoteMove={_report.RemoteMotionDistance:0.00} error='{_report.Error}'");
            _presence?.Dispose();
            if (_manager != null) _manager.Shutdown();
            Application.Quit(_report.Success ? 0 : 1);
        }

        /// <summary>A scripted input source for a proof run: a fixed move/aim intent, no device and no player.</summary>
        private sealed class ScriptedPeerInput : IPlayerInputReader
        {
#pragma warning disable CS0067 // The press-edge events are part of the contract; a scripted peer only walks.
            public Vector2 Move { get; set; }
            public Vector2 Aim { get; set; }
            public bool IsAimFromPointer => false;
            public bool FireHeld { get; set; }
            public bool SpecialHeld { get; set; }
            public bool InteractHeld { get; set; }

            public event Action Dash;
            public event Action Reload;
            public event Action Interact;
            public event Action Weapon1Selected;
            public event Action Weapon2Selected;
            public event Action WeaponSwapped;
            public event Action ConsumableUsed;
            public event Action QuickGrenadeUsed;
            public event Action InventoryToggled;
            public event Action PauseToggled;
#pragma warning restore CS0067

            public void Enable() { }
            public void Disable() { }
        }
    }
}

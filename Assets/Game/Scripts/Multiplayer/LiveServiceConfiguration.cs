using System;
using System.Linq;
using UnityEngine;

namespace RuinRail.Networking
{
    /// <summary>
    /// TASK 180 — decides whether the process composes the real Unity multiplayer adapters or the in-memory fakes.
    ///
    /// The rule matters for release honesty: a shipped build must never quietly run on
    /// <see cref="FakeMultiplayerServices"/> and present it to the player as online play. So live is the default for
    /// a real player build, and the fakes are reachable only through an explicit opt-out — a command-line flag, or
    /// code (tests, the deterministic smoke) asking for them by name.
    ///
    /// Live composition still needs a UGS project link at runtime. When that is absent the adapters fail on connect
    /// and the player sees the TASK 178 recoverable-error screen, which is the correct outcome: a visible, honest
    /// failure with a way out, not a silent fallback to a fake that pretends to work.
    /// </summary>
    public static class LiveServiceConfiguration
    {
        /// <summary>Forces the in-memory fakes in a build. For development and automated runs only.</summary>
        public const string OfflineArgument = "-offline-multiplayer";

        /// <summary>The smoke runner drives a deterministic solo flow and must not touch a live service.</summary>
        public const string SmokeArgument = "-smoke";

        public enum Mode
        {
            /// <summary>UnityMultiplayerServices + NgoNetworkDriver.</summary>
            Live,
            /// <summary>FakeMultiplayerServices + FakeNetworkDriver.</summary>
            Fake
        }

        /// <summary>
        /// Resolves the mode for this process: the command line decides, except that the editor never opens a live
        /// session implicitly (a live session there is opt-in via the NetworkBootstrap flag).
        /// </summary>
        public static Mode Resolve() =>
            Application.isEditor ? Mode.Fake : Resolve(Environment.GetCommandLineArgs());

        /// <summary>The command-line rule on its own, independent of where it runs, so it can be tested directly.</summary>
        public static Mode Resolve(string[] args)
        {
            if (args == null) return Mode.Live;
            return args.Contains(OfflineArgument) || args.Contains(SmokeArgument) ? Mode.Fake : Mode.Live;
        }

        /// <summary>
        /// Composes for this process, resolving the NetworkManager itself so callers outside the networking assembly
        /// (GameApp) never need a direct Netcode reference.
        /// </summary>
        public static (IMultiplayerServices Services, INetworkDriver Driver) Compose(Mode mode) =>
            Compose(mode, mode == Mode.Fake ? null : UnityEngine.Object.FindFirstObjectByType<Unity.Netcode.NetworkManager>());

        /// <summary>
        /// Composes for this process, creating the NetworkManager when live composition needs one.
        ///
        /// The shipped player had no NetworkManager anywhere: live mode resolved, found none and returned a null
        /// driver, so every "online" session fell back to the in-memory fake. The manager is therefore built here,
        /// next to the app root, with the transport and the one networked player prefab registered — which is what the
        /// session controller then has to drive.
        /// </summary>
        public static (IMultiplayerServices Services, INetworkDriver Driver) ComposeForProcess(Mode mode, GameObject networkPlayerPrefab) => ComposeForProcess(mode, networkPlayerPrefab, null);

        /// <summary>
        /// As above, also registering the co-op expedition channel prefab and spawning it whenever this process starts
        /// hosting: without it a joining client can own a character but never learns the dungeon, the enemies or the
        /// vote (the gap the co-op completion pass closed).
        /// </summary>
        public static (IMultiplayerServices Services, INetworkDriver Driver) ComposeForProcess(Mode mode, GameObject networkPlayerPrefab, GameObject coopLinkPrefab)
        {
            if (mode == Mode.Fake) return (new FakeMultiplayerServices(), new FakeNetworkDriver());
            var manager = UnityEngine.Object.FindFirstObjectByType<Unity.Netcode.NetworkManager>();
            if (manager == null) manager = ComposeNetworkManager(networkPlayerPrefab);
            if (coopLinkPrefab != null) CoopLinkSpawner.Install(manager, coopLinkPrefab);
            else Debug.LogError("The co-op expedition channel prefab is missing from the content catalog; joined clients could not play an expedition.");
            return Compose(mode, manager);
        }

        /// <summary>
        /// Transport payload ceiling for the co-op channel's reliable records (an inventory snapshot is a few KB). Both
        /// peers compose the same value; it is a transport buffer size, not a gameplay value.
        /// </summary>
        public const int CoopMaxPayloadBytes = 65536;

        /// <summary>
        /// The process's NetworkManager: UDP transport, no automatic player object, the player prefab registered.
        ///
        /// It is deliberately a ROOT object that survives scene loads on its own: NGO throws in a player build if the
        /// NetworkManager is nested under anything, so it must never be parented to the app root.
        /// </summary>
        private static Unity.Netcode.NetworkManager ComposeNetworkManager(GameObject networkPlayerPrefab)
        {
            var go = new GameObject("NetworkManager");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var manager = go.AddComponent<Unity.Netcode.NetworkManager>();
            var transport = go.AddComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            transport.MaxPayloadSize = CoopMaxPayloadBytes;
            manager.NetworkConfig = new Unity.Netcode.NetworkConfig
            {
                NetworkTransport = transport,
                // The presence service creates exactly one entity per member (82), so NGO must not add one of its own.
                PlayerPrefab = null,
                ConnectionApproval = true,
                EnableSceneManagement = false,
                TickRate = 30
            };

            if (networkPlayerPrefab != null && networkPlayerPrefab.GetComponent<Unity.Netcode.NetworkObject>() != null)
            {
                manager.AddNetworkPrefab(networkPlayerPrefab);
            }
            else
            {
                Debug.LogError("The networked player prefab is missing from the content catalog; a session could not spawn players.");
            }

            return manager;
        }

        /// <summary>
        /// Builds the adapter pair for a mode. Live needs a NetworkManager; without one there is nothing to drive, so
        /// it reports that plainly instead of silently degrading to a fake.
        /// </summary>
        public static (IMultiplayerServices Services, INetworkDriver Driver) Compose(Mode mode, Unity.Netcode.NetworkManager networkManager)
        {
            if (mode == Mode.Fake) return (new FakeMultiplayerServices(), new FakeNetworkDriver());

            if (networkManager == null)
            {
                Debug.LogError("Live multiplayer was requested but no NetworkManager is present; online play is unavailable in this build.");
                return (new UnityMultiplayerServices(), null);
            }

            return (new UnityMultiplayerServices(), new NgoNetworkDriver(networkManager));
        }
    }
}

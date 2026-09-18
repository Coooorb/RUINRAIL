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

using System;
using System.Linq;
using RuinRail.Networking;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// Built-player co-op peer entry (`-coop-peer host|client [-coop-port 7787] [-coop-size 2] [-coop-out file.json]`):
    /// two processes of the shipped player, one hosting and one joining over a real socket, both composing the live NGO
    /// session path. It exists so "co-op runs" can be demonstrated by actual peers rather than by in-process doubles.
    /// </summary>
    public static class CoopPeerRunner
    {
        public const string PeerArgument = "-coop-peer";
        public const string PortArgument = "-coop-port";
        public const string SizeArgument = "-coop-size";
        public const string OutArgument = "-coop-out";
        public const string SecondsArgument = "-coop-seconds";

        public static bool Requested(string[] args) => RoleOf(args) != null;

        public static string RoleOf(string[] args)
        {
            var index = Array.IndexOf(args, PeerArgument);
            if (index < 0 || index + 1 >= args.Length) return null;
            var role = args[index + 1].ToLowerInvariant();
            return role == "host" || role == "client" ? role : null;
        }

        public static void Begin(GameApp app, string[] args)
        {
            var role = RoleOf(args);
            if (role == null) return;
            var content = app.Content;
            CoopPeerHarness.Begin(new CoopPeerHarness.Options
            {
                IsHost = role == "host",
                Port = (ushort)Value(args, PortArgument, 7787),
                ExpectedPeers = Mathf.Clamp(Value(args, SizeArgument, 2), 1, ExpeditionParty.MaxPartySize),
                TimeoutSeconds = Value(args, SecondsArgument, 60),
                PlayerPrefab = content.NetworkPlayerEntity,
                LinkPrefab = content.CoopRunLink,
                NamePolicy = content.DisplayNamePolicy,
                DisplayName = role == "host" ? "Host" : "Client",
                OutputPath = Text(args, OutArgument, System.IO.Path.Combine(app.SaveDirectory, $"coop_peer_{role}.json"))
            });
        }

        private static int Value(string[] args, string name, int fallback)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value) ? value : fallback;
        }

        private static string Text(string[] args, string name, string fallback)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }
    }
}

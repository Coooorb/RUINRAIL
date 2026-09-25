using System;
using System.Collections.Generic;
using System.Linq;

namespace RuinRail.Networking
{
    /// <summary>
    /// What the co-op expedition runtime needs from the network: reliable host↔client records, unreliable enemy motion
    /// and shots, and the depth payload. The shipping implementation is <see cref="CoopRunLink"/> (one NGO network
    /// object per session); <see cref="LoopbackCoopBus"/> connects in-process peers for PlayMode tests. The runtime
    /// never touches NGO directly, so the same host/client logic runs over either.
    /// </summary>
    public interface ICoopBus
    {
        bool IsHost { get; }
        ulong LocalClientId { get; }
        /// <summary>Host: every connected client except itself. Client: empty.</summary>
        IReadOnlyList<ulong> RemoteClients { get; }
        /// <summary>The clock both peers agree on (NGO server time); replicated motion is stamped and sampled with it.</summary>
        double NetworkTime { get; }

        void SendToHost(string kind, string json);
        void SendToClients(string kind, string json);
        void SendToClient(ulong clientId, string kind, string json);
        void SendEnemyStates(EnemyNetState[] states);
        void SendShot(ShotNetRecord shot);

        /// <summary>(sender client id, kind, json). On a client the sender is always the host.</summary>
        event Action<ulong, string, string> Received;
        event Action<EnemyNetState[]> EnemyStatesReceived;
        event Action<ShotNetRecord> ShotReceived;

        DungeonSyncPayload DepthPayload { get; }
        void PublishDepth(in DungeonSyncPayload payload);
        event Action<DungeonSyncPayload> DepthPayloadChanged;
    }

    /// <summary>
    /// In-process bus for tests: one host endpoint and any number of client endpoints, delivering synchronously (or
    /// queued until <see cref="Pump"/> when <see cref="Queued"/> is set, to test ordering and duplicates).
    /// </summary>
    public sealed class LoopbackCoopBus : ICoopBus
    {
        private readonly LoopbackNetwork _network;

        private LoopbackCoopBus(LoopbackNetwork network, ulong clientId, bool isHost)
        {
            _network = network;
            LocalClientId = clientId;
            IsHost = isHost;
        }

        public bool IsHost { get; }
        public ulong LocalClientId { get; }
        public IReadOnlyList<ulong> RemoteClients => IsHost ? _network.Clients.Select(c => c.LocalClientId).ToList() : Array.Empty<ulong>();
        public bool Queued { get => _network.Queued; set => _network.Queued = value; }
        public double NetworkTime => UnityEngine.Time.timeAsDouble;
        public int Sent { get; private set; }

        public event Action<ulong, string, string> Received;
        public event Action<EnemyNetState[]> EnemyStatesReceived;
        public event Action<ShotNetRecord> ShotReceived;
        public event Action<DungeonSyncPayload> DepthPayloadChanged;

        public DungeonSyncPayload DepthPayload => _network.Payload;

        public static LoopbackCoopBus CreateHost(out LoopbackNetwork network)
        {
            network = new LoopbackNetwork();
            var host = new LoopbackCoopBus(network, 0, true);
            network.Host = host;
            return host;
        }

        public static LoopbackCoopBus Join(LoopbackNetwork network, ulong clientId)
        {
            var client = new LoopbackCoopBus(network, clientId, false);
            network.Clients.Add(client);
            return client;
        }

        public void Leave() => _network.Clients.Remove(this);

        public void SendToHost(string kind, string json)
        {
            if (IsHost) return;
            Sent++;
            var host = _network.Host;
            _network.Deliver(() => host?.Received?.Invoke(LocalClientId, kind, json));
        }

        public void SendToClients(string kind, string json)
        {
            if (!IsHost) return;
            Sent++;
            foreach (var client in _network.Clients.ToList()) _network.Deliver(() => client.Received?.Invoke(0, kind, json));
        }

        public void SendToClient(ulong clientId, string kind, string json)
        {
            if (!IsHost) return;
            Sent++;
            var client = _network.Clients.FirstOrDefault(c => c.LocalClientId == clientId);
            if (client != null) _network.Deliver(() => client.Received?.Invoke(0, kind, json));
        }

        public void SendEnemyStates(EnemyNetState[] states)
        {
            if (!IsHost) return;
            foreach (var client in _network.Clients.ToList()) _network.Deliver(() => client.EnemyStatesReceived?.Invoke(states));
        }

        public void SendShot(ShotNetRecord shot)
        {
            shot.Shooter = LocalClientId;
            if (IsHost)
            {
                foreach (var client in _network.Clients.ToList()) _network.Deliver(() => client.ShotReceived?.Invoke(shot));
                return;
            }

            var host = _network.Host;
            _network.Deliver(() =>
            {
                host?.ShotReceived?.Invoke(shot);
                foreach (var client in _network.Clients.Where(c => c.LocalClientId != shot.Shooter).ToList()) client.ShotReceived?.Invoke(shot);
            });
        }

        public void PublishDepth(in DungeonSyncPayload payload)
        {
            if (!IsHost) throw new AuthorityViolationException(AuthoritativeDomain.RoomGraph, NetworkRole.Client);
            var copy = payload;
            _network.Payload = copy;
            foreach (var client in _network.Clients.ToList()) _network.Deliver(() => client.DepthPayloadChanged?.Invoke(copy));
        }

        /// <summary>Loopback wiring shared by the endpoints of one test "session".</summary>
        public sealed class LoopbackNetwork
        {
            private readonly Queue<Action> _queue = new();
            public LoopbackCoopBus Host;
            public readonly List<LoopbackCoopBus> Clients = new();
            public DungeonSyncPayload Payload;
            public bool Queued;
            public int Delivered { get; private set; }

            public void Deliver(Action delivery)
            {
                if (Queued) { _queue.Enqueue(delivery); return; }
                Delivered++;
                delivery();
            }

            /// <summary>Delivers every queued message in send order.</summary>
            public int Pump()
            {
                var count = 0;
                while (_queue.Count > 0)
                {
                    _queue.Dequeue()();
                    Delivered++;
                    count++;
                }

                return count;
            }
        }
    }
}

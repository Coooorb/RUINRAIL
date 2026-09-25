using System;
using UnityEngine;

namespace RuinRail.Networking
{
    /// <summary>
    /// The missing link between a live session and the party the expedition is actually started for: every client the
    /// host accepts becomes a lobby member, and every client that leaves before the start is removed again.
    ///
    /// Without it the Multiplayer Terminal could host a session, show a join code and count connected players, while
    /// <see cref="PartyLobby"/> still held exactly one member — so the expedition's start snapshot named one
    /// participant and the run composed a solo party however many people had joined. The lobby is host-owned (82); a
    /// client's own lobby view holds only itself, which is the honest state until lobby replication exists.
    /// </summary>
    public sealed class SessionPartyBridge : IDisposable
    {
        private readonly IConnectionEvents _connection;
        private readonly PartyLobby _lobby;
        private readonly Func<ulong, string> _participantId;
        private bool _disposed;

        public SessionPartyBridge(IConnectionEvents connection, PartyLobby lobby, Func<ulong, string> participantId = null)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _lobby = lobby ?? throw new ArgumentNullException(nameof(lobby));
            _participantId = participantId;
            _connection.ClientConnected += OnConnected;
            _connection.ClientDisconnected += OnDisconnected;
        }

        public int Joined { get; private set; }
        public int Left { get; private set; }

        private void OnConnected(ulong clientId, string rawName)
        {
            // Only the host decides membership (82), and only before the expedition starts (81).
            if (!_connection.IsHost || _lobby.HasStarted) return;
            if (_lobby.Get(clientId) != null) return;
            if (_lobby.Members.Count >= _lobby.MaxMembers) return;
            _lobby.Join(clientId, _participantId?.Invoke(clientId) ?? clientId.ToString());
            Joined++;
        }

        private void OnDisconnected(ulong clientId)
        {
            if (!_connection.IsHost || _lobby.HasStarted) return;
            // The local host is never removed from its own lobby.
            if (clientId == _connection.LocalClientId) return;
            if (_lobby.Leave(clientId)) Left++;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _connection.ClientConnected -= OnConnected;
            _connection.ClientDisconnected -= OnDisconnected;
        }
    }
}

using System;
using Unity.Netcode;

namespace RuinRail.Networking
{
    /// <summary>Host publishes the depth payload; clients rebuild and verify when it changes.</summary>
    public sealed class NetworkDungeonSync : NetworkBehaviour
    {
        private readonly NetworkVariable<DungeonSyncPayload> _payload = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public DungeonSyncPayload Payload => _payload.Value;
        public event Action<DungeonSyncPayload> PayloadChanged;

        public override void OnNetworkSpawn()
        {
            _payload.OnValueChanged += (_, next) => PayloadChanged?.Invoke(next);
        }

        public void Publish(in DungeonSyncPayload payload)
        {
            if (!IsServer) throw new AuthorityViolationException(AuthoritativeDomain.RoomGraph, NetworkRole.Client);
            _payload.Value = payload;
        }
    }
}

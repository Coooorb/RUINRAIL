using RuinRail.Gameplay.Player;
using Unity.Netcode;
using UnityEngine;

namespace RuinRail.Networking
{
    /// <summary>Replicated life state of a player object (84): state plus the remaining bleedout, host-authored.</summary>
    public struct PlayerLifeNetState : INetworkSerializable
    {
        public int State;
        public float BleedoutRemaining;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref State);
            serializer.SerializeValue(ref BleedoutRemaining);
        }
    }

    /// <summary>
    /// 84 on every peer: the host decides Alive/Downed/Dead and owns the bleedout clock, and every other peer applies
    /// that state to its replica through the existing <see cref="PlayerLifeStateComponent.ApplyReplicatedState"/> seam.
    ///
    /// Health already replicated, but life state did not, so a teammate bleeding out on a client read as alive at 0 HP:
    /// no downed presentation, no bleedout countdown, no dead spectator handover. Nothing here decides anything — the
    /// transition itself stays a host decision (82).
    /// </summary>
    public sealed class NetworkPlayerLife : NetworkBehaviour
    {
        private readonly NetworkVariable<PlayerLifeNetState> _state = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private PlayerLifeStateComponent _life;

        public PlayerLifeNetState State => _state.Value;

        public override void OnNetworkSpawn()
        {
            _life = GetComponent<PlayerLifeStateComponent>();
            if (_life == null) return;
            if (IsServer)
            {
                Publish();
                _life.StateChanged += OnStateChanged;
            }
            else
            {
                Apply(_state.Value);
                _state.OnValueChanged += (_, next) => Apply(next);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (_life != null && IsServer) _life.StateChanged -= OnStateChanged;
        }

        private void OnStateChanged(PlayerLifeStateComponent _, PlayerLifeState __, PlayerLifeState ___) => Publish();

        private void Update()
        {
            // The bleedout countdown is continuous, so the replicated value is refreshed while a member is Downed.
            if (!IsSpawned || !IsServer || _life == null || !_life.IsDowned) return;
            Publish();
        }

        private void Publish()
        {
            if (_life == null) return;
            _state.Value = new PlayerLifeNetState { State = (int)_life.State, BleedoutRemaining = _life.BleedoutRemaining };
        }

        private void Apply(PlayerLifeNetState next) => _life?.ApplyReplicatedState((PlayerLifeState)next.State, next.BleedoutRemaining);
    }
}

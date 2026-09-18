using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Combat;
using Unity.Netcode;
using UnityEngine;

namespace RuinRail.Networking
{
    /// <summary>Replicated health on a network object: host publishes versioned states, clients apply them once.</summary>
    public sealed class NetworkHealth : NetworkBehaviour
    {
        private readonly NetworkVariable<HealthNetState> _state = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private HealthReplicator _replicator;
        private HealthReplicaApplier _applier;

        public HealthNetState State => _state.Value;
        public HealthReplicator Replicator => _replicator;

        public override void OnNetworkSpawn()
        {
            var health = GetComponent<HealthComponent>();
            if (health == null) return;
            if (IsServer)
            {
                _replicator = new HealthReplicator(health);
                _replicator.StateChanged += s => _state.Value = s;
                _state.Value = _replicator.Current;
            }
            else
            {
                _applier = new HealthReplicaApplier(health);
                _state.OnValueChanged += (_, next) => _applier.Apply(next);
                _applier.Apply(_state.Value);
            }
        }

        public override void OnNetworkDespawn()
        {
            _replicator?.Dispose();
            _replicator = null;
        }
    }
}

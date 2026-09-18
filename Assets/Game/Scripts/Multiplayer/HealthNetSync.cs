using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Combat;
using Unity.Netcode;
using UnityEngine;

namespace RuinRail.Networking
{
    /// <summary>
    /// Authoritative health as replicated: exact integers, alive flag, a monotonically increasing version so late or
    /// duplicated messages can never re-apply a change, and the last damage amount for hit feedback.
    /// </summary>
    [Serializable]
    public struct HealthNetState : INetworkSerializable
    {
        public int Current;
        public int Max;
        public bool IsAlive;
        public uint Version;
        public int LastDamage;
        public int LastHeal;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Current);
            serializer.SerializeValue(ref Max);
            serializer.SerializeValue(ref IsAlive);
            serializer.SerializeValue(ref Version);
            serializer.SerializeValue(ref LastDamage);
            serializer.SerializeValue(ref LastHeal);
        }
    }

    /// <summary>
    /// Host side: observes a HealthComponent and produces versioned states; exposes the once-per-life zero-HP hook the
    /// downed/dead systems (TASKS 102–104) bind to. Only authoritative damage/heal sources ever reach the component
    /// (DamageAuthority gates every non-host process).
    /// </summary>
    public sealed class HealthReplicator : IDisposable
    {
        private readonly HealthComponent _health;
        private uint _version;
        private bool _zeroReported;

        public HealthReplicator(HealthComponent health)
        {
            _health = health != null ? health : throw new ArgumentNullException(nameof(health));
            _health.Damaged += OnDamaged;
            _health.Healed += OnHealed;
            _health.Died += OnDied;
            Current = new HealthNetState { Current = _health.CurrentHealth, Max = _health.MaxHealth, IsAlive = _health.IsAlive, Version = 0 };
        }

        public HealthNetState Current { get; private set; }
        public uint Version => _version;
        public int Publishes { get; private set; }

        public event Action<HealthNetState> StateChanged;

        /// <summary>Fires exactly once when HP reaches zero (the downed/dead transition seam); re-armed by a revive heal.</summary>
        public event Action<HealthComponent> ZeroHealthReached;

        private void Publish(int damage, int heal)
        {
            _version++;
            Current = new HealthNetState { Current = _health.CurrentHealth, Max = _health.MaxHealth, IsAlive = _health.IsAlive, Version = _version, LastDamage = damage, LastHeal = heal };
            Publishes++;
            StateChanged?.Invoke(Current);
        }

        private void OnDamaged(int amount) => Publish(amount, 0);

        private void OnHealed(int amount)
        {
            if (_health.CurrentHealth > 0) _zeroReported = false;
            Publish(0, amount);
        }

        private void OnDied()
        {
            if (_zeroReported) return;
            _zeroReported = true;
            ZeroHealthReached?.Invoke(_health);
        }

        public void Dispose()
        {
            _health.Damaged -= OnDamaged;
            _health.Healed -= OnHealed;
            _health.Died -= OnDied;
        }
    }

    /// <summary>Client side: applies versioned states exactly once, in order; older/duplicate versions are ignored.</summary>
    public sealed class HealthReplicaApplier
    {
        private readonly HealthComponent _health;
        private bool _any;

        public HealthReplicaApplier(HealthComponent health)
        {
            _health = health != null ? health : throw new ArgumentNullException(nameof(health));
        }

        public uint LastAppliedVersion { get; private set; }
        public int Applied { get; private set; }
        public int Ignored { get; private set; }

        public bool Apply(in HealthNetState state)
        {
            if (_any && state.Version <= LastAppliedVersion)
            {
                Ignored++;
                return false;
            }

            _any = true;
            LastAppliedVersion = state.Version;
            Applied++;
            _health.ApplyReplicatedHealth(state.Current, state.Max);
            return true;
        }
    }

    /// <summary>
    /// Host-side hit deduplication for authoritative damage sources that can deliver the same hit through more than
    /// one callback (trigger re-entry, late area resolution): a hit id is applied at most once.
    /// </summary>
    public sealed class AuthoritativeHitGate
    {
        private readonly Queue<long> _order = new();
        private readonly HashSet<long> _seen = new();
        private readonly int _capacity;

        public AuthoritativeHitGate(int capacity = 256)
        {
            _capacity = Math.Max(8, capacity);
        }

        public int Applied { get; private set; }
        public int Duplicates { get; private set; }

        public bool TryApply(HealthComponent target, long hitId, DamageRequest request)
        {
            if (target == null) return false;
            if (!_seen.Add(hitId))
            {
                Duplicates++;
                return false;
            }

            _order.Enqueue(hitId);
            while (_order.Count > _capacity) _seen.Remove(_order.Dequeue());
            var applied = target.TryApplyDamage(request);
            if (applied) Applied++;
            return applied;
        }
    }

}

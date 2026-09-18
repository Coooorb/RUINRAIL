using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Expedition;
using UnityEngine;

namespace RuinRail.Gameplay.Loot
{
    /// <summary>
    /// Shared ground state of the current depth: every world pickup (item or coin pile) that was spawned or dropped.
    /// 32: ground loot never time-despawns while the party is on the depth; everything left behind is discarded when
    /// the party leaves it (Clear). Pickups that were collected simply disappear from the list on the next prune.
    /// </summary>
    public sealed class GroundLootRegistry
    {
        private readonly List<GameObject> _tracked = new();

        public int DepthClears { get; private set; }

        /// <summary>Raised once per newly tracked pickup (chest loot, event rewards, player drops): the observers that give a pickup its sound and prompts hook here.</summary>
        public event Action<GameObject> PickupTracked;

        /// <summary>Live tracked pickups (collected/destroyed ones are pruned lazily).</summary>
        public IReadOnlyList<GameObject> Tracked
        {
            get
            {
                Prune();
                return _tracked;
            }
        }

        public int Count => Tracked.Count;

        public void Track(GameObject pickup)
        {
            if (pickup == null) return;
            Prune();
            if (_tracked.Contains(pickup)) return;
            _tracked.Add(pickup);
            PickupTracked?.Invoke(pickup);
        }

        public bool Untrack(GameObject pickup) => pickup != null && _tracked.Remove(pickup);

        /// <summary>Discards everything still lying on the ground (party left the depth or the expedition ended).</summary>
        public void Clear()
        {
            DepthClears++;
            foreach (var pickup in _tracked)
            {
                if (pickup != null) UnityEngine.Object.Destroy(pickup);
            }

            _tracked.Clear();
        }

        private void Prune() => _tracked.RemoveAll(p => p == null);
    }

    /// <summary>
    /// Binds a ground registry to the expedition's depth lifetime: entering a depth (after Descend) and ending the
    /// expedition both discard the previous depth's ground loot. Nothing else ever removes it.
    /// </summary>
    public sealed class GroundLootLifetime : IDisposable
    {
        private readonly GroundLootRegistry _registry;
        private readonly ExpeditionService _expedition;

        public GroundLootLifetime(GroundLootRegistry registry, ExpeditionService expedition)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _expedition = expedition ?? throw new ArgumentNullException(nameof(expedition));
            _expedition.DepthEntered += OnDepthEntered;
            _expedition.ExpeditionEnded += OnExpeditionEnded;
        }

        private void OnDepthEntered(ExpeditionState state) => _registry.Clear();
        private void OnExpeditionEnded(ExpeditionSummary summary) => _registry.Clear();

        public void Dispose()
        {
            _expedition.DepthEntered -= OnDepthEntered;
            _expedition.ExpeditionEnded -= OnExpeditionEnded;
        }
    }
}

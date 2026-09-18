using System.Collections.Generic;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Stats;
using UnityEngine;

namespace RuinRail.Gameplay.Player
{
    /// <summary>
    /// Magnetic pickup attraction (30 Magnetic Coil: "+3 tiles pickup attraction radius for eligible pickups"). The
    /// radius is the flat PickupAttractionRadius stat in tiles (0 without a source: nothing is pulled). Eligible
    /// pickups (Coins, Ammo stacks) inside the radius glide toward the player and are collected on arrival through
    /// their normal Interact path — equipment and consumables keep requiring a deliberate interaction.
    /// A pickup the player cannot currently take (full backpack) is left in place, never duplicated.
    /// Room Sweep (34) reuses the same pull with no radius limit via Pull/SweepAll.
    /// </summary>
    public sealed class PickupAttractor : MonoBehaviour
    {
        private readonly ActionGateLookup _actionGate = new();
        // V1 FINAL (TASK 179) values: pull speed and collect distance are presentation feel, not GDD balance.
        [SerializeField, Min(0f)] private float _baseRadiusTiles;
        [SerializeField, Min(0.1f)] private float _pullSpeedTilesPerSecond = 8f;
        [SerializeField, Min(0.05f)] private float _collectDistance = 0.35f;

        private IPlayerStatsProvider _stats;
        private readonly Collider2D[] _hits = new Collider2D[32];
        private readonly HashSet<IAttractablePickup> _pulled = new();
        private readonly List<IAttractablePickup> _scratch = new();

        public float BaseRadiusTiles => _baseRadiusTiles;
        public float PullSpeedTilesPerSecond => _pullSpeedTilesPerSecond;
        public float CollectDistance => _collectDistance;

        /// <summary>Effective attraction radius in tiles: base + flat stat (e.g. +3 with a Magnetic Coil equipped).</summary>
        public float Radius => Mathf.Max(0f, _baseRadiusTiles + (_stats != null ? _stats.GetFlat(StatId.PickupAttractionRadius) : 0));

        public int Collected { get; private set; }
        public int PulledCount => _pulled.Count;

        public void SetStats(IPlayerStatsProvider stats) => _stats = stats;

        public void SetBaseRadius(float tiles) => _baseRadiusTiles = Mathf.Max(0f, tiles);

        /// <summary>Pulls one eligible pickup regardless of distance (Room Sweep).</summary>
        public bool Pull(IAttractablePickup pickup)
        {
            if (pickup == null || !pickup.IsAttractionEligible) return false;
            return _pulled.Add(pickup);
        }

        /// <summary>Pulls every eligible pickup among the given objects (e.g. the ground registry of the room).</summary>
        public int SweepAll(IEnumerable<GameObject> pickups)
        {
            var count = 0;
            if (pickups == null) return 0;
            foreach (var go in pickups)
            {
                if (go == null) continue;
                var pickup = go.GetComponent<IAttractablePickup>();
                if (pickup != null && Pull(pickup)) count++;
            }

            return count;
        }

        private void FixedUpdate()
        {
            Step(Time.fixedDeltaTime);
        }

        /// <summary>One attraction step; exposed for deterministic tests.</summary>
        public void Step(float deltaTime)
        {
            // 84: Downed/Dead players attract nothing (attraction is a pickup interaction).
            if (!_actionGate.CanAct(this)) return;
            var radius = Radius;
            if (radius > 0f)
            {
                var count = Physics2D.OverlapCircle(transform.position, radius, Physics2DQueries.LegacyQueryFilter(), _hits);
                for (var i = 0; i < count; i++)
                {
                    var pickup = _hits[i].GetComponentInParent<IAttractablePickup>();
                    if (pickup != null && pickup.IsAttractionEligible && pickup.CanInteract(gameObject)) _pulled.Add(pickup);
                }
            }

            if (_pulled.Count == 0) return;
            _scratch.Clear();
            _scratch.AddRange(_pulled);
            foreach (var pickup in _scratch)
            {
                if (pickup == null || (pickup is Component c && c == null) || !pickup.IsAttractionEligible || !pickup.CanInteract(gameObject))
                {
                    _pulled.Remove(pickup);
                    continue;
                }

                var t = pickup.transform;
                var target = (Vector2)transform.position;
                var next = Vector2.MoveTowards(t.position, target, _pullSpeedTilesPerSecond * deltaTime);
                t.position = new Vector3(next.x, next.y, t.position.z);
                if ((next - target).sqrMagnitude > _collectDistance * _collectDistance) continue;

                _pulled.Remove(pickup);
                if (pickup.Interact(gameObject)) Collected++;
            }
        }
    }
}

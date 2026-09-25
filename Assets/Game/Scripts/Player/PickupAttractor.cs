using System.Collections.Generic;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Stats;
using UnityEngine;

namespace RuinRail.Gameplay.Player
{
    /// <summary>
    /// Magnetic pickup attraction (30 Magnetic Coil: "+3 tiles pickup attraction radius for eligible pickups"). The
    /// radius is <see cref="DefaultBaseRadiusTiles"/> plus the flat PickupAttractionRadius stat in tiles. Eligible
    /// pickups (Coins, Ammo stacks) inside the radius glide toward the player and are collected on arrival through
    /// their normal Interact path — equipment and consumables keep requiring a deliberate interaction.
    /// A pickup the player cannot currently take (full backpack) is left in place, never duplicated.
    /// Room Sweep (34) reuses the same pull with no radius limit via Pull/SweepAll.
    /// </summary>
    public sealed class PickupAttractor : MonoBehaviour
    {
        /// <summary>
        /// The baseline attraction every survivor has, in tiles.
        ///
        /// Deliberately about one tile of reach past the body: a coin or ammo pile the player walks over or brushes
        /// past comes to them, and nothing further away moves. It is not a vacuum — 30's Magnetic Coil adds +3 tiles
        /// on top of it, so the accessory still multiplies the reach by more than three and stays the reason to wear
        /// it. Presentation feel, not a GDD balance value: no pickup contents, capacity rule or interact rule changes.
        /// </summary>
        public const float DefaultBaseRadiusTiles = 1.25f;

        /// <summary>The largest base radius this component will accept, so a mistake here can never become a room vacuum.</summary>
        public const float MaxBaseRadiusTiles = 2f;

        private readonly ActionGateLookup _actionGate = new();
        // V1 FINAL (TASK 179) values: pull speed and collect distance are presentation feel, not GDD balance.
        [SerializeField, Min(0f)] private float _baseRadiusTiles = DefaultBaseRadiusTiles;
        [SerializeField, Min(0.1f)] private float _pullSpeedTilesPerSecond = 8f;
        [SerializeField, Min(0.05f)] private float _collectDistance = 0.35f;

        private IPlayerStatsProvider _stats;
        private readonly Collider2D[] _hits = new Collider2D[32];
        private readonly HashSet<IAttractablePickup> _pulled = new();
        private readonly List<IAttractablePickup> _scratch = new();
        private readonly RaycastHit2D[] _obstructions = new RaycastHit2D[16];

        public float BaseRadiusTiles => _baseRadiusTiles;
        public float PullSpeedTilesPerSecond => _pullSpeedTilesPerSecond;
        public float CollectDistance => _collectDistance;

        /// <summary>Effective attraction radius in tiles: base + flat stat (e.g. +3 with a Magnetic Coil equipped).</summary>
        public float Radius => Mathf.Max(0f, _baseRadiusTiles + (_stats != null ? _stats.GetFlat(StatId.PickupAttractionRadius) : 0));

        public int Collected { get; private set; }
        public int PulledCount => _pulled.Count;

        public void SetStats(IPlayerStatsProvider stats) => _stats = stats;

        /// <summary>Sets the baseline reach, clamped to <see cref="MaxBaseRadiusTiles"/>.</summary>
        public void SetBaseRadius(float tiles) => _baseRadiusTiles = Mathf.Clamp(tiles, 0f, MaxBaseRadiusTiles);

        /// <summary>
        /// True when solid level geometry sits between the player and a pickup.
        ///
        /// Acquisition is a circle overlap, so without this a coin on the far side of a one-tile wall is inside the
        /// radius and would be dragged into the wall. Only <see cref="EnvironmentObstacle"/> blocks — the same thing a
        /// projectile treats as wall — so a pickup behind a crate or a shut door stays where it is, and one in the
        /// open room is unaffected. This applies to the Coil's reach as well, where the same overlap already existed.
        /// </summary>
        public bool IsObstructed(Vector2 pickupPosition)
        {
            var origin = (Vector2)transform.position;
            var delta = pickupPosition - origin;
            var length = delta.magnitude;
            if (length < 0.001f) return false;
            var count = Physics2D.Raycast(origin, delta / length, Physics2DQueries.LegacyQueryFilter(), _obstructions, length);
            for (var i = 0; i < count; i++)
            {
                if (_obstructions[i].collider != null && _obstructions[i].collider.GetComponentInParent<EnvironmentObstacle>() != null) return true;
            }

            return false;
        }

        /// <summary>Pulls one eligible pickup regardless of distance (Room Sweep).</summary>
        public bool Pull(IAttractablePickup pickup)
        {
            // Room Sweep (34) deliberately ignores radius and line of sight, but not capacity: a stack that cannot be
            // taken is not swept onto the player either.
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
                    // CanBeCollectedBy, not CanInteract: a stack the backpack has no room for must stay where it fell,
                    // or it would be dragged onto the player and then hold the interaction prompt hostage.
                    if (pickup == null || !pickup.IsAttractionEligible || !pickup.CanBeCollectedBy(gameObject)) continue;
                    if (IsObstructed(pickup.transform.position)) continue;
                    _pulled.Add(pickup);
                }
            }

            if (_pulled.Count == 0) return;
            _scratch.Clear();
            _scratch.AddRange(_pulled);
            foreach (var pickup in _scratch)
            {
                // Stops pulling the moment it stops being takeable — a backpack that filled up mid-pull releases it
                // rather than parking it on the player.
                if (pickup == null || (pickup is Component c && c == null) || !pickup.IsAttractionEligible || !pickup.CanBeCollectedBy(gameObject))
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

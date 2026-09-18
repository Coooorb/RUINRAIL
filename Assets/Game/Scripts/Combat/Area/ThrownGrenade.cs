using System;
using RuinRail.Gameplay.Items.Consumables;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Area
{
    /// <summary>
    /// Visible top-down grenade: travels along the ground toward its landing point at a fixed speed (the travel is the
    /// fuse), then resolves its authored effect exactly once at the landing point. Effect implementations are the
    /// reusable area foundations (AreaDamageResolver, BurnZone, SmokeZone) — no grenade-specific damage paths.
    /// </summary>
    public sealed class ThrownGrenade : MonoBehaviour
    {
        private GrenadeData _data;
        private Vector2 _landing;
        private DamageTeam _sourceTeam;
        private IDamageRoller _roller;
        private bool _resolved;

        public GrenadeData Data => _data;
        public Vector2 LandingPoint => _landing;
        public bool IsResolved => _resolved;
        public GameObject SpawnedZone { get; private set; }

        public event Action<ThrownGrenade, AreaDamageResolver.Result> Resolved;

        public void Launch(GrenadeData data, Vector2 origin, Vector2 landing, DamageTeam sourceTeam, IDamageRoller roller = null)
        {
            _data = data;
            _sourceTeam = sourceTeam;
            _roller = roller ?? new UnityRandomDamageRoller();
            _resolved = false;
            transform.position = origin;
            var toTarget = landing - origin;
            _landing = toTarget.magnitude <= data.ThrowRangeTiles ? landing : origin + toTarget.normalized * data.ThrowRangeTiles;
        }

        private void Update()
        {
            Advance(Time.deltaTime);
        }

        /// <summary>Deterministic stepping (also used by tests).</summary>
        public void Advance(float deltaTime)
        {
            if (_resolved) return;
            var position = (Vector2)transform.position;
            var remaining = _landing - position;
            var step = _data.ThrowSpeed * deltaTime;
            if (remaining.magnitude <= step)
            {
                transform.position = _landing;
                Resolve();
                return;
            }

            transform.position = position + remaining.normalized * step;
        }

        private void Resolve()
        {
            _resolved = true;
            var result = new AreaDamageResolver.Result(0, 0);
            switch (_data.Kind)
            {
                case GrenadeEffectKind.Frag:
                    result = AreaDamageResolver.Apply(_landing, _data.RadiusTiles, _data.DamageMin, _data.DamageMax, DamageKind.Explosion, _data.StaggerPower, _sourceTeam, _roller);
                    break;
                case GrenadeEffectKind.Shock:
                    result = AreaDamageResolver.Apply(_landing, _data.RadiusTiles, _data.DamageMin, _data.DamageMax, DamageKind.Explosion, _data.StaggerPower, _sourceTeam, _roller);
                    break;
                case GrenadeEffectKind.Incendiary:
                    result = AreaDamageResolver.Apply(_landing, _data.RadiusTiles, _data.DamageMin, _data.DamageMax, DamageKind.Explosion, _data.StaggerPower, _sourceTeam, _roller);
                    var burn = new GameObject("BurnZone").AddComponent<BurnZone>();
                    burn.transform.position = _landing;
                    burn.Configure(_data.RadiusTiles, _data.BurnDamagePerSecond, _data.BurnDurationSeconds, _sourceTeam);
                    SpawnedZone = burn.gameObject;
                    break;
                case GrenadeEffectKind.Smoke:
                    var smoke = new GameObject("SmokeZone").AddComponent<SmokeZone>();
                    smoke.transform.position = _landing;
                    smoke.Configure(_data.RadiusTiles, _data.SmokeDurationSeconds);
                    SpawnedZone = smoke.gameObject;
                    break;
            }

            Resolved?.Invoke(this, result);
            Destroy(gameObject);
        }
    }

    /// <summary>Spawns thrown grenades for a thrower (player team by default).</summary>
    public sealed class GrenadeLauncher : MonoBehaviour
    {
        [SerializeField] private DamageTeam _sourceTeam = DamageTeam.Player;

        private IDamageRoller _roller;

        public ThrownGrenade LastThrown { get; private set; }

        public void SetDamageRoller(IDamageRoller roller)
        {
            _roller = roller;
        }

        public ThrownGrenade Throw(GrenadeData data, Vector2 aimDirection)
        {
            var origin = (Vector2)transform.position;
            var direction = aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized : Vector2.right;
            var grenade = new GameObject($"Grenade_{data.Kind}").AddComponent<ThrownGrenade>();
            grenade.Launch(data, origin, origin + direction * data.ThrowRangeTiles, _sourceTeam, _roller);
            LastThrown = grenade;
            return grenade;
        }
    }
}

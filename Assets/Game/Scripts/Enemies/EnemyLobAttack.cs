using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Area;
using RuinRail.Gameplay.Items.Consumables;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies
{
    /// <summary>
    /// Bomber core attack (44): the landing point is locked at telegraph start (the player's position then — an
    /// approximation of where they will be), and after the telegraph a visible <see cref="ThrownGrenade"/> travels to it
    /// and resolves the shared explosion path (Explosion tag, one hit per target, team filter). The flight is the
    /// pre-detonation window during which the landing zone can be shown; <see cref="LandingPoint"/> feeds that marker.
    /// </summary>
    public sealed class EnemyLobAttack : MonoBehaviour, IEnemyAttackBehaviour, IEnemyTelegraphAware
    {
        private EnemyDefinition _definition;
        private IDamageRoller _damageRoller;
        private Vector2 _landing;

        public int BombsThrown { get; private set; }
        public Vector2 LandingPoint => _landing;
        public ThrownGrenade LastGrenade { get; private set; }

        public void Configure(EnemyDefinition definition, IDamageRoller damageRoller)
        {
            _definition = definition;
            _damageRoller = damageRoller;
        }

        public GrenadeData BombData => new()
        {
            Kind = GrenadeEffectKind.Frag,
            RadiusTiles = _definition != null ? _definition.BombRadiusTiles : 0f,
            DamageMin = _definition != null ? _definition.DamageMin : 0,
            DamageMax = _definition != null ? _definition.DamageMax : 0,
            StaggerPower = _definition != null ? _definition.BombStaggerPower : 0f,
            ThrowRangeTiles = _definition != null ? _definition.AttackRange : 1f,
            ThrowSpeed = _definition != null ? _definition.ProjectileSpeed : 1f
        };

        public bool IsTargetInAttackRange(Transform target)
        {
            if (target == null || _definition == null) return false;
            var toTarget = (Vector2)target.position - (Vector2)transform.position;
            if (toTarget.magnitude > _definition.AttackRange) return false;
            return !SmokeZone.IsLineOfSightBlocked(transform.position, target.position, ignoresSmoke: false);
        }

        public void OnTelegraphStarted(Transform target)
        {
            if (target != null) _landing = target.position;
        }

        public bool TryResolveAttack(Transform target)
        {
            if (_definition == null || _damageRoller == null) return false;
            var grenade = new GameObject("EnemyBomb").AddComponent<ThrownGrenade>();
            grenade.Launch(BombData, transform.position, _landing, DamageTeam.Enemy, _damageRoller);
            LastGrenade = grenade;
            BombsThrown++;
            return true;
        }
    }
}

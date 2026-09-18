using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Projectiles;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies.Elites
{
    /// <summary>Spawns one Elite mini-boss (an EliteEncounter wrapping exactly one EliteController) at a position.</summary>
    public interface IEliteSpawner
    {
        EliteEncounter Spawn(EliteDefinition definition, Vector2 position, Transform parent, Transform target);
    }

    /// <summary>
    /// Prefab-less Elite spawn used by the room runtime and tests: encounter root + actor with collider, body, health,
    /// projectile pool (ranged movesets) and the shared stagger/knockback foundation. Presentation is attached later.
    /// </summary>
    public sealed class DefaultEliteSpawner : IEliteSpawner
    {
        private readonly StaggerConfig _staggerConfig;

        public DefaultEliteSpawner(StaggerConfig staggerConfig = null)
        {
            _staggerConfig = staggerConfig;
        }

        public EliteEncounter Spawn(EliteDefinition definition, Vector2 position, Transform parent, Transform target)
        {
            var root = new GameObject(definition != null ? $"EliteEncounter_{definition.Id}" : "EliteEncounter");
            if (parent != null) root.transform.SetParent(parent, false);
            var encounter = root.AddComponent<EliteEncounter>();

            var go = new GameObject(definition != null ? definition.DisplayName : "Elite");
            go.transform.SetParent(root.transform, false);
            go.transform.position = position;
            go.AddComponent<CircleCollider2D>().radius = 0.6f;
            CombatLayers.TagEnemyBody(go);
            var body = go.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            go.AddComponent<HealthComponent>();
            go.AddComponent<TeamMember>().SetTeam(DamageTeam.Enemy);
            CombatHurtbox.Attach(go, CombatHurtbox.EliteSize, CombatHurtbox.EliteOffset);
            var pool = go.AddComponent<ProjectilePool>();
            var elite = go.AddComponent<EliteController>();
            elite.SetProjectilePool(pool);
            if (_staggerConfig != null) elite.SetStaggerConfig(_staggerConfig);
            elite.SetDefinition(definition);
            if (target != null) elite.SetTarget(target);
            encounter.Bind(elite);
            return encounter;
        }
    }
}

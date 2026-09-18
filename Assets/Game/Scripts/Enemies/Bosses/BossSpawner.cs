using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core;
using RuinRail.Core.Rng;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Projectiles;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies.Bosses
{
    /// <summary>What a boss arena asks for: biome, arena tags (a "boss:&lt;id&gt;" tag pins the boss), seed context and placement.</summary>
    public readonly struct BossSpawnRequest
    {
        public BossSpawnRequest(Biome biome, IReadOnlyList<string> arenaTags, int runSeed, int depth, int roomIndex, Vector2 position, Transform parent, Transform target = null)
        {
            Biome = biome;
            ArenaTags = arenaTags ?? Array.Empty<string>();
            RunSeed = runSeed;
            Depth = depth;
            RoomIndex = roomIndex;
            Position = position;
            Parent = parent;
            Target = target;
        }

        public Biome Biome { get; }
        public IReadOnlyList<string> ArenaTags { get; }
        public int RunSeed { get; }
        public int Depth { get; }
        public int RoomIndex { get; }
        public Vector2 Position { get; }
        public Transform Parent { get; }
        public Transform Target { get; }
    }

    /// <summary>
    /// 46: each biome has two Bosses. An arena may pin its boss with a "boss:&lt;id&gt;" tag (id with or without the
    /// "boss_" prefix); otherwise the depth's Dungeon-independent Encounter stream picks one of the biome's bosses.
    /// </summary>
    public static class BossSelection
    {
        public const string TagPrefix = "boss:";
        private const int Salt = 0x424F; // "BO"

        public static BossDefinition Select(IEnumerable<BossDefinition> roster, BossSpawnRequest request)
        {
            var biome = request.Biome;
            var candidates = (roster ?? Enumerable.Empty<BossDefinition>()).Where(b => b != null && b.Biome == biome).OrderBy(b => b.Id, StringComparer.Ordinal).ToList();
            if (candidates.Count == 0) return null;

            foreach (var tag in request.ArenaTags)
            {
                if (string.IsNullOrEmpty(tag) || !tag.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                var wanted = tag.Substring(TagPrefix.Length).Trim().ToLowerInvariant();
                var pinned = candidates.FirstOrDefault(b => b.Id == wanted || b.Id == "boss_" + wanted);
                if (pinned != null) return pinned;
            }

            var random = new SeededRandom(SeededRandom.MixSeed(request.RunSeed, request.Depth, (int)RngStream.Encounter, Salt, request.RoomIndex));
            return candidates[random.NextInt(candidates.Count)];
        }
    }

    /// <summary>
    /// Prefab-less boss spawn used by the room runtime and tests: BossEncounter root + actor with collider, body,
    /// health, team, projectile pool, stagger foundation and the room's enemy spawner for summoning bosses.
    /// </summary>
    public sealed class DefaultBossSpawner
    {
        private readonly List<BossDefinition> _roster;
        private readonly StaggerConfig _staggerConfig;
        private readonly IEnemySpawner _summonSpawner;

        public DefaultBossSpawner(IEnumerable<BossDefinition> roster, StaggerConfig staggerConfig = null, IEnemySpawner summonSpawner = null)
        {
            _roster = (roster ?? throw new ArgumentNullException(nameof(roster))).Where(b => b != null).ToList();
            _staggerConfig = staggerConfig;
            _summonSpawner = summonSpawner;
        }

        public IReadOnlyList<BossDefinition> Roster => _roster;

        public BossDefinition Select(in BossSpawnRequest request) => BossSelection.Select(_roster, request);

        public BossEncounter Spawn(in BossSpawnRequest request)
        {
            var definition = Select(request);
            return definition == null ? null : Spawn(definition, request.Position, request.Parent, request.Target);
        }

        public BossEncounter Spawn(BossDefinition definition, Vector2 position, Transform parent, Transform target = null)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            var root = new GameObject($"BossEncounter_{definition.Id}");
            if (parent != null) root.transform.SetParent(parent, false);
            var encounter = root.AddComponent<BossEncounter>();

            var go = new GameObject(definition.DisplayName);
            go.transform.SetParent(root.transform, false);
            go.transform.position = position;
            go.AddComponent<CircleCollider2D>().radius = 0.8f;
            CombatLayers.TagEnemyBody(go);
            var body = go.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            go.AddComponent<HealthComponent>();
            go.AddComponent<TeamMember>().SetTeam(DamageTeam.Enemy);
            CombatHurtbox.Attach(go, CombatHurtbox.BossSize, CombatHurtbox.BossOffset);
            var pool = go.AddComponent<ProjectilePool>();
            var boss = go.AddComponent<BossController>();
            boss.SetProjectilePool(pool);
            if (_staggerConfig != null) boss.SetStaggerConfig(_staggerConfig);
            boss.SetSummonSpawner(_summonSpawner);
            boss.SetDefinition(definition);
            if (target != null) boss.SetTarget(target);
            encounter.Bind(boss);
            return encounter;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core;
using RuinRail.Core.Rng;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies.Encounters
{
    /// <summary>59 solo threat budget targets, capped after Depth 50; linear between anchors.</summary>
    public static class ThreatBudgetTable
    {
        private static readonly (int depth, float min, float max)[] Anchors =
        {
            (1, 4f, 6f), (3, 5f, 7f), (5, 6f, 8f), (10, 8f, 10f), (20, 10f, 13f), (30, 12f, 15f), (50, 14f, 18f)
        };

        public static (float min, float max) SoloBudget(int depth)
        {
            depth = Mathf.Max(1, depth);
            if (depth >= Anchors[^1].depth) return (Anchors[^1].min, Anchors[^1].max);
            for (var i = 0; i < Anchors.Length - 1; i++)
            {
                var (d0, min0, max0) = Anchors[i];
                var (d1, min1, max1) = Anchors[i + 1];
                if (depth < d0 || depth > d1) continue;
                var t = (depth - d0) / (float)(d1 - d0);
                return (Mathf.Lerp(min0, min1, t), Mathf.Lerp(max0, max1, t));
            }

            return (Anchors[0].min, Anchors[0].max);
        }
    }

    /// <summary>83_COOP_SCALING: fixed for the expedition's starting party size; damage per hit never scales.</summary>
    public static class PartyScaling
    {
        public static int Clamp(int partySize) => Mathf.Clamp(partySize, 1, 3);
        public static float ThreatMultiplier(int partySize) => Clamp(partySize) switch { 1 => 1.0f, 2 => 1.4f, _ => 1.75f };
        public static float NormalEnemyHealthMultiplier(int partySize) => Clamp(partySize) switch { 1 => 1.0f, 2 => 1.2f, _ => 1.35f };
        public static float BossHealthMultiplier(int partySize) => Clamp(partySize) switch { 1 => 1.0f, 2 => 1.65f, _ => 2.2f };
        public static float EnemyDamageMultiplier(int partySize) => 1.0f;
        public static int ActiveNormalCap(int partySize) => Clamp(partySize) switch { 1 => 10, 2 => 14, _ => 18 };
        public const int ActiveEliteCap = 1;
    }

    /// <summary>Everything the director needs to compose one room deterministically.</summary>
    public readonly struct EncounterContext
    {
        public EncounterContext(int runSeed, int depth, int partySize, Biome biome, int roomIndex, IReadOnlyList<string> roomTags = null, int spawnMarkerCount = 0)
        {
            RunSeed = runSeed;
            Depth = Mathf.Max(1, depth);
            PartySize = PartyScaling.Clamp(partySize);
            Biome = biome;
            RoomIndex = roomIndex;
            RoomTags = roomTags ?? Array.Empty<string>();
            SpawnMarkerCount = spawnMarkerCount;
        }

        public int RunSeed { get; }
        public int Depth { get; }
        public int PartySize { get; }
        public Biome Biome { get; }
        public int RoomIndex { get; }
        public IReadOnlyList<string> RoomTags { get; }
        public int SpawnMarkerCount { get; }
    }

    public sealed class EncounterEntry
    {
        public EncounterEntry(EnemyDefinition definition, int count)
        {
            Definition = definition;
            Count = count;
        }

        public EnemyDefinition Definition { get; }
        public int Count { get; internal set; }
        public float Threat => Definition.ThreatCost * Count;
    }

    /// <summary>The composed room: what to spawn, in what amounts, under which caps.</summary>
    public sealed class EncounterPlan
    {
        public EncounterPlan(EncounterContext context, float budgetMin, float budgetMax, float targetThreat, IReadOnlyList<EncounterEntry> entries)
        {
            Context = context;
            BudgetMin = budgetMin;
            BudgetMax = budgetMax;
            TargetThreat = targetThreat;
            Entries = entries;
        }

        public EncounterContext Context { get; }
        public float BudgetMin { get; }
        public float BudgetMax { get; }
        public float TargetThreat { get; }
        public IReadOnlyList<EncounterEntry> Entries { get; }
        public float TotalThreat => Entries.Sum(e => e.Threat);
        public int TotalCount => Entries.Sum(e => e.Count);
        public int RoleCount => Entries.Count;
        public int ActiveCap => PartyScaling.ActiveNormalCap(Context.PartySize);
        public float NormalEnemyHealthMultiplier => PartyScaling.NormalEnemyHealthMultiplier(Context.PartySize);

        public IEnumerable<EnemyDefinition> Expand()
        {
            foreach (var entry in Entries)
            {
                for (var i = 0; i < entry.Count; i++) yield return entry.Definition;
            }
        }

        public string Signature => string.Join(",", Entries.Select(e => $"{e.Definition.Id}x{e.Count}"));
    }

    /// <summary>
    /// Deterministic enemy composition (47, 59, 83, 114): archetypes filtered by unlock depth and room tags, a threat
    /// target drawn from the depth budget scaled by the party multiplier, then a seeded weighted fill over 2-4 roles
    /// bounded by the active cap. Same run seed + depth + room index + party size => same plan.
    /// </summary>
    public static class EncounterDirector
    {
        public const int MinRoles = 2;
        public const int MaxRoles = 4;
        public const string ExcludeTagPrefix = "no_";

        public static bool IsEligible(EnemyDefinition definition, in EncounterContext context)
        {
            if (definition == null || definition.ThreatCost <= 0f) return false;
            if (definition.UnlockDepth > context.Depth) return false;
            foreach (var roomTag in context.RoomTags)
            {
                if (roomTag == null || !roomTag.StartsWith(ExcludeTagPrefix, StringComparison.Ordinal)) continue;
                var excluded = roomTag.Substring(ExcludeTagPrefix.Length);
                if (definition.SpawnTags.Contains(excluded) || definition.Id == excluded) return false;
            }

            return true;
        }

        public static SeededRandom DeriveRandom(in EncounterContext context)
        {
            // The Encounter stream of this depth, further mixed with the room so rooms of one depth differ but reproduce.
            var stream = RngStreams.Derive(context.RunSeed, context.Depth, RngStream.Encounter);
            return new SeededRandom(SeededRandom.MixSeed(stream.NextInt(int.MaxValue), context.RoomIndex, context.PartySize));
        }

        public static EncounterPlan Compose(in EncounterContext context, IEnumerable<EnemyDefinition> archetypes) => Compose(context, archetypes, 1f);

        /// <summary>threatScale > 1 composes a deliberately harder encounter (57 Cursed Chest) on the same seeded path.</summary>
        public static EncounterPlan Compose(in EncounterContext context, IEnumerable<EnemyDefinition> archetypes, float threatScale)
        {
            var ctx = context;
            threatScale = Mathf.Max(0.01f, threatScale);
            var eligible = (archetypes ?? Array.Empty<EnemyDefinition>()).Where(d => IsEligible(d, ctx)).OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
            var (min, max) = ThreatBudgetTable.SoloBudget(ctx.Depth);
            var multiplier = PartyScaling.ThreatMultiplier(ctx.PartySize);
            var random = DeriveRandom(ctx);

            // Target inside the band, then party pressure.
            var target = Mathf.Lerp(min, max, random.NextFloat()) * multiplier * threatScale;
            var cap = PartyScaling.ActiveNormalCap(ctx.PartySize);
            var entries = new List<EncounterEntry>();
            if (eligible.Count == 0) return new EncounterPlan(ctx, min * multiplier * threatScale, max * multiplier * threatScale, target, entries);

            // Roles: 2-4 distinct archetypes when available, picked by seed.
            var roleCount = Mathf.Clamp(random.NextInt(MinRoles, MaxRoles), 1, eligible.Count);
            var pool = new List<EnemyDefinition>(eligible);
            var roles = new List<EnemyDefinition>();
            for (var i = 0; i < roleCount; i++)
            {
                var pick = random.NextInt(pool.Count);
                roles.Add(pool[pick]);
                pool.RemoveAt(pick);
            }

            // Fill: seeded picks among the roles while the next one still fits the target and the active cap.
            var counts = roles.ToDictionary(r => r, _ => 0);
            var threat = 0f;
            var total = 0;
            var cheapest = roles.Min(r => r.ThreatCost);
            var guard = 0;
            while (total < cap && threat + cheapest <= target + 0.0001f && guard++ < 256)
            {
                var candidates = roles.Where(r => threat + r.ThreatCost <= target + 0.0001f).ToList();
                if (candidates.Count == 0) break;
                var role = candidates[random.NextInt(candidates.Count)];
                counts[role]++;
                threat += role.ThreatCost;
                total++;
            }

            if (total == 0)
            {
                // Budget below the cheapest eligible role (never for approved data, but never spawn an empty combat room).
                var role = roles.OrderBy(r => r.ThreatCost).First();
                counts[role] = 1;
            }

            foreach (var role in roles)
            {
                if (counts[role] > 0) entries.Add(new EncounterEntry(role, counts[role]));
            }

            return new EncounterPlan(ctx, min * multiplier * threatScale, max * multiplier * threatScale, target, entries);
        }
    }
}

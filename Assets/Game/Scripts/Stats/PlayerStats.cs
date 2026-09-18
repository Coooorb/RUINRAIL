using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Items;
using UnityEngine;

namespace RuinRail.Gameplay.Stats
{
    /// <summary>Read-only view consumers use; recomputed as a whole whenever a source changes.</summary>
    public interface IPlayerStatsProvider
    {
        /// <summary>Capped percent bonus of a stat in percent points (0 when no source contributes).</summary>
        int GetPercent(StatId stat);

        /// <summary>Uncapped flat contribution of a stat in units.</summary>
        int GetFlat(StatId stat);

        /// <summary>1 + capped percent / 100 (e.g. 1.3 at the +30% Movement cap).</summary>
        float GetMultiplier(StatId stat);

        /// <summary>1 − capped percent / 100 for reduction stats (e.g. 0.65 at 35% Dash Cooldown Reduction).</summary>
        float GetReductionFactor(StatId stat);

        int MaxHealth { get; }
        bool ArmorInjectorActive { get; }
        event Action Recomputed;
    }

    /// <summary>
    /// The single deterministic stat pipeline (player/12_PLAYER_STATS): base stats + every registered modifier source
    /// (skills, armor, accessory intrinsic, affixes, temporary effects) summed per stat, then clamped once at the
    /// approved global cap (player/16_GLOBAL_STAT_CAPS). Sources are keyed by id: adding, replacing or removing one
    /// recomputes everything, and summation makes insertion order irrelevant. Consumers read results; none re-does cap math.
    /// </summary>
    public sealed class PlayerStats : IPlayerStatsProvider
    {
        private readonly Dictionary<string, IStatModifierSource> _sources = new();
        private readonly Dictionary<StatId, int> _percent = new();
        private readonly Dictionary<StatId, int> _flat = new();
        private readonly GlobalStatCapsConfig _caps;
        private bool _armorInjectorActive;

        public PlayerStats(GlobalStatCapsConfig caps, int baseMaxHealth = 100)
        {
            _caps = caps;
            BaseMaxHealth = baseMaxHealth;
            Recompute();
        }

        public int BaseMaxHealth { get; }
        public IReadOnlyCollection<string> SourceIds => _sources.Keys;
        public int MaxHealth { get; private set; }

        public bool ArmorInjectorActive
        {
            get => _armorInjectorActive;
            set
            {
                if (_armorInjectorActive == value) return;
                _armorInjectorActive = value;
                Recompute();
            }
        }

        public event Action Recomputed;

        /// <summary>Adds or replaces the source with the same id, then recomputes.</summary>
        public void SetSource(IStatModifierSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            _sources[source.SourceId] = source;
            Recompute();
        }

        public bool RemoveSource(string sourceId)
        {
            var removed = _sources.Remove(sourceId);
            if (removed) Recompute();
            return removed;
        }

        public void ClearSources()
        {
            _sources.Clear();
            Recompute();
        }

        public int GetPercent(StatId stat) => _percent.TryGetValue(stat, out var v) ? v : 0;

        public int GetFlat(StatId stat) => _flat.TryGetValue(stat, out var v) ? v : 0;

        public float GetMultiplier(StatId stat) => 1f + GetPercent(stat) / 100f;

        public float GetReductionFactor(StatId stat) => Mathf.Max(0f, 1f - GetPercent(stat) / 100f);

        public int CapFor(StatId stat) => _caps != null ? _caps.GetCapPercent(stat, _armorInjectorActive) : 0;

        /// <summary>
        /// Incoming damage after general DR (capped) and, for explosions, the separate Blast-Suit-style reduction applied
        /// multiplicatively to the remainder (12_PLAYER_STATS). Integer result, never below 0.
        /// </summary>
        public int ApplyDamageReduction(int damage, bool isExplosion = false)
        {
            if (damage <= 0) return 0;
            var afterGeneral = damage * (100 - GetPercent(StatId.GeneralDamageReduction)) / 100f;
            if (isExplosion)
            {
                afterGeneral *= (100 - Mathf.Clamp(GetPercentUncapped(StatId.ExplosionDamageReduction), 0, 100)) / 100f;
            }

            return Mathf.Max(0, Mathf.RoundToInt(afterGeneral));
        }

        private int GetPercentUncapped(StatId stat)
        {
            var total = 0;
            foreach (var m in _sources.Values.SelectMany(s => s.GetModifiers()))
            {
                if (m.Stat == stat && m.Kind == StatModifierKind.Percent) total += m.Value;
            }

            return total;
        }

        public void Recompute()
        {
            _percent.Clear();
            _flat.Clear();
            foreach (var modifier in _sources.Values.OrderBy(s => s.SourceId, StringComparer.Ordinal).SelectMany(s => s.GetModifiers()))
            {
                var table = modifier.Kind == StatModifierKind.Percent ? _percent : _flat;
                table.TryGetValue(modifier.Stat, out var current);
                table[modifier.Stat] = current + modifier.Value;
            }

            foreach (var stat in _percent.Keys.ToList())
            {
                var cap = CapFor(stat);
                if (cap > 0 && _percent[stat] > cap)
                {
                    _percent[stat] = cap;
                }
            }

            MaxHealth = Mathf.Max(1, Mathf.RoundToInt((BaseMaxHealth + GetFlat(StatId.MaxHealth)) * GetMultiplier(StatId.MaxHealth)));
            Recomputed?.Invoke();
        }
    }
}

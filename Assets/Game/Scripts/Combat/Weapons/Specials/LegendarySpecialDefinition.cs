using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Weapons.Specials
{
    /// <summary>Which reusable primitive a special is built from. A kind, never a weapon.</summary>
    public enum SpecialKind
    {
        Burst,
        Fan,
        PiercingShot,
        Cone,
        ExplosionSalvo,
        DashStrike
    }

    /// <summary>
    /// Authored data for one fixed Legendary special (25_LEGENDARY_WEAPON_SPECIALS). The weapon definition names it
    /// through EquipmentItemDefinition.LegendaryMechanicId; <see cref="LegendarySpecialFactory"/> turns it into the
    /// matching primitive. Fields not used by a kind are ignored.
    /// </summary>
    [CreateAssetMenu(fileName = "LegendarySpecialDefinition", menuName = "RuinRail/Items/Legendary Special")]
    public sealed class LegendarySpecialDefinition : ScriptableObject
    {
        [SerializeField] private string _id;
        [SerializeField] private string _displayName;
        [SerializeField] private SpecialKind _kind;
        [SerializeField, Min(0f)] private float _cooldownSeconds = 10f;
        [SerializeField, Min(0)] private int _damageMin;
        [SerializeField, Min(0)] private int _damageMax;

        [Header("Projectiles (Burst / Fan / PiercingShot)")]
        [SerializeField, Min(1)] private int _shots = 1;
        [SerializeField, Min(0f)] private float _intervalSeconds;
        [SerializeField, Range(0f, 360f)] private float _arcDegrees;
        [SerializeField, Min(0f)] private float _projectileSpeed;
        [SerializeField, Min(0f)] private float _rangeTiles;

        [Header("Impact")]
        [SerializeField, Min(0f)] private float _knockback;
        [SerializeField, Min(0f)] private float _staggerPower;

        [Header("Explosions (ExplosionSalvo)")]
        [SerializeField, Min(0f)] private float _radiusTiles;
        [SerializeField, Min(0f)] private float _firstDistanceTiles;
        [SerializeField, Min(0f)] private float _spacingTiles;

        [Header("Movement (DashStrike)")]
        [SerializeField, Min(0f)] private float _distanceTiles;
        [SerializeField, Min(0.01f)] private float _durationSeconds = 0.2f;
        [SerializeField, Min(0.05f)] private float _hitRadiusTiles = 0.6f;

        public string Id => _id;
        public string DisplayName => _displayName;
        public SpecialKind Kind => _kind;
        public float CooldownSeconds => _cooldownSeconds;
        public int DamageMin => _damageMin;
        public int DamageMax => Mathf.Max(_damageMin, _damageMax);
        public int Shots => Mathf.Max(1, _shots);
        public float IntervalSeconds => _intervalSeconds;
        public float ArcDegrees => _arcDegrees;
        public float ProjectileSpeed => _projectileSpeed;
        public float RangeTiles => _rangeTiles;
        public float Knockback => _knockback;
        public float StaggerPower => _staggerPower;
        public float RadiusTiles => _radiusTiles;
        public float FirstDistanceTiles => _firstDistanceTiles;
        public float SpacingTiles => _spacingTiles;
        public float DistanceTiles => _distanceTiles;
        public float DurationSeconds => _durationSeconds;
        public float HitRadiusTiles => _hitRadiusTiles;
    }

    /// <summary>Builds the primitive for a definition. Switches on the kind of effect, never on a weapon id.</summary>
    public static class LegendarySpecialFactory
    {
        public static ILegendarySpecial Create(LegendarySpecialDefinition d)
        {
            if (d == null) throw new ArgumentNullException(nameof(d));
            var band = new DamageBand(d.DamageMin, d.DamageMax);
            return d.Kind switch
            {
                SpecialKind.Burst => new BurstSpecial(d.Id, d.CooldownSeconds, d.Shots, d.IntervalSeconds, band, d.ProjectileSpeed, d.RangeTiles, d.Knockback, d.StaggerPower),
                SpecialKind.Fan => new FanSpecial(d.Id, d.CooldownSeconds, d.Shots, d.ArcDegrees, band, d.ProjectileSpeed, d.RangeTiles, d.Knockback, d.StaggerPower),
                SpecialKind.PiercingShot => new PiercingShotSpecial(d.Id, d.CooldownSeconds, band, d.ProjectileSpeed, d.RangeTiles, d.Knockback, d.StaggerPower),
                SpecialKind.Cone => new ConeSpecial(d.Id, d.CooldownSeconds, band, d.RangeTiles, d.ArcDegrees, d.Knockback, d.StaggerPower),
                SpecialKind.ExplosionSalvo => new ExplosionSalvoSpecial(d.Id, d.CooldownSeconds, d.Shots, band, d.RadiusTiles, d.FirstDistanceTiles, d.SpacingTiles, d.Knockback, d.StaggerPower),
                SpecialKind.DashStrike => new DashStrikeSpecial(d.Id, d.CooldownSeconds, d.DistanceTiles, d.DurationSeconds, band, d.HitRadiusTiles, d.Knockback, d.StaggerPower),
                _ => throw new ArgumentOutOfRangeException(nameof(d), d.Kind, "Unknown special kind.")
            };
        }
    }

    /// <summary>Lookup from a weapon's LegendaryMechanicId to its authored special.</summary>
    public sealed class LegendarySpecialRegistry
    {
        private readonly Dictionary<string, LegendarySpecialDefinition> _byId = new(StringComparer.Ordinal);

        public LegendarySpecialRegistry(IEnumerable<LegendarySpecialDefinition> definitions)
        {
            foreach (var definition in definitions ?? Array.Empty<LegendarySpecialDefinition>())
            {
                if (definition == null || string.IsNullOrEmpty(definition.Id)) continue;
                if (_byId.ContainsKey(definition.Id)) throw new ArgumentException($"Duplicate special id '{definition.Id}'.");
                _byId.Add(definition.Id, definition);
            }
        }

        public int Count => _byId.Count;
        public IEnumerable<LegendarySpecialDefinition> Definitions => _byId.Values;

        public bool TryGet(string mechanicId, out LegendarySpecialDefinition definition)
        {
            definition = null;
            return !string.IsNullOrEmpty(mechanicId) && _byId.TryGetValue(mechanicId, out definition);
        }

        public ILegendarySpecial CreateFor(string mechanicId) => TryGet(mechanicId, out var d) ? LegendarySpecialFactory.Create(d) : null;
    }
}

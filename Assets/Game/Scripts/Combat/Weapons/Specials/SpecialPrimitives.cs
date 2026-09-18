using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Combat.Area;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Projectiles;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Weapons.Specials
{
    /// <summary>Integer damage band shared by every special (whole-number rolls, no crits).</summary>
    [Serializable]
    public struct DamageBand
    {
        public int Min;
        public int Max;

        public DamageBand(int min, int max)
        {
            Min = Mathf.Max(0, min);
            Max = Mathf.Max(Min, max);
        }

        public int Roll(IDamageRoller roller) => roller.Roll(Min, Max);
    }

    /// <summary>Shared projectile emission for specials: fixed damage band, no weapon affix scaling.</summary>
    internal static class SpecialShots
    {
        public static Projectile Fire(SpecialContext ctx, Vector2 direction, DamageBand damage, float speed, float range, float knockback = 0f, float stagger = 0f, bool piercing = false, float explosionRadius = 0f)
        {
            if (ctx.Pool == null) return null;
            var data = new ProjectileSpawnData(damage.Roll(ctx.Roller), speed, range, knockback, stagger, direction, ctx.Owner, ctx.Feedback, explosionRadius, ctx.SourceTeam, piercing);
            return ctx.Pool.Spawn(ctx.SpawnPosition, data);
        }
    }

    /// <summary>Sequential shots at a fixed interval along the current aim (Snapfire, Overrun, Overcharge Barrage).</summary>
    public sealed class BurstSpecial : ILegendarySpecial
    {
        public BurstSpecial(string id, float cooldownSeconds, int shots, float intervalSeconds, DamageBand damage, float speed, float range, float knockback = 0f, float stagger = 0f)
        {
            Id = id;
            CooldownSeconds = cooldownSeconds;
            Shots = Mathf.Max(1, shots);
            IntervalSeconds = Mathf.Max(0f, intervalSeconds);
            Damage = damage;
            Speed = speed;
            Range = range;
            Knockback = knockback;
            Stagger = stagger;
        }

        public string Id { get; }
        public float CooldownSeconds { get; }
        public int Shots { get; }
        public float IntervalSeconds { get; }
        public DamageBand Damage { get; }
        public float Speed { get; }
        public float Range { get; }
        public float Knockback { get; }
        public float Stagger { get; }

        public ISpecialExecution Begin(SpecialContext context)
        {
            var execution = new Execution(this, context);
            execution.Tick(0f); // the first shot leaves immediately
            return execution.IsComplete ? null : execution;
        }

        private sealed class Execution : ISpecialExecution
        {
            private readonly BurstSpecial _special;
            private readonly SpecialContext _ctx;
            private float _untilNext;
            private int _fired;

            public Execution(BurstSpecial special, SpecialContext ctx) { _special = special; _ctx = ctx; }
            public bool IsComplete => _fired >= _special.Shots;
            public bool LocksMovement => false;

            public void Tick(float deltaTime)
            {
                _untilNext -= deltaTime;
                while (!IsComplete && _untilNext <= 0f)
                {
                    SpecialShots.Fire(_ctx, _ctx.AimDirection, _special.Damage, _special.Speed, _special.Range, _special.Knockback, _special.Stagger);
                    _fired++;
                    _untilNext += _special.IntervalSeconds;
                    if (_special.IntervalSeconds <= 0f) _untilNext = 0f;
                }
            }
        }
    }

    /// <summary>All shots at once, evenly spread across an arc centred on the aim (Lead Bloom, Arrow Storm).</summary>
    public sealed class FanSpecial : ILegendarySpecial
    {
        public FanSpecial(string id, float cooldownSeconds, int shots, float arcDegrees, DamageBand damage, float speed, float range, float knockback = 0f, float stagger = 0f)
        {
            Id = id;
            CooldownSeconds = cooldownSeconds;
            Shots = Mathf.Max(1, shots);
            ArcDegrees = Mathf.Clamp(arcDegrees, 0f, 360f);
            Damage = damage;
            Speed = speed;
            Range = range;
            Knockback = knockback;
            Stagger = stagger;
        }

        public string Id { get; }
        public float CooldownSeconds { get; }
        public int Shots { get; }
        public float ArcDegrees { get; }
        public DamageBand Damage { get; }
        public float Speed { get; }
        public float Range { get; }
        public float Knockback { get; }
        public float Stagger { get; }

        /// <summary>Deterministic even distribution: shot i sits at -arc/2 + i * arc/(n-1).</summary>
        public static void ResolveDirections(Vector2 aim, int shots, float arcDegrees, List<Vector2> directions)
        {
            directions.Clear();
            aim = aim.sqrMagnitude > 0.0001f ? aim.normalized : Vector2.right;
            if (shots == 1 || arcDegrees <= 0f)
            {
                for (var i = 0; i < shots; i++) directions.Add(aim);
                return;
            }

            var step = arcDegrees / (shots - 1);
            for (var i = 0; i < shots; i++)
            {
                var angle = -arcDegrees * 0.5f + i * step;
                directions.Add(Quaternion.Euler(0f, 0f, angle) * aim);
            }
        }

        public ISpecialExecution Begin(SpecialContext context)
        {
            var directions = new List<Vector2>(Shots);
            ResolveDirections(context.AimDirection, Shots, ArcDegrees, directions);
            foreach (var direction in directions)
            {
                SpecialShots.Fire(context, direction, Damage, Speed, Range, Knockback, Stagger);
            }

            return null;
        }
    }

    /// <summary>One shot that passes through every enemy in its path (Piercing Line, Rail Shot).</summary>
    public sealed class PiercingShotSpecial : ILegendarySpecial
    {
        public PiercingShotSpecial(string id, float cooldownSeconds, DamageBand damage, float speed, float range, float knockback = 0f, float stagger = 0f)
        {
            Id = id;
            CooldownSeconds = cooldownSeconds;
            Damage = damage;
            Speed = speed;
            Range = range;
            Knockback = knockback;
            Stagger = stagger;
        }

        public string Id { get; }
        public float CooldownSeconds { get; }
        public DamageBand Damage { get; }
        public float Speed { get; }
        public float Range { get; }
        public float Knockback { get; }
        public float Stagger { get; }

        public ISpecialExecution Begin(SpecialContext context)
        {
            SpecialShots.Fire(context, context.AimDirection, Damage, Speed, Range, Knockback, Stagger, piercing: true);
            return null;
        }
    }

    /// <summary>Instant close-range cone (Concussion Blast): every enemy in range and arc takes the band once, plus knockback/stagger.</summary>
    public sealed class ConeSpecial : ILegendarySpecial
    {
        private static readonly Collider2D[] Overlaps = new Collider2D[64];

        public ConeSpecial(string id, float cooldownSeconds, DamageBand damage, float rangeTiles, float arcDegrees, float knockback = 0f, float stagger = 0f)
        {
            Id = id;
            CooldownSeconds = cooldownSeconds;
            Damage = damage;
            RangeTiles = Mathf.Max(0f, rangeTiles);
            ArcDegrees = Mathf.Clamp(arcDegrees, 0f, 360f);
            Knockback = knockback;
            Stagger = stagger;
        }

        public string Id { get; }
        public float CooldownSeconds { get; }
        public DamageBand Damage { get; }
        public float RangeTiles { get; }
        public float ArcDegrees { get; }
        public float Knockback { get; }
        public float Stagger { get; }
        public int LastTargetsHit { get; private set; }

        public ISpecialExecution Begin(SpecialContext context)
        {
            LastTargetsHit = 0;
            var origin = context.SpawnPosition;
            var aim = context.AimDirection;
            var halfArc = ArcDegrees * 0.5f;
            var count = Physics2D.OverlapCircle(origin, RangeTiles, Physics2DQueries.LegacyQueryFilter(), Overlaps);
            var seen = new HashSet<IDamageable>();
            for (var i = 0; i < count; i++)
            {
                var collider = Overlaps[i];
                if (context.Owner != null && (collider.transform == context.Owner.transform || collider.transform.IsChildOf(context.Owner.transform))) continue;
                if (TeamMember.TeamOf(collider) == context.SourceTeam) continue;
                var damageable = collider.GetComponentInParent<IDamageable>();
                if (damageable == null || !seen.Add(damageable)) continue;
                var toTarget = (Vector2)collider.transform.position - origin;
                if (toTarget.sqrMagnitude > 0.0001f && Vector2.Angle(aim, toTarget) > halfArc) continue;

                if (damageable.TryApplyDamage(new DamageRequest(Damage.Roll(context.Roller))))
                {
                    LastTargetsHit++;
                    ImpactDispatcher.Apply(collider, new ImpactRequest(toTarget.sqrMagnitude > 0.0001f ? toTarget : aim, Knockback, Stagger, DamageKind.Normal, context.Owner, context.Feedback));
                }
            }

            return null;
        }
    }

    /// <summary>Several explosions placed along the aim line (Meteor Salvo): each one is the shared explosion path.</summary>
    public sealed class ExplosionSalvoSpecial : ILegendarySpecial
    {
        private static readonly IDamageRoller ExactRoller = new UnityRandomDamageRoller();

        public ExplosionSalvoSpecial(string id, float cooldownSeconds, int explosions, DamageBand damage, float radiusTiles, float firstDistanceTiles, float spacingTiles, float knockback = 0f, float stagger = 0f)
        {
            Id = id;
            CooldownSeconds = cooldownSeconds;
            Explosions = Mathf.Max(1, explosions);
            Damage = damage;
            RadiusTiles = Mathf.Max(0.01f, radiusTiles);
            FirstDistanceTiles = Mathf.Max(0f, firstDistanceTiles);
            SpacingTiles = Mathf.Max(0f, spacingTiles);
            Knockback = knockback;
            Stagger = stagger;
        }

        public string Id { get; }
        public float CooldownSeconds { get; }
        public int Explosions { get; }
        public DamageBand Damage { get; }
        public float RadiusTiles { get; }
        public float FirstDistanceTiles { get; }
        public float SpacingTiles { get; }
        public float Knockback { get; }
        public float Stagger { get; }
        public IReadOnlyList<Vector2> LastCentres => _lastCentres;
        private readonly List<Vector2> _lastCentres = new();

        public ISpecialExecution Begin(SpecialContext context)
        {
            _lastCentres.Clear();
            var origin = context.SpawnPosition;
            var aim = context.AimDirection;
            for (var i = 0; i < Explosions; i++)
            {
                var centre = origin + aim * (FirstDistanceTiles + i * SpacingTiles);
                _lastCentres.Add(centre);
                var damage = Damage.Roll(context.Roller);
                AreaDamageResolver.Apply(centre, RadiusTiles, damage, damage, DamageKind.Explosion, Stagger, context.SourceTeam, ExactRoller, Knockback, context.Owner, context.Feedback);
            }

            return null;
        }
    }

    /// <summary>
    /// Move the wielder a fixed distance along the aim over a short time, damaging every enemy crossed once
    /// (Blink Strike, Impaling Charge). Owns the body while running; stops early at walls.
    /// </summary>
    public sealed class DashStrikeSpecial : ILegendarySpecial
    {
        private static readonly RaycastHit2D[] Hits = new RaycastHit2D[16];
        private static readonly Collider2D[] Overlaps = new Collider2D[32];

        public DashStrikeSpecial(string id, float cooldownSeconds, float distanceTiles, float durationSeconds, DamageBand damage, float hitRadiusTiles, float knockback = 0f, float stagger = 0f)
        {
            Id = id;
            CooldownSeconds = cooldownSeconds;
            DistanceTiles = Mathf.Max(0f, distanceTiles);
            DurationSeconds = Mathf.Max(0.01f, durationSeconds);
            Damage = damage;
            HitRadiusTiles = Mathf.Max(0.05f, hitRadiusTiles);
            Knockback = knockback;
            Stagger = stagger;
        }

        public string Id { get; }
        public float CooldownSeconds { get; }
        public float DistanceTiles { get; }
        public float DurationSeconds { get; }
        public DamageBand Damage { get; }
        public float HitRadiusTiles { get; }
        public float Knockback { get; }
        public float Stagger { get; }

        public ISpecialExecution Begin(SpecialContext context)
        {
            return context.Body == null ? null : new Execution(this, context);
        }

        private sealed class Execution : ISpecialExecution
        {
            private readonly DashStrikeSpecial _special;
            private readonly SpecialContext _ctx;
            private readonly Vector2 _direction;
            private readonly HashSet<IDamageable> _hit = new();
            private float _remaining;
            private Vector2 _current;
            private bool _started;

            public Execution(DashStrikeSpecial special, SpecialContext ctx)
            {
                _special = special;
                _ctx = ctx;
                _direction = ctx.AimDirection;
                _remaining = special.DistanceTiles;
            }

            public bool IsComplete => _remaining <= 0f;
            public bool LocksMovement => true;
            public int TargetsHit => _hit.Count;

            public void Tick(float deltaTime)
            {
                if (IsComplete || deltaTime <= 0f) return;
                var body = _ctx.Body;
                if (!_started)
                {
                    // Track our own position: several Update ticks may land between two physics steps and MovePosition only keeps the last.
                    _current = body.position;
                    _started = true;
                }

                var step = Mathf.Min(_special.DistanceTiles * deltaTime / _special.DurationSeconds, _remaining);

                // Walls end the dash (no clipping through geometry); enemies are crossed, not blocked.
                var count = Physics2D.Raycast(_current, _direction, Physics2DQueries.LegacyQueryFilter(), Hits, step + 0.1f);
                for (var i = 0; i < count; i++)
                {
                    var hit = Hits[i];
                    if (hit.collider == null || hit.collider.transform == body.transform || hit.collider.transform.IsChildOf(body.transform)) continue;
                    if (hit.collider.GetComponentInParent<EnvironmentObstacle>() == null) continue;
                    step = Mathf.Max(0f, hit.distance - 0.05f);
                    _remaining = step;
                    break;
                }

                var to = _current + _direction * step;
                body.MovePosition(to);
                _current = to;
                _remaining -= step;
                if (_remaining < 0.0001f) _remaining = 0f;
                Strike(to);
            }

            private void Strike(Vector2 centre)
            {
                var count = Physics2D.OverlapCircle(centre, _special.HitRadiusTiles, Physics2DQueries.LegacyQueryFilter(), Overlaps);
                for (var i = 0; i < count; i++)
                {
                    var collider = Overlaps[i];
                    if (_ctx.Owner != null && (collider.transform == _ctx.Owner.transform || collider.transform.IsChildOf(_ctx.Owner.transform))) continue;
                    if (TeamMember.TeamOf(collider) == _ctx.SourceTeam) continue;
                    var damageable = collider.GetComponentInParent<IDamageable>();
                    if (damageable == null || !_hit.Add(damageable)) continue;
                    if (damageable.TryApplyDamage(new DamageRequest(_special.Damage.Roll(_ctx.Roller))))
                    {
                        ImpactDispatcher.Apply(collider, new ImpactRequest(_direction, _special.Knockback, _special.Stagger, DamageKind.Normal, _ctx.Owner, _ctx.Feedback));
                    }
                }
            }
        }
    }
}

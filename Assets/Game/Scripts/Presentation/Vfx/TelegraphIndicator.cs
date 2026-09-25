using RuinRail.Gameplay;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using UnityEngine;

namespace RuinRail.Presentation.Vfx
{
    /// <summary>
    /// Sound-independent danger marker (art/103: telegraph animation matters more than realism; art/104 readability):
    /// while the authoritative controller is in Telegraph, a pooled ground marker shows the attack's real shape
    /// (HitRadius circle, ZoneLength×ZoneWidth box along the locked direction, dash lane, or attack range for normal
    /// enemies) and fills over the telegraph duration. Elites/Bosses use the stronger colour. It reads state only.
    /// </summary>
    public sealed class TelegraphIndicator : MonoBehaviour
    {
        [SerializeField] private FeedbackConfig _config;
        [SerializeField] private EffectPool _pool;

        private EnemyController _enemy;
        private MovesetActorController _actor;
        private PooledEffect _marker;
        private float _elapsed;
        private bool _wasTelegraphing;

        public bool IsShowing => _marker != null && _marker.IsActive;
        public float Fill01 { get; private set; }
        public Vector2 MarkerScale { get; private set; }
        public Color MarkerColor { get; private set; }
        public int Shown { get; private set; }
        public bool IsEliteOrBoss => _actor != null || (_replica != null && _replica.IsEliteOrBoss);

        private IReplicatedActorView _replica;

        /// <summary>Co-op client: the marker follows a host-replicated actor view (state, locked facing, current attack).</summary>
        public void ConfigureReplica(FeedbackConfig config, EffectPool pool, IReplicatedActorView replica)
        {
            _config = config;
            _pool = pool;
            _enemy = null;
            _actor = null;
            _replica = replica;
        }

        public void Configure(FeedbackConfig config, EffectPool pool, EnemyController enemy, MovesetActorController actor)
        {
            _config = config;
            _pool = pool;
            _enemy = enemy;
            _actor = actor;
        }

        private void Awake()
        {
            if (_enemy == null) _enemy = GetComponent<EnemyController>();
            if (_actor == null) _actor = GetComponent<MovesetActorController>();
        }

        private bool Telegraphing => _replica != null
            ? !_replica.IsDead && (_replica.IsMoveset ? _replica.MovesetState == MovesetActorState.Telegraph : _replica.EnemyState == EnemyState.Telegraph)
            : _actor != null ? _actor.State == MovesetActorState.Telegraph : _enemy != null && _enemy.State == EnemyState.Telegraph;

        private float TelegraphSeconds => _replica != null
            ? (_replica.IsMoveset ? (_replica.CurrentAttack != null ? _replica.CurrentAttack.TelegraphSeconds : 0f) : _replica.EnemyDefinition != null ? _replica.EnemyDefinition.AttackTelegraphSeconds : 0f)
            : _actor != null ? (_actor.CurrentAttack != null ? _actor.CurrentAttack.TelegraphSeconds : 0f) : _enemy != null ? _enemy.CurrentTelegraphSeconds : 0f;

        /// <summary>World-space size (x = along the attack direction, y = across) and centre offset of the danger shape.</summary>
        public static (Vector2 size, Vector2 offset) ShapeFor(EnemyAttackDefinition attack, Vector2 direction)
        {
            if (attack == null) return (Vector2.one, Vector2.zero);
            var dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
            switch (attack.Motion)
            {
                case AttackMotion.Zone:
                    return (new Vector2(attack.ZoneLength, attack.ZoneWidth), dir * (attack.ZoneLength * 0.5f));
                case AttackMotion.Dash:
                    return (new Vector2(attack.DashDistance + attack.HitRadius, attack.HitRadius * 2f), dir * (attack.DashDistance * 0.5f));
                case AttackMotion.Projectile:
                    return (new Vector2(attack.ProjectileRange, Mathf.Max(0.5f, attack.HitRadius * 2f)), dir * (attack.ProjectileRange * 0.5f));
                case AttackMotion.Slam:
                    return (Vector2.one * (attack.HitRadius * 2f), Vector2.zero);
                default:
                    return (Vector2.one * (attack.HitRadius * 2f), dir * attack.HitRadius);
            }
        }

        public void Tick(float deltaTime)
        {
            var telegraphing = Telegraphing;
            if (telegraphing && !_wasTelegraphing)
            {
                _elapsed = 0f;
                Shown++;
            }

            _wasTelegraphing = telegraphing;
            if (!telegraphing)
            {
                if (_marker != null && _marker.IsActive) _pool?.Return(_marker);
                _marker = null;
                Fill01 = 0f;
                return;
            }

            _elapsed += deltaTime;
            var seconds = TelegraphSeconds;
            Fill01 = seconds <= 0f ? 1f : Mathf.Clamp01(_elapsed / seconds);
            MarkerColor = _config == null ? Color.red : IsEliteOrBoss ? _config.EliteBossTelegraphColor : _config.EnemyTelegraphColor;
            var color = MarkerColor;
            color.a = Mathf.Lerp(color.a * 0.5f, color.a, Fill01);

            var shape = _replica != null ? ShapeOf(_replica, transform.position) : _actor != null ? ShapeOf(_actor) : ShapeOf(_enemy, transform.position);
            MarkerScale = shape.Size;
            MarkerKind = shape.Kind;
            if (_pool == null) return;
            var position = shape.Centre;
            if (_marker == null || !_marker.IsActive || _marker.Kind != shape.Kind)
            {
                if (_marker != null && _marker.IsActive) _pool.Return(_marker);
                _marker = _pool.Spawn(shape.Kind, position, seconds > 0f ? seconds + 0.5f : 1f, color, 1f, shape.AngleDegrees);
            }
            else
            {
                _marker.transform.position = new Vector3(position.x, position.y, 0f);
                _marker.transform.rotation = Quaternion.Euler(0f, 0f, shape.AngleDegrees);
                _marker.Renderer.color = color;
            }

            // The marker is drawn at the real world footprint of the mechanic, whatever the sprite's pixel size.
            _marker.SetWorldSize(shape.Size);
        }

        /// <summary>The effect kind ('telegraph_*') the current marker uses (tests/diagnostics).</summary>
        public string MarkerKind { get; private set; } = string.Empty;

        /// <summary>One danger shape: which telegraph art, its world footprint (x along the facing, y across), centre and facing.</summary>
        public readonly struct DangerShape
        {
            public DangerShape(string kind, Vector2 size, Vector2 centre, float angleDegrees)
            {
                Kind = kind;
                Size = size;
                Centre = centre;
                AngleDegrees = angleDegrees;
            }

            public string Kind { get; }
            public Vector2 Size { get; }
            public Vector2 Centre { get; }
            public float AngleDegrees { get; }
        }

        private static readonly RaycastHit2D[] LaneHits = new RaycastHit2D[8];
        public const string KindStationary = "telegraph_stationary";
        public const string KindDash = "telegraph_dash";
        public const string KindProjectile = "telegraph_projectile";
        public const string KindZone = "telegraph_zone";
        public const string KindSlam = "telegraph_slam";

        private static float AngleOf(Vector2 direction) => Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        /// <summary>Elite/Boss moveset attacks: the authored motion decides the art, the definition decides the footprint.</summary>
        public static DangerShape ShapeOf(MovesetActorController actor)
        {
            var direction = actor.LockedDirection.sqrMagnitude > 0.0001f ? actor.LockedDirection.normalized : Vector2.right;
            var (size, offset) = ShapeFor(actor.CurrentAttack, direction);
            var kind = actor.CurrentAttack != null ? KindOf(actor.CurrentAttack.Motion) : KindStationary;
            return new DangerShape(kind, size, (Vector2)actor.transform.position + offset, AngleOf(direction));
        }

        /// <summary>
        /// A replicated actor's danger shape from what the host sent: the moveset attack and its locked direction for
        /// Elites/Bosses, the authored attack kind along the replicated facing for normal enemies.
        /// </summary>
        public static DangerShape ShapeOf(IReplicatedActorView replica, Vector2 position)
        {
            var direction = replica.Facing.sqrMagnitude > 0.0001f ? replica.Facing.normalized : Vector2.right;
            if (replica.IsMoveset)
            {
                var (size, offset) = ShapeFor(replica.CurrentAttack, direction);
                var kind = replica.CurrentAttack != null ? KindOf(replica.CurrentAttack.Motion) : KindStationary;
                return new DangerShape(kind, size, position + offset, AngleOf(direction));
            }

            var definition = replica.EnemyDefinition;
            if (definition == null) return new DangerShape(KindStationary, Vector2.one, position, 0f);
            switch (definition.AttackKind)
            {
                case EnemyAttackKind.Projectile:
                {
                    var length = Mathf.Max(1f, definition.ProjectileRange);
                    return new DangerShape(KindProjectile, new Vector2(length, 0.6f), position + direction * (length * 0.5f), AngleOf(direction));
                }
                case EnemyAttackKind.Lob:
                {
                    var radius = Mathf.Max(0.5f, definition.BombRadiusTiles);
                    return new DangerShape(KindSlam, Vector2.one * (radius * 2f), position + direction * definition.AttackRange, 0f);
                }
                case EnemyAttackKind.Charge:
                {
                    var (size, offset) = ShapeFor(definition.ChargeAttack, direction);
                    return new DangerShape(KindDash, size, position + offset, AngleOf(direction));
                }
                default:
                {
                    var reach = Mathf.Max(0.5f, definition.AttackRange);
                    return new DangerShape(KindStationary, Vector2.one * (reach * 2f), position, 0f);
                }
            }
        }

        public static string KindOf(AttackMotion motion) => motion switch
        {
            AttackMotion.Dash => KindDash,
            AttackMotion.Projectile => KindProjectile,
            AttackMotion.Zone => KindZone,
            AttackMotion.Slam => KindSlam,
            _ => KindStationary
        };

        /// <summary>
        /// Normal enemies: the shape is what the attack actually does, never a square of the whole attack range —
        /// a melee contact is a ring of its reach around the body, a shot is a narrow lane along the locked direction
        /// to its range, a lob is a ring at the landing point of its blast radius, a charge is the dash lane, a
        /// moveset move uses that move's authored motion.
        /// </summary>
        public static DangerShape ShapeOf(EnemyController enemy, Vector2 position)
        {
            var definition = enemy != null ? enemy.Definition : null;
            var toTarget = enemy != null && enemy.Target != null ? (Vector2)enemy.Target.position - position : Vector2.right;
            var facing = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector2.right;
            if (definition == null) return new DangerShape(KindStationary, Vector2.one, position, 0f);

            switch (definition.AttackKind)
            {
                case EnemyAttackKind.Projectile:
                {
                    var locked = enemy.Attack is EnemyProjectileAttack shot && shot.LockedDirection.sqrMagnitude > 0.0001f ? shot.LockedDirection.normalized : facing;
                    var length = Mathf.Max(1f, definition.ProjectileRange);
                    // The lane ends where the shot would: at the first solid wall along the locked direction.
                    var blocked = Physics2D.Raycast(position + locked * 0.4f, locked, Physics2DQueries.LegacyQueryFilter(), LaneHits, length);
                    for (var i = 0; i < blocked; i++)
                    {
                        var hit = LaneHits[i];
                        if (hit.collider == null || hit.collider.isTrigger || hit.collider.GetComponentInParent<EnvironmentObstacle>() == null) continue;
                        length = Mathf.Max(1f, hit.distance + 0.4f);
                    }

                    return new DangerShape(KindProjectile, new Vector2(length, 0.6f), position + locked * (length * 0.5f), AngleOf(locked));
                }
                case EnemyAttackKind.Lob:
                {
                    var radius = Mathf.Max(0.5f, definition.BombRadiusTiles);
                    var landing = enemy.Attack is EnemyLobAttack lob && lob.LandingPoint.sqrMagnitude > 0.0001f ? lob.LandingPoint : position + facing * Mathf.Min(definition.AttackRange, toTarget.magnitude);
                    return new DangerShape(KindSlam, Vector2.one * (radius * 2f), landing, 0f);
                }
                case EnemyAttackKind.Charge:
                {
                    var charge = definition.ChargeAttack;
                    var locked = enemy.Attack is EnemyChargeAttack chargeAttack && chargeAttack.LockedDirection.sqrMagnitude > 0.0001f ? chargeAttack.LockedDirection.normalized : facing;
                    var (size, offset) = ShapeFor(charge, locked);
                    return new DangerShape(KindDash, size, position + offset, AngleOf(locked));
                }
                case EnemyAttackKind.Moveset:
                {
                    var moveset = enemy.Attack as EnemyMovesetAttack;
                    var attack = moveset != null ? (moveset.Resolver != null && moveset.Resolver.Current != null ? moveset.Resolver.Current : moveset.PendingAttack) : null;
                    var locked = moveset != null && moveset.LockedDirection.sqrMagnitude > 0.0001f ? moveset.LockedDirection.normalized : facing;
                    var (size, offset) = ShapeFor(attack, locked);
                    return new DangerShape(attack != null ? KindOf(attack.Motion) : KindStationary, size, position + offset, AngleOf(locked));
                }
                default:
                {
                    var reach = Mathf.Max(0.5f, definition.AttackRange);
                    return new DangerShape(KindStationary, Vector2.one * (reach * 2f), position, 0f);
                }
            }
        }

        private void Update() => Tick(Time.deltaTime);

        private void OnDisable()
        {
            if (_marker != null && _marker.IsActive) _pool?.Return(_marker);
            _marker = null;
        }
    }
}

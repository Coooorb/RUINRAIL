using System.Collections.Generic;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Bosses;
using UnityEngine;

namespace RuinRail.Presentation.Vfx
{
    /// <summary>
    /// The ground danger marker of an enemy, Elite or Boss attack (art/103: the telegraph matters more than realism;
    /// art/104 readability). It reads the authoritative actor (or a co-op client's replicated view of it) and draws
    /// exactly the footprint the attack strikes — the same <see cref="AttackFootprint"/> shapes the resolver queries — so a
    /// player outside the marker is outside the hit. Timing follows the committed telegraph (boss phase and attack-speed
    /// scaling included): the fill sweeps from the source and reaches the far edge on the frame the attack commits, the
    /// edge blinks white-hot in the final moment, and the footprint flashes on impact. Danger that outlasts the telegraph
    /// stays drawn: later hit windows of a multi-hit move, a dash in progress, volleys still to fire, a lobbed bomb still in
    /// the air. Presentation only: it never drives the actor.
    /// </summary>
    public sealed class TelegraphIndicator : MonoBehaviour
    {
        [SerializeField] private FeedbackConfig _config;
        [SerializeField] private EffectPool _pool;

        private EnemyController _enemy;
        private MovesetActorController _actor;
        private IReplicatedActorView _replica;
        private TelegraphMarkerView _view;
        private static Transform _markerRoot;

        private readonly List<AttackFootprint.Shape> _footprint = new();
        private readonly List<AttackFootprint.Shape> _frozen = new();
        private readonly List<DangerShape> _scratch = new();

        private bool _wasTelegraphing;
        private float _telegraphElapsed;
        private float _flash;
        private float _flashSeconds;
        private long _strikeKey;
        private bool _strikeBaselined;
        private float _clock;
        private float _shotsLandBy = float.NegativeInfinity;
        private float _shotSpeed;

        private static float LongestLane(List<AttackFootprint.Shape> lanes)
        {
            var longest = 0f;
            foreach (var lane in lanes) longest = Mathf.Max(longest, Vector2.Distance(lane.Start, lane.End) + lane.Radius);
            return longest;
        }
        private bool _bombWasFlying;
        private int _resolvedCount;

        // Co-op client timeline (from the replicated strike moment).
        private float _sinceStrike = -1f;
        private int _clientWindowsFlashed;
        private Vector2 _clientLastAim;

        public bool IsShowing { get; private set; }

        /// <summary>The attack is resolving and still dangerous (later windows, a running dash, volleys left, a bomb in flight).</summary>
        public bool IsLive { get; private set; }

        /// <summary>How many danger lanes the current marker shows (a volley fan: one per projectile; otherwise 1).</summary>
        public int MarkerCount { get; private set; }

        public float Fill01 { get; private set; }

        /// <summary>World footprint of the primary shape (x along the attack direction, y across).</summary>
        public Vector2 MarkerScale { get; private set; }

        /// <summary>World centre and facing of the primary shape (diagnostics / co-op agreement).</summary>
        public Vector2 MarkerCentre { get; private set; }
        public float MarkerAngle { get; private set; }

        public Color MarkerColor { get; private set; }
        public int Shown { get; private set; }

        /// <summary>Impact flashes played (one per strike: each hit window, each shot, a landing bomb).</summary>
        public int Impacts { get; private set; }

        /// <summary>Seconds until the shown danger strikes (0 when it is striking or nothing is shown).</summary>
        public float SecondsToImpact { get; private set; }

        /// <summary>The final-warning blink is lit this frame.</summary>
        public bool IsWarning { get; private set; }

        public bool IsEliteOrBoss => _actor != null || (_replica != null && _replica.IsEliteOrBoss);

        /// <summary>The effect kind ('telegraph_*') naming the current marker's shape family (tests/diagnostics).</summary>
        public string MarkerKind { get; private set; } = string.Empty;

        /// <summary>The painted marker (tests/diagnostics).</summary>
        public TelegraphMarkerView View => _view;

        /// <summary>Seconds the impact flash lasts: short for normal enemies, a beat longer for Elites/Bosses.</summary>
        public const float FlashSeconds = 0.14f;
        public const float HeavyFlashSeconds = 0.22f;

        /// <summary>The final-warning window: the last this-many seconds (at most 35 % of the telegraph) blink.</summary>
        public const float WarningSeconds = 0.24f;

        public const string KindStationary = "telegraph_stationary";
        public const string KindDash = "telegraph_dash";
        public const string KindProjectile = "telegraph_projectile";
        public const string KindZone = "telegraph_zone";
        public const string KindSlam = "telegraph_slam";

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
            if (_enemy != null) _enemy.AttackResolved -= OnEnemyResolved;
            _enemy = enemy;
            _actor = actor;
            if (_enemy != null) _enemy.AttackResolved += OnEnemyResolved;
        }

        private void Awake()
        {
            if (_enemy == null && _actor == null)
            {
                _enemy = GetComponent<EnemyController>();
                _actor = GetComponent<MovesetActorController>();
                if (_enemy != null) _enemy.AttackResolved += OnEnemyResolved;
            }
        }

        private void OnEnemyResolved(EnemyController _) => _resolvedCount++;

        private void OnDestroy()
        {
            if (_enemy != null) _enemy.AttackResolved -= OnEnemyResolved;
            if (_view != null) Destroy(_view.gameObject);
        }

        private void OnDisable()
        {
            if (_view != null) _view.Hide();
            IsShowing = false;
        }

        private TelegraphMarkerView View_
        {
            get
            {
                if (_view != null) return _view;
                // Markers live in world space beside the effects, never under the actor: a dash lane must stay where it
                // was drawn while the body runs down it.
                if (_markerRoot == null)
                {
                    var root = new GameObject("TelegraphMarkers");
                    if (_pool != null) root.transform.SetParent(_pool.transform.parent, false);
                    _markerRoot = root.transform;
                }

                _view = TelegraphMarkerView.Create(_markerRoot, "Telegraph_" + name);
                return _view;
            }
        }

        private bool Telegraphing => _replica != null
            ? !_replica.IsDead && (_replica.IsMoveset ? _replica.MovesetState == MovesetActorState.Telegraph : _replica.EnemyState == EnemyState.Telegraph)
            : _actor != null ? _actor.State == MovesetActorState.Telegraph : _enemy != null && _enemy.State == EnemyState.Telegraph;

        private void Update() => Tick(Time.deltaTime);

        /// <summary>One presentation step (tests drive it directly).</summary>
        public void Tick(float deltaTime)
        {
            var telegraphing = Telegraphing;
            if (telegraphing && !_wasTelegraphing)
            {
                _telegraphElapsed = 0f;
                _frozen.Clear();
                Shown++;
                if (_replica != null) _clientShotsAtTelegraph = _replica.ShotsFired;
            }

            if (telegraphing) _telegraphElapsed += deltaTime;
            if (!telegraphing && _wasTelegraphing && _replica != null && !_replica.IsDead) BeginClientStrike();
            _wasTelegraphing = telegraphing;

            MarkerColor = _config == null ? Color.red : IsEliteOrBoss ? _config.EliteBossTelegraphColor : _config.EnemyTelegraphColor;
            _footprint.Clear();
            var danger = _replica != null ? EvaluateReplica(deltaTime) : _actor != null ? EvaluateActor() : EvaluateEnemy();

            if (danger.Showing && danger.Kind == KindProjectile && danger.ShotSpeed > 0f) _shotSpeed = danger.ShotSpeed;
            if (danger.StrikeKey != _strikeKey)
            {
                // The first evaluation only learns the actor's counters: nothing has struck yet.
                if (danger.Struck && _strikeBaselined)
                {
                    Strike(danger.Heavy);
                    // Shots just fired keep their lanes on the ground until they can no longer arrive (lane length at
                    // their speed): the red still stands under every shot that can hit.
                    if (MarkerKind == KindProjectile && _shotSpeed > 0f && _frozen.Count > 0) _shotsLandBy = _clock + LongestLane(_frozen) / _shotSpeed;
                }

                _strikeKey = danger.StrikeKey;
            }

            _clock += deltaTime;
            if (!danger.Showing && _clock < _shotsLandBy && _frozen.Count > 0 && MarkerKind == KindProjectile)
            {
                _footprint.Clear();
                _footprint.AddRange(_frozen);
                danger.Showing = true;
                danger.Kind = KindProjectile;
                danger.Progress = 1f;
                danger.ToImpact = 0f;
                danger.Pattern = _lastPattern;
                danger.Sweep = _lastSweep;
                danger.Source = _lastSource;
            }

            _strikeBaselined = true;

            if (_flash > 0f) _flash = Mathf.Max(0f, _flash - deltaTime / Mathf.Max(0.01f, _flashSeconds));

            IsShowing = danger.Showing && _footprint.Count > 0;
            IsLive = IsShowing && !telegraphing;
            Fill01 = IsShowing ? danger.Progress : 0f;
            SecondsToImpact = IsShowing ? danger.ToImpact : 0f;
            if (danger.Showing && _footprint.Count > 0)
            {
                MarkerKind = danger.Kind;
                MarkerScale = _footprint[0].Size;
                MarkerCentre = _footprint[0].Centre;
                MarkerAngle = _footprint[0].AngleDegrees;
                MarkerCount = _footprint.Count;
                _frozen.Clear();
                _frozen.AddRange(_footprint);
                _lastPattern = danger.Pattern;
                _lastSweep = danger.Sweep;
                _lastSource = danger.Source;
            }
            else
            {
                MarkerCount = 0;
            }

            var warnWindow = Mathf.Min(WarningSeconds, danger.Window * 0.35f);
            IsWarning = IsShowing && danger.ToImpact > 0f && danger.ToImpact <= warnWindow && ((int)((warnWindow - danger.ToImpact) / 0.06f) & 1) == 0;

            // Draw the danger, or the fading impact flash on the footprint that just struck.
            var view = IsShowing || _flash > 0f ? View_ : _view;
            if (view == null) return;
            if (!IsShowing && _flash <= 0f) { view.Hide(); return; }
            if (_frozen.Count == 0) { view.Hide(); return; }
            view.SetFootprint(_frozen, _lastSource, _lastPattern, _lastSweep, IsEliteOrBoss);
            view.Paint(new TelegraphLook
            {
                Colour = MarkerColor,
                Progress = IsShowing ? Fill01 : 1f,
                Warning = IsWarning,
                Flash = _flash,
                Heavy = IsEliteOrBoss
            });
        }

        private TelegraphMarkerView.Pattern _lastPattern;
        private TelegraphMarkerView.Sweep _lastSweep;
        private Vector2 _lastSource;

        private void Strike(bool heavy)
        {
            Impacts++;
            _flashSeconds = IsEliteOrBoss ? HeavyFlashSeconds : FlashSeconds;
            _flash = 1f;
            // A Boss's ground strike lands with weight: the configured boss-slam shake (accessibility-scaled, whole pixels).
            if (heavy && _actor is BossController && _pool != null) _pool.GetComponent<CombatFeedback>()?.Shake?.Request(ShakeKind.BossSlam);
        }

        private struct Danger
        {
            public bool Showing;
            public string Kind;
            public float Progress;
            public float ToImpact;
            public float Window;
            public Vector2 Source;
            public TelegraphMarkerView.Pattern Pattern;
            public TelegraphMarkerView.Sweep Sweep;
            public long StrikeKey;
            public bool Struck;
            public bool Heavy;
            /// <summary>Projectile attacks: how fast the shots fly (their lanes stay while shots can still arrive).</summary>
            public float ShotSpeed;
        }

        // ---- Authoritative sources ----

        private Danger EvaluateActor()
        {
            var d = new Danger();
            var position = (Vector2)_actor.transform.position;
            var resolver = _actor.Resolver;
            d.StrikeKey = StrikeKeyOf(resolver);
            d.Struck = resolver.WindowsFired > 0 || resolver.IsRunning;
            var attack = _actor.CurrentAttack;
            if (_actor.State == MovesetActorState.Telegraph && attack != null)
            {
                Describe(attack, position, _actor.LockedDirection, _actor.transform, ref d);
                var duration = Mathf.Max(0.0001f, _actor.TelegraphDuration);
                d.ToImpact = _actor.TelegraphRemaining;
                d.Window = duration;
                d.Progress = Mathf.Clamp01(1f - d.ToImpact / duration);
                d.Showing = true;
                d.Heavy = IsHeavy(attack);
                // The strike is the commit: the first window of the attack lands as the telegraph ends.
                d.Struck = false;
                return d;
            }

            if (resolver.IsRunning && resolver.Current != null) DescribeLive(resolver, position, _actor.transform, ref d);
            else d.Heavy = resolver.Current != null && IsHeavy(resolver.Current);
            if (!d.Showing && attack != null) d.Heavy = IsHeavy(attack);
            return d;
        }

        private Danger EvaluateEnemy()
        {
            var d = new Danger();
            if (_enemy == null || _enemy.Definition == null) return d;
            var position = (Vector2)_enemy.transform.position;
            var definition = _enemy.Definition;
            if (_enemy.State == EnemyState.Telegraph)
            {
                DescribeEnemyTelegraph(position, ref d);
                var duration = Mathf.Max(0.0001f, _enemy.TelegraphDuration);
                d.ToImpact = _enemy.TelegraphRemaining;
                d.Window = duration;
                d.Progress = Mathf.Clamp01(1f - d.ToImpact / duration);
                d.Showing = true;
                d.StrikeKey = StrikeKeyOf();
                d.Struck = false;
                return d;
            }

            d.StrikeKey = StrikeKeyOf();
            d.Struck = true;
            switch (_enemy.Attack)
            {
                case EnemyChargeAttack charge when charge.IsResolving:
                    DescribeLive(charge.Resolver, position, _enemy.transform, ref d);
                    break;
                case EnemyMovesetAttack moveset when moveset.IsResolving:
                    DescribeLive(moveset.Resolver, position, _enemy.transform, ref d);
                    break;
                case EnemyProjectileAttack shot when shot.IsBursting:
                    _footprint.Add(AttackFootprint.ProjectileLane(position, shot.LockedDirection, 0f, definition.ProjectileRange, true, _enemy.transform));
                    SetLane(position, ref d);
                    d.Showing = true;
                    d.Progress = 1f;
                    d.Window = Mathf.Max(0.0001f, definition.BurstIntervalSeconds);
                    break;
                case EnemyLobAttack lob when lob.IsBombInFlight:
                {
                    var bomb = lob.LastGrenade;
                    _footprint.Add(AttackFootprint.Shape.Circle(bomb.LandingPoint, BombRadius(definition), Vector2.right));
                    d.Kind = KindSlam;
                    d.Pattern = TelegraphMarkerView.Pattern.Hatch;
                    d.Sweep = TelegraphMarkerView.Sweep.Radial;
                    d.Source = bomb.LandingPoint;
                    d.Showing = true;
                    d.Progress = bomb.Progress01;
                    d.ToImpact = bomb.SecondsToLanding;
                    d.Window = Mathf.Max(0.0001f, Vector2.Distance(bomb.Origin, bomb.LandingPoint) / Mathf.Max(0.01f, definition.ProjectileSpeed));
                    break;
                }
            }

            return d;
        }

        private long StrikeKeyOf()
        {
            switch (_enemy.Attack)
            {
                case EnemyChargeAttack charge when charge.Resolver != null:
                    return StrikeKeyOf(charge.Resolver);
                case EnemyMovesetAttack moveset when moveset.Resolver != null:
                    return StrikeKeyOf(moveset.Resolver);
                case EnemyProjectileAttack shot:
                    return shot.ShotsFired;
                case EnemyLobAttack lob:
                {
                    // The bomb's blast is the strike, not the throw.
                    var flying = lob.IsBombInFlight;
                    if (_bombWasFlying && !flying) _bombLandings++;
                    _bombWasFlying = flying;
                    return _bombLandings;
                }
                default:
                    return _resolvedCount;
            }
        }

        private int _bombLandings;

        /// <summary>The commit and its first window are one strike; each later window is another.</summary>
        private static long StrikeKeyOf(AttackResolver resolver) => (long)resolver.Begun * 64 + Mathf.Max(1, resolver.WindowsFired);

        private void DescribeEnemyTelegraph(Vector2 position, ref Danger d)
        {
            var definition = _enemy.Definition;
            var toTarget = _enemy.Target != null ? (Vector2)_enemy.Target.position - position : Vector2.right;
            var facing = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector2.right;
            switch (_enemy.Attack)
            {
                case EnemyProjectileAttack shot:
                {
                    var aim = shot.AimIfFiredNow(_enemy.Target);
                    if (aim.sqrMagnitude < 0.0001f) aim = facing;
                    _footprint.Add(AttackFootprint.ProjectileLane(position, aim, 0f, definition.ProjectileRange, true, _enemy.transform));
                    SetLane(position, ref d);
                    return;
                }
                case EnemyLobAttack lob:
                {
                    var landing = lob.LandingPoint.sqrMagnitude > 0.0001f ? lob.PlannedLanding : position + facing * Mathf.Min(definition.AttackRange, toTarget.magnitude);
                    _footprint.Add(AttackFootprint.Shape.Circle(landing, BombRadius(definition), Vector2.right));
                    d.Kind = KindSlam;
                    d.Pattern = TelegraphMarkerView.Pattern.Hatch;
                    d.Sweep = TelegraphMarkerView.Sweep.Radial;
                    d.Source = landing;
                    return;
                }
                case EnemyChargeAttack charge when charge.Charge != null:
                    Describe(charge.Charge, position, charge.LockedDirection.sqrMagnitude > 0.0001f ? charge.LockedDirection : facing, _enemy.transform, ref d);
                    return;
                case EnemyMovesetAttack moveset:
                {
                    var attack = moveset.Resolver != null && moveset.Resolver.Current != null ? moveset.Resolver.Current : moveset.PendingAttack;
                    if (attack != null)
                    {
                        Describe(attack, position, moveset.LockedDirection.sqrMagnitude > 0.0001f ? moveset.LockedDirection : facing, _enemy.transform, ref d);
                        return;
                    }

                    break;
                }
            }

            // Contact: the target is struck when its centre is within the reach of the body.
            _footprint.Add(AttackFootprint.Shape.Circle(position, Mathf.Max(0.5f, definition.AttackRange), facing));
            d.Kind = KindStationary;
            d.Pattern = TelegraphMarkerView.Pattern.Hatch;
            d.Sweep = TelegraphMarkerView.Sweep.Radial;
            d.Source = position;
        }

        private static float BombRadius(EnemyDefinition definition) => Mathf.Max(0.5f, definition.BombRadiusTiles);

        // ---- Co-op client ----

        private void BeginClientStrike()
        {
            _sinceStrike = 0f;
            _clientWindowsFlashed = 0;
        }

        // Shots the host had fired when this telegraph began (normal shooters flash per real shot).
        private int _clientShotsAtTelegraph;

        private Danger EvaluateReplica(float deltaTime)
        {
            var d = new Danger();
            if (_replica.IsDead) { _sinceStrike = -1f; return d; }
            var position = (Vector2)transform.position;
            var facing = _replica.Facing.sqrMagnitude > 0.0001f ? _replica.Facing.normalized : Vector2.right;
            var attack = _replica.CurrentAttack;
            var definition = _replica.EnemyDefinition;
            if (Telegraphing)
            {
                _sinceStrike = -1f;
                DescribeReplica(position, facing, attack, definition, ref d);
                var duration = _replica.TelegraphSeconds > 0f ? _replica.TelegraphSeconds
                    : attack != null ? attack.TelegraphSeconds : definition != null ? definition.AttackTelegraphSeconds : 0.5f;
                duration = Mathf.Max(0.0001f, duration);
                d.ToImpact = Mathf.Max(0f, duration - _telegraphElapsed);
                d.Window = duration;
                d.Progress = Mathf.Clamp01(_telegraphElapsed / duration);
                d.Showing = true;
                d.StrikeKey = Shown * 64L;
                d.Heavy = attack != null && IsHeavy(attack);
                return d;
            }

            if (_sinceStrike < 0f) { d.StrikeKey = Shown * 64L; return d; }
            _sinceStrike += deltaTime;

            // The strike moment and the windows that follow it, timed from the authored data the host also runs.
            var windows = 1;
            var interval = 0f;
            var live = 0f;
            if (attack != null)
            {
                d.Heavy = IsHeavy(attack);
                if (attack.Motion == AttackMotion.Dash) live = attack.DashSpeed > 0f ? attack.DashDistance / attack.DashSpeed : 0f;
                else { windows = attack.HitCount; interval = attack.HitIntervalSeconds; live = (windows - 1) * interval; }
                if (_replica.IsMoveset && _replica.MovesetState != MovesetActorState.Attacking) live = Mathf.Min(live, _sinceStrike);
                // A normal moveset enemy (Brute): its move ends when the host's does (a dash stopped at a wall).
                if (!_replica.IsMoveset && !_replica.IsResolving && _sinceStrike > 0.1f) live = Mathf.Min(live, _sinceStrike);
            }
            else if (definition != null)
            {
                switch (definition.AttackKind)
                {
                    case EnemyAttackKind.Projectile:
                        windows = definition.BurstCount;
                        interval = definition.BurstIntervalSeconds;
                        live = (windows - 1) * interval;
                        break;
                    case EnemyAttackKind.Charge when definition.ChargeAttack != null:
                        live = definition.ChargeAttack.DashSpeed > 0f ? definition.ChargeAttack.DashDistance / definition.ChargeAttack.DashSpeed : 0f;
                        // A charge stopped early by a wall ends when the host's ends: no lane left standing.
                        if (!_replica.IsResolving && _sinceStrike > 0.1f) live = Mathf.Min(live, _sinceStrike);
                        break;
                    case EnemyAttackKind.Lob:
                        live = _replica.HasAimPoint ? float.MaxValue : 0f;
                        if (_replica.HasAimPoint) _clientLastAim = _replica.AimPoint;
                        break;
                }
            }

            var fired = interval > 0f ? Mathf.Min(windows, 1 + Mathf.FloorToInt(_sinceStrike / interval)) : 1;
            // A plain shooter may not fire at all (its target left range): its impacts are the shots the host really fired.
            if (attack == null && definition != null && definition.AttackKind == EnemyAttackKind.Projectile)
                fired = Mathf.Clamp(_replica.ShotsFired - _clientShotsAtTelegraph, 0, windows);
            var isLob = attack == null && definition != null && definition.AttackKind == EnemyAttackKind.Lob;
            if (isLob)
            {
                // The bomb's landing is the strike: flash when the host stops reporting it in the air.
                fired = _replica.HasAimPoint ? 0 : 1;
            }

            if (fired > _clientWindowsFlashed)
            {
                _clientWindowsFlashed = fired;
                d.StrikeKey = Shown * 64L + fired;
                d.Struck = true;
            }
            else d.StrikeKey = _strikeKey;

            if (_sinceStrike < live)
            {
                DescribeReplica(position, facing, attack, definition, ref d);
                if (attack != null && attack.Motion == AttackMotion.Dash) { _footprint.Clear(); _footprint.AddRange(_frozen); }
                d.Showing = _footprint.Count > 0;
                if (isLob)
                {
                    var landing = _replica.AimPoint;
                    var flight = definition.ProjectileSpeed > 0f ? Vector2.Distance(position, landing) / definition.ProjectileSpeed : 0f;
                    d.Window = Mathf.Max(0.0001f, flight);
                    d.ToImpact = Mathf.Max(0f, flight - _sinceStrike);
                    d.Progress = Mathf.Clamp01(_sinceStrike / d.Window);
                }
                else if (interval > 0f && fired < windows)
                {
                    d.ToImpact = Mathf.Max(0f, fired * interval - _sinceStrike);
                    d.Window = interval;
                    d.Progress = Mathf.Clamp01(1f - d.ToImpact / interval);
                }
                else d.Progress = 1f;
            }
            else if (isLob && _clientLastAim != Vector2.zero && d.Struck)
            {
                // Keep the landing ring for the impact flash.
                _footprint.Add(AttackFootprint.Shape.Circle(_clientLastAim, BombRadius(definition), Vector2.right));
                _frozen.Clear();
                _frozen.AddRange(_footprint);
                _footprint.Clear();
                _clientLastAim = Vector2.zero;
            }

            return d;
        }

        private void DescribeReplica(Vector2 position, Vector2 facing, EnemyAttackDefinition attack, EnemyDefinition definition, ref Danger d)
        {
            if (attack != null) { Describe(attack, position, facing, transform, ref d); return; }
            if (definition == null) return;
            switch (definition.AttackKind)
            {
                case EnemyAttackKind.Projectile:
                    _footprint.Add(AttackFootprint.ProjectileLane(position, facing, 0f, definition.ProjectileRange, true, transform));
                    SetLane(position, ref d);
                    return;
                case EnemyAttackKind.Lob:
                {
                    var landing = _replica.HasAimPoint ? _replica.AimPoint : position + facing * definition.AttackRange;
                    _footprint.Add(AttackFootprint.Shape.Circle(landing, BombRadius(definition), Vector2.right));
                    d.Kind = KindSlam;
                    d.Pattern = TelegraphMarkerView.Pattern.Hatch;
                    d.Sweep = TelegraphMarkerView.Sweep.Radial;
                    d.Source = landing;
                    return;
                }
                case EnemyAttackKind.Charge when definition.ChargeAttack != null:
                    Describe(definition.ChargeAttack, position, facing, transform, ref d);
                    return;
                default:
                    _footprint.Add(AttackFootprint.Shape.Circle(position, Mathf.Max(0.5f, definition.AttackRange), facing));
                    d.Kind = KindStationary;
                    d.Pattern = TelegraphMarkerView.Pattern.Hatch;
                    d.Sweep = TelegraphMarkerView.Sweep.Radial;
                    d.Source = position;
                    return;
            }
        }

        // ---- Shared description of an authored attack ----

        /// <summary>The footprint and drawing style of one authored attack from (position, locked direction).</summary>
        private void Describe(EnemyAttackDefinition attack, Vector2 position, Vector2 direction, Transform self, ref Danger d)
        {
            d.Kind = KindOf(attack.Motion);
            d.Source = position;
            if (attack.Motion == AttackMotion.Projectile) d.ShotSpeed = attack.ProjectileSpeed;
            FootprintOf(attack, position, direction, self, _footprint, out d.Pattern, out d.Sweep);
        }

        /// <summary>
        /// Exactly what the marker draws for an authored attack from (position, locked direction): the resolver's own
        /// strike shape, the dash lane up to the first wall, or every volley lane up to its first wall.
        /// </summary>
        public static void FootprintOf(EnemyAttackDefinition attack, Vector2 position, Vector2 direction, Transform self, List<AttackFootprint.Shape> into,
            out TelegraphMarkerView.Pattern pattern, out TelegraphMarkerView.Sweep sweep)
        {
            switch (attack.Motion)
            {
                case AttackMotion.Dash:
                    into.Add(AttackFootprint.Dash(attack, position, direction, true, null, self));
                    pattern = TelegraphMarkerView.Pattern.Chevrons;
                    sweep = TelegraphMarkerView.Sweep.Along;
                    break;
                case AttackMotion.Projectile:
                    AttackFootprint.Lanes(attack, position, direction, into, true, self);
                    pattern = TelegraphMarkerView.Pattern.CentreLine;
                    sweep = TelegraphMarkerView.Sweep.Radial;
                    break;
                case AttackMotion.Zone:
                    into.Add(AttackFootprint.Strike(attack, position, direction));
                    pattern = TelegraphMarkerView.Pattern.Hatch;
                    sweep = TelegraphMarkerView.Sweep.Along;
                    break;
                default:
                    into.Add(AttackFootprint.Strike(attack, position, direction));
                    pattern = TelegraphMarkerView.Pattern.Hatch;
                    sweep = TelegraphMarkerView.Sweep.Radial;
                    break;
            }
        }

        /// <summary>A running attack: a dash keeps the lane it was warned with; windows still to strike stay drawn and refill.</summary>
        private void DescribeLive(AttackResolver resolver, Vector2 position, Transform self, ref Danger d)
        {
            var attack = resolver.Current;
            d.Heavy = IsHeavy(attack);
            if (attack.Motion == AttackMotion.Dash)
            {
                if (_frozen.Count == 0) Describe(attack, position, resolver.Direction, self, ref d);
                else { _footprint.AddRange(_frozen); d.Kind = KindDash; d.Pattern = TelegraphMarkerView.Pattern.Chevrons; d.Sweep = TelegraphMarkerView.Sweep.Along; d.Source = _lastSource; }
                d.Showing = true;
                d.Progress = 1f;
                return;
            }

            // The first window strikes as the telegraph ends; only windows still to come are live danger.
            if (resolver.WindowsFired < 1 || resolver.WindowsRemaining <= 0) return;
            Describe(attack, position, resolver.Direction, self, ref d);
            var interval = Mathf.Max(0.0001f, attack.HitIntervalSeconds);
            d.ToImpact = resolver.SecondsToNextWindow;
            d.Window = interval;
            d.Progress = Mathf.Clamp01(1f - d.ToImpact / interval);
            d.Showing = true;
        }

        private void SetLane(Vector2 position, ref Danger d)
        {
            var definition = _enemy != null ? _enemy.Definition : _replica?.EnemyDefinition;
            if (definition != null) d.ShotSpeed = definition.ProjectileSpeed;
            d.Kind = KindProjectile;
            d.Pattern = TelegraphMarkerView.Pattern.CentreLine;
            d.Sweep = TelegraphMarkerView.Sweep.Radial;
            d.Source = position;
        }

        /// <summary>Ground strikes of Elites/Bosses that land with weight (slams, cleaves, zones): the shake goes to Bosses only.</summary>
        private static bool IsHeavy(EnemyAttackDefinition attack) =>
            attack != null && (attack.Motion == AttackMotion.Slam || attack.Motion == AttackMotion.Zone || attack.Motion == AttackMotion.Stationary);

        // ---- Static shape API (tests, diagnostics, co-op parity) ----

        /// <summary>One danger shape: which family, its world footprint (x along the facing, y across), centre and facing.</summary>
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

        private static DangerShape From(string kind, AttackFootprint.Shape s) => new(kind, s.Size, s.Centre, s.AngleDegrees);

        /// <summary>World-space size (x along the attack direction, y across) and centre offset of an attack's footprint from an attacker at the origin (no walls).</summary>
        public static (Vector2 size, Vector2 offset) ShapeFor(EnemyAttackDefinition attack, Vector2 direction)
        {
            if (attack == null) return (Vector2.one, Vector2.zero);
            var s = PrimaryShape(attack, Vector2.zero, direction, false, null);
            return (s.Size, s.Centre);
        }

        private static AttackFootprint.Shape PrimaryShape(EnemyAttackDefinition attack, Vector2 origin, Vector2 direction, bool clip, Transform self)
        {
            switch (attack.Motion)
            {
                case AttackMotion.Dash:
                    return AttackFootprint.Dash(attack, origin, direction, clip, null, self);
                case AttackMotion.Projectile:
                    return AttackFootprint.ProjectileLane(origin, AttackFootprint.LaneDirection(attack, direction, 0), AttackFootprint.VolleyMuzzleOffset, attack.ProjectileRange, clip, self);
                default:
                    return AttackFootprint.Strike(attack, origin, direction);
            }
        }

        /// <summary>
        /// The lanes of a projectile fan or ring, exactly as the resolver fires them (even fan over SpreadDegrees around the
        /// locked direction, each ended at its first wall). Fills nothing for a single shot or any other attack.
        /// </summary>
        public static void LanesFor(EnemyAttackDefinition attack, Vector2 origin, Vector2 direction, List<DangerShape> lanes)
        {
            if (attack == null || attack.Motion != AttackMotion.Projectile || attack.ProjectileCount <= 1 || attack.SpreadDegrees <= 0f) return;
            var shapes = new List<AttackFootprint.Shape>();
            AttackFootprint.Lanes(attack, origin, direction, shapes, true);
            foreach (var s in shapes) lanes.Add(From(KindProjectile, s));
        }

        /// <summary>Elite/Boss moveset attacks: the authored motion decides the family, the shared footprint the shape.</summary>
        public static DangerShape ShapeOf(MovesetActorController actor)
        {
            var attack = actor.CurrentAttack;
            if (attack == null) return new DangerShape(KindStationary, Vector2.one, actor.transform.position, 0f);
            return From(KindOf(attack.Motion), PrimaryShape(attack, actor.transform.position, actor.LockedDirection, true, actor.transform));
        }

        /// <summary>A replicated actor's danger shape from what the host sent.</summary>
        public static DangerShape ShapeOf(IReplicatedActorView replica, Vector2 position)
        {
            var direction = replica.Facing.sqrMagnitude > 0.0001f ? replica.Facing.normalized : Vector2.right;
            var attack = replica.CurrentAttack;
            if (attack != null) return From(KindOf(attack.Motion), PrimaryShape(attack, position, direction, true, null));
            var definition = replica.EnemyDefinition;
            if (definition == null) return new DangerShape(KindStationary, Vector2.one, position, 0f);
            switch (definition.AttackKind)
            {
                case EnemyAttackKind.Projectile:
                    return From(KindProjectile, AttackFootprint.ProjectileLane(position, direction, 0f, definition.ProjectileRange, true));
                case EnemyAttackKind.Lob:
                    return From(KindSlam, AttackFootprint.Shape.Circle(replica.HasAimPoint ? replica.AimPoint : position + direction * definition.AttackRange, BombRadius(definition), Vector2.right));
                case EnemyAttackKind.Charge when definition.ChargeAttack != null:
                    return From(KindDash, AttackFootprint.Dash(definition.ChargeAttack, position, direction, true));
                default:
                    return From(KindStationary, AttackFootprint.Shape.Circle(position, Mathf.Max(0.5f, definition.AttackRange), Vector2.right));
            }
        }

        /// <summary>Normal enemies: the shape of what the attack actually does (contact ring, shot lane, blast ring at the real landing, charge lane, the move's footprint).</summary>
        public static DangerShape ShapeOf(EnemyController enemy, Vector2 position)
        {
            var definition = enemy != null ? enemy.Definition : null;
            if (definition == null) return new DangerShape(KindStationary, Vector2.one, position, 0f);
            var toTarget = enemy.Target != null ? (Vector2)enemy.Target.position - position : Vector2.right;
            var facing = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector2.right;
            switch (enemy.Attack)
            {
                case EnemyProjectileAttack shot:
                {
                    var aim = shot.AimIfFiredNow(enemy.Target);
                    return From(KindProjectile, AttackFootprint.ProjectileLane(position, aim.sqrMagnitude > 0.0001f ? aim : facing, 0f, definition.ProjectileRange, true, enemy.transform));
                }
                case EnemyLobAttack lob:
                {
                    var landing = lob.LandingPoint.sqrMagnitude > 0.0001f ? lob.PlannedLanding : position + facing * Mathf.Min(definition.AttackRange, toTarget.magnitude);
                    return From(KindSlam, AttackFootprint.Shape.Circle(landing, BombRadius(definition), Vector2.right));
                }
                case EnemyChargeAttack charge when charge.Charge != null:
                    return From(KindDash, AttackFootprint.Dash(charge.Charge, position, charge.LockedDirection.sqrMagnitude > 0.0001f ? charge.LockedDirection : facing, true, null, enemy.transform));
                case EnemyMovesetAttack moveset:
                {
                    var attack = moveset.Resolver != null && moveset.Resolver.Current != null ? moveset.Resolver.Current : moveset.PendingAttack;
                    if (attack == null) break;
                    return From(KindOf(attack.Motion), PrimaryShape(attack, position, moveset.LockedDirection.sqrMagnitude > 0.0001f ? moveset.LockedDirection : facing, true, enemy.transform));
                }
            }

            return From(KindStationary, AttackFootprint.Shape.Circle(position, Mathf.Max(0.5f, definition.AttackRange), Vector2.right));
        }

        public static string KindOf(AttackMotion motion) => motion switch
        {
            AttackMotion.Dash => KindDash,
            AttackMotion.Projectile => KindProjectile,
            AttackMotion.Zone => KindZone,
            AttackMotion.Slam => KindSlam,
            _ => KindStationary
        };
    }
}

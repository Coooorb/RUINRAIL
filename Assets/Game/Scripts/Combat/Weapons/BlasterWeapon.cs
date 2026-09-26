using System.Collections.Generic;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Stats;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Weapons
{
    /// <summary>
    /// Heat-based projectile weapon. Shares aim, damage rolling and pooled projectile emission with RangedWeapon
    /// through ProjectileEmitter; its resource is BlasterHeatState instead of magazine + ammo reserve.
    /// Consumes no ammo type and has no reload action.
    /// </summary>
    public sealed class BlasterWeapon : MonoBehaviour, IEquippableWeapon
    {
        [SerializeField] private BlasterWeaponDefinition _definition;

        public BlasterWeaponDefinition Definition => _definition;
        [SerializeField] private ProjectilePool _projectilePool;
        [SerializeField] private PlayerAiming _aiming;
        [SerializeField] private Transform _muzzle;

        private IPlayerInputReader _inputReader;
        private readonly ActionGateLookup _actionGate = new();
        private IDamageRoller _damageRoller;
        private IPlayerStatsProvider _stats;
        private IImpactAttackerFeedback _impactFeedback;
        private RuinRail.Gameplay.Stats.PlayerCombatEvents _combatEvents;
        private bool _firing;
        private float _peakHeat;
        private AimAssistConfig _aimAssist;

        /// <summary>The soft aim-assist tuning; null = raw aim only.</summary>
        public void SetAimAssist(AimAssistConfig config) => _aimAssist = config;
        public ShotSolution LastShot { get; private set; }

        /// <summary>Spawn point and direction of the next shot: pivot-anchored aim, close-range pull-back, soft assist.</summary>
        public ShotSolution ResolveShot(float range)
        {
            var rawDirection = _aiming != null ? _aiming.AimDirection : Vector2.right;
            var origin = _aiming != null ? _aiming.AimOrigin : (Vector2)transform.position;
            var muzzle = _muzzle != null ? (Vector2)_muzzle.position : origin;
            var weaponClass = _definition != null ? _definition.WeaponClass : WeaponClass.Blaster;
            return ShotSolver.Solve(origin, muzzle, rawDirection, range, weaponClass, gameObject, DamageTeam.Player, _aimAssist, _aiming);
        }
        private BlasterHeatState _heat;
        private readonly ProjectileEmitter _emitter = new();
        private readonly List<Projectile> _lastSpawnedProjectiles = new();

        private float _fireCooldownRemaining;

        public BlasterHeatState Heat => _heat ??= BuildHeatState();

        /// <summary>Shots per second after the capped Fire Rate bonus.</summary>
        public float CurrentFireRate => _definition == null ? 0f : WeaponStatMath.FireRate(_definition.FireRate, _stats);

        /// <summary>Seconds between shots.</summary>
        public float CurrentFireInterval => _definition == null ? 0f : WeaponStatMath.FireInterval(_definition.FireRate, _stats);

        /// <summary>Heat one shot adds after the capped Blaster Heat per Shot reduction.</summary>
        public float CurrentHeatPerShot => _definition == null ? 0f : WeaponStatMath.BlasterHeatPerShot(_definition.HeatPerShot, _stats);

        /// <summary>Heat shed per second after the capped Blaster Cooling Rate bonus.</summary>
        public float CurrentCoolingRatePerSecond => _definition == null ? 0f : WeaponStatMath.BlasterCoolingRate(_definition.CoolingRatePerSecond, _stats);

        /// <summary>Projectile speed after the capped Projectile Speed bonus.</summary>
        public float CurrentProjectileSpeed => _definition == null ? 0f : WeaponStatMath.ProjectileSpeed(_definition.ProjectileSpeed, _stats);

        /// <summary>Real reach after the capped Projectile Range bonus.</summary>
        public float CurrentRange => _definition == null ? 0f : WeaponStatMath.ProjectileRange(_definition.Range, _stats);
        public Projectile LastSpawnedProjectile { get; private set; }
        public IReadOnlyList<Projectile> LastSpawnedProjectiles => _lastSpawnedProjectiles;
        public bool IsEquipped { get; private set; } = true;

        public void OnEquipped()
        {
            IsEquipped = true;
        }

        /// <summary>Accepted shots since this component was created.</summary>
        public int ShotsFired { get; private set; }

        public void OnUnequipped()
        {
            // Heat is deliberately untouched: a holstered blaster keeps its heat and keeps cooling.
            IsEquipped = false;
            SetFiring(false);
        }

        /// <summary>Continuous-fire state for Stabilizer / Lock In: the trigger held on the equipped blaster.</summary>
        private void SetFiring(bool firing)
        {
            if (firing == _firing) return;
            _firing = firing;
            _combatEvents?.RaiseFiringStateChanged(firing);
        }

        public void SetDefinition(BlasterWeaponDefinition definition)
        {
            _definition = definition;
            _heat = null;
        }

        /// <summary>Player stat pipeline for Knockback / Stagger Power multipliers; null = authored values.</summary>
        public void SetStats(IPlayerStatsProvider stats)
        {
            _stats = stats;
        }

        /// <summary>The wearer's passive event hub (items/34 hooks); null = no passive hooks (enemies, tests).</summary>
        public void SetCombatEvents(RuinRail.Gameplay.Stats.PlayerCombatEvents events)
        {
            _combatEvents = events;
        }

        /// <summary>Attacker-side impact hooks carried by every projectile this weapon fires; null = none.</summary>
        public void SetImpactFeedback(IImpactAttackerFeedback feedback)
        {
            _impactFeedback = feedback;
        }

        public void SetProjectilePool(ProjectilePool projectilePool)
        {
            _projectilePool = projectilePool;
        }

        public void SetAiming(PlayerAiming aiming)
        {
            _aiming = aiming;
        }

        public void SetMuzzle(Transform muzzle)
        {
            _muzzle = muzzle;
        }

        public void SetDamageRoller(IDamageRoller damageRoller)
        {
            _damageRoller = damageRoller;
        }

        public void SetInputReader(IPlayerInputReader inputReader)
        {
            _inputReader = inputReader;
        }

        private void Awake()
        {
            _damageRoller ??= new UnityRandomDamageRoller();

            if (_aiming == null)
            {
                _aiming = GetComponent<PlayerAiming>();
            }

            _inputReader ??= GetComponent<PlayerInput>()?.Reader;
        }

        private void Update()
        {
            var deltaTime = Time.deltaTime;
            TickFireCooldown(deltaTime);
            // The cooling bonus is pushed into the heat state each frame so a source equipped or removed mid-run takes
            // effect immediately without rebuilding (and losing) the accumulated heat.
            Heat.CoolingRateMultiplier = _stats?.GetMultiplier(StatId.BlasterCoolingRate) ?? 1f;
            Heat.Tick(deltaTime);
            // Cooling Module: a blaster that cooled all the way to 0 reports the peak heat it cooled down from.
            if (_peakHeat > 0f && Heat.Heat <= 0f)
            {
                var peak = _peakHeat;
                _peakHeat = 0f;
                _combatEvents?.RaiseBlasterCooledToZero(peak);
            }

            if (IsEquipped && _inputReader != null && _inputReader.FireHeld)
            {
                TryFire();
            }

            SetFiring(IsEquipped && _inputReader != null && _inputReader.FireHeld);
        }

        private void TickFireCooldown(float deltaTime)
        {
            if (_fireCooldownRemaining > 0f)
            {
                _fireCooldownRemaining = Mathf.Max(0f, _fireCooldownRemaining - deltaTime);
            }
        }

        public bool TryFire()
        {
            if (!IsEquipped || _definition == null || _projectilePool == null || !_actionGate.CanAct(this))
            {
                return false;
            }

            if (_fireCooldownRemaining > 0f || !Heat.CanFire)
            {
                return false;
            }

            _fireCooldownRemaining = CurrentFireInterval;
            ShotsFired++;
            // Heat and damage hooks of the wearer (Cooling Module, Steady Aim / Fresh Mag); the overheat is announced once.
            var shotHeat = _combatEvents != null ? _combatEvents.RaiseBlasterShotHeatRolling(CurrentHeatPerShot).FinalHeat : CurrentHeatPerShot;
            var wasOverheated = Heat.IsOverheated;
            Heat.AddShotHeat(shotHeat);
            _peakHeat = Mathf.Max(_peakHeat, Heat.Heat);
            if (Heat.IsOverheated && !wasOverheated) _combatEvents?.RaiseBlasterOverheated();
            var damageMultiplier = (_stats?.GetMultiplier(StatId.WeaponDamage) ?? 1f) * (_combatEvents == null ? 1f : (100 + _combatEvents.RaiseAttackDamageRolling(_definition.DamageMax, true).BonusPercent) / 100f);

            var solved = ResolveShot(CurrentRange);
            LastShot = solved;

            _emitter.Emit(
                _projectilePool, SingleProjectilePattern.Instance, _damageRoller, solved.Direction, solved.SpawnPosition,
                _definition.DamageMin, _definition.DamageMax, CurrentProjectileSpeed, CurrentRange,
                gameObject, _lastSpawnedProjectiles, damageMultiplier,
                WeaponStatMath.Knockback(_definition.Knockback, _stats),
                WeaponStatMath.StaggerPower(_definition.StaggerPower, _stats),
                _impactFeedback,
                0f,
                DamageTeam.Player,
                ProjectileVisualCatalog.ResolveWeaponVisualId(_definition));
            LastSpawnedProjectile = _lastSpawnedProjectiles[_lastSpawnedProjectiles.Count - 1];

            return true;
        }

        private BlasterHeatState BuildHeatState()
        {
            return _definition == null
                ? new BlasterHeatState(100f, 0f, 0f, 0f)
                : new BlasterHeatState(_definition.MaxHeat, _definition.CoolingRatePerSecond, _definition.CoolingDelaySeconds, _definition.OverheatLockoutSeconds);
        }
    }
}

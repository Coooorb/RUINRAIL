using System;
using System.Collections.Generic;
using RuinRail.Core.Input;
using RuinRail.Core.Rng;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Stats;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Weapons
{
    public sealed class RangedWeapon : MonoBehaviour, IEquippableWeapon
    {
        [SerializeField] private RangedWeaponDefinition _definition;
        [SerializeField] private ProjectilePool _projectilePool;
        [SerializeField] private PlayerAiming _aiming;
        [SerializeField] private Transform _muzzle;

        private IPlayerInputReader _inputReader;
        private readonly ActionGateLookup _actionGate = new();
        private IAmmoReserve _ammoReserve;
        private IDamageRoller _damageRoller;
        private IFiringPattern _firingPattern;
        private IRandomSource _spreadRandom;
        private IPlayerStatsProvider _stats;
        private Impact.IImpactAttackerFeedback _impactFeedback;
        private AimAssistConfig _aimAssist;
        private readonly ProjectileEmitter _emitter = new();
        private readonly List<Projectile> _lastSpawnedProjectiles = new();

        private float _fireCooldownRemaining;
        private float _reloadTimeRemaining;

        public int MagazineAmmo { get; private set; }
        public RangedWeaponDefinition Definition => _definition;
        public bool IsReloading { get; private set; }
        public Projectile LastSpawnedProjectile { get; private set; }
        public IReadOnlyList<Projectile> LastSpawnedProjectiles => _lastSpawnedProjectiles;
        public IFiringPattern FiringPattern => _firingPattern ??= BuildPatternFromDefinition();
        public bool IsEquipped { get; private set; } = true;

        public void OnEquipped()
        {
            IsEquipped = true;
        }

        public void OnUnequipped()
        {
            IsEquipped = false;
            CancelReload();
        }

        public void SetDefinition(RangedWeaponDefinition definition)
        {
            _definition = definition;
            MagazineAmmo = _definition != null ? _definition.MagazineSize : 0;
            _firingPattern = null;
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

        public void SetAmmoReserve(IAmmoReserve ammoReserve)
        {
            _ammoReserve = ammoReserve;
        }

        public void SetDamageRoller(IDamageRoller damageRoller)
        {
            _damageRoller = damageRoller;
        }

        /// <summary>Player stat pipeline (weapon damage %, reload speed %); null = authored values.</summary>
        public void SetStats(IPlayerStatsProvider stats)
        {
            _stats = stats;
        }

        /// <summary>Attacker-side impact hooks carried by every projectile this weapon fires (wearer passives); null = none.</summary>
        public void SetImpactFeedback(Impact.IImpactAttackerFeedback feedback)
        {
            _impactFeedback = feedback;
        }

        /// <summary>Authored reload time Ã· (1 + capped Reload Speed bonus).</summary>
        public float CurrentReloadTime => _definition == null ? 0f : _definition.ReloadTime / (_stats?.GetMultiplier(StatId.ReloadSpeed) ?? 1f);

        /// <summary>Overrides the pattern derived from the definition (tests / composed weapons).</summary>
        public void SetFiringPattern(IFiringPattern firingPattern)
        {
            _firingPattern = firingPattern;
        }

        /// <summary>Seeded source used for pellet spread; inject for deterministic shots.</summary>
        public void SetSpreadRandom(IRandomSource random)
        {
            _spreadRandom = random;
            _firingPattern = null;
        }

        public void SetInputReader(IPlayerInputReader inputReader)
        {
            AttachInputReader(inputReader);
        }

        private void Awake()
        {
            if (_definition != null)
            {
                MagazineAmmo = _definition.MagazineSize;
            }

            _ammoReserve ??= new AmmoReserve();
            _damageRoller ??= new UnityRandomDamageRoller();

            if (_aiming == null)
            {
                _aiming = GetComponent<PlayerAiming>();
            }

            if (_inputReader == null)
            {
                AttachInputReader(GetComponent<PlayerInput>()?.Reader);
            }
        }

        private void OnDestroy()
        {
            AttachInputReader(null);
        }

        private void AttachInputReader(IPlayerInputReader inputReader)
        {
            if (_inputReader != null)
            {
                _inputReader.Reload -= HandleReloadPressed;
            }

            _inputReader = inputReader;

            if (_inputReader != null)
            {
                _inputReader.Reload += HandleReloadPressed;
            }
        }

        private void HandleReloadPressed()
        {
            if (!IsEquipped)
            {
                return;
            }

            TryStartReload();
        }

        private void Update()
        {
            var deltaTime = Time.deltaTime;
            TickFireCooldown(deltaTime);
            TickReload(deltaTime);

            if (IsEquipped && _inputReader != null && _inputReader.FireHeld)
            {
                TryFire();
            }
        }

        private void TickFireCooldown(float deltaTime)
        {
            if (_fireCooldownRemaining > 0f)
            {
                _fireCooldownRemaining -= deltaTime;
                if (_fireCooldownRemaining < 0f)
                {
                    _fireCooldownRemaining = 0f;
                }
            }
        }

        private void TickReload(float deltaTime)
        {
            if (!IsReloading)
            {
                return;
            }

            _reloadTimeRemaining -= deltaTime;
            if (_reloadTimeRemaining <= 0f)
            {
                CompleteReload();
            }
        }

        public bool TryFire()
        {
            if (!IsEquipped || !_actionGate.CanAct(this))
            {
                return false;
            }

            if (_definition == null || _projectilePool == null)
            {
                return false;
            }

            if (IsReloading || _fireCooldownRemaining > 0f)
            {
                return false;
            }

            if (MagazineAmmo <= 0)
            {
                // Empty magazine and an empty reserve (otherwise the auto reload is already running): a dry click, one per cooldown.
                if (_ammoReserve.Get(_definition.AmmoType) < _definition.AmmoCostPerShot)
                {
                    _fireCooldownRemaining = 1f / _definition.FireRate;
                    DryFires++;
                    DryFired?.Invoke(this);
                }

                return false;
            }

            _fireCooldownRemaining = 1f / _definition.FireRate;
            MagazineAmmo--;

            var shot = ResolveShot();
            LastShot = shot;

            // One magazine round per shot; the pattern decides how many pellets that shot emits (spread around the resolved centre).
            _emitter.Emit(
                _projectilePool, FiringPattern, _damageRoller, shot.Direction, shot.SpawnPosition,
                _definition.DamageMin, _definition.DamageMax, _definition.ProjectileSpeed, _definition.Range,
                gameObject, _lastSpawnedProjectiles, _stats?.GetMultiplier(StatId.WeaponDamage) ?? 1f,
                _definition.Knockback * (_stats?.GetMultiplier(StatId.Knockback) ?? 1f),
                _definition.StaggerPower * (_stats?.GetMultiplier(StatId.StaggerPower) ?? 1f),
                _impactFeedback,
                _definition.ExplosionRadiusTiles,
                DamageTeam.Player);
            LastSpawnedProjectile = _lastSpawnedProjectiles[_lastSpawnedProjectiles.Count - 1];

            // Auto-reload: the last round leaves the magazine and reserve remains, so the existing reload begins on its
            // own — the same flow, the same gates and interruptions as a manual reload.
            if (MagazineAmmo == 0 && !IsReloading && _ammoReserve.Get(_definition.AmmoType) >= _definition.AmmoCostPerShot)
            {
                if (TryStartReload()) AutoReloads++;
            }

            return true;
        }

        /// <summary>Spawn point and direction of the next shot: pivot-anchored aim, close-range pull-back, soft assist.</summary>
        public ShotSolution ResolveShot()
        {
            var rawDirection = _aiming != null ? _aiming.AimDirection : Vector2.right;
            var origin = _aiming != null ? _aiming.AimOrigin : (Vector2)transform.position;
            var muzzle = _muzzle != null ? (Vector2)_muzzle.position : origin;
            var weaponClass = _definition != null ? _definition.WeaponClass : WeaponClass.Pistol;
            return ShotSolver.Solve(origin, muzzle, rawDirection, _definition != null ? _definition.Range : 0f, weaponClass, gameObject, DamageTeam.Player, _aimAssist, _aiming);
        }

        /// <summary>The soft aim-assist tuning; null = raw aim only.</summary>
        public void SetAimAssist(AimAssistConfig config) => _aimAssist = config;

        public AimAssistConfig AimAssist => _aimAssist;
        public ShotSolution LastShot { get; private set; }
        /// <summary>Reloads that began automatically on an emptied magazine (diagnostics/tests).</summary>
        public int AutoReloads { get; private set; }

        /// <summary>A fire press on an empty magazine with nothing left to reload: the weapon clicks. Rate-limited by the fire cooldown; never consumes anything.</summary>
        public event Action<RangedWeapon> DryFired;
        public int DryFires { get; private set; }

        private IFiringPattern BuildPatternFromDefinition()
        {
            if (_definition == null || _definition.ProjectilesPerShot <= 1 && _definition.SpreadDegrees <= 0f)
            {
                return SingleProjectilePattern.Instance;
            }

            _spreadRandom ??= new SeededRandom(unchecked((ulong)System.Environment.TickCount));
            return new SpreadFiringPattern(_definition.ProjectilesPerShot, _definition.SpreadDegrees, _spreadRandom);
        }

        /// <summary>Owner-side convergence: adopts the host's magazine/reload state when the prediction drifted.</summary>
        public void ApplyAuthoritativeState(int magazineAmmo, bool isReloading)
        {
            MagazineAmmo = Mathf.Clamp(magazineAmmo, 0, _definition != null ? _definition.MagazineSize : magazineAmmo);
            if (!isReloading && IsReloading)
            {
                IsReloading = false;
                _reloadTimeRemaining = 0f;
            }
        }

        public bool TryStartReload()
        {
            if (!IsEquipped || !_actionGate.CanAct(this))
            {
                return false;
            }

            if (_definition == null)
            {
                return false;
            }

            if (IsReloading)
            {
                return false;
            }

            if (MagazineAmmo >= _definition.MagazineSize)
            {
                return false;
            }

            if (_ammoReserve.Get(_definition.AmmoType) < _definition.AmmoCostPerShot)
            {
                return false;
            }

            IsReloading = true;
            _reloadTimeRemaining = CurrentReloadTime;
            return true;
        }

        private void CompleteReload()
        {
            var costPerShot = _definition.AmmoCostPerShot;
            var shotsNeeded = _definition.MagazineSize - MagazineAmmo;
            var shotsAffordable = _ammoReserve.Get(_definition.AmmoType) / costPerShot;
            var shotsToLoad = Mathf.Min(shotsNeeded, shotsAffordable);

            if (shotsToLoad > 0)
            {
                _ammoReserve.Consume(_definition.AmmoType, shotsToLoad * costPerShot);
                MagazineAmmo += shotsToLoad;
            }

            IsReloading = false;
            _reloadTimeRemaining = 0f;
        }

        private void CancelReload()
        {
            if (!IsReloading)
            {
                return;
            }

            IsReloading = false;
            _reloadTimeRemaining = 0f;
        }
    }
}

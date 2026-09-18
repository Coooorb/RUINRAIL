using RuinRail.Gameplay.Combat.Weapons;
using UnityEngine;

namespace RuinRail.Presentation.Animation
{
    public enum WeaponVisualState
    {
        Idle,
        Firing,
        Reloading,
        Charging,
        Overheated,
        SwingWindUp,
        SwingRecovery
    }

    /// <summary>
    /// art/103: weapons are separate sprites on the WeaponPivot; firearms animate through rotation (PlayerAiming, 360°),
    /// recoil and effects rather than full-body clips; melee motion must match the real hitbox. This driver reads the
    /// active weapon's authoritative state (magazine, reload, heat, charge, melee phase) and produces visual-only
    /// outputs: a recoil offset, a reload/charge/overheat state and a swing angle that sweeps exactly the definition's
    /// attack arc over the gameplay wind-up + recovery, at the definition's reach. It owns no ammo, damage or timing.
    /// </summary>
    public sealed class WeaponVisualDriver : MonoBehaviour
    {
        /// <summary>V1 FINAL (TASK 179) recoil: 2 px kick along −aim, decaying over 0.08 s.</summary>
        public const float RecoilPixels = 2f;
        public const float RecoilSeconds = 0.08f;

        [SerializeField] private Transform _weaponSprite;

        private WeaponLoadout _loadout;
        private RangedWeapon _ranged;
        private BlasterWeapon _blaster;
        private BowWeapon _bow;
        private MeleeWeapon _melee;
        private int _lastMagazine = -1;
        private float _recoilRemaining;
        private float _swingElapsed;
        private MeleeAttackState _lastMeleeState = MeleeAttackState.Idle;

        public WeaponVisualState State { get; private set; } = WeaponVisualState.Idle;
        /// <summary>0..1 kick strength (visual only).</summary>
        public float Recoil01 => _recoilRemaining <= 0f ? 0f : Mathf.Clamp01(_recoilRemaining / RecoilSeconds);
        public Vector2 RecoilOffsetPixels => Vector2.left * (RecoilPixels * Recoil01);
        public float ChargeFraction { get; private set; }
        public float HeatFraction { get; private set; }
        /// <summary>Degrees relative to the aim direction: −arc/2 → +arc/2 across the swing; 0 when not swinging.</summary>
        public float SwingAngleDegrees { get; private set; }
        /// <summary>Tiles the swing visual reaches: the melee definition's attack range (the hitbox reach).</summary>
        public float SwingReachTiles { get; private set; }
        public float SwingArcDegrees { get; private set; }
        public int ShotsShown { get; private set; }
        public IEquippableWeapon ActiveWeapon => _loadout != null ? _loadout.ActiveWeapon : null;
        public int SwingsShown { get; private set; }

        public void Configure(WeaponLoadout loadout, Transform weaponSprite = null)
        {
            _loadout = loadout;
            _weaponSprite = weaponSprite;
        }

        private void Awake()
        {
            if (_loadout == null) _loadout = GetComponentInParent<WeaponLoadout>();
        }

        /// <summary>Pure: where the visible blade is inside the arc for a swing that is <paramref name="progress01"/> through wind-up + recovery.</summary>
        public static float SwingAngleFor(float progress01, float arcDegrees) => Mathf.Lerp(-arcDegrees * 0.5f, arcDegrees * 0.5f, Mathf.Clamp01(progress01));

        public void Tick(float deltaTime)
        {
            var active = _loadout != null ? _loadout.ActiveWeapon : null;
            Rebind(active);
            if (_recoilRemaining > 0f) _recoilRemaining -= deltaTime;

            ChargeFraction = 0f;
            HeatFraction = 0f;
            SwingAngleDegrees = 0f;
            var state = WeaponVisualState.Idle;

            if (_ranged != null)
            {
                // A magazine only ever drops through a shot (a reload adds), so the last round of the magazine kicks too —
                // even though the auto reload is already running by the time this tick sees it.
                if (_lastMagazine >= 0 && _ranged.MagazineAmmo < _lastMagazine) Kick();
                _lastMagazine = _ranged.MagazineAmmo;
                if (_ranged.IsReloading) state = WeaponVisualState.Reloading;
                else if (Recoil01 > 0f) state = WeaponVisualState.Firing;
            }
            else if (_blaster != null)
            {
                HeatFraction = _blaster.Heat.HeatFraction;
                if (_blaster.Heat.IsOverheated) state = WeaponVisualState.Overheated;
                else if (Recoil01 > 0f) state = WeaponVisualState.Firing;
            }
            else if (_bow != null)
            {
                ChargeFraction = _bow.IsCharging ? _bow.ChargeFraction : 0f;
                if (_bow.IsCharging) state = WeaponVisualState.Charging;
                else if (Recoil01 > 0f) state = WeaponVisualState.Firing;
            }
            else if (_melee != null && _melee.Definition != null)
            {
                var def = _melee.Definition;
                SwingReachTiles = def.AttackRange;
                SwingArcDegrees = def.AttackArcDegrees;
                var total = Mathf.Max(0.0001f, def.WindUpSeconds + def.RecoverySeconds);
                if (_melee.State != MeleeAttackState.Idle)
                {
                    if (_lastMeleeState == MeleeAttackState.Idle) { _swingElapsed = 0f; SwingsShown++; }
                    else _swingElapsed += deltaTime;
                    SwingAngleDegrees = SwingAngleFor(_swingElapsed / total, def.AttackArcDegrees);
                    state = _melee.State == MeleeAttackState.WindUp ? WeaponVisualState.SwingWindUp : WeaponVisualState.SwingRecovery;
                }

                _lastMeleeState = _melee.State;
            }

            State = state;
            if (_weaponSprite != null)
            {
                _weaponSprite.localPosition = new Vector3(RecoilOffsetPixels.x / 32f, 0f, 0f);
                _weaponSprite.localRotation = Quaternion.Euler(0f, 0f, SwingAngleDegrees);
            }
        }

        /// <summary>Blaster/bow shots are visible through their own state; a fire pulse can also be reported by scene wiring.</summary>
        public void ReportShot() => Kick();

        private void Kick()
        {
            _recoilRemaining = RecoilSeconds;
            ShotsShown++;
        }

        private void Rebind(IEquippableWeapon active)
        {
            var ranged = active as RangedWeapon;
            if (ranged != _ranged) { _ranged = ranged; _lastMagazine = ranged != null ? ranged.MagazineAmmo : -1; }
            _blaster = active as BlasterWeapon;
            _bow = active as BowWeapon;
            var melee = active as MeleeWeapon;
            if (melee != _melee) { _melee = melee; _lastMeleeState = melee != null ? melee.State : MeleeAttackState.Idle; _swingElapsed = 0f; }
        }

        private void Update() => Tick(Time.deltaTime);
    }
}

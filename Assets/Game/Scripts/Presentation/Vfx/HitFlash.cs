using RuinRail.Core.Rendering;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using UnityEngine;

namespace RuinRail.Presentation.Vfx
{
    /// <summary>
    /// art/104 hit flash: tints the body renderers for a few frames after an applied hit (HealthComponent.Damaged — the
    /// one event that fires only when HP actually went down, so blocked, invulnerable, i-framed or fully mitigated hits
    /// never flash) and a warmer tint on stagger (ImpactReceiver.Staggered). Enemies use the config's hit flash; the
    /// survivor uses its own red damage profile (<see cref="UsePlayerProfile"/>). A new hit restarts the flash; the
    /// renderers always return to their cached colours. Off via the accessibility setting; never changes state.
    /// </summary>
    public sealed class HitFlash : MonoBehaviour
    {
        [SerializeField] private FeedbackConfig _config;
        [SerializeField] private SpriteRenderer[] _renderers;

        private HealthComponent _health;
        private ImpactReceiver _impact;
        private Color[] _original;
        private float _remaining;
        private float _seconds = 0.06f;
        private bool _playerProfile;
        private bool _bossProfile;

        /// <summary>0..1 strength of the last boss flash (tests / diagnostics); 0 for other profiles.</summary>
        public float LastIntensity { get; private set; }

        public bool IsFlashing => _remaining > 0f;
        public int Flashes { get; private set; }
        public int Suppressed { get; private set; }
        public Color CurrentColor { get; private set; }

        public void Configure(FeedbackConfig config, HealthComponent health, ImpactReceiver impact, params SpriteRenderer[] renderers)
        {
            Unsubscribe();
            _config = config;
            _health = health;
            _impact = impact;
            _renderers = renderers;
            CacheOriginals();
            Subscribe();
        }

        private void Awake()
        {
            if (_health == null) _health = GetComponent<HealthComponent>();
            if (_impact == null) _impact = GetComponent<ImpactReceiver>();
            if (_renderers == null || _renderers.Length == 0) _renderers = GetComponentsInChildren<SpriteRenderer>();
            CacheOriginals();
            Subscribe();
        }

        private void OnDestroy() => Unsubscribe();

        private void CacheOriginals()
        {
            _original = new Color[_renderers?.Length ?? 0];
            for (var i = 0; i < _original.Length; i++) _original[i] = _renderers[i] != null ? _renderers[i].color : Color.white;
        }

        private void Subscribe()
        {
            if (_health != null) _health.Damaged += OnDamaged;
            if (_impact != null) _impact.Staggered += OnStaggered;
        }

        private void Unsubscribe()
        {
            if (_health != null) _health.Damaged -= OnDamaged;
            if (_impact != null) _impact.Staggered -= OnStaggered;
        }

        /// <summary>The survivor's damage read: the config's red player tint and duration instead of the enemy flash.</summary>
        public void UsePlayerProfile() => _playerProfile = true;

        /// <summary>A boss's damage read: the red tint scales with the effective damage of each hit (see FeedbackConfig).</summary>
        public void UseBossProfile() => _bossProfile = true;

        public bool IsBossProfile => _bossProfile;

        // `applied` is HealthComponent.Damaged's amount: what the hit actually took off after invulnerability,
        // mitigation and clamping (the replicated HP drop on a co-op client), never the weapon's nominal damage.
        private void OnDamaged(int applied)
        {
            if (_bossProfile && _config != null)
            {
                LastIntensity = _config.BossFlashIntensity(applied, _health != null ? _health.MaxHealth : 0);
                Flash(_config.BossFlashColor(applied, _health != null ? _health.MaxHealth : 0));
                return;
            }

            Flash(_playerProfile
                ? (_config != null ? _config.PlayerHitFlashColor : new Color(1f, 0.32f, 0.32f))
                : (_config != null ? _config.HitFlashColor : new Color(1f, 0.38f, 0.38f)));
        }
        private void OnStaggered(ImpactReceiver _) => Flash(_config != null ? _config.StaggerFlashColor : new Color(1f, 0.85f, 0.4f));

        public void Flash(Color color)
        {
            if (!FeedbackPreferences.HitFlash)
            {
                Suppressed++;
                return;
            }

            _seconds = _config == null ? (_playerProfile || _bossProfile ? 0.12f : 0.1f)
                : _playerProfile ? _config.PlayerHitFlashSeconds : _bossProfile ? _config.BossFlashSeconds : _config.HitFlashSeconds;
            _remaining = _seconds;
            CurrentColor = color;
            Flashes++;
            Apply(color);
        }

        public void Tick(float deltaTime)
        {
            if (!IsFlashing) return;
            _remaining -= deltaTime;
            if (_remaining <= 0f) Restore();
        }

        private void Apply(Color color)
        {
            if (_renderers == null) return;
            foreach (var r in _renderers) if (r != null) r.color = color;
        }

        private void Restore()
        {
            _remaining = 0f;
            if (_renderers == null) return;
            for (var i = 0; i < _renderers.Length; i++) if (_renderers[i] != null) _renderers[i].color = _original[i];
        }

        /// <summary>A flash never outlives its component: disabling mid-flash puts the colours back.</summary>
        private void OnDisable()
        {
            if (IsFlashing) Restore();
        }

        private void Update() => Tick(Time.deltaTime);
    }
}

using RuinRail.Core.Rendering;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using UnityEngine;

namespace RuinRail.Presentation.Vfx
{
    /// <summary>
    /// art/104 enemy hit flash: tints the body renderers for a few frames after an applied hit (HealthComponent.Damaged)
    /// and a warmer tint on stagger (ImpactReceiver.Staggered). Off via the accessibility setting; never changes state.
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

        private void OnDamaged(int _) => Flash(_config != null ? _config.HitFlashColor : Color.white);
        private void OnStaggered(ImpactReceiver _) => Flash(_config != null ? _config.StaggerFlashColor : new Color(1f, 0.85f, 0.4f));

        public void Flash(Color color)
        {
            if (!FeedbackPreferences.HitFlash)
            {
                Suppressed++;
                return;
            }

            _seconds = _config != null ? _config.HitFlashSeconds : 0.06f;
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
            if (_renderers == null) return;
            for (var i = 0; i < _renderers.Length; i++) if (_renderers[i] != null) _renderers[i].color = _original[i];
        }

        private void Update() => Tick(Time.deltaTime);
    }
}

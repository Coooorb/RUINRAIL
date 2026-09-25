using RuinRail.Core.Rendering;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Projectiles
{
    /// <summary>
    /// The in-flight presentation of one pooled <see cref="Projectile"/>: a body sprite (and optional trail) parented
    /// to the projectile object itself, so it is exactly where the physics body is, points exactly where the body
    /// points (the projectile rotates to its velocity), disappears the instant the projectile is returned to the pool
    /// on a hit / wall / expiry, and is reset before every reuse. There is no separate fake bullet that could diverge
    /// from the authoritative shot. Sorting: the Projectiles layer — above floor, props and characters, under the
    /// above-player wall band.
    /// </summary>
    public sealed class ProjectileVisual : MonoBehaviour
    {
        private SpriteRenderer _body;
        private SpriteRenderer _trail;
        private ProjectileVisualCatalog.Profile _profile;
        private float _frameClock;
        private int _frame;

        public ProjectileVisualCatalog.Profile Profile => _profile;
        public string ProfileId => _profile?.Id ?? string.Empty;
        public bool IsVisible => _body != null && _body.enabled && _body.sprite != null;
        public SpriteRenderer Body => _body;
        public SpriteRenderer Trail => _trail;
        public int Frame => _frame;

        private void Awake()
        {
            EnsureRenderers();
        }

        private void EnsureRenderers()
        {
            if (_body == null) _body = Make("Body", 1);
            if (_trail == null) _trail = Make("Trail", 0);
        }

        private SpriteRenderer Make(string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sortingLayerName = SortingLayers.Projectiles;
            renderer.sortingOrder = order;
            renderer.enabled = false;
            return renderer;
        }

        /// <summary>Binds the profile a spawn names (through the active catalog, with the side's default as fallback).</summary>
        public void Apply(string visualId, DamageTeam team)
        {
            EnsureRenderers();
            var catalog = ProjectileVisualCatalog.Active;
            Apply(catalog != null ? catalog.Resolve(visualId, team) : null);
        }

        public void Apply(ProjectileVisualCatalog.Profile profile)
        {
            EnsureRenderers();
            _profile = profile != null && profile.IsValid ? profile : null;
            _frame = 0;
            _frameClock = 0f;
            if (_profile == null)
            {
                Clear();
                return;
            }

            _body.sprite = _profile.Frames[0];
            _body.enabled = true;
            _body.transform.localPosition = Vector3.zero;
            _body.transform.localRotation = Quaternion.identity;
            if (_profile.Trail != null)
            {
                _trail.sprite = _profile.Trail;
                _trail.enabled = true;
                _trail.transform.localPosition = new Vector3(-Mathf.Max(0f, _profile.TrailBack), 0f, 0f);
                _trail.transform.localRotation = Quaternion.identity;
            }
            else
            {
                _trail.sprite = null;
                _trail.enabled = false;
            }
        }

        /// <summary>Pooled reset: no sprite, no stale frame or trail survives into the next spawn.</summary>
        public void Clear()
        {
            _profile = null;
            _frame = 0;
            _frameClock = 0f;
            if (_body != null) { _body.sprite = null; _body.enabled = false; }
            if (_trail != null) { _trail.sprite = null; _trail.enabled = false; }
        }

        private void Update()
        {
            if (_profile == null || _profile.Frames.Length < 2 || _profile.FrameSeconds <= 0f) return;
            _frameClock += Time.deltaTime;
            while (_frameClock >= _profile.FrameSeconds)
            {
                _frameClock -= _profile.FrameSeconds;
                _frame = (_frame + 1) % _profile.Frames.Length;
                _body.sprite = _profile.Frames[_frame];
            }
        }
    }
}

using System.Collections.Generic;
using RuinRail.Core.Rendering;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.Presentation.Vfx
{
    /// <summary>
    /// Dash presentation on a player body: a cold push-off burst ('dash_burst') where the dash started and a short trail
    /// of fading afterimages of the body's current frame, drawn at the bottom of the Characters layer — behind every actor,
    /// so the trail never covers an enemy, and above the floor/hazard layers only for its ~0.2 s. Read-only over <see cref="PlayerDash"/>: it watches IsDashing and
    /// DashDirection and never touches timing, speed, iFrames or collision.
    /// </summary>
    public sealed class DashTrailVfx : MonoBehaviour
    {
        // Presentation-only values, authored against the ~3-tile, 0.18 s dash: one ghost on launch and one per 0.5 tiles
        // travelled after it (spaced by distance, so a slow frame never leaves a gap in the trail). The tail is faintest
        // and the ghost nearest the player strongest, so the trail reads as one smear toward the body, not a row of clones.
        public const float GhostSpacingTiles = 0.5f;
        public const float GhostLifetime = 0.2f;
        public const float GhostTailAlpha = 0.22f;
        public const float GhostHeadAlpha = 0.6f;
        public const float BurstLifetime = 0.24f;
        public const int MaxGhostsPerDash = 6;
        private const int BehindAllActors = short.MinValue + 2;
        private static readonly Color GhostTint = new(0.5f, 0.88f, 1f, 1f);

        private PlayerDash _dash;
        private SpriteRenderer _body;
        private EffectPool _pool;
        private System.Func<string, IReadOnlyList<Sprite>> _frames;
        private bool _wasDashing;
        private Vector2 _lastGhostAt;
        private int _ghostsThisDash;
        private readonly List<(PooledEffect effect, float alpha)> _ghosts = new();

        public int DashesShown { get; private set; }
        public int GhostsShown { get; private set; }
        public int BurstsShown { get; private set; }
        public EffectPool Pool => _pool;

        public void Configure(PlayerDash dash, SpriteRenderer body, System.Func<string, IReadOnlyList<Sprite>> frames)
        {
            _dash = dash;
            _body = body;
            _frames = frames;
            EnsurePool();
        }

        private void EnsurePool()
        {
            if (_pool != null) return;
            // Unparented, in the active scene: effects stay where they were dropped while the player moves on. A
            // player that outlives a scene load gets a fresh pool on its next dash.
            _ghosts.Clear();
            _pool = new GameObject("DashEffects").AddComponent<EffectPool>();
            _pool.Configure(12, SortingRole.Character);
            _pool.SetSpriteResolver(_frames);
        }

        private void LateUpdate()
        {
            if (_dash == null) return;
            EnsurePool();
            var dashing = _dash.IsDashing;
            if (dashing && !_wasDashing)
            {
                DashesShown++;
                _ghostsThisDash = 0;
                _lastGhostAt = transform.position;
                Burst();
                Ghost(_lastGhostAt);
            }
            else if (dashing)
            {
                var at = (Vector2)transform.position;
                var step = at - _lastGhostAt;
                while (step.magnitude >= GhostSpacingTiles && _ghostsThisDash < MaxGhostsPerDash)
                {
                    _lastGhostAt += step.normalized * GhostSpacingTiles;
                    Ghost(_lastGhostAt);
                    step = at - _lastGhostAt;
                }
            }

            _wasDashing = dashing;
            FadeGhosts();
        }

        private void Burst()
        {
            var direction = _dash.DashDirection.sqrMagnitude > 0.0001f ? _dash.DashDirection.normalized : Vector2.right;
            var at = (Vector2)transform.position;
            // Authored pointing +X = behind the dash.
            var effect = _pool.Spawn("dash_burst", at, BurstLifetime, new Color(1f, 1f, 1f, 0.85f), 1f, Mathf.Atan2(-direction.y, -direction.x) * Mathf.Rad2Deg);
            if (effect == null) return;
            effect.transform.localScale = Vector3.one; // native pixels on the pixel-perfect camera
            effect.Renderer.flipX = false;
            effect.Renderer.sortingOrder = BehindAllActors - 1;
            BurstsShown++;
        }

        private void Ghost(Vector2 playerAt)
        {
            if (_body == null || _body.sprite == null || !_body.enabled) return;
            var bodyOffset = (Vector2)(_body.transform.position - transform.position);
            var effect = _pool.Spawn("dash_ghost", playerAt + bodyOffset, GhostLifetime, GhostTint, 1f, 0f, _body.sprite);
            if (effect == null) return;
            effect.transform.localScale = _body.transform.lossyScale;
            effect.Renderer.flipX = _body.flipX;
            effect.Renderer.sortingOrder = BehindAllActors + _ghostsThisDash; // later ghosts over earlier ones
            _ghosts.Add((effect, Mathf.Lerp(GhostTailAlpha, GhostHeadAlpha, _ghostsThisDash / (float)(MaxGhostsPerDash - 1))));
            _ghostsThisDash++;
            GhostsShown++;
        }

        private void FadeGhosts()
        {
            for (var i = _ghosts.Count - 1; i >= 0; i--)
            {
                var (ghost, alpha) = _ghosts[i];
                if (ghost == null || !ghost.IsActive || ghost.Kind != "dash_ghost") { _ghosts.RemoveAt(i); continue; }
                var fade = 1f - ghost.Lifetime01;
                var c = GhostTint;
                c.a = alpha * fade * fade;
                ghost.Renderer.color = c;
            }
        }

        private void OnDestroy()
        {
            if (_pool != null) Destroy(_pool.gameObject);
        }
    }
}

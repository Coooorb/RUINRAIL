using System.Collections.Generic;
using RuinRail.Core.Rendering;
using UnityEngine;

namespace RuinRail.Presentation.Vfx
{
    /// <summary>A pooled, timed sprite effect: shown for its lifetime, then returned. Never touches gameplay.</summary>
    public sealed class PooledEffect : MonoBehaviour
    {
        private EffectPool _pool;
        private float _remaining;
        private float _lifetime;

        public SpriteRenderer Renderer { get; private set; }
        public bool IsActive => _remaining > 0f;
        public float Lifetime01 => _lifetime <= 0f ? 1f : Mathf.Clamp01(1f - _remaining / _lifetime);
        public string Kind { get; private set; } = string.Empty;

        internal void Bind(EffectPool pool)
        {
            _pool = pool;
            Renderer = GetComponent<SpriteRenderer>();
            if (Renderer == null) Renderer = gameObject.AddComponent<SpriteRenderer>();
        }

        /// <summary>The frames this effect plays over its lifetime (the placeholder is a single frame).</summary>
        public IReadOnlyList<Sprite> Frames { get; private set; }
        public int CurrentFrame { get; private set; }

        /// <summary>
        /// World size of one unit of <c>scale</c>: every caller authored its scales against the 8 px (0.25 tile)
        /// placeholder, so a final sprite of any pixel size is normalised to draw the same world footprint.
        /// </summary>
        public const float ScaleUnitTiles = 0.25f;

        public void Show(string kind, Vector2 position, float lifetime, Color color, float scale, float rotationDegrees = 0f, Sprite sprite = null)
            => Show(kind, position, lifetime, color, scale, rotationDegrees, sprite != null ? new[] { sprite } : null);

        public void Show(string kind, Vector2 position, float lifetime, Color color, float scale, float rotationDegrees, IReadOnlyList<Sprite> frames)
        {
            Kind = kind;
            _lifetime = Mathf.Max(0.01f, lifetime);
            _remaining = _lifetime;
            transform.position = new Vector3(position.x, position.y, 0f);
            transform.rotation = Quaternion.Euler(0f, 0f, rotationDegrees);
            Frames = frames != null && frames.Count > 0 ? frames : null;
            CurrentFrame = 0;
            if (Frames != null) Renderer.sprite = Frames[0];
            var unit = Renderer.sprite != null && Renderer.sprite.bounds.size.x > 0.0001f ? ScaleUnitTiles / Renderer.sprite.bounds.size.x : 1f;
            transform.localScale = new Vector3(scale * unit, scale * unit, 1f);
            Renderer.color = color;
            gameObject.SetActive(true);
        }

        /// <summary>Sets the drawn footprint in world tiles (x along the effect's facing, y across), whatever the sprite's pixel size.</summary>
        public void SetWorldSize(Vector2 sizeTiles)
        {
            var sprite = Renderer.sprite;
            if (sprite == null) return;
            var b = sprite.bounds.size;
            transform.localScale = new Vector3(b.x > 0.0001f ? sizeTiles.x / b.x : 1f, b.y > 0.0001f ? sizeTiles.y / b.y : 1f, 1f);
        }

        public void Tick(float deltaTime)
        {
            if (!IsActive) return;
            _remaining -= deltaTime;
            if (Frames != null && Frames.Count > 1)
            {
                var frame = Mathf.Clamp(Mathf.FloorToInt(Lifetime01 * Frames.Count), 0, Frames.Count - 1);
                if (frame != CurrentFrame) { CurrentFrame = frame; Renderer.sprite = Frames[frame]; }
            }

            if (_remaining <= 0f) _pool.Return(this);
        }

        private void Update() => Tick(Time.deltaTime);
    }

    /// <summary>
    /// Fixed-capacity pool for high-frequency effects: instances are created once (prewarm or lazily up to the cap),
    /// the oldest live effect is recycled when the cap is reached, and nothing is ever destroyed or leaked.
    /// </summary>
    public sealed class EffectPool : MonoBehaviour
    {
        [SerializeField] private int _capacity = 64;
        [SerializeField] private SortingRole _role = SortingRole.WorldVfx;

        private readonly List<PooledEffect> _all = new();
        private readonly Queue<PooledEffect> _free = new();
        private readonly List<PooledEffect> _live = new();
        private Sprite _placeholder;
        private System.Func<string, IReadOnlyList<Sprite>> _frames;

        /// <summary>Binds the final effect art: frames per effect kind ('muzzle', 'impact', 'telegraph_dash', …). Unknown kinds keep the placeholder.</summary>
        public void SetSpriteResolver(System.Func<string, IReadOnlyList<Sprite>> frames) => _frames = frames;

        /// <summary>True when a final sprite set exists for a kind (validators/tests).</summary>
        public bool HasArtFor(string kind) => _frames != null && _frames(kind) is { Count: > 0 };

        public int Capacity => _capacity;
        public int Created => _all.Count;
        public int Live => _live.Count;
        public int Recycled { get; private set; }
        public int Spawned { get; private set; }

        public void Configure(int capacity, SortingRole role = SortingRole.WorldVfx)
        {
            _capacity = Mathf.Max(1, capacity);
            _role = role;
        }

        /// <summary>A flat 8×8 white sprite: the explicit placeholder until effect art arrives (BLOCKED_EXTERNAL_ASSET).</summary>
        public Sprite Placeholder
        {
            get
            {
                if (_placeholder == null)
                {
                    var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                    var pixels = new Color[64];
                    for (var i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
                    tex.SetPixels(pixels);
                    tex.Apply();
                    _placeholder = Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), SortingConvention.PixelsPerUnit);
                }

                return _placeholder;
            }
        }

        public PooledEffect Spawn(string kind, Vector2 position, float lifetime, Color color, float scale = 1f, float rotationDegrees = 0f, Sprite sprite = null)
        {
            PooledEffect effect;
            if (_free.Count > 0) effect = _free.Dequeue();
            else if (_all.Count < _capacity) effect = Create();
            else
            {
                effect = _live[0];
                _live.RemoveAt(0);
                Recycled++;
            }

            _live.Add(effect);
            Spawned++;
            var frames = sprite != null ? new[] { sprite } : _frames?.Invoke(kind);
            if (frames == null || frames.Count == 0) frames = new[] { Placeholder };
            effect.Show(kind, position, lifetime, color, scale, rotationDegrees, frames);
            return effect;
        }

        public void Return(PooledEffect effect)
        {
            if (effect == null) return;
            if (!_live.Remove(effect)) return;
            effect.gameObject.SetActive(false);
            _free.Enqueue(effect);
        }

        public void TickAll(float deltaTime)
        {
            for (var i = _live.Count - 1; i >= 0; i--) _live[i].Tick(deltaTime);
        }

        private PooledEffect Create()
        {
            var go = new GameObject("Effect");
            go.transform.SetParent(transform, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            SpriteSorting.Apply(renderer, _role);
            var effect = go.AddComponent<PooledEffect>();
            effect.Bind(this);
            go.SetActive(false);
            _all.Add(effect);
            return effect;
        }
    }
}

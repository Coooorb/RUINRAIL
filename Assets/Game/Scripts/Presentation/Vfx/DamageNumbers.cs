using System.Collections.Generic;
using RuinRail.Core.Rendering;
using RuinRail.Gameplay.Combat;
using UnityEngine;

namespace RuinRail.Presentation.Vfx
{
    /// <summary>
    /// One pooled world-space number.
    ///
    /// The figure is drawn twice: a near-black copy one reference pixel down-right, then the number itself over it.
    /// Plain white text had nothing behind it, so a number landing on a lit floor tile, a muzzle flash or an impact
    /// flash disappeared into it exactly when the player most wanted to read it — and a translucent plate behind every
    /// number would be a box of UI in the middle of the fight. A one-pixel shadow is what pixel art uses instead: it
    /// costs one extra quad, it reads at any background value, and it keeps the figure itself unblurred and on grid.
    /// </summary>
    public sealed class DamageNumber : MonoBehaviour
    {
        /// <summary>Shadow offset in reference pixels (one pixel right and one down, on the 32 PPU grid).</summary>
        public const int ShadowPixels = 1;

        private DamageNumberPool _pool;
        private float _remaining;
        private float _lifetime;
        private Vector2 _origin;
        private float _risePixels;

        public TextMesh Text { get; private set; }
        /// <summary>The dark copy under the figure; the tests assert it tracks the number's own text.</summary>
        public TextMesh Shadow { get; private set; }
        public int Value { get; private set; }
        public bool IsHeal { get; private set; }
        public bool IsActive => _remaining > 0f;

        internal void Bind(DamageNumberPool pool)
        {
            _pool = pool;
            Text = GetComponent<TextMesh>();
            if (Text == null) Text = gameObject.AddComponent<TextMesh>();
            Text.characterSize = 0.1f;
            Text.fontSize = 32;
            Text.anchor = TextAnchor.MiddleCenter;
            var renderer = GetComponent<MeshRenderer>();
            if (renderer != null) SpriteSorting.Apply(renderer, SortingRole.WorldUi);

            if (Shadow == null)
            {
                var shadowGo = new GameObject("Shadow");
                shadowGo.transform.SetParent(transform, false);
                var offset = ShadowPixels / (float)SortingConvention.PixelsPerUnit;
                shadowGo.transform.localPosition = new Vector3(offset, -offset, 0f);
                Shadow = shadowGo.AddComponent<TextMesh>();
                Shadow.font = Text.font;
                Shadow.characterSize = Text.characterSize;
                Shadow.fontSize = Text.fontSize;
                Shadow.anchor = Text.anchor;
                Shadow.color = new Color(0.03f, 0.04f, 0.05f, 0.9f);
                var shadowRenderer = shadowGo.GetComponent<MeshRenderer>();
                if (shadowRenderer != null)
                {
                    shadowRenderer.sharedMaterial = Text.font != null ? Text.font.material : shadowRenderer.sharedMaterial;
                    SpriteSorting.Apply(shadowRenderer, SortingRole.WorldUi);
                    // One order under the figure, so the shadow can never draw over the number it is behind.
                    shadowRenderer.sortingOrder -= 1;
                }
            }
        }

        public void Show(int value, bool isHeal, Vector2 position, float lifetime, float risePixels)
        {
            Value = value;
            IsHeal = isHeal;
            _origin = position;
            _lifetime = Mathf.Max(0.01f, lifetime);
            _remaining = _lifetime;
            _risePixels = risePixels;
            Text.text = isHeal ? "+" + value : value.ToString();
            Text.color = isHeal ? new Color(0.55f, 1f, 0.55f) : Color.white;
            if (Shadow != null) Shadow.text = Text.text;
            transform.position = new Vector3(position.x, position.y, 0f);
            gameObject.SetActive(true);
        }

        public void Tick(float deltaTime)
        {
            if (!IsActive) return;
            _remaining -= deltaTime;
            var t = 1f - Mathf.Clamp01(_remaining / _lifetime);
            var rise = Mathf.Round(t * _risePixels) / SortingConvention.PixelsPerUnit;
            transform.position = new Vector3(_origin.x, _origin.y + rise, 0f);
            if (_remaining <= 0f) _pool.Return(this);
        }

        private void Update() => Tick(Time.deltaTime);
    }

    /// <summary>
    /// ui/91: small, plain damage numbers (no crit styling — there are no crits), ON/OFF in Settings. Values are the
    /// authoritative applied integers from HealthComponent (Damaged/Healed), shown at the target's world position and
    /// pooled with a fixed cap (the oldest is recycled). Nothing here can trigger or alter gameplay.
    /// </summary>
    public sealed class DamageNumberPool : MonoBehaviour
    {
        [SerializeField] private FeedbackConfig _config;

        private readonly List<DamageNumber> _all = new();
        private readonly Queue<DamageNumber> _free = new();
        private readonly List<DamageNumber> _live = new();
        private readonly Dictionary<HealthComponent, (System.Action<int> damaged, System.Action<int> healed)> _bindings = new();

        public int Capacity => _config != null ? Mathf.Max(1, _config.DamageNumberCapacity) : 48;
        public int Created => _all.Count;
        public int Live => _live.Count;
        public int Shown { get; private set; }
        public int Suppressed { get; private set; }
        public IReadOnlyList<DamageNumber> LiveNumbers => _live;

        public void Configure(FeedbackConfig config) => _config = config;

        /// <summary>Observe a health component: every applied damage/heal becomes a number (read-only subscription).</summary>
        public void Bind(HealthComponent health, Transform anchor = null)
        {
            if (health == null || _bindings.ContainsKey(health)) return;
            var at = anchor != null ? anchor : health.transform;
            System.Action<int> damaged = amount => Show(amount, false, at.position);
            System.Action<int> healed = amount => Show(amount, true, at.position);
            health.Damaged += damaged;
            health.Healed += healed;
            _bindings[health] = (damaged, healed);
        }

        public void Unbind(HealthComponent health)
        {
            if (health == null || !_bindings.TryGetValue(health, out var b)) return;
            health.Damaged -= b.damaged;
            health.Healed -= b.healed;
            _bindings.Remove(health);
        }

        public DamageNumber Show(int value, bool isHeal, Vector2 position)
        {
            if (!FeedbackPreferences.DamageNumbers || value <= 0)
            {
                Suppressed++;
                return null;
            }

            DamageNumber number;
            if (_free.Count > 0) number = _free.Dequeue();
            else if (_all.Count < Capacity) number = Create();
            else { number = _live[0]; _live.RemoveAt(0); }
            _live.Add(number);
            Shown++;
            var lifetime = _config != null ? _config.DamageNumberSeconds : 0.7f;
            var rise = _config != null ? _config.DamageNumberRisePixels : 12f;
            number.Show(value, isHeal, position + Vector2.up * 0.75f, lifetime, rise);
            return number;
        }

        public void Return(DamageNumber number)
        {
            if (number == null || !_live.Remove(number)) return;
            number.gameObject.SetActive(false);
            _free.Enqueue(number);
        }

        public void TickAll(float deltaTime)
        {
            for (var i = _live.Count - 1; i >= 0; i--) _live[i].Tick(deltaTime);
        }

        private DamageNumber Create()
        {
            var go = new GameObject("DamageNumber");
            go.transform.SetParent(transform, false);
            var number = go.AddComponent<DamageNumber>();
            number.Bind(this);
            go.SetActive(false);
            _all.Add(number);
            return number;
        }

        private void OnDestroy()
        {
            foreach (var pair in _bindings)
            {
                if (pair.Key == null) continue;
                pair.Key.Damaged -= pair.Value.damaged;
                pair.Key.Healed -= pair.Value.healed;
            }

            _bindings.Clear();
        }
    }
}

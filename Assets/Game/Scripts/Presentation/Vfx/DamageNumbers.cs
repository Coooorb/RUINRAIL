using System.Collections.Generic;
using RuinRail.Core.Rendering;
using RuinRail.Gameplay.Combat;
using UnityEngine;

namespace RuinRail.Presentation.Vfx
{
    /// <summary>What a number reports: damage the player dealt, damage the local player took, or healing.</summary>
    public enum DamageNumberKind
    {
        Dealt,
        Taken,
        Heal
    }

    /// <summary>
    /// One pooled world-space number, drawn in the project's own pixel font (Resources/Fonts/ruinrail_pixel, the face
    /// the whole UI uses) at an exact integer pixel scale on the 32 PPU grid, with a one-font-pixel dark outline.
    ///
    /// The outline is eight dark copies one font pixel away (the down-right one is <see cref="Shadow"/>), under the
    /// figure: a number landing on a lit floor tile, a muzzle flash, an impact or a boss body stays legible, with no
    /// translucent plate and no blur. The point-filtered bitmap face, the snapped position and the parity correction
    /// keep every glyph pixel on a screen pixel; the rise moves in whole pixels.
    /// </summary>
    public sealed class DamageNumber : MonoBehaviour
    {
        /// <summary>Outline / shadow offset in font pixels (one pixel on every side).</summary>
        public const int ShadowPixels = 1;
        public const string PixelFontResource = "Fonts/ruinrail_pixel";
        /// <summary>TextMesh draws one font pixel as 0.1 world units at characterSize 1; the world has 32 pixels per unit.</summary>
        private const float CharacterSizePerScale = 10f / SortingConvention.PixelsPerUnit;
        private const int GlyphHeightPixels = 7;
        /// <summary>GameObjects one pooled number owns: the figure and its eight outline copies (pool budgets count this).</summary>
        public const int ObjectsPerNumber = 9;

        private static Font _font;
        private static readonly Vector2Int[] OutlineOffsets =
        {
            new(1, -1), new(-1, -1), new(1, 1), new(-1, 1), new(1, 0), new(-1, 0), new(0, 1), new(0, -1)
        };

        private DamageNumberPool _pool;
        private float _remaining;
        private float _lifetime;
        private Vector2 _origin;
        private float _risePixels;
        private int _scale = 1;
        private readonly List<TextMesh> _outline = new();

        public TextMesh Text { get; private set; }
        /// <summary>The down-right outline copy; the tests assert it tracks the number's own text.</summary>
        public TextMesh Shadow { get; private set; }
        public IReadOnlyList<TextMesh> Outline => _outline;
        public int Value { get; private set; }
        public DamageNumberKind Kind { get; private set; }
        public bool IsHeal => Kind == DamageNumberKind.Heal;
        public bool IsActive => _remaining > 0f;
        public int PixelScale => _scale;
        /// <summary>True when the digits are drawn with the RUINRAIL pixel face (false only if the resource is missing).</summary>
        public bool UsesPixelFont => Text != null && Text.font != null && Text.font == PixelFont();

        public static Font PixelFont()
        {
            if (_font == null) _font = Resources.Load<Font>(PixelFontResource);
            return _font;
        }

        internal void Bind(DamageNumberPool pool, int pixelScale, Color outlineColor)
        {
            _pool = pool;
            _scale = Mathf.Clamp(pixelScale, 1, 3);
            var font = PixelFont();
            Text = GetComponent<TextMesh>();
            if (Text == null) Text = gameObject.AddComponent<TextMesh>();
            Configure(Text, gameObject, font);
            var renderer = GetComponent<MeshRenderer>();
            if (renderer != null) SpriteSorting.Apply(renderer, SortingRole.WorldUi);

            if (_outline.Count > 0) return;
            var step = _scale * ShadowPixels / (float)SortingConvention.PixelsPerUnit;
            foreach (var offset in OutlineOffsets)
            {
                var copy = new GameObject("Outline");
                copy.transform.SetParent(transform, false);
                copy.transform.localPosition = new Vector3(offset.x * step, offset.y * step, 0f);
                var mesh = copy.AddComponent<TextMesh>();
                Configure(mesh, copy, font);
                mesh.color = outlineColor;
                var outlineRenderer = copy.GetComponent<MeshRenderer>();
                if (outlineRenderer != null)
                {
                    SpriteSorting.Apply(outlineRenderer, SortingRole.WorldUi);
                    // One order under the figure, so the outline can never draw over the number it frames.
                    outlineRenderer.sortingOrder -= 1;
                }

                _outline.Add(mesh);
            }

            Shadow = _outline[0];
        }

        private void Configure(TextMesh mesh, GameObject owner, Font font)
        {
            if (font != null)
            {
                mesh.font = font;
                var renderer = owner.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.sharedMaterial = font.material; // the point-filtered atlas of the face
            }

            mesh.fontSize = 0; // a bitmap face: its native size, scaled by characterSize only
            mesh.characterSize = CharacterSizePerScale * _scale;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
        }

        public void Show(int value, bool isHeal, Vector2 position, float lifetime, float risePixels) =>
            Show(value, isHeal ? DamageNumberKind.Heal : DamageNumberKind.Dealt, position, lifetime, risePixels, isHeal ? new Color(0.55f, 0.95f, 0.55f) : Color.white);

        public void Show(int value, DamageNumberKind kind, Vector2 position, float lifetime, float risePixels, Color color)
        {
            Value = value;
            Kind = kind;
            _origin = position;
            _lifetime = Mathf.Max(0.01f, lifetime);
            _remaining = _lifetime;
            _risePixels = risePixels;
            Text.text = kind == DamageNumberKind.Heal ? "+" + value : value.ToString();
            Text.color = color;
            foreach (var copy in _outline) copy.text = Text.text;
            Place(0f);
            gameObject.SetActive(true);
        }

        public void Tick(float deltaTime)
        {
            if (!IsActive) return;
            _remaining -= deltaTime;
            var t = 1f - Mathf.Clamp01(_remaining / _lifetime);
            Place(Mathf.Round(t * _risePixels));
            if (_remaining <= 0f) _pool.Return(this);
        }

        /// <summary>
        /// Puts the figure on the pixel grid: the anchor snaps to whole pixels, and a glyph block with an odd pixel
        /// height (7 px at scale 1) is centred half a pixel off so its rows still land on screen rows. Widths are always
        /// even (6-pixel advance), so the horizontal centre is already on the grid.
        /// </summary>
        private void Place(float risePixels)
        {
            const float ppu = SortingConvention.PixelsPerUnit;
            var x = Mathf.Round(_origin.x * ppu) / ppu;
            var y = (Mathf.Round(_origin.y * ppu) + risePixels) / ppu;
            if (GlyphHeightPixels * _scale % 2 == 1) y += 0.5f / ppu;
            transform.position = new Vector3(x, y, 0f);
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
        /// <param name="isLocalPlayer">The local player's own health: its damage reads as damage taken (red), not dealt.</param>
        public void Bind(HealthComponent health, Transform anchor = null, bool isLocalPlayer = false)
        {
            if (health == null || _bindings.ContainsKey(health)) return;
            var at = anchor != null ? anchor : health.transform;
            var hurt = isLocalPlayer ? DamageNumberKind.Taken : DamageNumberKind.Dealt;
            System.Action<int> damaged = amount => Show(amount, hurt, at.position);
            System.Action<int> healed = amount => Show(amount, DamageNumberKind.Heal, at.position);
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

        public DamageNumber Show(int value, bool isHeal, Vector2 position) => Show(value, isHeal ? DamageNumberKind.Heal : DamageNumberKind.Dealt, position);

        public DamageNumber Show(int value, DamageNumberKind kind, Vector2 position)
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
            number.Show(value, kind, position + Vector2.up * 0.75f, lifetime, rise, ColorOf(kind));
            return number;
        }

        public Color ColorOf(DamageNumberKind kind) => kind switch
        {
            DamageNumberKind.Taken => _config != null ? _config.DamageTakenColor : new Color(1f, 0.52f, 0.44f),
            DamageNumberKind.Heal => _config != null ? _config.HealNumberColor : new Color(0.55f, 0.95f, 0.55f),
            _ => _config != null ? _config.DamageDealtColor : new Color(0.96f, 0.92f, 0.82f)
        };

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
            number.Bind(this, _config != null ? _config.DamageNumberPixelScale : 1, _config != null ? _config.DamageNumberOutlineColor : new Color(0.04f, 0.05f, 0.06f));
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

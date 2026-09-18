using RuinRail.Core.Rendering;
using RuinRail.Gameplay.Combat;
using UnityEngine;

namespace RuinRail.Presentation.Vfx
{
    /// <summary>
    /// Compact world-space health bar over an enemy (91/art/104 readability): a 1 px dark border, a dark back and a
    /// high-contrast fill, drawn on the UIWorld layer so it sits above characters and props in every biome. Built once
    /// per actor at spawn and only re-scaled on health events — never instantiated per damage tick. Hidden while the
    /// actor is at full health, shown from the first point of damage, hidden again on death. It never rotates with the
    /// body and follows the actor's position, pixel-snapped. Health is read from <see cref="HealthComponent"/>, so a
    /// client showing replicated health (ApplyReplicatedHealth raises the same events) draws the authoritative value.
    /// </summary>
    public sealed class WorldHealthBar : MonoBehaviour
    {
        public enum Style
        {
            Normal,
            Elite
        }

        public const int NormalWidthPx = 24;
        public const int EliteWidthPx = 32;
        public const int HeightPx = 4;
        public const int BorderPx = 1;
        public const float PixelsPerUnit = SortingConvention.PixelsPerUnit;

        public static readonly Color BorderColor = new(0.04f, 0.05f, 0.06f, 1f);
        public static readonly Color BackColor = new(0.16f, 0.17f, 0.18f, 1f);
        public static readonly Color NormalFill = new(0.85f, 0.2f, 0.2f, 1f);
        /// <summary>Elite accent (the same warm accent the Elite telegraph uses) when no config colour is supplied.</summary>
        public static readonly Color EliteFill = new(0.91f, 0.65f, 0.29f, 1f);

        private static Sprite _pixel;

        private HealthComponent _health;
        private Transform _root;
        private SpriteRenderer _border;
        private SpriteRenderer _back;
        private SpriteRenderer _fill;
        private float _heightAbove = 1.35f;
        private int _widthPx = NormalWidthPx;

        public Style BarStyle { get; private set; }
        public int WidthPx => _widthPx;
        public bool IsVisible => _root != null && _root.gameObject.activeSelf;
        public float Fill01 { get; private set; }
        public Color FillColor => _fill != null ? _fill.color : Color.clear;
        public int Refreshes { get; private set; }

        /// <summary>A shared 1×1 sprite at the world PPU: one unit of scale is exactly one reference pixel.</summary>
        public static Sprite PixelSprite()
        {
            if (_pixel != null) return _pixel;
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "HealthBarPixel" };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            _pixel = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0f, 0.5f), PixelsPerUnit);
            _pixel.name = "HealthBarPixel";
            return _pixel;
        }

        public void Configure(HealthComponent health, Style style, float heightAbove = 1.35f, Color? fill = null)
        {
            Unsubscribe();
            _health = health;
            BarStyle = style;
            _widthPx = style == Style.Elite ? EliteWidthPx : NormalWidthPx;
            _heightAbove = heightAbove;
            Build(fill ?? (style == Style.Elite ? EliteFill : NormalFill));
            Subscribe();
            Refresh();
        }

        private void Build(Color fillColor)
        {
            if (_root == null)
            {
                _root = new GameObject("HealthBar").transform;
                _root.SetParent(transform, false);
                _border = Bar("Border", SortingConvention.BaseOrderOf(SortingRole.WorldUi));
                _back = Bar("Back", SortingConvention.BaseOrderOf(SortingRole.WorldUi) + 1);
                _fill = Bar("Fill", SortingConvention.BaseOrderOf(SortingRole.WorldUi) + 2);
            }

            var w = _widthPx;
            var half = w * 0.5f / PixelsPerUnit;
            Place(_border, -half, w, HeightPx, BorderColor);
            Place(_back, -half + BorderPx / PixelsPerUnit, w - BorderPx * 2, HeightPx - BorderPx * 2, BackColor);
            Place(_fill, -half + BorderPx / PixelsPerUnit, w - BorderPx * 2, HeightPx - BorderPx * 2, fillColor);
        }

        private SpriteRenderer Bar(string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = PixelSprite();
            renderer.sortingLayerName = SortingLayers.UIWorld;
            renderer.sortingOrder = order;
            return renderer;
        }

        private static void Place(SpriteRenderer renderer, float leftX, int widthPx, int heightPx, Color color)
        {
            renderer.transform.localPosition = new Vector3(leftX, 0f, 0f);
            renderer.transform.localScale = new Vector3(widthPx, heightPx, 1f);
            renderer.color = color;
        }

        private void Subscribe()
        {
            if (_health == null) return;
            _health.Damaged += OnHealthChanged;
            _health.Healed += OnHealthChanged;
            _health.Died += OnDied;
        }

        private void Unsubscribe()
        {
            if (_health == null) return;
            _health.Damaged -= OnHealthChanged;
            _health.Healed -= OnHealthChanged;
            _health.Died -= OnDied;
        }

        private void OnHealthChanged(int _) => Refresh();
        private void OnDied() => Refresh();
        private void OnDestroy() => Unsubscribe();

        /// <summary>Pure visibility rule (tests): shown only while damaged and alive.</summary>
        public static bool ShouldShow(int current, int max) => current > 0 && max > 0 && current < max;

        public void Refresh()
        {
            if (_root == null || _health == null) return;
            Refreshes++;
            var current = _health.CurrentHealth;
            var max = _health.MaxHealth;
            Fill01 = max > 0 ? Mathf.Clamp01(current / (float)max) : 0f;
            var show = ShouldShow(current, max);
            if (_root.gameObject.activeSelf != show) _root.gameObject.SetActive(show);
            if (!show) return;
            // The fill shrinks from the right in whole pixels so it reads as a bar, not a smear.
            var innerPx = _widthPx - BorderPx * 2;
            var fillPx = Mathf.Clamp(Mathf.CeilToInt(innerPx * Fill01), current > 0 ? 1 : 0, innerPx);
            _fill.transform.localScale = new Vector3(fillPx, HeightPx - BorderPx * 2, 1f);
        }

        private void LateUpdate()
        {
            if (_root == null) return;
            // World-aligned: the bar never inherits the body's rotation or flip, and lands on the pixel grid.
            _root.rotation = Quaternion.identity;
            var world = (Vector2)transform.position + Vector2.up * _heightAbove;
            var snapped = new Vector2(Mathf.Round(world.x * PixelsPerUnit) / PixelsPerUnit, Mathf.Round(world.y * PixelsPerUnit) / PixelsPerUnit);
            _root.position = new Vector3(snapped.x, snapped.y, transform.position.z);
        }
    }
}

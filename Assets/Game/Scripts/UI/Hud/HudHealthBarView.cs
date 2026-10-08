using System.Collections.Generic;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuinRail.UI.Hud
{
    /// <summary>
    /// A HUD health bar in the UI art language (spec 18: dark metal, chamfered corners, 1 px borders, warm accents), built
    /// from pixel-exact plates so nothing is resampled: a black outline with cut corners, a bevelled metal rim, a recessed
    /// trough and a four-tone lit fill with a bright leading edge. Feedback is presentation only, over the HP the HUD
    /// already reads: damage drops the fill at once (the current HP is always exact) and leaves a pale chip that holds,
    /// then drains; healing shows the gained span in green while the fill grows into it; at low HP the fill runs hot and
    /// breathes slowly. Optional notches mark fixed HP steps so a larger maximum reads as a longer bar of the same steps.
    /// </summary>
    public sealed class HudHealthBarView : MonoBehaviour
    {
        /// <summary>How long the damage chip holds before it drains (seconds, presentation only).</summary>
        public const float ChipHoldSeconds = 0.4f;
        /// <summary>Drain speed of the chip, in bar fractions per second.</summary>
        public const float ChipDrainPerSecond = 0.8f;
        /// <summary>How long the fill takes to grow into a heal.</summary>
        public const float HealGrowSeconds = 0.3f;
        public const float HitFlashSeconds = 0.12f;
        /// <summary>Low-HP styling at or below this share of effective max HP (the vignette's threshold).</summary>
        public const float LowThreshold = HudLowHealthVignetteView.DefaultThreshold;
        public const float LowPulseHz = 1.0f;

        private static readonly Color Outline = UiTheme.Hex("#0B0E0F");
        private static readonly Color RimLight = UiTheme.Hex("#55605F");
        private static readonly Color Rim = UiTheme.PanelEdge;
        private static readonly Color RimShadow = UiTheme.PanelEdgeSoft;
        private static readonly Color Trough = UiTheme.Hex("#1A1315");
        private static readonly Color TroughShadow = UiTheme.Hex("#0F0B0C");
        private static readonly Color Chip = UiTheme.Hex("#EBD3A8");
        private static readonly Color HealBand = UiTheme.Hex("#7FCB8A");
        private static readonly Color Edge = UiTheme.Hex("#FFD2C2");
        private static readonly Color Notch = new(0.04f, 0.03f, 0.03f, 0.55f);
        // The lit fill, top to bottom: highlight, light, body, shadow (spec 18 bar: a lit surface, never a flat block).
        private static readonly Color[] Ramp = { UiTheme.Hex("#F0907C"), UiTheme.Hex("#D9574A"), UiTheme.Hex("#B53A35"), UiTheme.Hex("#7C2326") };
        private static readonly Color[] HotRamp = { UiTheme.Hex("#FFB09A"), UiTheme.Hex("#F0604E"), UiTheme.Hex("#D6413A"), UiTheme.Hex("#93282A") };

        private readonly List<Image> _rows = new();
        private readonly List<Color> _rowBase = new();
        private readonly List<Color> _rowHot = new();
        private readonly List<Image> _notches = new();
        private Image _chip;
        private Image _heal;
        private Image _edge;
        private Image[] _outline;
        private int _innerWidth;
        private int _innerHeight;
        private int _notchHp;
        private bool _lowStyling;
        private bool _shown;
        private int _hp;
        private int _maxHp;
        private float _target;
        private float _fill;
        private float _chipFill;
        private float _healFrom;
        private float _healTimer;
        private float _chipHold;
        private float _flash;
        private float _phase;

        /// <summary>The fill drawn now (0..1); equals <see cref="Target01"/> except while growing into a heal.</summary>
        public float Fill01 => _fill;
        /// <summary>The exact current HP share.</summary>
        public float Target01 => _target;
        /// <summary>The damage chip's right end (0..1); at the fill when no damage is draining.</summary>
        public float Chip01 => _chipFill;
        public bool ChipVisible => _chip != null && _chip.enabled && _chipFill > _fill + 0.0001f;
        public bool HealVisible => _heal != null && _heal.enabled;
        public bool IsLow { get; private set; }
        public int NotchCount { get; private set; }
        public int Rows => _rows.Count;
        public RectTransform Rect => (RectTransform)transform;

        /// <param name="notchHp">HP per notch (0 = none).</param>
        /// <param name="lowStyling">Hot fill and slow breath at low HP (the player's own bar).</param>
        public static HudHealthBarView Create(Transform parent, UiRect bounds, int notchHp, bool lowStyling, string name)
        {
            var rect = UiBuild.NewRect(parent, name, bounds);
            var view = rect.gameObject.AddComponent<HudHealthBarView>();
            view.Build(bounds.Width, bounds.Height, notchHp, lowStyling);
            return view;
        }

        private void Build(int width, int height, int notchHp, bool lowStyling)
        {
            _notchHp = notchHp;
            _lowStyling = lowStyling;
            _outline = Frame(transform, width, height, out _);

            _innerWidth = width - 4;
            _innerHeight = height - 4;
            var inner = new UiRect(2, 2, _innerWidth, _innerHeight);
            UiBuild.Plate(transform, inner, Trough, "Trough");
            UiBuild.Plate(transform, new UiRect(2, 2, _innerWidth, 1), TroughShadow, "TroughShadow");

            _chip = UiBuild.Fillable(transform, inner, Chip, Image.FillMethod.Horizontal, (int)Image.OriginHorizontal.Left, "DamageChip");
            _heal = UiBuild.Fillable(transform, inner, HealBand, Image.FillMethod.Horizontal, (int)Image.OriginHorizontal.Left, "HealBand");
            for (var y = 0; y < _innerHeight; y++)
            {
                // Map each row onto the four-tone ramp: one highlight row, one light row, the body, two shadow rows.
                var tone = y == 0 ? 0 : y == 1 ? 1 : y >= _innerHeight - Mathf.Max(1, _innerHeight / 4) ? 3 : 2;
                var row = UiBuild.Fillable(transform, new UiRect(2, 2 + y, _innerWidth, 1), Ramp[tone], Image.FillMethod.Horizontal, (int)Image.OriginHorizontal.Left, "FillRow" + y);
                _rows.Add(row);
                _rowBase.Add(Ramp[tone]);
                _rowHot.Add(HotRamp[tone]);
            }

            _edge = UiBuild.Plate(transform, new UiRect(2, 3, 1, Mathf.Max(1, _innerHeight - 2)), Edge, "LeadingEdge");
        }

        /// <summary>
        /// The HUD frame (spec 18): a black outline with chamfered (cut) corners around a bevelled metal rim — light above,
        /// shadow below. Shared by every framed HUD element so they read as one family. Returns the four outline plates.
        /// </summary>
        public static Image[] Frame(Transform parent, int width, int height, out Image rimTop)
        {
            var outline = new[]
            {
                UiBuild.Plate(parent, new UiRect(1, 0, width - 2, 1), Outline, "OutlineTop"),
                UiBuild.Plate(parent, new UiRect(1, height - 1, width - 2, 1), Outline, "OutlineBottom"),
                UiBuild.Plate(parent, new UiRect(0, 1, 1, height - 2), Outline, "OutlineLeft"),
                UiBuild.Plate(parent, new UiRect(width - 1, 1, 1, height - 2), Outline, "OutlineRight")
            };
            rimTop = UiBuild.Plate(parent, new UiRect(1, 1, width - 2, 1), RimLight, "RimTop");
            UiBuild.Plate(parent, new UiRect(1, height - 2, width - 2, 1), RimShadow, "RimBottom");
            UiBuild.Plate(parent, new UiRect(1, 2, 1, height - 4), Rim, "RimLeft");
            UiBuild.Plate(parent, new UiRect(width - 2, 2, 1, height - 4), Rim, "RimRight");
            return outline;
        }

        public static Color RimLightColor => RimLight;
        public static Color OutlineColor => Outline;

        /// <summary>Shows the HP the HUD read; the first call (and any change of maximum) snaps without feedback.</summary>
        public void Show(int hp, int maxHp)
        {
            var target = maxHp > 0 ? Mathf.Clamp01(hp / (float)maxHp) : 0f;
            if (!_shown || maxHp != _maxHp)
            {
                _shown = true;
                _fill = _chipFill = _target = target;
                _healTimer = 0f;
                _chipHold = 0f;
                if (maxHp != _maxHp) LayNotches(maxHp);
            }
            else if (hp < _hp)
            {
                // Damage: the fill drops at once, the chip keeps the lost span and holds before draining.
                _chipFill = Mathf.Max(_chipFill, _fill);
                _fill = target;
                _healTimer = 0f;
                _chipHold = ChipHoldSeconds;
                _flash = HitFlashSeconds;
            }
            else if (hp > _hp)
            {
                // Healing: the gained span shows in green at once and the fill grows into it.
                _healFrom = _fill;
                _healTimer = HealGrowSeconds;
                _chipFill = Mathf.Max(_chipFill, target);
            }

            _hp = hp;
            _maxHp = maxHp;
            _target = target;
            IsLow = _lowStyling && maxHp > 0 && hp > 0 && target <= LowThreshold;
            Draw();
        }

        private void LayNotches(int maxHp)
        {
            var count = _notchHp > 0 && maxHp > _notchHp ? Mathf.Min((maxHp - 1) / _notchHp, _innerWidth / 4) : 0;
            while (_notches.Count < count) _notches.Add(UiBuild.Plate(transform, new UiRect(0, 3, 1, Mathf.Max(1, _innerHeight - 2)), Notch, "Notch" + _notches.Count));
            for (var i = 0; i < _notches.Count; i++)
            {
                var visible = i < count;
                _notches[i].enabled = visible;
                if (!visible) continue;
                var x = 2 + Mathf.RoundToInt((i + 1) * _notchHp / (float)maxHp * _innerWidth);
                _notches[i].rectTransform.anchoredPosition = new Vector2(x, -3f);
                _notches[i].transform.SetAsLastSibling();
            }

            _edge.transform.SetAsLastSibling();
            NotchCount = count;
        }

        public void Tick(float deltaTime)
        {
            if (!_shown) return;
            if (_healTimer > 0f)
            {
                _healTimer = Mathf.Max(0f, _healTimer - deltaTime);
                var t = 1f - _healTimer / HealGrowSeconds;
                _fill = Mathf.Lerp(_healFrom, _target, 1f - (1f - t) * (1f - t));
                if (_healTimer <= 0f) _fill = _target;
            }

            if (_chipHold > 0f) _chipHold -= deltaTime;
            else if (_chipFill > _fill) _chipFill = Mathf.Max(_fill, _chipFill - ChipDrainPerSecond * deltaTime);
            if (_flash > 0f) _flash = Mathf.Max(0f, _flash - deltaTime);
            _phase = IsLow ? (_phase + deltaTime * LowPulseHz) % 1f : 0f;
            Draw();
        }

        private void Update() => Tick(Time.unscaledDeltaTime);

        private void Draw()
        {
            var breath = IsLow ? 0.5f + 0.5f * Mathf.Sin(_phase * Mathf.PI * 2f) : 0f;
            var flash = _flash / HitFlashSeconds;
            for (var i = 0; i < _rows.Count; i++)
            {
                var color = IsLow ? Color.Lerp(_rowBase[i], _rowHot[i], 0.55f + 0.45f * breath) : _rowBase[i];
                _rows[i].color = Color.Lerp(color, Edge, 0.55f * flash);
                _rows[i].fillAmount = _fill;
            }

            _chip.fillAmount = _chipFill;
            _chip.enabled = _chipFill > _fill + 0.0001f;
            _heal.fillAmount = _target;
            _heal.enabled = _healTimer > 0f && _target > _fill + 0.0001f;
            var edgeX = Mathf.RoundToInt(_fill * _innerWidth);
            _edge.enabled = edgeX > 0 && edgeX < _innerWidth;
            _edge.rectTransform.anchoredPosition = new Vector2(2 + edgeX - 1, -3f);
            var outline = IsLow ? Color.Lerp(Outline, UiTheme.Danger, 0.35f + 0.35f * breath) : Outline;
            foreach (var o in _outline) o.color = outline;
        }
    }
}

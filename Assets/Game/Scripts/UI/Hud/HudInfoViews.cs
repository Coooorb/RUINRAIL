using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuinRail.UI.Hud
{
    /// <summary>
    /// The top-right Carried Coins readout (91 Top Information) as the coin token plus the number on a restrained
    /// plate. The word "COINS" is gone: the token says what the number is.
    /// </summary>
    public sealed class HudCoinView : MonoBehaviour
    {
        public const int Width = 62;
        public const int Height = 16;
        public const int IconSize = 12;

        private Image _icon;
        private Text _amount;

        public bool HasIconSprite => _icon != null && _icon.sprite != null;
        public bool IconVisible => _icon != null && _icon.enabled;
        public Sprite IconSprite => _icon != null ? _icon.sprite : null;
        public string AmountText => _amount != null ? _amount.text : string.Empty;
        public RectTransform Rect => (RectTransform)transform;

        public static HudCoinView Create(Transform parent, UiRect bounds, Sprite coinSprite, string name = "Coins")
        {
            var rect = UiBuild.NewRect(parent, name, bounds);
            var view = rect.gameObject.AddComponent<HudCoinView>();
            view.Build(bounds.Width, bounds.Height, coinSprite);
            return view;
        }

        private void Build(int width, int height, Sprite coinSprite)
        {
            var inner = new UiRect(0, 0, width, height);
            UiBuild.Plate(transform, inner, UiTheme.WithAlpha(UiTheme.NearBlack, 0.72f), "Plate");
            UiBuild.Border(transform, inner, UiTheme.PanelEdgeSoft);
            var iconRect = UiBuild.NewRect(transform, "Icon", new UiRect(3, (height - IconSize) / 2, IconSize, IconSize));
            _icon = iconRect.gameObject.AddComponent<Image>();
            _icon.raycastTarget = false;
            _icon.preserveAspect = true;
            _icon.sprite = coinSprite;
            if (coinSprite == null) _icon.color = UiTheme.Amber; // an unbound token still reads as the coin slot
            var textX = 3 + IconSize + 3;
            _amount = UiBuild.Label(transform, "0", new UiRect(textX, (height - UiText.LineHeight) / 2, width - textX - 4, UiText.LineHeight), 1, TextAnchor.UpperRight, UiTheme.Amber, false, "CoinAmount");
        }

        /// <summary>Renders the current Carried Coins; the caller publishes on every wallet change.</summary>
        public void Show(int coins) => _amount.text = coins.ToString();
    }

    /// <summary>
    /// The enemy-remaining chip (91): a hostile token plus "xN" on the same restrained plate as the coin readout, top-
    /// right under it. It is a count, not a sentence, and it exists on screen only while the view model says an active
    /// standard combat encounter still has enemies — the view never decides eligibility itself.
    /// </summary>
    public sealed class HudEnemyCountView : MonoBehaviour
    {
        public const int Width = HudCoinView.Width;
        public const int Height = HudCoinView.Height;
        public const int IconSize = 12;

        private Image _icon;
        private Text _count;
        private GameObject _body;

        public bool HasIconSprite => _icon != null && _icon.sprite != null;
        public bool IconVisible => _icon != null && _icon.enabled && IsVisible;
        public Sprite IconSprite => _icon != null ? _icon.sprite : null;
        public string CountText => _count != null ? _count.text : string.Empty;
        public bool IsVisible => _body != null && _body.activeSelf;
        public int Shown { get; private set; }
        public RectTransform Rect => (RectTransform)transform;

        public static HudEnemyCountView Create(Transform parent, UiRect bounds, Sprite enemySprite, string name = "Enemies")
        {
            var rect = UiBuild.NewRect(parent, name, bounds);
            var view = rect.gameObject.AddComponent<HudEnemyCountView>();
            view.Build(bounds.Width, bounds.Height, enemySprite);
            return view;
        }

        private void Build(int width, int height, Sprite enemySprite)
        {
            var inner = new UiRect(0, 0, width, height);
            _body = UiBuild.NewRect(transform, "Body", inner).gameObject;
            UiBuild.Plate(_body.transform, inner, UiTheme.WithAlpha(UiTheme.NearBlack, 0.72f), "Plate");
            UiBuild.Border(_body.transform, inner, UiTheme.PanelEdgeSoft);
            var iconRect = UiBuild.NewRect(_body.transform, "Icon", new UiRect(3, (height - IconSize) / 2, IconSize, IconSize));
            _icon = iconRect.gameObject.AddComponent<Image>();
            _icon.raycastTarget = false;
            _icon.preserveAspect = true;
            _icon.sprite = enemySprite;
            if (enemySprite == null) _icon.color = UiTheme.Danger; // an unbound token still reads as the hostile slot
            var textX = 3 + IconSize + 3;
            _count = UiBuild.Label(_body.transform, string.Empty, new UiRect(textX, (height - UiText.LineHeight) / 2, width - textX - 4, UiText.LineHeight), 1, TextAnchor.UpperRight, UiTheme.Danger, false, "EnemyCount");
        }

        /// <summary>Shows "xN" while visible; hides the whole chip otherwise (a hidden chip has no text and no plate).</summary>
        public void Show(bool visible, int remaining)
        {
            if (_body == null) return;
            if (_body.activeSelf != visible) _body.SetActive(visible);
            _count.text = visible ? $"x{Mathf.Max(0, remaining)}" : string.Empty;
            if (visible) Shown++;
        }
    }

    /// <summary>
    /// The room-title reveal (ui/91): the name of the room the player has just entered, briefly, top-centre under the
    /// HUD band. Fade in, hold, fade out — about two seconds in total, one line of name plus an optional role line
    /// for special rooms. It never blocks the play area and never repeats while the player stays in the room; the
    /// composition root decides when a reveal happens, from the one authoritative room-entry event.
    /// </summary>
    public sealed class HudRoomTitleView : MonoBehaviour
    {
        public const int Width = 300;
        public const int Height = 20;
        /// <summary>Tunable reveal envelope (seconds); total stays inside the approved 1.5–2.5 s window.</summary>
        public const float FadeInSeconds = 0.25f;
        public const float HoldSeconds = 1.35f;
        public const float FadeOutSeconds = 0.45f;
        public const float TotalSeconds = FadeInSeconds + HoldSeconds + FadeOutSeconds;

        private Text _name;
        private Text _role;
        private Image _plate;
        private float _elapsed = float.MaxValue;

        public string NameText => _name != null ? _name.text : string.Empty;
        public string RoleText => _role != null ? _role.text : string.Empty;
        public bool IsShowing => _elapsed < TotalSeconds;
        public float Alpha { get; private set; }
        public int Reveals { get; private set; }
        public RectTransform Rect => (RectTransform)transform;

        public static HudRoomTitleView Create(Transform parent, UiRect bounds, string name = "RoomTitle")
        {
            var rect = UiBuild.NewRect(parent, name, bounds);
            var view = rect.gameObject.AddComponent<HudRoomTitleView>();
            view.Build(bounds.Width);
            return view;
        }

        private void Build(int width)
        {
            // A plate exactly as wide as the announced name, so the reveal reads over a bright floor without ever
            // becoming a permanent bar across the top of the screen (it fades with the text).
            _plate = UiBuild.Plate(transform, new UiRect(0, 0, width, Height), UiTheme.WithAlpha(UiTheme.NearBlack, 0f), "RoomTitlePlate");
            _name = UiBuild.Label(transform, string.Empty, new UiRect(0, 0, width, UiText.LineHeight), 1, TextAnchor.UpperCenter, UiTheme.Amber, false, "RoomName");
            _role = UiBuild.Label(transform, string.Empty, new UiRect(0, UiText.LineHeight + 1, width, UiText.LineHeight), 1, TextAnchor.UpperCenter, UiTheme.InkMuted, false, "RoomRole");
            Apply(0f);
        }

        /// <summary>Starts one reveal. Calling it again restarts the envelope with the new room.</summary>
        public void Reveal(string roomName, string role)
        {
            _name.text = UiText.Fit((roomName ?? string.Empty).ToUpperInvariant(), Width);
            _role.text = UiText.Fit((role ?? string.Empty).ToUpperInvariant(), Width);
            var lines = _role.text.Length > 0 ? 2 : 1;
            var plateWidth = Mathf.Min(Width, Mathf.Max(UiText.Width(_name.text), UiText.Width(_role.text)) + 10);
            var plateRect = (RectTransform)_plate.transform;
            plateRect.sizeDelta = new Vector2(plateWidth, lines * (UiText.LineHeight + 1) + 2);
            plateRect.anchoredPosition = new Vector2(Mathf.Round((Width - plateWidth) / 2f), 1f);
            _elapsed = 0f;
            Reveals++;
            Apply(0f);
        }

        /// <summary>Clears the reveal immediately (scene transition, a new depth, an overlay taking the screen).</summary>
        public void Clear()
        {
            _elapsed = float.MaxValue;
            Apply(0f);
        }

        private void Update()
        {
            if (!IsShowing)
            {
                if (Alpha != 0f) Apply(0f);
                return;
            }

            // Unscaled: the reveal plays out at the same speed whether or not an overlay paused the world.
            _elapsed += Time.unscaledDeltaTime;
            Apply(EnvelopeAt(_elapsed));
        }

        /// <summary>The reveal's opacity at a moment in its envelope. Pure, so the timing is tested directly.</summary>
        public static float EnvelopeAt(float seconds)
        {
            if (seconds < 0f || seconds >= TotalSeconds) return 0f;
            if (seconds < FadeInSeconds) return Mathf.Clamp01(seconds / FadeInSeconds);
            if (seconds < FadeInSeconds + HoldSeconds) return 1f;
            return Mathf.Clamp01(1f - (seconds - FadeInSeconds - HoldSeconds) / FadeOutSeconds);
        }

        private void Apply(float alpha)
        {
            Alpha = alpha;
            _name.color = UiTheme.WithAlpha(UiTheme.Amber, alpha);
            _role.color = UiTheme.WithAlpha(UiTheme.InkMuted, alpha * 0.9f);
            if (_plate != null) _plate.color = UiTheme.WithAlpha(UiTheme.NearBlack, alpha * 0.62f);
        }
    }

    /// <summary>
    /// The event notice (ui/91): one line for what an interaction just did — the reward that landed, the repair that
    /// failed, the signal that started, the reason a press was refused — shown briefly under the room-title band, or
    /// held (a running Supply Signal's countdown) until cleared. It is presentation only: the composition root feeds
    /// it from the event results, and nothing waits for it.
    /// </summary>
    public sealed class HudNoticeView : MonoBehaviour
    {
        public const int Width = 400;
        public const int Height = UiText.LineHeight + 4;
        public const float DefaultSeconds = 3.2f;
        public const float FadeSeconds = 0.35f;

        private Text _text;
        private Image _plate;
        private float _remaining;
        private bool _held;
        private Color _color = UiTheme.Amber;

        public string Text => _text != null ? _text.text : string.Empty;
        public bool IsShowing => _held || _remaining > 0f;
        public bool IsHeld => _held;
        public float Alpha { get; private set; }
        public int Notices { get; private set; }
        public RectTransform Rect => (RectTransform)transform;

        public static HudNoticeView Create(Transform parent, UiRect bounds, string name = "EventNotice")
        {
            var rect = UiBuild.NewRect(parent, name, bounds);
            var view = rect.gameObject.AddComponent<HudNoticeView>();
            view.Build(bounds.Width);
            return view;
        }

        private void Build(int width)
        {
            _plate = UiBuild.Plate(transform, new UiRect(0, 0, width, Height), UiTheme.WithAlpha(UiTheme.NearBlack, 0f), "NoticePlate");
            _text = UiBuild.Label(transform, string.Empty, new UiRect(0, 2, width, UiText.LineHeight), 1, TextAnchor.UpperCenter, UiTheme.Amber, false, "NoticeText");
            Apply(0f);
        }

        /// <summary>Shows a line for a few seconds (a new notice replaces the current one).</summary>
        public void Show(string text, bool isProblem = false, float seconds = DefaultSeconds)
        {
            if (string.IsNullOrEmpty(text)) return;
            _held = false;
            _color = isProblem ? UiTheme.Danger : UiTheme.Amber;
            SetText(text);
            _remaining = Mathf.Max(0.1f, seconds);
            Notices++;
            Apply(1f);
        }

        /// <summary>Holds a line until <see cref="Clear"/> (a countdown the caller updates by calling Hold again).</summary>
        public void Hold(string text)
        {
            if (string.IsNullOrEmpty(text)) { Clear(); return; }
            _held = true;
            _color = UiTheme.Amber;
            SetText(text);
            Apply(1f);
        }

        public void Clear()
        {
            _held = false;
            _remaining = 0f;
            Apply(0f);
        }

        private void SetText(string text)
        {
            _text.text = UiText.Fit(text.ToUpperInvariant(), Width);
            var plateWidth = Mathf.Min(Width, UiText.Width(_text.text) + 10);
            var plateRect = (RectTransform)_plate.transform;
            plateRect.sizeDelta = new Vector2(plateWidth, Height);
            plateRect.anchoredPosition = new Vector2(Mathf.Round((Width - plateWidth) / 2f), 0f);
        }

        private void Update()
        {
            if (_held) { if (Alpha < 1f) Apply(1f); return; }
            if (_remaining <= 0f) { if (Alpha != 0f) Apply(0f); return; }
            _remaining -= Time.unscaledDeltaTime;
            Apply(_remaining <= 0f ? 0f : Mathf.Clamp01(_remaining / FadeSeconds));
        }

        private void Apply(float alpha)
        {
            Alpha = alpha;
            _text.color = UiTheme.WithAlpha(_color, alpha);
            if (_plate != null) _plate.color = UiTheme.WithAlpha(UiTheme.NearBlack, alpha * 0.7f);
        }
    }

    /// <summary>
    /// The low-health danger vignette (ui/91 Player State): a red frame around the screen edge that appears at or
    /// below a share of the player's <em>effective</em> maximum HP and pulses gently while it lasts. The play area
    /// stays clear — the art is a falloff that is transparent in the middle — and the whole thing lives at the bottom
    /// of the HUD canvas, so the inventory, pause and merchant windows (their own canvases, higher sorting order)
    /// always render over it.
    ///
    /// Every value below is tunable; <see cref="Configure"/> exists so a future UX pass can move them without
    /// touching the view.
    /// </summary>
    public sealed class HudLowHealthVignetteView : MonoBehaviour
    {
        /// <summary>Activates at or below this share of effective max HP.</summary>
        public const float DefaultThreshold = 0.30f;
        /// <summary>Pulse rate in Hz — a slow breath, never a rapid flash.</summary>
        public const float DefaultPulseHz = 1.0f;
        /// <summary>Opacity band at the threshold, and at the edge of death.</summary>
        public const float DefaultMinAlpha = 0.18f;
        public const float DefaultMaxAlpha = 0.34f;
        public const float DefaultCriticalMinAlpha = 0.34f;
        public const float DefaultCriticalMaxAlpha = 0.58f;
        /// <summary>How quickly the frame fades in when crossing the threshold and out when healed past it.</summary>
        public const float FadeSeconds = 0.25f;

        private Image _image;
        private float _threshold = DefaultThreshold;
        private float _pulseHz = DefaultPulseHz;
        private float _phase;
        private float _presence;
        private float _intensity;
        private bool _active;

        public bool HasSprite => _image != null && _image.sprite != null;
        /// <summary>True while the player is at or below the threshold (the frame is on or fading in).</summary>
        public bool IsActive => _active;
        public bool IsVisible => _image != null && _image.enabled && _image.color.a > 0.001f;
        public float Alpha => _image != null ? _image.color.a : 0f;
        public float Threshold => _threshold;
        public float PulseHz => _pulseHz;
        /// <summary>0 at the threshold, 1 at zero HP: how far into the danger band the player is.</summary>
        public float Intensity => _intensity;
        public RectTransform Rect => (RectTransform)transform;

        public static HudLowHealthVignetteView Create(Transform parent, Sprite sprite, string name = "LowHealthVignette")
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var view = go.AddComponent<HudLowHealthVignetteView>();
            view.Build(sprite);
            rect.SetAsFirstSibling(); // under every HUD element; overlay windows are separate canvases above it
            return view;
        }

        private void Build(Sprite sprite)
        {
            _image = gameObject.AddComponent<Image>();
            _image.sprite = sprite;
            _image.type = Image.Type.Simple;
            _image.raycastTarget = false; // the HUD is informational and never eats a gameplay click
            _image.color = UiTheme.WithAlpha(UiTheme.Danger, 0f);
            _image.enabled = sprite != null;
        }

        /// <summary>Overrides the tuning; any argument at or below zero keeps the current value.</summary>
        public void Configure(float threshold, float pulseHz)
        {
            if (threshold > 0f) _threshold = Mathf.Clamp01(threshold);
            if (pulseHz > 0f) _pulseHz = pulseHz;
        }

        /// <summary>
        /// Renders the danger state from authoritative health. <paramref name="effectiveMaxHp"/> is the effective
        /// maximum (base plus equipment), never the base value, so armor that raises max HP also raises the point at
        /// which the frame appears.
        /// </summary>
        public void Show(int currentHp, int effectiveMaxHp, bool suppressed = false)
        {
            var fraction = effectiveMaxHp > 0 ? Mathf.Clamp01(currentHp / (float)effectiveMaxHp) : 1f;
            _active = !suppressed && effectiveMaxHp > 0 && currentHp > 0 && fraction <= _threshold;
            _intensity = _active && _threshold > 0f ? Mathf.Clamp01(1f - fraction / _threshold) : 0f;
            if (!_active && _presence <= 0f) ApplyAlpha(0f);
        }

        /// <summary>Immediately off, with no lingering fade: scene change, death, revive, run end.</summary>
        public void Reset()
        {
            _active = false;
            _presence = 0f;
            _intensity = 0f;
            _phase = 0f;
            ApplyAlpha(0f);
        }

        private void Update()
        {
            var step = Time.unscaledDeltaTime / Mathf.Max(0.0001f, FadeSeconds);
            _presence = Mathf.Clamp01(_presence + (_active ? step : -step));
            if (_presence <= 0f)
            {
                if (Alpha != 0f) ApplyAlpha(0f);
                return;
            }

            _phase += Time.unscaledDeltaTime * _pulseHz;
            // A gentle breath between the band's ends; the band itself widens as the player nears death.
            var wave = 0.5f - 0.5f * Mathf.Cos(_phase * 2f * Mathf.PI);
            var low = Mathf.Lerp(DefaultMinAlpha, DefaultCriticalMinAlpha, _intensity);
            var high = Mathf.Lerp(DefaultMaxAlpha, DefaultCriticalMaxAlpha, _intensity);
            ApplyAlpha(Mathf.Lerp(low, high, wave) * _presence);
        }

        private void ApplyAlpha(float alpha)
        {
            if (_image == null) return;
            _image.color = UiTheme.WithAlpha(UiTheme.Danger, alpha);
        }
    }

    /// <summary>
    /// One timed-effect chip: the consumable's own icon over a vertical "time left" fill, with a thin frame that says
    /// whether the effect is something the player wanted or something done to them.
    ///
    /// A chip is a square the size of the HUD's small icon slots and carries no text of its own, because up to three
    /// of them sit over the consumable slot and three labels there would be clutter. The seconds and the effect line
    /// appear on the details row under the strip when the strip is pointed at or focused, which is where a player who
    /// wants the exact number looks.
    /// </summary>
    public sealed class HudStatusChipView : MonoBehaviour
    {
        public const int Size = 16;
        public const int IconSize = 12;

        private Image _plate;
        private Image _fill;
        private Image _icon;
        private readonly System.Collections.Generic.List<Image> _frame = new();

        public RectTransform Rect => (RectTransform)transform;
        public bool IsVisible => gameObject.activeSelf;
        public Sprite IconSprite => _icon != null ? _icon.sprite : null;
        public bool IconVisible => _icon != null && _icon.enabled;
        /// <summary>Fraction of the effect's duration still to run (the chip's vertical fill).</summary>
        public float Fill => _fill != null ? _fill.fillAmount : 0f;
        public string DefinitionId { get; private set; } = string.Empty;

        public static HudStatusChipView Create(Transform parent, UiRect bounds, string name)
        {
            var rect = UiBuild.NewRect(parent, name, bounds);
            var view = rect.gameObject.AddComponent<HudStatusChipView>();
            view.Build(bounds.Width, bounds.Height);
            return view;
        }

        private void Build(int width, int height)
        {
            var inner = new UiRect(0, 0, width, height);
            _plate = UiBuild.Plate(transform, inner, UiTheme.WithAlpha(UiTheme.NearBlack, 0.78f), "Plate");
            // The remaining-time wipe sits under the icon so the icon is never obscured by it.
            _fill = UiBuild.Fillable(transform, inner, UiTheme.WithAlpha(UiTheme.Terminal, 0.35f),
                Image.FillMethod.Vertical, (int)Image.OriginVertical.Bottom, "Fill");
            foreach (var edge in UiBuild.Border(transform, inner, UiTheme.Terminal)) _frame.Add(edge);
            var iconRect = UiBuild.NewRect(transform, "Icon", new UiRect((width - IconSize) / 2, (height - IconSize) / 2, IconSize, IconSize));
            _icon = iconRect.gameObject.AddComponent<Image>();
            _icon.raycastTarget = false;
            _icon.preserveAspect = true;
            _icon.enabled = false;
            gameObject.SetActive(false);
        }

        /// <summary>Renders one effect, or hides the chip when there is none. The caller owns which effects exist.</summary>
        public void Show(HudStatusEffect effect)
        {
            gameObject.SetActive(effect != null);
            if (effect == null) { DefinitionId = string.Empty; return; }
            DefinitionId = effect.DefinitionId;
            var accent = effect.IsPositive ? UiTheme.Terminal : UiTheme.Danger;
            _fill.color = UiTheme.WithAlpha(accent, 0.35f);
            _fill.fillAmount = effect.Remaining01;
            foreach (var edge in _frame) if (edge != null) edge.color = accent;
            _icon.enabled = effect.Icon != null;
            _icon.sprite = effect.Icon;
            // No icon on the definition is still a readable chip: the accent fill and frame carry the state.
            if (effect.Icon == null) _plate.color = UiTheme.WithAlpha(accent, 0.22f);
            else _plate.color = UiTheme.WithAlpha(UiTheme.NearBlack, 0.78f);
        }
    }
}

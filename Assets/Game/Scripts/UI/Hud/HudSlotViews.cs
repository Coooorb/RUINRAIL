using System.Collections.Generic;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuinRail.UI.Hud
{
    public enum HudDashState
    {
        Ready,
        Cooldown,
        Disabled
    }

    /// <summary>
    /// The bottom-left dash indicator (91 Player State) as a graphical icon slot: the generated dash icon on a slot
    /// plate, a vertical cooldown wipe that recedes as the authoritative cooldown recharges, and a dimmed/struck
    /// state while the dash cannot be used at all. There is no permanent text.
    ///
    /// The overlay is a genuine <see cref="Image.Type.Filled"/> mask over the icon: grey coverage stands at the
    /// bottom of the slot and drains downward, so the icon is revealed from the top as the dash recharges and is
    /// completely clear the instant it is ready. A thin bright line marks the moving boundary, and completing the
    /// cooldown flashes the ready brackets once rather than pulsing while the dash is available.
    ///
    /// The previous implementation gave the overlay no sprite. uGUI ignores <see cref="Image.type"/> when an image
    /// has none and draws a plain quad, so the fill amount was discarded and the whole slot sat under a flat grey
    /// block for the entire cooldown: the "static grey" the player saw. <see cref="UiBuild.Fillable"/> is the fix.
    /// </summary>
    public sealed class HudDashIconView : MonoBehaviour
    {
        public const int Size = 24;
        public const int IconSize = 16;
        /// <summary>How long the one-shot ready flash lasts, in seconds (tunable presentation value).</summary>
        public const float ReadyFlashSeconds = 0.35f;

        private Image _plate;
        private Image _icon;
        private Image _cooldown;
        private Image _boundary;
        private Image _disabledMark;
        private readonly List<Image> _readyBrackets = new();
        private int _coverHeight;
        private int _coverTop;
        private float _readyFlash;
        private bool _wasCoolingDown;

        public HudDashState State { get; private set; } = HudDashState.Disabled;
        /// <summary>Remaining cooldown fraction shown by the overlay (1 = just used, 0 = ready).</summary>
        public float Cooldown01 => _cooldown != null ? _cooldown.fillAmount : 0f;
        /// <summary>The wipe is a vertical fill: the covered band is the bottom <see cref="Cooldown01"/> of the slot.</summary>
        public bool CoverIsVertical => _cooldown != null && _cooldown.type == Image.Type.Filled && _cooldown.fillMethod == Image.FillMethod.Vertical;
        /// <summary>How many pixels of the icon slot the overlay currently covers (0 when the dash is ready).</summary>
        public float CoveredPixels => _cooldown != null && _cooldown.enabled ? _cooldown.fillAmount * _coverHeight : 0f;
        /// <summary>True while the one-shot amber flash that marks "ready again" is running.</summary>
        public bool ReadyFlashActive => _readyFlash > 0f;
        public bool HasIconSprite => _icon != null && _icon.sprite != null;
        public bool IconVisible => _icon != null && _icon.enabled;
        public RectTransform Rect => (RectTransform)transform;

        public static HudDashIconView Create(Transform parent, UiRect bounds, Sprite slotSprite, Sprite iconSprite, string name = "DashIcon")
        {
            var rect = UiBuild.NewRect(parent, name, bounds);
            var view = rect.gameObject.AddComponent<HudDashIconView>();
            view.Build(bounds.Width, bounds.Height, slotSprite, iconSprite);
            view.Show(false, 0f, true);
            return view;
        }

        private void Build(int width, int height, Sprite slotSprite, Sprite iconSprite)
        {
            var inner = new UiRect(0, 0, width, height);
            _plate = UiBuild.Sliced(transform, inner, slotSprite, null, "Plate");
            var iconRect = UiBuild.NewRect(transform, "Icon", new UiRect((width - IconSize) / 2, (height - IconSize) / 2, IconSize, IconSize));
            _icon = iconRect.gameObject.AddComponent<Image>();
            _icon.raycastTarget = false;
            _icon.preserveAspect = true;
            if (iconSprite != null)
            {
                _icon.sprite = iconSprite;
            }
            else
            {
                // No generated icon bound: a plain amber chevron plate keeps the slot readable rather than blank.
                _icon.color = UiTheme.Amber;
            }

            // The cooldown wipe: a semi-opaque grey band over the icon, filled from the bottom, that drains away as
            // the cooldown recharges. Semi-opaque on purpose — the dash icon stays identifiable underneath it.
            var cover = inner.Inset(2);
            _coverHeight = cover.Height;
            _coverTop = cover.Y;
            _cooldown = UiBuild.Fillable(transform, cover, UiTheme.WithAlpha(UiTheme.Hex("#5A6468"), 0.66f), Image.FillMethod.Vertical, (int)Image.OriginVertical.Bottom, "CooldownWipe");
            _cooldown.enabled = false;

            // A one-pixel bright edge riding the top of the grey band makes the movement readable at 640x360.
            _boundary = UiBuild.Plate(transform, new UiRect(cover.X, cover.Y, cover.Width, 1), UiTheme.Lighten(UiTheme.Amber, 0.25f), "CooldownEdge");
            _boundary.enabled = false;

            // Disabled: a rust strike across the slot (shape, not only colour).
            _disabledMark = UiBuild.Plate(transform, new UiRect(3, height / 2 - 1, width - 6, 2), UiTheme.Rust, "DisabledMark");
            _disabledMark.enabled = false;

            _readyBrackets.AddRange(UiBuild.Brackets(transform, inner, UiTheme.Amber));
            foreach (var bracket in _readyBrackets) bracket.enabled = false;
        }

        /// <summary>Renders the authoritative dash state: ready, recharging (<paramref name="cooldown01"/> remaining) or disabled.</summary>
        public void Show(bool ready, float cooldown01, bool disabled)
        {
            State = disabled ? HudDashState.Disabled : ready ? HudDashState.Ready : HudDashState.Cooldown;
            var iconTint = HasIconSprite ? Color.white : UiTheme.Amber;
            var remaining = Mathf.Clamp01(cooldown01);
            switch (State)
            {
                case HudDashState.Ready:
                    _plate.color = HasPlateSprite ? Color.white : UiTheme.Charcoal;
                    _icon.color = iconTint;
                    SetCover(0f, false);
                    _disabledMark.enabled = false;
                    // One short amber flash when the cooldown completes; never a repeating pulse while ready.
                    if (_wasCoolingDown) _readyFlash = ReadyFlashSeconds;
                    _wasCoolingDown = false;
                    break;
                case HudDashState.Cooldown:
                    _plate.color = HasPlateSprite ? new Color(0.8f, 0.8f, 0.8f, 1f) : UiTheme.Charcoal;
                    // The icon keeps most of its brightness: the wipe, not a dimming, is what reads as "recharging".
                    _icon.color = UiTheme.WithAlpha(iconTint, 0.85f);
                    SetCover(remaining, true);
                    _disabledMark.enabled = false;
                    _wasCoolingDown = true;
                    _readyFlash = 0f;
                    break;
                default:
                    _plate.color = HasPlateSprite ? new Color(0.55f, 0.55f, 0.55f, 1f) : UiTheme.Darken(UiTheme.Charcoal, 0.4f);
                    _icon.color = UiTheme.WithAlpha(UiTheme.InkDisabled, 0.9f);
                    SetCover(0f, false);
                    _disabledMark.enabled = true;
                    _wasCoolingDown = false;
                    _readyFlash = 0f;
                    break;
            }

            var showBrackets = State == HudDashState.Ready;
            var bracketColor = _readyFlash > 0f ? UiTheme.Lighten(UiTheme.Amber, 0.55f) : UiTheme.Amber;
            foreach (var bracket in _readyBrackets)
            {
                bracket.enabled = showBrackets;
                bracket.color = bracketColor;
            }
        }

        /// <summary>Places the grey band and its bright boundary line for a remaining-cooldown fraction.</summary>
        private void SetCover(float remaining, bool visible)
        {
            _cooldown.fillAmount = remaining;
            _cooldown.enabled = visible && remaining > 0f;
            var covered = Mathf.RoundToInt(remaining * _coverHeight);
            _boundary.enabled = _cooldown.enabled && covered > 0 && covered < _coverHeight;
            if (_boundary.enabled)
            {
                var rect = (RectTransform)_boundary.transform;
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -(_coverTop + _coverHeight - covered));
            }
        }

        private void Update()
        {
            if (_readyFlash <= 0f) return;
            _readyFlash = Mathf.Max(0f, _readyFlash - Time.unscaledDeltaTime);
            var color = _readyFlash > 0f ? UiTheme.Lighten(UiTheme.Amber, 0.55f) : UiTheme.Amber;
            foreach (var bracket in _readyBrackets) bracket.color = color;
        }

        private bool HasPlateSprite => _plate != null && _plate.sprite != null;
    }

    /// <summary>
    /// One bottom-centre weapon card (91 Weapons), framed like the HP bar (spec 18: black chamfered outline, bevelled metal
    /// rim): a recessed icon well with the item's rarity frame, its definition icon on a 1 px drop shadow and the slot
    /// number chip; beside it the name, the resource readout ("12 / 124", "NO AMMO", heat, draw — only for weapons that
    /// have one), a gauge (magazine rounds as pips, heat, draw, or the reload sweep) and the status line (the held
    /// Legendary's special, else RELOADING). The held weapon is lit — amber rim, brackets, full-bright icon and ink; a
    /// holstered one is dimmed; an empty slot is a faint card; a weapon with no ammo left shows red. Switching in, a
    /// finished reload and a special coming ready each get one short flash. A melee weapon shows its icon and name only.
    /// </summary>
    public sealed class HudWeaponSlotView : MonoBehaviour
    {
        public const int SlotSize = 40;
        public const int IconSize = 32;
        public const int TextWidth = 80;
        public const int Gap = 4;
        public const int Width = SlotSize + Gap + TextWidth;
        /// <summary>Above this magazine size the round gauge is a continuous bar instead of one pip per round.</summary>
        public const int MaxPips = 25;
        public const float SwitchFlashSeconds = 0.25f;
        public const float ReloadFlashSeconds = 0.18f;
        public const float SpecialFlashSeconds = 0.35f;
        private const int Well = 36;
        private const int GaugeWidth = TextWidth - 4;
        private const int GaugeHeight = 3;

        private static readonly Color CardFill = UiTheme.WithAlpha(UiTheme.Hex("#161B1D"), 0.97f);
        private static readonly Color CardFillActive = UiTheme.WithAlpha(UiTheme.Hex("#1E2528"), 0.98f);
        // Rarity backlight behind the icon (common → legendary), so a small icon reads in its well.
        private static readonly Color[] RarityGlow = { UiTheme.Hex("#8E9894"), UiTheme.Hex("#6FBF8C"), UiTheme.Hex("#58A6D6"), UiTheme.Hex("#B07CE0"), UiTheme.Hex("#E7A74A") };
        private static readonly Color WellFill = UiTheme.Hex("#0D1112");
        private static readonly Color WellShadow = UiTheme.Hex("#070909");
        private static readonly Color Round = UiTheme.Hex("#E9CF92");
        private static readonly Color RoundLow = UiTheme.Hex("#E58A3C");
        private static readonly Color RoundSpent = UiTheme.Hex("#2B3133");
        private static readonly Color HeatCool = UiTheme.Hex("#5FB6B0");
        private static readonly Color HeatHot = UiTheme.Hex("#E2653A");
        private static readonly Color Draw = UiTheme.Hex("#9FD8E6");

        private Image _card;
        private Image _rimTop;
        private Image[] _outline;
        private Image _plate;
        private Image _rarity;
        private Image _icon;
        private Image _iconShadow;
        private Image _glowOuter;
        private Image _glowInner;
        private Image _emptyMark;
        private Image _flash;
        private Image _numberBack;
        private Text _number;
        private Text _name;
        private Text _resource;
        private Text _special;
        private Image _gaugeBack;
        private Image _gaugeFill;
        private readonly List<Image> _pips = new();
        private readonly List<Image> _brackets = new();
        private System.Func<int, Sprite> _rarityFrame;
        private bool _shown;
        private bool _wasActive;
        private bool _wasReloading;
        private bool _wasSpecialReady;
        private float _switchFlash;
        private float _reloadFlash;
        private float _specialFlash;
        private Color _gaugeColor;
        private Color _specialColor;

        public string SlotNumber { get; private set; } = string.Empty;
        public bool IsActive { get; private set; }
        public bool IsEmpty { get; private set; } = true;
        /// <summary>A weapon with nothing left to fire (no magazine, no reserve).</summary>
        public bool IsUnavailable { get; private set; }
        public string NameText => _name != null ? _name.text : string.Empty;
        /// <summary>The visible resource line: empty for melee and for an empty slot.</summary>
        public string ResourceText => _resource != null && _resource.enabled ? _resource.text : string.Empty;
        public string SpecialText => _special != null && _special.enabled ? _special.text : string.Empty;
        public bool ResourceVisible => _resource != null && _resource.enabled;
        public Sprite IconSprite => _icon != null && _icon.enabled ? _icon.sprite : null;
        public bool IconVisible => _icon != null && _icon.enabled;
        public Sprite FrameSprite => _rarity != null && _rarity.enabled ? _rarity.sprite : null;
        public bool BracketsVisible => _brackets.Count > 0 && _brackets[0].enabled;
        /// <summary>Pips lit / drawn in the round gauge (0 / 0 when it is a continuous bar or hidden).</summary>
        public int PipsLit { get; private set; }
        public int PipsShown { get; private set; }
        /// <summary>The continuous gauge's fill (heat, draw, reload, large magazines); -1 when hidden or pips.</summary>
        public float GaugeFill => _gaugeFill != null && _gaugeFill.enabled ? _gaugeFill.fillAmount : -1f;
        public bool FlashActive => _switchFlash > 0f || _reloadFlash > 0f || _specialFlash > 0f;
        public RectTransform Rect => (RectTransform)transform;
        public RectTransform SlotRect { get; private set; }

        public static HudWeaponSlotView Create(Transform parent, UiRect bounds, string slotNumber, Sprite slotSprite, System.Func<int, Sprite> rarityFrame, string name)
        {
            var rect = UiBuild.NewRect(parent, name, bounds);
            var view = rect.gameObject.AddComponent<HudWeaponSlotView>();
            view.SlotNumber = slotNumber;
            view._rarityFrame = rarityFrame;
            view.Build(slotSprite);
            return view;
        }

        private void Build(Sprite slotSprite)
        {
            // The card: one framed unit per weapon, the HP bar's frame so the bottom HUD reads as one family.
            _card = UiBuild.Plate(transform, new UiRect(1, 1, Width - 2, SlotSize - 2), CardFill, "Card");
            _outline = HudHealthBarView.Frame(transform, Width, SlotSize, out _rimTop);

            // The icon well: recessed (dark, shadowed top edge), the rarity frame around it, the icon on a drop shadow.
            SlotRect = UiBuild.NewRect(transform, "Slot", new UiRect(0, 0, SlotSize, SlotSize));
            var well = new UiRect(2, 2, Well, Well);
            _plate = UiBuild.Plate(SlotRect, well, WellFill, "Plate");
            UiBuild.Plate(SlotRect, new UiRect(2, 2, Well, 1), WellShadow, "WellShadow");
            _rarity = UiBuild.Sliced(SlotRect, well, null, Color.white, "RarityFrame");
            _rarity.enabled = false;
            _emptyMark = UiBuild.Plate(SlotRect, new UiRect(2 + (Well - 4) / 2, 2 + (Well - 4) / 2, 4, 4), UiTheme.InkDisabled, "EmptyMark");
            _glowOuter = UiBuild.Plate(SlotRect, new UiRect(2 + (Well - 24) / 2, 2 + (Well - 24) / 2, 24, 24), Color.clear, "GlowOuter");
            _glowInner = UiBuild.Plate(SlotRect, new UiRect(2 + (Well - 16) / 2, 2 + (Well - 16) / 2, 16, 16), Color.clear, "GlowInner");
            var iconBounds = new UiRect(2 + (Well - IconSize) / 2, 2 + (Well - IconSize) / 2, IconSize, IconSize);
            _iconShadow = Icon(new UiRect(iconBounds.X + 1, iconBounds.Y + 1, IconSize, IconSize), "IconShadow");
            _iconShadow.color = new Color(0f, 0f, 0f, 0.55f);
            _icon = Icon(iconBounds, "Icon");
            _flash = UiBuild.Plate(SlotRect, well, new Color(1f, 1f, 1f, 0f), "Flash");

            // Slot number chip, top-left of the well: the key that selects the slot.
            var chip = new UiRect(2, 2, 8, UiText.LineHeight);
            _numberBack = UiBuild.Plate(SlotRect, chip, UiTheme.WithAlpha(UiTheme.NearBlack, 0.9f), "NumberBack");
            _number = UiBuild.Label(SlotRect, SlotNumber, chip, 1, TextAnchor.UpperCenter, UiTheme.Ink, false, gameObject.name + "Number");
            _brackets.AddRange(UiBuild.Brackets(SlotRect, well, UiTheme.Amber));
            foreach (var bracket in _brackets) bracket.enabled = false;

            // The info column: a 1 px divider, then name / resource / gauge / status, each in its own band.
            var x = SlotSize + Gap;
            UiBuild.Plate(transform, new UiRect(SlotSize - 1, 3, 1, SlotSize - 6), UiTheme.WithAlpha(UiTheme.PanelEdgeSoft, 0.9f), "Divider");
            _name = UiBuild.Label(transform, "—", new UiRect(x, 3, TextWidth, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Ink, false, gameObject.name + "Name");
            _resource = UiBuild.Label(transform, string.Empty, new UiRect(x, 13, TextWidth, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Ink, false, gameObject.name + "Resource");
            _resource.enabled = false;
            var gauge = new UiRect(x, 24, GaugeWidth, GaugeHeight);
            _gaugeBack = UiBuild.Plate(transform, new UiRect(gauge.X - 1, gauge.Y - 1, GaugeWidth + 2, GaugeHeight + 2), UiTheme.WithAlpha(WellShadow, 0.95f), "GaugeBack");
            _gaugeFill = UiBuild.Fillable(transform, gauge, Round, Image.FillMethod.Horizontal, (int)Image.OriginHorizontal.Left, "GaugeFill");
            _special = UiBuild.Label(transform, string.Empty, new UiRect(x, 29, TextWidth, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Amber, false, gameObject.name + "Special");
            _special.enabled = false;
        }

        private Image Icon(UiRect bounds, string name)
        {
            var rect = UiBuild.NewRect(SlotRect, name, bounds);
            var image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            image.enabled = false;
            return image;
        }

        public void Show(HudWeaponState w)
        {
            IsActive = w.IsActive;
            IsEmpty = string.IsNullOrEmpty(w.DefinitionId) && (string.IsNullOrEmpty(w.Name) || w.Name == "—");
            IsUnavailable = !IsEmpty && w.NoAmmo && w.Resource == HudResourceKind.Ammo;
            if (_shown && IsActive && !_wasActive) _switchFlash = SwitchFlashSeconds;
            if (_shown && _wasReloading && !w.IsReloading && IsActive) _reloadFlash = ReloadFlashSeconds;
            var specialReady = !IsEmpty && w.SpecialName != null && w.SpecialReady;
            if (_shown && specialReady && !_wasSpecialReady) _specialFlash = SpecialFlashSeconds;
            _shown = true;
            _wasActive = IsActive;
            _wasReloading = w.IsReloading;
            _wasSpecialReady = specialReady;

            // The card: lit when held, dimmed when holstered, faint when empty.
            _card.color = IsEmpty ? UiTheme.WithAlpha(CardFill, 0.6f) : IsActive ? CardFillActive : CardFill;
            _rimTop.color = IsActive ? UiTheme.Amber : HudHealthBarView.RimLightColor;
            var outline = IsActive ? UiTheme.Darken(UiTheme.AmberDim, 0.35f) : HudHealthBarView.OutlineColor;
            foreach (var o in _outline) o.color = outline;

            var frame = !IsEmpty ? _rarityFrame?.Invoke(w.Rarity) : null;
            _rarity.enabled = frame != null;
            _rarity.sprite = frame;
            _rarity.type = Image.Type.Sliced;
            // Slice borders at native pixels: the HUD canvas's reference pixels-per-unit would otherwise blow the
            // frame's 4 px border up until it swallows the well.
            var canvas = _rarity.canvas;
            if (frame != null && canvas != null) _rarity.pixelsPerUnitMultiplier = canvas.referencePixelsPerUnit / Mathf.Max(0.0001f, frame.pixelsPerUnit);
            var glow = IsEmpty ? Color.clear : RarityGlow[Mathf.Clamp(w.Rarity, 0, RarityGlow.Length - 1)];
            var glowAlpha = IsActive ? 1f : 0.5f;
            _glowOuter.color = UiTheme.WithAlpha(glow, 0.09f * glowAlpha);
            _glowInner.color = UiTheme.WithAlpha(glow, 0.14f * glowAlpha);
            _rarity.color = IsActive ? Color.white : new Color(0.7f, 0.7f, 0.7f, 0.85f);
            _icon.enabled = _iconShadow.enabled = !IsEmpty && w.Icon != null;
            _icon.sprite = _iconShadow.sprite = w.Icon;
            _icon.color = IsUnavailable ? new Color(0.85f, 0.45f, 0.42f, IsActive ? 1f : 0.7f) : IsActive ? Color.white : new Color(0.62f, 0.64f, 0.64f, 1f);
            _emptyMark.enabled = IsEmpty;
            foreach (var bracket in _brackets) bracket.enabled = IsActive;
            _number.color = IsActive ? UiTheme.Amber : UiTheme.InkMuted;

            _name.text = IsEmpty ? "—" : UiText.Fit(w.Name, TextWidth);
            _name.color = IsEmpty ? UiTheme.InkFaint : IsActive ? UiTheme.Amber : UiTheme.InkMuted;

            var showsResource = !IsEmpty && w.ShowsResource;
            _resource.enabled = showsResource;
            if (showsResource)
            {
                _resource.text = IsUnavailable ? "NO AMMO" : w.ResourceText;
                _resource.color = IsUnavailable || (w.Resource == HudResourceKind.Heat && w.Overheated) ? UiTheme.Danger : IsActive ? UiTheme.Ink : UiTheme.InkMuted;
            }

            ShowGauge(w, showsResource);

            // Status line: the held Legendary's special (only ever set for the held weapon), else a running reload.
            var showsSpecial = !IsEmpty && w.SpecialName != null;
            var reloading = !IsEmpty && w.IsReloading && w.Resource == HudResourceKind.Ammo;
            _special.enabled = showsSpecial || reloading;
            if (showsSpecial)
            {
                _special.text = w.SpecialReady ? "RMB READY" : $"RMB {Mathf.RoundToInt(w.SpecialCooldown01 * 100f)}%";
                _specialColor = w.SpecialReady ? UiTheme.Amber : UiTheme.AmberDim;
            }
            else if (reloading)
            {
                _special.text = "RELOADING";
                _specialColor = UiTheme.InkMuted;
            }

            Paint();
        }

        private void ShowGauge(HudWeaponState w, bool showsResource)
        {
            var dim = IsActive ? 1f : 0.6f;
            var pips = 0;
            var lit = 0;
            var bar = -1f;
            if (showsResource)
            {
                switch (w.Resource)
                {
                    case HudResourceKind.Ammo when w.IsReloading:
                        bar = w.Reload01;
                        _gaugeColor = UiTheme.Amber;
                        break;
                    case HudResourceKind.Ammo when w.MagazineSize > 0 && w.MagazineSize <= MaxPips:
                        pips = w.MagazineSize;
                        lit = Mathf.Clamp(w.Magazine, 0, pips);
                        _gaugeColor = lit * 4 <= pips ? RoundLow : Round;
                        break;
                    case HudResourceKind.Ammo when w.MagazineSize > 0:
                        bar = Mathf.Clamp01(w.Magazine / (float)w.MagazineSize);
                        _gaugeColor = w.Magazine * 4 <= w.MagazineSize ? RoundLow : Round;
                        break;
                    case HudResourceKind.Heat:
                        bar = w.Heat01;
                        _gaugeColor = w.Overheated ? UiTheme.Danger : Color.Lerp(HeatCool, HeatHot, w.Heat01);
                        break;
                    case HudResourceKind.Charge:
                        bar = w.IsCharging ? w.Charge01 : 1f;
                        _gaugeColor = w.IsCharging ? Draw : UiTheme.WithAlpha(Draw, 0.6f);
                        break;
                }
            }

            _gaugeColor = UiTheme.WithAlpha(_gaugeColor, _gaugeColor.a * dim);
            _gaugeBack.enabled = pips > 0 || bar >= 0f;
            if (IsUnavailable) _gaugeBack.color = UiTheme.WithAlpha(UiTheme.Darken(UiTheme.Danger, 0.45f), 0.95f);
            else _gaugeBack.color = UiTheme.WithAlpha(WellShadow, 0.95f);
            _gaugeFill.enabled = bar >= 0f;
            _gaugeFill.fillAmount = Mathf.Max(0f, bar);

            // One pip per round: the magazine at a glance, spent rounds dark, the last quarter warm.
            var pipWidth = pips > 0 ? Mathf.Max(1, (GaugeWidth - (pips - 1)) / pips) : 0;
            while (_pips.Count < pips) _pips.Add(UiBuild.Plate(transform, new UiRect(0, 24, 1, GaugeHeight), Round, "Pip" + _pips.Count));
            for (var i = 0; i < _pips.Count; i++)
            {
                var on = i < pips;
                _pips[i].enabled = on;
                if (!on) continue;
                var rect = _pips[i].rectTransform;
                rect.anchoredPosition = new Vector2(SlotSize + Gap + i * (pipWidth + 1), -24f);
                rect.sizeDelta = new Vector2(pipWidth, GaugeHeight);
            }

            PipsShown = pips;
            PipsLit = lit;
        }

        private void Paint()
        {
            var reload = _reloadFlash / ReloadFlashSeconds;
            for (var i = 0; i < PipsShown; i++)
                _pips[i].color = i < PipsLit ? Color.Lerp(_gaugeColor, Color.white, 0.6f * reload) : RoundSpent;
            _gaugeFill.color = Color.Lerp(_gaugeColor, Color.white, 0.6f * reload);
            var switchIn = _switchFlash / SwitchFlashSeconds;
            _flash.color = UiTheme.WithAlpha(UiTheme.Amber, 0.35f * switchIn * switchIn);
            var special = _specialFlash / SpecialFlashSeconds;
            if (_special.enabled) _special.color = Color.Lerp(_specialColor, Color.white, 0.7f * special);
        }

        private void Update()
        {
            if (!FlashActive) return;
            var dt = Time.unscaledDeltaTime;
            _switchFlash = Mathf.Max(0f, _switchFlash - dt);
            _reloadFlash = Mathf.Max(0f, _reloadFlash - dt);
            _specialFlash = Mathf.Max(0f, _specialFlash - dt);
            Paint();
        }
    }

    /// <summary>
    /// The bottom-right active consumable (91 Active Consumable) as an icon slot with a stack chip ("x3"). An empty
    /// slot shows the neutral plate and centre mark; there is no permanent text. While a timed use channels, a top strip
    /// shows the remaining seconds over a draining amber bar, both from the use action's timer.
    /// </summary>
    public sealed class HudConsumableSlotView : MonoBehaviour
    {
        public const int Size = 40;
        public const int IconSize = 32;
        private const int Inset = 3;

        private Image _plate;
        private Image _rarity;
        private Image _icon;
        private Image _emptyMark;
        private Image _countBack;
        private Text _count;
        private Image _useBar;
        private Image _useBack;
        private Text _useTime;
        private System.Func<int, Sprite> _rarityFrame;

        public bool IsEmpty { get; private set; } = true;
        public string CountText => _count != null && _count.enabled ? _count.text : string.Empty;
        /// <summary>The remaining-use countdown shown on the slot ("1.4s"), empty when no timed use is running.</summary>
        public string UseTimeText => _useTime != null && _useTime.enabled ? _useTime.text : string.Empty;
        /// <summary>Remaining fraction drawn by the use bar (0 when hidden).</summary>
        public float UseBar01 => _useBar != null && _useBar.enabled ? _useBar.fillAmount : 0f;
        public RectTransform UseTimeRect => _useBack != null ? _useBack.rectTransform : null;
        public RectTransform CountRect => _countBack != null ? _countBack.rectTransform : null;
        public Sprite IconSprite => _icon != null && _icon.enabled ? _icon.sprite : null;
        public bool IconVisible => _icon != null && _icon.enabled;
        public Sprite FrameSprite => _rarity != null && _rarity.enabled ? _rarity.sprite : null;
        public RectTransform Rect => (RectTransform)transform;

        public static HudConsumableSlotView Create(Transform parent, UiRect bounds, Sprite slotSprite, System.Func<int, Sprite> rarityFrame, string name = "ConsumableSlot")
        {
            var rect = UiBuild.NewRect(parent, name, bounds);
            var view = rect.gameObject.AddComponent<HudConsumableSlotView>();
            view._rarityFrame = rarityFrame;
            view.Build(bounds.Width, bounds.Height, slotSprite);
            return view;
        }

        private void Build(int width, int height, Sprite slotSprite)
        {
            var inner = new UiRect(0, 0, width, height);
            _plate = UiBuild.Sliced(transform, inner, slotSprite, null, "Plate");
            _rarity = UiBuild.Sliced(transform, inner, null, Color.white, "RarityFrame");
            _rarity.enabled = false;
            _emptyMark = UiBuild.Plate(transform, new UiRect((width - 4) / 2, (height - 4) / 2, 4, 4), UiTheme.InkDisabled, "EmptyMark");
            var iconRect = UiBuild.NewRect(transform, "Icon", new UiRect((width - IconSize) / 2, (height - IconSize) / 2, IconSize, IconSize));
            _icon = iconRect.gameObject.AddComponent<Image>();
            _icon.raycastTarget = false;
            _icon.preserveAspect = true;
            _icon.enabled = false;

            var chip = new UiRect(width - Inset - 20, height - Inset - UiText.LineHeight, 20, UiText.LineHeight);
            _countBack = UiBuild.Plate(transform, chip, UiTheme.WithAlpha(UiTheme.NearBlack, 0.85f), "CountBack");
            _countBack.enabled = false;
            _count = UiBuild.Label(transform, string.Empty, chip, 1, TextAnchor.UpperRight, UiTheme.Ink, false, "Count");
            _count.enabled = false;

            // Timed use: the remaining seconds on a dark strip along the top edge (clear of the chip, the icon stays
            // readable below it) over a 2px amber bar that drains toward the left as the use completes.
            var strip = new UiRect(Inset, Inset, width - Inset * 2, UiText.LineHeight + 3);
            _useBack = UiBuild.Plate(transform, strip, UiTheme.WithAlpha(UiTheme.NearBlack, 0.85f), "UseTimeBack");
            _useBack.enabled = false;
            _useTime = UiBuild.Label(transform, string.Empty, new UiRect(strip.X, strip.Y, strip.Width, UiText.LineHeight), 1, TextAnchor.UpperCenter, UiTheme.Amber, false, "UseTime");
            _useTime.enabled = false;
            _useBar = UiBuild.Fillable(transform, new UiRect(strip.X + 1, strip.Y + UiText.LineHeight, strip.Width - 2, 2), UiTheme.Amber, Image.FillMethod.Horizontal, (int)Image.OriginHorizontal.Left, "UseBar");
            _useBar.enabled = false;
        }

        public void Show(HudSnapshot s)
        {
            IsEmpty = !s.HasConsumable;
            var frame = !IsEmpty ? _rarityFrame?.Invoke(s.ConsumableRarity) : null;
            _rarity.enabled = frame != null;
            _rarity.sprite = frame;
            _icon.enabled = !IsEmpty && s.ConsumableIcon != null;
            _icon.sprite = s.ConsumableIcon;
            _emptyMark.enabled = IsEmpty;
            _plate.color = _plate.sprite != null ? (IsEmpty ? new Color(0.62f, 0.62f, 0.62f, 1f) : Color.white) : UiTheme.Charcoal;
            var showsCount = !IsEmpty && s.ConsumableQuantity > 0;
            _countBack.enabled = showsCount;
            _count.enabled = showsCount;
            if (showsCount) _count.text = $"x{s.ConsumableQuantity}";

            var channelling = s.ConsumableUseActive;
            _useBar.enabled = channelling;
            _useBar.fillAmount = s.ConsumableUse01;
            _useBack.enabled = channelling;
            _useTime.enabled = channelling;
            _useTime.text = s.ConsumableUseText;
        }
    }
}

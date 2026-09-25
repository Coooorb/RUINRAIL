using System.Collections.Generic;
using System.Linq;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuinRail.UI.Hud
{
    /// <summary>
    /// The in-expedition HUD (91) as a code-built uGUI canvas at the 640×360 reference (101; 1920×1080 = clean 3×).
    ///
    /// Top-left is the minimap with the biome identity under it — no permanent objective block. Top-centre carries the
    /// boss bar and, briefly, the room-title reveal when the player enters a room. Top-right is the graphical coin
    /// readout. Bottom-left is the dash icon with its cooldown wipe beside the HP bar and number, bottom-centre the
    /// two graphical weapon slots, bottom-right the active consumable. The low-HP vignette sits underneath everything
    /// in this canvas, so the overlay windows (their own canvases) always draw over it.
    ///
    /// It renders the view model's snapshot and never touches gameplay; per-frame work is the view model's cheap Tick.
    /// The slot art (plate, rarity frames, dash icon, coin token, vignette) comes from the UI skin the inventory uses.
    /// </summary>
    public sealed class DungeonHudView : MonoBehaviour
    {
        public const int ReferenceWidth = 640;
        public const int ReferenceHeight = 360;
        public const int Margin = 6;
        /// <summary>The pixel face is drawn at its authored cap height; the HUD never resamples it.</summary>
        public const int FontSize = UiText.GlyphHeight;
        /// <summary>Vertical pitch of stacked HUD lines: one authored line box, so two lines can never share pixels.</summary>
        public const int LinePitch = UiText.LineHeight + 1;
        public const int LineHeight = UiText.LineHeight;
        public const int BarHeight = 8;
        public const int HpBarWidth = 150;
        public const int WeaponsGap = 8;
        public const int WeaponsWidth = HudWeaponSlotView.Width * 2 + WeaponsGap;
        /// <summary>Gap between timed-effect chips in the strip over the consumable slot.</summary>
        public const int StatusChipGap = 2;
        public const int StatusChipPitch = HudStatusChipView.Size + StatusChipGap;

        // ---- Top band geometry, in reference pixels from the top-left ----
        public static readonly UiRect MinimapRect = new(Margin, Margin, HudMinimapView.Width, HudMinimapView.Height);
        public static readonly UiRect BiomeRect = new(Margin, MinimapRect.Bottom + 2, HudMinimapView.Width, LineHeight);
        public static readonly UiRect PartyRect = new(Margin, BiomeRect.Bottom + 3, 180, LinePitch + LineHeight);
        public static readonly UiRect RoomTitleRect = new((ReferenceWidth - HudRoomTitleView.Width) / 2, 28, HudRoomTitleView.Width, HudRoomTitleView.Height);
        /// <summary>Under the tutorial band (52..92) and the co-op party lines: the event-outcome notice line, never over the room title, a prompt or a party line.</summary>
        public static readonly UiRect NoticeRect = new((ReferenceWidth - HudNoticeView.Width) / 2, 124, HudNoticeView.Width, HudNoticeView.Height);
        public static readonly UiRect CoinsRect = new(ReferenceWidth - Margin - HudCoinView.Width, Margin, HudCoinView.Width, HudCoinView.Height);
        /// <summary>Top-right under the coins: the enemy-remaining chip (91), only during an active standard combat encounter.</summary>
        public static readonly UiRect EnemiesRect = new(ReferenceWidth - Margin - HudEnemyCountView.Width, CoinsRect.Bottom + 4, HudEnemyCountView.Width, HudEnemyCountView.Height);

        private DungeonHudViewModel _viewModel;
        private Canvas _canvas;
        private Text _hp;
        private Image _hpFill;
        private HudDashIconView _dashIcon;
        private HudWeaponSlotView _primary;
        private HudWeaponSlotView _secondary;
        private HudConsumableSlotView _consumable;
        private HudMinimapView _minimap;
        private HudCoinView _coins;
        private HudEnemyCountView _enemies;
        private HudRoomTitleView _roomTitle;
        private HudNoticeView _notice;
        private HudLowHealthVignetteView _vignette;
        private readonly List<HudStatusChipView> _statusChips = new();
        private Text _statusDetail;
        private Text _biome;
        private Text _bossName;
        private Image _bossFill;
        private readonly List<Text> _party = new();
        private RectTransform _partyRoot;
        private Font _font;

        public Canvas Canvas => _canvas;
        /// <summary>Bottom-left: the dash icon slot, left of the HP block.</summary>
        public RectTransform DashPanel { get; private set; }
        /// <summary>Bottom-left: the HP bar over the HP number.</summary>
        public RectTransform HpPanel { get; private set; }
        public RectTransform WeaponsPanel { get; private set; }
        public RectTransform ConsumablePanel { get; private set; }
        /// <summary>Top-left: the minimap panel (the biome line sits under it, the party lines under that).</summary>
        public RectTransform TopLeftPanel { get; private set; }
        /// <summary>Top-right: the graphical coin readout.</summary>
        public RectTransform TopRightPanel { get; private set; }
        public RectTransform PartyPanel => _partyRoot;
        /// <summary>Top-centre: the temporary room-title reveal, below the boss bar band.</summary>
        public RectTransform RoomTitlePanel { get; private set; }
        /// <summary>Boss bar: top-centre between the minimap block and the coins; active only during a boss encounter.</summary>
        public RectTransform BossPanel { get; private set; }
        public string BossNameText => _bossName != null ? _bossName.text : string.Empty;
        public float BossFill => _bossFill != null ? _bossFill.fillAmount : 0f;
        public bool BossVisible => BossPanel != null && BossPanel.gameObject.activeSelf;
        public DungeonHudViewModel ViewModel => _viewModel;
        public int Renders { get; private set; }

        public HudDashIconView DashIcon => _dashIcon;
        public HudWeaponSlotView PrimarySlot => _primary;
        public HudWeaponSlotView SecondarySlot => _secondary;
        public HudConsumableSlotView ConsumableSlot => _consumable;
        /// <summary>The room-graph minimap; bind its model from the composition root.</summary>
        public HudMinimapView Minimap => _minimap;
        public HudCoinView CoinView => _coins;
        /// <summary>Top-right, under the coins: the enemy-remaining chip; hidden outside an active standard combat encounter.</summary>
        public HudEnemyCountView EnemyCount => _enemies;
        public RectTransform EnemyCountPanel => _enemies != null ? _enemies.Rect : null;
        public bool EnemyCountVisible => _enemies != null && _enemies.IsVisible;
        /// <summary>The enemy chip's number text ("x5"); empty while hidden.</summary>
        public string EnemiesText => _enemies != null && _enemies.IsVisible ? _enemies.CountText : string.Empty;
        public HudRoomTitleView RoomTitle => _roomTitle;
        /// <summary>The event-outcome notice line (what an interaction just did / why it was refused).</summary>
        public HudNoticeView Notice => _notice;
        public RectTransform NoticePanel => _notice != null ? _notice.Rect : null;
        public HudLowHealthVignetteView Vignette => _vignette;
        /// <summary>Bottom-right, above the consumable slot: the timed-effect chips (91 keeps its own blocks untouched).</summary>
        public RectTransform StatusPanel { get; private set; }
        public IReadOnlyList<HudStatusChipView> StatusChips => _statusChips;
        /// <summary>The chips actually on screen right now, in the order they are drawn.</summary>
        public IReadOnlyList<HudStatusChipView> VisibleStatusChips => _statusChips.Where(c => c.IsVisible).ToList();
        /// <summary>The detail line under the strip ("COMBAT STIM  FIRE RATE +20%  6s"); empty when nothing is active.</summary>
        public string StatusDetailText => _statusDetail != null ? _statusDetail.text : string.Empty;

        public string HpText => _hp != null ? _hp.text : string.Empty;
        /// <summary>The primary slot's visible resource line ("12 / 36", "NO AMMO", "HEAT 0%"…); empty for melee.</summary>
        public string PrimaryText => _primary != null ? _primary.ResourceText : string.Empty;
        public string SecondaryText => _secondary != null ? _secondary.ResourceText : string.Empty;
        /// <summary>The consumable stack chip ("x3"); empty when nothing is equipped.</summary>
        public string ConsumableText => _consumable != null ? _consumable.CountText : string.Empty;
        /// <summary>The compact depth chip inside the minimap frame ("D1"), never a sentence.</summary>
        public string DepthText => _minimap != null ? _minimap.DepthText : string.Empty;
        /// <summary>The biome identity line under the minimap.</summary>
        public string BiomeText => _biome != null ? _biome.text : string.Empty;
        /// <summary>The coin readout's number; the token beside it carries the meaning.</summary>
        public string CoinsText => _coins != null ? _coins.AmountText : string.Empty;
        public IReadOnlyList<string> PartyTexts
        {
            get
            {
                var list = new List<string>();
                foreach (var t in _party) if (t.gameObject.activeSelf) list.Add(t.text);
                return list;
            }
        }

        /// <summary>Creates the canvas hierarchy under a new root object and binds it to a view model.</summary>
        public static DungeonHudView Create(DungeonHudViewModel viewModel, string name = "DungeonHUD")
        {
            var go = new GameObject(name);
            var view = go.AddComponent<DungeonHudView>();
            view.Build();
            view.Bind(viewModel);
            return view;
        }

        public void Bind(DungeonHudViewModel viewModel)
        {
            if (_viewModel != null) _viewModel.Changed -= Render;
            _viewModel = viewModel;
            if (_viewModel != null)
            {
                _viewModel.Changed += Render;
                Render(_viewModel.Snapshot);
            }
        }

        /// <summary>Binds the room-graph minimap to the run's map model (the composition root owns and feeds it).</summary>
        public void BindMinimap(MinimapModel model) => _minimap?.Bind(model);

        private void OnDestroy()
        {
            if (_viewModel != null) _viewModel.Changed -= Render;
        }

        private void Update()
        {
            _viewModel?.Tick();
        }

        // ---- Construction (pixel-aligned rects at the reference resolution) ----

        private void Build()
        {
            _font = UiFont.Font();
            var skin = UiSkin.Load();
            var slotSprite = skin != null ? skin.InventorySlot : null;
            System.Func<int, Sprite> rarityFrame = skin != null ? skin.RarityFrame : null;
            var dashSprite = skin != null ? skin.DashIcon : null;

            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.pixelPerfect = true;
            _canvas.sortingLayerName = RuinRail.Core.Rendering.SortingLayers.ScreenUI;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            var root = (RectTransform)transform;

            // Bottom-left: the dash icon slot on the margin, then the HP bar over the HP number (91 Player State).
            DashPanel = Panel("Dash", root, new Vector2(0f, 0f), new Vector2(Margin, Margin), new Vector2(HudDashIconView.Size, HudDashIconView.Size));
            _dashIcon = HudDashIconView.Create(DashPanel, new UiRect(0, 0, HudDashIconView.Size, HudDashIconView.Size), slotSprite, dashSprite);

            HpPanel = Panel("HP", root, new Vector2(0f, 0f), new Vector2(Margin + HudDashIconView.Size + 4, Margin), new Vector2(HpBarWidth, BarHeight + LineHeight));
            // A translucent plate keeps the number readable over bright floor tiles (the bar itself is opaque).
            UiBuild.Plate(HpPanel, new UiRect(-2, -2, HpBarWidth + 4, BarHeight + LineHeight + 4), UiTheme.WithAlpha(UiTheme.NearBlack, 0.55f), "HpPlate").transform.SetAsFirstSibling();
            var barBack = Panel("HpBarBack", HpPanel, new Vector2(0f, 1f), Vector2.zero, new Vector2(HpBarWidth, BarHeight));
            var barBackImage = barBack.gameObject.AddComponent<Image>();
            barBackImage.color = new Color(0.12f, 0.12f, 0.12f, 0.9f);
            barBackImage.raycastTarget = false;
            var fill = Panel("HpBarFill", barBack, new Vector2(0f, 0f), Vector2.zero, new Vector2(HpBarWidth, BarHeight));
            _hpFill = fill.gameObject.AddComponent<Image>();
            _hpFill.color = new Color(0.85f, 0.2f, 0.2f, 1f);
            _hpFill.type = Image.Type.Filled;
            _hpFill.fillMethod = Image.FillMethod.Horizontal;
            _hpFill.sprite = UiBuild.Solid();   // uGUI ignores Filled without a sprite (see the dash wipe)
            _hpFill.raycastTarget = false;
            _hp = Label("HpText", HpPanel, new Vector2(0f, 1f), new Vector2(0f, -BarHeight), new Vector2(HpBarWidth, LineHeight), TextAnchor.UpperLeft);

            // Bottom-centre: both weapon slots as icon slots with their number, rarity frame, active brackets and —
            // where the weapon has one — the resource readout (91 Weapons).
            WeaponsPanel = Panel("Weapons", root, new Vector2(0.5f, 0f), new Vector2(0f, Margin), new Vector2(WeaponsWidth, HudWeaponSlotView.SlotSize));
            _primary = HudWeaponSlotView.Create(WeaponsPanel, new UiRect(0, 0, HudWeaponSlotView.Width, HudWeaponSlotView.SlotSize), "1", slotSprite, rarityFrame, "Primary");
            _secondary = HudWeaponSlotView.Create(WeaponsPanel, new UiRect(HudWeaponSlotView.Width + WeaponsGap, 0, HudWeaponSlotView.Width, HudWeaponSlotView.SlotSize), "2", slotSprite, rarityFrame, "Secondary");

            // Bottom-right: the active consumable as an icon slot with its stack chip (91 Active Consumable).
            ConsumablePanel = Panel("Consumable", root, new Vector2(1f, 0f), new Vector2(-Margin, Margin), new Vector2(HudConsumableSlotView.Size, HudConsumableSlotView.Size));
            _consumable = HudConsumableSlotView.Create(ConsumablePanel, new UiRect(0, 0, HudConsumableSlotView.Size, HudConsumableSlotView.Size), slotSprite, rarityFrame);

            // Bottom-right, directly above the consumable slot: the timed-effect chips, right-aligned so the strip
            // grows leftwards into empty HUD and never reaches the weapon slots. Its detail line sits above the strip.
            // Nothing here moves or covers the minimap, biome line, party lines, enemy chip, boss bar, low-HP vignette,
            // dash icon, weapon slots or the consumable slot: it occupies space none of them use.
            var statusWidth = HudSnapshot.MaxStatusEffects * StatusChipPitch - StatusChipGap;
            StatusPanel = Panel("Status", root, new Vector2(1f, 0f),
                new Vector2(-Margin, Margin + HudConsumableSlotView.Size + 4), new Vector2(statusWidth, HudStatusChipView.Size));
            for (var i = 0; i < HudSnapshot.MaxStatusEffects; i++)
            {
                // Right-aligned: chip 0 is the rightmost, nearest the consumable slot that granted it.
                var x = statusWidth - (i + 1) * StatusChipPitch + StatusChipGap;
                _statusChips.Add(HudStatusChipView.Create(StatusPanel, new UiRect(x, 0, HudStatusChipView.Size, HudStatusChipView.Size), "Status" + i));
            }

            _statusDetail = UiBuild.Label(StatusPanel, string.Empty, new UiRect(statusWidth - 150, -LineHeight - 2, 150, LineHeight),
                1, TextAnchor.UpperRight, UiTheme.Ink, false, "StatusDetail");
            _statusDetail.gameObject.SetActive(false);

            // Top-left: the minimap, the biome identity under it, and the compact co-op party lines under that.
            _minimap = HudMinimapView.Create(root, MinimapRect);
            TopLeftPanel = _minimap.Rect;
            // A translucent plate keeps the biome line readable over bright floor tiles, exactly like the HP block.
            UiBuild.Plate(root, new UiRect(BiomeRect.X - 2, BiomeRect.Y - 1, BiomeRect.Width + 4, BiomeRect.Height + 2), UiTheme.WithAlpha(UiTheme.NearBlack, 0.7f), "BiomePlate");
            _biome = UiBuild.Label(root, string.Empty, BiomeRect, 1, TextAnchor.UpperLeft, UiTheme.Ink, false, "Biome");
            _partyRoot = UiBuild.NewRect(root, "Party", PartyRect);
            for (var i = 0; i < 2; i++)
            {
                var line = UiBuild.Label(_partyRoot, string.Empty, new UiRect(0, i * LinePitch, PartyRect.Width, LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Ink, false, $"Member{i}");
                line.gameObject.SetActive(false);
                _party.Add(line);
            }

            // Top-centre: the boss bar (name line over a 6 px bar). x 280..500 sits clear of the 106 px minimap block
            // and of the coins block that starts at 572, so it can never overlap either.
            BossPanel = Panel("Boss", root, new Vector2(0.5f, 1f), new Vector2(70f, -Margin), new Vector2(220f, LineHeight + BarHeight));
            // The bar used to be a bare opaque quad with no frame and no plate behind the name: against the dark
            // dungeon it read as a black rectangle dropped on the HUD rather than as part of it. It now gets the same
            // treatment as the HP block — one translucent theme plate behind the whole block, and a soft edge around
            // the bar so the empty portion is visibly the bar's track rather than a hole. No panel covers anything:
            // the plate is the first sibling, under the name and the fill.
            UiBuild.Plate(BossPanel, new UiRect(-3, -2, 226, LineHeight + BarHeight + 4), UiTheme.WithAlpha(UiTheme.NearBlack, 0.62f), "BossPlate")
                .transform.SetAsFirstSibling();
            _bossName = Label("BossName", BossPanel, new Vector2(0f, 1f), Vector2.zero, new Vector2(220f, LineHeight), TextAnchor.UpperCenter);
            var bossBack = Panel("BossBarBack", BossPanel, new Vector2(0f, 1f), new Vector2(0f, -LineHeight), new Vector2(220f, BarHeight - 2));
            var bossBackImage = bossBack.gameObject.AddComponent<Image>();
            bossBackImage.color = UiTheme.WithAlpha(UiTheme.Charcoal, 0.92f);
            bossBackImage.raycastTarget = false;
            UiBuild.Border(bossBack, new UiRect(0, 0, 220, BarHeight - 2), UiTheme.PanelEdgeSoft);
            var bossFill = Panel("BossBarFill", bossBack, new Vector2(0f, 0f), Vector2.zero, new Vector2(220f, BarHeight - 2));
            _bossFill = bossFill.gameObject.AddComponent<Image>();
            _bossFill.color = new Color(0.85f, 0.2f, 0.2f, 1f);
            _bossFill.type = Image.Type.Filled;
            _bossFill.fillMethod = Image.FillMethod.Horizontal;
            _bossFill.sprite = UiBuild.Solid();
            _bossFill.raycastTarget = false;
            BossPanel.gameObject.SetActive(false);

            // Top-centre, under the boss band: the brief room-title reveal.
            _roomTitle = HudRoomTitleView.Create(root, RoomTitleRect);
            RoomTitlePanel = _roomTitle.Rect;
            _notice = HudNoticeView.Create(root, NoticeRect);

            // Top-right: the coin token plus the Carried Coins number.
            _coins = HudCoinView.Create(root, CoinsRect, skin != null ? skin.CoinIcon : null);
            TopRightPanel = _coins.Rect;
            // Under it: the enemy-remaining chip (hostile icon + "xN"); it only exists on screen while a standard
            // combat encounter is active in the player's room.
            _enemies = HudEnemyCountView.Create(root, EnemiesRect, skin != null ? skin.EnemyIcon : null);
            _enemies.Show(false, 0);

            // The low-HP danger frame, underneath every HUD element of this canvas.
            _vignette = HudLowHealthVignetteView.Create(root, skin != null ? skin.LowHealthVignette : null);
        }

        private static RectTransform Panel(string name, RectTransform parent, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
            return rect;
        }

        /// <summary>
        /// One HUD line in the pixel face. The box is the line's band; the text is nudged by the face's ascent so the
        /// glyphs land inside it (the same rule the front-end uses), and a single line never overflows its band.
        /// </summary>
        private Text Label(string name, RectTransform parent, Vector2 anchor, Vector2 offset, Vector2 size, TextAnchor alignment)
        {
            var rect = Panel(name, parent, anchor, offset, size);
            rect.anchoredPosition = offset + new Vector2(0f, -UiFont.TopOffset);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = _font;
            text.fontSize = FontSize;
            text.lineSpacing = 1f;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        // ---- Rendering (pure presentation of the snapshot) ----

        private void Render(HudSnapshot s)
        {
            Renders++;
            _hp.text = s.HpText + (s.ShieldVisible ? "  [SHIELD]" : string.Empty);
            _hpFill.fillAmount = s.MaxHp > 0 ? Mathf.Clamp01(s.Hp / (float)s.MaxHp) : 0f;
            _dashIcon.Show(s.DashReady, s.DashCooldown01, s.DashDisabled);
            _primary.Show(s.Primary);
            _secondary.Show(s.Secondary);
            _consumable.Show(s);
            _biome.text = s.BiomeText;
            _coins.Show(s.Coins);
            _enemies.Show(s.EnemiesVisible, s.EnemiesRemaining);
            // Low HP is judged against the effective maximum the HUD already reads (base + equipment), never a base value.
            _vignette.Show(s.Hp, s.MaxHp, s.VignetteSuppressed);
            BossPanel.gameObject.SetActive(s.BossVisible);
            if (s.BossVisible)
            {
                _bossName.text = $"{s.BossName}  {s.BossHp} / {s.BossMaxHp}";
                _bossFill.fillAmount = s.BossHp01;
            }

            for (var i = 0; i < _party.Count; i++)
            {
                var visible = i < s.Party.Count;
                _party[i].gameObject.SetActive(visible);
                if (visible) _party[i].text = $"{s.Party[i].Name}  {s.Party[i].StateText}";
            }

            // Timed effects: one chip each, newest-expiring last, and one detail line naming the longest-running one.
            // An expired effect leaves the snapshot on the runner's own tick, so a chip cannot outlive its effect.
            for (var i = 0; i < _statusChips.Count; i++) _statusChips[i].Show(i < s.StatusEffects.Count ? s.StatusEffects[i] : null);
            var hasStatus = s.StatusEffects.Count > 0;
            _statusDetail.gameObject.SetActive(hasStatus);
            // Cleared, not merely hidden: a line left holding the name of an expired buff is stale UI whether or not
            // anything is drawing it, and the HUD's own readback would still report it.
            _statusDetail.text = hasStatus ? UiText.Fit(s.StatusEffects[0].DetailText, 150) : string.Empty;
        }
    }
}

using UnityEngine;

namespace RuinRail.UI.Theme
{
    /// <summary>
    /// The binding between the generated UI art and the runtime screens.
    ///
    /// The art itself stays at its convention path under <c>Assets/Game/Art/UI</c>, where the asset naming convention
    /// and the import validators expect to find it. A player build cannot load from there by path, so this asset —
    /// which does live in Resources — carries the references across, exactly as <c>GameContentCatalog</c> does for
    /// gameplay definitions. Nothing here is a second copy of an asset; it is a list of pointers.
    ///
    /// Every field is optional. A missing backdrop degrades to the flat charcoal ground rather than to a broken
    /// screen, because a menu that cannot be navigated is a worse failure than one that is plain.
    /// </summary>
    public sealed class UiSkin : ScriptableObject
    {
        /// <summary>Path inside Resources, without extension.</summary>
        public const string ResourcePath = "UiSkin";

        [SerializeField] private Sprite shelterBackdrop;
        [SerializeField] private Sprite menuBackdrop;
        [SerializeField] private Sprite[] stationIcons = System.Array.Empty<Sprite>();
        [SerializeField] private Texture2D cursorPointer;
        [SerializeField] private Vector2 cursorPointerHotspot;
        [SerializeField] private Texture2D cursorHover;
        [SerializeField] private Vector2 cursorHoverHotspot;
        [SerializeField] private Texture2D cursorAim;
        [SerializeField] private Vector2 cursorAimHotspot;
        [SerializeField] private Sprite panelFrame;
        [SerializeField] private Sprite panelEmphasis;
        [SerializeField] private Sprite inventorySlot;
        [SerializeField] private Sprite[] rarityFrames = System.Array.Empty<Sprite>();
        [SerializeField] private Sprite dashIcon;
        [SerializeField] private Sprite coinIcon;
        [SerializeField] private Sprite lowHealthVignette;

        /// <summary>The Shelter interior the hub screen is staged in.</summary>
        public Sprite ShelterBackdrop => shelterBackdrop;

        /// <summary>The main menu backdrop: the way into the Shelter.</summary>
        public Sprite MenuBackdrop => menuBackdrop;

        /// <summary>Station iconography, indexed by <c>BaseStation</c> order.</summary>
        public Sprite StationIcon(int index) =>
            stationIcons != null && index >= 0 && index < stationIcons.Length ? stationIcons[index] : null;

        public int StationIconCount => stationIcons?.Length ?? 0;

        /// <summary>Hardware cursors (pixel-art, point filtered) with their hotspots in texture pixels from the top-left.</summary>
        public Texture2D CursorPointer => cursorPointer;
        public Vector2 CursorPointerHotspot => cursorPointerHotspot;
        public Texture2D CursorHover => cursorHover;
        public Vector2 CursorHoverHotspot => cursorHoverHotspot;
        public Texture2D CursorAim => cursorAim;
        public Vector2 CursorAimHotspot => cursorAimHotspot;
        public bool HasCursors => cursorPointer != null && cursorHover != null && cursorAim != null;

        /// <summary>9-sliced window/panel frame (art/106 section 18).</summary>
        public Sprite PanelFrame => panelFrame;
        /// <summary>9-sliced emphasised panel frame (selected / highlighted regions).</summary>
        public Sprite PanelEmphasis => panelEmphasis;
        /// <summary>9-sliced inventory slot plate (the empty-slot look).</summary>
        public Sprite InventorySlot => inventorySlot;
        /// <summary>9-sliced rarity frame for an occupied slot, indexed by <c>Rarity</c> (Common..Legendary).</summary>
        public Sprite RarityFrame(int rarity) => rarityFrames != null && rarity >= 0 && rarity < rarityFrames.Length ? rarityFrames[rarity] : null;
        /// <summary>The HUD dash icon (91 Player State); the view draws a fallback glyph while it is unbound.</summary>
        public Sprite DashIcon => dashIcon;
        /// <summary>The HUD coin token (91 Top Information): the graphical Carried Coins readout is this plus the number.</summary>
        public Sprite CoinIcon => coinIcon;
        /// <summary>The low-HP danger vignette (91 Player State), stretched over the frame and tinted by the HUD.</summary>
        public Sprite LowHealthVignette => lowHealthVignette;
        public bool HasInventoryFrames => panelFrame != null && inventorySlot != null && rarityFrames != null && rarityFrames.Length == 5 && System.Array.TrueForAll(rarityFrames, s => s != null);

        private static UiSkin _cached;
        private static bool _looked;

        /// <summary>Loads the skin once per process. A missing skin is a warning, never an exception.</summary>
        public static UiSkin Load()
        {
            if (_looked) return _cached;
            _looked = true;
            _cached = Resources.Load<UiSkin>(ResourcePath);
            if (_cached == null)
                Debug.LogWarning($"No UI skin at Resources/{ResourcePath}; menus fall back to flat backgrounds. Run RuinRail/Art/Generate All Final Art.");
            return _cached;
        }

        /// <summary>Drops the cache so a rebuilt skin is picked up without a domain reload.</summary>
        public static void InvalidateCache()
        {
            _cached = null;
            _looked = false;
        }

#if UNITY_EDITOR
        /// <summary>Editor-only authoring entry point used by the art generator.</summary>
        public void EditorSet(Sprite shelter, Sprite menu, Sprite[] stations)
        {
            shelterBackdrop = shelter;
            menuBackdrop = menu;
            stationIcons = stations ?? System.Array.Empty<Sprite>();
        }

        /// <summary>Editor-only: binds the sliced panel, slot and rarity frames the inventory draws with.</summary>
        public void EditorSetFrames(Sprite panel, Sprite emphasis, Sprite slot, Sprite[] rarity)
        {
            panelFrame = panel;
            panelEmphasis = emphasis;
            inventorySlot = slot;
            rarityFrames = rarity ?? System.Array.Empty<Sprite>();
        }

        /// <summary>Editor-only: binds the generated HUD dash icon.</summary>
        public void EditorSetDashIcon(Sprite icon) => dashIcon = icon;

        /// <summary>Editor-only: binds the generated HUD coin token and the low-HP vignette.</summary>
        public void EditorSetHudSprites(Sprite coin, Sprite vignette)
        {
            coinIcon = coin;
            lowHealthVignette = vignette;
        }

        /// <summary>Editor-only: binds the three generated cursors and their hotspots.</summary>
        public void EditorSetCursors(Texture2D pointer, Vector2 pointerHotspot, Texture2D hover, Vector2 hoverHotspot, Texture2D aim, Vector2 aimHotspot)
        {
            cursorPointer = pointer;
            cursorPointerHotspot = pointerHotspot;
            cursorHover = hover;
            cursorHoverHotspot = hoverHotspot;
            cursorAim = aim;
            cursorAimHotspot = aimHotspot;
        }
#endif
    }
}

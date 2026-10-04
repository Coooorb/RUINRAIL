using System.Collections.Generic;
using System.Linq;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuinRail.UI.Hud
{
    /// <summary>
    /// The compact pixel symbols the minimap draws inside a discovered special room, as rectangles on a 5×5 grid.
    /// Every symbol differs from the others in shape as well as in colour (spec 18.4), so the map stays readable
    /// without relying on colour perception, and no extra sprite asset is needed for five-pixel glyphs.
    /// </summary>
    public static class MinimapSymbols
    {
        public const int Size = 5;
        public const int MaxParts = 3;

        private static readonly (UiRect rect, Color color)[] None = System.Array.Empty<(UiRect, Color)>();

        public static IReadOnlyList<(UiRect rect, Color color)> For(MinimapRoomKind kind) => kind switch
        {
            // Solid block with a dark core: the fight at the end of the depth.
            MinimapRoomKind.Boss => new[] { (new UiRect(0, 0, 5, 5), UiTheme.Danger), (new UiRect(2, 2, 1, 1), UiTheme.NearBlack) },
            // Stacked shelves.
            MinimapRoomKind.Merchant => new[] { (new UiRect(0, 0, 5, 1), UiTheme.Amber), (new UiRect(0, 2, 5, 1), UiTheme.Amber), (new UiRect(0, 4, 5, 1), UiTheme.Amber) },
            // A chest: body with a lid line.
            MinimapRoomKind.Treasure => new[] { (new UiRect(0, 0, 5, 5), UiTheme.AmberDim), (new UiRect(0, 2, 5, 1), UiTheme.Amber) },
            // A cross: the weapon cache.
            MinimapRoomKind.WeaponCache => new[] { (new UiRect(2, 0, 1, 5), UiTheme.Cyan), (new UiRect(0, 2, 5, 1), UiTheme.Cyan) },
            // A solid green core: the medical station.
            MinimapRoomKind.Medical => new[] { (new UiRect(1, 1, 3, 3), UiTheme.Terminal) },
            // Two crates side by side.
            MinimapRoomKind.Loot => new[] { (new UiRect(0, 1, 2, 3), UiTheme.Amber), (new UiRect(3, 1, 2, 3), UiTheme.Amber) },
            // An exclamation: an unresolved anomaly.
            MinimapRoomKind.Event => new[] { (new UiRect(2, 0, 1, 3), UiTheme.Cyan), (new UiRect(2, 4, 1, 1), UiTheme.Cyan) },
            // A hollow ring: where the depth started.
            MinimapRoomKind.Start => new[] { (new UiRect(0, 0, 5, 5), UiTheme.InkMuted), (new UiRect(1, 1, 3, 3), UiTheme.NearBlack) },
            // Two rails: the transit car.
            MinimapRoomKind.Transit => new[] { (new UiRect(0, 1, 5, 1), UiTheme.Cyan), (new UiRect(0, 3, 5, 1), UiTheme.Cyan) },
            _ => None
        };
    }

    /// <summary>One drawn minimap room: the block plus up to three symbol parts.</summary>
    internal sealed class MinimapCellView
    {
        public RectTransform Rect;
        public Image Fill;
        public readonly List<Image> Parts = new();
    }

    /// <summary>
    /// The minimap's pixel art, painted once at runtime: the panel (an opaque dark tactical ground with a faint grid,
    /// a bevelled steel frame and amber corner brackets), the three room states, the current-room halo and the
    /// eight-way player arrow. Pure presentation; sizes match <see cref="HudMinimapView"/> and
    /// <see cref="MinimapLayout.CellSize"/> exactly, so every sprite draws 1:1 on the pixel grid.
    /// </summary>
    internal static class MinimapArt
    {
        private static Sprite _panel, _unvisited, _visited, _current, _halo;
        private static readonly Sprite[] Arrows = new Sprite[8];

        private static Color32 H(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        private static Sprite Make(Color32[] px, int w, int h, string name)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name, hideFlags = HideFlags.DontSave };
            t.SetPixels32(px);
            t.Apply(false, true);
            var sprite = Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        /// <summary>The panel, y-up texture of the whole minimap rect.</summary>
        public static Sprite Panel(int w, int h)
        {
            if (_panel != null) return _panel;
            var px = new Color32[w * h];
            var grid = H("#172024");
            var major = H("#1E2B30");
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var top = 1f - y / (float)h;
                var c = Color32.Lerp(H("#0C1214"), H("#121A1D"), (1f - top) * 0.8f);
                if ((x - 3) % 24 == 0 || (y - 3) % 24 == 0) c = major;
                else if ((x - 3) % 6 == 0 || (y - 3) % 6 == 0) c = grid;
                if ((y & 1) == 0) c = Color32.Lerp(c, H("#080C0D"), 0.18f); // faint scanlines
                // Inner shadow inside the frame.
                var edge = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
                if (edge == 2) c = Color32.Lerp(c, H("#050708"), 0.55f);
                if (edge == 3) c = Color32.Lerp(c, H("#050708"), 0.25f);
                c.a = 255;
                if (edge == 0) c = H("#06090A");
                else if (edge == 1) c = x == 1 || y == h - 2 ? H("#6E7C80") : x == w - 2 || y == 1 ? H("#2C3538") : H("#4A585C");
                px[y * w + x] = c;
            }

            // Amber corner brackets over the frame.
            var amber = H("#C9902E");
            foreach (var (cx, cy, sx, sy) in new[] { (0, 0, 1, 1), (w - 1, 0, -1, 1), (0, h - 1, 1, -1), (w - 1, h - 1, -1, -1) })
                for (var i = 0; i < 6; i++)
                {
                    px[cy * w + cx + i * sx] = amber;
                    px[(cy + i * sy) * w + cx] = amber;
                    if (i < 5) { px[(cy + sy) * w + cx + i * sx] = H("#7A5418"); px[(cy + i * sy) * w + cx + sx] = H("#7A5418"); }
                }

            return _panel = Make(px, w, h, "MinimapPanel");
        }

        private static Sprite Cell(string name, Color32 fill, Color32 top, Color32 bottom, Color32 outline, bool dither)
        {
            var w = (int)MinimapLayout.CellSize.x;
            var h = (int)MinimapLayout.CellSize.y;
            var px = new Color32[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var border = x == 0 || y == 0 || x == w - 1 || y == h - 1;
                Color32 c;
                if (border) c = dither && ((x + y) & 1) == 1 ? new Color32(0, 0, 0, 0) : outline;
                else if (dither) c = ((x + y) & 1) == 0 ? fill : new Color32(fill.r, fill.g, fill.b, 90);
                else c = y == h - 2 ? top : y == 1 ? bottom : fill;
                px[y * w + x] = c;
            }

            return Make(px, w, h, name);
        }

        /// <summary>Discovered but never entered: a dithered, dashed shape — known to exist, not yet seen.</summary>
        public static Sprite Unvisited => _unvisited ??= Cell("MinimapUnvisited", H("#2C383C"), default, default, H("#5A6A70"), true);
        /// <summary>Entered: solid bevelled steel.</summary>
        public static Sprite Visited => _visited ??= Cell("MinimapVisited", H("#3C4A4F"), H("#56686E"), H("#2A3438"), H("#7A8C92"), false);
        /// <summary>The room the player is in: bevelled amber, the brightest block on the map.</summary>
        public static Sprite Current => _current ??= Cell("MinimapCurrent", H("#E0A23A"), H("#FFD27A"), H("#A86E1E"), H("#FFE6A8"), false);

        /// <summary>A one-pixel amber ring two pixels outside the current room, for the slow pulse.</summary>
        public static Sprite Halo
        {
            get
            {
                if (_halo != null) return _halo;
                var w = (int)MinimapLayout.CellSize.x + 4;
                var h = (int)MinimapLayout.CellSize.y + 4;
                var px = new Color32[w * h];
                for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var ring = x == 0 || y == 0 || x == w - 1 || y == h - 1;
                    var corner = (x == 0 || x == w - 1) && (y == 0 || y == h - 1);
                    px[y * w + x] = ring && !corner ? H("#FFC860") : new Color32(0, 0, 0, 0);
                }

                return _halo = Make(px, w, h, "MinimapHalo");
            }
        }

        /// <summary>The player arrow for one of eight facings (0 = east, counter-clockwise): white body, dark outline.</summary>
        public static Sprite Arrow(int octant)
        {
            octant = ((octant % 8) + 8) % 8;
            if (Arrows[octant] != null) return Arrows[octant];
            const int s = 7;
            var a = octant * Mathf.PI / 4f;
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            var perp = new Vector2(-dir.y, dir.x);
            var c = new Vector2(3f, 3f);
            var tip = c + dir * 2.6f;
            var left = c - dir * 1.8f + perp * 2.1f;
            var right = c - dir * 1.8f - perp * 2.1f;
            bool Inside(Vector2 p)
            {
                float Cross(Vector2 o, Vector2 u, Vector2 v) => (u.x - o.x) * (v.y - o.y) - (u.y - o.y) * (v.x - o.x);
                var d1 = Cross(tip, left, p);
                var d2 = Cross(left, right, p);
                var d3 = Cross(right, tip, p);
                return !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
            }

            var body = new bool[s * s];
            for (var y = 0; y < s; y++)
            for (var x = 0; x < s; x++)
                body[y * s + x] = Inside(new Vector2(x, y)) && x > 0 && y > 0 && x < s - 1 && y < s - 1;
            var px = new Color32[s * s];
            for (var y = 0; y < s; y++)
            for (var x = 0; x < s; x++)
            {
                if (body[y * s + x]) { px[y * s + x] = H("#FFFFFF"); continue; }
                var near = false;
                for (var dy = -1; dy <= 1 && !near; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    if (nx >= 0 && ny >= 0 && nx < s && ny < s && body[ny * s + nx]) { near = true; break; }
                }

                px[y * s + x] = near ? H("#0A0D0E") : new Color32(0, 0, 0, 0);
            }

            return Arrows[octant] = Make(px, s, s, "MinimapArrow" + octant);
        }
    }

    /// <summary>
    /// The top-left minimap (ui/91): a room-graph map of the current depth drawn from the real dungeon layout, not a
    /// camera render and not a world texture. Discovered rooms are blocks, real door connections are the corridors
    /// between them, the room the player is in is the bright one, and a special room shows its symbol only once it
    /// has been entered.
    ///
    /// Presentation: an opaque dark tactical panel with a faint grid and a bevelled steel frame, so the world never
    /// shows through the map; three room states that differ in form as well as value — a dithered dashed outline
    /// (known, never entered), bevelled steel (entered), bevelled amber with a slow pulsing halo (here); corridors as
    /// a lit line on a dark underlay; and the player as a white arrow pointing where they aim, at their place in the
    /// current room. Everything is small sprites at 1:1 (<see cref="MinimapArt"/>); the only motion is the halo.
    ///
    /// It is pure presentation: it renders a <see cref="MinimapModel"/>, never detects room entry and never consumes
    /// gameplay mouse input (every graphic has raycastTarget off).
    /// </summary>
    public sealed class HudMinimapView : MonoBehaviour
    {
        public const int Width = 100;
        public const int Height = 76;
        /// <summary>Frame inset: the bevelled border plus its inner shadow.</summary>
        public const int Inset = 4;
        public const int MaxCells = 20;
        public const int MaxLinks = 28;

        private MinimapModel _model;
        private RectTransform _content;
        private Text _depthChip;
        private Image _depthChipBack;
        private Image _depthChipRule;
        private readonly List<MinimapCellView> _cells = new();
        private readonly List<Image> _linkParts = new();
        private readonly List<Image> _linkUnder = new();
        private Image _halo;
        private Image _arrow;
        private MinimapCellView _currentCell;
        private Vector2 _currentSize;

        public MinimapModel Model => _model;
        public int Renders { get; private set; }
        /// <summary>The node ids that are actually drawn this frame (the discovered set, or its readable neighbourhood).</summary>
        public IReadOnlyList<int> DrawnNodeIds { get; private set; } = System.Array.Empty<int>();
        /// <summary>The node ids whose special symbol is visible this frame.</summary>
        public IReadOnlyList<int> DrawnMarkerNodeIds { get; private set; } = System.Array.Empty<int>();
        /// <summary>How many connector segments are drawn (two per visible link at most).</summary>
        public int DrawnLinkParts { get; private set; }
        /// <summary>True while the player arrow is shown in the current room.</summary>
        public bool PlayerMarkerVisible => _arrow != null && _arrow.enabled;
        /// <summary>The facing octant the arrow shows (0 = east, counter-clockwise).</summary>
        public int PlayerOctant { get; private set; }
        public string DepthText => _depthChip != null ? _depthChip.text : string.Empty;
        public RectTransform Rect => (RectTransform)transform;
        public Vector2 Inner => new(Width - Inset * 2, Height - Inset * 2);

        public static HudMinimapView Create(Transform parent, UiRect bounds, string name = "Minimap")
        {
            var rect = UiBuild.NewRect(parent, name, bounds);
            var view = rect.gameObject.AddComponent<HudMinimapView>();
            view.Build(bounds.Width, bounds.Height);
            return view;
        }

        private static Image Sprite(Transform parent, UiRect bounds, Sprite sprite, string name)
        {
            var image = UiBuild.Plate(parent, bounds, Color.white, name);
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.raycastTarget = false;
            return image;
        }

        private void Build(int width, int height)
        {
            var inner = new UiRect(0, 0, width, height);
            Sprite(transform, inner, MinimapArt.Panel(width, height), "Panel");

            var contentRect = UiBuild.NewRect(transform, "Content", new UiRect(Inset, Inset, width - Inset * 2, height - Inset * 2));
            _content = contentRect;

            for (var i = 0; i < MaxLinks * 2; i++)
            {
                var under = UiBuild.Plate(_content, new UiRect(0, 0, 1, 1), UiTheme.Hex("#05080A"), "LinkUnder" + i);
                under.enabled = false;
                _linkUnder.Add(under);
            }

            for (var i = 0; i < MaxLinks * 2; i++)
            {
                var link = UiBuild.Plate(_content, new UiRect(0, 0, 1, 1), UiTheme.PanelEdge, "Link" + i);
                link.enabled = false;
                _linkParts.Add(link);
            }

            var cw = (int)MinimapLayout.CellSize.x;
            var ch = (int)MinimapLayout.CellSize.y;
            _halo = Sprite(_content, new UiRect(0, 0, cw + 4, ch + 4), MinimapArt.Halo, "CurrentHalo");
            _halo.enabled = false;
            for (var i = 0; i < MaxCells; i++)
            {
                var cellRect = UiBuild.NewRect(_content, "Cell" + i, new UiRect(0, 0, cw, ch));
                var cell = new MinimapCellView { Rect = cellRect };
                cell.Fill = Sprite(cellRect, new UiRect(0, 0, cw, ch), MinimapArt.Visited, "Fill");
                for (var p = 0; p < MinimapSymbols.MaxParts; p++)
                {
                    var part = UiBuild.Plate(cellRect, new UiRect(0, 0, 1, 1), UiTheme.Amber, "Symbol" + p);
                    part.enabled = false;
                    cell.Parts.Add(part);
                }

                cellRect.gameObject.SetActive(false);
                _cells.Add(cell);
            }

            _arrow = Sprite(_content, new UiRect(0, 0, 7, 7), MinimapArt.Arrow(0), "PlayerArrow");
            _arrow.enabled = false;

            // Depth chip in the panel's top-right corner: a small tab with an amber rule, never an objective block.
            var chip = new UiRect(width - Inset - 15, Inset, 15, UiText.LineHeight);
            _depthChipBack = UiBuild.Plate(transform, chip, UiTheme.Hex("#0A0F11"), "DepthChipBack");
            _depthChipRule = UiBuild.Plate(transform, new UiRect(chip.X, chip.Y + chip.Height, chip.Width, 1), UiTheme.Hex("#C9902E"), "DepthChipRule");
            _depthChip = UiBuild.Label(transform, string.Empty, chip, 1, TextAnchor.UpperRight, UiTheme.Amber, false, "DepthChip");
        }

        public void Bind(MinimapModel model)
        {
            if (_model != null) _model.Changed -= Render;
            _model = model;
            if (_model != null) _model.Changed += Render;
            Render();
        }

        private void OnDestroy()
        {
            if (_model != null) _model.Changed -= Render;
        }

        public void Render()
        {
            Renders++;
            foreach (var link in _linkParts) link.enabled = false;
            foreach (var link in _linkUnder) link.enabled = false;
            foreach (var cell in _cells) cell.Rect.gameObject.SetActive(false);
            _currentCell = null;
            DrawnLinkParts = 0;
            DrawnNodeIds = System.Array.Empty<int>();
            DrawnMarkerNodeIds = System.Array.Empty<int>();
            if (_model == null)
            {
                if (_depthChip != null) { _depthChip.text = string.Empty; _depthChipBack.enabled = false; _depthChipRule.enabled = false; }
                SyncPlayer();
                return;
            }

            _depthChip.text = _model.Depth > 0 ? "D" + _model.Depth : string.Empty;
            _depthChipBack.enabled = _depthChipRule.enabled = _model.Depth > 0;

            var rooms = MinimapLayout.VisibleRooms(_model, Inner);
            if (rooms.Count > MaxCells)
            {
                // Never draw more blocks than the panel owns: keep the current room and its nearest neighbours.
                var current = _model.Current;
                var anchor = current != null ? current.Center : Vector2.zero;
                rooms = rooms.OrderBy(r => (r.Center - anchor).sqrMagnitude).Take(MaxCells).ToList();
            }

            var placed = MinimapLayout.Place(rooms, Inner);
            var centres = new Dictionary<int, Vector2>();
            for (var i = 0; i < placed.Count; i++) centres[placed[i].NodeId] = placed[i].Center;

            // Corridors first, so the room blocks sit on top of their own doors: a dark underlay, then the lit line —
            // bright where both ends have been entered, dim toward a room not yet seen.
            var linkIndex = 0;
            foreach (var (a, b) in _model.DiscoveredLinks)
            {
                if (!centres.TryGetValue(a, out var from) || !centres.TryGetValue(b, out var to)) continue;
                if (linkIndex + 2 > _linkParts.Count) break;
                var walked = (_model.Room(a)?.Visited ?? false) && (_model.Room(b)?.Visited ?? false);
                var tone = walked ? UiTheme.Hex("#8A9CA2") : UiTheme.Hex("#46565C");
                // An L connector: one horizontal and one vertical segment, so every door line stays on whole pixels.
                Segment(_linkUnder[linkIndex], _linkParts[linkIndex++], from.x, to.x, from.y, true, tone);
                Segment(_linkUnder[linkIndex], _linkParts[linkIndex++], from.y, to.y, to.x, false, tone);
            }

            DrawnLinkParts = linkIndex;

            var drawn = new List<int>();
            var markers = new List<int>();
            for (var i = 0; i < placed.Count && i < _cells.Count; i++)
            {
                var cell = _cells[i];
                var room = _model.Room(placed[i].NodeId);
                if (room == null) continue;
                var centre = placed[i].Center;
                var size = placed[i].Size;
                cell.Rect.anchoredPosition = new Vector2(Mathf.Round(centre.x - size.x / 2f), -Mathf.Round(centre.y - size.y / 2f));
                cell.Rect.sizeDelta = size;
                cell.Rect.gameObject.SetActive(true);
                drawn.Add(room.NodeId);

                var isCurrent = _model.CurrentNodeId == room.NodeId;
                cell.Fill.sprite = isCurrent ? MinimapArt.Current : room.Visited ? MinimapArt.Visited : MinimapArt.Unvisited;
                if (isCurrent) { _currentCell = cell; _currentSize = size; }

                var parts = room.ShowsMarker ? MinimapSymbols.For(room.Kind) : System.Array.Empty<(UiRect, Color)>();
                if (parts.Count > 0) markers.Add(room.NodeId);
                for (var p = 0; p < cell.Parts.Count; p++)
                {
                    var image = cell.Parts[p];
                    if (p >= parts.Count) { image.enabled = false; continue; }
                    var (rect, color) = parts[p];
                    var ox = Mathf.RoundToInt((size.x - MinimapSymbols.Size) / 2f);
                    var oy = Mathf.RoundToInt((size.y - MinimapSymbols.Size) / 2f);
                    var imageRect = (RectTransform)image.transform;
                    imageRect.anchoredPosition = new Vector2(ox + rect.X, -(oy + rect.Y));
                    imageRect.sizeDelta = new Vector2(rect.Width, rect.Height);
                    // The current room is amber: its symbol switches to the dark ink so it stays legible on it.
                    image.color = isCurrent ? UiTheme.NearBlack : color;
                    image.enabled = true;
                }
            }

            DrawnNodeIds = drawn;
            DrawnMarkerNodeIds = markers;
            SyncPlayer();
        }

        private void LateUpdate() => SyncPlayer();

        /// <summary>The live layer: the current room's slow halo pulse and the player arrow at its place in the room.</summary>
        private void SyncPlayer()
        {
            if (_halo == null) return;
            var cell = _currentCell;
            if (cell == null || !cell.Rect.gameObject.activeSelf)
            {
                _halo.enabled = false;
                _arrow.enabled = false;
                return;
            }

            var origin = cell.Rect.anchoredPosition;
            var haloRect = (RectTransform)_halo.transform;
            haloRect.anchoredPosition = origin + new Vector2(-2f, 2f);
            _halo.enabled = true;
            var pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.6f);
            _halo.color = new Color(1f, 1f, 1f, 0.25f + 0.45f * pulse);

            if (_model == null || !_model.HasPlayer)
            {
                _arrow.enabled = false;
                return;
            }

            var facing = _model.PlayerFacing;
            var octant = Mathf.RoundToInt(Mathf.Atan2(facing.y, facing.x) / (Mathf.PI / 4f));
            octant = ((octant % 8) + 8) % 8;
            if (octant != PlayerOctant || _arrow.sprite == null) _arrow.sprite = MinimapArt.Arrow(octant);
            PlayerOctant = octant;
            // The arrow's centre at the player's place in the room (room y-up; UI y-down), kept on whole pixels.
            var local = _model.PlayerInRoom;
            var cx = Mathf.Round(origin.x + 1f + local.x * (_currentSize.x - 2f));
            var cy = Mathf.Round(-origin.y + 1f + (1f - local.y) * (_currentSize.y - 2f));
            var arrowRect = (RectTransform)_arrow.transform;
            arrowRect.anchoredPosition = new Vector2(cx - 3f, -(cy - 3f));
            _arrow.enabled = true;
            // The arrow replaces the current room's symbol (the room title already names it); everywhere else stays.
            foreach (var part in cell.Parts) part.enabled = false;
        }

        private static void Segment(Image under, Image line, float a, float b, float fixedAxis, bool horizontal, Color tone)
        {
            var min = Mathf.Round(Mathf.Min(a, b));
            var max = Mathf.Round(Mathf.Max(a, b));
            var length = Mathf.Max(1f, max - min);
            var rect = (RectTransform)line.transform;
            var u = (RectTransform)under.transform;
            if (horizontal)
            {
                rect.anchoredPosition = new Vector2(min, -Mathf.Round(fixedAxis));
                rect.sizeDelta = new Vector2(length, 1f);
                u.anchoredPosition = new Vector2(min - 1f, -Mathf.Round(fixedAxis) + 1f);
                u.sizeDelta = new Vector2(length + 2f, 3f);
            }
            else
            {
                rect.anchoredPosition = new Vector2(Mathf.Round(fixedAxis), -min);
                rect.sizeDelta = new Vector2(1f, length);
                u.anchoredPosition = new Vector2(Mathf.Round(fixedAxis) - 1f, -min + 1f);
                u.sizeDelta = new Vector2(3f, length + 2f);
            }

            line.color = tone;
            line.enabled = true;
            under.enabled = true;
        }
    }
}

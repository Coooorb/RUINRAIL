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
        public Image Outline0, Outline1, Outline2, Outline3;
        public readonly List<Image> Parts = new();
    }

    /// <summary>
    /// The top-left minimap (ui/91): a room-graph map of the current depth drawn from the real dungeon layout, not a
    /// camera render and not a world texture. Discovered rooms are blocks, real door connections are the lines
    /// between them, the room the player is in is the bright one, and a special room shows its symbol only once it
    /// has been entered.
    ///
    /// It is pure presentation: it renders a <see cref="MinimapModel"/>, never detects room entry and never consumes
    /// gameplay mouse input (every graphic has raycastTarget off).
    /// </summary>
    public sealed class HudMinimapView : MonoBehaviour
    {
        public const int Width = 100;
        public const int Height = 76;
        /// <summary>Frame inset: the panel border plus one pixel of air.</summary>
        public const int Inset = 3;
        public const int MaxCells = 20;
        public const int MaxLinks = 28;

        private MinimapModel _model;
        private RectTransform _content;
        private Text _depthChip;
        private Image _depthChipBack;
        private readonly List<MinimapCellView> _cells = new();
        private readonly List<Image> _linkParts = new();

        public MinimapModel Model => _model;
        public int Renders { get; private set; }
        /// <summary>The node ids that are actually drawn this frame (the discovered set, or its readable neighbourhood).</summary>
        public IReadOnlyList<int> DrawnNodeIds { get; private set; } = System.Array.Empty<int>();
        /// <summary>The node ids whose special symbol is visible this frame.</summary>
        public IReadOnlyList<int> DrawnMarkerNodeIds { get; private set; } = System.Array.Empty<int>();
        /// <summary>How many connector segments are drawn (two per visible link at most).</summary>
        public int DrawnLinkParts { get; private set; }
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

        private void Build(int width, int height)
        {
            var inner = new UiRect(0, 0, width, height);
            // Dark panel with a thin charcoal/steel frame and amber corner ticks: the HUD's own chrome (spec 18).
            UiBuild.Plate(transform, inner, UiTheme.WithAlpha(UiTheme.NearBlack, 0.82f), "Backdrop");
            UiBuild.Border(transform, inner, UiTheme.PanelEdge);
            foreach (var bracket in UiBuild.Brackets(transform, inner, UiTheme.AmberDim)) bracket.enabled = true;

            var contentRect = UiBuild.NewRect(transform, "Content", new UiRect(Inset, Inset, width - Inset * 2, height - Inset * 2));
            _content = contentRect;

            for (var i = 0; i < MaxLinks * 2; i++)
            {
                var link = UiBuild.Plate(_content, new UiRect(0, 0, 1, 1), UiTheme.PanelEdge, "Link" + i);
                link.enabled = false;
                _linkParts.Add(link);
            }

            for (var i = 0; i < MaxCells; i++)
            {
                var cellRect = UiBuild.NewRect(_content, "Cell" + i, new UiRect(0, 0, (int)MinimapLayout.CellSize.x, (int)MinimapLayout.CellSize.y));
                var cell = new MinimapCellView { Rect = cellRect };
                cell.Fill = UiBuild.Plate(cellRect, new UiRect(0, 0, (int)MinimapLayout.CellSize.x, (int)MinimapLayout.CellSize.y), UiTheme.Charcoal, "Fill");
                var edges = UiBuild.Border(cellRect, new UiRect(0, 0, (int)MinimapLayout.CellSize.x, (int)MinimapLayout.CellSize.y), UiTheme.PanelEdgeSoft);
                cell.Outline0 = edges[0]; cell.Outline1 = edges[1]; cell.Outline2 = edges[2]; cell.Outline3 = edges[3];
                for (var p = 0; p < MinimapSymbols.MaxParts; p++)
                {
                    var part = UiBuild.Plate(cellRect, new UiRect(0, 0, 1, 1), UiTheme.Amber, "Symbol" + p);
                    part.enabled = false;
                    cell.Parts.Add(part);
                }

                cellRect.gameObject.SetActive(false);
                _cells.Add(cell);
            }

            // Depth chip inside the panel's top-right corner: lightweight, never a multi-line objective block.
            var chip = new UiRect(width - Inset - 16, Inset, 16, UiText.LineHeight);
            _depthChipBack = UiBuild.Plate(transform, chip, UiTheme.WithAlpha(UiTheme.NearBlack, 0.85f), "DepthChipBack");
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
            foreach (var cell in _cells) cell.Rect.gameObject.SetActive(false);
            DrawnLinkParts = 0;
            DrawnNodeIds = System.Array.Empty<int>();
            DrawnMarkerNodeIds = System.Array.Empty<int>();
            if (_model == null)
            {
                if (_depthChip != null) { _depthChip.text = string.Empty; _depthChipBack.enabled = false; }
                return;
            }

            _depthChip.text = _model.Depth > 0 ? "D" + _model.Depth : string.Empty;
            _depthChipBack.enabled = _model.Depth > 0;

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

            // Connections first, so the room blocks sit on top of their own doors.
            var linkIndex = 0;
            foreach (var (a, b) in _model.DiscoveredLinks)
            {
                if (!centres.TryGetValue(a, out var from) || !centres.TryGetValue(b, out var to)) continue;
                if (linkIndex + 2 > _linkParts.Count) break;
                // An L connector: one horizontal and one vertical segment, so every door line stays on whole pixels.
                Segment(_linkParts[linkIndex++], from.x, to.x, from.y, horizontal: true);
                Segment(_linkParts[linkIndex++], from.y, to.y, to.x, horizontal: false);
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
                cell.Fill.color = isCurrent ? UiTheme.Amber
                    : room.Visited ? UiTheme.Hex("#39464A")
                    : UiTheme.WithAlpha(UiTheme.Charcoal, 0.75f);
                var edge = isCurrent ? UiTheme.Lighten(UiTheme.Amber, 0.4f)
                    : room.Visited ? UiTheme.PanelEdge
                    : UiTheme.PanelEdgeSoft;
                cell.Outline0.color = cell.Outline1.color = cell.Outline2.color = cell.Outline3.color = edge;

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
        }

        private static void Segment(Image image, float a, float b, float fixedAxis, bool horizontal)
        {
            var min = Mathf.Round(Mathf.Min(a, b));
            var max = Mathf.Round(Mathf.Max(a, b));
            var length = Mathf.Max(1f, max - min);
            var rect = (RectTransform)image.transform;
            if (horizontal)
            {
                rect.anchoredPosition = new Vector2(min, -Mathf.Round(fixedAxis));
                rect.sizeDelta = new Vector2(length, 1f);
            }
            else
            {
                rect.anchoredPosition = new Vector2(Mathf.Round(fixedAxis), -min);
                rect.sizeDelta = new Vector2(1f, length);
            }

            image.enabled = true;
        }
    }
}

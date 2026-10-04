using System.Collections.Generic;
using RuinRail.Core;
using RuinRail.Core.Rendering;
using RuinRail.Core.Rng;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuinRail.Dungeon.Runtime
{
    /// <summary>
    /// The environment-art layer of one room: material wear, contact shadows, dirty edges, biome markings, wall-side
    /// clutter, room-category storytelling and lamp pools, painted at runtime into two textures that sit exactly on
    /// the room's pixel grid.
    ///
    /// The shipped rooms are baked tilemaps whose floor tiles all carry the same 1–2 px seam (a graph-paper grid),
    /// whose walls repeat one tile, and whose prop layout is mirrored; this layer is what makes a room read as a used,
    /// damaged former space instead of an arena. It reuses the existing dressing seam
    /// (<see cref="DungeonRoomRuntimeComposer.Dress"/>) and is presentation only, by construction:
    /// <list type="bullet">
    /// <item>it adds two SpriteRenderers and nothing else — no collider, no tile, no tilemap write, no marker, no
    /// occupancy — so navigation, sealing, spawns, encounters, hazards and the minimap cannot see it;</item>
    /// <item>the floor texture renders on <see cref="SortingLayers.Ground"/> at order <see cref="FloorSortingOrder"/>:
    /// above the floor tiles, below FloorDetail, hazards and everything that moves; the wall texture renders on
    /// <see cref="SortingLayers.LowProps"/> at <see cref="WallSortingOrder"/> and is only ever painted on wall cells,
    /// so it can never cover a character, projectile, telegraph, loot or effect — no new sorting layer, no light;</item>
    /// <item>clutter (the only opaque pieces) goes only on the wall band (<see cref="ClutterMaxWallDistance"/> steps
    /// from a wall) — along walls, in corners and beside the props that stand there — never on door clearance, a marker
    /// or its neighbours, a hazard, a FloorDetail prop or an AbovePlayer cell; the central combat space (cells at least
    /// <see cref="CoreDistance"/> from a wall), including the cover standing in it, only receives flat, low-alpha
    /// wear and grime;</item>
    /// <item>nothing is drawn on a hazard cell, so hazard footprints keep their full contrast.</item>
    /// </list>
    ///
    /// Determinism: every decision comes from (run seed, depth, node id) on the Biome stream plus integer hash noise,
    /// so the same room of the same depth paints identically on every peer and every rebuild. The seam repair copies
    /// the room's own floor-tile pixels (GPU read-back of the tile textures, cached once per texture); without a
    /// graphics device (headless) it is skipped, which changes nothing anyone can see.
    /// </summary>
    public static class RoomEnvironmentDressing
    {
        public const int FloorSortingOrder = 1;
        public const int WallSortingOrder = 20;
        /// <summary>Cells at least this many steps from a wall are the room's central combat space.</summary>
        public const int CoreDistance = 3;
        /// <summary>Clutter only on cells this close to a wall.</summary>
        public const int ClutterMaxWallDistance = 2;
        /// <summary>Alpha scale for every flat decal inside the central combat space.</summary>
        public const float CoreAlphaScale = 0.7f;
        public const int DoorClearanceTiles = 2;
        private const int Salt = 0x454E56; // "ENV"

        public readonly struct Result
        {
            public Result(ulong floorPrint, ulong wallPrint, int mergedCells, int clutter, int glows, string vignette, int floorPixels, int wallPixels, bool seamsRepaired)
            {
                FloorFingerprint = floorPrint;
                WallFingerprint = wallPrint;
                MergedCells = mergedCells;
                Clutter = clutter;
                Glows = glows;
                Vignette = vignette;
                FloorPixels = floorPixels;
                WallPixels = wallPixels;
                SeamsRepaired = seamsRepaired;
            }

            public ulong FloorFingerprint { get; }
            public ulong WallFingerprint { get; }
            /// <summary>Floor cells joined into a larger slab (their shared seams removed).</summary>
            public int MergedCells { get; }
            public int Clutter { get; }
            public int Glows { get; }
            /// <summary>The room-category story the room was dressed with (empty when none fit).</summary>
            public string Vignette { get; }
            public int FloorPixels { get; }
            public int WallPixels { get; }
            public bool SeamsRepaired { get; }
        }

        /// <summary>Paints and attaches the layer. Safe on a room without a Floor layer: it does nothing.</summary>
        public static Result Apply(RoomRoot root, int runSeed, int depth, int nodeId)
        {
            if (root == null || root.Grid == null || root.Definition == null) return default;
            var map = RoomEnvironmentMap.Build(root);
            if (map == null) return default;

            var rng = new SeededRandom(SeededRandom.MixSeed(runSeed, depth, nodeId, (int)RngStream.Biome, Salt));
            var style = RoomEnvironmentStyle.For(root.Definition.Biome);
            var floor = new RoomEnvironmentCanvas(map.W * RoomEnvironmentMap.Tile, map.H * RoomEnvironmentMap.Tile);
            var walls = new RoomEnvironmentCanvas(floor.Width, floor.Height);
            var paint = new RoomEnvironmentPainter(map, style, rng, floor, walls);
            paint.PaintAll();

            var layer = new GameObject("EnvironmentLayer").AddComponent<RoomEnvironmentLayer>();
            layer.transform.SetParent(root.transform, false);
            layer.transform.position = map.Origin;
            layer.Build(floor, map.FloorRenderer, walls, map.WallRenderer);

            var result = new Result(floor.Fingerprint(), walls.Fingerprint(), paint.MergedCells, paint.ClutterPlaced, paint.GlowsPlaced, paint.Vignette,
                floor.CountTouched(), walls.CountTouched(), paint.SeamsRepaired);
            layer.Result = result;
            layer.ClutterCells = paint.ClutterCells;
            return result;
        }
    }

    /// <summary>Owns one room's environment textures and releases them with the room.</summary>
    [DisallowMultipleComponent]
    public sealed class RoomEnvironmentLayer : MonoBehaviour
    {
        private readonly List<Object> _owned = new();

        public RoomEnvironmentDressing.Result Result { get; internal set; }
        public SpriteRenderer Floor { get; private set; }
        public SpriteRenderer Walls { get; private set; }
        /// <summary>Every cell a clutter piece touches (validators/tests check them against the clutter rules).</summary>
        public IReadOnlyCollection<Vector2Int> ClutterCells { get; internal set; } = System.Array.Empty<Vector2Int>();

        internal void Build(RoomEnvironmentCanvas floor, Renderer floorSource, RoomEnvironmentCanvas walls, Renderer wallSource)
        {
            Floor = Make("Floor", floor, SortingLayers.Ground, RoomEnvironmentDressing.FloorSortingOrder, floorSource);
            Walls = Make("Walls", walls, SortingLayers.LowProps, RoomEnvironmentDressing.WallSortingOrder, wallSource != null ? wallSource : floorSource);
        }

        private SpriteRenderer Make(string name, RoomEnvironmentCanvas canvas, string layer, int order, Renderer source)
        {
            var texture = new Texture2D(canvas.Width, canvas.Height, TextureFormat.RGBA32, false, false)
            {
                name = "RoomEnvironment_" + name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(canvas.Pixels);
            texture.Apply(false, true); // upload and drop the CPU copy
            var sprite = Sprite.Create(texture, new Rect(0, 0, canvas.Width, canvas.Height), Vector2.zero, GridConstants.PixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = texture.name;
            _owned.Add(sprite);
            _owned.Add(texture);

            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingLayerName = layer;
            renderer.sortingOrder = order;
            if (source != null && source.sharedMaterial != null) renderer.sharedMaterial = source.sharedMaterial;
            return renderer;
        }

        private void OnDestroy()
        {
            foreach (var owned in _owned)
            {
                if (owned == null) continue;
                if (Application.isPlaying) Destroy(owned);
                else DestroyImmediate(owned);
            }

            _owned.Clear();
        }
    }

    /// <summary>What the painters need to know about a room, per cell.</summary>
    internal sealed class RoomEnvironmentMap
    {
        public const int Tile = GridConstants.TileSizePixels;

        public int W, H;
        public RoomType Type;
        public Vector3 Origin;
        public Renderer FloorRenderer, WallRenderer;
        public bool[] Floor, Wall, Obstacle, Hazard, Above, Detail, Door, MarkerZone, NearSocket;
        public int[] Dist;
        /// <summary>Per cell: which of the eight neighbours are solid (wall / no floor / outside), as N..SW bits.</summary>
        public int[] Around;
        /// <summary>Per cell: which of the four orthogonal neighbours are obstacles.</summary>
        public int[] ObstacleAround;
        public const int BitN = 1, BitE = 2, BitS = 4, BitW = 8, BitNE = 16, BitNW = 32, BitSE = 64, BitSW = 128, Corners = BitNE | BitNW | BitSE | BitSW;
        public Sprite[] FloorSprite;
        public bool[] FloorIdentity;
        public readonly List<RoomMarker> Markers = new();
        public readonly List<DoorSocket> Sockets = new();

        public int I(int x, int y) => y * W + x;
        public bool InRoom(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;
        public bool Open(int x, int y) => InRoom(x, y) && Floor[I(x, y)] && !Wall[I(x, y)] && !Obstacle[I(x, y)];
        public bool Solid(int x, int y) => !InRoom(x, y) || !Floor[I(x, y)] || Wall[I(x, y)];
        public bool IsObstacle(int x, int y) => InRoom(x, y) && Obstacle[I(x, y)];
        public bool IsHazard(int x, int y) => InRoom(x, y) && Hazard[I(x, y)];
        public int DistAt(int x, int y) => InRoom(x, y) ? Dist[I(x, y)] : 0;
        public bool Core(int x, int y) => Open(x, y) && Dist[I(x, y)] >= RoomEnvironmentDressing.CoreDistance;

        /// <summary>Open floor a flat decal may touch: not a hazard.</summary>
        public bool Decal(int x, int y) => Open(x, y) && !Hazard[I(x, y)];

        public bool ObstacleAdjacent(int x, int y) =>
            IsObstacle(x + 1, y) || IsObstacle(x - 1, y) || IsObstacle(x, y + 1) || IsObstacle(x, y - 1);

        /// <summary>A cell an opaque clutter piece may touch.</summary>
        public bool Clutter(int x, int y)
        {
            if (!Open(x, y)) return false;
            var i = I(x, y);
            if (Hazard[i] || Above[i] || Detail[i] || Door[i] || MarkerZone[i]) return false;
            return Dist[i] <= RoomEnvironmentDressing.ClutterMaxWallDistance;
        }

        /// <summary>A wall cell the wall texture may paint (door frames stay clean).</summary>
        public bool WallPaint(int x, int y) => InRoom(x, y) && Wall[I(x, y)] && !NearSocket[I(x, y)];

        public static RoomEnvironmentMap Build(RoomRoot root)
        {
            var grid = root.Grid;
            var floor = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Floor);
            if (floor == null) return null;
            var walls = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Walls);
            var obstacles = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Obstacles);
            var hazards = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Hazards);
            var above = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.AbovePlayer);
            var detail = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.FloorDetail);

            var map = new RoomEnvironmentMap
            {
                W = Mathf.Max(1, root.Size.x),
                H = Mathf.Max(1, root.Size.y),
                Type = root.Definition.RoomType,
                Origin = floor.CellToWorld(Vector3Int.zero),
                FloorRenderer = floor.GetComponent<TilemapRenderer>(),
                WallRenderer = walls != null ? walls.GetComponent<TilemapRenderer>() : null
            };
            var n = map.W * map.H;
            map.Floor = new bool[n]; map.Wall = new bool[n]; map.Obstacle = new bool[n]; map.Hazard = new bool[n];
            map.Above = new bool[n]; map.Detail = new bool[n]; map.Door = new bool[n]; map.MarkerZone = new bool[n];
            map.NearSocket = new bool[n]; map.Dist = new int[n]; map.FloorSprite = new Sprite[n]; map.FloorIdentity = new bool[n];

            for (var y = 0; y < map.H; y++)
            for (var x = 0; x < map.W; x++)
            {
                var cell = new Vector3Int(x, y, 0);
                var i = map.I(x, y);
                map.Floor[i] = floor.GetTile(cell) != null;
                map.FloorSprite[i] = map.Floor[i] ? floor.GetSprite(cell) : null;
                map.FloorIdentity[i] = floor.GetTransformMatrix(cell) == Matrix4x4.identity;
                map.Wall[i] = walls != null && walls.GetTile(cell) != null;
                map.Obstacle[i] = obstacles != null && obstacles.GetTile(cell) != null;
                map.Hazard[i] = hazards != null && hazards.GetTile(cell) != null;
                map.Above[i] = above != null && above.GetTile(cell) != null;
                map.Detail[i] = detail != null && detail.GetTile(cell) != null;
            }

            foreach (var socket in root.GetSockets())
            {
                if (socket == null) continue;
                map.Sockets.Add(socket);
                for (var w = -DoorReach; w <= socket.Width + DoorReach; w++)
                for (var d = -DoorReach; d <= DoorReach; d++)
                {
                    var c = DoorDirections.IsHorizontalEdge(socket.Direction)
                        ? new Vector2Int(socket.Cell.x + w, socket.Cell.y + d)
                        : new Vector2Int(socket.Cell.x + d, socket.Cell.y + w);
                    if (map.InRoom(c.x, c.y)) map.Door[map.I(c.x, c.y)] = true;
                }

                foreach (var c in socket.Cells())
                for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                    if (map.InRoom(c.x + dx, c.y + dy)) map.NearSocket[map.I(c.x + dx, c.y + dy)] = true;
            }

            foreach (var marker in root.GetMarkers())
            {
                if (marker == null) continue;
                map.Markers.Add(marker);
                var r = marker.Rect;
                for (var x = r.xMin - 1; x <= r.xMax; x++)
                for (var y = r.yMin - 1; y <= r.yMax; y++)
                    if (map.InRoom(x, y)) map.MarkerZone[map.I(x, y)] = true;
            }

            // Steps from the nearest wall, through open floor (obstacles are islands, not walls).
            var queue = new Queue<int>();
            for (var i = 0; i < n; i++) map.Dist[i] = int.MaxValue;
            for (var y = 0; y < map.H; y++)
            for (var x = 0; x < map.W; x++)
            {
                if (!map.Open(x, y)) { map.Dist[map.I(x, y)] = 0; continue; }
                if (map.Solid(x + 1, y) || map.Solid(x - 1, y) || map.Solid(x, y + 1) || map.Solid(x, y - 1))
                {
                    map.Dist[map.I(x, y)] = 1;
                    queue.Enqueue(map.I(x, y));
                }
            }

            while (queue.Count > 0)
            {
                var i = queue.Dequeue();
                int x = i % map.W, y = i / map.W;
                foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                {
                    if (!map.Open(nx, ny)) continue;
                    var j = map.I(nx, ny);
                    if (map.Dist[j] <= map.Dist[i] + 1) continue;
                    map.Dist[j] = map.Dist[i] + 1;
                    queue.Enqueue(j);
                }
            }

            for (var i = 0; i < n; i++) if (map.Dist[i] == int.MaxValue) map.Dist[i] = RoomEnvironmentDressing.CoreDistance + 4;
            map.Around = new int[n];
            map.ObstacleAround = new int[n];
            for (var y = 0; y < map.H; y++)
            for (var x = 0; x < map.W; x++)
            {
                var a = 0;
                if (map.Solid(x, y + 1)) a |= BitN;
                if (map.Solid(x + 1, y)) a |= BitE;
                if (map.Solid(x, y - 1)) a |= BitS;
                if (map.Solid(x - 1, y)) a |= BitW;
                if (map.Solid(x + 1, y + 1)) a |= BitNE;
                if (map.Solid(x - 1, y + 1)) a |= BitNW;
                if (map.Solid(x + 1, y - 1)) a |= BitSE;
                if (map.Solid(x - 1, y - 1)) a |= BitSW;
                map.Around[map.I(x, y)] = a;
                var o = 0;
                if (map.IsObstacle(x, y + 1)) o |= BitN;
                if (map.IsObstacle(x + 1, y)) o |= BitE;
                if (map.IsObstacle(x, y - 1)) o |= BitS;
                if (map.IsObstacle(x - 1, y)) o |= BitW;
                map.ObstacleAround[map.I(x, y)] = o;
            }
            return map;
        }

        private const int DoorReach = RoomEnvironmentDressing.DoorClearanceTiles;
    }
}

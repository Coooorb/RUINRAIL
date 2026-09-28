using System.Collections.Generic;
using RuinRail.Gameplay.Combat;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies
{
    /// <summary>
    /// The route layer the chase loops were missing. <see cref="ObstacleSteering"/> only looks 0.75 tiles ahead, so a
    /// pursuer whose straight line to its target runs into a pillar, a wall run or the inside of an L pressed into it
    /// and held there (by design: "a wall head-on to the goal is not slid along") or slid until the chosen side was
    /// blocked too — in the shipped rooms 64 of 66 blocked-but-reachable pursuits ended pinned 2-6 tiles short.
    ///
    /// While the straight line is clear this does nothing: the heading is the target, exactly as before. When solid
    /// geometry (<see cref="EnvironmentObstacle"/>, the same colliders the steering probes) blocks the line, it routes
    /// over a <see cref="PursuitGrid"/> of cells the body fits in, sampled from physics inside the actor's
    /// <see cref="EncounterBounds"/> legal area — so the route never leaves the room, never crosses a wall or door
    /// blocker, and a target nobody can reach (outside the room, behind a locked door) leads only to the reachable
    /// cell nearest it, which is where the old behaviour held too. The heading is the farthest route point in clear
    /// view; the steering still owns the final slide along any wall. Speeds, ranges and timing are untouched.
    /// </summary>
    public sealed class PursuitNavigator
    {
        public const float RepathSeconds = 0.4f;
        /// <summary>An unbound actor (no room) plans in a window around itself and its target, this far past both.</summary>
        public const float WindowMarginTiles = 6f;
        private const int LookAheadWaypoints = 8;
        private static readonly RaycastHit2D[] Hits = new RaycastHit2D[12];

        private readonly Transform _self;
        private readonly List<Vector2> _path = new();
        private int _next;
        private float _repathAt;
        private Vector2 _goal;
        private bool _hasPath;
        private readonly List<(Vector2 position, float radius)> _avoid = new();
        private float _avoidUntil;

        public PursuitNavigator(Transform self) => _self = self;

        /// <summary>How often a crowd asked for a way around it (diagnostics / tests).</summary>
        public int CrowdReroutes { get; private set; }
        public bool IsAvoidingBodies => _avoid.Count > 0 && Time.time < _avoidUntil;
        /// <summary>Whether the last plan around blocking bodies reached the target (false: the bodies hold the only way).</summary>
        public bool LastCrowdRerouteFound { get; private set; }

        /// <summary>
        /// Other enemies' bodies have held this actor up (<see cref="CrowdAvoidance"/>): for a short while, plan around
        /// them as if their cells were closed. When no other way reaches the target the ordinary route stands.
        /// </summary>
        public void AvoidBodies(IReadOnlyList<(Vector2 position, float radius)> bodies, float seconds)
        {
            if (bodies == null || bodies.Count == 0) return;
            _avoid.Clear();
            _avoid.AddRange(bodies);
            _avoidUntil = Time.time + seconds;
            _hasPath = false;
            CrowdReroutes++;
        }

        /// <summary>True while the straight line is blocked and the heading follows a route (diagnostics / tests).</summary>
        public bool IsDetouring { get; private set; }
        public int Repaths { get; private set; }
        /// <summary>The route in use (world points), empty while the line is clear.</summary>
        public IReadOnlyList<Vector2> Path => _path;

        /// <summary>Unit heading toward <paramref name="target"/> that goes around blocking geometry (zero when on it).</summary>
        public Vector2 Heading(Vector2 position, Vector2 target, float radius, EncounterBounds bounds)
        {
            var toTarget = target - position;
            if (toTarget.sqrMagnitude < 0.0001f) return Vector2.zero;
            // A target outside the actor's room (a player beyond an open doorway or a locked door) cannot be reached by
            // anything the room contains: no route, the straight heading, and the steering and the room edge hold the
            // actor there exactly as before. Only targets inside the room are routed to.
            var outsideRoom = bounds != null && bounds.IsBound && !bounds.Interior.Contains(target);
            var avoiding = IsAvoidingBodies;
            if (outsideRoom || (!avoiding && !Blocked(position, target, radius)))
            {
                IsDetouring = false;
                _hasPath = false;
                _path.Clear();
                return toTarget.normalized;
            }

            IsDetouring = true;
            if (!_hasPath || Time.time >= _repathAt || (target - _goal).sqrMagnitude > 1f) Repath(position, target, radius, bounds);
            if (_path.Count == 0) return toTarget.normalized; // no route at all: the steering holds at the wall, as before

            // Drop the points already reached, then aim at the farthest one still in clear view (only a step or two
            // ahead while going around bodies, which the view check cannot see).
            while (_next < _path.Count - 1 && (_path[_next] - position).sqrMagnitude < 0.3f * 0.3f) _next++;
            var aim = _path[_next];
            for (var i = Mathf.Min(_path.Count - 1, _next + (avoiding ? 2 : LookAheadWaypoints)); i > _next; i--)
            {
                if (Blocked(position, _path[i], radius)) continue;
                aim = _path[i];
                _next = i;
                break;
            }

            var heading = aim - position;
            return heading.sqrMagnitude < 0.0001f ? toTarget.normalized : heading.normalized;
        }

        private void Repath(Vector2 position, Vector2 target, float radius, EncounterBounds bounds)
        {
            Repaths++;
            _repathAt = Time.time + RepathSeconds;
            _goal = target;
            _hasPath = true;
            _next = 0;
            Rect area, legal;
            if (bounds != null && bounds.IsBound)
            {
                area = bounds.Interior;
                legal = bounds.Legal;
            }
            else
            {
                var min = Vector2.Min(position, target) - Vector2.one * WindowMarginTiles;
                var max = Vector2.Max(position, target) + Vector2.one * WindowMarginTiles;
                area = legal = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }

            var grid = PursuitGrid.For(area, legal, radius);
            // Around the bodies that held it up when that still reaches the target; otherwise the ordinary route.
            if (IsAvoidingBodies)
            {
                LastCrowdRerouteFound = grid.FindPath(position, target, legal, _path, _avoid, radius);
                if (LastCrowdRerouteFound) return;
            }

            grid.FindPath(position, target, legal, _path);
        }

        /// <summary>A solid obstacle between the body at <paramref name="from"/> and <paramref name="to"/>.</summary>
        private bool Blocked(Vector2 from, Vector2 to, float radius)
        {
            var delta = to - from;
            var distance = delta.magnitude;
            if (distance < 0.0001f) return false;
            var count = Physics2D.CircleCast(from, radius * 0.9f, delta / distance, Physics2DQueries.LegacyQueryFilter(), Hits, distance);
            for (var i = 0; i < count; i++)
            {
                var c = Hits[i].collider;
                if (c == null || c.isTrigger) continue;
                if (_self != null && (c.transform == _self || c.transform.IsChildOf(_self))) continue;
                if (c.GetComponentInParent<EnvironmentObstacle>() != null) return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Half-tile cells of one area where a body of a given radius fits: inside the legal rectangle and touching no solid
    /// <see cref="EnvironmentObstacle"/> collider. Sampled from physics (the one truth for walls, obstacles, sealed
    /// sockets and door blockers) and shared by every pursuer with the same area and body size for a short while — room
    /// geometry is static during an encounter, and the short lifetime picks up anything that does change.
    /// </summary>
    public sealed class PursuitGrid
    {
        public const float CellSize = 0.5f;
        public const float LifetimeSeconds = 3f;
        private static readonly Dictionary<(int, int, int, int, int), PursuitGrid> Cache = new();
        private static readonly Collider2D[] Overlaps = new Collider2D[16];

        private readonly Rect _area;
        private readonly int _cols;
        private readonly int _rows;
        private readonly bool[] _free;
        private readonly float _builtAt;

        // A* scratch (single-threaded: reused across searches).
        private float[] _g;
        private int[] _parent;
        private bool[] _closed;

        public int Cols => _cols;
        public int Rows => _rows;
        public static int Builds { get; private set; }

        private PursuitGrid(Rect area, Rect legal, float clearance)
        {
            _area = area;
            _cols = Mathf.Max(1, Mathf.CeilToInt(area.width / CellSize));
            _rows = Mathf.Max(1, Mathf.CeilToInt(area.height / CellSize));
            _free = new bool[_cols * _rows];
            _builtAt = Time.time;
            for (var y = 0; y < _rows; y++)
            for (var x = 0; x < _cols; x++)
            {
                var c = Center(x, y);
                _free[y * _cols + x] = legal.Contains(c) && Clear(c, clearance);
            }

            Builds++;
        }

        public static PursuitGrid For(Rect area, Rect legal, float radius)
        {
            var clearance = radius + 0.05f;
            var key = (Mathf.RoundToInt(area.x * 4f), Mathf.RoundToInt(area.y * 4f), Mathf.RoundToInt(area.width * 4f), Mathf.RoundToInt(area.height * 4f), Mathf.RoundToInt(clearance * 40f));
            if (Cache.TryGetValue(key, out var grid) && Time.time >= grid._builtAt && Time.time - grid._builtAt < LifetimeSeconds) return grid;
            if (Cache.Count > 64) Cache.Clear();
            grid = new PursuitGrid(area, legal, clearance);
            Cache[key] = grid;
            return grid;
        }

        private static bool Clear(Vector2 point, float radius)
        {
            var count = Physics2D.OverlapCircle(point, radius, ContactFilter2D.noFilter, Overlaps);
            for (var i = 0; i < count; i++)
            {
                var c = Overlaps[i];
                if (c != null && !c.isTrigger && c.enabled && c.GetComponentInParent<EnvironmentObstacle>() != null) return false;
            }

            return true;
        }

        public Vector2 Center(int x, int y) => _area.min + new Vector2((x + 0.5f) * CellSize, (y + 0.5f) * CellSize);
        public bool IsFree(int x, int y) => x >= 0 && y >= 0 && x < _cols && y < _rows && _free[y * _cols + x];

        private (int x, int y) CellOf(Vector2 p) =>
            (Mathf.Clamp(Mathf.FloorToInt((p.x - _area.xMin) / CellSize), 0, _cols - 1), Mathf.Clamp(Mathf.FloorToInt((p.y - _area.yMin) / CellSize), 0, _rows - 1));

        /// <summary>The free cell nearest <paramref name="p"/> (a body pressed against a wall reads its own cell as blocked).</summary>
        private int NearestFree(Vector2 p)
        {
            var (cx, cy) = CellOf(p);
            var best = -1;
            var bestDistance = float.MaxValue;
            for (var ring = 0; ring <= 4 && best < 0; ring++)
            for (var y = cy - ring; y <= cy + ring; y++)
            for (var x = cx - ring; x <= cx + ring; x++)
            {
                if (!IsFree(x, y)) continue;
                var d = (Center(x, y) - p).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = y * _cols + x; }
            }

            return best;
        }

        /// <summary>
        /// A* over the free cells (8-way, never cutting a blocked corner) from the body to the target. A target that
        /// cannot be reached (another side of a wall, outside the area) routes to the reached cell nearest it. Fills
        /// <paramref name="path"/> with world points ending at the target when the body may stand there (inside
        /// <paramref name="legal"/>), else at that nearest cell; leaves it empty when the body has no free cell at all.
        /// Cells a body of <paramref name="bodyRadius"/> would share with one of <paramref name="avoid"/> (other enemies
        /// holding the way) are treated as closed. True when the route reaches the target's cell.
        /// </summary>
        public bool FindPath(Vector2 from, Vector2 to, Rect legal, List<Vector2> path, IReadOnlyList<(Vector2 position, float radius)> avoid = null, float bodyRadius = 0f)
        {
            path.Clear();
            var start = NearestFree(from);
            if (start < 0) return false;

            bool Open(int x, int y)
            {
                if (!IsFree(x, y)) return false;
                if (avoid == null) return true;
                var c = Center(x, y);
                foreach (var (position, radius) in avoid)
                    if ((c - position).sqrMagnitude < (radius + bodyRadius) * (radius + bodyRadius)) return false;
                return true;
            }

            var (tx, ty) = CellOf(to);
            var goal = IsFree(tx, ty) ? ty * _cols + tx : NearestFree(to);
            var n = _cols * _rows;
            if (_g == null || _g.Length != n) { _g = new float[n]; _parent = new int[n]; _closed = new bool[n]; }
            for (var i = 0; i < n; i++) { _g[i] = float.MaxValue; _parent[i] = -1; _closed[i] = false; }

            var open = new SortedSet<(float f, int cell)>();
            _g[start] = 0f;
            open.Add((Heuristic(start, to), start));
            var bestCell = start;
            var bestH = Heuristic(start, to);
            while (open.Count > 0)
            {
                var (_, current) = open.Min;
                open.Remove(open.Min);
                if (_closed[current]) continue;
                _closed[current] = true;
                var h = Heuristic(current, to);
                if (h < bestH) { bestH = h; bestCell = current; }
                if (current == goal) { bestCell = goal; break; }
                int cx = current % _cols, cy = current / _cols;
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = cx + dx, ny = cy + dy;
                    if (!Open(nx, ny)) continue;
                    if (dx != 0 && dy != 0 && (!Open(cx + dx, cy) || !Open(cx, cy + dy))) continue; // no corner cutting
                    var next = ny * _cols + nx;
                    if (_closed[next]) continue;
                    var cost = _g[current] + (dx != 0 && dy != 0 ? 1.4142f : 1f) * CellSize;
                    if (cost >= _g[next]) continue;
                    _g[next] = cost;
                    _parent[next] = current;
                    open.Add((cost + Heuristic(next, to), next));
                }
            }

            for (var cell = bestCell; cell >= 0; cell = _parent[cell]) path.Add(Center(cell % _cols, cell / _cols));
            path.Reverse();
            // End on the target itself only where the body may stand; otherwise the reachable cell nearest it.
            if (bestCell == goal && IsFree(tx, ty) && legal.Contains(to)) path[path.Count - 1] = to;
            return bestCell == goal;
        }

        private float Heuristic(int cell, Vector2 to) => Vector2.Distance(Center(cell % _cols, cell / _cols), to);
    }
}

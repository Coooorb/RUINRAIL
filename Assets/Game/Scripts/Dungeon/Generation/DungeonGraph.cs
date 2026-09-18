using System.Collections.Generic;
using System.Linq;
using RuinRail.Dungeon.Rooms;

namespace RuinRail.Dungeon.Generation
{
    /// <summary>One abstract room slot in the layout graph. No geometry, no prefab — categories and topology only.</summary>
    public sealed class RoomNode
    {
        public RoomNode(int id, RoomType type)
        {
            Id = id;
            Type = type;
        }

        public int Id { get; }
        public RoomType Type { get; internal set; }

        /// <summary>Elite encounter flag on a Combat node (55_ROOM_TYPES: Elite is not a room architecture).</summary>
        public bool IsElite { get; internal set; }

        /// <summary>Index along the main path, or -1 for branch rooms.</summary>
        public int MainPathIndex { get; internal set; } = -1;

        /// <summary>Branch number (0-based) for branch rooms, or -1 on the main path.</summary>
        public int BranchIndex { get; internal set; } = -1;

        /// <summary>Position inside its branch (0 = attached to the main path).</summary>
        public int BranchStep { get; internal set; } = -1;

        public List<int> Neighbors { get; } = new();

        public bool IsOnMainPath => MainPathIndex >= 0;
    }

    /// <summary>Deterministic abstract dungeon layout for one depth: nodes, undirected edges, main path and branches.</summary>
    public sealed class DungeonGraph
    {
        private readonly List<RoomNode> _nodes = new();
        private readonly List<(int a, int b)> _edges = new();
        private readonly List<int> _mainPath = new();
        private readonly List<List<int>> _branches = new();

        public DungeonGraph(int runSeed, int depth)
        {
            RunSeed = runSeed;
            Depth = depth;
        }

        public int RunSeed { get; }
        public int Depth { get; }
        public IReadOnlyList<RoomNode> Nodes => _nodes;
        public IReadOnlyList<(int a, int b)> Edges => _edges;
        public IReadOnlyList<int> MainPath => _mainPath;
        public IReadOnlyList<IReadOnlyList<int>> Branches => _branches;
        public int StartId => _mainPath.Count > 0 ? _mainPath[0] : -1;
        public int BossId => _mainPath.Count > 0 ? _mainPath[_mainPath.Count - 1] : -1;

        public RoomNode GetNode(int id) => _nodes[id];

        public bool AreAdjacent(int a, int b) => _nodes[a].Neighbors.Contains(b);

        public IEnumerable<RoomNode> NodesOfType(RoomType type) => _nodes.Where(n => n.Type == type);

        internal RoomNode AddNode(RoomType type)
        {
            var node = new RoomNode(_nodes.Count, type);
            _nodes.Add(node);
            return node;
        }

        internal void AddEdge(int a, int b)
        {
            if (a == b || AreAdjacent(a, b))
            {
                return;
            }

            _edges.Add((a, b));
            _nodes[a].Neighbors.Add(b);
            _nodes[b].Neighbors.Add(a);
        }

        internal void SetMainPath(IEnumerable<int> ids)
        {
            _mainPath.Clear();
            _mainPath.AddRange(ids);
            for (var i = 0; i < _mainPath.Count; i++)
            {
                _nodes[_mainPath[i]].MainPathIndex = i;
            }
        }

        internal void AddBranch(List<int> ids)
        {
            var index = _branches.Count;
            _branches.Add(ids);
            for (var i = 0; i < ids.Count; i++)
            {
                _nodes[ids[i]].BranchIndex = index;
                _nodes[ids[i]].BranchStep = i;
            }
        }

        /// <summary>Stable textual signature used to compare structural identity across runs.</summary>
        public string Signature()
        {
            var nodes = string.Join(";", _nodes.Select(n => $"{n.Id}:{n.Type}{(n.IsElite ? "*" : "")}:{n.MainPathIndex}:{n.BranchIndex}"));
            var edges = string.Join(";", _edges.Select(e => $"{e.a}-{e.b}"));
            return $"seed={RunSeed} depth={Depth} main=[{string.Join(",", _mainPath)}] nodes=[{nodes}] edges=[{edges}]";
        }
    }
}

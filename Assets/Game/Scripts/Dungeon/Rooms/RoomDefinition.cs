using RuinRail.Core;
using System.Collections.Generic;
using UnityEngine;

namespace RuinRail.Dungeon.Rooms
{
    /// <summary>
    /// Stable metadata for one hand-authored room (dungeon/52_ROOM_METADATA_AND_TAGS.md). Geometry lives in the prefab;
    /// encounter contents are decided elsewhere so a room never carries a fixed enemy composition.
    /// </summary>
    [CreateAssetMenu(fileName = "Room_", menuName = "RuinRail/Dungeon/Room Definition")]
    public sealed class RoomDefinition : ScriptableObject
    {
        [SerializeField] private string _id;
        [SerializeField] private Biome _biome;
        [SerializeField] private RoomType _roomType;
        [SerializeField] private RoomSizeClass _sizeClass;
        [SerializeField] private Vector2Int _dimensions = new(16, 12);
        [SerializeField] private bool _allowsAuthoredSizeVariation;
        [SerializeField, Min(0)] private int _difficulty = 1;
        [SerializeField, Min(1)] private int _minDepth = 1;
        [SerializeField, Min(0)] private int _maxDepth;
        [SerializeField] private bool _supportsElite;
        [SerializeField, Min(0f)] private float _selectionWeight = 1f;
        [SerializeField] private DoorDirection[] _supportedDoors = System.Array.Empty<DoorDirection>();
        [SerializeField] private string[] _tags = System.Array.Empty<string>();
        [SerializeField] private GameObject _prefab;

        public string Id => _id;
        public Biome Biome => _biome;
        public RoomType RoomType => _roomType;
        public RoomSizeClass SizeClass => _sizeClass;

        /// <summary>Authored tile dimensions. Must equal the size class unless authored variation is explicitly allowed.</summary>
        public Vector2Int Dimensions => _dimensions;
        public bool AllowsAuthoredSizeVariation => _allowsAuthoredSizeVariation;
        public int Difficulty => _difficulty;
        public int MinDepth => Mathf.Max(1, _minDepth);

        /// <summary>0 means unlimited depth.</summary>
        public int MaxDepth => _maxDepth;
        public bool IsUnlimitedDepth => _maxDepth <= 0;
        public bool SupportsElite => _supportsElite;
        public float SelectionWeight => Mathf.Max(0f, _selectionWeight);
        public IReadOnlyList<DoorDirection> SupportedDoors => _supportedDoors;
        public IReadOnlyList<string> Tags => _tags;
        public GameObject Prefab => _prefab;

        public bool SupportsDoor(DoorDirection direction)
        {
            foreach (var door in _supportedDoors)
            {
                if (door == direction)
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsAvailableAtDepth(int depth)
        {
            return depth >= MinDepth && (IsUnlimitedDepth || depth <= _maxDepth);
        }

        public bool HasTag(string tag)
        {
            foreach (var t in _tags)
            {
                if (t == tag)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

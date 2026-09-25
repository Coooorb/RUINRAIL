using UnityEngine;

namespace RuinRail.Dungeon.Generation
{
    /// <summary>
    /// Centralized approved graph targets (dungeon/53_DUNGEON_GENERATOR.md, 55_ROOM_TYPES.md, 59_DEPTH_SCALING.md).
    /// Serialized so balance stays data-driven; <see cref="CreateDefault"/> yields the approved V1 values.
    /// </summary>
    [CreateAssetMenu(fileName = "DungeonGraphRules", menuName = "RuinRail/Dungeon/Graph Rules")]
    public sealed class DungeonGraphRules : ScriptableObject
    {
        [Header("Room count by depth")]
        [SerializeField] private Vector2Int _roomsDepth1To3 = new(9, 10);
        [SerializeField] private Vector2Int _roomsDepth4To8 = new(10, 11);
        [SerializeField] private Vector2Int _roomsDepth9Plus = new(10, 13);

        [Header("Main path / branches")]
        [SerializeField] private Vector2Int _mainPathLength = new(6, 9);
        [SerializeField] private Vector2Int _branchCount = new(1, 3);
        [SerializeField] private Vector2Int _branchLength = new(1, 3);

        [Header("Per-dungeon category ranges")]
        [SerializeField] private Vector2Int _combatRooms = new(4, 8);
        [SerializeField] private int _maxMerchant = 1;
        [SerializeField] private int _maxEvent = 2;
        [SerializeField] private int _maxLoot = 2;
        [SerializeField] private int _maxTreasure = 1;
        [SerializeField] private int _maxMedical = 1;
        [SerializeField] private int _maxEliteEarly = 1;
        [SerializeField] private int _maxEliteLate = 2;
        [SerializeField] private int _lateEliteDepth = 11;

        [Header("Elite chance per dungeon (percent)")]
        // 59_DEPTH_SCALING elite chance. Raised by the run-variety pass from 5/10/15/20/25: the measured rate meant a
        // player pushing five depths had a 65.8% chance of meeting no elite at all, so six authored elite variants went
        // unseen. Depth 1-2 is deliberately left lowest so a first run is not elite-heavy, and the D21+ cap is unchanged
        // at 25% per slot so deep play does not become an elite parade. Slot counts, elite HP, damage and rewards are
        // not touched by this pass.
        [SerializeField] private int _eliteChanceDepth1To2 = 8;
        [SerializeField] private int _eliteChanceDepth3To5 = 18;
        [SerializeField] private int _eliteChanceDepth6To10 = 25;
        [SerializeField] private int _eliteChanceDepth11To20 = 25;
        [SerializeField] private int _eliteChanceDepth21Plus = 25;

        [SerializeField, Min(1)] private int _maxGenerationAttempts = 32;

        public Vector2Int MainPathLength => _mainPathLength;
        public Vector2Int BranchCount => _branchCount;
        public Vector2Int BranchLength => _branchLength;
        public Vector2Int CombatRooms => _combatRooms;
        public int MaxMerchant => _maxMerchant;
        public int MaxEvent => _maxEvent;
        public int MaxLoot => _maxLoot;
        public int MaxTreasure => _maxTreasure;
        public int MaxMedical => _maxMedical;
        public int MaxGenerationAttempts => Mathf.Max(1, _maxGenerationAttempts);

        public Vector2Int RoomCountRange(int depth)
        {
            if (depth <= 3) return _roomsDepth1To3;
            if (depth <= 8) return _roomsDepth4To8;
            return _roomsDepth9Plus;
        }

        public int MaxElites(int depth) => depth >= _lateEliteDepth ? _maxEliteLate : _maxEliteEarly;

        public int EliteChancePercent(int depth)
        {
            if (depth <= 2) return _eliteChanceDepth1To2;
            if (depth <= 5) return _eliteChanceDepth3To5;
            if (depth <= 10) return _eliteChanceDepth6To10;
            if (depth <= 20) return _eliteChanceDepth11To20;
            return _eliteChanceDepth21Plus;
        }

        public static DungeonGraphRules CreateDefault()
        {
            return CreateInstance<DungeonGraphRules>();
        }
    }
}

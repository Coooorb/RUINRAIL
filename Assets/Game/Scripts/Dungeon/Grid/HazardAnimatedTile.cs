using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuinRail.Dungeon.Grid
{
    /// <summary>
    /// The damaging-floor tile of a biome's Hazards tilemap: a looping frame set the <see cref="TilemapRenderer"/>
    /// plays by itself, so a hazard reads as live and dangerous instead of as one more floor texture.
    ///
    /// Presentation only. It never has a collider (the damage is the room's <c>RoomHazard</c> trigger, sized from its
    /// marker), so swapping a static tile for this one changes nothing about collision, triggers or generation. The
    /// animation is the engine's own tile animation: no per-frame script, identical in the editor and the player.
    /// </summary>
    [CreateAssetMenu(fileName = "HazardAnimatedTile", menuName = "RuinRail/Dungeon/Hazard Animated Tile")]
    public sealed class HazardAnimatedTile : TileBase
    {
        [SerializeField] private Sprite[] _frames = System.Array.Empty<Sprite>();
        [Tooltip("Loop playback rate (frames per second).")]
        [SerializeField, Min(0.1f)] private float _framesPerSecond = 6f;
        [Tooltip("Liquids and heat look alive when neighbouring cells are out of step; current reads best as one pulse.")]
        [SerializeField] private bool _desyncCells;

        public Sprite[] Frames => _frames;
        public float FramesPerSecond => _framesPerSecond;
        public bool DesyncCells => _desyncCells;
        public bool IsAnimated => _frames != null && _frames.Length > 1 && System.Array.TrueForAll(_frames, f => f != null);

        [System.NonSerialized] private Sprite[][] _rotations;

        private Sprite[] Rotation(int start)
        {
            if (start == 0) return _frames;
            if (_rotations == null || _rotations.Length != _frames.Length) _rotations = new Sprite[_frames.Length][];
            if (_rotations[start] != null) return _rotations[start];
            var rotated = new Sprite[_frames.Length];
            for (var i = 0; i < rotated.Length; i++) rotated[i] = _frames[(i + start) % _frames.Length];
            return _rotations[start] = rotated;
        }

        public void Configure(Sprite[] frames, float framesPerSecond, bool desyncCells)
        {
            _rotations = null;
            _frames = frames ?? System.Array.Empty<Sprite>();
            _framesPerSecond = Mathf.Max(0.1f, framesPerSecond);
            _desyncCells = desyncCells;
        }

        public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
        {
            tileData.sprite = _frames != null && _frames.Length > 0 ? _frames[0] : null;
            tileData.color = Color.white;
            tileData.transform = Matrix4x4.identity;
            tileData.flags = TileFlags.LockTransform;
            tileData.colliderType = Tile.ColliderType.None;
        }

        public override bool GetTileAnimationData(Vector3Int position, ITilemap tilemap, ref TileAnimationData tileAnimationData)
        {
            if (!IsAnimated) return false;
            tileAnimationData.animationSpeed = _framesPerSecond;
            // Out-of-step cells get the same loop rotated to their own starting frame, all starting now: a start-time
            // offset would instead hold a freshly composed cell on its first frame until its offset elapsed.
            tileAnimationData.animatedSprites = _desyncCells ? Rotation(StartFrame(position, _frames.Length)) : _frames;
            tileAnimationData.animationStartTime = 0f;
            return true;
        }

        /// <summary>
        /// Deterministic starting frame per cell: 3x + 5y (neither step divides the 8-frame loop), so no two edge-adjacent
        /// cells ever start on the same frame and a room always shimmers the same way.
        /// </summary>
        public static int StartFrame(Vector3Int cell, int frames)
        {
            if (frames <= 0) return 0;
            var k = (cell.x * 3 + cell.y * 5) % frames;
            return k < 0 ? k + frames : k;
        }
    }
}

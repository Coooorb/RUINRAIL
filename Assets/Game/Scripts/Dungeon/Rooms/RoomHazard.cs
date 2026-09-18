using RuinRail.Dungeon.Grid;
using RuinRail.Gameplay.Combat.Hazards;
using UnityEngine;

namespace RuinRail.Dungeon.Rooms
{
    /// <summary>
    /// Authoring glue for a Hazard <see cref="RoomMarker"/>: the marker gives the integer cell footprint on the room grid,
    /// this component names the <see cref="HazardDefinition"/> and materialises a trigger BoxCollider2D + HazardVolume of
    /// exactly that footprint (one tile = one world unit). Validation refuses hazard markers without a definition.
    /// </summary>
    [RequireComponent(typeof(RoomMarker))]
    public sealed class RoomHazard : MonoBehaviour
    {
        [SerializeField] private HazardDefinition _definition;

        public HazardDefinition Definition => _definition;
        public RoomMarker Marker => GetComponent<RoomMarker>();
        public HazardVolume Volume { get; private set; }

        public void SetDefinition(HazardDefinition definition)
        {
            _definition = definition;
            if (Volume != null) Volume.SetDefinition(definition);
        }

        /// <summary>Creates (or refreshes) the runtime trigger and volume sized to the marker footprint.</summary>
        public HazardVolume Build()
        {
            var marker = Marker;
            var footprint = marker.Footprint;
            var size = new Vector2(footprint.x * GridConstants.TileWorldSize, footprint.y * GridConstants.TileWorldSize);

            var box = GetComponent<BoxCollider2D>();
            if (box == null) box = gameObject.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = size;
            // RoomMarker.SnapToGrid places the transform at the footprint centre, so the box is centred on it.
            box.offset = Vector2.zero;

            Volume = GetComponent<HazardVolume>();
            if (Volume == null) Volume = gameObject.AddComponent<HazardVolume>();
            Volume.SetDefinition(_definition);
            return Volume;
        }

        private void Awake()
        {
            if (Application.isPlaying && _definition != null) Build();
        }
    }
}

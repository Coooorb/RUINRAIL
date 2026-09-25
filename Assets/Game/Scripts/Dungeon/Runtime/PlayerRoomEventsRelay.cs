using RuinRail.Gameplay.Stats;
using UnityEngine;

namespace RuinRail.Dungeon.Runtime
{
    /// <summary>
    /// Player-side bridge from room lifecycle to the player's PlayerCombatEvents hub, so per-room armor passives
    /// (Patchwork heal on clear, Emergency Care / Second Wind once-per-room resets) react without knowing rooms.
    /// </summary>
    public sealed class PlayerRoomEventsRelay : MonoBehaviour, IRoomEventListener
    {
        private PlayerCombatEvents _events;

        public PlayerCombatEvents Events => _events;
        public RoomRuntime CurrentCombatRoom { get; private set; }
        public int RoomsCleared { get; private set; }
        /// <summary>The combat room whose clear was relayed last (Room Sweep pulls that room's pickups).</summary>
        public RoomRuntime LastClearedRoom { get; private set; }

        public void SetEvents(PlayerCombatEvents events) => _events = events;

        public void OnCombatRoomEntered(RoomRuntime room)
        {
            CurrentCombatRoom = room;
            _events?.RaiseCombatRoomEntered();
        }

        public void OnCombatRoomCleared(RoomRuntime room, RoomClearedContext context)
        {
            if (CurrentCombatRoom == room) CurrentCombatRoom = null;
            RoomsCleared++;
            LastClearedRoom = room;
            _events?.RaiseCombatRoomCleared();
        }
    }
}

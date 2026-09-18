using System;

namespace RuinRail.Core.Input
{
    /// <summary>
    /// A counted hold that menus over gameplay (inventory, pause) take while they own the screen: while held, the
    /// player input reader reports no movement, no fire/special/interact and raises no gameplay action, so a click on
    /// an inventory slot can never also be a shot into the world and a menu key can never dash the player. The
    /// Inventory and Pause toggles pass through, because they are how the menus close again.
    /// </summary>
    public static class GameplayInputGate
    {
        public static int Holds { get; private set; }
        public static bool IsHeld => Holds > 0;

        public static event Action Changed;

        public static void Hold() { Holds++; Changed?.Invoke(); }
        public static void Release() { if (Holds == 0) return; Holds--; Changed?.Invoke(); }

        /// <summary>Tests / scene teardown: drop every hold.</summary>
        public static void Reset() { if (Holds == 0) return; Holds = 0; Changed?.Invoke(); }
    }
}

using System;
using UnityEngine;

namespace RuinRail.Core.Input
{
    public interface IPlayerInputReader
    {
        Vector2 Move { get; }
        Vector2 Aim { get; }
        bool IsAimFromPointer { get; }
        bool FireHeld { get; }
        bool SpecialHeld { get; }
        /// <summary>Interact held down (84 hold-to-revive); the Interact event stays the press edge.</summary>
        bool InteractHeld { get; }

        event Action Dash;
        event Action Reload;
        event Action Interact;
        event Action Weapon1Selected;
        event Action Weapon2Selected;
        event Action WeaponSwapped;
        event Action ConsumableUsed;
        event Action InventoryToggled;
        /// <summary>Pause / menu press edge (116: Escape, controller Menu/Options). Consumed by the pause flow, never by gameplay.</summary>
        event Action PauseToggled;

        void Enable();
        void Disable();
    }
}

using System;
using UnityEngine;

namespace RuinRail.Core.Input
{
    /// <summary>
    /// An input reader that never reads anything: attached to remote player replicas so they can share the exact
    /// same component composition as the local player without ever touching this machine's devices.
    /// </summary>
    public sealed class NullPlayerInputReader : IPlayerInputReader
    {
        public static readonly NullPlayerInputReader Instance = new();

        public Vector2 Move => Vector2.zero;
        public Vector2 Aim => Vector2.zero;
        public bool IsAimFromPointer => false;
        public bool FireHeld => false;
        public bool SpecialHeld => false;
        public bool InteractHeld => false;

#pragma warning disable CS0067
        public event Action Dash;
        public event Action Reload;
        public event Action Interact;
        public event Action Weapon1Selected;
        public event Action Weapon2Selected;
        public event Action WeaponSwapped;
        public event Action ConsumableUsed;
        public event Action InventoryToggled;
        public event Action PauseToggled;
#pragma warning restore CS0067

        public void Enable()
        {
        }

        public void Disable()
        {
        }
    }
}

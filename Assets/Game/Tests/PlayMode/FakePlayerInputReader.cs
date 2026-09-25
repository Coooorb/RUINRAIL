using System;
using RuinRail.Core.Input;
using UnityEngine;

namespace RuinRail.Tests
{
#pragma warning disable CS0067 // Unused by this test double; part of the IPlayerInputReader contract.
    internal sealed class FakePlayerInputReader : IPlayerInputReader
    {
        public Vector2 Move { get; set; }
        public Vector2 Aim { get; set; }
        public bool IsAimFromPointer { get; set; }
        public bool FireHeld { get; set; }
        public bool SpecialHeld { get; set; }
        public bool InteractHeld { get; set; }

        public event Action Dash;
        public event Action Reload;
        public event Action Interact;
        public event Action Weapon1Selected;
        public event Action Weapon2Selected;
        public event Action WeaponSwapped;
        public event Action ConsumableUsed;
        public event Action QuickGrenadeUsed;
        public event Action InventoryToggled;
        public event Action PauseToggled;

        public void Enable()
        {
        }

        public void Disable()
        {
        }

        public void RaiseDash()
        {
            Dash?.Invoke();
        }

        public void RaiseReload()
        {
            Reload?.Invoke();
        }

        public void RaiseInteract()
        {
            Interact?.Invoke();
        }

        public void RaiseWeapon1Selected()
        {
            Weapon1Selected?.Invoke();
        }

        public void RaiseWeapon2Selected()
        {
            Weapon2Selected?.Invoke();
        }

        public void RaiseWeaponSwapped()
        {
            WeaponSwapped?.Invoke();
        }

        public void RaiseInventoryToggled()
        {
            InventoryToggled?.Invoke();
        }

        public void RaisePause()
        {
            PauseToggled?.Invoke();
        }

        public void RaiseConsumableUsed()
        {
            ConsumableUsed?.Invoke();
        }

        /// <summary>
        /// The quick-grenade press. The real reader only raises this while gameplay input is allowed, so this double
        /// honours the same gate — a test that holds <see cref="GameplayInputGate"/> must see nothing happen.
        /// </summary>
        public void RaiseQuickGrenade()
        {
            if (GameplayInputGate.IsHeld) return;
            QuickGrenadeUsed?.Invoke();
        }
    }
#pragma warning restore CS0067
}

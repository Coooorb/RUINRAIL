using System;
using RuinRail.Core.Input;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// The input a built-player proof drives a player with: a move, an aim direction, held fire/interact and the edge
    /// presses, exactly the <see cref="IPlayerInputReader"/> surface the device reader offers — and gated by the same
    /// <see cref="GameplayInputGate"/>, so a scripted player cannot act while a menu or a co-op depth wait holds input.
    /// It changes only where this player's intents come from; the host validates them like any other (82).
    /// </summary>
    public sealed class ProofInputReader : IPlayerInputReader
    {
        private Vector2 _move;
        private bool _fire;
        private bool _interact;

        public Vector2 Move => GameplayInputGate.IsHeld ? Vector2.zero : _move;
        public Vector2 Aim { get; private set; } = Vector2.right;
        public bool IsAimFromPointer => false;
        public bool FireHeld => !GameplayInputGate.IsHeld && _fire;
        public bool SpecialHeld => false;
        public bool InteractHeld => !GameplayInputGate.IsHeld && _interact;

#pragma warning disable CS0067 // Edges a scripted proof player never presses are still part of the reader contract.
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
#pragma warning restore CS0067

        public int InteractPresses { get; private set; }

        public void SetMove(Vector2 move) => _move = Vector2.ClampMagnitude(move, 1f);

        public void AimAt(Vector2 direction)
        {
            if (direction.sqrMagnitude > 0.0001f) Aim = direction.normalized;
        }

        public void SetFire(bool held) => _fire = held;
        public void SetInteractHeld(bool held) => _interact = held;

        public void PressInteract()
        {
            if (GameplayInputGate.IsHeld) return;
            InteractPresses++;
            Interact?.Invoke();
        }

        public void PressReload() => Reload?.Invoke();

        public void PressDash()
        {
            if (GameplayInputGate.IsHeld) return;
            Dash?.Invoke();
        }

        public void Release()
        {
            _move = Vector2.zero;
            _fire = false;
            _interact = false;
        }

        public void Enable() { }
        public void Disable() { }
    }
}

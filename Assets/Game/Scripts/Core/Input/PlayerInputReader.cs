using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RuinRail.Core.Input
{
    public sealed class PlayerInputReader : IPlayerInputReader, RuinRailInputActions.IPlayerActions, IDisposable
    {
        private readonly RuinRailInputActions _actions;

        private Vector2 _move;
        private bool _fireHeld;
        private bool _specialHeld;
        private bool _interactHeld;

        // A menu layer over gameplay (GameplayInputGate) reads as "no gameplay input": the raw values are kept so the
        // state is correct again the moment the menu releases the hold.
        public Vector2 Move => GameplayInputGate.IsHeld ? Vector2.zero : _move;
        public Vector2 Aim { get; private set; }
        public bool IsAimFromPointer { get; private set; }
        public bool FireHeld => !GameplayInputGate.IsHeld && _fireHeld;
        public bool SpecialHeld => !GameplayInputGate.IsHeld && _specialHeld;
        public bool InteractHeld => !GameplayInputGate.IsHeld && _interactHeld;

        private static bool GameplayAllowed => !GameplayInputGate.IsHeld;

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

        public PlayerInputReader()
        {
            _actions = new RuinRailInputActions();
            _actions.Player.AddCallbacks(this);
            ActiveBindingOverrides.ApplyTo(_actions.asset);
            ActiveBindingOverrides.Changed += OnOverridesChanged;
        }

        /// <summary>This reader's own action instance (rebinding UI operates on the local player's reader).</summary>
        public InputActionAsset Asset => _actions.asset;

        public void Enable()
        {
            _actions.Player.Enable();
        }

        public void Disable()
        {
            _actions.Player.Disable();
        }

        public void Dispose()
        {
            ActiveBindingOverrides.Changed -= OnOverridesChanged;
            _actions.Player.RemoveCallbacks(this);
            if (Application.isPlaying)
            {
                _actions.Dispose();
            }
            else
            {
                // The generated wrapper destroys its asset with Object.Destroy, which the editor refuses outside play mode.
                _actions.Player.Disable();
                UnityEngine.Object.DestroyImmediate(_actions.asset);
            }
        }

        private void OnOverridesChanged() => ActiveBindingOverrides.ApplyTo(_actions.asset);

        private static void Note(InputAction.CallbackContext context)
        {
            if (context.performed || context.started) ActiveInputDevice.NoteControl(context.control);
        }

        public void OnMove(InputAction.CallbackContext context)
        {
            Note(context);
            _move = context.ReadValue<Vector2>();
        }

        public void OnAim(InputAction.CallbackContext context)
        {
            Note(context);
            Aim = context.ReadValue<Vector2>();
            IsAimFromPointer = context.control.device is Pointer;
        }

        public void OnFire(InputAction.CallbackContext context)
        {
            Note(context);
            _fireHeld = context.ReadValueAsButton();
        }

        public void OnSpecial(InputAction.CallbackContext context)
        {
            Note(context);
            _specialHeld = context.ReadValueAsButton();
        }

        public void OnDash(InputAction.CallbackContext context)
        {
            Note(context);
            if (context.performed)
            {
                if (GameplayAllowed) Dash?.Invoke();
            }
        }

        public void OnReload(InputAction.CallbackContext context)
        {
            Note(context);
            if (context.performed)
            {
                if (GameplayAllowed) Reload?.Invoke();
            }
        }

        public void OnInteract(InputAction.CallbackContext context)
        {
            Note(context);
            if (context.started) _interactHeld = true;
            if (context.canceled) _interactHeld = false;
            if (context.performed)
            {
                _interactHeld = true;
                if (GameplayAllowed) Interact?.Invoke();
            }
        }

        public void OnWeapon1(InputAction.CallbackContext context)
        {
            Note(context);
            if (context.performed)
            {
                if (GameplayAllowed) Weapon1Selected?.Invoke();
            }
        }

        public void OnWeapon2(InputAction.CallbackContext context)
        {
            Note(context);
            if (context.performed)
            {
                if (GameplayAllowed) Weapon2Selected?.Invoke();
            }
        }

        public void OnWeaponSwap(InputAction.CallbackContext context)
        {
            Note(context);
            if (context.performed)
            {
                if (GameplayAllowed) WeaponSwapped?.Invoke();
            }
        }

        public void OnConsumable(InputAction.CallbackContext context)
        {
            Note(context);
            if (context.performed)
            {
                if (GameplayAllowed) ConsumableUsed?.Invoke();
            }
        }

        /// <summary>
        /// Quick-grenade. Gated exactly like firing and the consumable key: while an inventory, merchant, pause or
        /// event window holds gameplay input, <see cref="GameplayInputGate"/> is held and the press does nothing.
        /// </summary>
        public void OnQuickGrenade(InputAction.CallbackContext context)
        {
            Note(context);
            if (context.performed)
            {
                if (GameplayAllowed) QuickGrenadeUsed?.Invoke();
            }
        }

        public void OnInventory(InputAction.CallbackContext context)
        {
            Note(context);
            if (context.performed)
            {
                InventoryToggled?.Invoke();
            }
        }

        public void OnPause(InputAction.CallbackContext context)
        {
            Note(context);
            if (context.performed)
            {
                PauseToggled?.Invoke();
            }
        }
    }
}

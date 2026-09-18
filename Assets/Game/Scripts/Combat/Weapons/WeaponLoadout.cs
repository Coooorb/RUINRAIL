using System;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Weapons
{
    public sealed class WeaponLoadout : MonoBehaviour
    {
        [SerializeField] private Component _primarySource;
        [SerializeField] private Component _secondarySource;

        private IEquippableWeapon _primary;
        private IEquippableWeapon _secondary;
        private IPlayerInputReader _inputReader;
        private readonly ActionGateLookup _actionGate = new();

        public WeaponSlot ActiveSlot { get; private set; } = WeaponSlot.Primary;
        public IEquippableWeapon ActiveWeapon => GetWeapon(ActiveSlot);

        public event Action<WeaponSlot> ActiveSlotChanged;

        public void SetPrimary(IEquippableWeapon weapon)
        {
            _primary = weapon;
        }

        public void SetSecondary(IEquippableWeapon weapon)
        {
            _secondary = weapon;
        }

        public void SetInputReader(IPlayerInputReader inputReader)
        {
            AttachInputReader(inputReader);
        }

        public IEquippableWeapon GetSlot(WeaponSlot slot)
        {
            return GetWeapon(slot);
        }

        public void Initialize()
        {
            ActiveSlot = WeaponSlot.Primary;
            _primary?.OnEquipped();
            _secondary?.OnUnequipped();
        }

        private void Awake()
        {
            if (_primary == null && _primarySource is IEquippableWeapon primaryFromSource)
            {
                _primary = primaryFromSource;
            }

            if (_secondary == null && _secondarySource is IEquippableWeapon secondaryFromSource)
            {
                _secondary = secondaryFromSource;
            }

            if (_inputReader == null)
            {
                AttachInputReader(GetComponent<PlayerInput>()?.Reader);
            }

            Initialize();
        }

        private void OnDestroy()
        {
            AttachInputReader(null);
        }

        private void AttachInputReader(IPlayerInputReader inputReader)
        {
            if (_inputReader != null)
            {
                _inputReader.Weapon1Selected -= HandleWeapon1Selected;
                _inputReader.Weapon2Selected -= HandleWeapon2Selected;
                _inputReader.WeaponSwapped -= HandleWeaponSwapped;
            }

            _inputReader = inputReader;

            if (_inputReader != null)
            {
                _inputReader.Weapon1Selected += HandleWeapon1Selected;
                _inputReader.Weapon2Selected += HandleWeapon2Selected;
                _inputReader.WeaponSwapped += HandleWeaponSwapped;
            }
        }

        private void HandleWeapon1Selected()
        {
            SelectSlot(WeaponSlot.Primary);
        }

        private void HandleWeapon2Selected()
        {
            SelectSlot(WeaponSlot.Secondary);
        }

        private void HandleWeaponSwapped()
        {
            SelectSlot(ActiveSlot == WeaponSlot.Primary ? WeaponSlot.Secondary : WeaponSlot.Primary);
        }

        public void SelectSlot(WeaponSlot slot)
        {
            if (slot == ActiveSlot || !_actionGate.CanAct(this))
            {
                return;
            }

            GetWeapon(ActiveSlot)?.OnUnequipped();
            ActiveSlot = slot;
            GetWeapon(ActiveSlot)?.OnEquipped();

            ActiveSlotChanged?.Invoke(ActiveSlot);
        }

        private IEquippableWeapon GetWeapon(WeaponSlot slot)
        {
            return slot == WeaponSlot.Primary ? _primary : _secondary;
        }
    }
}

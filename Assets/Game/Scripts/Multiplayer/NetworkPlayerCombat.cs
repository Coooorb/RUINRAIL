using System;
using RuinRail.Gameplay.Combat.Weapons;
using Unity.Netcode;
using UnityEngine;

namespace RuinRail.Networking
{
    /// <summary>
    /// Player combat over NGO (82/23/40/25): the owner's held fire/special travel inside MovementIntent, edge actions
    /// (reload, slot select/swap, consumable, interact) as sequence-deduplicated WeaponCommands; the host runs the
    /// unchanged weapon components (all cooldown/ammo/heat/charge/active-slot rules) and publishes WeaponNetState.
    /// Clients never apply damage (DamageAuthority) — they render replicated projectiles/effects and health.
    /// </summary>
    public sealed class NetworkPlayerCombat : NetworkBehaviour
    {
        private readonly NetworkVariable<WeaponNetState> _state = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private WeaponLoadout _loadout;
        private NetworkPlayerMotion _motion;
        private uint _commandSequence;

        public WeaponNetState State => _state.Value;

        public override void OnNetworkSpawn()
        {
            _loadout = GetComponent<WeaponLoadout>();
            _motion = GetComponent<NetworkPlayerMotion>();
            if (IsServer && !IsOwner && _motion?.RemoteReader != null)
            {
                _loadout?.SetInputReader(_motion.RemoteReader);
                foreach (var weapon in GetComponentsInChildren<RangedWeapon>(true)) weapon.SetInputReader(_motion.RemoteReader);
                foreach (var weapon in GetComponentsInChildren<BlasterWeapon>(true)) weapon.SetInputReader(_motion.RemoteReader);
                foreach (var weapon in GetComponentsInChildren<BowWeapon>(true)) weapon.SetInputReader(_motion.RemoteReader);
                foreach (var weapon in GetComponentsInChildren<MeleeWeapon>(true)) weapon.SetInputReader(_motion.RemoteReader);
            }

            if (IsOwner && !IsServer) SubscribeOwnerCommands();
        }

        private Core.Input.IPlayerInputReader _commandReader;

        /// <summary>The owner's edge presses travel to the host as sequence-numbered commands (one subscription per reader).</summary>
        private void SubscribeOwnerCommands()
        {
            UnsubscribeOwnerCommands();
            _commandReader = GetComponent<Gameplay.Player.PlayerInput>()?.Reader;
            if (_commandReader == null) return;
            _commandReader.Reload += OnReload;
            _commandReader.Weapon1Selected += OnWeapon1;
            _commandReader.Weapon2Selected += OnWeapon2;
            _commandReader.WeaponSwapped += OnSwap;
            _commandReader.ConsumableUsed += OnConsumable;
            _commandReader.Interact += OnInteract;
        }

        /// <summary>A despawned (or no longer owned) character never sends again, whatever reader outlives it.</summary>
        private void UnsubscribeOwnerCommands()
        {
            if (_commandReader == null) return;
            _commandReader.Reload -= OnReload;
            _commandReader.Weapon1Selected -= OnWeapon1;
            _commandReader.Weapon2Selected -= OnWeapon2;
            _commandReader.WeaponSwapped -= OnSwap;
            _commandReader.ConsumableUsed -= OnConsumable;
            _commandReader.Interact -= OnInteract;
            _commandReader = null;
        }

        private void OnReload() => SendCommand(WeaponCommandKind.Reload);
        private void OnWeapon1() => SendCommand(WeaponCommandKind.SelectPrimary);
        private void OnWeapon2() => SendCommand(WeaponCommandKind.SelectSecondary);
        private void OnSwap() => SendCommand(WeaponCommandKind.Swap);
        private void OnConsumable() => SendCommand(WeaponCommandKind.UseConsumable);
        private void OnInteract() => SendCommand(WeaponCommandKind.Interact);

        /// <summary>85: a reclaimed character becomes this client's own after its spawn; its presses reach the host from now on.</summary>
        public override void OnGainedOwnership()
        {
            if (IsServer) return;
            SubscribeOwnerCommands();
        }

        public override void OnLostOwnership() => UnsubscribeOwnerCommands();

        public override void OnNetworkDespawn() => UnsubscribeOwnerCommands();

        private void SendCommand(WeaponCommandKind kind)
        {
            if (!IsSpawned || !IsOwner) return;
            SubmitCommandRpc(new WeaponCommand { Sequence = ++_commandSequence, Kind = kind });
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitCommandRpc(WeaponCommand command)
        {
            _motion?.RemoteReader?.ApplyCommand(command);
        }

        private void LateUpdate()
        {
            if (!IsSpawned || _loadout == null) return;
            if (IsServer)
            {
                _state.Value = WeaponStateSync.Capture(_loadout, _motion?.RemoteReader?.LastSequence ?? 0);
            }
            else if (IsOwner)
            {
                WeaponStateSync.Apply(_loadout, _state.Value);
            }
        }

        private void Update()
        {
            // A pure replica has no loadout of its own: its held-weapon view follows the replicated id (presentation only).
            if (!IsSpawned || IsServer || IsOwner) return;
            var view = GetComponent<IHeldWeaponView>();
            view?.ShowWeapon(_state.Value.ActiveWeaponId.ToString());
        }
    }
}

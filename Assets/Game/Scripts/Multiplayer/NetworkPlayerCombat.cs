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

            if (IsOwner && !IsServer)
            {
                var input = GetComponent<Gameplay.Player.PlayerInput>()?.Reader;
                if (input != null)
                {
                    input.Reload += () => SendCommand(WeaponCommandKind.Reload);
                    input.Weapon1Selected += () => SendCommand(WeaponCommandKind.SelectPrimary);
                    input.Weapon2Selected += () => SendCommand(WeaponCommandKind.SelectSecondary);
                    input.WeaponSwapped += () => SendCommand(WeaponCommandKind.Swap);
                    input.ConsumableUsed += () => SendCommand(WeaponCommandKind.UseConsumable);
                    input.Interact += () => SendCommand(WeaponCommandKind.Interact);
                }
            }
        }

        private void SendCommand(WeaponCommandKind kind)
        {
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

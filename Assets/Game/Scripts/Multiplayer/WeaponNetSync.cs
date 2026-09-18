using System;
using RuinRail.Gameplay.Combat.Weapons;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RuinRail.Networking
{
    /// <summary>
    /// Host-published weapon presentation state (owner HUD + remote replicas): active slot, magazine, reload, heat,
    /// bow charge, melee state. Damage never travels here — outcomes are applied on the host only.
    /// </summary>
    [Serializable]
    public struct WeaponNetState : INetworkSerializable
    {
        public int ActiveSlot;
        public int MagazineAmmo;
        public bool IsReloading;
        public float Heat;
        public bool IsOverheated;
        public float ChargeFraction;
        public int MeleeState;
        public uint LastCommandSequence;
        /// <summary>Definition id of the active weapon, so a replica without a loadout can draw the right held sprite (presentation only).</summary>
        public FixedString32Bytes ActiveWeaponId;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ActiveSlot);
            serializer.SerializeValue(ref MagazineAmmo);
            serializer.SerializeValue(ref IsReloading);
            serializer.SerializeValue(ref Heat);
            serializer.SerializeValue(ref IsOverheated);
            serializer.SerializeValue(ref ChargeFraction);
            serializer.SerializeValue(ref MeleeState);
            serializer.SerializeValue(ref LastCommandSequence);
            serializer.SerializeValue(ref ActiveWeaponId);
        }
    }

    /// <summary>Captures the host's weapon state from a loadout and applies it on the owner for convergence.</summary>
    public static class WeaponStateSync
    {
        public static WeaponNetState Capture(WeaponLoadout loadout, uint lastCommandSequence = 0)
        {
            var state = new WeaponNetState { ActiveSlot = (int)loadout.ActiveSlot, LastCommandSequence = lastCommandSequence };
            switch (loadout.ActiveWeapon)
            {
                case RangedWeapon ranged:
                    state.MagazineAmmo = ranged.MagazineAmmo;
                    state.IsReloading = ranged.IsReloading;
                    state.ActiveWeaponId = new FixedString32Bytes(ranged.Definition != null ? ranged.Definition.Id : string.Empty);
                    break;
                case BlasterWeapon blaster:
                    state.Heat = blaster.Heat.Heat;
                    state.IsOverheated = blaster.Heat.IsOverheated;
                    state.ActiveWeaponId = new FixedString32Bytes(blaster.Definition != null ? blaster.Definition.Id : string.Empty);
                    break;
                case BowWeapon bow:
                    state.ChargeFraction = bow.ChargeFraction;
                    state.ActiveWeaponId = new FixedString32Bytes(bow.Definition != null ? bow.Definition.Id : string.Empty);
                    break;
                case MeleeWeapon melee:
                    state.MeleeState = (int)melee.State;
                    state.ActiveWeaponId = new FixedString32Bytes(melee.Definition != null ? melee.Definition.Id : string.Empty);
                    break;
            }

            return state;
        }

        /// <summary>Owner convergence: slot always follows the host; magazine/heat adopt the host values when they drifted.</summary>
        public static bool Apply(WeaponLoadout loadout, in WeaponNetState state)
        {
            var changed = false;
            var slot = (WeaponSlot)state.ActiveSlot;
            if (loadout.ActiveSlot != slot)
            {
                loadout.SelectSlot(slot);
                changed = true;
            }

            switch (loadout.ActiveWeapon)
            {
                case RangedWeapon ranged when ranged.MagazineAmmo != state.MagazineAmmo || (ranged.IsReloading && !state.IsReloading):
                    ranged.ApplyAuthoritativeState(state.MagazineAmmo, state.IsReloading);
                    changed = true;
                    break;
                case BlasterWeapon blaster when Mathf.Abs(blaster.Heat.Heat - state.Heat) > 0.01f:
                    blaster.Heat.ApplyAuthoritativeHeat(state.Heat);
                    changed = true;
                    break;
            }

            return changed;
        }
    }

}

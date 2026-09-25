using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;

namespace RuinRail.Gameplay.Base
{
    /// <summary>
    /// Free Starter Kit (base/75_STARTER_KIT): P9 Ranger (Primary), Field Knife (Secondary, the ammo-free fallback) and
    /// Scrap Vest forced Common with no affixes and zero resale value, Bandage ×1, Light Ammo ×60; no Accessory. Granted
    /// exactly once on first profile creation (persisted flag) and re-granted only when the player has no weapon left
    /// anywhere, so no profile can softlock, a dry firearm never ends a run, and nothing of value is minted.
    /// </summary>
    public sealed class StarterKitService
    {
        public const string PistolId = "weapon_p9_ranger";
        /// <summary>The approved starter-tier ammo-free melee weapon (33_WEAPON_CATALOG Field Knife): usable at zero reserve of every ammo type.</summary>
        public const string KnifeId = "weapon_field_knife";
        public const string VestId = "armor_scrap_vest";
        public const string BandageId = "consumable_bandage";
        public const string LightAmmoId = "ammo_light";
        public const int BandageCount = 1;
        public const int LightAmmoCount = 60;

        private readonly Func<string, ItemDefinition> _resolveDefinition;
        private readonly Func<AmmoType, AmmoItemDefinition> _resolveAmmo;
        private readonly AmmoBalanceConfig _ammoBalance;

        public StarterKitService(Func<string, ItemDefinition> resolveDefinition, Func<AmmoType, AmmoItemDefinition> resolveAmmo, AmmoBalanceConfig ammoBalance)
        {
            _resolveDefinition = resolveDefinition ?? throw new ArgumentNullException(nameof(resolveDefinition));
            _resolveAmmo = resolveAmmo ?? throw new ArgumentNullException(nameof(resolveAmmo));
            _ammoBalance = ammoBalance;
        }

        /// <summary>The kit as fresh instances: equipment forced Common, affix-free and unsellable.</summary>
        public static List<(ItemInstance item, EquippedSlot? slot)> CreateKit()
        {
            return new List<(ItemInstance, EquippedSlot?)>
            {
                (new ItemInstance(PistolId, 1, Rarity.Common) { IsUnsellable = true }, EquippedSlot.PrimaryWeapon),
                (new ItemInstance(KnifeId, 1, Rarity.Common) { IsUnsellable = true }, EquippedSlot.SecondaryWeapon),
                (new ItemInstance(VestId, 1, Rarity.Common) { IsUnsellable = true }, EquippedSlot.Armor),
                (new ItemInstance(BandageId, BandageCount), EquippedSlot.ActiveConsumable),
                (new ItemInstance(LightAmmoId, LightAmmoCount), null)
            };
        }

        /// <summary>First-profile grant; returns false (and changes nothing) when the profile already received it.</summary>
        public bool GrantFirstProfileKit(PlayerProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (profile.StarterKitGranted) return false;
            MergeKitIntoSafeLoadout(profile);
            profile.StarterKitGranted = true;
            return true;
        }

        /// <summary>True when the player owns no weapon in the safe loadout or storage — the only condition for a re-grant.</summary>
        public bool NeedsRescueKit(PlayerProfile profile, Storage storage)
        {
            if (profile == null) return false;
            var inventory = LoadSafeLoadout(profile);
            var hasWeapon = HasWeapon(inventory.GetEquipped(EquippedSlot.PrimaryWeapon))
                            || HasWeapon(inventory.GetEquipped(EquippedSlot.SecondaryWeapon))
                            || inventory.BackpackSlots.Any(HasWeapon)
                            || (storage != null && storage.Items.Any(HasWeapon));
            return !hasWeapon;
        }

        /// <summary>Softlock prevention before an expedition: re-grants the kit only when no weapon exists anywhere.</summary>
        public bool EnsureStartableLoadout(PlayerProfile profile, Storage storage)
        {
            if (!NeedsRescueKit(profile, storage)) return false;
            MergeKitIntoSafeLoadout(profile);
            return true;
        }

        /// <summary>True when a weapon is equipped in either weapon slot (an equipped item that does not resolve to a weapon counts as absent).</summary>
        public bool HasEquippedWeapon(PlayerInventory loadout)
        {
            return loadout != null && (HasWeapon(loadout.GetEquipped(EquippedSlot.PrimaryWeapon)) || HasWeapon(loadout.GetEquipped(EquippedSlot.SecondaryWeapon)));
        }

        /// <summary>
        /// Starter Loadout fallback at expedition start: when the live Base loadout has no weapon equipped — nothing
        /// equipped, an empty profile, or weapon slots emptied by the save validator's quarantine of unresolved items —
        /// the existing free kit is merged into it (kit pieces fill the empty slots, the rest goes to the backpack) so
        /// the run begins with the authoritative Starter Loadout. A loadout that already has a weapon equipped is never
        /// touched, so an intentional loadout is preserved and a second call in a row changes nothing. Nothing is
        /// written to storage and the equipment copies carry the usual starter restrictions (Common, affix-free,
        /// unsellable).
        /// </summary>
        public bool EnsureEquippedLoadout(PlayerInventory loadout)
        {
            if (loadout == null) throw new ArgumentNullException(nameof(loadout));
            if (HasEquippedWeapon(loadout)) return false;
            MergeKitInto(loadout);
            return true;
        }

        private bool HasWeapon(ItemInstance item)
        {
            return item != null && _resolveDefinition(item.DefinitionId) is { Category: ItemCategory.Weapon };
        }

        private PlayerInventory LoadSafeLoadout(PlayerProfile profile)
        {
            var inventory = new PlayerInventory(_resolveDefinition, _resolveAmmo, _ammoBalance);
            if (profile.SafeLoadout != null) inventory.RestoreFromSnapshot(profile.SafeLoadout);
            return inventory;
        }

        /// <summary>Equips kit pieces into empty slots and puts the rest in the backpack through the normal inventory rules.</summary>
        private void MergeKitIntoSafeLoadout(PlayerProfile profile)
        {
            var inventory = LoadSafeLoadout(profile);
            MergeKitInto(inventory);
            profile.SafeLoadout = inventory.ToSnapshot();
        }

        /// <summary>The one kit merge: equips kit pieces into empty slots and puts the rest in the backpack through the normal inventory rules.</summary>
        private static void MergeKitInto(PlayerInventory inventory)
        {
            foreach (var (item, slot) in CreateKit())
            {
                var placed = slot.HasValue && inventory.GetEquipped(slot.Value) == null && inventory.TryEquip(item, slot.Value);
                if (!placed && !inventory.TryAddToBackpack(item) && item.DefinitionId == LightAmmoId)
                {
                    inventory.Add(AmmoType.Light, LightAmmoCount); // partial fit into existing stacks/space; never fails the grant
                }
            }
        }
    }
}

using RuinRail.Gameplay.Items;

namespace RuinRail.Gameplay.Combat.Weapons
{
    public static class WeaponSlotMapping
    {
        public static EquippedSlot ToEquippedSlot(WeaponSlot slot)
        {
            return slot == WeaponSlot.Primary ? EquippedSlot.PrimaryWeapon : EquippedSlot.SecondaryWeapon;
        }

        public static bool TryToWeaponSlot(EquippedSlot slot, out WeaponSlot weaponSlot)
        {
            switch (slot)
            {
                case EquippedSlot.PrimaryWeapon:
                    weaponSlot = WeaponSlot.Primary;
                    return true;
                case EquippedSlot.SecondaryWeapon:
                    weaponSlot = WeaponSlot.Secondary;
                    return true;
                default:
                    weaponSlot = WeaponSlot.Primary;
                    return false;
            }
        }
    }
}

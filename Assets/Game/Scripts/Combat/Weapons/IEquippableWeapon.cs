namespace RuinRail.Gameplay.Combat.Weapons
{
    public interface IEquippableWeapon
    {
        /// <summary>True while this weapon is the loadout's active weapon (the only one that consumes attack/reload/special input).</summary>
        bool IsEquipped { get; }
        void OnEquipped();
        void OnUnequipped();
    }
}

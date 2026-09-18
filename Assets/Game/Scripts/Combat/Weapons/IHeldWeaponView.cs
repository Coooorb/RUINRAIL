namespace RuinRail.Gameplay.Combat.Weapons
{
    /// <summary>
    /// Presentation seam for "which weapon is in this actor's hands". A local actor's view reads its own
    /// <see cref="WeaponLoadout"/>; a pure network replica has no loadout, so the networking layer hands it the
    /// replicated active weapon id through this interface without knowing how it is drawn.
    /// </summary>
    public interface IHeldWeaponView
    {
        /// <summary>Shows the weapon with this definition id (null/empty = empty hands). Idempotent.</summary>
        void ShowWeapon(string definitionId);

        /// <summary>The definition id currently shown, or null.</summary>
        string ShownWeaponId { get; }
    }
}

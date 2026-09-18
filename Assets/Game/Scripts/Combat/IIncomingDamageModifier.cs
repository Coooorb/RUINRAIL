namespace RuinRail.Gameplay.Combat
{
    /// <summary>Adjusts damage before it reaches health (player damage reduction). Returns the integer amount to apply.</summary>
    public interface IIncomingDamageModifier
    {
        int ModifyIncomingDamage(DamageRequest request);
    }
}

namespace RuinRail.Gameplay.Combat
{
    public interface IDamageable
    {
        bool TryApplyDamage(DamageRequest request);
    }
}

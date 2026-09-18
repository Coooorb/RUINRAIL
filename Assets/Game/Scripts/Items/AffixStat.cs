namespace RuinRail.Gameplay.Items
{
    /// <summary>
    /// Stats a random affix may modify. Deliberately contains no critical-hit or weak-spot entries (GDD 22).
    /// </summary>
    public enum AffixStat
    {
        Damage,
        FireRate,
        ReloadSpeed,
        MagazineSize,
        ProjectileSpeed,
        Range,
        Knockback,
        StaggerPower,
        MeleeAttackSpeed,
        BlasterCoolingRate,
        BlasterHeatPerShot,
        BowChargeSpeed,
        MovementSpeed,
        DamageReduction,
        DashCooldownReduction,
        DashDistance,
        WeaponSwitchSpeed,
        HealingReceived,
        KnockbackResistance,
        StaggerResistance,
        MaxHealth
    }
}

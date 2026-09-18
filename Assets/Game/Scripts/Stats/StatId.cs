using RuinRail.Gameplay.Items;

namespace RuinRail.Gameplay.Stats
{
    /// <summary>
    /// Stable ids for every player stat any source may modify. Percent stats are integer percent points; flat stats
    /// are integer units. There are no crit, weak-spot, mana or stamina stats.
    /// </summary>
    public enum StatId
    {
        MaxHealth,
        MovementSpeed,
        GeneralDamageReduction,
        ExplosionDamageReduction,
        WeaponDamage,
        FireRate,
        ReloadSpeed,
        MagazineSize,
        WeaponSwitchSpeed,
        DashCooldownReduction,
        DashDistance,
        MeleeAttackSpeed,
        ProjectileRange,
        ProjectileSpeed,
        HealingReceived,
        BlasterCoolingRate,
        BlasterHeatPerShotReduction,
        BowChargeSpeed,
        Knockback,
        StaggerPower,
        KnockbackResistance,
        StaggerResistance,
        AmmoStackCapacity,
        PickupAttractionRadius,
        WeaponSpreadReduction
    }

    public static class StatIds
    {
        /// <summary>Bridges affix stats (item data) onto the stat pipeline.</summary>
        public static StatId FromAffix(AffixStat stat)
        {
            return stat switch
            {
                AffixStat.Damage => StatId.WeaponDamage,
                AffixStat.FireRate => StatId.FireRate,
                AffixStat.ReloadSpeed => StatId.ReloadSpeed,
                AffixStat.MagazineSize => StatId.MagazineSize,
                AffixStat.ProjectileSpeed => StatId.ProjectileSpeed,
                AffixStat.Range => StatId.ProjectileRange,
                AffixStat.Knockback => StatId.Knockback,
                AffixStat.StaggerPower => StatId.StaggerPower,
                AffixStat.MeleeAttackSpeed => StatId.MeleeAttackSpeed,
                AffixStat.BlasterCoolingRate => StatId.BlasterCoolingRate,
                AffixStat.BlasterHeatPerShot => StatId.BlasterHeatPerShotReduction,
                AffixStat.BowChargeSpeed => StatId.BowChargeSpeed,
                AffixStat.MovementSpeed => StatId.MovementSpeed,
                AffixStat.DamageReduction => StatId.GeneralDamageReduction,
                AffixStat.DashCooldownReduction => StatId.DashCooldownReduction,
                AffixStat.DashDistance => StatId.DashDistance,
                AffixStat.WeaponSwitchSpeed => StatId.WeaponSwitchSpeed,
                AffixStat.HealingReceived => StatId.HealingReceived,
                AffixStat.KnockbackResistance => StatId.KnockbackResistance,
                AffixStat.StaggerResistance => StatId.StaggerResistance,
                AffixStat.MaxHealth => StatId.MaxHealth,
                _ => StatId.WeaponDamage
            };
        }
    }
}

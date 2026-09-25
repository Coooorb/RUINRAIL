namespace RuinRail.Gameplay.Stats
{
    /// <summary>Player-facing names of the pipeline stats (ui/93): shared by tooltips and item descriptions so the same stat never reads two ways.</summary>
    public static class StatLabels
    {
        public static string Of(StatId stat) => stat switch
        {
            StatId.MaxHealth => "Max HP",
            StatId.MovementSpeed => "Movement Speed",
            StatId.GeneralDamageReduction => "Damage Reduction",
            StatId.ExplosionDamageReduction => "Explosion Damage Reduction",
            StatId.WeaponDamage => "Weapon Damage",
            StatId.FireRate => "Fire Rate",
            StatId.ReloadSpeed => "Reload Speed",
            StatId.MagazineSize => "Magazine Size",
            StatId.WeaponSwitchSpeed => "Weapon Switch Speed",
            StatId.DashCooldownReduction => "Dash Cooldown Reduction",
            StatId.DashDistance => "Dash Distance",
            StatId.MeleeAttackSpeed => "Melee Attack Speed",
            StatId.ProjectileRange => "Projectile Range",
            StatId.ProjectileSpeed => "Projectile Speed",
            StatId.HealingReceived => "Healing Received",
            StatId.BlasterCoolingRate => "Blaster Cooling Rate",
            StatId.BlasterHeatPerShotReduction => "Blaster Heat per Shot",
            StatId.BowChargeSpeed => "Bow Charge Speed",
            StatId.Knockback => "Knockback",
            StatId.StaggerPower => "Stagger Power",
            StatId.KnockbackResistance => "Knockback Resistance",
            StatId.StaggerResistance => "Stagger Resistance",
            StatId.AmmoStackCapacity => "Ammo Stack Capacity",
            StatId.PickupAttractionRadius => "Pickup Attraction Radius",
            StatId.WeaponSpreadReduction => "Weapon Spread Reduction",
            _ => stat.ToString()
        };

        /// <summary>"+20%" / "-4%" for percent modifiers, "+3" / "-2" for flat ones (with the flat unit when known).</summary>
        public static string Format(StatModifier modifier)
        {
            var sign = modifier.Value >= 0 ? "+" : string.Empty;
            if (modifier.Kind == StatModifierKind.Percent) return $"{sign}{modifier.Value}%";
            var unit = modifier.Stat == StatId.PickupAttractionRadius ? " tiles" : string.Empty;
            return $"{sign}{modifier.Value}{unit}";
        }
    }
}

using UnityEngine;

namespace RuinRail.Gameplay.Items
{
    /// <summary>
    /// Approved V1 global stat caps (player/16_GLOBAL_STAT_CAPS.md), applied once after all bonus sources are summed.
    /// Percent points; a cap of 0 means "no cap defined for this stat".
    /// </summary>
    [CreateAssetMenu(menuName = "RuinRail/Balance/Global Stat Caps", fileName = "GlobalStatCapsConfig")]
    public sealed class GlobalStatCapsConfig : ScriptableObject
    {
        [SerializeField] private int _movementSpeedBonus = 30;
        [SerializeField] private int _generalDamageReduction = 40;
        [SerializeField] private int _generalDamageReductionWithArmorInjector = 50;
        [SerializeField] private int _weaponDamageBonus = 50;
        [SerializeField] private int _reloadSpeedBonus = 40;
        [SerializeField] private int _weaponSwitchSpeedBonus = 50;
        [SerializeField] private int _dashCooldownReduction = 35;
        [SerializeField] private int _dashDistanceBonus = 30;
        [SerializeField] private int _meleeAttackSpeedBonus = 30;
        [SerializeField] private int _projectileRangeBonus = 40;
        [SerializeField] private int _projectileSpeedBonus = 40;
        [SerializeField] private int _healingReceivedBonus = 50;
        [SerializeField] private int _blasterCoolingRateBonus = 50;
        [SerializeField] private int _blasterHeatPerShotReduction = 30;
        [SerializeField] private int _bowChargeSpeedBonus = 35;
        [SerializeField] private int _knockbackResistance = 50;
        [SerializeField] private int _staggerResistance = 50;

        public int GeneralDamageReductionWithArmorInjector => _generalDamageReductionWithArmorInjector;

        /// <summary>Cap in percent points for a pipeline stat (0 = uncapped). General DR uses the temporary 50% cap while the Armor Injector is active.</summary>
        public int GetCapPercent(RuinRail.Gameplay.Stats.StatId stat, bool armorInjectorActive = false)
        {
            return stat switch
            {
                RuinRail.Gameplay.Stats.StatId.MovementSpeed => _movementSpeedBonus,
                RuinRail.Gameplay.Stats.StatId.GeneralDamageReduction => armorInjectorActive ? _generalDamageReductionWithArmorInjector : _generalDamageReduction,
                RuinRail.Gameplay.Stats.StatId.WeaponDamage => _weaponDamageBonus,
                RuinRail.Gameplay.Stats.StatId.ReloadSpeed => _reloadSpeedBonus,
                RuinRail.Gameplay.Stats.StatId.WeaponSwitchSpeed => _weaponSwitchSpeedBonus,
                RuinRail.Gameplay.Stats.StatId.DashCooldownReduction => _dashCooldownReduction,
                RuinRail.Gameplay.Stats.StatId.DashDistance => _dashDistanceBonus,
                RuinRail.Gameplay.Stats.StatId.MeleeAttackSpeed => _meleeAttackSpeedBonus,
                RuinRail.Gameplay.Stats.StatId.ProjectileRange => _projectileRangeBonus,
                RuinRail.Gameplay.Stats.StatId.ProjectileSpeed => _projectileSpeedBonus,
                RuinRail.Gameplay.Stats.StatId.HealingReceived => _healingReceivedBonus,
                RuinRail.Gameplay.Stats.StatId.BlasterCoolingRate => _blasterCoolingRateBonus,
                RuinRail.Gameplay.Stats.StatId.BlasterHeatPerShotReduction => _blasterHeatPerShotReduction,
                RuinRail.Gameplay.Stats.StatId.BowChargeSpeed => _bowChargeSpeedBonus,
                RuinRail.Gameplay.Stats.StatId.KnockbackResistance => _knockbackResistance,
                RuinRail.Gameplay.Stats.StatId.StaggerResistance => _staggerResistance,
                _ => 0
            };
        }

        public int GetCapPercent(AffixStat stat)
        {
            return stat switch
            {
                AffixStat.MovementSpeed => _movementSpeedBonus,
                AffixStat.DamageReduction => _generalDamageReduction,
                AffixStat.Damage => _weaponDamageBonus,
                AffixStat.ReloadSpeed => _reloadSpeedBonus,
                AffixStat.WeaponSwitchSpeed => _weaponSwitchSpeedBonus,
                AffixStat.DashCooldownReduction => _dashCooldownReduction,
                AffixStat.DashDistance => _dashDistanceBonus,
                AffixStat.MeleeAttackSpeed => _meleeAttackSpeedBonus,
                AffixStat.Range => _projectileRangeBonus,
                AffixStat.ProjectileSpeed => _projectileSpeedBonus,
                AffixStat.HealingReceived => _healingReceivedBonus,
                AffixStat.BlasterCoolingRate => _blasterCoolingRateBonus,
                AffixStat.BlasterHeatPerShot => _blasterHeatPerShotReduction,
                AffixStat.BowChargeSpeed => _bowChargeSpeedBonus,
                AffixStat.KnockbackResistance => _knockbackResistance,
                AffixStat.StaggerResistance => _staggerResistance,
                _ => 0
            };
        }
    }
}

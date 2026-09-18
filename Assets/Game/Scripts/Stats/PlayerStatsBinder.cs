using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.Gameplay.Stats
{
    /// <summary>
    /// Player-side composition root for the stat pipeline: owns the PlayerStats instance, hands it to movement, dash,
    /// health and every ranged weapon on the player, applies incoming-damage reduction and keeps max health in sync.
    /// Systems read final values from the provider; none of them contains cap logic.
    /// </summary>
    public sealed class PlayerStatsBinder : MonoBehaviour, IIncomingDamageModifier
    {
        [SerializeField] private GlobalStatCapsConfig _caps;
        [SerializeField] private PlayerBalanceConfig _balanceConfig;

        private PlayerStats _stats;
        private HealthComponent _health;

        public PlayerStats Stats => _stats ??= new PlayerStats(_caps, _balanceConfig != null ? _balanceConfig.MaxHealth : 100);

        public void Configure(GlobalStatCapsConfig caps, PlayerBalanceConfig balanceConfig)
        {
            _caps = caps;
            _balanceConfig = balanceConfig;
            _stats = null;
            Bind();
        }

        private void Awake()
        {
            Bind();
        }

        private void OnDestroy()
        {
            if (_stats != null) _stats.Recomputed -= SyncMaxHealth;
        }

        /// <summary>Wires every stat consumer found on this object. Safe to call again after components are added.</summary>
        public void Bind()
        {
            var stats = Stats;
            stats.Recomputed -= SyncMaxHealth;
            stats.Recomputed += SyncMaxHealth;

            GetComponent<PlayerMovement>()?.SetStats(stats);
            GetComponent<PlayerDash>()?.SetStats(stats);
            GetComponent<PickupAttractor>()?.SetStats(stats);
            var impact = GetComponent<PlayerImpactReceiver>();
            impact?.SetStats(stats);
            foreach (var weapon in GetComponentsInChildren<RangedWeapon>(true))
            {
                weapon.SetStats(stats);
                weapon.SetImpactFeedback(impact);
            }

            foreach (var weapon in GetComponentsInChildren<BlasterWeapon>(true))
            {
                weapon.SetStats(stats);
                weapon.SetImpactFeedback(impact);
            }

            foreach (var weapon in GetComponentsInChildren<BowWeapon>(true))
            {
                weapon.SetStats(stats);
                weapon.SetImpactFeedback(impact);
            }

            foreach (var weapon in GetComponentsInChildren<MeleeWeapon>(true))
            {
                weapon.SetStats(stats);
                weapon.SetImpactFeedback(impact);
            }

            _health = GetComponent<HealthComponent>();
            if (_health != null)
            {
                _health.SetIncomingDamageModifier(this);
                SyncMaxHealth();
            }
        }

        public int ModifyIncomingDamage(DamageRequest request)
        {
            return Stats.ApplyDamageReduction(request.Amount, request.IsExplosion);
        }

        private void SyncMaxHealth()
        {
            if (_health != null && _health.MaxHealth != Stats.MaxHealth)
            {
                _health.ResizeMaxHealth(Stats.MaxHealth);
            }
        }
    }
}

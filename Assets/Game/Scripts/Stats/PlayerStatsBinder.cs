using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Progression;
using UnityEngine;

namespace RuinRail.Gameplay.Stats
{
    /// <summary>
    /// Player-side composition root for the stat pipeline: owns the PlayerStats instance, hands it to movement, dash,
    /// health and every ranged weapon on the player, applies incoming-damage reduction and keeps max health in sync.
    /// Systems read final values from the provider; none of them contains cap logic.
    ///
    /// The permanent attribute ranks (player/13) are held here rather than registered once by a caller, because
    /// <see cref="Configure"/> rebuilds the PlayerStats instance: owning the allocation is what guarantees that no later
    /// composition step can silently drop the progression source. Registration is by the stable
    /// <see cref="SkillStatSource.Id"/> key, so re-applying is idempotent and can never double a bonus.
    /// </summary>
    public sealed class PlayerStatsBinder : MonoBehaviour, IIncomingDamageModifier
    {
        [SerializeField] private GlobalStatCapsConfig _caps;
        [SerializeField] private PlayerBalanceConfig _balanceConfig;

        private PlayerStats _stats;
        private HealthComponent _health;
        private SkillAllocation _progression;

        public PlayerStats Stats
        {
            get
            {
                if (_stats != null) return _stats;
                _stats = new PlayerStats(_caps, _balanceConfig != null ? _balanceConfig.MaxHealth : 100);
                ApplyProgressionSource();
                return _stats;
            }
        }

        /// <summary>The permanent attribute ranks feeding this player's pipeline, or null when none were applied.</summary>
        public SkillAllocation Progression => _progression;

        public void Configure(GlobalStatCapsConfig caps, PlayerBalanceConfig balanceConfig)
        {
            _caps = caps;
            _balanceConfig = balanceConfig;
            _stats = null;
            Bind();
        }

        /// <summary>
        /// Registers this player's permanent attribute ranks as the pipeline's "skills" source (player/12 step 2, before
        /// armor/accessory/affix sources, which sum independently). The allocation is held live: a later
        /// <see cref="Configure"/> re-registers it. Null removes the source. Max health is re-synced, which only ever
        /// resizes the ceiling — it never grants or takes current HP.
        /// </summary>
        public void ApplyProgression(SkillAllocation allocation)
        {
            _progression = allocation;
            _ = Stats;
            ApplyProgressionSource();
            SyncMaxHealth();
        }

        private void ApplyProgressionSource()
        {
            if (_stats == null) return;
            if (_progression == null) _stats.RemoveSource(SkillStatSource.Id);
            else _stats.SetSource(new SkillStatSource(_progression));
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
                weapon.SetCombatEvents(impact != null ? impact.Events : null);
            }

            foreach (var weapon in GetComponentsInChildren<BlasterWeapon>(true))
            {
                weapon.SetStats(stats);
                weapon.SetImpactFeedback(impact);
                weapon.SetCombatEvents(impact != null ? impact.Events : null);
            }

            foreach (var weapon in GetComponentsInChildren<BowWeapon>(true))
            {
                weapon.SetStats(stats);
                weapon.SetImpactFeedback(impact);
                weapon.SetCombatEvents(impact != null ? impact.Events : null);
            }

            foreach (var weapon in GetComponentsInChildren<MeleeWeapon>(true))
            {
                weapon.SetStats(stats);
                weapon.SetImpactFeedback(impact);
                weapon.SetCombatEvents(impact != null ? impact.Events : null);
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

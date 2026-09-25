using System;
using RuinRail.Gameplay.Stats;
using UnityEngine;

namespace RuinRail.Gameplay.Items.Passives
{
    /// <summary>Requests a passive may issue to the world; the player composition supplies the implementations.</summary>
    public sealed class PassiveWorldActions
    {
        public static readonly PassiveWorldActions None = new();

        /// <summary>Damage every enemy within radius (tiles) around the wearer for an integer roll in [min, max].</summary>
        public Action<float, int, int> AreaDamage = (_, _, _) => { };

        /// <summary>Shockwave around the wearer: knockback/stagger pressure only (no damage).</summary>
        public Action<float, float, float> Shockwave = (_, _, _) => { };

        /// <summary>Pull remaining Coin and Ammo pickups of the current room to the wearer.</summary>
        public Action PullCoinAndAmmoPickups = () => { };
    }

    /// <summary>What a passive may touch: the stat pipeline, the event hub and a few explicit player/world actions.</summary>
    public sealed class PassiveContext
    {
        public PassiveContext(PlayerStats stats, PlayerCombatEvents events, Func<int> currentHealth, Func<int> maxHealth, Func<int, bool> heal, Action resetDashCooldown, PassiveWorldActions world = null)
        {
            Stats = stats ?? throw new ArgumentNullException(nameof(stats));
            Events = events ?? throw new ArgumentNullException(nameof(events));
            CurrentHealth = currentHealth ?? (() => 0);
            MaxHealth = maxHealth ?? (() => 0);
            Heal = heal ?? (_ => false);
            ResetDashCooldown = resetDashCooldown ?? (() => { });
            World = world ?? PassiveWorldActions.None;
        }

        public PlayerStats Stats { get; }
        public PlayerCombatEvents Events { get; }
        public Func<int> CurrentHealth { get; }
        public Func<int> MaxHealth { get; }
        public Func<int, bool> Heal { get; }
        public Action ResetDashCooldown { get; }
        public PassiveWorldActions World { get; }
    }

    /// <summary>
    /// A fixed Legendary equipment passive (armor 28, accessory 34). Attached while Legendary gear of its family is
    /// equipped, detached on unequip (removing every temporary source it created). Timed effects advance via Tick.
    /// </summary>
    public abstract class EquipmentPassive
    {
        protected PassiveContext Context { get; private set; }

        public abstract string Id { get; }

        /// <summary>
        /// The player-facing effect text of this passive, written next to the constants it quotes so the tooltip can
        /// never drift from the mechanic (items/28, 34). One sentence; numbers are the passive's own.
        /// </summary>
        public abstract string Description { get; }

        public bool IsAttached => Context != null;

        public void Attach(PassiveContext context)
        {
            if (Context != null) return;
            Context = context ?? throw new ArgumentNullException(nameof(context));
            OnAttach();
        }

        public void Detach()
        {
            if (Context == null) return;
            OnDetach();
            Context = null;
        }

        public virtual void Tick(float deltaTime)
        {
        }

        protected abstract void OnAttach();
        protected abstract void OnDetach();

        /// <summary>Helper for "+X% stat for N seconds" effects: one source id, refreshed on re-trigger.</summary>
        protected sealed class TimedBuff
        {
            private readonly PlayerStats _stats;
            private readonly string _sourceId;
            private readonly StatModifier[] _modifiers;
            private float _remaining;

            public TimedBuff(PlayerStats stats, string sourceId, params StatModifier[] modifiers)
            {
                _stats = stats;
                _sourceId = sourceId;
                _modifiers = modifiers;
            }

            public bool IsActive => _remaining > 0f;
            public float Remaining => _remaining;

            public void Trigger(float duration)
            {
                _remaining = duration;
                _stats.SetSource(new StatModifierSource(_sourceId, _modifiers));
            }

            public void Tick(float deltaTime)
            {
                if (_remaining <= 0f) return;
                _remaining -= deltaTime;
                if (_remaining <= 0f) Clear();
            }

            public void Clear()
            {
                _remaining = 0f;
                _stats.RemoveSource(_sourceId);
            }
        }
    }

    /// <summary>Marker base for armor passives (28_ARMOR_CATALOG).</summary>
    public abstract class ArmorPassive : EquipmentPassive
    {
    }

    /// <summary>Marker base for accessory passives (34_LEGENDARY_ACCESSORY_PASSIVES). Never RMB/LT.</summary>
    public abstract class AccessoryPassive : EquipmentPassive
    {
    }
}

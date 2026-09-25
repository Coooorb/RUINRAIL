using System;
using System.Collections.Generic;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.UI.Onboarding
{
    /// <summary>
    /// Connects the first-expedition prompts (ui/95) to their contexts and completions. Triggers come from observed
    /// gameplay events (expedition start, first enemy, first attack telegraph, first loot, empty magazine, second weapon,
    /// low health with a consumable, transit after the boss); completions come from the local player's own input. The
    /// binder only reads; it never changes gameplay state and never runs on remote replicas.
    /// </summary>
    public sealed class ExpeditionTutorialBinder : IDisposable
    {
        /// <summary>Healing becomes relevant at or below this fraction of max health (V1 FINAL (TASK 179) tunable for the prompt only; not a balance value).</summary>
        public const float ConsumablePromptHealthFraction = 0.5f;

        /// <summary>
        /// Ammo becomes a thing worth teaching at or below this many magazines' worth of total rounds (magazine plus
        /// reserve) for the weapon in hand.
        ///
        /// Two magazines is deliberately early: it lands while the player still has rounds to spend and a decision to
        /// make, not in the boss fight where the lesson arrives too late to act on. It teaches nothing about melee
        /// being required — the copy says use both weapons — and it changes no ammo value, drop or cap.
        /// </summary>
        public const float LowAmmoMagazineThreshold = 2f;

        private readonly TutorialPromptService _prompts;
        private readonly IPlayerInputReader _reader;
        private readonly List<Action> _unsubscribe = new();
        private HealthComponent _health;
        private PlayerInventory _inventory;
        private WeaponLoadout _loadout;
        private bool _moved;
        private bool _aimed;
        private bool _hasFired;
        private Vector2 _initialAim;
        private bool _aimSampled;

        public ExpeditionTutorialBinder(TutorialPromptService prompts, IPlayerInputReader reader)
        {
            _prompts = prompts ?? throw new ArgumentNullException(nameof(prompts));
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _reader.Dash += OnDash;
            _reader.Interact += OnInteract;
            _reader.InventoryToggled += OnInventory;
            _reader.Reload += OnReload;
            _reader.WeaponSwapped += OnWeaponSwap;
            _reader.ConsumableUsed += OnConsumable;
        }

        public TutorialPromptService Prompts => _prompts;

        // ---- Attachments (each optional; scene wiring attaches what exists) ----

        public ExpeditionTutorialBinder Attach(ExpeditionService expedition)
        {
            if (expedition == null) return this;
            Action<ExpeditionState> started = _ => ObserveExpeditionStarted();
            Action<TransitDecision> transit = ObserveTransitOpened;
            expedition.ExpeditionStarted += started;
            expedition.TransitOpened += transit;
            _unsubscribe.Add(() => { expedition.ExpeditionStarted -= started; expedition.TransitOpened -= transit; });
            return this;
        }

        public ExpeditionTutorialBinder Attach(HealthComponent health, PlayerInventory inventory)
        {
            _health = health;
            _inventory = inventory;
            if (health != null)
            {
                Action<int> damaged = _ => ObserveDamaged();
                health.Damaged += damaged;
                _unsubscribe.Add(() => health.Damaged -= damaged);
            }

            if (inventory != null)
            {
                Action<EquippedSlot, ItemInstance> equipped = (_, _) => ObserveEquipmentChanged();
                inventory.EquippedChanged += equipped;
                _unsubscribe.Add(() => inventory.EquippedChanged -= equipped);
            }

            return this;
        }

        public ExpeditionTutorialBinder Attach(WeaponLoadout loadout)
        {
            _loadout = loadout;
            return this;
        }

        /// <summary>An enemy that can attack: its first telegraph is the "dangerous attack" that teaches Dash.</summary>
        public ExpeditionTutorialBinder Attach(MovesetActorController enemy)
        {
            if (enemy == null) return this;
            ObserveEnemySpawned();
            Action<MovesetActorController, EnemyAttackDefinition> telegraph = (_, _) => ObserveAttackTelegraph();
            enemy.AttackTelegraphStarted += telegraph;
            _unsubscribe.Add(() => enemy.AttackTelegraphStarted -= telegraph);
            return this;
        }

        public ExpeditionTutorialBinder Attach(WorldItemPickup pickup)
        {
            if (pickup == null) return this;
            ObserveLootSpawned();
            Action<WorldItemPickup> picked = _ => _prompts.Complete(TutorialPromptId.PickupInventory);
            pickup.PickedUp += picked;
            _unsubscribe.Add(() => pickup.PickedUp -= picked);
            return this;
        }

        // ---- Contexts (also callable directly by scene wiring that owns the event) ----

        public void ObserveExpeditionStarted()
        {
            _moved = false;
            _aimed = false;
            _aimSampled = false;
            _prompts.Trigger(TutorialPromptId.MoveAim);
        }

        public void ObserveEnemySpawned() => _prompts.Trigger(TutorialPromptId.Fire);

        public void ObserveAttackTelegraph() => _prompts.Trigger(TutorialPromptId.Dash);

        public void ObserveLootSpawned() => _prompts.Trigger(TutorialPromptId.PickupInventory);

        public void ObserveMagazineEmpty() => _prompts.Trigger(TutorialPromptId.Reload);

        public void ObserveTransitOpened(TransitDecision decision)
        {
            if (_prompts.Trigger(TutorialPromptId.Transit) && decision != null)
            {
                Action<TransitDecision, TransitChoice> resolved = (_, _) => _prompts.Complete(TutorialPromptId.Transit);
                decision.Resolved += resolved;
                _unsubscribe.Add(() => decision.Resolved -= resolved);
            }
        }

        public void ObserveDamaged()
        {
            if (_health == null || !_health.IsAlive) return;
            if (_health.CurrentHealth > _health.MaxHealth * ConsumablePromptHealthFraction) return;
            if (_inventory != null && _inventory.GetEquipped(EquippedSlot.ActiveConsumable) == null) return;
            _prompts.Trigger(TutorialPromptId.Consumable);
        }

        public void ObserveEquipmentChanged()
        {
            if (_inventory == null) return;
            if (_inventory.GetEquipped(EquippedSlot.PrimaryWeapon) != null && _inventory.GetEquipped(EquippedSlot.SecondaryWeapon) != null) _prompts.Trigger(TutorialPromptId.WeaponSwap);
        }

        /// <summary>Per frame: movement/aim completion and the empty-magazine context from the active ranged weapon.</summary>
        public void Tick()
        {
            if (_prompts.Active == TutorialPromptId.MoveAim || !_prompts.IsSeen(TutorialPromptId.MoveAim))
            {
                if (_reader.Move.sqrMagnitude > 0.01f) _moved = true;
                if (!_aimSampled) { _initialAim = _reader.Aim; _aimSampled = true; }
                else if ((_reader.Aim - _initialAim).sqrMagnitude > 0.01f) _aimed = true;
                if (_moved && _aimed && _prompts.Active == TutorialPromptId.MoveAim) _prompts.Complete(TutorialPromptId.MoveAim);
            }

            if (_reader.FireHeld && _prompts.Active == TutorialPromptId.Fire) _prompts.Complete(TutorialPromptId.Fire);

            if (_loadout != null && _loadout.ActiveWeapon is RangedWeapon ranged && ranged.MagazineAmmo <= 0 && !ranged.IsReloading && !_prompts.IsSeen(TutorialPromptId.Reload)) ObserveMagazineEmpty();

            if (_reader.FireHeld) _hasFired = true;
            ObserveAmmoReserve();
        }

        /// <summary>
        /// The low-ammo context: the weapon in hand is a firearm, the player has actually been shooting, a second
        /// weapon is equipped so the advice is something they can do right now, and the rounds they hold are down to
        /// <see cref="LowAmmoMagazineThreshold"/> magazines. The prompt service does the rest — it is shown once and
        /// never again, it is silent when prompts are off in Settings, and it queues rather than stacking.
        /// </summary>
        public void ObserveAmmoReserve()
        {
            if (!_hasFired || _inventory == null || _loadout == null || _prompts.IsSeen(TutorialPromptId.LowAmmo)) return;
            if (_loadout.ActiveWeapon is not RangedWeapon weapon || weapon.Definition == null) return;
            // Nothing to swap to is nothing to teach.
            if (_inventory.GetEquipped(EquippedSlot.PrimaryWeapon) == null || _inventory.GetEquipped(EquippedSlot.SecondaryWeapon) == null) return;
            var magazine = weapon.CurrentMagazineSize;
            if (magazine <= 0) return;
            var rounds = weapon.MagazineAmmo + _inventory.Get(weapon.Definition.AmmoType);
            if (rounds > magazine * LowAmmoMagazineThreshold) return;
            _prompts.Trigger(TutorialPromptId.LowAmmo);
        }

        // ---- Completions from the local player's input ----

        private void OnDash() => CompleteIfActive(TutorialPromptId.Dash);
        private void OnInteract() => CompleteIfActive(TutorialPromptId.PickupInventory);
        private void OnInventory() => CompleteIfActive(TutorialPromptId.PickupInventory);
        private void OnReload() => CompleteIfActive(TutorialPromptId.Reload);
        private void OnWeaponSwap() { CompleteIfActive(TutorialPromptId.WeaponSwap); CompleteIfActive(TutorialPromptId.LowAmmo); }
        private void OnConsumable() => CompleteIfActive(TutorialPromptId.Consumable);

        private void CompleteIfActive(TutorialPromptId id)
        {
            if (_prompts.Active == id) _prompts.Complete(id);
        }

        public void Dispose()
        {
            _reader.Dash -= OnDash;
            _reader.Interact -= OnInteract;
            _reader.InventoryToggled -= OnInventory;
            _reader.Reload -= OnReload;
            _reader.WeaponSwapped -= OnWeaponSwap;
            _reader.ConsumableUsed -= OnConsumable;
            foreach (var u in _unsubscribe) u();
            _unsubscribe.Clear();
        }
    }
}

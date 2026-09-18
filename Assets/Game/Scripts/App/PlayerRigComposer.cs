using System;
using System.Collections.Generic;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Combat.Weapons.Specials;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Passives;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Stats;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// Boot-flow composition of the local player for an expedition: the entity from PlayerEntityBuilder, weapons
    /// mounted from the at-risk inventory (Primary/Secondary, class-agnostic), stats/passives from the equipped items,
    /// consumables, loot receiver and the Legendary special. Re-mounts weapons when the inventory's weapon slots change.
    /// Everything comes from the existing services; nothing here decides gameplay.
    /// </summary>
    public sealed class PlayerRig : IDisposable
    {
        private readonly GameContentCatalog _content;
        private readonly ItemDefinitionRegistry _registry;
        private readonly LegendarySpecialRegistry _specials;
        private readonly List<Component> _mounted = new();
        private LoadoutStatRegistrar _statRegistrar;
        private EquipmentPassiveRegistrar _passives;

        public PlayerRig(GameContentCatalog content, ItemDefinitionRegistry registry, LegendarySpecialRegistry specials)
        {
            _content = content;
            _registry = registry;
            _specials = specials;
        }

        public GameObject Player { get; private set; }
        public IPlayerInputReader Reader { get; private set; }
        public PlayerInventory Inventory { get; private set; }
        public WeaponLoadout Loadout { get; private set; }
        public ProjectilePool Projectiles { get; private set; }
        public PlayerStatsBinder StatsBinder { get; private set; }
        public PlayerCombatEvents CombatEvents { get; } = new();
        public LegendarySpecialController Special { get; private set; }
        public int Remounts { get; private set; }

        public GameObject Build(ExpeditionState state, PartyLifeRoster roster, string participantId, Vector2 position, IPlayerInputReader reader = null)
        {
            Inventory = state.Inventory;
            Player = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options
            {
                Name = "Player",
                IsLocal = true,
                InputReader = reader,
                BalanceConfig = _content.PlayerBalance,
                Caps = _content.StatCaps,
                Position = position,
                LifeRoster = roster,
                ParticipantId = participantId
            });
            Reader = reader ?? Player.GetComponent<PlayerInput>()?.Reader;
            Projectiles = Player.AddComponent<ProjectilePool>();
            StatsBinder = Player.GetComponent<PlayerStatsBinder>();
            Loadout = Player.AddComponent<WeaponLoadout>();
            Loadout.SetInputReader(Reader);
            Special = Player.AddComponent<LegendarySpecialController>();

            var receiver = Player.GetComponent<PlayerLootReceiver>();
            receiver.SetInventory(Inventory);
            receiver.SetWallet(state.CarriedWallet);

            var health = Player.GetComponent<HealthComponent>();
            var stats = StatsBinder.Stats;
            _statRegistrar = new LoadoutStatRegistrar(Inventory, stats, Resolve, ResolveAffix);
            _passives = new EquipmentPassiveRegistrar(Inventory, Resolve, new PassiveContext(stats, CombatEvents, () => health.CurrentHealth, () => health.MaxHealth, amount => health.Heal(amount), () => Player.GetComponent<PlayerDash>()?.ResetCooldown()));
            var consumables = Player.AddComponent<PlayerConsumableUser>();
            consumables.Configure(Inventory, Resolve, stats, CombatEvents, health, null, () => Player.GetComponent<PlayerAiming>().AimDirection);
            consumables.SetInputReader(Reader);

            MountWeapons();
            Inventory.EquippedChanged += OnEquippedChanged;
            StatsBinder.Bind();
            InitializeRunHealth(health);
            return Player;
        }

        /// <summary>
        /// Run-start health: a new expedition begins at the TRUE effective maximum — after the starting loadout's armor/
        /// accessory/affix modifiers have been registered and the stat pipeline recomputed (PlayerStats.MaxHealth is the one
        /// authoritative figure the HUD reads too). Before this, the entity was created at the base maximum and the later
        /// resize kept the base current value (100 / 120). Only the rig build (a new run) fills; a mid-run equipment
        /// change still goes through ResizeMaxHealth, which never heals.
        /// </summary>
        private void InitializeRunHealth(HealthComponent health)
        {
            if (health == null || StatsBinder?.Stats == null) return;
            health.SetMaxHealth(StatsBinder.Stats.MaxHealth);
            RunStartHealth = health.CurrentHealth;
        }

        /// <summary>The effective maximum the run started at (diagnostics / proof).</summary>
        public int RunStartHealth { get; private set; }

        private ItemDefinition Resolve(string id) => _registry.TryGet(id, out var d) ? d : null;
        private AffixDefinition ResolveAffix(string id) => null;

        private void OnEquippedChanged(EquippedSlot slot, ItemInstance item)
        {
            if (slot != EquippedSlot.PrimaryWeapon && slot != EquippedSlot.SecondaryWeapon) return;
            MountWeapons();
            StatsBinder.Bind();
        }

        /// <summary>Weapon components for the two class-agnostic slots; only the loadout's active one consumes input.</summary>
        public void MountWeapons()
        {
            foreach (var c in _mounted) if (c != null) UnityEngine.Object.Destroy(c);
            _mounted.Clear();
            var primary = Mount(Inventory.GetEquipped(EquippedSlot.PrimaryWeapon));
            var secondary = Mount(Inventory.GetEquipped(EquippedSlot.SecondaryWeapon));
            Loadout.SetPrimary(primary);
            Loadout.SetSecondary(secondary);
            Loadout.Initialize();
            Remounts++;
        }

        private IEquippableWeapon Mount(ItemInstance item)
        {
            if (item == null) return null;
            var definition = Resolve(item.DefinitionId);
            var aiming = Player.GetComponent<PlayerAiming>();
            var roller = new UnityRandomDamageRoller();
            IEquippableWeapon weapon = null;
            switch (definition)
            {
                case BlasterWeaponDefinition blaster:
                    var b = Player.AddComponent<BlasterWeapon>();
                    b.SetInputReader(Reader); b.SetAiming(aiming); b.SetProjectilePool(Projectiles); b.SetDamageRoller(roller); b.SetDefinition(blaster); b.SetAimAssist(_content.AimAssist);
                    weapon = b; _mounted.Add(b);
                    break;
                case BowWeaponDefinition bow:
                    var w = Player.AddComponent<BowWeapon>();
                    w.SetInputReader(Reader); w.SetAiming(aiming); w.SetProjectilePool(Projectiles); w.SetDamageRoller(roller); w.SetDefinition(bow); w.SetAimAssist(_content.AimAssist);
                    weapon = w; _mounted.Add(w);
                    break;
                case MeleeWeaponDefinition melee:
                    var m = Player.AddComponent<MeleeWeapon>();
                    m.SetInputReader(Reader); m.SetAiming(aiming); m.SetDamageRoller(roller); m.SetDefinition(melee);
                    weapon = m; _mounted.Add(m);
                    break;
                case RangedWeaponDefinition ranged:
                    var r = Player.AddComponent<RangedWeapon>();
                    r.SetInputReader(Reader); r.SetAiming(aiming); r.SetProjectilePool(Projectiles); r.SetAmmoReserve(Inventory); r.SetDamageRoller(roller); r.SetDefinition(ranged); r.SetAimAssist(_content.AimAssist);
                    weapon = r; _mounted.Add(r);
                    break;
            }

            if (weapon != null && definition is EquipmentItemDefinition equipment && item.Rarity == Rarity.Legendary && !string.IsNullOrEmpty(equipment.LegendaryMechanicId))
            {
                var special = _specials?.CreateFor(equipment.LegendaryMechanicId);
                if (special != null)
                {
                    var body = Player.GetComponent<Rigidbody2D>();
                    Special.Configure(special, weapon, () => new SpecialContext(Player, body, () => aiming.AimDirection, () => (Vector2)Player.transform.position, Projectiles, roller));
                }
            }

            return weapon;
        }

        public void Dispose()
        {
            if (Inventory != null) Inventory.EquippedChanged -= OnEquippedChanged;
            _statRegistrar?.Dispose();
            _passives?.Dispose();
        }
    }
}

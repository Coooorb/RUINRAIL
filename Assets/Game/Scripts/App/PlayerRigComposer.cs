using System;
using System.Collections.Generic;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Area;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Combat.Weapons.Specials;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using RuinRail.Gameplay.Items.Passives;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Progression;
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
        private readonly AffixRegistry _affixes;
        private readonly List<Component> _mounted = new();
        private LoadoutStatRegistrar _statRegistrar;
        private EquipmentPassiveRegistrar _passives;
        private EquipmentPassiveTicker _ticker;
        private HealthComponent _damageSource;
        private PlayerCombatEventProducers _producers;
        private RuinRail.Dungeon.Runtime.PlayerRoomEventsRelay _roomRelay;

        /// <summary>
        /// Which Legendary passives this rig may run (null = all). A co-op client's own rig leaves the incoming-impact
        /// passives (Anchored, Shock Absorber, Exo Lock) to the host, which simulates the member's body; running them
        /// here too would trigger them a second time off replicated health.
        /// </summary>
        public System.Func<string, bool> PassiveAdmit { get; set; }

        /// <summary>The run's ground pickups (Room Sweep's pull source); null = nothing to sweep.</summary>
        public System.Func<IEnumerable<GameObject>> GroundPickups { get; set; }

        /// <summary>The world side of this rig's passives (Emergency Vent, Discharge, Arc Stagger, Room Sweep).</summary>
        public PlayerPassiveWorld PassiveWorld { get; private set; }

        /// <summary>The Legendary passives this rig runs (diagnostics / proof).</summary>
        public EquipmentPassiveRegistrar Passives => _passives;

        public PlayerRig(GameContentCatalog content, ItemDefinitionRegistry registry, LegendarySpecialRegistry specials)
        {
            _content = content;
            _registry = registry;
            _specials = specials;
            _affixes = AffixRegistry.FromDefinitions(registry?.Definitions);
        }

        public GameObject Player { get; private set; }
        public IPlayerInputReader Reader { get; private set; }
        public PlayerInventory Inventory { get; private set; }
        public WeaponLoadout Loadout { get; private set; }
        public ProjectilePool Projectiles { get; private set; }
        public PlayerStatsBinder StatsBinder { get; private set; }
        public PlayerCombatEvents CombatEvents { get; } = new();
        public LegendarySpecialController Special { get; private set; }
        /// <summary>The run's consumable channel (Active Consumable and quick-grenade); the HUD reads its effect runner.</summary>
        public PlayerConsumableUser Consumables { get; private set; }
        public int Remounts { get; private set; }

        /// <summary>
        /// Where a Revive consumable (Defibrillator) sends its request; set by the run before the rig composes. Null
        /// leaves revive consumables unsupported, which is what every run was until the hook existed.
        /// </summary>
        public Func<ReviveRequest, bool> ReviveRequester { get; set; }

        /// <summary>
        /// Builds the local run player. <paramref name="progression"/> is the profile's permanent attribute allocation
        /// (player/13); it is handed to the one player composition so the "skills" stat source exists BEFORE the
        /// loadout registrar adds equipment and before the run-start health fill reads the effective maximum.
        /// </summary>
        public GameObject Build(ExpeditionState state, PartyLifeRoster roster, string participantId, Vector2 position, IPlayerInputReader reader = null, SkillAllocation progression = null)
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
                ParticipantId = participantId,
                Progression = progression
            });
            Reader = reader ?? Player.GetComponent<PlayerInput>()?.Reader;
            return ComposeGameplay(state);
        }

        /// <summary>
        /// Co-op: composes this peer's run player onto the network player object it already owns (82) — the one the
        /// host spawned for it — instead of building a second, hidden character. The object already is the proven
        /// player entity (the release prefab is PlayerEntityBuilder's composition); this adds exactly what
        /// <see cref="Build"/> adds on top: identity, progression, weapons from the at-risk inventory, stats, passives,
        /// consumables, grenades and the Legendary special, bound to the owner's own input reader.
        /// </summary>
        public GameObject Attach(GameObject owned, ExpeditionState state, PartyLifeRoster roster, string participantId, SkillAllocation progression = null)
        {
            if (owned == null) throw new ArgumentNullException(nameof(owned));
            Inventory = state.Inventory;
            Player = owned;
            IsAttached = true;
            var life = Player.GetComponent<PlayerLifeStateComponent>();
            if (life != null)
            {
                if (!string.IsNullOrEmpty(participantId)) life.SetParticipantId(participantId);
                if (life.Roster != roster) life.SetRoster(roster);
            }

            Player.GetComponent<PlayerStatsBinder>()?.ApplyProgression(progression);
            var input = Player.GetComponent<PlayerInput>();
            if (input == null) input = Player.AddComponent<PlayerInput>();
            Reader = input.Reader;
            // Every input consumer of the owned object reads the run's one reader (the object was spawned before the
            // run composed, so movement/aim/dash/interact/revive may still hold the spawn-time reader).
            Player.GetComponent<PlayerMovement>()?.SetInputReader(Reader);
            Player.GetComponent<PlayerAiming>()?.SetInputReader(Reader);
            Player.GetComponent<PlayerDash>()?.SetInputReader(Reader);
            Player.GetComponent<PlayerInteractor>()?.SetInputReader(Reader);
            Player.GetComponent<PlayerLifeStateComponent>()?.SetInputReader(Reader);
            Player.GetComponent<PlayerReviver>()?.SetInputReader(Reader);
            Player.GetComponent<DeadSpectatorFollow>()?.SetInputReader(Reader);
            return ComposeGameplay(state);
        }

        /// <summary>True when the rig was composed onto an existing (network) player object rather than built.</summary>
        public bool IsAttached { get; private set; }

        private GameObject ComposeGameplay(ExpeditionState state)
        {
            Projectiles = Player.AddComponent<ProjectilePool>();
            StatsBinder = Player.GetComponent<PlayerStatsBinder>();
            Loadout = Player.AddComponent<WeaponLoadout>();
            Loadout.SetInputReader(Reader);
            Special = Player.AddComponent<LegendarySpecialController>();

            var receiver = Player.GetComponent<PlayerLootReceiver>();
            receiver.SetInventory(Inventory);
            receiver.SetWallet(state.CarriedWallet);
            receiver.SetCombatEvents(CombatEvents);

            var health = Player.GetComponent<HealthComponent>();
            var stats = StatsBinder.Stats;
            _statRegistrar = new LoadoutStatRegistrar(Inventory, stats, Resolve, ResolveAffix);
            // Ammo Stack Capacity reaches the at-risk backpack the same way every other equipment stat reaches its
            // consumer: through the run's authoritative PlayerStats. Without this the provider seam existed but nothing
            // called it, which is the defect class that left SkillStatSource inert.
            Inventory.SetAmmoCapacityBonusProvider(() => stats.GetPercent(StatId.AmmoStackCapacity));
            // The passives' world requests (pulse, shockwave, room pickup pull) were never supplied, so every context fell
            // back to PassiveWorldActions.None and Emergency Vent / Discharge / Arc Stagger / Room Sweep did nothing.
            PassiveWorld = new PlayerPassiveWorld(Player, GroundPickups);
            _passives = new EquipmentPassiveRegistrar(Inventory, Resolve, new PassiveContext(stats, CombatEvents, () => health.CurrentHealth, () => health.MaxHealth, amount => health.Heal(amount), () => Player.GetComponent<PlayerDash>()?.ResetCooldown(), PassiveWorld.Actions), PassiveAdmit);
            // The passives' clock (cooldowns, timed buffs) and the one damage signal Exo Lock reacts to: the registrar was
            // never ticked and nothing raised DamageTaken, so Anchored recharged never and Exo Lock never triggered.
            _ticker = EquipmentPassiveTicker.On(Player);
            _ticker.Add(_passives);
            _damageSource = health;
            _damageSource.Damaged += CombatEvents.RaiseDamageTaken;
            // The body-side producers (dash, movement, health, weapon swap) and the room lifecycle relay (combat room
            // entered / cleared) feed the same hub; the relay existed but was never composed onto the player.
            _producers = Player.GetComponent<PlayerCombatEventProducers>();
            if (_producers == null) _producers = Player.AddComponent<PlayerCombatEventProducers>();
            _roomRelay = Player.GetComponent<RuinRail.Dungeon.Runtime.PlayerRoomEventsRelay>();
            if (_roomRelay == null) _roomRelay = Player.AddComponent<RuinRail.Dungeon.Runtime.PlayerRoomEventsRelay>();
            _roomRelay.SetEvents(CombatEvents);
            // The grenade launcher was never composed onto the player, so ConsumableTargets.ThrowGrenade stayed null,
            // ConsumableEffectRunner.CanThrowGrenades stayed false, and every grenade consumable was refused with
            // UnsupportedEffect: four authored grenades that could be looted, priced and equipped but never thrown.
            // Same dead-seam shape as the affix registry and the coin scaler; the fix is the missing call, not new rules.
            var launcher = Player.AddComponent<GrenadeLauncher>();
            launcher.SetDamageRoller(new UnityRandomDamageRoller());
            Consumables = Player.AddComponent<PlayerConsumableUser>();
            Consumables.Configure(Inventory, Resolve, stats, CombatEvents, health, launcher, () => Player.GetComponent<PlayerAiming>().AimDirection, ReviveRequester);
            // The player's stagger/knockback boundary (42) was added to every player entity but never given its config
            // or the combat-event hub, so ApplyStagger/ApplyKnockback returned None for every enemy, elite, boss and
            // hazard impact: Resilience's resistances and the Anchored / Shock Absorber / Exo Lock hooks had nothing to
            // act on. The config is the same authored StaggerConfig the enemies use; no value is new.
            var impactReceiver = Player.GetComponent<PlayerImpactReceiver>();
            if (impactReceiver != null)
            {
                impactReceiver.SetConfig(_content.Stagger);
                impactReceiver.SetEvents(CombatEvents);
            }
            Consumables.SetInputReader(Reader);

            MountWeapons();
            Inventory.EquippedChanged += OnEquippedChanged;
            StatsBinder.Bind();
            _producers.Bind(CombatEvents, Reader, Loadout);
            InitializeRunHealth(health);
            return Player;
        }

        /// <summary>
        /// Co-op reconnect: the owned network object was replaced (the host handed this peer its held character again);
        /// the run's gameplay is composed onto the new object. Inventory, wallet and transaction are the expedition's,
        /// not the object's, so nothing about the player's state is lost or duplicated.
        /// </summary>
        public GameObject Reattach(GameObject owned, ExpeditionState state, PartyLifeRoster roster, string participantId, SkillAllocation progression = null)
        {
            if (Inventory != null) Inventory.EquippedChanged -= OnEquippedChanged;
            _statRegistrar?.Dispose();
            ReleasePassives();
            _mounted.Clear();
            return Attach(owned, state, roster, participantId, progression);
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
        /// <summary>
        /// The affix an equipped item's persisted roll names. This returned null until this pass, which meant
        /// <see cref="EquippedItemStatSource"/> built no affix source and every rolled affix — on every weapon, armor
        /// and accessory, at every rarity — contributed nothing to the run while still being rolled, priced and shown.
        /// An id no current pool contains still resolves to null, which is how a legacy save's removed affix stays
        /// loadable without silently taking effect.
        /// </summary>
        private AffixDefinition ResolveAffix(string id) => _affixes?.Get(id);

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
                    var visualId = ProjectileVisualCatalog.ResolveWeaponVisualId(definition as WeaponDefinition);
                    Special.Configure(special, weapon, () => new SpecialContext(Player, body, () => aiming.AimDirection, () => (Vector2)Player.transform.position, Projectiles, roller, projectileVisualId: visualId));
                }
            }

            return weapon;
        }

        private void ReleasePassives()
        {
            if (_producers != null) _producers.Unbind();
            if (Player != null)
            {
                var lootReceiver = Player.GetComponent<PlayerLootReceiver>();
                if (lootReceiver != null) lootReceiver.SetCombatEvents(null);
            }
            if (_roomRelay != null) _roomRelay.SetEvents(null);
            _producers = null;
            _roomRelay = null;
            if (_ticker != null) _ticker.Remove(_passives);
            if (_damageSource != null) _damageSource.Damaged -= CombatEvents.RaiseDamageTaken;
            _damageSource = null;
            _passives?.Dispose();
            _passives = null;
        }

        public void Dispose()
        {
            if (Inventory != null) Inventory.EquippedChanged -= OnEquippedChanged;
            _statRegistrar?.Dispose();
            ReleasePassives();
        }
    }
}

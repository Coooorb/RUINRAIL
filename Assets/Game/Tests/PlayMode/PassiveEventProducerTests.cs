using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Accessories;
using RuinRail.Gameplay.Items.Armor;
using RuinRail.Gameplay.Items.Passives;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Stats;
using RuinRail.Networking;
using RuinRail.UI.Base;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// PlayerCombatEvents producers for the Legendary passives, through the real boot and the real rig composition: every
    /// event a shipping passive subscribes to is raised by the shipped producer (dash, movement, health, swap, room
    /// lifecycle, weapon attacks, reload, firing, blaster heat, bow draw, projectile hits, kills, ammo pickups), exactly
    /// once, and the passive's authored effect follows. The host-resolved member passives run on the host's copy.
    /// </summary>
    public sealed class PassiveEventProducerTests
    {
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_producers_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            CursorService.SetApplier(_ => true);
            DamageAuthority.LocalIsAuthoritative = true;
        }

        [TearDown]
        public void TearDown()
        {
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            foreach (var screen in Object.FindObjectsByType<MainMenuScreen>(FindObjectsSortMode.None)) Object.DestroyImmediate(screen.gameObject);
            foreach (var screen in Object.FindObjectsByType<BaseHubScreen>(FindObjectsSortMode.None)) Object.DestroyImmediate(screen.gameObject);
            foreach (var scene in Object.FindObjectsByType<ExpeditionScene>(FindObjectsSortMode.None)) Object.DestroyImmediate(scene.gameObject);
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == null || IsTestRunner(root)) continue;
                Object.DestroyImmediate(root);
            }

            Time.timeScale = 1f;
            NetworkPlayerObject.VisualComposer = null;
            CursorService.Reset();
            DamageAuthority.LocalIsAuthoritative = true;
            try { Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private static bool IsTestRunner(GameObject root)
        {
            if (root.name.IndexOf("tests runner", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (var component in root.GetComponents<Component>())
                if (component != null && (component.GetType().Namespace ?? string.Empty).StartsWith("UnityEngine.TestTools")) return true;
            return false;
        }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 30f;
            while (_app.ComposedScene != scene)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed in time.");
                yield return null;
            }
        }

        private IEnumerator EnterRun()
        {
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(11);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Producer Runner");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 12; i++) yield return null;
        }

        private static void Wear(PlayerInventory inventory, string id, EquippedSlot slot)
        {
            inventory.Unequip(slot);
            Assert.IsTrue(inventory.TryEquip(new ItemInstance(id, 1, Rarity.Legendary), slot), "equip " + id);
        }

        private static T Passive<T>(ExpeditionScene run, EquippedSlot slot) where T : EquipmentPassive
        {
            var passive = run.Rig.Passives.GetActive(slot) as T;
            Assert.IsNotNull(passive, typeof(T).Name + " attached");
            return passive;
        }

        private EnemyController Dummy(ExpeditionScene run, Vector2 at, int health = 1000)
        {
            var enemy = new DefaultEnemySpawner(_app.Content.Stagger).Spawn(_app.Content.Enemies.First(e => e.Id == "grunt"), at, run.Rig.Player.transform);
            enemy.enabled = false;
            var body = enemy.GetComponent<Rigidbody2D>();
            body.linearVelocity = Vector2.zero;
            body.bodyType = RigidbodyType2D.Kinematic;
            var hp = enemy.GetComponent<HealthComponent>();
            if (hp.MaxHealth < health) hp.SetMaxHealth(health);
            hp.Heal(health);
            return enemy;
        }

        private static int Capped(PlayerStats stats, StatId stat, int value) => stats.CapFor(stat) > 0 ? Mathf.Min(stats.CapFor(stat), value) : value;

        /// <summary>Drives health below <paramref name="fraction"/> of max through real hits (armor DR shrinks each one).</summary>
        private static void DropTo(HealthComponent health, float fraction)
        {
            var target = Mathf.FloorToInt(health.MaxHealth * fraction);
            for (var i = 0; i < 200 && health.CurrentHealth > target; i++)
                health.TryApplyDamage(new DamageRequest(Mathf.Max(1, health.CurrentHealth - target)));
            Assert.LessOrEqual(health.CurrentHealth, target);
            Assert.IsTrue(health.IsAlive);
        }

        [UnityTest]
        public IEnumerator Solo_BodyAndRoomEvents_DashMovementHealthSwapRoom_ReachTheirPassivesOnce()
        {
            yield return EnterRun();
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var player = run.Rig.Player;
            var inventory = run.Rig.Inventory;
            var stats = run.Rig.StatsBinder.Stats;
            var health = player.GetComponent<HealthComponent>();
            var dash = player.GetComponent<PlayerDash>();
            var events = run.Rig.CombatEvents;
            var counts = new Dictionary<string, int>();
            void Count(string k) => counts[k] = (counts.TryGetValue(k, out var c) ? c : 0) + 1;
            events.Dashed += () => Count("dashed");
            events.DashEnded += () => Count("dashEnded");
            events.WeaponSwapped += () => Count("swapped");
            events.CombatRoomEntered += () => Count("entered");
            events.CombatRoomCleared += () => Count("cleared");
            events.HealthChanged += (_, _) => Count("health");

            // Dash → Runner's Watch (+10%) and Scout Rig Momentum (+12%); the endpoint raises DashEnded once.
            Wear(inventory, "accessory_runners_watch", EquippedSlot.Accessory);
            Wear(inventory, "armor_scout_rig", EquippedSlot.Armor);
            var move0 = stats.GetPercent(StatId.MovementSpeed);
            Assert.IsTrue(dash.TryStartDash(Vector2.right));
            Assert.AreEqual(1, counts.GetValueOrDefault("dashed"), "Dashed raised once by the real dash");
            Assert.AreEqual(Capped(stats, StatId.MovementSpeed, move0 + WatchMomentumPassive.MovePercent + MomentumPassive.MovePercent), stats.GetPercent(StatId.MovementSpeed), "both dash passives fired");
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(1, counts.GetValueOrDefault("dashEnded"), "DashEnded raised once at the endpoint");

            // Movement state → Field Scope: standing still for 1 s arms it; the first attack carries +12%.
            Wear(inventory, "accessory_field_scope", EquippedSlot.Accessory);
            var scope = Passive<SteadyAimPassive>(run, EquippedSlot.Accessory);
            yield return new WaitForSeconds(SteadyAimPassive.StillSeconds + 0.3f);
            Assert.IsTrue(scope.IsActive, "no movement for 1 s arms Steady Aim");

            // Weapon swap → Quickdraw Holster: one swap buffs, a second swap inside the 6 s cooldown does not re-trigger.
            Wear(inventory, "accessory_quickdraw_holster", EquippedSlot.Accessory);
            var swap = Passive<HotSwapPassive>(run, EquippedSlot.Accessory);
            run.Rig.Loadout.SelectSlot(WeaponSlot.Secondary);
            Assert.AreEqual(1, counts.GetValueOrDefault("swapped"));
            Assert.IsTrue(swap.IsBuffActive);
            run.Rig.Loadout.SelectSlot(WeaponSlot.Secondary);
            Assert.AreEqual(1, counts.GetValueOrDefault("swapped"), "re-selecting the active slot is not a swap");
            run.Rig.Loadout.SelectSlot(WeaponSlot.Primary);
            Assert.AreEqual(2, counts.GetValueOrDefault("swapped"));

            // Health → Last Stand (+15% DR below 25%, removed above it).
            health.Heal(health.MaxHealth);
            Wear(inventory, "armor_heavy_plate", EquippedSlot.Armor);
            var dr0 = stats.GetPercent(StatId.GeneralDamageReduction);
            var healthEvents = counts.GetValueOrDefault("health");
            DropTo(health, 0.2f);
            Assert.Greater(counts.GetValueOrDefault("health"), healthEvents, "HealthChanged raised by the damage");
            Assert.AreEqual(Capped(stats, StatId.GeneralDamageReduction, dr0 + LastStandPassive.DrPercent), stats.GetPercent(StatId.GeneralDamageReduction), "Last Stand active below 25%");
            health.Heal(health.MaxHealth);
            Assert.AreEqual(dr0, stats.GetPercent(StatId.GeneralDamageReduction), "Last Stand off again at full health");

            // Room lifecycle → Second Wind (in a combat room, first fall below 30% resets the dash) and Patchwork (clear heals 6%).
            Wear(inventory, "armor_runner_suit", EquippedSlot.Armor);
            var room = run.Rooms.Values.First(r => r.State.RoomType == RoomType.Combat && !r.State.IsElite && r.Lifecycle == RoomLifecycleState.Unentered && r.HasEncounter);
            var centre = (Vector2)room.InteriorWorldBounds.center;
            player.transform.position = centre;
            player.GetComponent<Rigidbody2D>().position = centre;
            for (var i = 0; i < 4; i++) yield return new WaitForFixedUpdate();
            yield return null;
            Assert.AreEqual(RoomLifecycleState.Active, room.Lifecycle);
            Assert.AreEqual(1, counts.GetValueOrDefault("entered"), "CombatRoomEntered raised once by the room");
            foreach (var e in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (e != null) { e.enabled = false; e.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic; }
            Assert.IsTrue(dash.TryStartDash(Vector2.up));
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(dash.CooldownRemaining, 0f);
            DropTo(health, 0.2f);
            Assert.AreEqual(0f, dash.CooldownRemaining, "Second Wind reset the dash cooldown");

            Wear(inventory, "armor_scrap_vest", EquippedSlot.Armor);
            var before = health.CurrentHealth;
            for (var i = 0; i < 300 && room.Lifecycle != RoomLifecycleState.Cleared; i++)
            {
                foreach (var e in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
                    if (e != null && e.IsAlive && room.InteriorWorldBounds.Contains(e.transform.position)) e.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(999999));
                yield return null;
            }

            Assert.AreEqual(RoomLifecycleState.Cleared, room.Lifecycle);
            Assert.AreEqual(1, counts.GetValueOrDefault("cleared"), "CombatRoomCleared raised once");
            Assert.AreEqual(Mathf.Min(health.MaxHealth, before + Mathf.RoundToInt(health.MaxHealth * PatchworkPassive.HealPercentOfMax / 100f)), health.CurrentHealth, "Patchwork healed 6% of max on the clear");
        }

        [UnityTest]
        public IEnumerator Solo_WeaponKillAndPickupEvents_ReachTheirPassivesOnce()
        {
            yield return EnterRun();
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var player = run.Rig.Player;
            var inventory = run.Rig.Inventory;
            var stats = run.Rig.StatsBinder.Stats;
            var events = run.Rig.CombatEvents;
            var receiver = player.GetComponent<PlayerImpactReceiver>();
            var bonuses = new List<int>();
            var pistol = (RangedWeapon)run.Rig.Loadout.GetSlot(WeaponSlot.Primary);

            // Reload → Loader's Glove: a completed reload arms 3 attacks at +15%.
            Wear(inventory, "accessory_loaders_glove", EquippedSlot.Accessory);
            var fresh = Passive<FreshMagPassive>(run, EquippedSlot.Accessory);
            events.AttackDamageRolling += r => bonuses.Add(r.BonusPercent); // after the passive: sees the settled bonus
            pistol = (RangedWeapon)run.Rig.Loadout.GetSlot(WeaponSlot.Primary);
            pistol.ApplyAuthoritativeState(0, false);
            Assert.IsTrue(pistol.TryStartReload());
            yield return new WaitForSeconds(pistol.CurrentReloadTime + 0.2f);
            Assert.AreEqual(FreshMagPassive.Attacks, fresh.RemainingAttacks, "ReloadCompleted raised once by the real reload");
            bonuses.Clear();
            Assert.IsTrue(pistol.TryFire());
            Assert.AreEqual(new[] { FreshMagPassive.DamagePercent }, bonuses.ToArray(), "one attack roll per shot, +15%");
            Assert.AreEqual(FreshMagPassive.Attacks - 1, fresh.RemainingAttacks);

            // Firing state → Stabilizer: 1 s of continuous fire applies the spread reduction; letting go removes it.
            Wear(inventory, "accessory_stabilizer", EquippedSlot.Accessory);
            var lockIn = Passive<LockInPassive>(run, EquippedSlot.Accessory);
            pistol = (RangedWeapon)run.Rig.Loadout.GetSlot(WeaponSlot.Primary);
            var trigger = new FakePlayerInputReader { FireHeld = true };
            pistol.SetInputReader(trigger);
            yield return new WaitForSeconds(LockInPassive.ContinuousSeconds + 0.3f);
            Assert.IsTrue(lockIn.IsApplied, "FiringStateChanged(true) held for 1 s");
            trigger.FireHeld = false;
            yield return null;
            yield return null;
            Assert.IsFalse(lockIn.IsApplied, "FiringStateChanged(false) on release");
            pistol.SetInputReader(run.Rig.Reader);

            // Projectile hit → Rangefinder: 7+ tiles of travel add 15%; a short hit does not.
            Wear(inventory, "accessory_rangefinder", EquippedSlot.Accessory);
            var far = Dummy(run, new Vector2(510f, 500f));
            var near = Dummy(run, new Vector2(512f, 520f));
            var farHp = far.GetComponent<HealthComponent>();
            var nearHp = near.GetComponent<HealthComponent>();
            var f0 = farHp.CurrentHealth;
            var n0 = nearHp.CurrentHealth;
            run.Rig.Projectiles.Spawn(new Vector2(500f, 500f), new ProjectileSpawnData(20, 30f, 20f, 0f, 0f, Vector2.right, player, receiver, 0f, DamageTeam.Player));
            run.Rig.Projectiles.Spawn(new Vector2(510f, 520f), new ProjectileSpawnData(20, 30f, 20f, 0f, 0f, Vector2.right, player, receiver, 0f, DamageTeam.Player));
            yield return new WaitForSeconds(0.8f);
            Assert.AreEqual(Mathf.RoundToInt(20 * 1.15f), f0 - farHp.CurrentHealth, "a 10-tile hit deals +15%");
            Assert.AreEqual(20, n0 - nearHp.CurrentHealth, "a 2-tile hit is unchanged");

            // Kill → Combat Harness (Adrenaline): the wearer's killing projectile reports the kill once.
            Wear(inventory, "armor_combat_harness", EquippedSlot.Armor);
            var adrenaline = Passive<AdrenalinePassive>(run, EquippedSlot.Armor);
            var kills = 0;
            events.EnemyKilled += () => kills++;
            var victim = Dummy(run, new Vector2(530f, 500f), 10);
            var vh = victim.GetComponent<HealthComponent>();
            vh.TryApplyDamage(new DamageRequest(vh.CurrentHealth - 1));
            run.Rig.Projectiles.Spawn(new Vector2(527f, 500f), new ProjectileSpawnData(5, 30f, 10f, 0f, 0f, Vector2.right, player, receiver, 0f, DamageTeam.Player));
            yield return new WaitForSeconds(0.5f);
            Assert.IsFalse(vh.IsAlive);
            Assert.AreEqual(1, kills, "EnemyKilled raised once for the killing hit");
            Assert.IsTrue(adrenaline.IsBuffActive, "Adrenaline triggered by the kill");

            // Bow full draw → Archer's Ring: the shot pierces its first target at full damage and resolves on the second.
            Wear(inventory, "accessory_archers_ring", EquippedSlot.Accessory);
            inventory.Unequip(EquippedSlot.PrimaryWeapon);
            Assert.IsTrue(inventory.TryEquip(new ItemInstance("weapon_recurve_bow"), EquippedSlot.PrimaryWeapon));
            yield return null;
            var bow = (BowWeapon)run.Rig.Loadout.GetSlot(WeaponSlot.Primary);
            Assert.IsTrue(bow.TryStartCharge());
            bow.AdvanceCharge(10f);
            Assert.IsTrue(bow.TryRelease());
            Assert.AreEqual(1, bow.LastSpawnedProjectile.Data.PierceCount, "BowShotFired(full draw) → one penetration");
            var a = Dummy(run, new Vector2(545f, 500f));
            var b = Dummy(run, new Vector2(547f, 500f));
            var ah = a.GetComponent<HealthComponent>();
            var bh = b.GetComponent<HealthComponent>();
            var a0 = ah.CurrentHealth;
            var b0 = bh.CurrentHealth;
            run.Rig.Projectiles.Spawn(new Vector2(542f, 500f), new ProjectileSpawnData(30, 30f, 20f, 0f, 0f, Vector2.right, player, null, 0f, DamageTeam.Player, false, null, 1));
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(30, a0 - ah.CurrentHealth, "first target: full damage, passed through");
            Assert.AreEqual(30, b0 - bh.CurrentHealth, "second target: full damage, resolves the shot");

            // Blaster heat → Cooling Module: cooling from 50+ to 0 halves the next 5 shots' heat.
            Wear(inventory, "accessory_cooling_module", EquippedSlot.Accessory);
            var cold = Passive<ColdStartPassive>(run, EquippedSlot.Accessory);
            inventory.Unequip(EquippedSlot.PrimaryWeapon);
            Assert.IsTrue(inventory.TryEquip(new ItemInstance("weapon_pulse_carbine_b1"), EquippedSlot.PrimaryWeapon));
            yield return null;
            var blaster = (BlasterWeapon)run.Rig.Loadout.GetSlot(WeaponSlot.Primary);
            var shots = 0;
            for (var t = Time.time + 20f; Time.time < t && blaster.Heat.Heat < ColdStartPassive.PeakHeatThreshold;) { if (blaster.TryFire()) shots++; yield return null; }
            Assert.GreaterOrEqual(blaster.Heat.Heat, ColdStartPassive.PeakHeatThreshold, $"shots={shots}");
            for (var t = Time.time + 30f; Time.time < t && blaster.Heat.Heat > 0f;) yield return null;
            yield return null;
            Assert.AreEqual(ColdStartPassive.Shots, cold.RemainingShots, "BlasterCooledToZero(peak ≥ 50) raised once");
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(blaster.TryFire());
            Assert.AreEqual(blaster.CurrentHeatPerShot * ColdStartPassive.HeatMultiplier, blaster.Heat.Heat, 0.001f, "the next shot builds half the heat");

            // Overheat → Heat Sink: the overheat is announced exactly once.
            var overheats = 0;
            events.BlasterOverheated += () => overheats++;
            for (var t = Time.time + 30f; Time.time < t && !blaster.Heat.IsOverheated;) { blaster.TryFire(); yield return null; }
            for (var t = Time.time + 0.3f; Time.time < t;) { blaster.TryFire(); yield return null; }
            Assert.AreEqual(1, overheats, "BlasterOverheated raised once per overheat");

            // Ammo pickup → Ammo Pouch (Scavenger's Reserve): the stack is taken with +25%.
            Wear(inventory, "accessory_ammo_pouch", EquippedSlot.Accessory);
            inventory.Consume(AmmoType.Light, inventory.Get(AmmoType.Light));
            var spawnerHost = new GameObject("TestLootSpawner");
            var pickup = run.CreateLootSpawnerFor(spawnerHost).CreateItemPickup((Vector2)player.transform.position);
            pickup.Hold(new ItemInstance("ammo_light", 40), ItemCategory.Ammo);
            Assert.IsTrue(pickup.Interact(player));
            Assert.AreEqual(50, inventory.Get(AmmoType.Light), "AmmoPickupRolling: 40 light ammo taken as 50");
            Object.DestroyImmediate(spawnerHost);
        }

        [UnityTest]
        public IEnumerator RemoteMember_HostCopy_RunsItsHostResolvedPassives_AndTheMemberRigDoesNot()
        {
            yield return EnterRun();
            var body = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options
            {
                Name = "Remote_7", IsLocal = false, BalanceConfig = _app.Content.PlayerBalance, Caps = _app.Content.StatCaps,
                Position = new Vector2(600f, 600f), LifeRoster = new PartyLifeRoster(), ParticipantId = "remote_7"
            });
            var loadout = new InventorySnapshot
            {
                Equipped = new[]
                {
                    new InventorySnapshot.Entry { Slot = (int)EquippedSlot.Armor, Item = new ItemInstance("armor_heavy_plate", 1, Rarity.Legendary).ToSnapshot() },
                    new InventorySnapshot.Entry { Slot = (int)EquippedSlot.Accessory, Item = new ItemInstance("accessory_ammo_pouch", 1, Rarity.Legendary).ToSnapshot() }
                },
                Backpack = new InventorySnapshot.Entry[0]
            };
            var mirror = new CoopMemberMirror(body, 7, _app.Content, _app.Registry, new CoopMemberProfile { ClientId = 7, Loadout = loadout });
            Assert.AreEqual("last_stand", mirror.Passives.GetActive(EquippedSlot.Armor)?.Id);
            Assert.AreEqual("scavengers_reserve", mirror.Passives.GetActive(EquippedSlot.Accessory)?.Id);

            // Last Stand: the host copy's own health drives it (HealthChanged on the host's hub for this member).
            var stats = body.GetComponent<PlayerStatsBinder>().Stats;
            var health = body.GetComponent<HealthComponent>();
            var dr0 = stats.GetPercent(StatId.GeneralDamageReduction);
            DropTo(health, 0.2f);
            Assert.AreEqual(Capped(stats, StatId.GeneralDamageReduction, dr0 + LastStandPassive.DrPercent), stats.GetPercent(StatId.GeneralDamageReduction));

            // Scavenger's Reserve: the member's pickup is resolved on its host copy and sized there (+25%).
            var before = mirror.Inventory.Get(AmmoType.Light);
            var spawnerHost = new GameObject("TestLootSpawner");
            var pickup = Object.FindFirstObjectByType<ExpeditionScene>().CreateLootSpawnerFor(spawnerHost).CreateItemPickup(new Vector2(600f, 600f));
            pickup.Hold(new ItemInstance("ammo_light", 40), ItemCategory.Ammo);
            Assert.IsTrue(pickup.Interact(body));
            Assert.AreEqual(before + 50, mirror.Inventory.Get(AmmoType.Light));

            // Host-resolved on the host copy, never on the member's own rig (no double application); kills are host → member only.
            foreach (var mechanic in EquipmentPassiveRegistrar.HostResolvedMechanics)
                Assert.IsFalse(EquipmentPassiveRegistrar.IsMemberRigMechanic(mechanic), mechanic);
            Assert.IsTrue(EquipmentPassiveRegistrar.IsMemberRigMechanic("hot_swap") && EquipmentPassiveRegistrar.IsMemberRigMechanic("adrenaline") && EquipmentPassiveRegistrar.IsMemberRigMechanic("second_wind"));
            Assert.IsFalse(CoopKinds.IsClientToHost(CoopKinds.Kill), "a member cannot send itself a kill");
            mirror.Dispose();
            Object.DestroyImmediate(spawnerHost);
            Object.DestroyImmediate(body);
        }

        // ---------------------------------------------------------------- PassiveWorldActions (world side of the passives)

        private static void Place(GameObject player, Vector2 at)
        {
            player.transform.position = at;
            player.GetComponent<Rigidbody2D>().position = at;
            player.GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
        }

        [UnityTest]
        public IEnumerator Solo_WorldActions_EmergencyVentDischargeArcStagger_ActOnTheEnemiesAroundTheWearer()
        {
            yield return EnterRun();
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var player = run.Rig.Player;
            var inventory = run.Rig.Inventory;
            var receiver = player.GetComponent<PlayerImpactReceiver>();
            var world = run.Rig.PassiveWorld;
            Assert.IsNotNull(world, "the rig composes its passives' world actions");
            var origin = (Vector2)run.Rooms.Values.First(r => r.State.RoomType == RoomType.Start).InteriorWorldBounds.center;
            Place(player, origin);
            yield return new WaitForFixedUpdate();

            // ---- Emergency Vent (Heat Sink): the real overheat emits one 2.5-tile pulse for 30–40; nothing beyond it is hit ----
            Wear(inventory, "accessory_heat_sink", EquippedSlot.Accessory);
            inventory.Unequip(EquippedSlot.PrimaryWeapon);
            Assert.IsTrue(inventory.TryEquip(new ItemInstance("weapon_pulse_carbine_b1"), EquippedSlot.PrimaryWeapon));
            yield return null;
            var blaster = (BlasterWeapon)run.Rig.Loadout.GetSlot(WeaponSlot.Primary);
            Assert.IsTrue(blaster.TryFire());
            var shot = blaster.LastShot.Direction;
            var side = new Vector2(-shot.y, shot.x); // off the line of fire: only the pulse can reach them
            var near = Dummy(run, origin + side * 1.5f);
            var far = Dummy(run, origin + side * 4f);
            var nearHp = near.GetComponent<HealthComponent>();
            var farHp = far.GetComponent<HealthComponent>();
            var n0 = nearHp.CurrentHealth;
            var f0 = farHp.CurrentHealth;
            for (var t = Time.time + 30f; Time.time < t && !blaster.Heat.IsOverheated;) { blaster.TryFire(); yield return null; }
            Assert.IsTrue(blaster.Heat.IsOverheated);
            Assert.AreEqual(1, world.AreaPulses, "one pulse per overheat");
            Assert.AreEqual(1, world.AreaTargetsHit);
            Assert.That(n0 - nearHp.CurrentHealth, Is.InRange(EmergencyVentPassive.DamageMin, EmergencyVentPassive.DamageMax), "the pulse dealt its authored 30–40");
            Assert.AreEqual(f0, farHp.CurrentHealth, "4 tiles away is outside the 2.5-tile pulse");
            Object.DestroyImmediate(near.gameObject);
            Object.DestroyImmediate(far.gameObject);

            // ---- Discharge (Dash Capacitor): the real dash endpoint emits one knockback + stagger shockwave, no damage ----
            var dash = player.GetComponent<PlayerDash>();
            Place(player, origin);
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(dash.TryStartDash(Vector2.right));
            yield return new WaitForSeconds(0.5f);
            var endpoint = (Vector2)player.transform.position;
            for (var t = Time.time + 5f; Time.time < t && dash.CooldownRemaining > 0f;) yield return null;
            Wear(inventory, "accessory_dash_capacitor", EquippedSlot.Accessory);
            Place(player, origin);
            var bystander = Dummy(run, endpoint + Vector2.up * 1.1f);
            var bystanderImpact = bystander.GetComponent<ImpactReceiver>();
            var bystanderHp = bystander.GetComponent<HealthComponent>();
            var b0 = bystanderHp.CurrentHealth;
            var knock0 = bystanderImpact.KnockbacksApplied;
            yield return new WaitForFixedUpdate();
            var shock0 = world.Shockwaves;
            Assert.IsTrue(dash.TryStartDash(Vector2.right));
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(shock0 + 1, world.Shockwaves, "one shockwave at the dash endpoint");
            Assert.AreEqual(knock0 + 1, bystanderImpact.KnockbacksApplied, "the endpoint shockwave knocked the enemy back");
            Assert.Greater(bystanderImpact.Meter.Pressure + bystanderImpact.Meter.TriggerCount, 0f, "and put stagger pressure on it");
            Assert.AreEqual(b0, bystanderHp.CurrentHealth, "no damage");
            Object.DestroyImmediate(bystander.gameObject);

            // ---- Arc Stagger (Shock Charm): the wearer's own stagger emits one stagger shockwave around the wearer ----
            Wear(inventory, "accessory_shock_charm", EquippedSlot.Accessory);
            Place(player, origin);
            var struck = Dummy(run, origin + Vector2.right * 1.2f);
            var nearby = Dummy(run, origin + Vector2.left * 2f);
            var outside = Dummy(run, origin + Vector2.down * 4f);
            yield return new WaitForFixedUpdate();
            var struckImpact = struck.GetComponent<ImpactReceiver>();
            var nearbyImpact = nearby.GetComponent<ImpactReceiver>();
            var outsideImpact = outside.GetComponent<ImpactReceiver>();
            shock0 = world.Shockwaves;
            ImpactDispatcher.Apply(struckImpact, new ImpactRequest(Vector2.right, 0f, 1000f, DamageKind.Normal, player, receiver));
            Assert.IsTrue(struckImpact.IsStaggered);
            Assert.AreEqual(shock0 + 1, world.Shockwaves, "EnemyStaggeredByWearer → one shockwave");
            Assert.Greater(nearbyImpact.Meter.Pressure + nearbyImpact.Meter.TriggerCount, 0f, "an enemy 2 tiles away took the arc's stagger");
            Assert.AreEqual(0f, outsideImpact.Meter.Pressure + outsideImpact.Meter.TriggerCount, "4 tiles away is outside the 2.5-tile arc");
            ImpactDispatcher.Apply(nearbyImpact, new ImpactRequest(Vector2.left, 0f, 1000f, DamageKind.Normal, player, receiver));
            Assert.AreEqual(shock0 + 1, world.Shockwaves, "the 6 s cooldown blocks a second arc");
        }

        [UnityTest]
        public IEnumerator Solo_WorldActions_RoomSweep_PullsTheClearedRoomsCoinsAndAmmoOnly()
        {
            yield return EnterRun();
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var player = run.Rig.Player;
            var world = run.Rig.PassiveWorld;
            Wear(run.Rig.Inventory, "accessory_magnetic_coil", EquippedSlot.Accessory);
            var room = run.Rooms.Values.First(r => r.State.RoomType == RoomType.Combat && !r.State.IsElite && r.Lifecycle == RoomLifecycleState.Unentered && r.HasEncounter);
            var bounds = room.InteriorWorldBounds;
            Place(player, bounds.center);
            for (var i = 0; i < 4; i++) yield return new WaitForFixedUpdate();
            yield return null;
            Assert.AreEqual(RoomLifecycleState.Active, room.Lifecycle);
            foreach (var e in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (e != null) { e.enabled = false; e.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic; }

            var spawnerHost = new GameObject("TestSweepSpawner");
            var spawner = run.CreateLootSpawnerFor(spawnerHost);
            var coins = spawner.CreateCoinPickup(new Vector2(bounds.xMin + 0.7f, bounds.yMin + 0.7f));
            coins.SetAmount(7);
            var ammo = spawner.CreateItemPickup(new Vector2(bounds.xMax - 0.7f, bounds.yMax - 0.7f));
            ammo.Hold(new ItemInstance("ammo_light", 6), ItemCategory.Ammo);
            var elsewhere = spawner.CreateCoinPickup((Vector2)run.Rooms.Values.First(r => r.State.RoomType == RoomType.Start).InteriorWorldBounds.center);
            elsewhere.SetAmount(5);
            var elsewhereAt = elsewhere.transform.position;
            var reach = player.GetComponent<PickupAttractor>().Radius;
            Assert.Greater(Vector2.Distance(coins.transform.position, player.transform.position), reach + 0.5f, "precondition: out of normal attraction reach");
            Assert.Greater(Vector2.Distance(ammo.transform.position, player.transform.position), reach + 0.5f);
            yield return new WaitForSeconds(0.5f);
            Assert.IsFalse(coins.IsCollected, "nothing is pulled before the clear");

            for (var t = Time.time + 10f; Time.time < t && room.Lifecycle != RoomLifecycleState.Cleared;)
            {
                foreach (var e in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
                    if (e != null && e.IsAlive && bounds.Contains(e.transform.position)) e.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(999999));
                yield return null;
            }

            Assert.AreEqual(RoomLifecycleState.Cleared, room.Lifecycle);
            Assert.AreEqual(1, world.Sweeps, "one sweep per clear");
            Assert.AreEqual(2, world.SweptPickups, "the room's coin pile and ammo stack, nothing else");
            for (var t = Time.time + 5f; Time.time < t && !(coins.IsCollected && ammo.IsConsumed);) yield return null;
            Assert.IsTrue(coins.IsCollected, "coins pulled to the wearer and collected");
            Assert.IsTrue(ammo.IsConsumed, "ammo pulled to the wearer and collected");
            Assert.IsFalse(elsewhere.IsCollected, "a pile in another room stays");
            Assert.AreEqual(elsewhereAt, elsewhere.transform.position);
            Object.DestroyImmediate(spawnerHost);
        }

        [UnityTest]
        public IEnumerator RemoteMember_HostCopy_ArcStaggerActsOnTheHostWorld_AndRoomEventsReachItsHub()
        {
            yield return EnterRun();
            var body = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options
            {
                Name = "Remote_8", IsLocal = false, BalanceConfig = _app.Content.PlayerBalance, Caps = _app.Content.StatCaps,
                Position = new Vector2(600f, 600f), LifeRoster = new PartyLifeRoster(), ParticipantId = "remote_8"
            });
            var loadout = new InventorySnapshot
            {
                Equipped = new[] { new InventorySnapshot.Entry { Slot = (int)EquippedSlot.Accessory, Item = new ItemInstance("accessory_shock_charm", 1, Rarity.Legendary).ToSnapshot() } },
                Backpack = new InventorySnapshot.Entry[0]
            };
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var mirror = new CoopMemberMirror(body, 8, _app.Content, _app.Registry, new CoopMemberProfile { ClientId = 8, Loadout = loadout }, () => run.GroundLoot.Tracked);
            Assert.AreEqual("arc_stagger", mirror.Passives.GetActive(EquippedSlot.Accessory)?.Id);
            Assert.IsNotNull(mirror.World);
            var relay = body.GetComponent<PlayerRoomEventsRelay>();
            Assert.IsNotNull(relay, "the host copy hears the room lifecycle");
            Assert.AreSame(mirror.Events, relay.Events);
            var struck = Dummy(run, new Vector2(601.2f, 600f));
            var nearby = Dummy(run, new Vector2(598f, 600f));
            yield return new WaitForFixedUpdate();
            // The member's hit is resolved on the host with the member's copy as the attacker (CoopHostWorld.HandleImpact).
            ImpactDispatcher.Apply(struck.GetComponent<ImpactReceiver>(), new ImpactRequest(Vector2.right, 0f, 1000f, DamageKind.Normal, body, body.GetComponent<PlayerImpactReceiver>()));
            Assert.AreEqual(1, mirror.World.Shockwaves, "Arc Stagger's shockwave executed on the host copy");
            Assert.Greater(nearby.GetComponent<ImpactReceiver>().Meter.Pressure + nearby.GetComponent<ImpactReceiver>().Meter.TriggerCount, 0f);
            Assert.IsFalse(EquipmentPassiveRegistrar.IsMemberRigMechanic("arc_stagger") || EquipmentPassiveRegistrar.IsMemberRigMechanic("room_sweep"), "host-resolved: never on the member's own rig");
            Assert.IsTrue(EquipmentPassiveRegistrar.IsMemberRigMechanic("emergency_vent") && EquipmentPassiveRegistrar.IsMemberRigMechanic("discharge"), "owner-side: the member's rig, relayed to the host as validated hits/impacts");
            mirror.Dispose();
            Assert.IsNull(relay.Events, "dispose releases the relay (no stale hub after a reconnect/rebuild)");
            Object.DestroyImmediate(body);
        }
    }
}

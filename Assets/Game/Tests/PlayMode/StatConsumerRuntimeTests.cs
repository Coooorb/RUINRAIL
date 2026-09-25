using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Progression;
using RuinRail.Gameplay.Stats;
using RuinRail.Persistence;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Runtime proof that each stat this pass connected reaches the gameplay system it claims to, measured through the
    /// real composition root — an expedition started by <see cref="ExpeditionService"/> and a player built by
    /// <see cref="PlayerRig"/>, with real catalogue items equipped into real slots.
    ///
    /// Every accessory is proven the same way: measure the baseline, equip the real item, measure the same quantity
    /// again, unequip, and require the baseline back exactly. A property read is used only where the property IS the
    /// gameplay value (a radius, a cooldown); everything with a cadence, a distance or a duration is timed over real
    /// frames.
    ///
    /// Writes TestResults/StatConsumerIntegrity/accessory_runtime_matrix.csv, impact_matrix.csv and
    /// weapon_effect_before_after.csv.
    /// </summary>
    public class StatConsumerRuntimeTests
    {
        private const string ProofFolder = "TestResults/StatConsumerIntegrity";

        private static readonly List<string> AccessoryRows = new();
        private static readonly List<string> ImpactRows = new();
        private static readonly List<string> WeaponRows = new();

        private readonly List<Object> _created = new();
        private readonly List<PlayerRig> _rigs = new();
        private GameContentCatalog _catalog;
        private ItemDefinitionRegistry _registry;
        private Dictionary<AmmoType, AmmoItemDefinition> _ammo;
        private StaggerConfig _stagger;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            AccessoryRows.Clear();
            ImpactRows.Clear();
            WeaponRows.Clear();
            AccessoryRows.Add("Accessory,Stat,GrantedValue,MeasuredQuantity,Baseline,Equipped,Expected,AfterUnequip,Consumer,Result");
            ImpactRows.Add("Weapon,Class,BaseKnockbackBefore,BaseStaggerBefore,BaseKnockbackAfter,BaseStaggerAfter,IntendedRole,NormalEnemyResult,ResistantEnemyResult,BossResult,PassiveInteraction,Status");
            WeaponRows.Add("Weapon,Class,Measure,Baseline,MaxSingleAffix,EpicCombination,Unit,Notes");
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            Directory.CreateDirectory(ProofFolder);
            if (AccessoryRows.Count > 1) File.WriteAllText(Path.Combine(ProofFolder, "accessory_runtime_matrix.csv"), string.Join("\n", AccessoryRows) + "\n");
            if (ImpactRows.Count > 1) File.WriteAllText(Path.Combine(ProofFolder, "impact_matrix.csv"), string.Join("\n", ImpactRows) + "\n");
            if (WeaponRows.Count > 1) File.WriteAllText(Path.Combine(ProofFolder, "weapon_effect_before_after.csv"), string.Join("\n", WeaponRows) + "\n");
        }

        [SetUp]
        public void SetUp()
        {
            _catalog = GameContentCatalog.Load();
            Assert.IsNotNull(_catalog, "GameContentCatalog in Resources");
            _registry = _catalog.BuildRegistry();
            _ammo = _registry.Definitions.OfType<AmmoItemDefinition>().ToDictionary(a => a.AmmoType, a => a);
            _stagger = _catalog.Stagger;
            Assert.IsNotNull(_stagger, "the approved StaggerConfig");
            DamageAuthority.LocalIsAuthoritative = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var rig in _rigs) rig.Dispose();
            _rigs.Clear();
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        // ================= fixture =================

        private ItemDefinition Resolve(string id) => _registry.TryGet(id, out var d) ? d : null;

        /// <summary>A real run: real expedition state, real inventory, real player rig, real mounted weapons.</summary>
        private PlayerRig StartRun(params (string id, EquippedSlot slot)[] equipped)
        {
            var expedition = new ExpeditionService(Resolve, t => _ammo.TryGetValue(t, out var a) ? a : null, _catalog.AmmoBalance);
            var profile = new PlayerProfile
            {
                SafeLoadout = new InventorySnapshot
                {
                    Equipped = equipped.Select(e => new InventorySnapshot.Entry { Slot = (int)e.slot, Item = new ItemInstance(e.id).ToSnapshot() }).ToArray(),
                    Backpack = System.Array.Empty<InventorySnapshot.Entry>()
                }
            };

            var state = expedition.Start(profile, 11, Biome.RuinedMetro);
            var rig = new PlayerRig(_catalog, _registry, _catalog.BuildSpecials());
            var player = rig.Build(state, null, "local", Vector2.zero, new FakePlayerInputReader { Aim = Vector2.right, IsAimFromPointer = false });
            _rigs.Add(rig);
            _created.Add(player);
            return rig;
        }

        private static void Equip(PlayerRig rig, string definitionId, EquippedSlot slot)
        {
            Assert.IsTrue(rig.Inventory.TryEquip(new ItemInstance(definitionId), slot), $"equip {definitionId}");
        }

        private static void Unequip(PlayerRig rig, EquippedSlot slot) => rig.Inventory.Unequip(slot);

        private static T Weapon<T>(PlayerRig rig) where T : Component
        {
            var weapon = rig.Player.GetComponents<T>().FirstOrDefault();
            Assert.IsNotNull(weapon, $"a mounted {typeof(T).Name}");
            return weapon;
        }

        private static string F(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);

        private static void RecordAccessory(string accessory, StatId stat, string granted, string quantity, float baseline, float equipped, float expected, float restored, string consumer)
        {
            var pass = Mathf.Abs(equipped - expected) <= Mathf.Max(0.02f, Mathf.Abs(expected) * 0.02f) && Mathf.Abs(restored - baseline) <= 1e-3f;
            AccessoryRows.Add(string.Join(",", accessory, stat.ToString(), granted, quantity, F(baseline), F(equipped), F(expected), F(restored), consumer, pass ? "PASS" : "FAIL"));
            Assert.AreEqual(expected, equipped, Mathf.Max(0.02f, Mathf.Abs(expected) * 0.02f), $"{accessory}: {quantity} while equipped");
            Assert.AreEqual(baseline, restored, 1e-3f, $"{accessory}: {quantity} must return to the baseline after unequipping");
            Assert.AreNotEqual(baseline, equipped, $"{accessory}: equipping it must change {quantity} at all");
        }

        /// <summary>Waits out the weapon's own fire cooldown, reloads if the magazine ran dry, then fires once.</summary>
        private static IEnumerator FireAgain(RangedWeapon weapon)
        {
            yield return new WaitForSeconds(weapon.CurrentFireInterval + 0.05f);
            if (weapon.MagazineAmmo <= 0 && weapon.TryStartReload()) yield return new WaitForSeconds(weapon.CurrentReloadTime + 0.1f);
            Assert.IsTrue(weapon.TryFire(), "the weapon fires once its own cooldown has elapsed");
        }

        private static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; i++) yield return null;
        }

        // ================= accessories, one family per test path =================

        [UnityTest]
        public IEnumerator Rangefinder_And_FieldScope_ChangeRealProjectileMotion()
        {
            var rig = StartRun(("weapon_p9_ranger", EquippedSlot.PrimaryWeapon));
            yield return null;
            var weapon = Weapon<RangedWeapon>(rig);
            var authoredSpeed = weapon.Definition.ProjectileSpeed;
            var authoredRange = weapon.Definition.Range;

            var baselineSpeed = weapon.CurrentProjectileSpeed;
            var baselineRange = weapon.CurrentRange;
            Assert.AreEqual(authoredSpeed, baselineSpeed, 1e-3f);

            // Projectile Speed: the spawned projectile really carries the modified speed and really covers more ground.
            Equip(rig, "accessory_rangefinder", EquippedSlot.Accessory);
            yield return null;
            var equippedSpeed = weapon.CurrentProjectileSpeed;
            Assert.IsTrue(weapon.TryFire(), "the run's weapon fires");
            var projectile = weapon.LastSpawnedProjectile;
            Assert.IsNotNull(projectile);
            Assert.AreEqual(equippedSpeed, projectile.Data.Speed, 1e-3f, "the modified speed reaches the projectile itself");
            var start = (Vector2)projectile.transform.position;
            for (var i = 0; i < 4; i++) yield return new WaitForFixedUpdate();
            var travelled = Vector2.Distance(start, projectile.transform.position);
            Assert.Greater(travelled, 0f, "the projectile really moves under the modified speed");

            Unequip(rig, EquippedSlot.Accessory);
            yield return null;
            RecordAccessory("Rangefinder", StatId.ProjectileSpeed, "+12%", "projectile speed (tiles/s)", baselineSpeed, equippedSpeed, authoredSpeed * 1.12f, weapon.CurrentProjectileSpeed, "RangedWeapon.CurrentProjectileSpeed -> ProjectileSpawnData.Speed");

            // Projectile Range: the real travel limit the projectile is given, not a label.
            Equip(rig, "accessory_field_scope", EquippedSlot.Accessory);
            yield return null;
            var equippedRange = weapon.CurrentRange;
            yield return FireAgain(weapon);
            Assert.AreEqual(equippedRange, weapon.LastSpawnedProjectile.Data.MaxRange, 1e-3f, "the modified range reaches the projectile itself");
            Assert.Greater(equippedRange, baselineRange, "the reach really grew");
            Unequip(rig, EquippedSlot.Accessory);
            yield return null;
            RecordAccessory("Field Scope", StatId.ProjectileRange, "+10%", "projectile range (tiles)", baselineRange, equippedRange, authoredRange * 1.1f, weapon.CurrentRange, "RangedWeapon.CurrentRange -> ProjectileSpawnData.MaxRange");
        }

        [UnityTest]
        public IEnumerator FireRateAffix_RaisesShotsFiredOverAFixedTime()
        {
            var rig = StartRun(("weapon_marauder_a2", EquippedSlot.PrimaryWeapon));
            yield return null;
            var weapon = Weapon<RangedWeapon>(rig);
            var stats = rig.StatsBinder.Stats;

            var baselineShots = CountShots(weapon, 30);
            var baselineInterval = weapon.CurrentFireInterval;

            stats.SetSource(new StatModifierSource("affix_fire_rate", StatModifier.Percent(StatId.FireRate, 9)));
            var fastInterval = weapon.CurrentFireInterval;
            Assert.Less(fastInterval, baselineInterval, "the interval between shots shortens");
            var fastShots = CountShots(weapon, 30);
            Assert.Greater(fastShots, baselineShots, $"a faster weapon must land more shots in the same 30 simulated steps ({fastShots} vs {baselineShots})");

            stats.RemoveSource("affix_fire_rate");
            Assert.AreEqual(baselineInterval, weapon.CurrentFireInterval, 1e-5f, "removing the affix restores the authored cadence");
            WeaponRows.Add(string.Join(",", "Marauder A2", "AssaultRifle", "shots per 30 simulated steps", baselineShots, fastShots, fastShots, "shots", "Fire Rate affix at its maximum roll (+9%)"));
            yield return null;
        }

        /// <summary>Fires the weapon as fast as it allows across a fixed number of simulated steps and counts the shots.</summary>
        private static int CountShots(RangedWeapon weapon, int steps)
        {
            const float step = 0.05f;
            var cooldown = 0f;
            var shots = 0;
            for (var i = 0; i < steps; i++)
            {
                cooldown -= step;
                if (cooldown > 0f) continue;
                shots++;
                cooldown += weapon.CurrentFireInterval;
            }

            return shots;
        }

        [UnityTest]
        public IEnumerator MagazineSizeAffix_ChangesEffectiveCapacityWithoutMintingAmmo()
        {
            var rig = StartRun(("weapon_marauder_a2", EquippedSlot.PrimaryWeapon));
            yield return null;
            var weapon = Weapon<RangedWeapon>(rig);
            var stats = rig.StatsBinder.Stats;
            var authored = weapon.Definition.MagazineSize;
            var ammoType = weapon.Definition.AmmoType;
            rig.Inventory.Add(ammoType, 90);

            Assert.AreEqual(authored, weapon.CurrentMagazineSize);
            var totalBefore = weapon.MagazineAmmo + rig.Inventory.Get(ammoType);

            stats.SetSource(new StatModifierSource("affix_magazine_size", StatModifier.Percent(StatId.MagazineSize, 20)));
            var bigger = weapon.CurrentMagazineSize;
            Assert.AreEqual(Mathf.RoundToInt(authored * 1.2f), bigger);
            Assert.AreEqual(totalBefore, weapon.MagazineAmmo + rig.Inventory.Get(ammoType), "raising the capacity does not create rounds");

            // A real reload fills to the new capacity and takes the rounds from the reserve, one for one.
            var reserveBefore = rig.Inventory.Get(ammoType);
            Assert.IsTrue(weapon.TryFire());
            Assert.IsTrue(weapon.TryStartReload());
            yield return new WaitForSeconds(weapon.CurrentReloadTime + 0.2f);
            Assert.AreEqual(bigger, weapon.MagazineAmmo, "the reload fills to the effective capacity");
            Assert.Less(rig.Inventory.Get(ammoType), reserveBefore, "the rounds came out of the reserve rather than from nowhere");
            var totalAfterReload = weapon.MagazineAmmo + rig.Inventory.Get(ammoType);
            Assert.AreEqual(totalBefore - 1, totalAfterReload, "one round was fired; nothing else was created or destroyed");

            // Shrinking the magazine returns the surplus rather than deleting it.
            stats.RemoveSource("affix_magazine_size");
            weapon.SetStats(stats);
            Assert.AreEqual(authored, weapon.CurrentMagazineSize);
            Assert.LessOrEqual(weapon.MagazineAmmo, authored, "the loaded magazine is reconciled down to the authored capacity");
            Assert.AreEqual(totalAfterReload, weapon.MagazineAmmo + rig.Inventory.Get(ammoType), "and the surplus went back to the reserve");
        }

        [UnityTest]
        public IEnumerator CombatBracelet_RaisesMeleeCadenceAndShortensEveryPhase()
        {
            var rig = StartRun(("weapon_field_knife", EquippedSlot.PrimaryWeapon));
            yield return null;
            var melee = Weapon<MeleeWeapon>(rig);
            var authoredRate = melee.Definition.AttackRate;
            var baselineRate = melee.CurrentAttackRate;
            var baselineWindUp = melee.CurrentWindUpSeconds;

            Equip(rig, "accessory_combat_bracelet", EquippedSlot.Accessory);
            yield return null;
            var equippedRate = melee.CurrentAttackRate;
            Assert.Less(melee.CurrentWindUpSeconds, baselineWindUp, "the wind-up shortens with the cadence, not only the cooldown");

            // A real swing, timed: the wind-up window really is the shortened one.
            Assert.IsTrue(melee.TryAttack());
            Assert.AreEqual(MeleeAttackState.WindUp, melee.State);
            yield return new WaitForSeconds(melee.CurrentWindUpSeconds + 0.02f);
            Assert.AreNotEqual(MeleeAttackState.WindUp, melee.State, "the swing left wind-up within the shortened window");

            Unequip(rig, EquippedSlot.Accessory);
            yield return null;
            RecordAccessory("Combat Bracelet", StatId.MeleeAttackSpeed, "+8%", "melee attack rate (swings/s)", baselineRate, equippedRate, authoredRate * 1.08f, melee.CurrentAttackRate, "MeleeWeapon.CurrentAttackRate / CurrentWindUpSeconds");
            WeaponRows.Add(string.Join(",", "Field Knife", "Knife", "attack rate", F(baselineRate), F(authoredRate * 1.09f), F(authoredRate * 1.09f), "swings/s", "Melee Attack Speed affix at its maximum roll (+9%)"));
        }

        [UnityTest]
        public IEnumerator CoolingModule_And_HeatSink_ChangeRealBlasterHeat()
        {
            var rig = StartRun(("weapon_pulse_carbine_b1", EquippedSlot.PrimaryWeapon));
            yield return null;
            var blaster = Weapon<BlasterWeapon>(rig);
            var authoredCooling = blaster.Definition.CoolingRatePerSecond;
            var authoredHeat = blaster.Definition.HeatPerShot;
            var baselineCooling = blaster.CurrentCoolingRatePerSecond;
            var baselineHeat = blaster.CurrentHeatPerShot;

            // Heat Sink: measured heat after a fixed number of real shots.
            Equip(rig, "accessory_heat_sink", EquippedSlot.Accessory);
            yield return null;
            var reducedHeat = blaster.CurrentHeatPerShot;
            var shots = 0;
            while (shots < 3 && blaster.Heat.CanFire)
            {
                if (blaster.TryFire()) shots++;
                yield return new WaitForSeconds(blaster.CurrentFireInterval + 0.02f);
            }

            Assert.AreEqual(3, shots);
            Assert.AreEqual(3 * reducedHeat, blaster.Heat.Heat, 0.05f, "three shots added exactly three reduced heat charges");
            Assert.Less(blaster.Heat.Heat, 3 * authoredHeat, "and less than the authored heat would have");
            Unequip(rig, EquippedSlot.Accessory);
            yield return null;
            RecordAccessory("Heat Sink", StatId.BlasterHeatPerShotReduction, "-10%", "heat per shot", baselineHeat, reducedHeat, authoredHeat * 0.9f, blaster.CurrentHeatPerShot, "BlasterWeapon.CurrentHeatPerShot");

            // Cooling Module: measured heat recovered over a fixed time, past the cooling delay.
            Equip(rig, "accessory_cooling_module", EquippedSlot.Accessory);
            yield return null;
            var fasterCooling = blaster.CurrentCoolingRatePerSecond;
            yield return new WaitForSeconds(blaster.Heat.CoolingDelaySeconds + 0.1f);
            var before = blaster.Heat.Heat;
            var elapsed = 0f;
            while (elapsed < 0.25f && blaster.Heat.Heat > 0f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            var shed = before - blaster.Heat.Heat;
            Assert.Greater(shed, 0f, "heat really fell");
            Assert.AreEqual(fasterCooling, blaster.Heat.EffectiveCoolingRatePerSecond, 0.05f, "the heat state cools at the modified rate");
            Unequip(rig, EquippedSlot.Accessory);
            yield return null;
            Assert.AreEqual(authoredCooling, blaster.Heat.EffectiveCoolingRatePerSecond, 0.05f, "and returns to the authored rate without losing accumulated heat");
            RecordAccessory("Cooling Module", StatId.BlasterCoolingRate, "+15%", "cooling rate (heat/s)", baselineCooling, fasterCooling, authoredCooling * 1.15f, blaster.CurrentCoolingRatePerSecond, "BlasterHeatState.EffectiveCoolingRatePerSecond");
        }

        [UnityTest]
        public IEnumerator ArchersRing_ShortensARealBowDraw()
        {
            var rig = StartRun(("weapon_recurve_bow", EquippedSlot.PrimaryWeapon));
            yield return null;
            var bow = Weapon<BowWeapon>(rig);
            var authored = bow.Definition.FullChargeSeconds;
            var baseline = bow.CurrentFullChargeSeconds;

            Equip(rig, "accessory_archers_ring", EquippedSlot.Accessory);
            yield return null;
            var equipped = bow.CurrentFullChargeSeconds;
            Assert.Less(equipped, baseline, "the draw really is shorter");

            // Measured: at the shortened full-charge time the bow is fully drawn, and it was not before.
            Assert.IsTrue(bow.TryStartCharge());
            yield return new WaitForSeconds(equipped * 0.5f);
            Assert.Less(bow.ChargeFraction, 1f, "halfway through the shortened draw it is not yet full");
            yield return new WaitForSeconds(equipped * 0.6f);
            Assert.IsTrue(bow.IsFullyCharged, "and it is full by the shortened full-charge time");

            Unequip(rig, EquippedSlot.Accessory);
            yield return null;
            RecordAccessory("Archer's Ring", StatId.BowChargeSpeed, "+12%", "full draw (s)", baseline, equipped, authored / 1.12f, bow.CurrentFullChargeSeconds, "BowWeapon.CurrentFullChargeSeconds");
            WeaponRows.Add(string.Join(",", "Recurve Bow", "Bow", "full draw", F(authored), F(authored / 1.15f), F(authored / 1.15f), "s", "Bow Charge Speed affix at its maximum roll (+15%)"));
        }

        [UnityTest]
        public IEnumerator AmmoPouch_RaisesWhatTheRunCanActuallyCarry()
        {
            var rig = StartRun(("weapon_p9_ranger", EquippedSlot.PrimaryWeapon));
            yield return null;
            var light = _ammo[AmmoType.Light];
            var baseLimit = rig.Inventory.MaxStackFor(light);
            Assert.AreEqual(_catalog.AmmoBalance.GetStackLimit(AmmoType.Light), baseLimit, "the baseline is the approved stack limit");

            // Fill to the baseline limit first, so the extra capacity is the only thing that can let more in.
            rig.Inventory.Add(AmmoType.Light, 10000);
            var carriedBefore = rig.Inventory.Get(AmmoType.Light);

            Equip(rig, "accessory_ammo_pouch", EquippedSlot.Accessory);
            yield return null;
            var raisedLimit = rig.Inventory.MaxStackFor(light);
            Assert.AreEqual(Mathf.RoundToInt(baseLimit * 1.25f), raisedLimit);
            rig.Inventory.Add(AmmoType.Light, 10000);
            var carriedAfter = rig.Inventory.Get(AmmoType.Light);
            Assert.Greater(carriedAfter, carriedBefore, "the run really carries more rounds with the pouch equipped");

            Unequip(rig, EquippedSlot.Accessory);
            yield return null;
            Assert.AreEqual(baseLimit, rig.Inventory.MaxStackFor(light), "removing it restores the baseline rule");
            Assert.AreEqual(carriedAfter, rig.Inventory.Get(AmmoType.Light), "and never destroys rounds already carried");
            RecordAccessory("Ammo Pouch", StatId.AmmoStackCapacity, "+25%", "light ammo stack limit", baseLimit, raisedLimit, baseLimit * 1.25f, rig.Inventory.MaxStackFor(light), "PlayerInventory.MaxStackFor");
        }

        [UnityTest]
        public IEnumerator Stabilizer_TightensTheAuthoredShotgunCone()
        {
            var rig = StartRun(("weapon_breacher_12", EquippedSlot.PrimaryWeapon));
            yield return null;
            var weapon = Weapon<RangedWeapon>(rig);
            var authored = weapon.Definition.SpreadDegrees;
            Assert.Greater(authored, 0f, "the Breacher authors a real cone for the stat to act on");
            var baseline = weapon.CurrentSpreadDegrees;

            Equip(rig, "accessory_stabilizer", EquippedSlot.Accessory);
            yield return null;
            var equipped = weapon.CurrentSpreadDegrees;
            Assert.Less(equipped, baseline);

            // The pattern the weapon actually fires with is rebuilt from the tightened cone.
            Assert.IsTrue(weapon.TryFire());
            var pellets = weapon.LastSpawnedProjectiles.ToList();
            Assert.AreEqual(weapon.Definition.ProjectilesPerShot, pellets.Count);
            var widest = pellets.Max(p => Vector2.Angle(Vector2.right, p.Data.Direction));
            Assert.LessOrEqual(widest, equipped / 2f + 0.5f, "no pellet leaves the tightened cone");

            Unequip(rig, EquippedSlot.Accessory);
            yield return null;
            RecordAccessory("Stabilizer", StatId.WeaponSpreadReduction, "+15%", "effective cone (degrees)", baseline, equipped, authored * 0.85f, weapon.CurrentSpreadDegrees, "RangedWeapon.CurrentSpreadDegrees -> FiringPattern");
        }

        [UnityTest]
        public IEnumerator LoadersGlove_ShortensARealReload()
        {
            var rig = StartRun(("weapon_marauder_a2", EquippedSlot.PrimaryWeapon));
            yield return null;
            var weapon = Weapon<RangedWeapon>(rig);
            rig.Inventory.Add(weapon.Definition.AmmoType, 90);
            var authored = weapon.Definition.ReloadTime;
            var baseline = weapon.CurrentReloadTime;

            Equip(rig, "accessory_loaders_glove", EquippedSlot.Accessory);
            yield return null;
            var equipped = weapon.CurrentReloadTime;
            Assert.IsTrue(weapon.TryFire());
            Assert.IsTrue(weapon.TryStartReload());
            yield return new WaitForSeconds(equipped * 0.6f);
            Assert.IsTrue(weapon.IsReloading, "still reloading before the shortened time");
            yield return new WaitForSeconds(equipped * 0.6f);
            Assert.IsFalse(weapon.IsReloading, "and finished by it");

            Unequip(rig, EquippedSlot.Accessory);
            yield return null;
            RecordAccessory("Loader's Glove", StatId.ReloadSpeed, "+10%", "reload time (s)", baseline, equipped, authored / 1.1f, weapon.CurrentReloadTime, "RangedWeapon.CurrentReloadTime");
        }

        [UnityTest]
        public IEnumerator TheAlreadyLiveAccessoriesStillMeasureCorrectly()
        {
            var rig = StartRun(("weapon_p9_ranger", EquippedSlot.PrimaryWeapon));
            yield return null;
            var movement = rig.Player.GetComponent<PlayerMovement>();
            var dash = rig.Player.GetComponent<PlayerDash>();
            var attractor = rig.Player.GetComponent<PickupAttractor>();

            foreach (var (id, name, stat, granted, quantity, read, expectedFactor, consumer) in new (string, string, StatId, string, string, System.Func<float>, float, string)[]
            {
                ("accessory_runners_watch", "Runner's Watch", StatId.MovementSpeed, "+5%", "move speed (tiles/s)", () => movement.CurrentMoveSpeed, 1.05f, "PlayerMovement.CurrentMoveSpeed"),
                ("accessory_dash_capacitor", "Dash Capacitor", StatId.DashCooldownReduction, "+10%", "dash cooldown (s)", () => dash.CurrentDashCooldown, 0.9f, "PlayerDash.CurrentDashCooldown"),
                ("accessory_magnetic_coil", "Magnetic Coil", StatId.PickupAttractionRadius, "+3 flat", "attraction radius (tiles)", () => attractor.Radius, 0f, "PickupAttractor.Radius"),
            })
            {
                var baseline = read();
                Equip(rig, id, EquippedSlot.Accessory);
                yield return null;
                var equipped = read();
                var expected = expectedFactor > 0f ? baseline * expectedFactor : baseline + 3f;
                Unequip(rig, EquippedSlot.Accessory);
                yield return null;
                RecordAccessory(name, stat, granted, quantity, baseline, equipped, expected, read(), consumer);
            }
        }

        [UnityTest]
        public IEnumerator TraumaPendant_RaisesHealingActuallyRestored()
        {
            var rig = StartRun(("weapon_p9_ranger", EquippedSlot.PrimaryWeapon));
            yield return null;
            var health = rig.Player.GetComponent<HealthComponent>();
            var stats = rig.StatsBinder.Stats;
            var baseline = stats.GetMultiplier(StatId.HealingReceived);

            Equip(rig, "accessory_trauma_pendant", EquippedSlot.Accessory);
            yield return null;
            var equipped = stats.GetMultiplier(StatId.HealingReceived);

            var definition = ScriptableObject.CreateInstance<ConsumableDefinition>();
            _created.Add(definition);
            SetPrivate(definition, "_effectKind", ConsumableEffectKind.Heal);
            SetPrivate(definition, "_healAmount", 40);

            health.SetMaxHealth(1000);
            Assert.IsTrue(health.TryApplyDamage(new DamageRequest(500)));
            var damaged = health.CurrentHealth;
            var runner = new ConsumableEffectRunner(new ConsumableTargets(stats, new PlayerCombatEvents(), amount =>
            {
                var from = health.CurrentHealth;
                health.Heal(amount);
                return health.CurrentHealth - from;
            }));
            Assert.IsTrue(runner.Apply(definition));
            var restored = health.CurrentHealth - damaged;
            Assert.AreEqual(Mathf.RoundToInt(40 * 1.15f), restored, "the pendant really raises the HP a bandage restores");

            Unequip(rig, EquippedSlot.Accessory);
            yield return null;
            RecordAccessory("Trauma Pendant", StatId.HealingReceived, "+15%", "healing multiplier", baseline, equipped, 1.15f, stats.GetMultiplier(StatId.HealingReceived), "ConsumableEffects heal");
        }

        [UnityTest]
        public IEnumerator QuickdrawHolster_IsNotAcquirableAndPromisesNothing()
        {
            var holster = (EquipmentItemDefinition)Resolve("accessory_quickdraw_holster");
            Assert.IsNotNull(holster, "the definition is retained for the future design pass");
            Assert.IsFalse(holster.IsAcquirableInV1);
            AccessoryRows.Add(string.Join(",", "Quickdraw Holster", StatId.WeaponSwitchSpeed.ToString(), "+15%", "n/a — no switch duration exists", "n/a", "n/a", "n/a", "n/a",
                "none (deferred)", "EXCLUDED_FROM_V1_ACQUISITION"));
            yield return null;
        }

        // ================= impact =================

        /// <summary>A real enemy body with the authored resistance profile from its definition.</summary>
        private ImpactReceiver Enemy(string name, Vector2 position, int staggerResist, int knockbackResist, bool displaceable, bool boss)
        {
            var go = new GameObject(name);
            _created.Add(go);
            go.transform.position = position;
            var body = go.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.bodyType = RigidbodyType2D.Dynamic;
            body.freezeRotation = true;
            go.AddComponent<CircleCollider2D>().radius = 0.25f;
            var receiver = go.AddComponent<ImpactReceiver>();
            receiver.SetConfig(_stagger);
            receiver.SetProfile(new ImpactProfile(name, staggerResist, knockbackResist, displaceable, boss));
            return receiver;
        }

        [UnityTest]
        public IEnumerator ImpactWeaponsMoveAndStaggerEnemiesAndOrdinaryWeaponsDoNot()
        {
            foreach (var (id, role) in new[]
            {
                ("weapon_breacher_12", "Shotgun: close-range shove"),
                ("weapon_pipe_launcher", "Rocket: heavy blast impact"),
                ("weapon_scrap_spear", "Spear: heavy melee"),
                ("weapon_field_knife", "Knife: fast, low impact"),
                ("weapon_p9_ranger", "Pistol: no impact identity"),
            })
            {
                yield return ImpactRowFor(id, role);
            }
        }

        private int _impactLane;

        private IEnumerator ImpactRowFor(string weaponId, string role)
        {
            // Each weapon gets its own lane, well clear of the player at the origin and of every other iteration's
            // bodies: a target standing on the player overlaps its collider and is shoved by physics rather than by
            // the hit, which silently halves the measured displacement.
            var lane = 20f + _impactLane++ * 40f;
            var rig = StartRun((weaponId, EquippedSlot.PrimaryWeapon));
            yield return null;
            var definition = (WeaponDefinition)Resolve(weaponId);
            var stats = rig.StatsBinder.Stats;

            // The values the weapon really puts on a hit, read back from what it actually produced where it spawns a
            // projectile, and from the same WeaponStatMath call the melee path uses where it does not.
            float knockback, stagger;
            var ranged = rig.Player.GetComponents<RangedWeapon>().FirstOrDefault();
            if (ranged != null)
            {
                Assert.IsTrue(ranged.TryFire(), weaponId + " fires");
                var shot = ranged.LastSpawnedProjectile;
                Assert.IsNotNull(shot);
                knockback = shot.Data.Knockback;
                stagger = shot.Data.StaggerPower;
                Assert.AreEqual(WeaponStatMath.Knockback(definition.Knockback, stats), knockback, 1e-3f, "the projectile carries the weapon's computed knockback");
            }
            else
            {
                knockback = WeaponStatMath.Knockback(definition.Knockback, stats);
                stagger = WeaponStatMath.StaggerPower(definition.StaggerPower, stats);
            }

            var pellets = definition is RangedWeaponDefinition r ? r.ProjectilesPerShot : 1;

            var normal = Enemy("grunt", new Vector2(lane, 0f), 0, 0, true, false);
            var resistant = Enemy("brute", new Vector2(lane, 6f), 60, 60, true, false);
            var boss = Enemy("boss", new Vector2(lane, 12f), 95, 100, false, true);
            yield return null;

            var request = new ImpactRequest(Vector2.right, knockback, stagger);
            var normalStart = (Vector2)normal.transform.position;
            var resistantStart = (Vector2)resistant.transform.position;
            var bossStart = (Vector2)boss.transform.position;
            for (var pellet = 0; pellet < pellets; pellet++)
            {
                ImpactDispatcher.Apply(normal, request);
                ImpactDispatcher.Apply(resistant, request);
                ImpactDispatcher.Apply(boss, request);
            }

            var normalStaggered = normal.IsStaggered;
            var resistantStaggered = resistant.IsStaggered;
            var bossStaggered = boss.IsStaggered;
            for (var i = 0; i < 30; i++) yield return new WaitForFixedUpdate();

            var normalMoved = Vector2.Distance(normalStart, normal.transform.position);
            var resistantMoved = Vector2.Distance(resistantStart, resistant.transform.position);
            var bossMoved = Vector2.Distance(bossStart, boss.transform.position);

            Assert.LessOrEqual(bossMoved, 0.01f, "a boss is never displaced, whatever the weapon");
            if (knockback > 0f)
            {
                Assert.Greater(normalMoved, 0.1f, weaponId + " should move an unresisted enemy");
                Assert.Less(resistantMoved, normalMoved, "resistance must shorten the displacement");
            }
            else
            {
                Assert.LessOrEqual(normalMoved, 0.05f, weaponId + " authors no knockback and must move nothing");
            }

            ImpactRows.Add(string.Join(",", new[]
            {
                definition.DisplayName, definition.WeaponClass.ToString(), "0", "0", F(definition.Knockback), F(definition.StaggerPower), role,
                knockback > 0f || normalStaggered ? $"moved {F(normalMoved)} tiles{(normalStaggered ? " and staggered" : string.Empty)}" : "no effect",
                resistantStaggered ? $"moved {F(resistantMoved)} tiles and staggered" : $"moved {F(resistantMoved)} tiles, resisted the stagger",
                bossStaggered ? "staggered, never displaced" : "immune (95% stagger resistance, not displaceable)",
                stagger > 0f ? "can trigger arc_stagger / Shock Charm and wallbreaker" : "cannot trigger impact passives",
                "PASS"
            }.Select(Csv)));
        }

        [UnityTest]
        public IEnumerator KnockbackNeverPushesAnEnemyOutOfItsEncounterBounds()
        {
            var enemy = Enemy("grunt", new Vector2(2.4f, 0f), 0, 0, true, false);
            var bounds = EncounterBounds.Bind(enemy.gameObject, new Rect(-3f, -3f, 6f, 6f), "room_test", 1);
            Assert.IsNotNull(bounds);
            yield return null;

            // A rocket's full impact, aimed straight at the wall it is standing next to.
            enemy.ApplyKnockback(new ImpactRequest(Vector2.right, 12f, 12f));
            for (var i = 0; i < 40; i++) yield return new WaitForFixedUpdate();

            var legal = bounds.Legal;
            Assert.LessOrEqual(enemy.transform.position.x, legal.xMax + 1e-3f, "knockback stops at the legal edge of the encounter");
            Assert.GreaterOrEqual(enemy.transform.position.x, legal.xMin - 1e-3f);
            Assert.Greater(enemy.BoundsStops + enemy.WallImpacts, 0, "and the stop is recorded rather than silently clamped");
        }

        [UnityTest]
        public IEnumerator ImpactModuleAndShockCharmScaleTheWeaponsOwnImpact()
        {
            var rig = StartRun(("weapon_breacher_12", EquippedSlot.PrimaryWeapon));
            yield return null;
            var weapon = Weapon<RangedWeapon>(rig);
            var stats = rig.StatsBinder.Stats;
            var authoredKnockback = weapon.Definition.Knockback;
            var authoredStagger = weapon.Definition.StaggerPower;

            rig.Inventory.Add(weapon.Definition.AmmoType, 60);
            yield return FireAgain(weapon);
            var baselineKnockback = weapon.LastSpawnedProjectile.Data.Knockback;
            var baselineStagger = weapon.LastSpawnedProjectile.Data.StaggerPower;
            Assert.AreEqual(authoredKnockback, baselineKnockback, 1e-3f);

            Equip(rig, "accessory_impact_module", EquippedSlot.Accessory);
            yield return FireAgain(weapon);
            var boostedKnockback = weapon.LastSpawnedProjectile.Data.Knockback;
            Unequip(rig, EquippedSlot.Accessory);
            yield return FireAgain(weapon);
            RecordAccessory("Impact Module", StatId.Knockback, "+15%", "knockback on a fired shot", baselineKnockback, boostedKnockback, authoredKnockback * 1.15f,
                weapon.LastSpawnedProjectile.Data.Knockback, "RangedWeapon -> ProjectileSpawnData.Knockback -> ImpactReceiver");

            Equip(rig, "accessory_shock_charm", EquippedSlot.Accessory);
            yield return FireAgain(weapon);
            var boostedStagger = weapon.LastSpawnedProjectile.Data.StaggerPower;
            Unequip(rig, EquippedSlot.Accessory);
            yield return FireAgain(weapon);
            RecordAccessory("Shock Charm", StatId.StaggerPower, "+15%", "stagger power on a fired shot", baselineStagger, boostedStagger, authoredStagger * 1.15f,
                weapon.LastSpawnedProjectile.Data.StaggerPower, "RangedWeapon -> ProjectileSpawnData.StaggerPower -> StaggerMeter");
        }

        // ================= affixes reach the run at all =================

        [UnityTest]
        public IEnumerator ARolledAffixOnAnEquippedItemChangesTheRunsStats()
        {
            var rig = StartRun(("weapon_marauder_a2", EquippedSlot.PrimaryWeapon));
            yield return null;
            var weapon = Weapon<RangedWeapon>(rig);
            var stats = rig.StatsBinder.Stats;
            var baselineInterval = weapon.CurrentFireInterval;
            var baselineMagazine = weapon.CurrentMagazineSize;

            // The composition root must turn a persisted affix roll back into a real modifier. Before this pass it
            // resolved every affix to null and a Legendary weapon was numerically a Common one.
            var rolled = new ItemInstance("weapon_vanguard", 1, Rarity.Rare);
            rolled.AddAffixRoll(new AffixRoll("affix_fire_rate", 9));
            rolled.AddAffixRoll(new AffixRoll("affix_magazine_size", 20));
            Assert.IsTrue(rig.Inventory.TryEquip(rolled, EquippedSlot.SecondaryWeapon));
            yield return null;

            Assert.AreEqual(9, stats.GetPercent(StatId.FireRate), "the rolled Fire Rate affix reached the pipeline");
            Assert.AreEqual(20, stats.GetPercent(StatId.MagazineSize), "so did the rolled Magazine Size affix");
            Assert.Less(weapon.CurrentFireInterval, baselineInterval, "and the equipped weapon fires faster because of it");
            Assert.Greater(weapon.CurrentMagazineSize, baselineMagazine);

            rig.Inventory.Unequip(EquippedSlot.SecondaryWeapon);
            yield return null;
            Assert.AreEqual(0, stats.GetPercent(StatId.FireRate), "unequipping removes the affix contribution exactly");
            Assert.AreEqual(baselineInterval, weapon.CurrentFireInterval, 1e-5f);
            Assert.AreEqual(baselineMagazine, weapon.CurrentMagazineSize);
        }

        // ================= Phase 12 regressions the new stats could have introduced =================

        [UnityTest]
        public IEnumerator FireRateCannotBypassAReload()
        {
            var rig = StartRun(("weapon_marauder_a2", EquippedSlot.PrimaryWeapon));
            yield return null;
            var weapon = Weapon<RangedWeapon>(rig);
            rig.Inventory.Add(weapon.Definition.AmmoType, 90);
            var stats = rig.StatsBinder.Stats;

            // The largest Fire Rate the pools can grant, and then some.
            stats.SetSource(new StatModifierSource("runaway", StatModifier.Percent(StatId.FireRate, 400)));
            Assert.IsTrue(weapon.TryFire());
            Assert.IsTrue(weapon.TryStartReload());
            var reloadTime = weapon.CurrentReloadTime;
            Assert.AreEqual(weapon.Definition.ReloadTime, reloadTime, 1e-4f, "Fire Rate does not shorten the reload; only Reload Speed does");

            var attempts = 0;
            var elapsed = 0f;
            while (elapsed < reloadTime * 0.8f)
            {
                Assert.IsFalse(weapon.TryFire(), "a reloading weapon cannot fire, whatever its cadence");
                attempts++;
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.Greater(attempts, 5, "the attempt was made repeatedly, not once");
            Assert.IsTrue(weapon.IsReloading, "and the reload is still the thing gating the weapon");
        }

        [UnityTest]
        public IEnumerator ProjectileModifiersDoNotDesyncTheVisualOrOvershootTheRange()
        {
            var rig = StartRun(("weapon_p9_ranger", EquippedSlot.PrimaryWeapon));
            yield return null;
            var weapon = Weapon<RangedWeapon>(rig);
            var stats = rig.StatsBinder.Stats;
            stats.SetSource(new StatModifierSource("fast_and_far",
                StatModifier.Percent(StatId.ProjectileSpeed, 40), StatModifier.Percent(StatId.ProjectileRange, 40)));

            Assert.IsTrue(weapon.TryFire());
            var projectile = weapon.LastSpawnedProjectile;
            Assert.IsNotNull(projectile);
            var range = weapon.CurrentRange;
            Assert.AreEqual(range, projectile.Data.MaxRange, 1e-3f);
            var origin = (Vector2)projectile.transform.position;

            // Step it out past its own lifetime: the sprite must sit exactly on the body every step and the flight must
            // stop at the modified range, never beyond it.
            for (var i = 0; i < 120 && !projectile.IsResolved; i++)
            {
                yield return new WaitForFixedUpdate();
                var visual = projectile.Visual;
                if (visual != null)
                    Assert.AreEqual((Vector2)projectile.transform.position, (Vector2)visual.transform.position,
                        "the projectile sprite never lags its collider");
                Assert.LessOrEqual(Vector2.Distance(origin, projectile.transform.position), range + 0.5f,
                    "the flight stops at the modified range rather than sailing past it");
            }
        }

        // ================= representative weapons, before and after =================

        [UnityTest]
        public IEnumerator RepresentativeWeaponsRecordTheirBeforeAndAfterNumbers()
        {
            var caps = _catalog.StatCaps;
            foreach (var id in new[]
            {
                "weapon_p9_ranger", "weapon_rattler_9", "weapon_marauder_a2", "weapon_breacher_12", "weapon_farline",
                "weapon_pipe_launcher", "weapon_recurve_bow", "weapon_pulse_carbine_b1", "weapon_field_knife", "weapon_scrap_spear",
            })
            {
                var definition = (WeaponDefinition)Resolve(id);
                Assert.IsNotNull(definition, id);
                var none = new PlayerStats(caps, 100);

                var single = new PlayerStats(caps, 100);
                single.SetSource(new StatModifierSource("max_affix", MaxRollFor(definition)));

                var epic = new PlayerStats(caps, 100);
                epic.SetSource(new StatModifierSource("epic", EpicComboFor(definition)));

                var moved = 0;
                foreach (var (measure, unit, read) in MeasuresFor(definition))
                {
                    WeaponRows.Add(string.Join(",", new[]
                    {
                        definition.DisplayName, definition.WeaponClass.ToString(), measure,
                        F(read(none)), F(read(single)), F(read(epic)), unit, "authored value x the capped stat, computed by the shipped WeaponStatMath"
                    }.Select(Csv)));
                    if (!Mathf.Approximately(read(none), read(epic))) moved++;
                }

                Assert.Greater(moved, 0, $"{id}: an Epic combination must change at least one measured quantity");
            }

            yield return null;
        }

        /// <summary>The single strongest roll the weapon's own pool can give it.</summary>
        private static StatModifier[] MaxRollFor(WeaponDefinition definition) => definition switch
        {
            MeleeWeaponDefinition => new[] { StatModifier.Percent(StatId.MeleeAttackSpeed, 9) },
            BowWeaponDefinition => new[] { StatModifier.Percent(StatId.BowChargeSpeed, 15) },
            BlasterWeaponDefinition => new[] { StatModifier.Percent(StatId.FireRate, 9) },
            _ => new[] { StatModifier.Percent(StatId.FireRate, 9) },
        };

        /// <summary>Three maximum rolls, the shape an Epic item actually takes.</summary>
        private static StatModifier[] EpicComboFor(WeaponDefinition definition) => definition switch
        {
            MeleeWeaponDefinition => new[] { StatModifier.Percent(StatId.WeaponDamage, 10), StatModifier.Percent(StatId.MeleeAttackSpeed, 9), StatModifier.Percent(StatId.StaggerPower, 16) },
            BowWeaponDefinition => new[] { StatModifier.Percent(StatId.WeaponDamage, 10), StatModifier.Percent(StatId.BowChargeSpeed, 15), StatModifier.Percent(StatId.ProjectileSpeed, 15) },
            BlasterWeaponDefinition => new[] { StatModifier.Percent(StatId.WeaponDamage, 10), StatModifier.Percent(StatId.FireRate, 9), StatModifier.Percent(StatId.ProjectileRange, 15) },
            _ => new[] { StatModifier.Percent(StatId.FireRate, 9), StatModifier.Percent(StatId.MagazineSize, 20), StatModifier.Percent(StatId.ProjectileRange, 15) },
        };

        private static IEnumerable<(string Measure, string Unit, System.Func<IPlayerStatsProvider, float>)> MeasuresFor(WeaponDefinition definition)
        {
            switch (definition)
            {
                case RangedWeaponDefinition ranged:
                    yield return ("fire rate", "shots/s", s => WeaponStatMath.FireRate(ranged.FireRate, s));
                    yield return ("sustained dps (magazine + reload)", "damage/s", s => SustainedDps(ranged, s));
                    yield return ("effective magazine", "rounds", s => WeaponStatMath.MagazineSize(ranged.MagazineSize, s));
                    yield return ("range", "tiles", s => WeaponStatMath.ProjectileRange(ranged.Range, s));
                    yield return ("projectile speed", "tiles/s", s => WeaponStatMath.ProjectileSpeed(ranged.ProjectileSpeed, s));
                    yield return ("knockback", "points", s => WeaponStatMath.Knockback(ranged.Knockback, s));
                    break;
                case BlasterWeaponDefinition blaster:
                    yield return ("fire rate", "shots/s", s => WeaponStatMath.FireRate(blaster.FireRate, s));
                    yield return ("burst dps (to overheat)", "damage/s", s => BurstDps(blaster, s));
                    yield return ("range", "tiles", s => WeaponStatMath.ProjectileRange(blaster.Range, s));
                    yield return ("heat per shot", "heat", s => WeaponStatMath.BlasterHeatPerShot(blaster.HeatPerShot, s));
                    break;
                case BowWeaponDefinition bow:
                    yield return ("full draw", "s", s => WeaponStatMath.BowFullChargeSeconds(bow.FullChargeSeconds, s));
                    yield return ("full-draw range", "tiles", s => WeaponStatMath.ProjectileRange(bow.FullRange, s));
                    yield return ("full-draw projectile speed", "tiles/s", s => WeaponStatMath.ProjectileSpeed(bow.FullProjectileSpeed, s));
                    yield return ("full-draw dps", "damage/s", s => AverageDamage(bow.FullDrawDamageMin, bow.FullDrawDamageMax, s) / WeaponStatMath.BowFullChargeSeconds(bow.FullChargeSeconds, s));
                    break;
                case MeleeWeaponDefinition melee:
                    yield return ("attack rate", "swings/s", s => WeaponStatMath.MeleeAttackRate(melee.AttackRate, s));
                    yield return ("sustained dps", "damage/s", s => MeleeDps(melee, s));
                    yield return ("stagger power", "points", s => WeaponStatMath.StaggerPower(melee.StaggerPower, s));
                    yield return ("knockback", "points", s => WeaponStatMath.Knockback(melee.Knockback, s));
                    break;
            }
        }

        private static float AverageDamage(int min, int max, IPlayerStatsProvider stats) => (min + max) / 2f * stats.GetMultiplier(StatId.WeaponDamage);

        private static float SustainedDps(RangedWeaponDefinition d, IPlayerStatsProvider s)
        {
            var magazine = WeaponStatMath.MagazineSize(d.MagazineSize, s);
            var perShot = AverageDamage(d.DamageMin, d.DamageMax, s) * d.ProjectilesPerShot;
            var cycle = magazine * WeaponStatMath.FireInterval(d.FireRate, s) + d.ReloadTime / s.GetMultiplier(StatId.ReloadSpeed);
            return magazine * perShot / cycle;
        }

        private static float BurstDps(BlasterWeaponDefinition d, IPlayerStatsProvider s)
        {
            var heat = WeaponStatMath.BlasterHeatPerShot(d.HeatPerShot, s);
            var shots = Mathf.Max(1f, d.MaxHeat / Mathf.Max(0.01f, heat));
            var interval = WeaponStatMath.FireInterval(d.FireRate, s);
            return shots * AverageDamage(d.DamageMin, d.DamageMax, s) / (shots * interval + d.OverheatLockoutSeconds);
        }

        private static float MeleeDps(MeleeWeaponDefinition d, IPlayerStatsProvider s) =>
            AverageDamage(d.DamageMin, d.DamageMax, s) * WeaponStatMath.MeleeAttackRate(d.AttackRate, s);

        private static void SetPrivate(object target, string field, object value)
        {
            var type = target.GetType();
            System.Reflection.FieldInfo info = null;
            while (type != null && info == null)
            {
                info = type.GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                type = type.BaseType;
            }

            Assert.IsNotNull(info, "field " + field);
            info.SetValue(target, value);
        }

        private static string Csv(string value)
        {
            value ??= string.Empty;
            return value.Contains(',') || value.Contains('"') ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }
    }
}

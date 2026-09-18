using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Accessories;
using RuinRail.Gameplay.Items.Passives;
using RuinRail.Gameplay.Stats;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>Exact conditions, magnitudes, durations and cooldowns of the sixteen Legendary accessory passives.</summary>
    public class AccessoryPassiveTests
    {
        private GlobalStatCapsConfig _caps;
        private PlayerStats _stats;
        private PlayerCombatEvents _events;
        private PassiveContext _context;
        private int _healed;
        private readonly List<(float radius, int min, int max)> _areaDamage = new();
        private readonly List<(float radius, float kb, float stagger)> _shockwaves = new();
        private int _pulls;

        [SetUp]
        public void SetUp()
        {
            _caps = ScriptableObject.CreateInstance<GlobalStatCapsConfig>();
            _stats = new PlayerStats(_caps, 100);
            _events = new PlayerCombatEvents();
            _healed = 0;
            _areaDamage.Clear();
            _shockwaves.Clear();
            _pulls = 0;
            var world = new PassiveWorldActions
            {
                AreaDamage = (r, min, max) => _areaDamage.Add((r, min, max)),
                Shockwave = (r, kb, st) => _shockwaves.Add((r, kb, st)),
                PullCoinAndAmmoPickups = () => _pulls++
            };
            _context = new PassiveContext(_stats, _events, () => 100, () => 100, a => { _healed += a; return true; }, () => { }, world);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_caps);
        }

        private T Attach<T>() where T : AccessoryPassive, new()
        {
            var passive = new T();
            passive.Attach(_context);
            return passive;
        }

        [Test]
        public void Momentum_Plus10MoveFor3SecondsAfterDash()
        {
            var p = Attach<WatchMomentumPassive>();
            _events.RaiseDashed();
            Assert.AreEqual(10, _stats.GetPercent(StatId.MovementSpeed));
            p.Tick(2.9f);
            Assert.AreEqual(10, _stats.GetPercent(StatId.MovementSpeed));
            p.Tick(0.2f);
            Assert.AreEqual(0, _stats.GetPercent(StatId.MovementSpeed));
        }

        [Test]
        public void SteadyAim_Plus12ProjectileDamage_After1SecondStill_EndsOnMovement()
        {
            var p = Attach<SteadyAimPassive>();
            _events.RaiseMovementStateChanged(false);
            p.Tick(0.9f);
            Assert.AreEqual(0, _events.RaiseAttackDamageRolling(100, true).BonusPercent, "Not yet still for 1s.");
            p.Tick(0.2f);
            Assert.AreEqual(12, _events.RaiseAttackDamageRolling(100, true).BonusPercent);
            Assert.AreEqual(0, _events.RaiseAttackDamageRolling(100, false).BonusPercent, "Melee is not a projectile weapon.");
            _events.RaiseMovementStateChanged(true);
            Assert.AreEqual(0, _events.RaiseAttackDamageRolling(100, true).BonusPercent, "Ends immediately when movement resumes.");
            _events.RaiseMovementStateChanged(false);
            p.Tick(1.0f);
            Assert.AreEqual(112, _events.RaiseAttackDamageRolling(100, true).FinalAmount);
        }

        [Test]
        public void FlowState_Plus15MeleeAttackSpeed_4Seconds_RefreshedByMeleeKills()
        {
            var p = Attach<FlowStatePassive>();
            _events.RaiseEnemyKilled();
            Assert.AreEqual(0, _stats.GetPercent(StatId.MeleeAttackSpeed), "Ranged kill does nothing.");
            _events.RaiseMeleeKill();
            Assert.AreEqual(15, _stats.GetPercent(StatId.MeleeAttackSpeed));
            p.Tick(3.5f);
            _events.RaiseMeleeKill();
            p.Tick(3.5f);
            Assert.AreEqual(15, _stats.GetPercent(StatId.MeleeAttackSpeed), "Refreshed to a full 4s.");
            p.Tick(0.6f);
            Assert.AreEqual(0, _stats.GetPercent(StatId.MeleeAttackSpeed));
        }

        [Test]
        public void HotSwap_Plus15Speed_3Seconds_Cooldown6()
        {
            var p = Attach<HotSwapPassive>();
            _events.RaiseWeaponSwapped();
            Assert.AreEqual(15, _stats.GetPercent(StatId.FireRate));
            Assert.AreEqual(15, _stats.GetPercent(StatId.MeleeAttackSpeed));
            p.Tick(3.1f);
            Assert.AreEqual(0, _stats.GetPercent(StatId.FireRate));
            _events.RaiseWeaponSwapped();
            Assert.AreEqual(0, _stats.GetPercent(StatId.FireRate), "3.1s: still inside the 6s cooldown.");
            p.Tick(3.0f);
            _events.RaiseWeaponSwapped();
            Assert.AreEqual(15, _stats.GetPercent(StatId.FireRate));
        }

        [Test]
        public void FreshMag_Next3AttacksAfterReload_Plus15Damage()
        {
            var p = Attach<FreshMagPassive>();
            Assert.AreEqual(0, _events.RaiseAttackDamageRolling(20, true).BonusPercent);
            _events.RaiseReloadCompleted();
            Assert.AreEqual(23, _events.RaiseAttackDamageRolling(20, true).FinalAmount);
            Assert.AreEqual(15, _events.RaiseAttackDamageRolling(20, true).BonusPercent);
            Assert.AreEqual(15, _events.RaiseAttackDamageRolling(20, false).BonusPercent, "Any weapon attack counts (shots, not pellets).");
            Assert.AreEqual(0, _events.RaiseAttackDamageRolling(20, true).BonusPercent, "Fourth attack is normal.");
            Assert.AreEqual(0, p.RemainingAttacks);
        }

        [Test]
        public void ColdStart_HalvesHeatFor5Shots_OnlyAfterCoolingFrom50Plus()
        {
            var p = Attach<ColdStartPassive>();
            _events.RaiseBlasterCooledToZero(49f);
            Assert.AreEqual(7f, _events.RaiseBlasterShotHeatRolling(7f).FinalHeat, 0.001f, "Peak below 50: no effect.");
            _events.RaiseBlasterCooledToZero(50f);
            for (var i = 0; i < 5; i++) Assert.AreEqual(3.5f, _events.RaiseBlasterShotHeatRolling(7f).FinalHeat, 0.001f, $"shot {i + 1}");
            Assert.AreEqual(7f, _events.RaiseBlasterShotHeatRolling(7f).FinalHeat, 0.001f, "Sixth shot is normal.");
            Assert.AreEqual(0, p.RemainingShots);
        }

        [Test]
        public void EmergencyVent_PulseOnOverheat_2Point5Tiles_30To40_Cooldown8()
        {
            var p = Attach<EmergencyVentPassive>();
            _events.RaiseBlasterOverheated();
            Assert.AreEqual(1, _areaDamage.Count);
            Assert.AreEqual((2.5f, 30, 40), _areaDamage[0]);
            _events.RaiseBlasterOverheated();
            Assert.AreEqual(1, _areaDamage.Count, "Cooldown blocks.");
            p.Tick(8.1f);
            _events.RaiseBlasterOverheated();
            Assert.AreEqual(2, _areaDamage.Count);
        }

        [Test]
        public void PerfectDraw_FullDrawShotsPenetrateOneEnemy()
        {
            Attach<PerfectDrawPassive>();
            Assert.AreEqual(0, _events.RaiseBowShotFired(false).Penetrations);
            Assert.AreEqual(1, _events.RaiseBowShotFired(true).Penetrations);
        }

        [Test]
        public void Discharge_ShockwaveAtDashEnd_NoDamage_Cooldown6()
        {
            var p = Attach<DischargePassive>();
            _events.RaiseDashed();
            Assert.AreEqual(0, _shockwaves.Count, "Dash start is not the endpoint.");
            _events.RaiseDashEnded();
            Assert.AreEqual(1, _shockwaves.Count);
            Assert.Greater(_shockwaves[0].kb, 0f);
            Assert.Greater(_shockwaves[0].stagger, 0f);
            Assert.AreEqual(0, _areaDamage.Count, "No direct damage.");
            _events.RaiseDashEnded();
            Assert.AreEqual(1, _shockwaves.Count);
            p.Tick(6.1f);
            _events.RaiseDashEnded();
            Assert.AreEqual(2, _shockwaves.Count);
        }

        [Test]
        public void LongShot_Plus15Damage_At7TilesOrMore()
        {
            Attach<LongShotPassive>();
            Assert.AreEqual(0, _events.RaiseProjectileHitRolling(20, 6.99f).BonusPercent);
            Assert.AreEqual(15, _events.RaiseProjectileHitRolling(20, 7f).BonusPercent);
            Assert.AreEqual(23, _events.RaiseProjectileHitRolling(20, 12f).FinalAmount);
        }

        [Test]
        public void SecondPulse_Restores15PercentOfNormalHealingOver5Seconds()
        {
            var p = Attach<SecondPulsePassive>();
            var request = _events.RaiseHealingConsumableUsed(40);
            Assert.AreEqual(40, request.FinalAmount, "The instant heal itself is unchanged.");
            Assert.AreEqual(6, p.PendingHeal, "15% of 40 = 6 pending");
            p.Tick(2.5f);
            Assert.That(_healed, Is.InRange(2, 4));
            p.Tick(2.5f);
            Assert.AreEqual(6, _healed, "All 6 delivered by 5s.");
            p.Tick(5f);
            Assert.AreEqual(6, _healed, "Nothing after the window.");
        }

        [Test]
        public void ScavengersReserve_Plus25PercentAmmoPickups_RoundedToWholeUnits()
        {
            Attach<ScavengersReservePassive>();
            Assert.AreEqual(25, _events.RaiseAmmoPickupRolling(20).FinalAmount);
            Assert.AreEqual(9, _events.RaiseAmmoPickupRolling(7).FinalAmount, "7 × 1.25 = 8.75 → 9");
            Assert.AreEqual(4, _events.RaiseAmmoPickupRolling(3).FinalAmount, "3 × 1.25 = 3.75 → 4");
        }

        [Test]
        public void RoomSweep_PullsCoinAndAmmoPickups_OnCombatRoomClear()
        {
            Attach<RoomSweepPassive>();
            _events.RaiseCombatRoomEntered();
            Assert.AreEqual(0, _pulls);
            _events.RaiseCombatRoomCleared();
            Assert.AreEqual(1, _pulls);
        }

        [Test]
        public void LockIn_Extra25SpreadReduction_After1SecondContinuousFire_UntilFiringStops()
        {
            var p = Attach<LockInPassive>();
            _events.RaiseFiringStateChanged(true);
            p.Tick(0.9f);
            Assert.AreEqual(0, _stats.GetPercent(StatId.WeaponSpreadReduction));
            p.Tick(0.2f);
            Assert.AreEqual(25, _stats.GetPercent(StatId.WeaponSpreadReduction));
            _stats.SetSource(new StatModifierSource("intrinsic", StatModifier.Percent(StatId.WeaponSpreadReduction, 15)));
            Assert.AreEqual(40, _stats.GetPercent(StatId.WeaponSpreadReduction), "Additional to the intrinsic.");
            _events.RaiseFiringStateChanged(false);
            Assert.AreEqual(15, _stats.GetPercent(StatId.WeaponSpreadReduction), "Ends when firing stops.");
        }

        [Test]
        public void Wallbreaker_15To20BonusAndStagger_PerTargetCooldown2_NeverBosses()
        {
            var p = Attach<WallbreakerPassive>();
            var first = _events.RaiseEnemyKnockedIntoWall("grunt_1", false);
            Assert.IsTrue(first.HasBonus);
            Assert.AreEqual(15, first.BonusDamageMin);
            Assert.AreEqual(20, first.BonusDamageMax);
            Assert.IsTrue(first.ApplyHighStagger);
            Assert.IsFalse(_events.RaiseEnemyKnockedIntoWall("grunt_1", false).HasBonus, "Same target within 2s.");
            Assert.IsTrue(_events.RaiseEnemyKnockedIntoWall("grunt_2", false).HasBonus, "Other target is independent.");
            Assert.IsFalse(_events.RaiseEnemyKnockedIntoWall("boss", true).HasBonus, "Bosses never trigger it.");
            p.Tick(2.1f);
            Assert.IsTrue(_events.RaiseEnemyKnockedIntoWall("grunt_1", false).HasBonus);
        }

        [Test]
        public void ArcStagger_ShockwaveOnWearerStagger_Cooldown6()
        {
            var p = Attach<ArcStaggerPassive>();
            _events.RaiseEnemyStaggeredByWearer("grunt_1");
            Assert.AreEqual(1, _shockwaves.Count);
            Assert.AreEqual(2.5f, _shockwaves[0].radius, 0.001f);
            Assert.Greater(_shockwaves[0].stagger, 0f);
            _events.RaiseEnemyStaggeredByWearer("grunt_2");
            Assert.AreEqual(1, _shockwaves.Count);
            p.Tick(6.1f);
            _events.RaiseEnemyStaggeredByWearer("grunt_3");
            Assert.AreEqual(2, _shockwaves.Count);
        }

        // ---- Acceptance 4: equip/unequip leaves no stale effects and duplicates nothing ----

        [Test]
        public void Registrar_AttachesAccessoryAndArmorPassivesIndependently_AndClearsStaleEffects()
        {
            var watch = ScriptableObject.CreateInstance<AccessoryDefinition>();
            Set(watch, "_id", "accessory_runners_watch");
            Set(watch, "_category", ItemCategory.Accessory);
            Set(watch, "_legendaryMechanicId", "accessory_momentum");
            var vest = ScriptableObject.CreateInstance<ArmorDefinition>();
            Set(vest, "_id", "armor_scrap_vest");
            Set(vest, "_category", ItemCategory.Armor);
            Set(vest, "_legendaryMechanicId", "patchwork");
            var ammo = ScriptableObject.CreateInstance<AmmoItemDefinition>();
            Set(ammo, "_id", "ammo_light");
            Set(ammo, "_category", ItemCategory.Ammo);
            Set(ammo, "_isStackable", true);
            Set(ammo, "_maxStack", 180);
            var registry = ItemDefinitionRegistry.Build(new ItemDefinition[] { watch, vest, ammo });
            var inventory = PlayerInventory.FromRegistry(registry, null);
            inventory.TryAddToBackpack(new ItemInstance("ammo_light", 50));
            using var registrar = new EquipmentPassiveRegistrar(inventory, id => registry.TryGet(id, out var d) ? d : null, _context);

            inventory.TryEquip(new ItemInstance("accessory_runners_watch", 1, Rarity.Legendary), EquippedSlot.Accessory);
            inventory.TryEquip(new ItemInstance("armor_scrap_vest", 1, Rarity.Legendary), EquippedSlot.Armor);
            Assert.IsInstanceOf<WatchMomentumPassive>(registrar.AccessoryPassive);
            Assert.IsInstanceOf<RuinRail.Gameplay.Items.Armor.PatchworkPassive>(registrar.ArmorPassive);

            _events.RaiseDashed();
            Assert.AreEqual(10, _stats.GetPercent(StatId.MovementSpeed));
            inventory.Unequip(EquippedSlot.Accessory);
            Assert.IsNull(registrar.AccessoryPassive);
            Assert.AreEqual(0, _stats.GetPercent(StatId.MovementSpeed), "Unequip removes the running buff (no stale effect).");
            Assert.IsNotNull(registrar.ArmorPassive, "Armor passive unaffected by the accessory slot.");
            Assert.AreEqual(50, inventory.CountOf("ammo_light"), "Equip/unequip never touches consumable/ammo quantities.");

            for (var i = 0; i < 4; i++)
            {
                inventory.TryEquip(new ItemInstance("accessory_runners_watch", 1, Rarity.Legendary), EquippedSlot.Accessory);
                inventory.Unequip(EquippedSlot.Accessory);
            }

            _events.RaiseDashed();
            Assert.AreEqual(0, _stats.GetPercent(StatId.MovementSpeed), "No leftover subscriptions after repeated cycles.");
            Assert.AreEqual(0, _stats.SourceIds.Count);

            Object.DestroyImmediate(watch);
            Object.DestroyImmediate(vest);
            Object.DestroyImmediate(ammo);
        }

        private static void Set(object target, string field, object value)
        {
            var type = target.GetType();
            FieldInfo info = null;
            while (type != null && info == null)
            {
                info = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
                type = type.BaseType;
            }

            info.SetValue(target, value);
        }
    }
}

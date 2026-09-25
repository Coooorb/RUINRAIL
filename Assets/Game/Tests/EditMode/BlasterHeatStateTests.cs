using NUnit.Framework;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Items;
using UnityEditor;

namespace RuinRail.Tests
{
    public class BlasterHeatStateTests
    {
        private const string PulseCarbineAssetPath = "Assets/Game/ScriptableObjects/Items/PulseCarbineB1.asset";

        /// <summary>
        /// A synthetic heat state for the behavioural tests below. Its 0.6 s / 2.2 s timings are the fixture's own,
        /// deliberately not the catalogue's, so those tests keep exercising the state machine rather than the
        /// authored balance numbers (which PulseCarbineB1_UsesApprovedV1Values pins separately).
        /// </summary>
        private static BlasterHeatState NewState() => new(100f, 35f, 0.6f, 2.2f);

        [Test]
        public void PulseCarbineB1_UsesApprovedV1Values()
        {
            var definition = AssetDatabase.LoadAssetAtPath<BlasterWeaponDefinition>(PulseCarbineAssetPath);

            Assert.IsNotNull(definition, $"Expected a BlasterWeaponDefinition asset at {PulseCarbineAssetPath}.");
            Assert.AreEqual("weapon_pulse_carbine_b1", definition.Id);
            Assert.AreEqual("Pulse Carbine B1", definition.DisplayName);
            Assert.AreEqual(WeaponClass.Blaster, definition.WeaponClass);
            Assert.AreEqual(ItemCategory.Weapon, definition.Category);
            Assert.AreEqual(7, definition.DamageMin);
            Assert.AreEqual(9, definition.DamageMax);
            Assert.AreEqual(8f, definition.FireRate, 0.001f);
            Assert.AreEqual(22f, definition.ProjectileSpeed, 0.001f);
            Assert.AreEqual(7f, definition.HeatPerShot, 0.001f);
            Assert.AreEqual(35f, definition.CoolingRatePerSecond, 0.001f);
            Assert.AreEqual(100f, definition.MaxHeat, 0.001f);
            Assert.AreEqual(0.5f, definition.CoolingDelaySeconds, 0.001f);
            Assert.AreEqual(1.9f, definition.OverheatLockoutSeconds, 0.001f);
            Assert.IsInstanceOf<EquipmentItemDefinition>(definition);
        }

        [Test]
        public void BlasterDefinition_HasNoAmmoTypeOrMagazineConcept()
        {
            var type = typeof(BlasterWeaponDefinition);
            Assert.IsNull(type.GetProperty("AmmoType"));
            Assert.IsNull(type.GetProperty("MagazineSize"));
            Assert.IsNull(type.GetProperty("ReloadTime"));
            Assert.IsNull(type.GetProperty("AmmoCostPerShot"));
        }

        [Test]
        public void Shot_AddsExactlyHeatPerShot_AndNeverExceedsMax()
        {
            var state = NewState();

            Assert.IsTrue(state.AddShotHeat(7f));
            Assert.AreEqual(7f, state.Heat, 0.0001f);
            Assert.IsFalse(state.IsOverheated);

            for (var i = 0; i < 13; i++) state.AddShotHeat(7f);
            Assert.AreEqual(98f, state.Heat, 0.0001f, "14 shots × 7 = 98.");
            Assert.IsFalse(state.IsOverheated);

            Assert.IsTrue(state.AddShotHeat(7f));
            Assert.AreEqual(100f, state.Heat, 0.0001f, "Clamped to max heat, never 105.");
            Assert.IsTrue(state.IsOverheated);
            Assert.AreEqual(2.2f, state.LockoutRemaining, 0.0001f);
        }

        [Test]
        public void Cooling_WaitsForDelay_ThenCoolsAtRate()
        {
            var state = NewState();
            state.AddShotHeat(35f);

            state.Tick(0.5f);
            Assert.AreEqual(35f, state.Heat, 0.0001f, "No cooling during the 0.6s delay.");
            Assert.IsFalse(state.IsCooling);

            state.Tick(0.1f);
            Assert.AreEqual(35f, state.Heat, 0.0001f, "Delay exactly elapsed; cooling starts now.");
            Assert.IsTrue(state.IsCooling);

            state.Tick(0.5f);
            Assert.AreEqual(17.5f, state.Heat, 0.001f, "35/s × 0.5s = 17.5 cooled.");

            state.Tick(1f);
            Assert.AreEqual(0f, state.Heat, 0.0001f, "Never negative.");
            Assert.IsFalse(state.IsCooling);
        }

        [Test]
        public void Cooling_SplitTick_SpendsDelayThenCoolsRemainder()
        {
            var state = NewState();
            state.AddShotHeat(50f);

            state.Tick(1.0f);
            Assert.AreEqual(50f - 35f * 0.4f, state.Heat, 0.001f, "0.6s delay consumed inside the tick, 0.4s of cooling applied.");
        }

        [Test]
        public void EveryShot_RestartsCoolingDelay()
        {
            var state = NewState();
            state.AddShotHeat(20f);
            state.Tick(0.5f);
            state.AddShotHeat(20f);
            state.Tick(0.5f);

            Assert.AreEqual(40f, state.Heat, 0.0001f, "Second shot reset the delay; still no cooling.");
        }

        [Test]
        public void Overheat_BlocksShotsForLockout_ThenUnlocks_AndHeatCoolsMeanwhile()
        {
            var state = NewState();
            var overheatedEvents = 0;
            var unlockedEvents = 0;
            state.Overheated += () => overheatedEvents++;
            state.LockoutEnded += () => unlockedEvents++;

            state.AddShotHeat(100f);
            Assert.IsTrue(state.IsOverheated);
            Assert.IsFalse(state.CanFire);
            Assert.AreEqual(1, overheatedEvents);

            Assert.IsFalse(state.AddShotHeat(7f), "Shots are rejected while overheated.");
            Assert.AreEqual(100f, state.Heat, 0.0001f);

            state.Tick(2.1f);
            Assert.IsTrue(state.IsOverheated, "Lockout is 2.2s; 2.1s is not enough.");
            Assert.IsFalse(state.CanFire);
            Assert.AreEqual(100f - 35f * (2.1f - 0.6f), state.Heat, 0.001f, "Heat cools during lockout after the delay.");

            state.Tick(0.1f);
            Assert.IsFalse(state.IsOverheated);
            Assert.IsTrue(state.CanFire);
            Assert.AreEqual(1, unlockedEvents);
            Assert.IsTrue(state.AddShotHeat(7f));
        }

        [Test]
        public void HeatFraction_ExposesNormalizedHeatForHud()
        {
            var state = NewState();
            state.AddShotHeat(25f);
            Assert.AreEqual(0.25f, state.HeatFraction, 0.0001f);
        }
    }
}

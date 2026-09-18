using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    public class WeaponLoadoutTests
    {
        private const float MeleeWindUp = 0.08f;
        private const float MeleeRecovery = 0.12f;
        private const float MeleeAttackRate = 3.5f;
        private const float RangedFireRate = 4.0f;

        private GameObject _playerObject;
        private PlayerAiming _aiming;
        private RangedWeapon _ranged;
        private MeleeWeapon _melee;
        private WeaponLoadout _loadout;
        private FakePlayerInputReader _input;
        private RangedWeaponDefinition _rangedDefinition;
        private MeleeWeaponDefinition _meleeDefinition;
        private AmmoReserve _ammoReserve;
        private readonly List<GameObject> _spawnedObjects = new();

        [SetUp]
        public void SetUp()
        {
            _playerObject = new GameObject("TestPlayer");
            _playerObject.AddComponent<ProjectilePool>();
            _aiming = _playerObject.AddComponent<PlayerAiming>();
            _ranged = _playerObject.AddComponent<RangedWeapon>();
            _melee = _playerObject.AddComponent<MeleeWeapon>();
            _loadout = _playerObject.AddComponent<WeaponLoadout>();

            _input = new FakePlayerInputReader();
            _aiming.SetInputReader(_input);

            _rangedDefinition = ScriptableObject.CreateInstance<RangedWeaponDefinition>();
            SetRangedDefinition(_rangedDefinition, 12, 14, RangedFireRate, 12, 1.2f, 10f, 20f, AmmoType.Light);

            _meleeDefinition = ScriptableObject.CreateInstance<MeleeWeaponDefinition>();
            SetMeleeDefinition(_meleeDefinition, 14, 17, MeleeAttackRate, 1.2f, 80f, MeleeWindUp, MeleeRecovery, 0f, 0f);

            _ammoReserve = new AmmoReserve();
            _ammoReserve.Add(AmmoType.Light, 50);

            _ranged.SetInputReader(_input);
            _ranged.SetProjectilePool(_playerObject.GetComponent<ProjectilePool>());
            _ranged.SetAiming(_aiming);
            _ranged.SetAmmoReserve(_ammoReserve);
            _ranged.SetDamageRoller(new FixedDamageRoller { FixedValue = 13 });
            _ranged.SetDefinition(_rangedDefinition);

            _melee.SetInputReader(_input);
            _melee.SetAiming(_aiming);
            _melee.SetDamageRoller(new FixedDamageRoller { FixedValue = 15 });
            _melee.SetDefinition(_meleeDefinition);

            _loadout.SetInputReader(_input);
            _loadout.SetPrimary(_ranged);
            _loadout.SetSecondary(_melee);
            _loadout.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            if (_playerObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_playerObject);
            }

            if (_rangedDefinition != null)
            {
                UnityEngine.Object.DestroyImmediate(_rangedDefinition);
            }

            if (_meleeDefinition != null)
            {
                UnityEngine.Object.DestroyImmediate(_meleeDefinition);
            }

            foreach (var spawnedObject in _spawnedObjects)
            {
                if (spawnedObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(spawnedObject);
                }
            }

            _spawnedObjects.Clear();
        }

        private static void SetRangedDefinition(
            RangedWeaponDefinition definition, int damageMin, int damageMax, float fireRate,
            int magazineSize, float reloadTime, float range, float projectileSpeed, AmmoType ammoType)
        {
            SetField(definition, "_damageMin", damageMin);
            SetField(definition, "_damageMax", damageMax);
            SetField(definition, "_fireRate", fireRate);
            SetField(definition, "_magazineSize", magazineSize);
            SetField(definition, "_reloadTime", reloadTime);
            SetField(definition, "_range", range);
            SetField(definition, "_projectileSpeed", projectileSpeed);
            SetField(definition, "_ammoType", ammoType);
        }

        private static void SetMeleeDefinition(
            MeleeWeaponDefinition definition, int damageMin, int damageMax, float attackRate,
            float attackRange, float attackArcDegrees, float windUp, float recovery, float knockback, float staggerPower)
        {
            SetField(definition, "_damageMin", damageMin);
            SetField(definition, "_damageMax", damageMax);
            SetField(definition, "_attackRate", attackRate);
            SetField(definition, "_attackRange", attackRange);
            SetField(definition, "_attackArcDegrees", attackArcDegrees);
            SetField(definition, "_windUpSeconds", windUp);
            SetField(definition, "_recoverySeconds", recovery);
            SetField(definition, "_knockback", knockback);
            SetField(definition, "_staggerPower", staggerPower);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            // Knockback/StaggerPower live on the WeaponDefinition base since TASK 050: walk the hierarchy.
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null) continue;
                field.SetValue(target, value);
                return;
            }

            throw new System.MissingFieldException(target.GetType().Name, fieldName);
        }

        private void AimRight()
        {
            _input.IsAimFromPointer = false;
            _input.Aim = Vector2.right;
        }

        private TestDamageableTarget CreateTarget(Vector2 position)
        {
            var targetObject = new GameObject("TestTarget");
            targetObject.transform.position = position;
            var collider = targetObject.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = 0.2f;
            _spawnedObjects.Add(targetObject);
            return targetObject.AddComponent<TestDamageableTarget>();
        }

        // ---- Slot existence / initial state ----

        [Test]
        public void ExactlyTwoSlots_PrimaryAndSecondary_Exist()
        {
            var values = Enum.GetValues(typeof(WeaponSlot));
            Assert.AreEqual(2, values.Length);
            CollectionAssert.Contains(values, WeaponSlot.Primary);
            CollectionAssert.Contains(values, WeaponSlot.Secondary);
        }

        [Test]
        public void Primary_IsActiveByDefault_AndExactlyOneWeaponIsEquipped()
        {
            Assert.AreEqual(WeaponSlot.Primary, _loadout.ActiveSlot);
            Assert.IsTrue(_ranged.IsEquipped);
            Assert.IsFalse(_melee.IsEquipped, "Only the active slot's weapon should start equipped.");
        }

        [Test]
        public void ActiveWeapon_IsExposedCorrectly()
        {
            Assert.AreSame(_ranged, _loadout.ActiveWeapon);

            _loadout.SelectSlot(WeaponSlot.Secondary);

            Assert.AreSame(_melee, _loadout.ActiveWeapon);
        }

        // ---- Direct selection / swap ----

        [Test]
        public void Weapon1_SelectsPrimary()
        {
            _loadout.SelectSlot(WeaponSlot.Secondary);

            _input.RaiseWeapon1Selected();

            Assert.AreEqual(WeaponSlot.Primary, _loadout.ActiveSlot);
            Assert.IsTrue(_ranged.IsEquipped);
            Assert.IsFalse(_melee.IsEquipped);
        }

        [Test]
        public void Weapon2_SelectsSecondary()
        {
            _input.RaiseWeapon2Selected();

            Assert.AreEqual(WeaponSlot.Secondary, _loadout.ActiveSlot);
            Assert.IsTrue(_melee.IsEquipped);
            Assert.IsFalse(_ranged.IsEquipped);
        }

        [Test]
        public void WeaponSwap_TogglesPrimaryToSecondary()
        {
            _input.RaiseWeaponSwapped();

            Assert.AreEqual(WeaponSlot.Secondary, _loadout.ActiveSlot);
        }

        [Test]
        public void WeaponSwap_TogglesSecondaryToPrimary()
        {
            _input.RaiseWeaponSwapped();
            _input.RaiseWeaponSwapped();

            Assert.AreEqual(WeaponSlot.Primary, _loadout.ActiveSlot);
        }

        [Test]
        public void RepeatedToggles_RemainDeterministic()
        {
            for (var i = 1; i <= 6; i++)
            {
                _input.RaiseWeaponSwapped();
                var expected = i % 2 == 1 ? WeaponSlot.Secondary : WeaponSlot.Primary;
                Assert.AreEqual(expected, _loadout.ActiveSlot, $"Unexpected slot after {i} toggles.");
            }
        }

        [Test]
        public void SelectingAlreadyActiveSlot_IsIdempotentNoOp()
        {
            var eventFireCount = 0;
            _loadout.ActiveSlotChanged += _ => eventFireCount++;

            _input.RaiseWeapon1Selected(); // already Primary

            Assert.AreEqual(WeaponSlot.Primary, _loadout.ActiveSlot);
            Assert.AreEqual(0, eventFireCount, "Selecting the already-active slot must not raise a change notification.");
            Assert.IsTrue(_ranged.IsEquipped);
            Assert.IsFalse(_melee.IsEquipped);
        }

        [Test]
        public void ActiveSlotChanged_FiresExactlyOnce_PerActualSwitch()
        {
            var events = new List<WeaponSlot>();
            _loadout.ActiveSlotChanged += slot => events.Add(slot);

            _input.RaiseWeaponSwapped();
            _input.RaiseWeapon2Selected(); // already Secondary: no-op
            _input.RaiseWeapon1Selected();

            CollectionAssert.AreEqual(new[] { WeaponSlot.Secondary, WeaponSlot.Primary }, events);
        }

        // ---- Only active weapon responds to Fire ----

        [UnityTest]
        public IEnumerator WithP9RangerActive_FireDoesNotAlsoTriggerFieldKnife()
        {
            AimRight();
            yield return null;

            _input.FireHeld = true;
            yield return null;
            _input.FireHeld = false;

            Assert.IsNotNull(_ranged.LastSpawnedProjectile, "The active ranged weapon should have fired.");
            Assert.AreEqual(MeleeAttackState.Idle, _melee.State, "The inactive melee weapon must not have started an attack.");
        }

        [UnityTest]
        public IEnumerator WithFieldKnifeActive_FireDoesNotAlsoSpawnP9RangerProjectile()
        {
            _loadout.SelectSlot(WeaponSlot.Secondary);
            AimRight();
            yield return null;

            var magazineBefore = _ranged.MagazineAmmo;

            _input.FireHeld = true;
            yield return null;
            _input.FireHeld = false;

            Assert.AreNotEqual(MeleeAttackState.Idle, _melee.State, "The active melee weapon should have started attacking.");
            Assert.AreEqual(magazineBefore, _ranged.MagazineAmmo, "The inactive ranged weapon must not have fired or consumed ammo.");
            Assert.IsNull(_ranged.LastSpawnedProjectile);
        }

        [Test]
        public void InactiveRangedWeapon_CannotBeginReload()
        {
            Assert.IsTrue(_ranged.TryFire()); // mag 11/12, still Primary/active
            _loadout.SelectSlot(WeaponSlot.Secondary); // ranged now inactive

            var started = _ranged.TryStartReload();

            Assert.IsFalse(started, "An inactive ranged weapon must not be able to begin reload.");
            Assert.IsFalse(_ranged.IsReloading);
        }

        // ---- Reload cancellation on switch ----

        [Test]
        public void SwitchingAway_DuringReload_CancelsReload_TransfersNoAmmo_PreservesMagazineAndReserve()
        {
            Assert.IsTrue(_ranged.TryFire());
            var magazineAfterFire = _ranged.MagazineAmmo;
            Assert.IsTrue(_ranged.TryStartReload());
            Assert.IsTrue(_ranged.IsReloading);
            var reserveBeforeSwitch = _ammoReserve.Get(AmmoType.Light);

            _loadout.SelectSlot(WeaponSlot.Secondary);

            Assert.IsFalse(_ranged.IsReloading, "Switching away must cancel the active reload.");
            Assert.AreEqual(magazineAfterFire, _ranged.MagazineAmmo, "Magazine must be preserved, not filled by the cancelled reload.");
            Assert.AreEqual(reserveBeforeSwitch, _ammoReserve.Get(AmmoType.Light), "Cancelled reload must transfer no ammo from reserve.");
        }

        [UnityTest]
        public IEnumerator SwitchingBack_DoesNotAutoResumeCancelledReload()
        {
            Assert.IsTrue(_ranged.TryFire());
            var magazineAfterFire = _ranged.MagazineAmmo;
            Assert.IsTrue(_ranged.TryStartReload());

            _loadout.SelectSlot(WeaponSlot.Secondary);
            _loadout.SelectSlot(WeaponSlot.Primary);

            yield return new WaitForSeconds(_rangedDefinition.ReloadTime + 0.2f);

            Assert.IsFalse(_ranged.IsReloading, "Switching back must not resume the cancelled reload.");
            Assert.AreEqual(magazineAfterFire, _ranged.MagazineAmmo, "Magazine must remain unfilled until Reload is initiated again.");
        }

        [Test]
        public void SwitchingWhileNotReloading_ChangesNothingAboutMagazineOrReserve()
        {
            Assert.IsTrue(_ranged.TryFire());
            var magazine = _ranged.MagazineAmmo;
            var reserve = _ammoReserve.Get(AmmoType.Light);

            _loadout.SelectSlot(WeaponSlot.Secondary);
            _loadout.SelectSlot(WeaponSlot.Primary);

            Assert.AreEqual(magazine, _ranged.MagazineAmmo);
            Assert.AreEqual(reserve, _ammoReserve.Get(AmmoType.Light));
        }

        // ---- Melee wind-up cancellation on switch ----

        [UnityTest]
        public IEnumerator SwitchingAway_DuringWindUp_PreventsDelayedMeleeHit()
        {
            _loadout.SelectSlot(WeaponSlot.Secondary);
            AimRight();
            yield return null;
            var target = CreateTarget(new Vector2(0.6f, 0f));

            Assert.IsTrue(_melee.TryAttack());
            Assert.AreEqual(MeleeAttackState.WindUp, _melee.State);

            _loadout.SelectSlot(WeaponSlot.Primary); // switch away before the active hit point

            yield return new WaitForSeconds(MeleeWindUp + 0.1f);

            Assert.AreEqual(0, target.HitCount, "A weapon switched away mid-wind-up must not produce a delayed hit.");
            Assert.AreEqual(MeleeAttackState.Idle, _melee.State, "The unequipped weapon's attack should have been cancelled/reset.");
        }

        [Test]
        public void SwitchingBack_ToFieldKnife_LeavesCleanAttackReadyState()
        {
            _loadout.SelectSlot(WeaponSlot.Secondary);
            AimRight();
            Assert.IsTrue(_melee.TryAttack());

            _loadout.SelectSlot(WeaponSlot.Primary);
            _loadout.SelectSlot(WeaponSlot.Secondary);

            Assert.AreEqual(MeleeAttackState.Idle, _melee.State);
            Assert.IsTrue(_melee.TryAttack(), "Re-equipping should leave the weapon immediately attack-ready.");
        }

        // ---- State persistence ----

        [Test]
        public void P9RangerMagazine_PersistsAcrossSwitchAwayAndBack()
        {
            Assert.IsTrue(_ranged.TryFire());
            var magazineAfterFiring = _ranged.MagazineAmmo;

            _loadout.SelectSlot(WeaponSlot.Secondary);
            _loadout.SelectSlot(WeaponSlot.Primary);

            Assert.AreEqual(magazineAfterFiring, _ranged.MagazineAmmo,
                "Magazine must not reset to full capacity merely because the weapon was unequipped/re-equipped.");
        }

        [Test]
        public void SwitchingNeverCreatesAmmo()
        {
            var reserveBefore = _ammoReserve.Get(AmmoType.Light);

            for (var i = 0; i < 6; i++)
            {
                _input.RaiseWeaponSwapped();
            }

            Assert.AreEqual(reserveBefore, _ammoReserve.Get(AmmoType.Light), "Switching alone must never create reserve ammo.");
        }

        // ---- Independence from aim/movement ----

        [UnityTest]
        public IEnumerator SwitchingDoesNotChangeAimDirection()
        {
            const float angle = 65f;
            _input.IsAimFromPointer = false;
            _input.Aim = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            yield return null;

            var directionBefore = _aiming.AimDirection;

            _input.RaiseWeaponSwapped();
            yield return null;

            Assert.AreEqual(directionBefore.x, _aiming.AimDirection.x, 0.001f);
            Assert.AreEqual(directionBefore.y, _aiming.AimDirection.y, 0.001f);
        }

        [UnityTest]
        public IEnumerator SwitchingDoesNotChangeMovementState()
        {
            var movement = _playerObject.AddComponent<PlayerMovement>();
            var balanceConfig = ScriptableObject.CreateInstance<PlayerBalanceConfig>();
            movement.SetInputReader(_input);
            movement.SetBalanceConfig(balanceConfig);
            _input.Move = Vector2.right;

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            var positionBeforeSwitch = _playerObject.transform.position.x;

            _input.RaiseWeaponSwapped();

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            var positionAfterSwitch = _playerObject.transform.position.x;

            Assert.Greater(positionAfterSwitch, positionBeforeSwitch,
                "Movement must keep progressing normally across a weapon switch.");

            UnityEngine.Object.DestroyImmediate(balanceConfig);
        }

        // ---- No class restriction ----

        [Test]
        public void SharedEquipContract_CanRepresentEitherReferenceWeapon_InEitherSlot()
        {
            var reversePlayer = new GameObject("ReverseTestPlayer");
            _spawnedObjects.Add(reversePlayer);
            reversePlayer.AddComponent<ProjectilePool>();
            var reverseAiming = reversePlayer.AddComponent<PlayerAiming>();
            var reverseRanged = reversePlayer.AddComponent<RangedWeapon>();
            var reverseMelee = reversePlayer.AddComponent<MeleeWeapon>();
            var reverseLoadout = reversePlayer.AddComponent<WeaponLoadout>();

            var reverseInput = new FakePlayerInputReader();
            reverseAiming.SetInputReader(reverseInput);
            reverseRanged.SetInputReader(reverseInput);
            reverseRanged.SetProjectilePool(reversePlayer.GetComponent<ProjectilePool>());
            reverseRanged.SetAiming(reverseAiming);
            reverseRanged.SetAmmoReserve(new AmmoReserve());
            reverseRanged.SetDefinition(_rangedDefinition);
            reverseMelee.SetInputReader(reverseInput);
            reverseMelee.SetAiming(reverseAiming);
            reverseMelee.SetDefinition(_meleeDefinition);

            // Field Knife as Primary, P9 Ranger as Secondary: the reverse of the canonical test setup.
            reverseLoadout.SetInputReader(reverseInput);
            reverseLoadout.SetPrimary(reverseMelee);
            reverseLoadout.SetSecondary(reverseRanged);
            reverseLoadout.Initialize();

            Assert.AreEqual(WeaponSlot.Primary, reverseLoadout.ActiveSlot);
            Assert.AreSame(reverseMelee, reverseLoadout.ActiveWeapon);
            Assert.IsTrue(reverseMelee.IsEquipped);
            Assert.IsFalse(reverseRanged.IsEquipped);

            reverseInput.RaiseWeapon2Selected();
            Assert.AreEqual(WeaponSlot.Secondary, reverseLoadout.ActiveSlot);
            Assert.AreSame(reverseRanged, reverseLoadout.ActiveWeapon);
            Assert.IsTrue(reverseRanged.IsEquipped);
            Assert.IsFalse(reverseMelee.IsEquipped);
        }

        [Test]
        public void WeaponLoadout_SlotSetters_AcceptTheSharedInterface_NotConcreteWeaponTypes()
        {
            var setPrimaryParameterType = typeof(WeaponLoadout).GetMethod("SetPrimary").GetParameters()[0].ParameterType;
            var setSecondaryParameterType = typeof(WeaponLoadout).GetMethod("SetSecondary").GetParameters()[0].ParameterType;

            Assert.AreEqual(typeof(IEquippableWeapon), setPrimaryParameterType);
            Assert.AreEqual(typeof(IEquippableWeapon), setSecondaryParameterType);
        }
    }
}

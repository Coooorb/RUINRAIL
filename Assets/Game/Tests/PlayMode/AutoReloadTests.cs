using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Auto-reload on an emptied magazine (§5): the last round leaves, reserve remains → the ordinary reload starts on
    /// its own; no reserve → nothing; never twice; a weapon swap cancels it like a manual one; every magazine-fed
    /// class in the catalog does it; blaster/bow/melee have no magazine and are untouched; ammo counts stay exact.
    /// </summary>
    public sealed class AutoReloadTests
    {
        private readonly List<Object> _created = new();
        private GameContentCatalog _catalog;
        private ItemDefinitionRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            DamageAuthority.LocalIsAuthoritative = true;
            _catalog = GameContentCatalog.Load();
            _registry = _catalog.BuildRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var p in Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None)) if (p != null) Object.DestroyImmediate(p.gameObject);
            _created.Clear();
        }

        private (RangedWeapon weapon, AmmoReserve reserve, WeaponLoadout loadout, FakePlayerInputReader input) Mount(string id, int reserveRounds)
        {
            Assert.IsTrue(_registry.TryGet(id, out var definition), id);
            var ranged = definition as RangedWeaponDefinition;
            Assert.IsNotNull(ranged, id + " is magazine-fed");
            var go = new GameObject("Shooter_" + id);
            _created.Add(go);
            var input = new FakePlayerInputReader();
            var pool = go.AddComponent<ProjectilePool>();
            var aiming = go.AddComponent<PlayerAiming>();
            aiming.SetInputReader(input);
            var weapon = go.AddComponent<RangedWeapon>();
            weapon.SetInputReader(input);
            var reserve = new AmmoReserve();
            reserve.Add(ranged.AmmoType, reserveRounds);
            weapon.SetProjectilePool(pool);
            weapon.SetAiming(aiming);
            weapon.SetAmmoReserve(reserve);
            weapon.SetDamageRoller(new FixedDamageRoller());
            weapon.SetDefinition(ranged);
            var loadout = go.AddComponent<WeaponLoadout>();
            loadout.SetInputReader(input);
            loadout.SetPrimary(weapon);
            loadout.Initialize();
            return (weapon, reserve, loadout, input);
        }

        private static IEnumerable<string> MagazineWeaponIds(ItemDefinitionRegistry registry) =>
            registry.Definitions.OfType<RangedWeaponDefinition>().Where(d => d.MagazineSize > 0).Select(d => d.Id).OrderBy(id => id);

        [Test]
        public void P9Ranger_FinalShot_StartsReload_ExactlyOnce()
        {
            var (weapon, reserve, _, _) = Mount("weapon_p9_ranger", 60);
            var reserveBefore = reserve.Get(weapon.Definition.AmmoType);
            weapon.ApplyAuthoritativeState(1, false);

            Assert.IsTrue(weapon.TryFire());
            Assert.AreEqual(0, weapon.MagazineAmmo, "the final round left the magazine");
            Assert.IsTrue(weapon.IsReloading, "the reload started on its own");
            Assert.AreEqual(1, weapon.AutoReloads);
            Assert.AreEqual(reserveBefore, reserve.Get(weapon.Definition.AmmoType), "reserve is consumed at reload completion, not at its start");

            Assert.IsFalse(weapon.TryStartReload(), "a manual reload during the auto reload is a no-op, not a second reload");
            Assert.IsFalse(weapon.TryFire(), "no firing while reloading");
            Assert.AreEqual(1, weapon.AutoReloads);
        }

        [UnityTest]
        public IEnumerator P9Ranger_AutoReload_TakesTheConfiguredTime_AndFillsFromReserveExactly()
        {
            var (weapon, reserve, _, _) = Mount("weapon_p9_ranger", 60);
            var definition = weapon.Definition;
            var reserveBefore = reserve.Get(definition.AmmoType);
            weapon.ApplyAuthoritativeState(1, false);
            Assert.IsTrue(weapon.TryFire());
            Assert.IsTrue(weapon.IsReloading);

            var start = Time.time;
            while (weapon.IsReloading && Time.time - start < definition.ReloadTime + 2f) yield return null;
            Assert.IsFalse(weapon.IsReloading, "the auto reload completed");
            Assert.GreaterOrEqual(Time.time - start, definition.ReloadTime - 0.1f, "no faster than a manual reload");
            Assert.AreEqual(definition.MagazineSize, weapon.MagazineAmmo);
            Assert.AreEqual(reserveBefore - definition.MagazineSize * definition.AmmoCostPerShot, reserve.Get(definition.AmmoType), "ammo integrity: reserve dropped by exactly what went into the magazine");
        }

        [Test]
        public void NoReserve_NoAutoReload_AndNoCrash()
        {
            var (weapon, reserve, _, _) = Mount("weapon_p9_ranger", 0);
            weapon.ApplyAuthoritativeState(1, false);
            Assert.IsTrue(weapon.TryFire());
            Assert.AreEqual(0, weapon.MagazineAmmo);
            Assert.IsFalse(weapon.IsReloading, "nothing to reload from");
            Assert.AreEqual(0, weapon.AutoReloads);
            Assert.AreEqual(0, reserve.Get(weapon.Definition.AmmoType));
            Assert.IsFalse(weapon.TryFire(), "still empty, still cannot fire");
        }

        [Test]
        public void WeaponSwap_CancelsTheAutoReload_LikeAManualOne()
        {
            var (weapon, _, loadout, _) = Mount("weapon_p9_ranger", 60);
            var secondary = weapon.gameObject.AddComponent<MeleeWeapon>();
            loadout.SetSecondary(secondary);
            weapon.ApplyAuthoritativeState(1, false);
            Assert.IsTrue(weapon.TryFire());
            Assert.IsTrue(weapon.IsReloading);

            loadout.SelectSlot(WeaponSlot.Secondary);
            Assert.IsFalse(weapon.IsReloading, "unequipping cancels the reload in progress");
            Assert.AreEqual(0, weapon.MagazineAmmo, "no ammo was transferred by the cancelled reload");
            Assert.AreEqual(1, weapon.AutoReloads, "the cancelled reload is not retried behind the player's back");
        }

        [Test]
        public void EveryMagazineFedCatalogWeapon_AutoReloadsOnItsFinalRound()
        {
            var ids = MagazineWeaponIds(_registry).ToList();
            Assert.GreaterOrEqual(ids.Count, 8, "the catalog has the eight+ magazine-fed classes");
            var classes = new HashSet<WeaponClass>();
            foreach (var id in ids)
            {
                var (weapon, _, _, _) = Mount(id, 500);
                classes.Add(weapon.Definition.WeaponClass);
                weapon.ApplyAuthoritativeState(1, false);
                Assert.IsTrue(weapon.TryFire(), id);
                Assert.AreEqual(0, weapon.MagazineAmmo, id);
                Assert.IsTrue(weapon.IsReloading, id + " auto-reloads on its final round");
                Assert.AreEqual(1, weapon.AutoReloads, id);
            }

            foreach (var cls in new[] { WeaponClass.Pistol, WeaponClass.Smg, WeaponClass.AssaultRifle, WeaponClass.BattleRifle, WeaponClass.Shotgun, WeaponClass.Sniper, WeaponClass.RocketLauncher })
                Assert.IsTrue(classes.Contains(cls), cls + " covered by a catalog weapon");
        }

        [Test]
        public void BlasterBowAndMelee_HaveNoMagazine_AndAreNotOnTheAutoReloadPath()
        {
            foreach (var definition in _registry.Definitions.OfType<WeaponDefinition>())
            {
                var cls = definition.WeaponClass;
                var isMagazineClass = definition is RangedWeaponDefinition;
                var isMagazineless = cls == WeaponClass.Blaster || cls == WeaponClass.Bow || cls == WeaponClass.Knife || cls == WeaponClass.Spear;
                Assert.AreNotEqual(isMagazineClass, isMagazineless, definition.Id + ": magazine-fed exactly when not blaster/bow/melee");
            }

            // The blaster and bow runtimes expose no magazine at all: heat / charge instead, unchanged.
            Assert.IsNull(typeof(BlasterWeapon).GetProperty("MagazineAmmo"));
            Assert.IsNull(typeof(BowWeapon).GetProperty("MagazineAmmo"));
            Assert.IsNull(typeof(MeleeWeapon).GetProperty("MagazineAmmo"));
            Assert.IsNull(typeof(BlasterWeapon).GetProperty("AutoReloads"));
            Assert.IsNull(typeof(BowWeapon).GetProperty("AutoReloads"));
        }

        [UnityTest]
        public IEnumerator PartialMagazine_NeverAutoReloads()
        {
            var (weapon, _, _, _) = Mount("weapon_p9_ranger", 60);
            Assert.Greater(weapon.Definition.MagazineSize, 2);
            Assert.IsTrue(weapon.TryFire());
            Assert.IsFalse(weapon.IsReloading, "one round fired, rounds remain: the player decides when to reload");
            yield return new WaitForSeconds(1f / weapon.Definition.FireRate + 0.05f);
            Assert.IsTrue(weapon.TryFire());
            Assert.IsFalse(weapon.IsReloading);
            Assert.AreEqual(0, weapon.AutoReloads);
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Items;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 057 — every non-Legendary V1 weapon exists exactly as items/33 states: two per class, 22 in total.</summary>
    public class WeaponCatalogTests
    {
        private List<WeaponDefinition> _weapons;

        [SetUp]
        public void SetUp()
        {
            _weapons = AssetDatabase.FindAssets("t:WeaponDefinition", new[] { "Assets/Game/ScriptableObjects/Items" })
                .Select(g => AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(d => d != null)
                .ToList();
        }

        private IEnumerable<WeaponDefinition> Normal => _weapons.Where(w => string.IsNullOrEmpty(w.LegendaryMechanicId));

        private T Get<T>(string id) where T : WeaponDefinition
        {
            var w = _weapons.OfType<T>().FirstOrDefault(d => d.Id == id);
            Assert.IsNotNull(w, $"Missing {typeof(T).Name} '{id}'.");
            return w;
        }


        /// <summary>
        /// The pool a weapon class rolls from. Shotguns and Rocket Launchers carry authored Knockback/Stagger Power
        /// (combat/42, items/24) so they roll the impact pool; every other firearm has no impact base, and an impact
        /// affix on one would be a guaranteed no-op.
        /// </summary>
        private static string ExpectedPoolFor(WeaponClass cls) => cls switch
        {
            WeaponClass.Shotgun or WeaponClass.RocketLauncher => "pool_ranged_impact",
            WeaponClass.Spear => "pool_melee_heavy",
            WeaponClass.Knife => "pool_melee",
            WeaponClass.Bow => "pool_bow",
            WeaponClass.Blaster => "pool_blaster",
            _ => "pool_ranged"
        };

        private void Firearm(string id, string name, WeaponClass cls, int dMin, int dMax, float rate, int mag, float reload, float range, float speed, AmmoType ammo, int cost = 1, int pellets = 1)
        {
            var w = Get<RangedWeaponDefinition>(id);
            Assert.AreEqual(name, w.DisplayName, id);
            Assert.AreEqual(cls, w.WeaponClass, id);
            Assert.AreEqual(dMin, w.DamageMin, id);
            Assert.AreEqual(dMax, w.DamageMax, id);
            Assert.AreEqual(rate, w.FireRate, 0.001f, id);
            Assert.AreEqual(mag, w.MagazineSize, id);
            Assert.AreEqual(reload, w.ReloadTime, 0.001f, id);
            Assert.AreEqual(range, w.Range, 0.001f, id);
            Assert.AreEqual(speed, w.ProjectileSpeed, 0.001f, id);
            Assert.AreEqual(ammo, w.AmmoType, id);
            Assert.AreEqual(cost, w.AmmoCostPerShot, id);
            Assert.AreEqual(pellets, w.ProjectilesPerShot, id);
            Assert.IsNotNull(w.AffixPool, $"{id} must roll from a pool.");
            Assert.AreEqual(ExpectedPoolFor(cls), w.AffixPool.Id, id);
            Assert.AreEqual(cls == WeaponClass.RocketLauncher, w.IsExplosive, id);
        }

        // ---- Acceptance 1 + 3: 22 normal weapons, 2 per class, unique stable ids, approved ammo only ----

        [Test]
        public void ExactlyTwoNormalWeaponsPerClass_22Total_WithUniqueIds()
        {
            var normal = Normal.ToList();
            Assert.AreEqual(22, normal.Count, string.Join(", ", normal.Select(w => w.Id)));
            foreach (WeaponClass cls in System.Enum.GetValues(typeof(WeaponClass)))
            {
                Assert.AreEqual(2, normal.Count(w => w.WeaponClass == cls), $"{cls} needs exactly two normal weapons.");
            }

            CollectionAssert.AllItemsAreUnique(_weapons.Select(w => w.Id).ToList(), "Stable ids are unique across the catalog.");
            Assert.IsTrue(_weapons.All(w => w.Id.StartsWith("weapon_")), "Stable id convention.");
            Assert.IsTrue(_weapons.All(w => w.Category == ItemCategory.Weapon));
            var ammoTypes = _weapons.OfType<RangedWeaponDefinition>().Select(w => w.AmmoType).Distinct().ToList();
            CollectionAssert.IsSubsetOf(ammoTypes, new[] { AmmoType.Light, AmmoType.Medium, AmmoType.Heavy, AmmoType.Shells }, "No fifth ammo category.");
            Assert.IsTrue(_weapons.OfType<RangedWeaponDefinition>().Where(w => w.WeaponClass == WeaponClass.RocketLauncher).All(w => w.AmmoType == AmmoType.Heavy && w.AmmoCostPerShot == 4), "26: rockets are 4 Heavy per shot.");
            Assert.IsTrue(_weapons.OfType<RangedWeaponDefinition>().Where(w => w.WeaponClass == WeaponClass.Shotgun).All(w => w.AmmoType == AmmoType.Shells), "33: all shotguns use Shells.");
        }

        // ---- Acceptance 2: every numeric value matches items/33 ----

        [Test]
        public void Firearms_MatchTheCatalog()
        {
            Firearm("weapon_p9_ranger", "P9 Ranger", WeaponClass.Pistol, 12, 14, 4.0f, 12, 1.2f, 10f, 20f, AmmoType.Light);
            Firearm("weapon_kestrel_12", "Kestrel-12", WeaponClass.Pistol, 10, 12, 5.2f, 15, 1.4f, 9f, 20f, AmmoType.Light);
            Firearm("weapon_rattler_9", "Rattler-9", WeaponClass.Smg, 6, 8, 10.0f, 32, 1.6f, 8f, 18f, AmmoType.Light);
            Firearm("weapon_wasp_45", "Wasp-45", WeaponClass.Smg, 8, 10, 8.0f, 26, 1.7f, 9f, 18f, AmmoType.Light);
            Firearm("weapon_ar_17", "AR-17", WeaponClass.AssaultRifle, 8, 10, 7.5f, 30, 1.8f, 12f, 22f, AmmoType.Medium);
            Firearm("weapon_marauder_a2", "Marauder A2", WeaponClass.AssaultRifle, 10, 12, 6.5f, 28, 1.9f, 13f, 22f, AmmoType.Medium);
            Firearm("weapon_sentinel_br", "Sentinel BR", WeaponClass.BattleRifle, 18, 22, 3.0f, 12, 2.0f, 15f, 26f, AmmoType.Medium);
            Firearm("weapon_hound_br", "Hound BR", WeaponClass.BattleRifle, 15, 18, 4.0f, 16, 2.1f, 14f, 26f, AmmoType.Medium);
            Firearm("weapon_longshot_s1", "Longshot S1", WeaponClass.Sniper, 45, 52, 1.0f, 5, 2.4f, 20f, 34f, AmmoType.Heavy);
            Firearm("weapon_needle_m7", "Needle M7", WeaponClass.Sniper, 34, 40, 1.5f, 7, 2.2f, 18f, 34f, AmmoType.Heavy);
            Firearm("weapon_pipe_launcher", "Pipe Launcher", WeaponClass.RocketLauncher, 65, 80, 0.5f, 1, 2.2f, 14f, 10f, AmmoType.Heavy, cost: 4);
            Firearm("weapon_twin_tube", "Twin-Tube", WeaponClass.RocketLauncher, 50, 60, 0.65f, 2, 2.8f, 13f, 10f, AmmoType.Heavy, cost: 4);
            Firearm("weapon_breacher_12", "Breacher-12", WeaponClass.Shotgun, 5, 7, 1.4f, 6, 2.2f, 6f, 16f, AmmoType.Shells, pellets: 6);
            Firearm("weapon_scatter_8", "Scatter-8", WeaponClass.Shotgun, 4, 5, 1.7f, 8, 2.4f, 5f, 16f, AmmoType.Shells, pellets: 8);
        }

        [Test]
        public void BowsBlastersAndMelee_MatchTheCatalog()
        {
            var recurve = Get<BowWeaponDefinition>("weapon_recurve_bow");
            Assert.AreEqual((10, 12, 25, 30, 0.65f), (recurve.QuickDamageMin, recurve.QuickDamageMax, recurve.FullDrawDamageMin, recurve.FullDrawDamageMax, recurve.FullChargeSeconds));
            var compound = Get<BowWeaponDefinition>("weapon_compound_bow");
            Assert.AreEqual("Compound Bow", compound.DisplayName);
            Assert.AreEqual((12, 15, 32, 38, 1.0f), (compound.QuickDamageMin, compound.QuickDamageMax, compound.FullDrawDamageMin, compound.FullDrawDamageMax, compound.FullChargeSeconds));
            foreach (var bow in new[] { recurve, compound })
            {
                Assert.AreEqual(WeaponClass.Bow, bow.WeaponClass);
                Assert.AreEqual("pool_bow", bow.AffixPool.Id, $"{bow.Id} rolls from the bow pool.");
            }

            var pulse = Get<BlasterWeaponDefinition>("weapon_pulse_carbine_b1");
            Assert.AreEqual((7, 9, 8f, 7f, 35f), (pulse.DamageMin, pulse.DamageMax, pulse.FireRate, pulse.HeatPerShot, pulse.CoolingRatePerSecond));
            var arc = Get<BlasterWeaponDefinition>("weapon_arc_blaster_b4");
            Assert.AreEqual("Arc Blaster B4", arc.DisplayName);
            Assert.AreEqual((10, 12, 6f, 10f, 35f), (arc.DamageMin, arc.DamageMax, arc.FireRate, arc.HeatPerShot, arc.CoolingRatePerSecond));
            foreach (var blaster in new[] { pulse, arc })
            {
                Assert.AreEqual(WeaponClass.Blaster, blaster.WeaponClass);
                Assert.AreEqual(100f, blaster.MaxHeat, 0.001f, "33: Max Heat 100.");
                Assert.AreEqual(0.5f, blaster.CoolingDelaySeconds, 0.001f, "33: Cooling Delay 0.5 s (shortened by the D1 ammo / blaster fine-tuning pass).");
                Assert.AreEqual(1.9f, blaster.OverheatLockoutSeconds, 0.001f, "33: Overheat Lockout 1.9 s (shortened by the D1 ammo / blaster fine-tuning pass).");
                Assert.AreEqual(22f, blaster.ProjectileSpeed, 0.001f, "33: Blaster projectile speed 22.");
                Assert.AreEqual("pool_blaster", blaster.AffixPool.Id);
            }

            void Melee(string id, string name, WeaponClass cls, int dMin, int dMax, float rate, float range, float arc)
            {
                var m = Get<MeleeWeaponDefinition>(id);
                Assert.AreEqual(name, m.DisplayName);
                Assert.AreEqual(cls, m.WeaponClass);
                Assert.AreEqual((dMin, dMax), (m.DamageMin, m.DamageMax), id);
                Assert.AreEqual(rate, m.AttackRate, 0.001f, id);
                Assert.AreEqual(range, m.AttackRange, 0.001f, id);
                Assert.AreEqual(arc, m.AttackArcDegrees, 0.001f, id);
                Assert.AreEqual(ExpectedPoolFor(cls), m.AffixPool.Id, id);
                Assert.LessOrEqual(m.WindUpSeconds + m.RecoverySeconds, 1f / m.AttackRate + 0.0001f, $"{id}: swing phases fit the cadence.");
            }

            Melee("weapon_field_knife", "Field Knife", WeaponClass.Knife, 14, 17, 3.5f, 1.2f, 80f);
            Melee("weapon_ripper_knife", "Ripper Knife", WeaponClass.Knife, 10, 13, 5.0f, 1.0f, 70f);
            Melee("weapon_scrap_spear", "Scrap Spear", WeaponClass.Spear, 24, 28, 1.8f, 2.6f, 20f);
            Melee("weapon_guard_lance", "Guard Lance", WeaponClass.Spear, 20, 24, 2.2f, 3.0f, 20f);
        }

        // ---- Requirement 4: loot/economy/rarity participation; either slot ----

        [Test]
        public void EveryNormalWeapon_HasAnAffixPoolAClassPrice_AndFitsEitherWeaponSlot()
        {
            var economy = AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset");
            var prices = new PriceService(economy);
            foreach (var weapon in Normal)
            {
                Assert.IsNotNull(weapon.AffixPool, $"{weapon.Id} has no affix pool.");
                Assert.GreaterOrEqual(weapon.AffixPool.Affixes.Count, RarityRules.RandomAffixCount(Rarity.Epic), $"{weapon.Id}: pool must support Epic (3 affixes).");
                Assert.IsTrue(economy.TryGetWeaponClassPrice(weapon.WeaponClass, out var basePrice) && basePrice > 0, $"{weapon.Id}: class price.");
                Assert.Greater(prices.BuyValue(basePrice, Rarity.Common), 0);
                Assert.IsTrue(PlayerInventory.IsSlotCompatible(weapon.Category, EquippedSlot.PrimaryWeapon), $"{weapon.Id} in Primary.");
                Assert.IsTrue(PlayerInventory.IsSlotCompatible(weapon.Category, EquippedSlot.SecondaryWeapon), $"{weapon.Id} in Secondary.");
            }

            // Class pools exclude nonsense combinations (22): no magazine/reload on bows, blasters or melee; no projectile stats on melee.
            // They also exclude affixes that could not move a number on the weapon that rolled them: a knife authors 0 knockback,
            // so a Knockback affix on it would be a purely decorative tooltip line. Knockback lives on the heavy-melee pool instead.
            var meleePool = AssetDatabase.LoadAssetAtPath<AffixPool>("Assets/Game/ScriptableObjects/Affixes/AffixPool_Melee.asset");
            var meleeHeavyPool = AssetDatabase.LoadAssetAtPath<AffixPool>("Assets/Game/ScriptableObjects/Affixes/AffixPool_MeleeHeavy.asset");
            var bowPool = AssetDatabase.LoadAssetAtPath<AffixPool>("Assets/Game/ScriptableObjects/Affixes/AffixPool_Bow.asset");
            var blasterPool = AssetDatabase.LoadAssetAtPath<AffixPool>("Assets/Game/ScriptableObjects/Affixes/AffixPool_Blaster.asset");
            CollectionAssert.AreEquivalent(new[] { AffixStat.Damage, AffixStat.MeleeAttackSpeed, AffixStat.StaggerPower }, meleePool.Affixes.Select(a => a.Stat));
            CollectionAssert.AreEquivalent(new[] { AffixStat.Damage, AffixStat.MeleeAttackSpeed, AffixStat.StaggerPower, AffixStat.Knockback }, meleeHeavyPool.Affixes.Select(a => a.Stat));
            CollectionAssert.AreEquivalent(new[] { AffixStat.Damage, AffixStat.ProjectileSpeed, AffixStat.Range, AffixStat.BowChargeSpeed }, bowPool.Affixes.Select(a => a.Stat));
            CollectionAssert.AreEquivalent(new[] { AffixStat.Damage, AffixStat.FireRate, AffixStat.ProjectileSpeed, AffixStat.Range }, blasterPool.Affixes.Select(a => a.Stat));
        }

        // ---- Acceptance 4: data-only variants, no per-weapon scripts ----

        [Test]
        public void NoPerWeaponAttackImplementations()
        {
            var weaponBehaviours = typeof(RuinRail.Gameplay.Combat.Weapons.RangedWeapon).Assembly.GetTypes()
                .Where(t => typeof(RuinRail.Gameplay.Combat.Weapons.IEquippableWeapon).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .Select(t => t.Name)
                .ToList();
            CollectionAssert.AreEquivalent(new[] { "RangedWeapon", "BlasterWeapon", "BowWeapon", "MeleeWeapon" }, weaponBehaviours, "Four shared behaviours cover all 22 definitions.");
            var definitionTypes = _weapons.Select(w => w.GetType().Name).Distinct().OrderBy(n => n).ToList();
            CollectionAssert.AreEquivalent(new[] { "RangedWeaponDefinition", "BowWeaponDefinition", "BlasterWeaponDefinition", "MeleeWeaponDefinition" }, definitionTypes);
        }
    }
}

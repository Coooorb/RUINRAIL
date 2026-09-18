using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.EditorTools.Items;
using RuinRail.Gameplay.Combat.Weapons.Specials;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Validation;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 062 — the production catalog audit passes on the project and fails on corrupted fixtures.</summary>
    public class WeaponCatalogAuditTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static void Set(object target, string field, object value)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var f = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
                if (f == null) continue;
                f.SetValue(target, value);
                return;
            }

            throw new System.MissingFieldException(target.GetType().Name, field);
        }

        private List<WeaponDefinition> Clones()
        {
            return WeaponCatalogValidationTools.LoadAllWeaponDefinitions().Select(d =>
            {
                var clone = Object.Instantiate(d);
                _created.Add(clone);
                return (WeaponDefinition)clone;
            }).ToList();
        }

        private static EconomyConfig Economy() => AssetDatabase.LoadAssetAtPath<EconomyConfig>(WeaponCatalogValidationTools.EconomyConfigPath);

        // ---- Acceptance 1-3: the project passes 33/33 with 2+1 per class ----

        [Test]
        public void ProjectCatalog_Passes_33of33_With2Plus1PerClass()
        {
            var report = WeaponCatalogValidationTools.ValidateProject();
            Assert.IsTrue(report.IsValid, string.Join("\n", report.Problems));
            Assert.AreEqual(33, report.DefinitionCount);
            Assert.AreEqual(11, report.PerClass.Count);
            Assert.IsTrue(report.PerClass.Values.All(v => v.normal == 2 && v.legendary == 1));
            Assert.AreEqual(33, WeaponCatalogValidator.AllCatalogIds.Distinct().Count(), "The embedded catalog itself lists 33 unique ids.");
            Assert.AreEqual(11, WeaponCatalogValidator.ClassBasePrices.Count);
        }

        [Test]
        public void EveryLegendary_HasExactlyOneSpecial_AndThreeAffixCapability()
        {
            var registry = WeaponCatalogValidationTools.LoadSpecialRegistry();
            var legendaries = WeaponCatalogValidationTools.LoadAllWeaponDefinitions().Where(w => !string.IsNullOrEmpty(w.LegendaryMechanicId)).ToList();
            Assert.AreEqual(11, legendaries.Count);
            foreach (var w in legendaries)
            {
                Assert.IsTrue(registry.TryGet(w.LegendaryMechanicId, out _), w.Id);
                Assert.GreaterOrEqual(w.AffixPool.Affixes.Count, RarityRules.RandomAffixCount(Rarity.Legendary), w.Id);
            }

            Assert.AreEqual(11, legendaries.Select(w => w.LegendaryMechanicId).Distinct().Count());
        }

        // ---- Acceptance 4: corrupted fixtures fail with the exact reason ----

        [Test]
        public void CorruptedFixtures_FailForTheRightReason()
        {
            var registry = WeaponCatalogValidationTools.LoadSpecialRegistry();
            var economy = Economy();

            // Rebalanced value.
            var rebalanced = Clones();
            Set(rebalanced.First(w => w.Id == "weapon_ar_17"), "_damageMax", 11);
            var r1 = WeaponCatalogValidator.Validate(rebalanced, economy, registry);
            Assert.IsFalse(r1.IsValid);
            Assert.IsTrue(r1.Problems.Any(p => p.Contains("weapon_ar_17") && p.Contains("DamageMax") && p.Contains("11") && p.Contains("10")), string.Join("\n", r1.Problems));

            // Missing definition and wrong count.
            var missing = Clones();
            missing.RemoveAll(w => w.Id == "weapon_needle_m7");
            var r2 = WeaponCatalogValidator.Validate(missing, economy, registry);
            Assert.IsTrue(r2.Problems.Any(p => p.Contains("weapon_needle_m7") && p.Contains("missing")));
            Assert.IsTrue(r2.Problems.Any(p => p.Contains("Expected 33") && p.Contains("32")));
            Assert.IsTrue(r2.Problems.Any(p => p.Contains("Sniper") && p.Contains("expected 2 normal")));

            // Duplicate stable id.
            var duplicate = Clones();
            Set(duplicate.First(w => w.Id == "weapon_wasp_45"), "_id", "weapon_rattler_9");
            var r3 = WeaponCatalogValidator.Validate(duplicate, economy, registry);
            Assert.IsTrue(r3.Problems.Any(p => p.Contains("Duplicate stable id 'weapon_rattler_9'")));

            // Unsupported ammo / rocket cost / class distribution / special.
            var resources = Clones();
            Set(resources.First(w => w.Id == "weapon_pipe_launcher"), "_ammoCostPerShot", 1);
            Set(resources.First(w => w.Id == "weapon_breacher_12"), "_ammoType", AmmoType.Medium);
            Set(resources.First(w => w.Id == "weapon_quickfang"), "_legendaryMechanicId", "not_a_special");
            Set(resources.First(w => w.Id == "weapon_kestrel_12"), "_weaponClass", WeaponClass.Smg);
            var r4 = WeaponCatalogValidator.Validate(resources, economy, registry);
            Assert.IsTrue(r4.Problems.Any(p => p.Contains("weapon_pipe_launcher") && p.Contains("4 Heavy")));
            Assert.IsTrue(r4.Problems.Any(p => p.Contains("weapon_breacher_12") && p.Contains("Shells")));
            Assert.IsTrue(r4.Problems.Any(p => p.Contains("weapon_quickfang") && p.Contains("not_a_special")));
            Assert.IsTrue(r4.Problems.Any(p => p.Contains("Pistol") && p.Contains("found 1")));
            Assert.IsTrue(r4.Problems.Any(p => p.Contains("Smg") && p.Contains("found 3")));

            // A Legendary that lost its special, and a normal weapon claiming one (distribution + fixed-special rule).
            var specials = Clones();
            Set(specials.First(w => w.Id == "weapon_farline"), "_legendaryMechanicId", "");
            Set(specials.First(w => w.Id == "weapon_longshot_s1"), "_legendaryMechanicId", "rail_shot");
            var r5 = WeaponCatalogValidator.Validate(specials, economy, registry);
            Assert.IsTrue(r5.Problems.Any(p => p.Contains("weapon_farline") && p.Contains("LegendaryMechanicId")));
            Assert.IsTrue(r5.Problems.Any(p => p.Contains("weapon_longshot_s1") && p.Contains("LegendaryMechanicId")));

            // Wrong class price and an unknown definition.
            var priced = ScriptableObject.CreateInstance<EconomyConfig>();
            _created.Add(priced);
            var prices = (EconomyConfig.WeaponClassPrice[])typeof(EconomyConfig).GetField("_weaponClassPrices", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(priced);
            for (var i = 0; i < prices.Length; i++) if (prices[i].Class == WeaponClass.Sniper) prices[i].BasePrice = 999;
            var extra = Clones();
            var bogus = ScriptableObject.CreateInstance<RangedWeaponDefinition>();
            _created.Add(bogus);
            Set(bogus, "_id", "weapon_not_in_catalog");
            Set(bogus, "_category", ItemCategory.Weapon);
            extra.Add(bogus);
            var r6 = WeaponCatalogValidator.Validate(extra, priced, registry);
            Assert.IsTrue(r6.Problems.Any(p => p.Contains("Sniper") && p.Contains("999") && p.Contains("600")), string.Join("\n", r6.Problems));
            Assert.IsTrue(r6.Problems.Any(p => p.Contains("weapon_not_in_catalog") && p.Contains("not part of the approved")));
        }
    }
}

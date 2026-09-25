using System.Linq;
using NUnit.Framework;
using RuinRail.Core.Rng;
using RuinRail.Gameplay.Combat.Weapons.Specials;
using RuinRail.Gameplay.Items;
using UnityEditor;

namespace RuinRail.Tests
{
    /// <summary>TASK 059 — Snapfire, Lead Bloom, Overrun, Piercing Line data and their four Legendary weapons.</summary>
    public class LegendarySpecialDefinitionTests
    {
        private static LegendarySpecialRegistry Registry()
        {
            var specials = AssetDatabase.FindAssets("t:LegendarySpecialDefinition").Select(g => AssetDatabase.LoadAssetAtPath<LegendarySpecialDefinition>(AssetDatabase.GUIDToAssetPath(g)));
            return new LegendarySpecialRegistry(specials);
        }

        private static RangedWeaponDefinition Weapon(string file) => AssetDatabase.LoadAssetAtPath<RangedWeaponDefinition>($"Assets/Game/ScriptableObjects/Items/{file}.asset");

        [Test]
        public void FourSpecials_MatchTheApprovedCatalog()
        {
            var registry = Registry();
            Assert.IsTrue(registry.TryGet("snapfire", out var snapfire));
            Assert.AreEqual((SpecialKind.Burst, 10f, 8, 10, 12), (snapfire.Kind, snapfire.CooldownSeconds, snapfire.Shots, snapfire.DamageMin, snapfire.DamageMax), "Quickfang Snapfire: 8 shots, 10-12, 10 s.");
            Assert.IsTrue(registry.TryGet("lead_bloom", out var bloom));
            Assert.AreEqual((SpecialKind.Fan, 14f, 24, 7, 9), (bloom.Kind, bloom.CooldownSeconds, bloom.Shots, bloom.DamageMin, bloom.DamageMax), "Buzzsaw Lead Bloom: 24 projectiles at the SMG's normal 7-9, 14 s.");
            Assert.Greater(bloom.ArcDegrees, 45f, "A broad fan (PROTOTYPE 90 degrees).");
            Assert.IsTrue(registry.TryGet("overrun", out var overrun));
            Assert.AreEqual((SpecialKind.Burst, 13f, 12, 10, 12), (overrun.Kind, overrun.CooldownSeconds, overrun.Shots, overrun.DamageMin, overrun.DamageMax), "Vanguard Overrun: 12-shot hyper-burst, 10-12, 13 s.");
            Assert.IsTrue(registry.TryGet("piercing_line", out var line));
            Assert.AreEqual((SpecialKind.PiercingShot, 12f, 55, 65), (line.Kind, line.CooldownSeconds, line.DamageMin, line.DamageMax), "Judicator Piercing Line: 55-65, 12 s.");

            Assert.IsInstanceOf<BurstSpecial>(LegendarySpecialFactory.Create(snapfire));
            Assert.IsInstanceOf<FanSpecial>(LegendarySpecialFactory.Create(bloom));
            Assert.IsInstanceOf<BurstSpecial>(LegendarySpecialFactory.Create(overrun));
            Assert.IsInstanceOf<PiercingShotSpecial>(LegendarySpecialFactory.Create(line));
            Assert.AreEqual(20f, snapfire.ProjectileSpeed, "Pistol projectile speed.");
            Assert.AreEqual(18f, bloom.ProjectileSpeed);
            Assert.AreEqual(22f, overrun.ProjectileSpeed);
            Assert.AreEqual(26f, line.ProjectileSpeed);
        }

        [Test]
        public void FourLegendaryWeapons_MatchTheCatalog_AndNameTheirFixedSpecial()
        {
            void Check(string file, string id, string name, WeaponClass cls, int dMin, int dMax, float rate, int mag, float reload, float range, float speed, AmmoType ammo, string special)
            {
                var w = Weapon(file);
                Assert.IsNotNull(w, file);
                Assert.AreEqual(id, w.Id);
                Assert.AreEqual(name, w.DisplayName);
                Assert.AreEqual(cls, w.WeaponClass);
                Assert.AreEqual((dMin, dMax), (w.DamageMin, w.DamageMax), id);
                Assert.AreEqual(rate, w.FireRate, 0.001f, id);
                Assert.AreEqual(mag, w.MagazineSize, id);
                Assert.AreEqual(reload, w.ReloadTime, 0.001f, id);
                Assert.AreEqual(range, w.Range, 0.001f, id);
                Assert.AreEqual(speed, w.ProjectileSpeed, 0.001f, id);
                Assert.AreEqual(ammo, w.AmmoType, id);
                Assert.AreEqual(special, w.LegendaryMechanicId, id);
                // Shotguns and rockets carry authored impact, so they roll the impact-capable ranged pool.
                var expectedPool = cls is WeaponClass.Shotgun or WeaponClass.RocketLauncher ? "pool_ranged_impact" : "pool_ranged";
                Assert.AreEqual(expectedPool, w.AffixPool.Id, id);
                Assert.IsNotNull(Registry().CreateFor(w.LegendaryMechanicId), $"{id}: mechanic id resolves to a special.");
            }

            Check("Quickfang", "weapon_quickfang", "Quickfang", WeaponClass.Pistol, 13, 15, 4.4f, 12, 1.2f, 10f, 20f, AmmoType.Light, "snapfire");
            Check("Buzzsaw", "weapon_buzzsaw", "Buzzsaw", WeaponClass.Smg, 7, 9, 11.0f, 36, 1.8f, 8f, 18f, AmmoType.Light, "lead_bloom");
            Check("Vanguard", "weapon_vanguard", "Vanguard", WeaponClass.AssaultRifle, 9, 11, 8.0f, 30, 1.8f, 12f, 22f, AmmoType.Medium, "overrun");
            Check("Judicator", "weapon_judicator", "Judicator", WeaponClass.BattleRifle, 20, 24, 2.8f, 10, 2.0f, 16f, 26f, AmmoType.Medium, "piercing_line");
        }

        [Test]
        public void LegendaryInstance_RollsThreeNormalAffixes_PlusTheFixedSpecial_NeverAsAnAffix()
        {
            var quickfang = Weapon("Quickfang");
            var instance = new ItemInstance(quickfang.Id, 1, Rarity.Legendary);
            var result = new AffixRollService().Roll(instance, quickfang, Rarity.Legendary, new SeededRandom(42));
            Assert.IsTrue(result.Success, result.Error.ToString());
            Assert.AreEqual(3, instance.AffixRolls.Count, "Legendary = 3 normal affixes ...");
            Assert.AreEqual("snapfire", AffixRollService.GetLegendaryMechanicId(instance, quickfang), "... plus the fixed special from the definition.");
            Assert.IsFalse(instance.AffixRolls.Any(r => r.AffixId.Contains("snapfire")), "The special is never an affix roll.");

            var epic = new ItemInstance(quickfang.Id, 1, Rarity.Epic);
            new AffixRollService().Roll(epic, quickfang, Rarity.Epic, new SeededRandom(42));
            Assert.IsNull(AffixRollService.GetLegendaryMechanicId(epic, quickfang), "Below Legendary the special is inactive (Legendary-only definition in loot).");
        }
    }
}

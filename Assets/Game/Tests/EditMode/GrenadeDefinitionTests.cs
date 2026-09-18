using System.Linq;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using UnityEditor;

namespace RuinRail.Tests
{
    public class GrenadeDefinitionTests
    {
        private static ConsumableDefinition Load(string id)
        {
            return AssetDatabase.FindAssets("t:ConsumableDefinition")
                .Select(g => AssetDatabase.LoadAssetAtPath<ConsumableDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Single(c => c.Id == id);
        }

        [Test]
        public void FourGrenades_MatchApprovedValues()
        {
            var frag = Load("consumable_frag_grenade");
            Assert.AreEqual(Rarity.Common, frag.FixedRarity);
            Assert.AreEqual(4, frag.MaxStack);
            Assert.AreEqual(ConsumableEffectKind.Grenade, frag.EffectKind);
            Assert.AreEqual(GrenadeEffectKind.Frag, frag.Grenade.Kind);
            Assert.AreEqual(2.5f, frag.Grenade.RadiusTiles, 0.001f);
            Assert.AreEqual(35, frag.Grenade.DamageMin);
            Assert.AreEqual(45, frag.Grenade.DamageMax);

            var shock = Load("consumable_shock_grenade");
            Assert.AreEqual(Rarity.Rare, shock.FixedRarity);
            Assert.AreEqual(3, shock.MaxStack);
            Assert.AreEqual(GrenadeEffectKind.Shock, shock.Grenade.Kind);
            Assert.AreEqual(15, shock.Grenade.DamageMin);
            Assert.AreEqual(20, shock.Grenade.DamageMax);
            Assert.Greater(shock.Grenade.StaggerPower, frag.Grenade.StaggerPower, "Very high stagger.");

            var incendiary = Load("consumable_incendiary_grenade");
            Assert.AreEqual(Rarity.Rare, incendiary.FixedRarity);
            Assert.AreEqual(3, incendiary.MaxStack);
            Assert.AreEqual(GrenadeEffectKind.Incendiary, incendiary.Grenade.Kind);
            Assert.AreEqual(12, incendiary.Grenade.DamageMin);
            Assert.AreEqual(16, incendiary.Grenade.DamageMax);
            Assert.AreEqual(6, incendiary.Grenade.BurnDamagePerSecond);
            Assert.AreEqual(5f, incendiary.Grenade.BurnDurationSeconds, 0.001f);

            var smoke = Load("consumable_smoke_grenade");
            Assert.AreEqual(Rarity.Uncommon, smoke.FixedRarity);
            Assert.AreEqual(3, smoke.MaxStack);
            Assert.AreEqual(GrenadeEffectKind.Smoke, smoke.Grenade.Kind);
            Assert.AreEqual(4f, smoke.Grenade.RadiusTiles, 0.001f);
            Assert.AreEqual(6f, smoke.Grenade.SmokeDurationSeconds, 0.001f);
            Assert.AreEqual(0, smoke.Grenade.DamageMax);

            foreach (var g in new[] { frag, shock, incendiary, smoke })
            {
                Assert.AreEqual(0f, g.UseTimeSeconds, 0.001f, $"{g.Id}: grenades use the throw animation, no channel.");
                Assert.AreEqual(ConsumptionPoint.OnCompletion, g.ConsumptionPoint, $"{g.Id}: spent only on a successful throw.");
                Assert.IsTrue(g.IsStackable);
            }
        }

        [Test]
        public void DamageRequest_CarriesKindAndStagger_DefaultingToNormal()
        {
            var plain = new DamageRequest(10);
            Assert.AreEqual(DamageKind.Normal, plain.Kind);
            Assert.IsFalse(plain.IsExplosion);
            Assert.AreEqual(0f, plain.StaggerPower);
            var explosion = new DamageRequest(40, DamageKind.Explosion, 4f);
            Assert.IsTrue(explosion.IsExplosion);
            Assert.AreEqual(4f, explosion.StaggerPower);
            Assert.AreEqual(DamageKind.Burn, new DamageRequest(6, DamageKind.Burn).Kind);
        }
    }
}

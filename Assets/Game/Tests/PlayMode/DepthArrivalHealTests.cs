using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Stats;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>
    /// The new-depth heal rule (dungeon/60): a living participant enters a newly generated depth at their own
    /// effective maximum HP (PlayerStats.MaxHealth over the real loadout), never a hard-coded base; the rule is a
    /// pure fill through the ordinary heal path, so it is host-authoritative and can never resurrect anyone.
    /// The live-run half — Descend fires it exactly once, room entries / Transit / rebuilds never — is in
    /// <see cref="DepthSettingsDescriptionsNonCombatProofTests"/>.
    /// </summary>
    public class DepthArrivalHealTests
    {
        private readonly List<Object> _created = new();
        private readonly List<PlayerRig> _rigs = new();
        private GameContentCatalog _catalog;
        private ItemDefinitionRegistry _registry;
        private Dictionary<AmmoType, AmmoItemDefinition> _ammo;

        [SetUp]
        public void SetUp()
        {
            _catalog = GameContentCatalog.Load();
            _registry = _catalog.BuildRegistry();
            _ammo = _registry.Definitions.OfType<AmmoItemDefinition>().ToDictionary(a => a.AmmoType, a => a);
            DamageAuthority.LocalIsAuthoritative = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var rig in _rigs) rig.Dispose();
            _rigs.Clear();
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
            DamageAuthority.LocalIsAuthoritative = true;
        }

        private ItemDefinition Resolve(string id) => _registry.TryGet(id, out var d) ? d : null;

        private (PlayerRig rig, GameObject player, HealthComponent health) StartRun(PartyLifeRoster roster, string participantId, params (string id, EquippedSlot slot)[] equipped)
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
            var state = expedition.Start(profile, 7, Biome.RuinedMetro);
            var rig = new PlayerRig(_catalog, _registry, _catalog.BuildSpecials());
            var player = rig.Build(state, roster, participantId, Vector2.zero, new FakePlayerInputReader());
            _rigs.Add(rig);
            _created.Add(player);
            return (rig, player, player.GetComponent<HealthComponent>());
        }

        private static void Damage(HealthComponent health, int amount) => health.TryApplyDamage(new DamageRequest(amount));

        [Test]
        public void DamagedPlayer_100Max_IsFilledTo100Of100()
        {
            var (rig, player, health) = StartRun(new PartyLifeRoster(), "p1", (StarterKitService.PistolId, EquippedSlot.PrimaryWeapon));
            Damage(health, 63);
            Assert.AreEqual(37, health.CurrentHealth);
            var outcome = DepthArrivalHeal.Apply(player);
            Assert.IsTrue(outcome.Eligible);
            Assert.AreEqual(37, outcome.Before);
            Assert.AreEqual(100, outcome.After);
            Assert.AreEqual(100, outcome.EffectiveMax);
            Assert.AreEqual(100, health.CurrentHealth);
            Assert.AreEqual(rig.StatsBinder.Stats.MaxHealth, health.CurrentHealth, "CurrentHP == EffectiveMaxHP");
        }

        [Test]
        public void ScrapVest_120Max_IsFilledTo120Of120_NotTo100()
        {
            var (rig, player, health) = StartRun(new PartyLifeRoster(), "p1", (StarterKitService.PistolId, EquippedSlot.PrimaryWeapon), (StarterKitService.VestId, EquippedSlot.Armor));
            Assert.AreEqual(120, rig.StatsBinder.Stats.MaxHealth);
            Damage(health, 90); // armor DR applies: the exact value is the component's, the rule reads it
            Assert.Less(health.CurrentHealth, 120);
            var outcome = DepthArrivalHeal.Apply(player);
            Assert.AreEqual(120, outcome.EffectiveMax, "the authoritative effective maximum, not the base 100");
            Assert.AreEqual(120, health.CurrentHealth);
            Assert.AreEqual(120, health.MaxHealth);
            Assert.IsTrue(outcome.IsFull);
        }

        [Test]
        public void HeaviestArmor_UsesItsOwnEffectiveMaximum()
        {
            var plate = _registry.Definitions.OfType<ArmorDefinition>().OrderByDescending(a => a.BaseModifiers().Where(m => m.Stat == StatId.MaxHealth && m.Kind == StatModifierKind.Flat).Sum(m => m.Value)).First();
            var (rig, player, health) = StartRun(new PartyLifeRoster(), "p1", (plate.Id, EquippedSlot.Armor));
            var max = rig.StatsBinder.Stats.MaxHealth;
            Assert.Greater(max, 120);
            Damage(health, max - 5);
            Assert.Less(health.CurrentHealth, max);
            DepthArrivalHeal.Apply(player);
            Assert.AreEqual(max, health.CurrentHealth);
        }

        [Test]
        public void AlreadyFullPlayer_StaysFull_AndRestoresNothing()
        {
            var (_, player, health) = StartRun(new PartyLifeRoster(), "p1", (StarterKitService.VestId, EquippedSlot.Armor));
            var healed = 0;
            health.Healed += n => healed += n;
            var outcome = DepthArrivalHeal.Apply(player);
            Assert.IsTrue(outcome.Eligible);
            Assert.AreEqual(0, outcome.Restored);
            Assert.AreEqual(0, healed, "no heal event for a full player");
            Assert.AreEqual(120, health.CurrentHealth);
        }

        [Test]
        public void ApplyingTheRuleTwice_ChangesNothingTheSecondTime()
        {
            var (_, player, health) = StartRun(new PartyLifeRoster(), "p1", (StarterKitService.PistolId, EquippedSlot.PrimaryWeapon));
            Damage(health, 40);
            var first = DepthArrivalHeal.Apply(player);
            var second = DepthArrivalHeal.Apply(player);
            Assert.AreEqual(40, first.Restored);
            Assert.AreEqual(0, second.Restored);
            Assert.AreEqual(100, health.CurrentHealth);
        }

        [Test]
        public void HealGoesThroughTheOrdinaryHealPath_RaisingHealed_AndNeverOverMax()
        {
            var (_, player, health) = StartRun(new PartyLifeRoster(), "p1", (StarterKitService.VestId, EquippedSlot.Armor));
            Damage(health, 50);
            var missing = 120 - health.CurrentHealth;
            var events = new List<int>();
            health.Healed += events.Add;
            DepthArrivalHeal.Apply(player);
            CollectionAssert.AreEqual(new[] { missing }, events, "one Healed event with the exact restored amount (HUD / audio follow it)");
            Assert.AreEqual(120, health.CurrentHealth);
        }

        [Test]
        public void CoopParty_EachMemberIsFilledToTheirOwnEffectiveMaximum()
        {
            var roster = new PartyLifeRoster();
            var (rigA, a, healthA) = StartRun(roster, "a", (StarterKitService.VestId, EquippedSlot.Armor));
            var (rigB, b, healthB) = StartRun(roster, "b", (StarterKitService.PistolId, EquippedSlot.PrimaryWeapon));
            var plate = _registry.Definitions.OfType<ArmorDefinition>().OrderByDescending(x => x.BaseModifiers().Where(m => m.Stat == StatId.MaxHealth).Sum(m => m.Value)).First();
            var (rigC, c, healthC) = StartRun(roster, "c", (plate.Id, EquippedSlot.Armor));
            Assert.AreEqual(3, roster.Count);
            Damage(healthA, 70); Damage(healthB, 60); Damage(healthC, 100);
            var outcomes = DepthArrivalHeal.ApplyToParty(roster);
            Assert.AreEqual(3, outcomes.Count);
            Assert.AreEqual(120, healthA.CurrentHealth, "a: Scrap Vest");
            Assert.AreEqual(100, healthB.CurrentHealth, "b: no armor");
            Assert.AreEqual(rigC.StatsBinder.Stats.MaxHealth, healthC.CurrentHealth, "c: heaviest armor");
            Assert.IsTrue(outcomes.All(o => o.IsFull));
            CollectionAssert.AreEquivalent(new[] { "a", "b", "c" }, outcomes.Select(o => o.ParticipantId));
            Assert.AreEqual(rigA.StatsBinder.Stats.MaxHealth, outcomes.First(o => o.ParticipantId == "a").EffectiveMax);
            Assert.AreEqual(rigB.StatsBinder.Stats.MaxHealth, outcomes.First(o => o.ParticipantId == "b").EffectiveMax);
        }

        [Test]
        public void DeadMember_IsNeverResurrected_AndDownedMemberIsNotRefilled()
        {
            var roster = new PartyLifeRoster();
            var (_, alive, aliveHealth) = StartRun(roster, "alive", (StarterKitService.PistolId, EquippedSlot.PrimaryWeapon));
            var (_, downed, downedHealth) = StartRun(roster, "downed", (StarterKitService.PistolId, EquippedSlot.PrimaryWeapon));
            var (_, dead, deadHealth) = StartRun(roster, "dead", (StarterKitService.PistolId, EquippedSlot.PrimaryWeapon));
            Damage(aliveHealth, 30);
            Damage(downedHealth, 999); // co-op with an Alive teammate: Downed, not Dead
            var deadLife = dead.GetComponent<PlayerLifeStateComponent>();
            Damage(deadHealth, 999);
            deadLife.MarkDeadByAuthority("test");
            Assert.AreEqual(PlayerLifeState.Downed, downed.GetComponent<PlayerLifeStateComponent>().State);
            Assert.AreEqual(PlayerLifeState.Dead, deadLife.State);

            var outcomes = DepthArrivalHeal.ApplyToParty(roster);
            Assert.AreEqual(100, aliveHealth.CurrentHealth, "the living member is filled");
            Assert.AreEqual(0, downedHealth.CurrentHealth, "a Downed body is not refilled by a rule that is not a revive");
            Assert.AreEqual(0, deadHealth.CurrentHealth, "a Dead body stays dead");
            Assert.AreEqual(PlayerLifeState.Downed, downed.GetComponent<PlayerLifeStateComponent>().State);
            Assert.AreEqual(PlayerLifeState.Dead, deadLife.State, "no resurrection exploit");
            Assert.IsFalse(outcomes.First(o => o.ParticipantId == "downed").Eligible);
            Assert.IsFalse(outcomes.First(o => o.ParticipantId == "dead").Eligible);
        }

        [Test]
        public void OnAClient_TheRuleAppliesNothing_TheHostDecides()
        {
            var (_, player, health) = StartRun(new PartyLifeRoster(), "p1", (StarterKitService.PistolId, EquippedSlot.PrimaryWeapon));
            Damage(health, 40);
            DamageAuthority.LocalIsAuthoritative = false;
            DepthArrivalHeal.Apply(player);
            Assert.AreEqual(60, health.CurrentHealth, "Heal() refuses on a non-authoritative peer; the replicated value arrives from the host");
        }

        [Test]
        public void EquipmentChangeMidRun_StillNeverHeals()
        {
            var (rig, player, health) = StartRun(new PartyLifeRoster(), "p1", (StarterKitService.PistolId, EquippedSlot.PrimaryWeapon));
            Damage(health, 40);
            Assert.IsTrue(rig.Inventory.TryEquip(new ItemInstance(StarterKitService.VestId), EquippedSlot.Armor));
            Assert.AreEqual(120, health.MaxHealth);
            Assert.AreEqual(60, health.CurrentHealth, "raising the maximum does not heal (the depth rule is the only fill, and it is not this)");
        }
    }
}

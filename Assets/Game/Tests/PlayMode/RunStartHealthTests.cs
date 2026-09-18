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
using RuinRail.UI.Hud;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>
    /// A run starts at the TRUE effective maximum HP: after the starting loadout's armor / accessory / affix modifiers
    /// are aggregated by the existing stat pipeline (PlayerStats.MaxHealth), CurrentHP == EffectiveMaxHP — never the
    /// base maximum shown against a larger effective one (100 / 120). Mid-run equipment changes still never heal.
    /// </summary>
    public class RunStartHealthTests
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
            Assert.IsNotNull(_catalog, "GameContentCatalog in Resources");
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
        }

        private ItemDefinition Resolve(string id) => _registry.TryGet(id, out var d) ? d : null;

        private (PlayerRig rig, ExpeditionState state, HealthComponent health) StartRun(params (string id, EquippedSlot slot)[] equipped)
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
            var player = rig.Build(state, null, "local", Vector2.zero, new FakePlayerInputReader());
            _rigs.Add(rig);
            _created.Add(player);
            return (rig, state, player.GetComponent<HealthComponent>());
        }

        [Test]
        public void NoArmor_StartsAtBaseMaximum_100Of100()
        {
            var (rig, _, health) = StartRun((StarterKitService.PistolId, EquippedSlot.PrimaryWeapon));
            Assert.AreEqual(100, rig.StatsBinder.Stats.MaxHealth);
            Assert.AreEqual(100, health.MaxHealth);
            Assert.AreEqual(100, health.CurrentHealth, "CurrentHP == EffectiveMaxHP at the start of the run");
            Assert.AreEqual(100, rig.RunStartHealth);
        }

        [Test]
        public void ScrapVest_StartsAtTrueEffectiveMaximum_120Of120()
        {
            var (rig, _, health) = StartRun((StarterKitService.PistolId, EquippedSlot.PrimaryWeapon), (StarterKitService.VestId, EquippedSlot.Armor));
            Assert.AreEqual(120, rig.StatsBinder.Stats.MaxHealth, "base 100 + Scrap Vest +20 through the stat pipeline");
            Assert.AreEqual(120, health.MaxHealth);
            Assert.AreEqual(120, health.CurrentHealth, "the run starts full, not at 100 / 120");
        }

        [Test]
        public void HeavierArmor_StartsAtItsOwnEffectiveMaximum()
        {
            var plate = _registry.Definitions.OfType<ArmorDefinition>().OrderByDescending(a => a.BaseModifiers().Where(m => m.Stat == StatId.MaxHealth && m.Kind == StatModifierKind.Flat).Sum(m => m.Value)).First();
            var bonus = plate.BaseModifiers().Where(m => m.Stat == StatId.MaxHealth && m.Kind == StatModifierKind.Flat).Sum(m => m.Value);
            Assert.Greater(bonus, 20, "a heavier armor than the Scrap Vest exists in the catalog");
            var (rig, _, health) = StartRun((plate.Id, EquippedSlot.Armor));
            Assert.AreEqual(100 + bonus, rig.StatsBinder.Stats.MaxHealth);
            Assert.AreEqual(100 + bonus, health.CurrentHealth);
            Assert.AreEqual(health.MaxHealth, health.CurrentHealth);
        }

        [Test]
        public void MultipleMaxHealthModifiers_AreAggregatedByTheStatPipeline_FlatThenPercent()
        {
            var stats = new PlayerStats(ScriptableObject.CreateInstance<GlobalStatCapsConfig>(), 100);
            stats.SetSource(new StaticSource("armor", new StatModifier(StatId.MaxHealth, StatModifierKind.Flat, 20)));
            stats.SetSource(new StaticSource("accessory", new StatModifier(StatId.MaxHealth, StatModifierKind.Flat, 10)));
            stats.SetSource(new StaticSource("affix", new StatModifier(StatId.MaxHealth, StatModifierKind.Percent, 10)));
            Assert.AreEqual(Mathf.RoundToInt((100 + 20 + 10) * 1.10f), stats.MaxHealth, "round((base + flat) x multiplier)");

            var player = new GameObject("Player");
            _created.Add(player);
            var health = player.AddComponent<HealthComponent>();
            health.SetMaxHealth(stats.MaxHealth);
            Assert.AreEqual(stats.MaxHealth, health.CurrentHealth, "a fresh run fills to the aggregated maximum");
        }

        [Test]
        public void MidRunArmorChange_NeverHeals_AndRemovingArmorClampsOnlyWhenNeeded()
        {
            var (rig, state, health) = StartRun((StarterKitService.PistolId, EquippedSlot.PrimaryWeapon), (StarterKitService.VestId, EquippedSlot.Armor));
            Assert.AreEqual(120, health.CurrentHealth);
            health.TryApplyDamage(new DamageRequest(50));
            var damaged = health.CurrentHealth; // < 120 (the vest's damage reduction shaves the 50)
            Assert.Less(damaged, 120);
            Assert.Greater(damaged, 0);

            // Swap to a heavier armor mid-run: the maximum rises, the current value does not (no free heal).
            var plate = _registry.Definitions.OfType<ArmorDefinition>().First(a => a.Id != StarterKitService.VestId && a.BaseModifiers().Any(m => m.Stat == StatId.MaxHealth && m.Value > 20));
            var vest = state.Inventory.Unequip(EquippedSlot.Armor);
            Assert.IsNotNull(vest);
            Assert.IsTrue(state.Inventory.TryEquip(new ItemInstance(plate.Id), EquippedSlot.Armor));
            Assert.Greater(health.MaxHealth, 120);
            Assert.AreEqual(damaged, health.CurrentHealth, "raising the maximum mid-run never heals");

            // Remove the armor: the maximum drops to 100; current 70 needs no clamp.
            state.Inventory.Unequip(EquippedSlot.Armor);
            Assert.AreEqual(100, health.MaxHealth);
            Assert.AreEqual(Mathf.Min(damaged, 100), health.CurrentHealth, "clamp only applies when current exceeds the reduced maximum");

            // Re-equip the vest and heal to full 120, then remove it: now the clamp applies (120 -> 100).
            Assert.IsTrue(state.Inventory.TryEquip(vest, EquippedSlot.Armor));
            health.Heal(200);
            Assert.AreEqual(120, health.CurrentHealth);
            state.Inventory.Unequip(EquippedSlot.Armor);
            Assert.AreEqual(100, health.MaxHealth);
            Assert.AreEqual(100, health.CurrentHealth, "current is clamped to the reduced maximum, never below");
        }

        [Test]
        public void NewExpeditionAfterADamagedRun_StartsFullAgain_AndTheHudDenominatorIsPlayerStatsMaxHealth()
        {
            var (_, _, first) = StartRun((StarterKitService.VestId, EquippedSlot.Armor));
            first.TryApplyDamage(new DamageRequest(80));
            Assert.Less(first.CurrentHealth, 60);

            // A new run builds a new rig from the profile: full again, independent of the previous run's damage.
            var (rig, _, health) = StartRun((StarterKitService.VestId, EquippedSlot.Armor));
            Assert.AreEqual(120, health.CurrentHealth);

            var hud = new DungeonHudViewModel();
            hud.BindPlayer(health, rig.Player.GetComponent<PlayerDash>(), rig.Player.GetComponent<PlayerLifeStateComponent>());
            Assert.AreEqual(rig.StatsBinder.Stats.MaxHealth, hud.Snapshot.MaxHp, "the HUD denominator is PlayerStats.MaxHealth");
            Assert.AreEqual("120 / 120", hud.Snapshot.HpText);
        }

        [Test]
        public void StarterKitOnAFreshProfile_StartsAt120Of120_ThroughTheStarterLoadout()
        {
            // The wipe / new-profile path: a fresh profile is granted the starter kit into its safe loadout, and that is what its first run carries.
            var kit = new StarterKitService(Resolve, t => _ammo.TryGetValue(t, out var a) ? a : null, _catalog.AmmoBalance);
            var profile = new PlayerProfile();
            Assert.IsTrue(kit.GrantFirstProfileKit(profile));
            var equipped = profile.SafeLoadout.Equipped.Select(e => (e.Item.DefinitionId, (EquippedSlot)e.Slot)).ToArray();
            Assert.IsTrue(equipped.Any(e => e.DefinitionId == StarterKitService.VestId && e.Item2 == EquippedSlot.Armor), "the kit equips the Scrap Vest");
            var (rig, _, health) = StartRun(equipped);
            Assert.AreEqual(120, rig.StatsBinder.Stats.MaxHealth);
            Assert.AreEqual(120, health.CurrentHealth);
            Assert.AreEqual(health.MaxHealth, health.CurrentHealth);
        }

        private sealed class StaticSource : IStatModifierSource
        {
            private readonly StatModifier[] _modifiers;
            public StaticSource(string id, params StatModifier[] modifiers) { SourceId = id; _modifiers = modifiers; }
            public string SourceId { get; }
            public IEnumerable<StatModifier> GetModifiers() => _modifiers;
        }
    }
}

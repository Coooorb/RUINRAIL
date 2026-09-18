using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using RuinRail.Gameplay.Combat.Weapons.Specials;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.UI.Inventory;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 132: Tab inventory over the transfer service — slots, service-only transfers, solo pause vs co-op, exact tooltips, ownership stable across open/close, navigation.</summary>
    public class InventoryUiTests
    {
        private readonly List<Object> _created = new();
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;
        private GroundLootRegistry _ground;
        private LootSpawner _spawner;
        private LegendarySpecialRegistry _specials;

        private sealed class CountingPause : IWorldPause
        {
            public int Pauses;
            public int Resumes;
            public void Pause() => Pauses++;
            public void Resume() => Resumes++;
        }

        [SetUp]
        public void SetUp()
        {
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(catalog);
            _ammoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
            _specials = new LegendarySpecialRegistry(AssetDatabase.FindAssets("t:LegendarySpecialDefinition").Select(g => AssetDatabase.LoadAssetAtPath<LegendarySpecialDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null));
            _ground = new GroundLootRegistry();
            var spawnerObject = new GameObject("Spawner");
            _created.Add(spawnerObject);
            _spawner = spawnerObject.AddComponent<LootSpawner>();
            _spawner.SetRegistry(_ground);
            _spawner.SetDefinitionResolver(Resolve);
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var go in _ground.Tracked.ToArray()) if (go != null) Object.DestroyImmediate(go);
            _created.Clear();
        }

        private ItemDefinition Resolve(string id) => _registry.TryGet(id, out var d) ? d : null;

        private (InventoryViewModel vm, PlayerInventory inventory, PlayerLootReceiver receiver) Build(bool withDrops = true)
        {
            var inventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            var go = new GameObject("Player");
            _created.Add(go);
            var receiver = go.AddComponent<PlayerLootReceiver>();
            receiver.SetInventory(inventory);
            receiver.SetWallet(new CoinWallet(CoinDomain.Carried, 340));
            if (withDrops) receiver.SetDropService(new ItemDropService(_spawner));
            var vm = new InventoryViewModel();
            vm.Bind(inventory, receiver, () => 340, _specials);
            return (vm, inventory, receiver);
        }

        private static IEnumerable<string> AllIds(PlayerInventory inventory)
        {
            foreach (EquippedSlot slot in System.Enum.GetValues(typeof(EquippedSlot)))
            {
                var e = inventory.GetEquipped(slot);
                if (e != null) yield return e.InstanceId;
            }

            foreach (var b in inventory.BackpackSlots) if (b != null) yield return b.InstanceId;
        }

        private static InventorySlotRef Bag(int i) => new(InventorySlotKind.Backpack, i);
        private static InventorySlotRef Eq(EquippedSlot slot) => new(InventorySlotKind.Equipped, (int)slot);

        // ---- Req 1: slots and coins ----

        [Test]
        public void Inventory_ShowsFiveEquipmentSlots_EightBackpackSlots_AndCoinsSeparately()
        {
            var (vm, inventory, _) = Build();
            CollectionAssert.AreEqual(new[] { EquippedSlot.PrimaryWeapon, EquippedSlot.SecondaryWeapon, EquippedSlot.Armor, EquippedSlot.Accessory, EquippedSlot.ActiveConsumable }, vm.EquipmentSlots);
            Assert.AreEqual(8, InventoryViewModel.BackpackSlots);
            Assert.AreEqual(8, inventory.BackpackSlots.Count);
            Assert.AreEqual(340, vm.Coins, "Carried Coins are not an item slot.");
            var view = InventoryView.Create(vm);
            _created.Add(view.gameObject);
            Assert.IsFalse(view.IsVisible);
            vm.Open();
            Assert.IsTrue(view.IsVisible);
            Assert.AreEqual(5, view.EquipmentTexts.Count);
            Assert.AreEqual(8, view.BackpackTexts.Count);
            StringAssert.Contains("340", view.CoinsText);
            StringAssert.Contains("COINS", view.CoinsText);
            StringAssert.Contains("PRIMARY", view.EquipmentTexts[0]);
            StringAssert.Contains("CONSUMABLE", view.EquipmentTexts[4]);
            Assert.AreEqual(5, view.EquipmentSlots.Count, "five graphical equipment slots");
            Assert.AreEqual(8, view.BackpackSlots.Count, "eight graphical backpack slots");
        }

        // ---- Req 2 / Acceptance 1: every transfer through the service, no dup/loss ----

        [Test]
        public void EquipSwapUnequipDrop_AllGoThroughTheTransferService_WithoutDuplicationOrLoss()
        {
            var (vm, inventory, _) = Build();
            var rifle = new ItemInstance("weapon_p9_ranger");
            var smg = new ItemInstance("weapon_rattler_9", 1, Rarity.Rare);
            var vest = new ItemInstance("armor_blast_suit");
            var bandage = new ItemInstance("consumable_bandage", 3);
            Assert.IsTrue(inventory.TryAddToBackpack(rifle));
            Assert.IsTrue(inventory.TryAddToBackpack(smg));
            Assert.IsTrue(inventory.TryAddToBackpack(vest));
            Assert.IsTrue(inventory.TryAddToBackpack(bandage));
            var ids = AllIds(inventory).OrderBy(i => i).ToList();

            // Equip from the backpack (default slot), then equip the SMG into the occupied primary: swap through the backpack.
            Assert.AreEqual(InventoryActionResult.Done, vm.Equip(Bag(0)));
            Assert.AreSame(rifle, inventory.GetEquipped(EquippedSlot.PrimaryWeapon));
            Assert.AreEqual(InventoryActionResult.Done, vm.MoveTo(Bag(inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.InstanceId == smg.InstanceId)), Eq(EquippedSlot.PrimaryWeapon)));
            Assert.AreSame(smg, inventory.GetEquipped(EquippedSlot.PrimaryWeapon), "Swap: the SMG took the slot...");
            Assert.IsTrue(inventory.BackpackSlots.Any(i => i != null && i.InstanceId == rifle.InstanceId), "...and the rifle returned to the backpack.");
            Assert.AreEqual(InventoryActionResult.Done, vm.SetActiveConsumable(Bag(inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.DefinitionId == "consumable_bandage"))));
            Assert.AreEqual("consumable_bandage", inventory.GetEquipped(EquippedSlot.ActiveConsumable).DefinitionId);
            Assert.AreEqual(InventoryActionResult.Done, vm.Equip(Bag(inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.InstanceId == vest.InstanceId))));
            Assert.AreSame(vest, inventory.GetEquipped(EquippedSlot.Armor));
            Assert.AreEqual(InventoryActionResult.IncompatibleSlot, vm.MoveTo(Eq(EquippedSlot.Armor), Eq(EquippedSlot.PrimaryWeapon)), "Armor never goes into a weapon slot.");
            Assert.AreEqual(InventoryActionResult.Done, vm.MoveTo(Eq(EquippedSlot.PrimaryWeapon), Eq(EquippedSlot.SecondaryWeapon)), "Weapon slots are class-agnostic: primary -> secondary.");
            Assert.AreSame(smg, inventory.GetEquipped(EquippedSlot.SecondaryWeapon));
            Assert.AreEqual(InventoryActionResult.Done, vm.Unequip(EquippedSlot.Armor));
            Assert.IsNull(inventory.GetEquipped(EquippedSlot.Armor));

            CollectionAssert.AreEquivalent(ids, AllIds(inventory).ToList(), "Every instance exactly once after equip/swap/move/unequip.");
            var containers = new List<IItemContainer> { new BackpackContainer(inventory) };
            foreach (EquippedSlot slot in System.Enum.GetValues(typeof(EquippedSlot))) containers.Add(new EquippedSlotContainer(inventory, slot));
            Assert.IsEmpty(ItemTransferService.DetectDuplicateOwnership(containers));

            // Drop: the instance leaves the inventory and lands on the ground exactly once.
            var smgIndex = 1;
            Assert.AreEqual(InventoryActionResult.Done, vm.Drop(Eq(EquippedSlot.SecondaryWeapon)));
            Assert.IsNull(inventory.GetEquipped(EquippedSlot.SecondaryWeapon));
            Assert.AreEqual(1, _ground.Count);
            Assert.AreEqual(smg.InstanceId, _ground.Tracked[0].GetComponent<WorldItemPickup>().Item.InstanceId);
            Assert.IsFalse(AllIds(inventory).Contains(smg.InstanceId));
            Assert.AreEqual(ids.Count - 1, AllIds(inventory).Count());
            _ = smgIndex;

            // BACKPACK FULL: unequip refused when no room; message surfaced.
            for (var i = 0; inventory.BackpackSlots.Any(s => s == null); i++) Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance("weapon_p9_ranger")));
            Assert.IsTrue(vm.IsBackpackFull);
            Assert.AreEqual(InventoryActionResult.BackpackFull, vm.Unequip(EquippedSlot.ActiveConsumable));
            Assert.AreEqual("BACKPACK FULL", vm.Message);
            Assert.AreEqual("consumable_bandage", inventory.GetEquipped(EquippedSlot.ActiveConsumable).DefinitionId, "Refused transfer changes nothing.");

            // The UI code never edits containers directly.
            foreach (var source in Directory.GetFiles("Assets/Game/Scripts/UI/Inventory", "*.cs").Select(File.ReadAllText))
            {
                Assert.IsFalse(Regex.IsMatch(source, @"TryEquip\(|TryAddToBackpack\(|RemoveFromBackpack\(|\.Unequip\(EquippedSlot|SetQuantity\(|\.Slots\b|\.Items\.Add|\.Items\.Remove"), "Inventory UI goes through ItemTransferService / PlayerLootReceiver only.");
                Assert.IsFalse(Regex.IsMatch(source, @"GearScore|PowerScore|Power Score"), "No invented score.");
            }
        }

        // ---- Req 4 / Acceptance 2: solo pauses, co-op never ----

        [Test]
        public void Solo_OpeningPausesTheWorld_Coop_DoesNot()
        {
            var (solo, _, _) = Build();
            var pause = new CountingPause();
            solo.ConfigurePause(pause, isCoop: false);
            solo.Open();
            Assert.AreEqual(1, pause.Pauses);
            solo.Open();
            Assert.AreEqual(1, pause.Pauses, "Idempotent.");
            solo.Close();
            Assert.AreEqual(1, pause.Resumes);

            var (coop, _, _) = Build();
            var coopPause = new CountingPause();
            coop.ConfigurePause(coopPause, isCoop: true);
            coop.Open();
            coop.Close();
            Assert.AreEqual(0, coopPause.Pauses, "92: co-op inventory does not pause the shared game.");
            Assert.AreEqual(0, coopPause.Resumes);

            var timeScale = new TimeScalePause();
            solo.ConfigurePause(timeScale, isCoop: false);
            solo.Open();
            Assert.AreEqual(0f, Time.timeScale);
            solo.Close();
            Assert.AreEqual(1f, Time.timeScale);
        }

        // ---- Req 3 / Acceptance 3: tooltips reflect exact rolls and fixed Legendary mechanics ----

        [Test]
        public void Tooltips_ShowNameRarityBaseStatsExactAffixRollsLegendaryMechanicAndQuantity_NoScore()
        {
            var (vm, inventory, _) = Build();
            var rifleDefinition = (RangedWeaponDefinition)Resolve("weapon_p9_ranger");
            var rare = new ItemInstance("weapon_p9_ranger", 1, Rarity.Rare);
            var pool = rifleDefinition.AffixPool;
            Assert.IsNotNull(pool);
            rare.AddAffixRoll(new AffixRoll(pool.Affixes[0].Id, 7));
            rare.AddAffixRoll(new AffixRoll(pool.Affixes[1].Id, 3));
            inventory.TryAddToBackpack(rare);
            var tooltip = vm.TooltipAt(Bag(0));
            Assert.AreEqual(rifleDefinition.DisplayName, tooltip.Name);
            Assert.AreEqual("RARE", tooltip.RarityText, "90: rarity as explicit text.");
            Assert.AreEqual("WEAPON", tooltip.CategoryText);
            Assert.IsTrue(tooltip.BaseStats.Any(l => l.Label == "Damage" && l.Value == $"{rifleDefinition.DamageMin}–{rifleDefinition.DamageMax}"));
            Assert.IsTrue(tooltip.BaseStats.Any(l => l.Label == "Magazine" && l.Value == rifleDefinition.MagazineSize.ToString()));
            Assert.AreEqual(2, tooltip.Affixes.Count);
            Assert.AreEqual(pool.Affixes[0].DisplayName + " (affix)", tooltip.Affixes[0].Label);
            Assert.AreEqual("+7", tooltip.Affixes[0].Value, "The exact rolled integer, not the range.");
            Assert.AreEqual("+3", tooltip.Affixes[1].Value);
            Assert.IsNull(tooltip.LegendaryText);
            Assert.IsNull(tooltip.Quantity, "Non-stackable: no quantity line.");
            Assert.IsFalse(tooltip.Lines().Any(l => l.ToLowerInvariant().Contains("score")));

            var legendaryDefinition = _registry.Definitions.OfType<WeaponDefinition>().First(w => !string.IsNullOrEmpty(w.LegendaryMechanicId) && _specials.TryGet(w.LegendaryMechanicId, out _));
            var legendary = new ItemInstance(legendaryDefinition.Id, 1, Rarity.Legendary);
            inventory.TryAddToBackpack(legendary);
            var legendaryTooltip = vm.TooltipAt(Bag(1));
            Assert.AreEqual("LEGENDARY", legendaryTooltip.RarityText);
            _specials.TryGet(legendaryDefinition.LegendaryMechanicId, out var special);
            StringAssert.Contains(special.DisplayName, legendaryTooltip.LegendaryText, "Fixed Legendary special named from the registry.");
            StringAssert.Contains("cooldown", legendaryTooltip.LegendaryText);

            var stack = new ItemInstance("consumable_bandage", 4);
            inventory.TryAddToBackpack(stack);
            Assert.AreEqual(4, vm.TooltipAt(Bag(2)).Quantity, "Stack quantity where relevant.");

            // Comparison against the equipped candidate slot: per-stat marks, never a single score.
            var common = new ItemInstance("weapon_p9_ranger");
            inventory.TryEquip(common, EquippedSlot.PrimaryWeapon);
            var comparison = vm.CompareAt(Bag(0));
            Assert.IsTrue(comparison.Count > 0);
            var affixLine = comparison.First(c => c.Label == pool.Affixes[0].DisplayName + " (affix)");
            Assert.AreEqual("+7", affixLine.Candidate);
            Assert.AreEqual("—", affixLine.Current);
            Assert.AreEqual("▲", affixLine.Mark);
            Assert.AreEqual("=", comparison.First(c => c.Label == "Damage").Mark, "Same base weapon: equal damage.");
        }

        // ---- Acceptance 4: open/close keeps ownership; navigation ----

        [Test]
        public void OpenAndClose_NeverChangeOwnership_AndCursorNavigationCoversEverySlot()
        {
            var (vm, inventory, _) = Build();
            inventory.TryEquip(new ItemInstance("weapon_p9_ranger"), EquippedSlot.PrimaryWeapon);
            inventory.TryAddToBackpack(new ItemInstance("consumable_bandage", 2));
            var before = AllIds(inventory).OrderBy(i => i).ToList();
            var quantity = inventory.BackpackSlots[0].Quantity;
            for (var i = 0; i < 5; i++) { vm.Open(); vm.Close(); }
            Assert.AreEqual(5, vm.Opens);
            CollectionAssert.AreEqual(before, AllIds(inventory).OrderBy(i => i).ToList());
            Assert.AreEqual(quantity, inventory.BackpackSlots[0].Quantity);

            vm.Open();
            Assert.AreEqual(Eq(EquippedSlot.PrimaryWeapon), vm.Cursor);
            vm.MoveCursor(Vector2Int.down);
            Assert.AreEqual(Eq(EquippedSlot.SecondaryWeapon), vm.Cursor);
            for (var i = 0; i < 10; i++) vm.MoveCursor(Vector2Int.down);
            Assert.AreEqual(Eq(EquippedSlot.ActiveConsumable), vm.Cursor, "Clamped at the last equipment slot.");
            vm.MoveCursor(Vector2Int.right);
            Assert.AreEqual(Bag(4), vm.Cursor, "Crossing from the lower equipment rows lands on the backpack's second row.");
            vm.MoveCursor(Vector2Int.right);
            vm.MoveCursor(Vector2Int.right);
            vm.MoveCursor(Vector2Int.right);
            vm.MoveCursor(Vector2Int.right);
            Assert.AreEqual(Bag(7), vm.Cursor, "Clamped at the last column.");
            vm.MoveCursor(Vector2Int.up);
            Assert.AreEqual(Bag(3), vm.Cursor);
            for (var i = 0; i < 3; i++) vm.MoveCursor(Vector2Int.left);
            Assert.AreEqual(Bag(0), vm.Cursor);
            vm.MoveCursor(Vector2Int.left);
            Assert.AreEqual(Eq(EquippedSlot.PrimaryWeapon), vm.Cursor, "Left from the first column returns to equipment.");

            // Activate = select then move: keyboard/controller equip of the bandage into the consumable slot.
            vm.SetCursor(Bag(0));
            Assert.AreEqual(InventoryActionResult.Done, vm.Activate());
            Assert.AreEqual(Bag(0), vm.Selected);
            vm.SetCursor(Eq(EquippedSlot.ActiveConsumable));
            Assert.AreEqual(InventoryActionResult.Done, vm.Activate());
            Assert.IsNull(vm.Selected);
            Assert.IsNotNull(inventory.GetEquipped(EquippedSlot.ActiveConsumable));
            Assert.IsNull(inventory.BackpackSlots[0]);
            vm.Close();
            CollectionAssert.AreEqual(before, AllIds(inventory).OrderBy(i => i).ToList(), "Ownership set unchanged: one instance moved slots.");
        }
    }
}

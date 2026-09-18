using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    public class StarterKitTests
    {
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;
        private StarterKitService _kit;

        [SetUp]
        public void SetUp()
        {
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(catalog);
            _ammoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
            var ammoByType = _registry.Definitions.OfType<AmmoItemDefinition>().ToDictionary(a => a.AmmoType, a => a);
            _kit = new StarterKitService(id => _registry.TryGet(id, out var d) ? d : null, t => ammoByType.TryGetValue(t, out var a) ? a : null, _ammoBalance);
        }

        private PlayerInventory Load(PlayerProfile profile)
        {
            var ammoByType = _registry.Definitions.OfType<AmmoItemDefinition>().ToDictionary(a => a.AmmoType, a => a);
            var inventory = new PlayerInventory(id => _registry.TryGet(id, out var d) ? d : null, t => ammoByType.TryGetValue(t, out var a) ? a : null, _ammoBalance);
            inventory.RestoreFromSnapshot(profile.SafeLoadout);
            return inventory;
        }

        // ---- Acceptance 1 + 2 ----

        [Test]
        public void NewProfile_ReceivesExactlyTheKit_Once()
        {
            var profile = new PlayerProfile();
            Assert.IsTrue(_kit.GrantFirstProfileKit(profile));
            Assert.IsTrue(profile.StarterKitGranted);

            var inventory = Load(profile);
            var pistol = inventory.GetEquipped(EquippedSlot.PrimaryWeapon);
            var vest = inventory.GetEquipped(EquippedSlot.Armor);
            var bandage = inventory.GetEquipped(EquippedSlot.ActiveConsumable);
            Assert.AreEqual("weapon_p9_ranger", pistol.DefinitionId);
            Assert.AreEqual(Rarity.Common, pistol.Rarity);
            Assert.IsEmpty(pistol.AffixRolls, "Forced Common, no affixes.");
            Assert.IsTrue(pistol.IsUnsellable);
            Assert.AreEqual("armor_scrap_vest", vest.DefinitionId);
            Assert.AreEqual(Rarity.Common, vest.Rarity);
            Assert.IsEmpty(vest.AffixRolls);
            Assert.IsTrue(vest.IsUnsellable);
            Assert.AreEqual("consumable_bandage", bandage.DefinitionId);
            Assert.AreEqual(1, bandage.Quantity);
            Assert.AreEqual(60, inventory.Get(AmmoType.Light));
            var knife = inventory.GetEquipped(EquippedSlot.SecondaryWeapon);
            Assert.IsNotNull(knife, "The ammo-free Secondary is guaranteed (75, design update 2026-09-17).");
            Assert.AreEqual(StarterKitService.KnifeId, knife.DefinitionId);
            Assert.AreEqual(Rarity.Common, knife.Rarity);
            Assert.IsEmpty(knife.AffixRolls);
            Assert.IsTrue(knife.IsUnsellable);
            Assert.IsNull(inventory.GetEquipped(EquippedSlot.Accessory), "No guaranteed Accessory.");
            Assert.AreEqual(1, inventory.BackpackSlots.Count(s => s != null), "Only the ammo stack occupies the backpack.");

            Assert.IsFalse(_kit.GrantFirstProfileKit(profile), "Second boot: nothing.");
            Assert.IsFalse(_kit.GrantFirstProfileKit(profile));
            var reloaded = Load(profile);
            Assert.AreEqual(60, reloaded.Get(AmmoType.Light));
            Assert.AreEqual(1, reloaded.BackpackSlots.Count(s => s != null));
            Assert.AreEqual(pistol.InstanceId, reloaded.GetEquipped(EquippedSlot.PrimaryWeapon).InstanceId);

            var json = JsonUtility.ToJson(profile);
            var restored = JsonUtility.FromJson<PlayerProfile>(json);
            Assert.IsTrue(restored.StarterKitGranted, "Flag persists through serialization.");
            Assert.IsFalse(_kit.GrantFirstProfileKit(restored));
        }

        // ---- Acceptance 3 ----

        [Test]
        public void KitCoreItems_AreRejectedByTheTrader_AndAreWorthNothing()
        {
            var profile = new PlayerProfile { BankedCoins = 0, ProfileSeed = 1 };
            _kit.GrantFirstProfileKit(profile);
            var inventory = Load(profile);
            var storage = new Storage(id => _registry.TryGet(id, out var d) ? d : null, _ammoBalance);
            var pistol = inventory.Unequip(EquippedSlot.PrimaryWeapon);
            var vest = inventory.Unequip(EquippedSlot.Armor);
            storage.TryAdd(pistol);
            storage.TryAdd(vest);

            var economy = AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset");
            var traderConfig = AssetDatabase.LoadAssetAtPath<TraderConfig>("Assets/Game/ScriptableObjects/Base/TraderConfig.asset");
            var banked = new CoinWallet(CoinDomain.Banked, () => profile.BankedCoins, v => profile.BankedCoins = v);
            var trader = new TraderService(traderConfig, new PriceService(economy), banked, profile.Trader, profile.ProfileSeed, _registry.Definitions, id => _registry.TryGet(id, out var d) ? d : null);

            Assert.AreEqual(TradeError.Unsellable, trader.Sell(storage, pistol.InstanceId));
            Assert.AreEqual(TradeError.Unsellable, trader.Sell(storage, vest.InstanceId));
            Assert.AreEqual(0, profile.BankedCoins);
            Assert.IsNotNull(storage.Find(pistol.InstanceId));
            Assert.IsNotNull(storage.Find(vest.InstanceId));
            Assert.AreEqual(0, trader.QuoteSellValue(pistol));

            var bandage = inventory.GetEquipped(EquippedSlot.ActiveConsumable);
            Assert.IsFalse(bandage.IsUnsellable, "Only the core equipment is protected; consumables/ammo are ordinary.");
        }

        // ---- Acceptance 4 ----

        [Test]
        public void SoftlockRescue_RegrantsOnlyWhenNoWeaponExistsAnywhere()
        {
            var profile = new PlayerProfile();
            _kit.GrantFirstProfileKit(profile);
            var storage = new Storage(id => _registry.TryGet(id, out var d) ? d : null, _ammoBalance);

            Assert.IsFalse(_kit.NeedsRescueKit(profile, storage));
            Assert.IsFalse(_kit.EnsureStartableLoadout(profile, storage), "Owning a weapon: no free gear.");

            // Simulate a failed expedition that destroyed the carried loadout.
            profile.SafeLoadout = null;
            Assert.IsTrue(_kit.NeedsRescueKit(profile, storage));
            Assert.IsTrue(_kit.EnsureStartableLoadout(profile, storage));
            var inventory = Load(profile);
            var pistol = inventory.GetEquipped(EquippedSlot.PrimaryWeapon);
            Assert.AreEqual("weapon_p9_ranger", pistol.DefinitionId);
            Assert.AreEqual(Rarity.Common, pistol.Rarity);
            Assert.IsTrue(pistol.IsUnsellable);
            Assert.AreEqual(StarterKitService.KnifeId, inventory.GetEquipped(EquippedSlot.SecondaryWeapon)?.DefinitionId, "The rescue kit restores the ammo-free Secondary too.");
            Assert.AreEqual(60, inventory.Get(AmmoType.Light));
            Assert.IsTrue(profile.StarterKitGranted, "First-grant flag untouched by a rescue.");

            // A weapon parked in Storage counts: no rescue even with an empty loadout.
            profile.SafeLoadout = null;
            storage.TryAdd(new ItemInstance("weapon_ar_17"));
            Assert.IsFalse(_kit.NeedsRescueKit(profile, storage));
            Assert.IsFalse(_kit.EnsureStartableLoadout(profile, storage));
            Assert.IsNull(profile.SafeLoadout);

            foreach (var (item, _) in StarterKitService.CreateKit())
            {
                Assert.AreEqual(Rarity.Common, item.Rarity, item.DefinitionId);
                Assert.IsEmpty(item.AffixRolls, item.DefinitionId);
            }
        }

        // ---- Ammo-free Secondary (75 design update 2026-09-17) ----

        [Test]
        public void AmmoFreeSecondary_IsTheApprovedFieldKnife_InTheSecondarySlot_AndConsumesNoAmmo()
        {
            var definition = _registry.TryGet(StarterKitService.KnifeId, out var d) ? d : null;
            Assert.IsNotNull(definition, "The Field Knife definition exists in the catalog (no new weapon was invented).");
            Assert.IsInstanceOf<MeleeWeaponDefinition>(definition, "Knife = existing melee logic, no ammo resource.");
            Assert.AreEqual(WeaponClass.Knife, ((WeaponDefinition)definition).WeaponClass);
            Assert.AreEqual(ItemCategory.Weapon, definition.Category);

            var kit = StarterKitService.CreateKit();
            var secondary = kit.Single(k => k.slot == EquippedSlot.SecondaryWeapon);
            Assert.AreEqual(StarterKitService.KnifeId, secondary.item.DefinitionId);
            Assert.AreEqual(StarterKitService.PistolId, kit.Single(k => k.slot == EquippedSlot.PrimaryWeapon).item.DefinitionId);
            Assert.AreEqual(1, kit.Count(k => k.item.DefinitionId == StarterKitService.KnifeId), "Exactly one knife per kit.");
        }

        [Test]
        public void RepeatedInitialisation_NeverDuplicatesTheSecondary()
        {
            var profile = new PlayerProfile();
            var storage = new Storage(id => _registry.TryGet(id, out var d) ? d : null, _ammoBalance);
            Assert.IsTrue(_kit.GrantFirstProfileKit(profile));
            for (var boot = 0; boot < 5; boot++)
            {
                Assert.IsFalse(_kit.GrantFirstProfileKit(profile));
                Assert.IsFalse(_kit.EnsureStartableLoadout(profile, storage));
            }

            var inventory = Load(profile);
            var knives = inventory.BackpackSlots.Count(s => s != null && s.DefinitionId == StarterKitService.KnifeId)
                         + (inventory.GetEquipped(EquippedSlot.SecondaryWeapon)?.DefinitionId == StarterKitService.KnifeId ? 1 : 0)
                         + (inventory.GetEquipped(EquippedSlot.PrimaryWeapon)?.DefinitionId == StarterKitService.KnifeId ? 1 : 0);
            Assert.AreEqual(1, knives, "One knife after any number of boots.");
            Assert.AreEqual(1, inventory.BackpackSlots.Count(s => s != null), "Still only the ammo stack in the backpack.");

            // A wipe followed by several boots: one rescue, then nothing.
            profile.SafeLoadout = null;
            Assert.IsTrue(_kit.EnsureStartableLoadout(profile, storage));
            Assert.IsFalse(_kit.EnsureStartableLoadout(profile, storage));
            inventory = Load(profile);
            Assert.AreEqual(StarterKitService.KnifeId, inventory.GetEquipped(EquippedSlot.SecondaryWeapon).DefinitionId);
            Assert.AreEqual(StarterKitService.PistolId, inventory.GetEquipped(EquippedSlot.PrimaryWeapon).DefinitionId);
            Assert.AreEqual(0, inventory.BackpackSlots.Count(s => s != null && s.DefinitionId == StarterKitService.KnifeId));
        }

        [Test]
        public void Knife_IsUnsellableLikeTheOtherStarterEquipment()
        {
            var profile = new PlayerProfile { ProfileSeed = 3 };
            _kit.GrantFirstProfileKit(profile);
            var inventory = Load(profile);
            var storage = new Storage(id => _registry.TryGet(id, out var d) ? d : null, _ammoBalance);
            var knife = inventory.Unequip(EquippedSlot.SecondaryWeapon);
            storage.TryAdd(knife);
            var economy = AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset");
            var traderConfig = AssetDatabase.LoadAssetAtPath<TraderConfig>("Assets/Game/ScriptableObjects/Base/TraderConfig.asset");
            var banked = new CoinWallet(CoinDomain.Banked, () => profile.BankedCoins, v => profile.BankedCoins = v);
            var trader = new TraderService(traderConfig, new PriceService(economy), banked, profile.Trader, profile.ProfileSeed, _registry.Definitions, id => _registry.TryGet(id, out var d) ? d : null);
            Assert.AreEqual(TradeError.Unsellable, trader.Sell(storage, knife.InstanceId));
            Assert.AreEqual(0, trader.QuoteSellValue(knife));
        }

        [Test]
        public void KitItems_FlowThroughNormalTransferPaths()
        {
            var profile = new PlayerProfile();
            _kit.GrantFirstProfileKit(profile);
            var inventory = Load(profile);
            var storage = new Storage(id => _registry.TryGet(id, out var d) ? d : null, _ammoBalance);
            var vest = inventory.GetEquipped(EquippedSlot.Armor);

            var result = new ItemTransferService().Transfer(new EquippedSlotContainer(inventory, EquippedSlot.Armor), vest.InstanceId, storage);
            Assert.IsTrue(result.Success, result.Error.ToString());
            Assert.AreSame(vest, storage.Find(vest.InstanceId));
            Assert.IsTrue(storage.Find(vest.InstanceId).IsUnsellable, "The flag travels with the instance.");
        }
    }
}

using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Progression;
using RuinRail.Networking;
using RuinRail.Persistence;
using RuinRail.UI.Base;
using RuinRail.UI.Inventory;
using UnityEditor;

namespace RuinRail.Tests.EditMode
{
    /// <summary>TASK 133 — Main Menu → profile/Base flow and the Shelter station panels over the existing services.</summary>
    public sealed class BaseUiTests
    {
        private ItemDefinitionRegistry _registry;
        private BaseConfigs _configs;

        [SetUp]
        public void SetUp()
        {
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(catalog);
            _configs = new BaseConfigs
            {
                Registry = _registry,
                AmmoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset"),
                Economy = AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset"),
                Trader = AssetDatabase.LoadAssetAtPath<TraderConfig>("Assets/Game/ScriptableObjects/Base/TraderConfig.asset"),
                Workshop = AssetDatabase.LoadAssetAtPath<WorkshopConfig>("Assets/Game/ScriptableObjects/Base/WorkshopConfig.asset")
            };
            Assert.IsNotNull(_configs.Economy);
            Assert.IsNotNull(_configs.Trader);
            Assert.IsNotNull(_configs.Workshop);
        }

        private ItemDefinition Resolve(string id) => _registry.TryGet(id, out var d) ? d : null;

        private (MainMenuViewModel menu, MemorySaveStore store, SaveSlotService saves) Menu()
        {
            var store = new MemorySaveStore();
            var saves = new SaveSlotService(store, Resolve);
            return (new MainMenuViewModel(saves, _configs), store, saves);
        }

        // ---- Acceptance 3: first / new / existing / recovered / unreadable save paths ----

        [Test]
        public void MainMenu_HasPlaySettingsQuit_AndPlayHandlesFirstExistingAndBrokenSaves()
        {
            // HELP is a Main Menu entry, between SETTINGS and QUIT: the manual is reachable before a run starts.
            CollectionAssert.AreEqual(new[] { "PLAY", "SETTINGS", "HELP", "QUIT" }, MainMenuViewModel.Entries.Select(MainMenuViewModel.Label));
            var (menu, store, saves) = Menu();
            Assert.IsFalse(menu.HasSave);
            menu.Select(MainMenuEntry.Settings);
            Assert.AreEqual(MainMenuState.Settings, menu.State);
            menu.BackToMenu();

            // First launch: a new profile with the starter kit, written as a safe point.
            Assert.AreEqual(PlayOutcome.NewProfile, menu.Play());
            Assert.AreEqual(MainMenuState.Base, menu.State);
            Assert.IsTrue(menu.Session.GrantedFirstKit);
            Assert.IsNotNull(menu.Session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon), "Starter kit equipped in the Base loadout.");
            Assert.IsTrue(store.Exists, "The new profile is saved before play continues.");
            Assert.AreEqual(PlayOutcome.NewProfile, menu.Play(), "Play again while in the Base is idempotent.");
            menu.Session.Banked.Credit(500, "test");
            menu.LeaveBase();
            Assert.AreEqual(MainMenuState.Menu, menu.State);
            Assert.IsTrue(menu.HasSave);

            // Existing save: continue with everything intact.
            var (again, _, _) = (new MainMenuViewModel(saves, _configs), store, saves);
            Assert.AreEqual(PlayOutcome.Continued, again.Play());
            Assert.AreEqual(500, again.Session.Profile.BankedCoins);
            Assert.IsFalse(again.Session.GrantedFirstKit, "The starter kit is granted once per profile.");
            Assert.IsNotNull(again.Session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon));
            again.LeaveBase();

            // Unreadable save: surfaced, never silently replaced; an explicit reset creates a new profile.
            store.Document = "{ this is not a save";
            store.Backup = "{ neither is this";
            store.Temp = null;
            var broken = new MainMenuViewModel(saves, _configs);
            Assert.AreEqual(PlayOutcome.Failed, broken.Play());
            Assert.AreEqual(MainMenuState.SaveError, broken.State);
            Assert.IsNull(broken.Session);
            StringAssert.Contains("could not be read", broken.Message);
            Assert.IsFalse(broken.ResetSave(confirmed: false), "No reset without confirmation.");
            Assert.AreEqual("{ this is not a save", store.Document, "Nothing overwritten until the player confirms.");
            Assert.IsTrue(broken.ResetSave(confirmed: true));
            Assert.AreEqual(MainMenuState.Base, broken.State);
            Assert.AreEqual(PlayOutcome.NewProfile, broken.LastOutcome);
            Assert.AreEqual(0, broken.Session.Profile.BankedCoins);
            broken.LeaveBase();
        }

        [Test]
        public void MainMenu_OpenExpeditionMarker_IsResolvedAsFailure_OnPlay()
        {
            var (menu, _, saves) = Menu();
            menu.Play();
            var session = menu.Session;
            session.Banked.Credit(200, "test");
            var transit = new TransitPanelViewModel(session, () => 7);
            Assert.IsTrue(session.Lobby.SetReady(BaseSession.LocalClientId, true));
            Assert.IsTrue(transit.StartExpedition(), transit.Feedback.Text);
            Assert.IsTrue(session.Expedition.IsExpeditionActive);
            Assert.IsTrue(saves.Load().Slot.ActiveExpedition.IsOpen, "The start marker is committed (113).");
            session.Dispose(); // "application closed" mid-run: no Return/Fail ever ran

            var relaunch = new MainMenuViewModel(saves, _configs);
            Assert.AreEqual(PlayOutcome.Continued, relaunch.Play());
            Assert.IsNotNull(relaunch.AbandonedExpedition, "Open marker resolved as failure on load (no mid-run resume).");
            StringAssert.Contains("counts as failed", relaunch.Message);
            Assert.AreEqual(200, relaunch.Session.Profile.BankedCoins, "Banked Coins are safe.");
            Assert.IsTrue(relaunch.Session.GrantedRescueKit || relaunch.Session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon) != null, "A startable loadout exists again (rescue kit if needed).");
            relaunch.LeaveBase();
        }

        // ---- Acceptance 1 + 2: every station reachable; transactions succeed/fail with feedback; reopening never duplicates ----

        [Test]
        public void EveryStation_IsReachable_AndTransactionsRunOnce_WithFeedback()
        {
            var (menu, _, _) = Menu();
            menu.Play();
            var session = menu.Session;
            session.Banked.Credit(5000, "test");
            using var hub = new BaseHubViewModel(session, null, () => 11);
            CollectionAssert.AreEqual(new[] { "STORAGE", "LOADOUT", "TRADER", "CHARACTER", "WORKSHOP", "MULTIPLAYER", "TRANSIT" }, BaseHubViewModel.Stations.Select(BaseHubViewModel.Label));
            foreach (var station in BaseHubViewModel.Stations)
            {
                hub.Open(station);
                Assert.AreEqual(station, hub.Current);
                hub.Close();
            }

            // Trader: buy once; reopening the station and buying the same offer again is refused, coins charged once.
            hub.Open(BaseStation.Trader);
            session.Trader.RefreshForEndedExpedition(1);
            var offer = hub.Trader.Offers.First(o => !o.IsSold);
            var before = session.Banked.Balance;
            Assert.IsTrue(hub.Trader.Buy(offer.Index), hub.Trader.Feedback.Text);
            Assert.AreEqual(before - offer.Price, session.Banked.Balance);
            StringAssert.Contains("Bought", hub.Trader.Feedback.Text);
            hub.Close();
            hub.Open(BaseStation.Trader);
            Assert.IsFalse(hub.Trader.Buy(offer.Index));
            Assert.AreEqual("Already sold.", hub.Trader.Feedback.Text);
            Assert.AreEqual(before - offer.Price, session.Banked.Balance, "Charged exactly once.");
            Assert.IsTrue(hub.Trader.Feedback.IsError);
            var bought = session.Loadout.BackpackSlots.First(i => i != null && i.DefinitionId == offer.Definition.Id);

            // Sell it back from the backpack through the service; starter gear cannot be sold.
            var quote = hub.Trader.QuoteSell(bought.InstanceId);
            Assert.Greater(quote, 0);
            Assert.IsTrue(hub.Trader.Sell(bought.InstanceId));
            Assert.AreEqual(before - offer.Price + quote, session.Banked.Balance);
            var starter = session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon);
            Assert.IsTrue(starter.IsUnsellable);
            var storage = hub.Storage;
            Assert.IsTrue(storage.Deposit(starter.InstanceId), storage.Feedback.Text);
            Assert.IsFalse(hub.Trader.Sell(starter.InstanceId));
            Assert.AreEqual("Starter gear cannot be sold.", hub.Trader.Feedback.Text);

            // Storage: withdraw back, filters/sort readable, feedback on failure.
            Assert.IsTrue(storage.Withdraw(starter.InstanceId), storage.Feedback.Text);
            Assert.IsFalse(storage.Withdraw(starter.InstanceId));
            Assert.AreEqual("That item is no longer here.", storage.Feedback.Text);
            storage.Filter = ItemCategory.Weapon;
            Assert.IsTrue(storage.Items.All(i => Resolve(i.DefinitionId).Category == ItemCategory.Weapon));

            // Loadout: equip from storage straight into the primary slot (after storing the starter again), feedback on a bad slot.
            hub.Open(BaseStation.Loadout);
            Assert.IsTrue(hub.Loadout.Inventory.IsOpen);
            var starterIndex = session.Loadout.BackpackSlots.ToList().FindIndex(i => i != null && i.InstanceId == starter.InstanceId);
            Assert.GreaterOrEqual(starterIndex, 0, "Withdrawn into the backpack.");
            Assert.AreEqual(InventoryActionResult.Done, hub.Loadout.Inventory.Equip(new InventorySlotRef(InventorySlotKind.Backpack, starterIndex)));
            Assert.AreEqual(starter.InstanceId, session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon).InstanceId);
            Assert.IsTrue(hub.Loadout.StoreFromLoadout(EquippedSlot.PrimaryWeapon), hub.Loadout.Feedback.Text);
            Assert.IsNull(session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon));
            Assert.IsFalse(hub.Loadout.EquipFromStorage(starter.InstanceId, EquippedSlot.Armor));
            Assert.IsTrue(hub.Loadout.Feedback.IsError);
            Assert.IsTrue(hub.Loadout.EquipFromStorage(starter.InstanceId, EquippedSlot.PrimaryWeapon), hub.Loadout.Feedback.Text);
            Assert.AreEqual(starter.InstanceId, session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon).InstanceId);
            hub.Close();
            Assert.IsFalse(hub.Loadout.Inventory.IsOpen);

            // Workshop: storage upgrade once; a second buy at insufficient funds is refused with feedback.
            hub.Open(BaseStation.Workshop);
            var capacity = hub.Workshop.StorageCapacity;
            var cost = hub.Workshop.NextStorageUpgradeCost;
            var coins = session.Banked.Balance;
            Assert.IsTrue(hub.Workshop.BuyStorageUpgrade(), hub.Workshop.Feedback.Text);
            Assert.AreEqual(coins - cost, session.Banked.Balance);
            Assert.Greater(hub.Workshop.StorageCapacity, capacity);
            Assert.AreEqual(hub.Workshop.StorageCapacity, session.Storage.Capacity);
            session.Banked.Debit(session.Banked.Balance, "drain");
            Assert.IsFalse(hub.Workshop.BuyStorageUpgrade());
            Assert.AreEqual("Not enough Coins.", hub.Workshop.Feedback.Text);

            // Character: sheet, allocation, respec feedback.
            hub.Open(BaseStation.Character);
            Assert.AreEqual(6, CharacterPanelViewModel.Attributes.Length, "Six attributes.");
            Assert.IsFalse(hub.Character.Allocate(SkillId.Vitality));
            Assert.AreEqual("No skill points to spend.", hub.Character.Feedback.Text);
            session.Progression.AddXp(100000);
            Assert.Greater(hub.Character.Sheet.UnspentPoints, 0);
            Assert.IsTrue(hub.Character.Allocate(SkillId.Vitality), hub.Character.Feedback.Text);
            Assert.AreEqual(1, hub.Character.Sheet.Ranks[SkillId.Vitality]);
            Assert.IsFalse(hub.Character.Respec());
            Assert.AreEqual("Not enough Coins for a respec.", hub.Character.Feedback.Text);
            session.Banked.Credit(hub.Character.Sheet.RespecPrice, "test");
            Assert.IsTrue(hub.Character.Respec());
            Assert.AreEqual(0, hub.Character.Sheet.Ranks[SkillId.Vitality]);

            // Multiplayer + Transit: ready state, then a start that cannot be started twice.
            hub.Open(BaseStation.Multiplayer);
            Assert.IsTrue(hub.Multiplayer.SetReady(true));
            Assert.IsTrue(hub.Multiplayer.AllReady);
            hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Transit.CanStart);
            Assert.IsTrue(hub.Transit.StartExpedition(), hub.Transit.Feedback.Text);
            Assert.IsTrue(session.Expedition.IsExpeditionActive);
            Assert.AreEqual(1, session.Expedition.State.StartingPartySize);
            Assert.IsFalse(hub.Transit.StartExpedition(), "Reopening/re-pressing cannot start a second expedition.");
            Assert.IsFalse(hub.Transit.CanStart);
            menu.LeaveBase();
        }

        // ---- Req 4: loadout changes invalidate Ready; summary after a run ----

        [Test]
        public void LoadoutChange_ClearsReady_AndTheSummaryFollowsTheClosedTransaction()
        {
            var (menu, _, _) = Menu();
            menu.Play();
            var session = menu.Session;
            using var hub = new BaseHubViewModel(session, null, () => 3);
            Assert.IsTrue(hub.Multiplayer.SetReady(true));
            Assert.IsTrue(hub.Multiplayer.LocalReady);
            hub.Open(BaseStation.Loadout);
            var inventory = hub.Loadout.Inventory;
            Assert.AreEqual(InventoryActionResult.Done, inventory.MoveTo(new InventorySlotRef(InventorySlotKind.Equipped, (int)EquippedSlot.PrimaryWeapon), new InventorySlotRef(InventorySlotKind.Equipped, (int)EquippedSlot.SecondaryWeapon)));
            Assert.IsFalse(hub.Multiplayer.LocalReady, "81: changing the loadout after Ready clears Ready.");
            Assert.IsTrue(hub.Multiplayer.SetReady(true));

            ExpeditionSummaryViewModel summary = null;
            hub.SummaryReady += s => summary = s;
            Assert.IsTrue(hub.Transit.StartExpedition(), hub.Transit.Feedback.Text);
            session.Expedition.AddCarriedCoins(90);
            session.Expedition.RecordEnemyDefeated(30);
            session.Expedition.RecordBossDefeated(650);
            session.Expedition.Return();
            Assert.IsNotNull(summary);
            Assert.AreEqual("EXTRACTION SUCCESSFUL", summary.Title);
            CollectionAssert.Contains(summary.Lines, "Coins Extracted: 90");
            CollectionAssert.Contains(summary.Lines, "XP Earned: 680 (kept)");
            CollectionAssert.Contains(summary.Lines, "Bosses Defeated: 1");
            Assert.IsTrue(summary.Lines.Any(l => l.StartsWith("  ") && l.Contains("[COMMON]")), "Extracted item list with rarity text.");
            Assert.IsNotNull(session.Loadout.GetEquipped(EquippedSlot.SecondaryWeapon), "Back at the Base the secured loadout is the Base loadout again.");

            var failed = new ExpeditionSummaryViewModel(session.Expedition.LastSummary, Resolve);
            Assert.AreEqual(session.Expedition.LastSummary, failed.Summary);
            menu.LeaveBase();
        }

        // ---- The loop continues: after a run ends the party reopens and the next expedition can start ----

        [Test]
        public void AfterARunEnds_ThePartyReopens_ReadyIsAskedAgain_AndASecondExpeditionStartsFromTheCurrentLoadout()
        {
            var (menu, _, _) = Menu();
            menu.Play();
            var session = menu.Session;
            using var hub = new BaseHubViewModel(session, null, () => 5);
            Assert.IsTrue(hub.Multiplayer.SetReady(true));
            Assert.IsTrue(hub.Transit.StartExpedition(), hub.Transit.Feedback.Text);
            session.Expedition.AddCarriedCoins(10);
            session.Expedition.Return();
            Assert.IsFalse(session.Expedition.IsExpeditionActive);

            Assert.IsFalse(session.Lobby.HasStarted, "the party reopened when the run ended");
            Assert.IsFalse(hub.Multiplayer.LocalReady, "81: a new run needs a new Ready");
            var member = session.Lobby.Get(BaseSession.LocalClientId);
            Assert.AreEqual(LoadoutValidation.Fingerprint(session.Loadout.ToSnapshot()), member.LoadoutFingerprint, "the lobby holds the loadout the player actually has now, not a stale pre-run copy");
            Assert.IsTrue(hub.Multiplayer.SetReady(true), hub.Multiplayer.Feedback.Text);
            Assert.IsTrue(hub.Transit.StartExpedition(), hub.Transit.Feedback.Text);
            Assert.IsTrue(session.Expedition.IsExpeditionActive, "second expedition running in the same session");
            Assert.AreEqual(10, session.Profile.BankedCoins, "the first run's extraction stayed banked");
            session.Expedition.Fail();
            Assert.IsFalse(session.Lobby.HasStarted, "a failed run reopens the party too");
            menu.LeaveBase();
        }

        // ---- Shelter stash: bring loot home and put it away ----

        /// <summary>Every item id the survivor and Storage hold, with its owner (a duplicate would appear twice).</summary>
        private static System.Collections.Generic.List<string> Owned(BaseSession session) =>
            session.Loadout.BackpackSlots.Where(i => i != null).Select(i => i.InstanceId)
                .Concat(System.Enum.GetValues(typeof(EquippedSlot)).Cast<EquippedSlot>().Select(session.Loadout.GetEquipped).Where(i => i != null).Select(i => i.InstanceId))
                .Concat(session.Storage.Items.Select(i => i.InstanceId)).ToList();

        /// <summary>
        /// What the survivor and Storage hold together: unique items by instance id, stackables by total quantity per
        /// definition (Storage re-cuts a stack into its own instances when it accepts it, so a stack's id may change).
        /// </summary>
        private (System.Collections.Generic.List<string> Unique, System.Collections.Generic.Dictionary<string, int> Stacks) Holdings(BaseSession session)
        {
            var all = session.Loadout.BackpackSlots.Where(i => i != null)
                .Concat(System.Enum.GetValues(typeof(EquippedSlot)).Cast<EquippedSlot>().Select(session.Loadout.GetEquipped).Where(i => i != null))
                .Concat(session.Storage.Items).ToList();
            var unique = all.Where(i => !Resolve(i.DefinitionId).IsStackable).Select(i => i.InstanceId).OrderBy(x => x).ToList();
            var stacks = all.Where(i => Resolve(i.DefinitionId).IsStackable).GroupBy(i => i.DefinitionId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
            return (unique, stacks);
        }

        private static InventorySlotRef CellOf(StashViewModel stash, string instanceId)
        {
            for (var i = 0; i < StashViewModel.EquippedSlots; i++)
                if (stash.ItemAt(new InventorySlotRef(InventorySlotKind.Equipped, i))?.InstanceId == instanceId) return new InventorySlotRef(InventorySlotKind.Equipped, i);
            for (var i = 0; i < PlayerInventory.BackpackCapacity; i++)
                if (stash.ItemAt(new InventorySlotRef(InventorySlotKind.Backpack, i))?.InstanceId == instanceId) return new InventorySlotRef(InventorySlotKind.Backpack, i);
            for (var page = 0; page < stash.PageCount; page++)
            {
                while (stash.Page < page) stash.NextPage();
                for (var i = 0; i < StashViewModel.PageSize; i++)
                    if (stash.ItemAt(StashViewModel.StorageCell(i))?.InstanceId == instanceId) return StashViewModel.StorageCell(i);
            }

            Assert.Fail($"{instanceId} is nowhere in the stash");
            return default;
        }

        [Test]
        public void Stash_AfterReturningAlive_MovesWornAndBackpackLootIntoStorage_WithoutLossOrDuplication_ExplainsBlockedMoves_AndPersists()
        {
            var (menu, store, saves) = Menu();
            menu.Play();
            var session = menu.Session;
            using var hub = new BaseHubViewModel(session, null, () => 5);
            Assert.IsTrue(hub.Multiplayer.SetReady(true));
            Assert.IsTrue(hub.Transit.StartExpedition(), hub.Transit.Feedback.Text);

            // Loot picked up on the run: carried at risk while the expedition lasts.
            var carried = session.Expedition.State.Inventory;
            var smg = new ItemInstance("weapon_rattler_9", 1, Rarity.Rare);
            var harness = new ItemInstance("armor_combat_harness", 1, Rarity.Uncommon);
            var stim = new ItemInstance("consumable_combat_stim", 2);
            Assert.IsTrue(carried.TryAddToBackpack(smg) && carried.TryAddToBackpack(harness) && carried.TryAddToBackpack(stim));
            Assert.IsTrue(smg.IsAtRisk, "run loot is at risk while carried");

            // 1. Returning alive secures it: it is on the survivor, no longer at risk, and the Shelter points at Storage.
            session.Expedition.Return();
            foreach (var item in new[] { smg, harness, stim })
                Assert.IsTrue(session.Loadout.Contains(item.InstanceId) || Resolve(item.DefinitionId).IsStackable && session.Loadout.BackpackSlots.Any(i => i?.DefinitionId == item.DefinitionId),
                    $"{item.DefinitionId} {item.InstanceId} came home (loadout: {string.Join(", ", Owned(session).Select(id => id.Substring(0, 6)))}; backpack defs: {string.Join(", ", session.Loadout.BackpackSlots.Where(i => i != null).Select(i => i.DefinitionId + "/" + i.InstanceId.Substring(0, 6)))})");
            Assert.IsTrue(session.HasLootToStash, "the Shelter points at the stash after a successful return with loot");

            using var stash = new StashViewModel(session, hub.Storage, hub.Loadout);
            var owned = Holdings(session);
            CollectionAssert.AllItemsAreUnique(owned.Unique);

            // 2. A backpack item: STORE, and it moves (not copied).
            var smgCell = CellOf(stash, smg.InstanceId);
            Assert.AreEqual(StashAction.Store, stash.IntentFor(smgCell).Action);
            Assert.IsTrue(stash.Activate(smgCell), stash.Message);
            Assert.IsFalse(session.Loadout.Contains(smg.InstanceId));
            Assert.IsTrue(session.Storage.Items.Any(i => i.InstanceId == smg.InstanceId));

            // 3. Worn gear: the equipped weapon and a worn armour go into Storage too (existing rule: allowed).
            var primary = session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon);
            Assert.IsNotNull(primary);
            var wornCell = new InventorySlotRef(InventorySlotKind.Equipped, (int)EquippedSlot.PrimaryWeapon);
            Assert.AreEqual(StashAction.Store, stash.IntentFor(wornCell).Action);
            Assert.IsTrue(stash.Activate(wornCell), stash.Message);
            Assert.IsNull(session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon), "the slot is empty, not a ghost");
            Assert.IsTrue(session.Storage.Items.Any(i => i.InstanceId == primary.InstanceId));

            // 4. Take back into the backpack, and drop a stored weapon straight onto the worn slot to equip it.
            var takeCell = CellOf(stash, smg.InstanceId);
            Assert.AreEqual(StashAction.Take, stash.IntentFor(takeCell).Action);
            Assert.IsTrue(stash.Activate(takeCell), stash.Message);
            Assert.IsTrue(session.Loadout.BackpackSlots.Any(i => i?.InstanceId == smg.InstanceId));
            Assert.IsTrue(stash.Drop(CellOf(stash, primary.InstanceId), wornCell), stash.Message);
            Assert.AreEqual(primary.InstanceId, session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon)?.InstanceId, "equipped straight from Storage");

            // 5. STORE WHOLE BACKPACK: everything carried in the bag goes in at once.
            var bagBefore = session.Loadout.BackpackSlots.Count(i => i != null);
            Assert.Greater(bagBefore, 0);
            Assert.AreEqual(bagBefore, stash.StoreBackpack(), stash.Message);
            Assert.AreEqual(0, session.Loadout.BackpackSlots.Count(i => i != null));
            Assert.IsFalse(session.HasLootToStash, "all secured loot is put away: no more pointer");

            // No loss, no duplication across every move above: the same unique items, the same stack totals.
            var after = Holdings(session);
            CollectionAssert.AllItemsAreUnique(after.Unique);
            CollectionAssert.AreEqual(owned.Unique, after.Unique, "every unique item still exists exactly once");
            CollectionAssert.AreEquivalent(owned.Stacks, after.Stacks, "every stack total is unchanged");
            Assert.AreEqual(2, session.Storage.Items.Where(i => i.DefinitionId == "consumable_combat_stim").Sum(i => i.Quantity), "the stim stack arrived whole");

            // 6. Blocked moves explain themselves and change nothing: a full backpack refuses TAKE ...
            for (var i = 0; i < PlayerInventory.BackpackCapacity; i++) Assert.IsTrue(session.Loadout.TryAddToBackpack(new ItemInstance("weapon_p9_ranger")));
            var anyStored = StashViewModel.StorageCell(0);
            while (stash.Page > 0) stash.PrevPage();
            var refused = stash.IntentFor(anyStored);
            Assert.AreEqual(StashAction.Blocked, refused.Action);
            Assert.AreEqual("BACKPACK FULL", refused.Label);
            Assert.IsNotEmpty(refused.Reason);
            var storedBefore = session.Storage.Items.Count();
            Assert.IsFalse(stash.Activate(anyStored));
            Assert.AreEqual(storedBefore, session.Storage.Items.Count(), "a refused take moves nothing");
            Assert.IsTrue(stash.MessageIsError);

            // ... and a full Storage refuses STORE.
            while (session.Storage.Items.Count() < session.Storage.Capacity) Assert.IsTrue(session.Storage.TryAdd(new ItemInstance("weapon_kestrel_12")));
            Assert.IsTrue(stash.StorageFull);
            var bagCell = new InventorySlotRef(InventorySlotKind.Backpack, 0);
            var full = stash.IntentFor(bagCell);
            Assert.AreEqual(StashAction.Blocked, full.Action);
            Assert.AreEqual("STORAGE FULL", full.Label);
            var bagItem = stash.ItemAt(bagCell).InstanceId;
            Assert.IsFalse(stash.Activate(bagCell));
            Assert.IsTrue(session.Loadout.Contains(bagItem), "a refused store leaves the item on the survivor");
            CollectionAssert.AllItemsAreUnique(Holdings(session).Unique);

            // 7. Persistence: the Shelter's own safe point, leave, continue: Storage and the loadout come back as left.
            var expectedStorage = session.Storage.Items.Select(i => i.InstanceId).OrderBy(x => x).ToList();
            var expectedLoadout = Owned(session).Except(expectedStorage).OrderBy(x => x).ToList();
            menu.LeaveBase();
            var again = new MainMenuViewModel(saves, _configs);
            Assert.AreEqual(PlayOutcome.Continued, again.Play());
            CollectionAssert.AreEqual(expectedStorage, again.Session.Storage.Items.Select(i => i.InstanceId).OrderBy(x => x).ToList(), "Storage persisted");
            CollectionAssert.AreEqual(expectedLoadout, Owned(again.Session).Except(expectedStorage).OrderBy(x => x).ToList(), "the survivor persisted");
            Assert.AreEqual(primary.InstanceId, again.Session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon)?.InstanceId, "the re-equipped weapon is still worn");
            again.LeaveBase();
        }

        // ---- Coins for the run (77): chosen at Transit, moved exactly once by the start transaction ----

        [Test]
        public void CoinsForTheRun_AreChosenAtTransit_ClampedToTheBank_MovedOnceAtStart_AndCancellingMovesNothing()
        {
            var (menu, _, saves) = Menu();
            menu.Play();
            var session = menu.Session;
            session.Banked.Credit(1000, "test");
            using (var hub = new BaseHubViewModel(session, null, () => 11))
            {
                var transit = hub.Transit;
                var step = transit.CoinStep;
                Assert.Greater(step, 0);
                Assert.AreEqual(0, transit.CoinsToCarry, "nothing is taken unless the player chooses it");
                Assert.AreEqual(1000, transit.BankedAfterDeparture);

                Assert.AreEqual(step, transit.TakeMore());
                Assert.AreEqual(step * 2, transit.TakeMore());
                Assert.AreEqual(step, transit.TakeLess());
                Assert.AreEqual(1000, transit.TakeAll());
                Assert.IsFalse(transit.CanTakeMore);
                Assert.AreEqual(1000, transit.TakeMore(), "never above the bank");
                Assert.AreEqual(0, transit.BankedAfterDeparture);
                Assert.AreEqual((1000 - 1) / step * step, transit.TakeLess(), "from ALL one step down lands on the step grid");
                Assert.AreEqual(0, transit.TakeNone());
                Assert.IsFalse(transit.CanTakeLess);
                Assert.AreEqual(0, transit.TakeLess(), "never below zero");
                Assert.AreEqual(0, session.SetCoinsToCarry(-25));
                Assert.AreEqual(1000, session.SetCoinsToCarry(99999));
                Assert.AreEqual(1000, session.Profile.BankedCoins, "choosing moves nothing");
                Assert.AreEqual(1000, session.Lobby.Get(BaseSession.LocalClientId).CarriedCoins, "the lobby holds the choice it captures at start");

                // Spending in the Shelter lowers the bank under the choice: the choice (and the lobby) follow it down.
                Assert.IsTrue(session.Banked.Debit(400, "test_spend").Success);
                Assert.AreEqual(600, transit.CoinsToCarry);
                Assert.AreEqual(600, session.Lobby.Get(BaseSession.LocalClientId).CarriedCoins);
                Assert.AreEqual(0, transit.BankedAfterDeparture);

                // Cancelling preparation: change the choice, close the station — the bank is untouched.
                transit.TakeNone();
                transit.TakeMore();
                transit.TakeMore();
                hub.Open(BaseStation.Transit);
                hub.Close();
                Assert.AreEqual(600, session.Profile.BankedCoins);
                Assert.AreEqual(step * 2, transit.CoinsToCarry);
            }

            // Leaving the Shelter abandons the choice without moving a coin; it is never saved.
            menu.LeaveBase();
            var again = new MainMenuViewModel(saves, _configs);
            Assert.AreEqual(PlayOutcome.Continued, again.Play());
            session = again.Session;
            Assert.AreEqual(600, session.Profile.BankedCoins);
            Assert.AreEqual(0, session.CoinsToCarry, "a new visit starts with nothing taken");

            using (var hub = new BaseHubViewModel(session, null, () => 11))
            {
                var transit = hub.Transit;
                transit.TakeNone();
                session.SetCoinsToCarry(250);
                Assert.IsTrue(session.Lobby.SetReady(BaseSession.LocalClientId, true));
                Assert.IsTrue(transit.StartExpedition(), transit.Feedback.Text);
                StringAssert.Contains("250 C taken", transit.Feedback.Text);
                var state = session.Expedition.State;
                Assert.AreEqual(250, state.CarriedCoins, "exactly the chosen amount became Carried Coins");
                Assert.AreEqual(350, session.Profile.BankedCoins);
                Assert.AreEqual(0, session.CoinsToCarry, "the choice was consumed by that start");
                var onDisk = saves.Load().Slot;
                Assert.IsTrue(onDisk.ActiveExpedition.IsOpen);
                Assert.AreEqual(350, onDisk.Profile.BankedCoins, "the debit is saved together with the open run");

                Assert.IsFalse(transit.StartExpedition(), "a second start is refused");
                Assert.AreEqual(350, session.Profile.BankedCoins, "and moves nothing");
                Assert.AreEqual(250, state.CarriedCoins);

                session.Expedition.Return();
                Assert.AreEqual(600, session.Profile.BankedCoins, "returning alive banks the taken coins again, once");
                Assert.AreEqual(600, saves.Load().Slot.Profile.BankedCoins);
            }

            again.LeaveBase();
        }

        [Test]
        public void CoopLobby_CapturesEachMembersCoins_OnceAtStart_AndTheRunStartCarriesThem()
        {
            var lobby = new PartyLobby(0, Resolve);
            lobby.Join(0, "host");
            lobby.Join(7, "client");
            var loadout = new InventorySnapshot { Equipped = new[] { new InventorySnapshot.Entry { Slot = (int)EquippedSlot.PrimaryWeapon, Item = new ItemInstance("weapon_p9_ranger").ToSnapshot() } } };
            foreach (var id in new ulong[] { 0, 7 }) { lobby.SetLoadout(id, loadout); lobby.SetReady(id, true); }
            Assert.IsTrue(lobby.SetCarriedCoins(0, 120));
            Assert.IsTrue(lobby.SetCarriedCoins(7, -40), "a negative request is stored as zero");
            Assert.AreEqual(0, lobby.Get(7).CarriedCoins);
            Assert.IsTrue(lobby.SetCarriedCoins(7, 300));
            Assert.IsTrue(lobby.Get(7).IsReady, "changing the coins keeps Ready: the start captures the current value");
            Assert.AreEqual(LobbyStartError.None, lobby.TryStart(0, 5, out var snapshot));
            Assert.AreEqual(120, snapshot.CarriedCoinsOf(0));
            Assert.AreEqual(300, snapshot.CarriedCoinsOf(7));
            Assert.IsFalse(lobby.SetCarriedCoins(7, 999), "locked once the expedition started");
            Assert.AreEqual(300, lobby.StartSnapshot.CarriedCoinsOf(7));
        }

        // ---- Acceptance 4 + Req 5: no economy constants in UI; English text ----

        [Test]
        public void BaseUiScripts_ContainNoBusinessConstants_AndOnlyEnglishText()
        {
            var sources = Directory.GetFiles("Assets/Game/Scripts/UI/Base", "*.cs").Select(File.ReadAllText).ToList();
            Assert.IsTrue(sources.Count >= 3);
            foreach (var source in sources)
            {
                Assert.IsFalse(Regex.IsMatch(source, @"const\s+(int|float|double)\s+\w*(Price|Cost|Coin|Xp|Level|Percent|Weight|Cap)\w*\s*="), "No economy/progression constant in UI scripts.");
                Assert.IsFalse(Regex.IsMatch(source, @"(Price|Cost)\w*\s*=\s*\d+"), "Prices/costs always come from the services.");
                Assert.IsFalse(Regex.IsMatch(source, @"[äöüßÄÖÜ]"), "Player-facing text is English.");
                Assert.IsFalse(Regex.IsMatch(source, @"GearScore|PowerScore"), "No score abstraction.");
            }
        }
    }
}

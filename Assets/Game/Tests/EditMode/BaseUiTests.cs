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
            CollectionAssert.AreEqual(new[] { "PLAY", "SETTINGS", "QUIT" }, MainMenuViewModel.Entries.Select(MainMenuViewModel.Label));
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

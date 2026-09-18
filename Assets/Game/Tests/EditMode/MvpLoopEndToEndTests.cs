using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.EditorTools.Rooms;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Progression;
using RuinRail.Networking;
using RuinRail.Persistence;
using RuinRail.UI.Base;
using RuinRail.UI.Onboarding;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests.EditMode
{
    /// <summary>
    /// TASK 146 — the complete MVP loop end to end through the shipped view models and persistence: first profile →
    /// onboarding → every Shelter station → solo expedition start → three seeded depths across the biomes (generation
    /// + rooms/enemies/boss records + transit) → Return → summary → save → relaunch; plus the quit/failure branch.
    /// Deterministic (fixed seeds, in-memory store).
    /// </summary>
    public sealed class MvpLoopEndToEndTests
    {
        public const string ReportPath = "TestResults/mvp_loop_e2e.md";

        private BaseConfigs _configs;
        private DisplayNamePolicy _policy;
        private BiomeRoomPools _pools;
        private DungeonGraphRules _rules;

        [SetUp]
        public void SetUp()
        {
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _configs = new BaseConfigs
            {
                Registry = ItemDefinitionRegistry.Build(catalog),
                AmmoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset"),
                Economy = AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset"),
                Trader = AssetDatabase.LoadAssetAtPath<TraderConfig>("Assets/Game/ScriptableObjects/Base/TraderConfig.asset"),
                Workshop = AssetDatabase.LoadAssetAtPath<WorkshopConfig>("Assets/Game/ScriptableObjects/Base/WorkshopConfig.asset")
            };
            _policy = AssetDatabase.LoadAssetAtPath<DisplayNamePolicy>("Assets/Game/ScriptableObjects/Player/DisplayNamePolicy.asset");
            _pools = BiomeRoomPools.Build(RoomValidationTools.LoadAllRoomDefinitions().Where(d => !AssetDatabase.GetAssetPath(d).Contains("/_Test/")));
            Assert.IsTrue(_pools.IsComplete);
            _rules = DungeonGraphRules.CreateDefault();
            FakeMultiplayerServices.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            if (_rules != null) Object.DestroyImmediate(_rules);
        }

        private ItemDefinition Resolve(string id) => _configs.Resolve(id);

        private (MainMenuViewModel menu, SaveSlotService saves) Launch(MemorySaveStore store)
        {
            var saves = new SaveSlotService(store, Resolve);
            return (new MainMenuViewModel(saves, _configs), saves);
        }

        /// <summary>Simulates one depth: host generation for the selected biome, rooms cleared with enemies, the boss, and the transit choice.</summary>
        private void PlayDepth(ExpeditionService expedition, TransitChoice choice, System.Text.StringBuilder log)
        {
            var state = expedition.State;
            var (payload, generation) = DungeonSync.HostGenerate(new DungeonGraphGenerator(_rules), _pools, state.RunSeed, state.Depth, state.Biome);
            Assert.IsTrue(generation.Success && payload.IsValid, $"depth {state.Depth} {state.Biome}");
            Assert.AreEqual((int)state.Biome, payload.Biome);
            var rooms = generation.Layout.Placements.Count;
            for (var r = 0; r < rooms; r++)
            {
                for (var e = 0; e < 3; e++) expedition.RecordEnemyDefeated(10);
                expedition.RecordRoomCleared();
            }

            expedition.AddCarriedCoins(120);
            state.Inventory.TryAddToBackpack(new ItemInstance("ammo_light", 20));
            var decision = expedition.RecordBossDefeated(500);
            Assert.IsNotNull(decision);
            log.AppendLine($"- depth {state.Depth} {state.Biome}: {rooms} rooms generated (layout {payload.LayoutFingerprint}), {rooms * 3} enemies, boss defeated → {choice}");
            Assert.IsTrue(expedition.ChooseTransit(choice), "Solo vote resolves immediately.");
        }

        [Test]
        public void FreshProfile_Onboarding_AllStations_ThreeBiomeExpedition_Return_Save_Relaunch()
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine("# MVP loop end-to-end (TASK 146)");
            var store = new MemorySaveStore();
            var (menu, saves) = Launch(store);

            // 1. First profile and onboarding.
            Assert.AreEqual(PlayOutcome.NewProfile, menu.Play());
            var session = menu.Session;
            using var onboarding = new ShelterOnboardingViewModel(session, _policy);
            Assert.AreEqual(ShelterOnboardingStep.ChooseDisplayName, onboarding.Step);
            Assert.IsTrue(onboarding.SubmitDisplayName("Rail Ghost"));
            onboarding.AcknowledgeStarterKit();
            Assert.AreEqual(ShelterOnboardingStep.StartFirstExpedition, onboarding.Step, "Starter kit equipped → ready to start.");
            var pistolId = session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon).InstanceId;
            log.AppendLine("- new profile 'Rail Ghost', starter kit granted once, onboarding at 'start first expedition'");

            // 2. Every Shelter station through the hub view model.
            session.Banked.Credit(5000, "test");
            session.Progression.AddXp(LevelCurve.TotalXpForLevel(4));
            var terminal = new MultiplayerTerminalService(new NetworkSessionController(new FakeMultiplayerServices(), new FakeNetworkDriver()));
            using var hub = new BaseHubViewModel(session, terminal, () => 2026);
            hub.Open(BaseStation.Storage);
            var vest = session.Loadout.GetEquipped(EquippedSlot.Armor);
            Assert.IsTrue(hub.Storage.Deposit(vest.InstanceId), hub.Storage.Feedback.Text);
            Assert.IsNull(session.Loadout.GetEquipped(EquippedSlot.Armor));
            Assert.IsTrue(hub.Loadout.EquipFromStorage(vest.InstanceId, EquippedSlot.Armor), hub.Loadout.Feedback.Text);
            Assert.AreEqual(vest.InstanceId, session.Loadout.GetEquipped(EquippedSlot.Armor).InstanceId, "Non-stackable instance keeps its id through storage.");
            hub.Open(BaseStation.Trader);
            var offer = hub.Trader.Offers.First(o => o.Definition != null);
            var bankedBefore = session.Banked.Balance;
            Assert.IsTrue(hub.Trader.Buy(offer.Index), hub.Trader.Feedback.Text);
            Assert.AreEqual(bankedBefore - offer.Price, session.Banked.Balance);
            if (offer.Definition is EquipmentItemDefinition) Assert.IsTrue(hub.Trader.Sell(offer.Item.InstanceId), hub.Trader.Feedback.Text);
            hub.Open(BaseStation.Workshop);
            var capacityBefore = session.Workshop.StorageCapacity;
            Assert.IsTrue(hub.Workshop.BuyStorageUpgrade(), hub.Workshop.Feedback.Text);
            Assert.Greater(session.Workshop.StorageCapacity, capacityBefore);
            hub.Open(BaseStation.Character);
            var pointsBefore = session.Progression.UnspentSkillPoints;
            Assert.Greater(pointsBefore, 0);
            Assert.IsTrue(hub.Character.Allocate(SkillId.Vitality), hub.Character.Feedback.Text);
            Assert.AreEqual(pointsBefore - 1, session.Progression.UnspentSkillPoints);
            hub.Open(BaseStation.Multiplayer);
            Assert.IsTrue(hub.Multiplayer.SetReady(true));
            log.AppendLine($"- stations: storage deposit/withdraw, trader buy {offer.Price}/sell, workshop storage {capacityBefore}→{session.Workshop.StorageCapacity}, character +1 Vitality, multiplayer ready");

            // 3. Solo expedition across three seeded depths.
            hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Transit.StartExpedition(), hub.Transit.Feedback.Text);
            Assert.IsTrue(session.Expedition.IsExpeditionActive);
            Assert.AreEqual(ShelterOnboardingStep.Complete, onboarding.Step);
            var expected = BiomeSelector.Sequence(2026, 3);
            var expedition = session.Expedition;
            Assert.AreEqual(expected[0], expedition.State.Biome);
            var bankedAtStart = session.Profile.BankedCoins;
            var xpAtStart = session.Profile.TotalXp;
            PlayDepth(expedition, TransitChoice.DescendDeeper, log);
            Assert.AreEqual(2, expedition.State.Depth);
            Assert.AreEqual(expected[1], expedition.State.Biome);
            PlayDepth(expedition, TransitChoice.DescendDeeper, log);
            Assert.AreEqual(expected[2], expedition.State.Biome);
            PlayDepth(expedition, TransitChoice.ReturnToShelter, log);
            Assert.IsFalse(expedition.IsExpeditionActive);
            var summary = expedition.LastSummary;
            Assert.IsTrue(summary.IsSuccess);
            Assert.AreEqual(3, summary.DepthReached);
            Assert.AreEqual(3, summary.BossesDefeated);
            CollectionAssert.AreEqual(expected, summary.Biomes);
            Assert.AreEqual(bankedAtStart + 360, session.Profile.BankedCoins, "Three depths × 120 carried coins banked once.");
            Assert.Greater(session.Profile.TotalXp, xpAtStart);
            Assert.IsNotNull(hub.LastSummary);
            Assert.IsTrue(hub.LastSummary.Lines.Any(l => l.Contains("360")), "Summary shows the banked coins.");
            var secured = session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon);
            Assert.IsNotNull(secured);
            Assert.AreEqual(pistolId, secured.InstanceId, "The pistol that left is the pistol that came back.");
            log.AppendLine($"- Return: depth {summary.DepthReached}, {summary.BossesDefeated} bosses, +360 coins, XP {xpAtStart}→{session.Profile.TotalXp}, pistol {pistolId} secured");

            // 4. Save and relaunch: everything exact, onboarding complete, no open marker.
            Assert.AreEqual(SaveError.None, session.SaveNow("e2e"));
            menu.LeaveBase();
            var (again, _) = Launch(store);
            Assert.AreEqual(PlayOutcome.Continued, again.Play());
            var reloaded = again.Session;
            Assert.AreEqual("Rail Ghost", reloaded.Profile.DisplayName);
            Assert.AreEqual(session.Profile.BankedCoins, reloaded.Profile.BankedCoins);
            Assert.AreEqual(session.Profile.TotalXp, reloaded.Profile.TotalXp);
            Assert.AreEqual(1, reloaded.Profile.Skills.Vitality);
            Assert.AreEqual(session.Workshop.StorageCapacity, reloaded.Workshop.StorageCapacity);
            Assert.AreEqual(pistolId, reloaded.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon).InstanceId);
            Assert.IsFalse(reloaded.Slot.ActiveExpedition.IsOpen);
            using var relaunchedOnboarding = new ShelterOnboardingViewModel(reloaded, _policy);
            Assert.AreEqual(ShelterOnboardingStep.Complete, relaunchedOnboarding.Step);
            Assert.IsNull(again.AbandonedExpedition);
            log.AppendLine("- relaunch: Continued, name/coins/XP/skills/storage capacity/loadout identity exact, onboarding complete");
            again.LeaveBase();

            Directory.CreateDirectory("TestResults");
            File.WriteAllText(ReportPath, log.ToString());
        }

        [Test]
        public void Expedition_FailureAndQuit_LoseCarriedState_KeepPermanentState_AndRelaunchCleanly()
        {
            var store = new MemorySaveStore();
            var (menu, _) = Launch(store);
            menu.Play();
            var session = menu.Session;
            session.Banked.Credit(300, "test");
            var pistolId = session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon).InstanceId;
            using var hub = new BaseHubViewModel(session, null, () => 77);
            Assert.IsTrue(hub.Multiplayer.SetReady(true));
            Assert.IsTrue(hub.Transit.StartExpedition());
            var expedition = session.Expedition;
            expedition.AddCarriedCoins(200);
            expedition.RecordEnemyDefeated(40);
            var xpBeforeFail = session.Profile.TotalXp;
            var summary = expedition.Fail();
            Assert.IsFalse(summary.IsSuccess);
            Assert.AreEqual(300, session.Profile.BankedCoins, "Carried coins lost, banked kept.");
            Assert.AreEqual(xpBeforeFail, session.Profile.TotalXp, "XP earned in the run is kept.");
            Assert.IsNull(session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon), "Carried gear lost…");
            Assert.IsTrue(session.GrantedRescueKit || session.StarterKit.EnsureStartableLoadout(session.Profile, session.Storage) || session.Profile.SafeLoadout != null, "…and a rescue kit path exists so nothing softlocks.");
            session.SaveNow("fail");
            menu.LeaveBase();

            // Quit mid-run: start again, then relaunch without ending the run.
            var (second, _) = Launch(store);
            second.Play();
            var s2 = second.Session;
            using var hub2 = new BaseHubViewModel(s2, null, () => 78);
            Assert.IsNotNull(s2.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon), "Rescue kit equipped for the next run.");
            var rescuePistol = s2.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon).InstanceId;
            Assert.AreNotEqual(pistolId, rescuePistol);
            Assert.IsTrue(hub2.Multiplayer.SetReady(true));
            Assert.IsTrue(hub2.Transit.StartExpedition());
            second.LeaveBase(); // Alt+F4 with the marker open
            var (third, _) = Launch(store);
            Assert.AreEqual(PlayOutcome.Continued, third.Play());
            Assert.IsNotNull(third.AbandonedExpedition, "The open run resolved as a failure on boot.");
            StringAssert.Contains("counts as failed", third.Message);
            Assert.AreEqual(300, third.Session.Profile.BankedCoins);
            Assert.IsFalse(third.Session.Slot.ActiveExpedition.IsOpen);
            Assert.IsNotNull(third.Session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon), "Rescue kit again: never a softlock.");
            third.LeaveBase();
            File.AppendAllText(ReportPath, "- failure branch: carried coins/gear lost, banked + XP kept, rescue kit; quit mid-run resolved as failure on relaunch, rescue kit again: PASS\n");
        }
    }
}

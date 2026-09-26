using System.Linq;
using NUnit.Framework;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.Persistence;
using RuinRail.UI.Base;
using RuinRail.UI.Onboarding;
using RuinRail.UI.Settings;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests.EditMode
{
    /// <summary>TASK 136 — first-launch Shelter onboarding (name → kit → equip → first expedition), persisted tutorial progress, existing-profile bypass, settings reset.</summary>
    public sealed class OnboardingTests
    {
        private BaseConfigs _configs;
        private DisplayNamePolicy _policy;

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
            Assert.IsNotNull(_policy);
        }

        private ItemDefinition Resolve(string id) => _configs.Resolve(id);

        private (MainMenuViewModel menu, MemorySaveStore store, SaveSlotService saves) Menu(MemorySaveStore store = null)
        {
            store ??= new MemorySaveStore();
            var saves = new SaveSlotService(store, Resolve);
            return (new MainMenuViewModel(saves, _configs), store, saves);
        }

        // ---- Acceptance 1 + 3: a fresh profile walks name → kit → equip → start; every step idempotent ----

        [Test]
        public void FreshProfile_WalksTheFourSteps_AndCanStartTheFirstExpedition()
        {
            var (menu, store, _) = Menu();
            Assert.AreEqual(PlayOutcome.NewProfile, menu.Play());
            var session = menu.Session;
            using var onboarding = new ShelterOnboardingViewModel(session, _policy);

            Assert.AreEqual(ShelterOnboardingStep.ChooseDisplayName, onboarding.Step);
            StringAssert.Contains("display name", onboarding.PromptText);
            Assert.IsFalse(onboarding.SubmitDisplayName("ab"), "Too short: rejected through the approved policy.");
            Assert.AreEqual(DisplayNameError.TooShort.ToString(), onboarding.NameError);
            Assert.AreEqual(ShelterOnboardingStep.ChooseDisplayName, onboarding.Step);
            Assert.IsTrue(onboarding.SubmitDisplayName("  Rail   Ghost "));
            Assert.AreEqual("Rail Ghost", session.Profile.DisplayName);
            Assert.IsTrue(session.Slot.FirstLaunch.DisplayNameConfirmed);
            StringAssert.Contains("Rail Ghost", store.Document, "Name persisted immediately.");

            Assert.AreEqual(ShelterOnboardingStep.ReceiveStarterKit, onboarding.Step);
            Assert.IsTrue(session.GrantedFirstKit, "Kit granted exactly once by the session.");
            StringAssert.Contains("P9 Ranger", onboarding.PromptText);
            StringAssert.Contains("Scrap Vest", onboarding.PromptText);
            onboarding.AcknowledgeStarterKit();

            // The kit is already equipped (75: pistol + vest in their slots) so step 3 is satisfied; unequipping re-opens it.
            Assert.AreEqual(ShelterOnboardingStep.StartFirstExpedition, onboarding.Step);
            var vest = session.Loadout.Unequip(EquippedSlot.Armor);
            Assert.AreEqual(ShelterOnboardingStep.EquipGear, onboarding.Step);
            StringAssert.Contains("Primary Weapon and Armor", onboarding.PromptText);
            Assert.IsTrue(session.Loadout.TryEquip(vest, EquippedSlot.Armor));
            Assert.AreEqual(ShelterOnboardingStep.StartFirstExpedition, onboarding.Step);
            StringAssert.Contains("Transit", onboarding.PromptText);

            // Step 4 through the existing transit panel: completes and persists; no tutorial dungeon, no class choice.
            var transit = new TransitPanelViewModel(session, () => 11);
            Assert.IsTrue(session.Lobby.SetReady(BaseSession.LocalClientId, true));
            Assert.IsTrue(transit.StartExpedition(), transit.Feedback.Text);
            Assert.AreEqual(ShelterOnboardingStep.Complete, onboarding.Step);
            Assert.IsTrue(session.Slot.FirstLaunch.ShelterOnboardingComplete);
            session.Autosave.Flush();
            StringAssert.Contains("\"ShelterOnboardingComplete\":true", store.Document);

            // Idempotent: a second name submit is a rename with the same validation; the kit is never granted twice.
            Assert.IsTrue(onboarding.SubmitDisplayName("Rail Ghost 2"));
            Assert.IsTrue(session.Slot.FirstLaunch.DisplayNameConfirmed);
            Assert.IsFalse(session.StarterKit.GrantFirstProfileKit(session.Profile));
            Assert.AreEqual(ShelterOnboardingStep.Complete, onboarding.Step);
        }

        // ---- Acceptance 4: existing profiles are never forced through first launch again ----

        [Test]
        public void ExistingProfile_SkipsOnboarding_ButStillAsksForAMissingName()
        {
            var (menu, store, _) = Menu();
            menu.Play();
            using (var first = new ShelterOnboardingViewModel(menu.Session, _policy))
            {
                Assert.IsTrue(first.SubmitDisplayName("Runner One"));
            }

            menu.Session.SaveNow("test");
            menu.LeaveBase();

            // Relaunch: kit already granted → onboarding complete on open, persisted; no kit/name prompts.
            var (again, _, _) = Menu(store);
            Assert.AreEqual(PlayOutcome.Continued, again.Play());
            Assert.IsFalse(again.Session.GrantedFirstKit);
            using var relaunched = new ShelterOnboardingViewModel(again.Session, _policy);
            Assert.AreEqual(ShelterOnboardingStep.Complete, relaunched.Step);
            Assert.IsTrue(again.Session.Slot.FirstLaunch.ShelterOnboardingComplete);
            again.Session.SaveNow("test");
            StringAssert.Contains("\"ShelterOnboardingComplete\":true", store.Document);
            again.LeaveBase();

            // A profile that never confirmed a name (e.g. migrated) is asked for the name only, then goes straight to Complete.
            var noName = new MemorySaveStore();
            var (legacy, _, legacySaves) = Menu(noName);
            legacy.Play();
            legacy.Session.SaveNow("test");
            var slot = legacySaves.Load().Slot;
            slot.FirstLaunch.DisplayNameConfirmed = false;
            legacySaves.Save(slot);
            legacy.LeaveBase();
            var (third, _, _) = Menu(noName);
            third.Play();
            using var legacyVm = new ShelterOnboardingViewModel(third.Session, _policy);
            Assert.AreEqual(ShelterOnboardingStep.ChooseDisplayName, legacyVm.Step);
            Assert.IsTrue(legacyVm.SubmitDisplayName("Veteran"));
            Assert.AreEqual(ShelterOnboardingStep.Complete, legacyVm.Step, "Kit already granted: nothing else to walk through.");
        }

        // ---- Display name: the Character station's name field renames through the one validated, saved path ----

        [Test]
        public void NameEntry_RenamesAndPersists_RejectsEmptyInput_AndLeavesProgressionAndItemsAlone()
        {
            var (menu, store, _) = Menu();
            menu.Play();
            var session = menu.Session;
            using var onboarding = new ShelterOnboardingViewModel(session, _policy);
            Assert.IsTrue(onboarding.SubmitDisplayName("Rail Ghost"));
            session.Progression.AddXp(500);
            session.Banked.Credit(123, "test");
            session.SaveNow("test");
            var before = session.Slot.Profile;
            var (xp, coins, points, loadout, storage) = (before.TotalXp, before.BankedCoins, before.UnspentSkillPoints,
                JsonUtility.ToJson(before.SafeLoadout), JsonUtility.ToJson(session.Slot.Storage));

            string saved = null;
            onboarding.DisplayNameChanged += n => saved = n;
            var entry = new DisplayNameEntry(onboarding);
            Assert.IsTrue(entry.Open());
            Assert.AreEqual("Rail Ghost", entry.Text, "the field opens on the saved name");

            // Empty (and whitespace-only) input never replaces a valid name; the field stays open and says why.
            while (entry.Text.Length > 0) entry.Backspace();
            Assert.IsFalse(entry.Submit());
            Assert.AreEqual("Enter a name.", entry.Error);
            entry.Type("    ");
            Assert.IsFalse(entry.Submit());
            Assert.IsTrue(entry.IsOpen);
            Assert.AreEqual("Rail Ghost", session.Profile.DisplayName);
            Assert.IsNull(saved);

            // Typing is filtered to the allowed set and capped at the policy maximum (16), so the HUD never overflows.
            while (entry.Text.Length > 0) entry.Backspace();
            entry.Type("<b>Iron!</b>");
            Assert.AreEqual("bIronb", entry.Text, "markup and punctuation are never typed into the field");
            while (entry.Text.Length > 0) entry.Backspace();
            entry.Type(new string('W', 30));
            Assert.AreEqual(16, entry.Text.Length);

            // Controller editing: Up/Down steps the last character, Right adds one.
            while (entry.Text.Length > 0) entry.Backspace();
            entry.Cycle(+1);
            Assert.AreEqual("A", entry.Text);
            entry.Cycle(+1);
            entry.AddCharacter();
            Assert.AreEqual("BB", entry.Text);
            entry.Cycle(-1);
            Assert.AreEqual("BA", entry.Text);

            // Save: trimmed and collapsed, stored, autosaved, announced.
            while (entry.Text.Length > 0) entry.Backspace();
            entry.Type("  Iron   Wolf ");
            Assert.IsTrue(entry.Submit(), entry.Error);
            Assert.IsFalse(entry.IsOpen);
            Assert.AreEqual("Iron Wolf", session.Profile.DisplayName);
            Assert.AreEqual("Iron Wolf", saved);
            StringAssert.Contains("\"DisplayName\":\"Iron Wolf\"", store.Document, "the rename was flushed to the save");

            // Only the name changed: progression, coins, points, loadout and storage are exactly as they were.
            var after = session.Slot.Profile;
            Assert.AreEqual(xp, after.TotalXp);
            Assert.AreEqual(coins, after.BankedCoins);
            Assert.AreEqual(points, after.UnspentSkillPoints);
            Assert.AreEqual(loadout, JsonUtility.ToJson(after.SafeLoadout));
            Assert.AreEqual(storage, JsonUtility.ToJson(session.Slot.Storage));
            menu.LeaveBase();

            // Restart: the saved name is restored automatically, no name step, same progression.
            var (again, _, _) = Menu(store);
            Assert.AreEqual(PlayOutcome.Continued, again.Play());
            Assert.AreEqual("Iron Wolf", again.Session.Profile.DisplayName);
            Assert.AreEqual(xp, again.Session.Profile.TotalXp);
            Assert.AreEqual(coins, again.Session.Profile.BankedCoins);
            using var relaunched = new ShelterOnboardingViewModel(again.Session, _policy);
            Assert.IsFalse(relaunched.NeedsDisplayName);

            // While a live session fixed the party's names, the field refuses to open.
            var blocked = new DisplayNameEntry(relaunched, () => "Leave the party to change your name.");
            Assert.IsFalse(blocked.Open());
            Assert.IsFalse(blocked.IsOpen);
        }

        // ---- Acceptance 2 + Req 3: persisted completion suppresses repeats; settings reset/re-enable ----

        [Test]
        public void TutorialProgress_PersistsInTheSave_IsSuppressedOnceSeen_AndSettingsCanResetOrDisable()
        {
            var (menu, store, saves) = Menu();
            menu.Play();
            var session = menu.Session;
            var settingsStore = new MemorySaveStore();
            var settingsService = new UserSettingsService(settingsStore);
            settingsService.Load();
            var progress = new SaveSlotTutorialProgress(session.Slot, session.Autosave, () => settingsService.Current.Tutorial.ShowPrompts);
            var prompts = new TutorialPromptService(progress, new SchemeGlyphs(InputScheme.KeyboardMouse));

            Assert.IsTrue(prompts.Trigger(TutorialPromptId.MoveAim));
            Assert.AreEqual("Move with WASD. Aim in any direction with Mouse.", prompts.ActiveText);
            Assert.IsFalse(prompts.Trigger(TutorialPromptId.MoveAim), "Already showing.");
            Assert.IsTrue(prompts.Trigger(TutorialPromptId.Fire), "Queued behind the active prompt.");
            Assert.AreEqual(1, prompts.Queued.Count);
            prompts.Complete(TutorialPromptId.MoveAim);
            Assert.AreEqual(TutorialPromptId.Fire, prompts.Active, "One at a time: the next relevant prompt follows.");
            prompts.Dismiss(TutorialPromptId.Fire);
            Assert.IsNull(prompts.Active);
            session.Autosave.Flush();
            StringAssert.Contains("MoveAim", store.Document);
            StringAssert.Contains("\"Fire\"", store.Document);
            Assert.IsFalse(settingsStore.Exists, "Seen state is in the gameplay save, not in settings.");

            // Relaunch: seen prompts never trigger again; unseen ones still do.
            menu.LeaveBase();
            var (again, _, _) = Menu(store);
            again.Play();
            var reloaded = new SaveSlotTutorialProgress(again.Session.Slot, again.Session.Autosave, () => settingsService.Current.Tutorial.ShowPrompts);
            var prompts2 = new TutorialPromptService(reloaded, new SchemeGlyphs(InputScheme.Gamepad));
            Assert.IsFalse(prompts2.Trigger(TutorialPromptId.MoveAim));
            Assert.IsFalse(prompts2.Trigger(TutorialPromptId.Fire));
            Assert.IsTrue(prompts2.Trigger(TutorialPromptId.Dash));
            Assert.AreEqual("Enemy attack incoming — B: Dash through it.", prompts2.ActiveText, "Controller glyphs for the gamepad scheme.");
            prompts2.DismissActive();

            // Settings: disabling suppresses everything; reset makes them show again; both persist in their own documents.
            using var settings = new SettingsViewModel(settingsService, null, null);
            settings.SetTutorialProgress(reloaded);
            settings.SetTutorialPrompts(false);
            settings.Apply();
            Assert.IsFalse(settingsService.Current.Tutorial.ShowPrompts);
            Assert.IsFalse(prompts2.Trigger(TutorialPromptId.PickupInventory), "Disabled in Settings.");
            Assert.IsTrue(settings.ResetTutorials());
            settings.Apply();
            Assert.IsTrue(settingsService.Current.Tutorial.ShowPrompts);
            Assert.AreEqual(0, again.Session.Slot.FirstLaunch.TutorialPromptsSeen.Count);
            Assert.IsTrue(prompts2.Trigger(TutorialPromptId.MoveAim), "Reset: shows again.");
            again.Session.Autosave.Flush();
            var persisted = saves.Load().Slot;
            Assert.AreEqual(0, persisted.FirstLaunch.TutorialPromptsSeen.Count);
            Assert.AreEqual(0, persisted.Profile.BankedCoins, "Reset touched only the seen list.");
            StringAssert.Contains("\"ShowPrompts\":true", settingsStore.Document);
        }

        [Test]
        public void Glyphs_FollowTheDeviceAndRebinds()
        {
            using var reader = new RuinRail.Core.Input.PlayerInputReader();
            using var rebinder = new RuinRail.Core.Input.InputRebinder(reader.Asset);
            var glyphs = new SchemeGlyphs(InputScheme.KeyboardMouse, rebinder);
            Assert.AreEqual("WASD", glyphs.For("Move"));
            Assert.AreEqual("Space", glyphs.For("Dash"));
            Assert.AreEqual("Enemy attack incoming — Space: Dash through it.", TutorialPromptText.Build(TutorialPromptId.Dash, glyphs));
            rebinder.TryBind(rebinder.Find("Dash", RuinRail.Core.Input.InputRebinder.KeyboardMouseScheme), "<Keyboard>/leftShift");
            StringAssert.Contains("Shift", glyphs.For("Dash"), "Rebinds are reflected in prompt text.");
            glyphs.SetScheme(InputScheme.Gamepad);
            Assert.AreEqual("B", glyphs.For("Dash"));
            Assert.AreEqual("Mouse", new SchemeGlyphs(InputScheme.KeyboardMouse).For("Aim"), "Fixed actions use the approved default label.");
            Assert.AreEqual("Right Stick", new SchemeGlyphs(InputScheme.Gamepad).For("Aim"));
        }
    }
}

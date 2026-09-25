using System;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Networking;
using RuinRail.Persistence;
using RuinRail.UI.Base;
using RuinRail.UI.Inventory;
using RuinRail.UI.Multiplayer;
using RuinRail.UI.Navigation;
using RuinRail.UI.Onboarding;
using RuinRail.UI.Pause;
using RuinRail.UI.Settings;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RuinRail.Tests.EditMode
{
    /// <summary>TASK 142 — controller/keyboard reachability of every control, predictable focus restoration, rarity never by colour alone, device-aware prompts that never touch bindings, text budgets at 640×360.</summary>
    public sealed class UiNavigationTests
    {
        private BaseConfigs _configs;
        private ItemDefinitionRegistry _registry;

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
            FakeMultiplayerServices.ResetRegistry();
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
            ActiveBindingOverrides.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
            ActiveBindingOverrides.Clear();
            Time.timeScale = 1f;
        }

        private ItemDefinition Resolve(string id) => _configs.Resolve(id);

        /// <summary>Walks a list with ±1 steps from the first control and returns the ids visited (the navigation checklist).</summary>
        private static string[] Walk(FocusList list)
        {
            var visited = new System.Collections.Generic.List<string>();
            var first = list.Focused?.Id;
            if (first == null) return Array.Empty<string>();
            visited.Add(first);
            for (var i = 0; i < list.Items.Count * 2; i++)
            {
                if (!list.MoveNext()) break;
                if (list.Focused.Id == first) break;
                visited.Add(list.Focused.Id);
            }

            return visited.ToArray();
        }

        // ---- Acceptance 1: every control on Main Menu / Base / Inventory / Multiplayer / Settings / Transit ----

        [Test]
        public void Checklist_ReachesEveryEnabledControl_OnEveryScreen_WithStepsOnly()
        {
            var saves = new SaveSlotService(new MemorySaveStore(), Resolve);
            var menu = new MainMenuViewModel(saves, _configs);
            var mainList = ScreenNavigation.MainMenu(menu);
            CollectionAssert.AreEqual(new[] { "menu.Play", "menu.Settings", "menu.Help", "menu.Quit" }, Walk(mainList));
            mainList.Focus("menu.Play");
            Assert.IsTrue(mainList.ActivateFocused());
            Assert.AreEqual(MainMenuState.Base, menu.State, "Activation runs the view model's own action.");

            var session = menu.Session;
            var terminal = new MultiplayerTerminalService(new NetworkSessionController(new FakeMultiplayerServices(), new FakeNetworkDriver()));
            using var hub = new BaseHubViewModel(session, terminal, () => 5);
            var hubList = ScreenNavigation.BaseHub(hub);
            CollectionAssert.AreEqual(BaseHubViewModel.Stations.Select(s => "station." + s).Append("station.close"), Walk(hubList), "All 7 stations + leave.");

            // The Character Station only offers a rank it could actually sell: its controls are disabled without a
            // Skill Point in hand (progression pass). Give the fixture profile the levels that make them purchasable.
            session.Progression.AddXp(RuinRail.Gameplay.Progression.LevelCurve.TotalXpForLevel(4));

            string selected = session.Loadout.GetEquipped(EquippedSlot.Armor)?.InstanceId;
            var screens = new (string name, FocusList list, string[] mustContain)[]
            {
                ("Storage", ScreenNavigation.Storage(hub.Storage, () => selected), new[] { "storage.filter.all", "storage.filter.Weapon", "storage.sort", "storage.deposit", "storage.withdraw" }),
                ("Inventory", ScreenNavigation.Inventory(hub.Loadout.Inventory), new[] { "slot.PrimaryWeapon", "slot.ActiveConsumable", "backpack.0", "backpack.7", "inventory.drop", "inventory.consumable", "inventory.close" }),
                ("Trader", ScreenNavigation.Trader(hub.Trader, () => selected), new[] { "trader.buy.0", "trader.sell" }),
                ("Character", ScreenNavigation.Character(hub.Character), CharacterPanelViewModel.Attributes.Select(a => "character.allocate." + a).Append("character.respec").ToArray()),
                ("Workshop", ScreenNavigation.Workshop(hub.Workshop), new[] { "workshop.storage", "workshop.trader" }),
                ("Multiplayer", ScreenNavigation.Multiplayer(new TerminalViewModel(terminal, session.Lobby, BaseSession.LocalClientId)), new[] { "terminal.Solo", "terminal.Host", "terminal.Join" }),
                ("Transit", ScreenNavigation.Transit(hub.Transit, hub.Multiplayer), new[] { "transit.ready", "transit.start" })
            };
            foreach (var (name, list, mustContain) in screens)
            {
                var walked = Walk(list);
                var enabled = list.Items.Where(i => i.IsEnabled).Select(i => i.Id).ToArray();
                CollectionAssert.AreEquivalent(enabled, walked, $"{name}: every enabled control is reached by stepping alone.");
                foreach (var id in mustContain) Assert.IsNotNull(list.Find(id), $"{name} is missing '{id}'.");
                Assert.IsTrue(list.Items.All(i => !string.IsNullOrWhiteSpace(i.Label)), $"{name}: every control has an English label.");
            }

            // Disabled controls are skipped but come back when enabled (Deposit needs a selection).
            selected = null;
            var storage = ScreenNavigation.Storage(hub.Storage, () => selected);
            CollectionAssert.DoesNotContain(Walk(storage), "storage.deposit");
            selected = "x";
            CollectionAssert.Contains(Walk(storage), "storage.deposit");

            // Settings (with a live rebinder) and Pause.
            var settingsService = new UserSettingsService(new MemorySaveStore());
            settingsService.Load();
            using var reader = new PlayerInputReader();
            using var rebinder = new InputRebinder(reader.Asset);
            using var settings = new SettingsViewModel(settingsService, rebinder, null);
            // Settings is category-based: the root lists the four real categories plus RESET TO DEFAULTS and BACK;
            // each category is its own page of adjustable rows.
            var settingsList = ScreenNavigation.Settings(settings);
            CollectionAssert.AreEqual(new[] { "settings.category.Video", "settings.category.Audio", "settings.category.Controls", "settings.category.Gameplay", "settings.defaults", "settings.back" }, Walk(settingsList));
            var gameplay = Walk(ScreenNavigation.SettingsPage(settings, SettingsTab.Gameplay));
            Assert.IsTrue(gameplay.Contains("settings.shake") && gameplay.Contains("settings.hit_flash") && gameplay.Contains("settings.damage_numbers") && gameplay.Contains("settings.tutorials"), "Gameplay toggles are reachable.");
            var controls = Walk(ScreenNavigation.SettingsPage(settings, SettingsTab.Controls));
            Assert.IsTrue(controls.Contains("settings.rebind.Keyboard&Mouse.Dash."), "Rebind rows are reachable (keyboard scheme by default).");
            Assert.IsFalse(controls.Contains("settings.rebind.Keyboard&Mouse.Pause."), "Fixed bindings are not focusable.");
            Assert.IsTrue(controls.Contains("settings.controls.scheme") && controls.Contains("settings.reset_bindings") && controls.Contains("settings.back"));
            var audio = ScreenNavigation.SettingsPage(settings, SettingsTab.Audio);
            CollectionAssert.AreEqual(new[] { "settings.audio.master", "settings.audio.music", "settings.audio.sfx", "settings.audio.ambience", "settings.audio.mute", "settings.back" }, Walk(audio));
            audio.Focus("settings.audio.mute");
            audio.ActivateFocused();
            Assert.IsTrue(settings.Draft.Audio.Mute, "Toggle through the focus item.");
            audio.Focus("settings.audio.music");
            Assert.IsTrue(audio.Focused.IsAdjustable, "a slider steps with left/right");
            Assert.IsTrue(audio.AdjustFocused(-2));
            Assert.AreEqual(0.9f, settings.Draft.Audio.MusicVolume, 1e-4f, "two steps down from 100%");
            Assert.IsFalse(audio.Focused.TryActivate(), "a slider has no Enter action of its own (a click on its bar never also bumps it)");
            var video = Walk(ScreenNavigation.SettingsPage(settings, SettingsTab.Video));
            CollectionAssert.AreEqual(new[] { "settings.video.display_mode", "settings.video.resolution", "settings.video.vsync", "settings.video.framerate", "settings.back" }, video, "APPLY / REVERT only become focusable once a display change is pending");
            settings.SetFullscreen(!settings.Draft.Video.Fullscreen);
            CollectionAssert.Contains(Walk(ScreenNavigation.SettingsPage(settings, SettingsTab.Video)), "settings.video.apply");

            using var pause = new PauseMenuViewModel(null, new TimeScalePause(), isCoop: true, settings);
            pause.Open();
            CollectionAssert.AreEqual(new[] { "pause.Resume", "pause.Settings", "pause.Help", "pause.ReturnToMainMenu", "pause.QuitGame" }, Walk(ScreenNavigation.PauseMenu(pause)));
            menu.LeaveBase();
        }

        // ---- Acceptance 3: focus restoration across panel changes; never behind a closed panel ----

        [Test]
        public void FocusStack_RestoresParentFocus_OnPop_AndOnOutOfOrderClose()
        {
            var stack = new FocusStack();
            var hub = new FocusList("Hub").Add("a", "A", null).Add("b", "B", null).Add("c", "C", null);
            stack.Push(hub);
            hub.Focus("b");
            var panel = new FocusList("Panel").Add("p1", "P1", null).Add("p2", "P2", null);
            stack.Push(panel);
            Assert.AreEqual("p1", stack.Focused.Id, "A pushed panel owns focus.");
            stack.Move(+1);
            var dialog = new FocusList("Dialog").Add("ok", "OK", null).Add("cancel", "Cancel", null, () => false);
            stack.Push(dialog);
            Assert.AreEqual("ok", stack.Focused.Id);
            Assert.IsFalse(stack.Move(+1), "Cancel is disabled: focus stays on the only enabled control.");
            Assert.AreEqual("ok", stack.Focused.Id);

            stack.Pop();
            Assert.AreEqual("Panel", stack.CurrentPanel);
            Assert.AreEqual("p2", stack.Focused.Id, "Back to exactly the control the panel had focused.");
            stack.Pop();
            Assert.AreEqual("b", stack.Focused.Id, "…and the hub's remembered station.");

            // Out-of-order: a panel under a dialog gets destroyed → dialog and panel both go, focus returns to the hub.
            stack.Push(panel);
            stack.Push(dialog);
            stack.Remove(panel);
            Assert.AreEqual(1, stack.Depth);
            Assert.AreEqual("b", stack.Focused.Id);
            stack.Pop();
            Assert.AreEqual(0, stack.Depth);
            Assert.IsNull(stack.Focused, "No panel: no focus target — never a hidden one.");

            // A focused control that becomes disabled is left the moment the list is used again.
            var enabled = true;
            var live = new FocusList("Live").Add("x", "X", null, () => enabled).Add("y", "Y", null);
            stack.Push(live);
            Assert.AreEqual("x", stack.Focused.Id);
            enabled = false;
            live.EnsureValid();
            Assert.AreEqual("y", stack.Focused.Id);
            Assert.IsFalse(live.Focus("x"), "The pointer cannot focus a disabled control either.");
        }

        // ---- Acceptance 2: rarity in grayscale ----

        [Test]
        public void Rarity_IsDistinguishableWithoutColour_ByLabelMarkerBorderAndLuminance()
        {
            var styles = Enum.GetValues(typeof(Rarity)).Cast<Rarity>().Select(RarityStyle.For).ToList();
            Assert.AreEqual(5, styles.Select(s => s.Label).Distinct().Count());
            Assert.AreEqual(5, styles.Select(s => s.Border).Distinct().Count(), "Distinct frame treatment per rarity.");
            Assert.AreEqual(5, styles.Select(s => s.Marker).Distinct().Count(), "Distinct text marker per rarity.");
            var luminance = styles.Select(s => s.Luminance).ToList();
            for (var i = 0; i < luminance.Count; i++)
                for (var j = i + 1; j < luminance.Count; j++)
                    Assert.GreaterOrEqual(Mathf.Abs(luminance[i] - luminance[j]), 0.05f, $"{styles[i].Rarity} vs {styles[j].Rarity} must differ in grayscale.");
            Assert.AreEqual("* LEGENDARY Sunbreaker", RarityStyle.For(Rarity.Legendary).Decorate("Sunbreaker"));
            Assert.AreEqual("COMMON Scrap Vest", RarityStyle.For(Rarity.Common).Decorate("Scrap Vest"));

            var tooltip = ItemTooltip.Build(new ItemInstance("armor_scrap_vest", 1, Rarity.Rare), Resolve("armor_scrap_vest"));
            Assert.AreEqual("++ RARE Scrap Vest", tooltip.Title);
            StringAssert.Contains("RARE", string.Join("\n", tooltip.Lines()), "Rarity text stays in the tooltip lines as well.");
        }

        // ---- Requirement 3 + Acceptance 4: device prompts, never bindings ----

        [Test]
        public void Prompts_FollowTheActiveDevice_AndDeviceSwitchingNeverChangesBindings()
        {
            using var reader = new PlayerInputReader();
            using var rebinder = new InputRebinder(reader.Asset);
            var prompts = new UiPrompts(new SchemeGlyphs(InputScheme.KeyboardMouse, rebinder));
            Assert.AreEqual("Enter: Confirm", prompts.For(UiAction.Confirm));
            Assert.AreEqual("Esc: Back", prompts.For(UiAction.Cancel));
            Assert.AreEqual("E: Interact", prompts.For(UiAction.Interact));
            var before = reader.Asset.SaveBindingOverridesAsJson();
            var switchesBefore = ActiveInputDevice.Switches;
            var dashBefore = rebinder.Find("Dash", InputRebinder.KeyboardMouseScheme).EffectivePath;

            ActiveInputDevice.Set(InputDeviceKind.Gamepad);
            Assert.AreEqual("A: Confirm", prompts.For(UiAction.Confirm));
            Assert.AreEqual("B: Back", prompts.For(UiAction.Cancel));
            Assert.AreEqual("A: Interact", prompts.For(UiAction.Interact));
            Assert.AreEqual("Menu: Pause", prompts.For(UiAction.Pause));
            StringAssert.Contains("D-Pad: Navigate", prompts.Footer());
            Assert.AreEqual(before, reader.Asset.SaveBindingOverridesAsJson(), "Switching device changes prompts only.");
            Assert.AreEqual(dashBefore, rebinder.Find("Dash", InputRebinder.KeyboardMouseScheme).EffectivePath);
            Assert.IsFalse(rebinder.HasOverrides);
            Assert.AreEqual(string.Empty, ActiveBindingOverrides.Json);

            // A rebind shows up in the interact prompt on the matching device only; the other device keeps its own binding.
            rebinder.TryBind(rebinder.Find("Interact", InputRebinder.KeyboardMouseScheme), "<Keyboard>/f");
            Assert.AreEqual("A: Interact", prompts.For(UiAction.Interact));
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
            Assert.AreEqual("F: Interact", prompts.For(UiAction.Interact));
            Assert.AreEqual(switchesBefore + 2, ActiveInputDevice.Switches);
        }

        // ---- Requirement 4: legibility budgets ----

        [Test]
        public void Text_FitsReferenceBudgets_ForLongestNamesAndItemTitles()
        {
            var longestName = new string('W', TextFit.MaxDisplayNameChars);
            var partyLine = $"{longestName}: DOWNED 20s";
            Assert.IsTrue(TextFit.Fits(partyLine, TextFit.PartyLineChars), "A 16-char name with the longest state suffix fits the HUD party line.");
            Assert.IsTrue(TextFit.Fits($"{longestName}: DISCONNECTED", TextFit.PartyLineChars));
            Assert.AreEqual(33, TextFit.CharsFor(200));

            var longestItem = _registry.Definitions.Select(d => d.DisplayName).OrderByDescending(n => n.Length).First();
            var worst = RarityStyle.For(Rarity.Legendary).Decorate(longestItem);
            var title = TextFit.Clamp(worst, TextFit.ItemNameLineChars);
            Assert.LessOrEqual(title.Length, TextFit.ItemNameLineChars);
            if (worst.Length > TextFit.ItemNameLineChars) StringAssert.EndsWith("…", title); else Assert.AreEqual(worst, title);
            Assert.IsTrue(TextFit.Fits("+++ EPIC " + longestItem, TextFit.ItemNameLineChars), $"Longest item name '{longestItem}' with the longest label must fit the line without truncation.");
            Assert.AreEqual("abc…", TextFit.Clamp("abcdef", 4));
            Assert.AreEqual(string.Empty, TextFit.Clamp(null, 5));
        }
    }
}

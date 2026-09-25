using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core.Input;
using RuinRail.Persistence;
using RuinRail.UI.Base;
using RuinRail.UI.Settings;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RuinRail.Tests.EditMode
{
    /// <summary>TASK 135 — Pause action/bindings, rebinding (conflict/cancel/reset/reserved/impossible), persistent overrides and the separate settings document.</summary>
    public sealed class PauseSettingsRebindingTests
    {
        private const string AssetPath = "Assets/Game/Settings/Input/RuinRailInputActions.inputactions";
        // The 13 actions of 116, in their original order, plus QuickGrenade appended by the UX-QoL pass: appended, so
        // no existing action's index, default binding or generated callback moved.
        private static readonly string[] ApprovedActions = { "Move", "Aim", "Fire", "Special", "Dash", "Reload", "Interact", "Weapon1", "Weapon2", "WeaponSwap", "Consumable", "Inventory", "Pause", "QuickGrenade" };

        private readonly List<PlayerInputReader> _readers = new();

        private sealed class RecordingApplier : ISettingsApplier
        {
            public readonly List<SettingsData> Applied = new();
            public IReadOnlyList<Vector2Int> AvailableResolutions { get; } = new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080) };
            public void Apply(SettingsData settings) => Applied.Add(JsonUtility.FromJson<SettingsData>(JsonUtility.ToJson(settings)));
        }

        [SetUp]
        public void SetUp() => ActiveBindingOverrides.Clear();

        [TearDown]
        public void TearDown()
        {
            foreach (var r in _readers) r.Dispose();
            _readers.Clear();
            ActiveBindingOverrides.Clear();
        }

        private PlayerInputReader Reader()
        {
            var reader = new PlayerInputReader();
            _readers.Add(reader);
            return reader;
        }

        private static string Effective(InputActionAsset asset, string action, string scheme, string part = null)
        {
            var a = asset.FindAction("Player/" + action, throwIfNotFound: true);
            return a.bindings.First(b => !b.isComposite && b.groups == scheme && (part == null || b.name == part)).effectivePath;
        }

        // ---- Acceptance 1 + 2: Pause exists with Esc / Menu; the existing actions and their defaults are untouched ----

        [Test]
        public void PauseAction_ExistsWithEscapeAndMenu_AndTheExistingActionsAreIntact()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPath);
            var map = asset.FindActionMap("Player", throwIfNotFound: true);
            CollectionAssert.AreEqual(ApprovedActions, map.actions.Select(a => a.name).ToArray(), "116: exactly the required actions, original order preserved.");
            var pause = map.FindAction("Pause", throwIfNotFound: true);
            Assert.AreEqual("Button", pause.expectedControlType);
            Assert.IsTrue(pause.bindings.Any(b => b.path == "<Keyboard>/escape" && b.groups == "Keyboard&Mouse"));
            Assert.IsTrue(pause.bindings.Any(b => b.path == "<Gamepad>/start" && b.groups == "Gamepad"), "Menu / Options = Gamepad start.");

            // Generated wrapper: the callback interface carries one method per action and nothing else.
            var generated = typeof(RuinRailInputActions.IPlayerActions).GetMethods().Select(m => m.Name).ToList();
            foreach (var action in ApprovedActions) CollectionAssert.Contains(generated, "On" + action);
            Assert.AreEqual(ApprovedActions.Length, generated.Count);

            // A live reader exposes the same defaults (defaults are the approved 116 tables).
            var reader = Reader();
            Assert.AreEqual("<Keyboard>/escape", Effective(reader.Asset, "Pause", "Keyboard&Mouse"));
            Assert.AreEqual("<Keyboard>/space", Effective(reader.Asset, "Dash", "Keyboard&Mouse"));
            Assert.AreEqual("<Gamepad>/buttonEast", Effective(reader.Asset, "Dash", "Gamepad"));
            Assert.AreEqual("<Keyboard>/w", Effective(reader.Asset, "Move", "Keyboard&Mouse", "up"));
            Assert.IsFalse(reader.Asset.bindings.Any(b => b.overridePath != null), "No overrides published: pristine defaults.");
        }

        // ---- Req 3 + 5: rebinding rules ----

        [Test]
        public void Rebinder_ListsSchemes_RefusesReservedImpossibleWrongDevice_AndHandlesConflictsExplicitly()
        {
            var reader = Reader();
            using var rebinder = new InputRebinder(reader.Asset);

            var kb = rebinder.EntriesFor(InputRebinder.KeyboardMouseScheme).ToList();
            var pad = rebinder.EntriesFor(InputRebinder.GamepadScheme).ToList();
            // 13 single keyboard bindings (the 12 of 116 plus QuickGrenade's Q) + 4 WASD parts.
            Assert.AreEqual(17, kb.Count, "13 single bindings + 4 WASD parts.");
            // One gamepad binding per action (Move is a single stick, so it has no parts to split).
            Assert.AreEqual(14, pad.Count);
            Assert.IsFalse(rebinder.Find("Pause", InputRebinder.KeyboardMouseScheme).IsRebindable, "Pause stays on Escape.");
            Assert.IsFalse(rebinder.Find("Aim", InputRebinder.KeyboardMouseScheme).IsRebindable, "Mouse position is not a rebindable control.");
            Assert.IsFalse(rebinder.Find("Move", InputRebinder.GamepadScheme).IsRebindable, "Left stick is fixed.");
            Assert.IsTrue(rebinder.Find("Move", InputRebinder.KeyboardMouseScheme, "up").IsRebindable);
            Assert.AreEqual("Move Up", rebinder.Find("Move", InputRebinder.KeyboardMouseScheme, "up").Label);
            Assert.AreEqual("Primary Attack", rebinder.Find("Fire", InputRebinder.KeyboardMouseScheme).Label);

            var dash = rebinder.Find("Dash", InputRebinder.KeyboardMouseScheme);
            Assert.AreEqual(RebindOutcome.Reserved, rebinder.TryBind(dash, "<Keyboard>/escape").Outcome, "Escape is reserved for Pause.");
            Assert.AreEqual(RebindOutcome.Reserved, rebinder.TryBind(dash, "<Keyboard>/leftMeta").Outcome);
            Assert.AreEqual(RebindOutcome.Impossible, rebinder.TryBind(dash, "<Mouse>/position").Outcome);
            Assert.AreEqual(RebindOutcome.Impossible, rebinder.TryBind(dash, "<Keyboard>/notAKey").Outcome);
            Assert.AreEqual(RebindOutcome.WrongDevice, rebinder.TryBind(dash, "<Gamepad>/buttonNorth").Outcome, "Gamepad control on the keyboard scheme.");
            Assert.AreEqual(RebindOutcome.Cancelled, rebinder.TryBind(dash, "").Outcome);
            Assert.AreEqual(RebindOutcome.Unchanged, rebinder.TryBind(dash, "<Keyboard>/space").Outcome);
            Assert.AreEqual(RebindOutcome.NotRebindable, rebinder.TryBind(rebinder.Find("Pause", InputRebinder.KeyboardMouseScheme), "<Keyboard>/p").Outcome);
            Assert.AreEqual("<Keyboard>/space", dash.EffectivePath, "Every refusal leaves the binding untouched.");
            Assert.IsFalse(rebinder.HasOverrides);

            // Conflict: E belongs to Interact. Refused unless swapped; swap leaves nothing unbound.
            var conflict = rebinder.TryBind(dash, "<Keyboard>/e");
            Assert.AreEqual(RebindOutcome.Conflict, conflict.Outcome);
            Assert.AreEqual("Interact", conflict.ConflictingAction);
            Assert.AreEqual("<Keyboard>/space", dash.EffectivePath);
            var swapped = rebinder.TryBind(dash, "<Keyboard>/e", swapOnConflict: true);
            Assert.AreEqual(RebindOutcome.Swapped, swapped.Outcome);
            Assert.AreEqual("<Keyboard>/e", dash.EffectivePath);
            Assert.AreEqual("<Keyboard>/space", rebinder.Find("Interact", InputRebinder.KeyboardMouseScheme).EffectivePath);
            Assert.IsTrue(rebinder.HasOverrides);

            // Plain apply; a gamepad rebind on the gamepad scheme; a concrete controller path is generalised.
            Assert.AreEqual(RebindOutcome.Applied, rebinder.TryBind(dash, "<Keyboard>/leftShift").Outcome);
            Assert.AreEqual("<Keyboard>/leftShift", dash.EffectivePath);
            var padDash = rebinder.Find("Dash", InputRebinder.GamepadScheme);
            // D-pad up, not LB: LB is QuickGrenade's default (paired with RB for the Active Consumable), so rebinding
            // onto it would now be a conflict rather than the plain apply this case is about.
            Assert.AreEqual(RebindOutcome.Applied, rebinder.TryBind(padDash, "<XInputController>/dpad/up").Outcome);
            Assert.AreEqual("<Gamepad>/dpad/up", padDash.EffectivePath);
            Assert.AreEqual(RebindOutcome.Impossible, rebinder.TryBind(padDash, "<Gamepad>/leftStick").Outcome);
            Assert.AreEqual(RebindOutcome.Reserved, rebinder.TryBind(padDash, "<Gamepad>/start").Outcome, "Menu is reserved for Pause.");
            var padConflict = rebinder.TryBind(padDash, "<Gamepad>/leftShoulder");
            Assert.AreEqual(RebindOutcome.Conflict, padConflict.Outcome, "LB already belongs to Quick Grenade.");
            Assert.AreEqual("Quick Grenade", padConflict.ConflictingAction, "The conflict names the action by its rebinding label.");
            Assert.AreNotEqual("—", InputRebinder.HumanReadable("<Gamepad>/leftShoulder"), "Display text resolves for overrides.");
            Assert.AreEqual("—", InputRebinder.HumanReadable(""));

            // Per-entry reset then full reset restore the approved defaults.
            rebinder.ResetEntry(dash);
            Assert.AreEqual("<Keyboard>/space", dash.EffectivePath);
            Assert.IsTrue(rebinder.HasOverrides, "Interact and gamepad Dash are still overridden.");
            rebinder.ResetAll();
            Assert.IsFalse(rebinder.HasOverrides);
            Assert.AreEqual("<Keyboard>/e", rebinder.Find("Interact", InputRebinder.KeyboardMouseScheme).EffectivePath);
            Assert.AreEqual("<Gamepad>/buttonEast", padDash.EffectivePath);
        }

        // ---- Acceptance 3: a rebind survives restart (new reader from the persisted document); reset restores defaults ----

        [Test]
        public void Rebind_PersistsThroughSettingsDocument_ReachesNewAndExistingReaders_AndResetRestoresDefaults()
        {
            var store = new MemorySaveStore();
            var service = new UserSettingsService(store);
            var applier = new RecordingApplier();
            SettingsViewModel.Bootstrap(service, applier);

            var local = Reader();
            var other = Reader();
            using var rebinder = new InputRebinder(local.Asset);
            using var vm = new SettingsViewModel(service, rebinder, applier);
            vm.SelectTab(SettingsTab.Controls);
            var dash = rebinder.Find("Dash", InputRebinder.KeyboardMouseScheme);
            Assert.AreEqual(RebindOutcome.Applied, vm.TryBind(dash, "<Keyboard>/leftShift").Outcome);
            Assert.IsTrue(vm.IsDirty);
            StringAssert.Contains("Dash rebound to", vm.Message);
            Assert.AreEqual("<Keyboard>/space", Effective(other.Asset, "Dash", "Keyboard&Mouse"), "Not applied yet: other readers keep defaults.");
            Assert.IsTrue(vm.BindingLine(dash).EndsWith("*"), "Overridden entries are marked.");

            Assert.AreEqual(SaveError.None, vm.Apply());
            Assert.IsFalse(vm.IsDirty);
            Assert.AreEqual("<Keyboard>/leftShift", Effective(other.Asset, "Dash", "Keyboard&Mouse"), "APPLY reaches readers already alive.");
            StringAssert.Contains("leftShift", store.Document, "Persisted in the settings document, not the gameplay save.");
            Assert.IsFalse(store.Document.Contains("Backpack") || store.Document.Contains("Storage"));

            // Restart: a fresh service + fresh readers built from the persisted document.
            ActiveBindingOverrides.Clear();
            var restarted = new UserSettingsService(store);
            SettingsViewModel.Bootstrap(restarted, applier);
            var afterRestart = Reader();
            Assert.AreEqual("<Keyboard>/leftShift", Effective(afterRestart.Asset, "Dash", "Keyboard&Mouse"), "Rebind survives restart.");
            Assert.AreEqual("<Keyboard>/e", Effective(afterRestart.Asset, "Interact", "Keyboard&Mouse"), "Untouched actions keep their defaults.");
            Assert.AreEqual("<Keyboard>/escape", Effective(afterRestart.Asset, "Pause", "Keyboard&Mouse"));

            // Discard drops an unapplied rebind; reset-to-defaults restores the approved bindings everywhere and persists.
            using var rebinder2 = new InputRebinder(afterRestart.Asset);
            using var vm2 = new SettingsViewModel(restarted, rebinder2, applier);
            var reload = rebinder2.Find("Reload", InputRebinder.KeyboardMouseScheme);
            Assert.AreEqual(RebindOutcome.Applied, vm2.TryBind(reload, "<Keyboard>/f").Outcome);
            vm2.Discard();
            Assert.AreEqual("<Keyboard>/r", reload.EffectivePath);
            Assert.AreEqual("<Keyboard>/leftShift", rebinder2.Find("Dash", InputRebinder.KeyboardMouseScheme).EffectivePath, "Discard returns to the persisted state, not to factory defaults.");
            Assert.AreEqual(SaveError.None, vm2.ResetToDefaults());
            Assert.AreEqual("<Keyboard>/space", Effective(afterRestart.Asset, "Dash", "Keyboard&Mouse"));
            Assert.AreEqual("<Keyboard>/space", Effective(other.Asset, "Dash", "Keyboard&Mouse"), "Reset is published to every reader.");
            Assert.AreEqual(string.Empty, restarted.Current.Controls.BindingOverridesJson);
            Assert.AreEqual(string.Empty, ActiveBindingOverrides.Json);
            var reloaded = new UserSettingsService(store).Load();
            Assert.AreEqual(string.Empty, reloaded.Controls.BindingOverridesJson, "Defaults persisted.");
        }

        // ---- Req 4: audio / video / accessibility settings apply and persist separately ----

        [Test]
        public void AudioVideoAccessibility_ApplyThroughTheApplier_PersistSeparately_AndRejectUnknownResolutions()
        {
            var store = new MemorySaveStore();
            var service = new UserSettingsService(store);
            var applier = new RecordingApplier();
            service.Load();
            using var vm = new SettingsViewModel(service, null, applier);

            vm.SetMasterVolume(0.5f);
            vm.SetMusicVolume(1.5f);
            vm.SetSfxVolume(-1f);
            vm.SetFullscreen(false);
            vm.SetVSync(false);
            vm.SetScreenShake(false);
            vm.SetScreenShakeIntensity(0.4f);
            vm.SetDamageNumbers(false);
            vm.SetHitFlash(false);
            Assert.IsFalse(vm.SetResolution(640, 480), "Not offered by the display.");
            StringAssert.Contains("not available", vm.Message);
            Assert.IsTrue(vm.SetResolution(1280, 720));
            Assert.AreEqual("1280 x 720", vm.ResolutionText, "the pixel face has no multiplication sign");
            Assert.IsTrue(vm.IsDirty);
            Assert.AreEqual(1f, service.Current.Audio.MasterVolume, "Draft only until APPLY.");
            Assert.AreEqual(0, applier.Applied.Count);

            Assert.AreEqual(SaveError.None, vm.Apply());
            Assert.AreEqual(1, applier.Applied.Count);
            var applied = applier.Applied[0];
            Assert.AreEqual(0.5f, applied.Audio.MasterVolume);
            Assert.AreEqual(1f, applied.Audio.MusicVolume, "Clamped.");
            Assert.AreEqual(0f, applied.Audio.SfxVolume, "Clamped.");
            Assert.IsFalse(applied.Video.Fullscreen);
            Assert.IsFalse(applied.Video.VSync);
            Assert.AreEqual(1280, applied.Video.ResolutionWidth);
            Assert.IsFalse(applied.Accessibility.ScreenShake);
            Assert.AreEqual(0f, RuinRail.Core.Rendering.FeedbackPreferences.ScreenShakeIntensity, "Shake off publishes 0 to the presentation layer.");
            Assert.IsFalse(RuinRail.Core.Rendering.FeedbackPreferences.DamageNumbers);
            Assert.IsFalse(RuinRail.Core.Rendering.FeedbackPreferences.HitFlash);

            var reloaded = new UserSettingsService(store).Load();
            Assert.AreEqual(0.5f, reloaded.Audio.MasterVolume);
            Assert.IsFalse(reloaded.Accessibility.ScreenShake);
            Assert.AreEqual(720, reloaded.Video.ResolutionHeight);

            Assert.IsTrue(vm.SetResolution(0, 0));
            Assert.AreEqual("Native", vm.ResolutionText);
            vm.Discard();
            Assert.AreEqual("1280 x 720", vm.ResolutionText, "Discard returns to persisted values.");
            Assert.AreEqual(SaveError.None, vm.ResetToDefaults());
            Assert.AreEqual(1f, service.Current.Audio.MasterVolume);
            Assert.IsTrue(service.Current.Accessibility.ScreenShake);
            Assert.AreEqual(1f, RuinRail.Core.Rendering.FeedbackPreferences.ScreenShakeIntensity);
            Assert.IsTrue(RuinRail.Core.Rendering.FeedbackPreferences.DamageNumbers);
            Assert.AreEqual(2, applier.Applied.Count, "Reset applies defaults immediately.");
        }

        private sealed class PauseOnlyReader : IPlayerInputReader
        {
            public Vector2 Move => Vector2.zero;
            public Vector2 Aim => Vector2.zero;
            public bool IsAimFromPointer => false;
            public bool FireHeld => false;
            public bool SpecialHeld => false;
            public bool InteractHeld => false;
#pragma warning disable CS0067
            public event System.Action Dash, Reload, Interact, Weapon1Selected, Weapon2Selected, WeaponSwapped, ConsumableUsed, QuickGrenadeUsed, InventoryToggled;
#pragma warning restore CS0067
            public event System.Action PauseToggled;
            public void Enable() { }
            public void Disable() { }
            public void Press() => PauseToggled?.Invoke();
        }

        [Test]
        public void PauseMenu_SettingsScreen_EscAppliesAndGoesBack_ButBelongsToAListeningRebind()
        {
            var store = new MemorySaveStore();
            var service = new UserSettingsService(store);
            service.Load();
            var reader = new PauseOnlyReader();
            var live = Reader();
            using var rebinder = new InputRebinder(live.Asset);
            using var settings = new SettingsViewModel(service, rebinder, new RecordingApplier());
            using var menu = new RuinRail.UI.Pause.PauseMenuViewModel(reader, new RuinRail.UI.Inventory.TimeScalePause(), isCoop: false, settings);
            try
            {
                reader.Press();
                menu.Activate(RuinRail.UI.Pause.PauseMenuItem.Settings);
                Assert.AreEqual(RuinRail.UI.Pause.PauseScreen.Settings, menu.Screen);
                settings.SetMasterVolume(0.4f);
                reader.Press();
                Assert.AreEqual(RuinRail.UI.Pause.PauseScreen.Root, menu.Screen, "Esc on the settings screen goes back…");
                Assert.AreEqual(0.4f, service.Current.Audio.MasterVolume, "…and persists the page.");
                Assert.IsTrue(menu.IsOpen);
                reader.Press();
                Assert.IsFalse(menu.IsOpen);

                // While a rebind is listening, Esc is the rebind's cancel and never reaches the menu.
                menu.Open();
                menu.Activate(RuinRail.UI.Pause.PauseMenuItem.Settings);
                var dash = rebinder.Find("Dash", InputRebinder.KeyboardMouseScheme);
                Assert.IsTrue(settings.BeginRebind(dash));
                Assert.IsTrue(settings.IsListening);
                StringAssert.Contains("press a control", settings.BindingLine(dash));
                reader.Press();
                Assert.AreEqual(RuinRail.UI.Pause.PauseScreen.Settings, menu.Screen, "Swallowed by the listening rebind.");
                settings.CancelRebind();
                Assert.IsFalse(settings.IsListening);
                Assert.AreEqual(RebindOutcome.Cancelled, rebinder.LastResult.Outcome);
                Assert.AreEqual("<Keyboard>/space", dash.EffectivePath, "Cancel leaves the binding untouched.");
                Assert.IsFalse(settings.BeginRebind(rebinder.Find("Pause", InputRebinder.KeyboardMouseScheme)), "Fixed entries never start listening.");
            }
            finally
            {
                UnityEngine.Time.timeScale = 1f;
            }
        }

        [Test]
        public void MainMenu_SettingsEntry_LeavingAppliesTheSettingsPage()
        {
            var store = new MemorySaveStore();
            var service = new UserSettingsService(store);
            service.Load();
            var applier = new RecordingApplier();
            using var settings = new SettingsViewModel(service, null, applier);
            var saves = new SaveSlotService(new MemorySaveStore(), _ => null);
            var menu = new MainMenuViewModel(saves, new BaseConfigs());
            menu.SetSettings(settings);
            menu.Select(MainMenuEntry.Settings);
            Assert.AreEqual(MainMenuState.Settings, menu.State);
            settings.SetMasterVolume(0.25f);
            menu.BackToMenu();
            Assert.AreEqual(MainMenuState.Menu, menu.State);
            Assert.AreEqual(0.25f, service.Current.Audio.MasterVolume, "Leaving SETTINGS persists.");
            Assert.AreEqual(1, applier.Applied.Count);
        }
    }
}

using System;
using System.Collections.Generic;
using NUnit.Framework;
using RuinRail.Core.Input;
using RuinRail.Persistence;
using RuinRail.UI.Inventory;
using RuinRail.UI.Pause;
using RuinRail.UI.Settings;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>
    /// The pause flow's states: RESUME, SETTINGS (Back returns to the pause root), RETURN TO MAIN MENU and QUIT GAME
    /// behind confirmations that state the expedition loss rule, Pause-while-open backing out of nested panels, and
    /// the world pause released before any leave path runs.
    /// </summary>
    public sealed class PauseMenuFlowTests
    {
        /// <summary>Pause-only reader: the one event the pause flow subscribes to.</summary>
        private sealed class PauseReader : IPlayerInputReader
        {
            public Vector2 Move => Vector2.zero;
            public Vector2 Aim => Vector2.zero;
            public bool IsAimFromPointer => false;
            public bool FireHeld => false;
            public bool SpecialHeld => false;
            public bool InteractHeld => false;
#pragma warning disable CS0067
            public event Action Dash;
            public event Action Reload;
            public event Action Interact;
            public event Action Weapon1Selected;
            public event Action Weapon2Selected;
            public event Action WeaponSwapped;
            public event Action ConsumableUsed;
            public event Action QuickGrenadeUsed;
            public event Action InventoryToggled;
#pragma warning restore CS0067
            public event Action PauseToggled;
            public void Enable() { }
            public void Disable() { }
            public void RaisePause() => PauseToggled?.Invoke();
        }

        private sealed class NoApplier : ISettingsApplier
        {
            public IReadOnlyList<Vector2Int> AvailableResolutions => new[] { new Vector2Int(640, 360) };
            public void Apply(SettingsData settings) { }
        }

        private static SettingsViewModel NewSettings()
        {
            var service = new UserSettingsService(new MemorySaveStore());
            service.Load();
            return new SettingsViewModel(service, null, new NoApplier());
        }

        private sealed class CountingPause : IWorldPause
        {
            public int Holds;
            public void Pause() => Holds++;
            public void Resume() => Holds--;
        }

        [Test]
        public void Resume_ClosesTheMenu_AndReleasesTheSoloWorldPause_WithoutStackingASecondLayer()
        {
            var reader = new PauseReader();
            var world = new CountingPause();
            using var menu = new PauseMenuViewModel(reader, world, isCoop: false);
            reader.RaisePause();
            reader.RaisePause();
            Assert.IsFalse(menu.IsOpen, "Pause while open resumes");
            Assert.AreEqual(0, world.Holds);
            menu.Open();
            menu.Open();
            Assert.AreEqual(1, world.Holds, "opening twice never stacks a second pause layer");
            Assert.AreEqual(2, menu.Opens);
            menu.Activate(PauseMenuItem.Resume);
            Assert.IsFalse(menu.IsOpen);
            Assert.AreEqual(0, world.Holds);
        }

        [Test]
        public void Settings_OpensTheSettingsScreen_AndBackReturnsToThePauseRoot_NotToGameplay()
        {
            var reader = new PauseReader();
            var settings = NewSettings();
            using var menu = new PauseMenuViewModel(reader, new CountingPause(), isCoop: false, settings);
            menu.Open();
            menu.Activate(PauseMenuItem.Settings);
            Assert.AreEqual(PauseScreen.Settings, menu.Screen);
            menu.Back();
            Assert.AreEqual(PauseScreen.Root, menu.Screen, "Back from Settings lands on the pause root");
            Assert.IsTrue(menu.IsOpen);
            menu.Back();
            Assert.IsFalse(menu.IsOpen, "Back on the root resumes");
        }

        [Test]
        public void ReturnToMainMenu_IsConfirmedFirst_StatesTheLossRuleDuringAnExpedition_AndRunsTheLeavePathOnce()
        {
            var reader = new PauseReader();
            var world = new CountingPause();
            var returns = 0;
            var active = true;
            using var menu = new PauseMenuViewModel(reader, world, isCoop: false, returnToMenu: () => returns++, expeditionActive: () => active);
            menu.Open();
            menu.Activate(PauseMenuItem.ReturnToMainMenu);
            Assert.AreEqual(PauseScreen.ConfirmReturn, menu.Screen);
            Assert.IsTrue(menu.IsConfirming);
            Assert.AreEqual(0, returns, "nothing happens before the confirmation");
            StringAssert.Contains("RETURN TO MAIN MENU", menu.ConfirmationTitle);
            StringAssert.Contains("counts as failed", menu.ConfirmationText);
            StringAssert.Contains("lost", menu.ConfirmationText);
            StringAssert.Contains("cannot be resumed", menu.ConfirmationText);
            StringAssert.Contains("XP, banked Coins and Storage are safe", menu.ConfirmationText);

            // Pause / Back while confirming backs out; nothing runs.
            reader.RaisePause();
            Assert.AreEqual(PauseScreen.Root, menu.Screen);
            Assert.IsTrue(menu.IsOpen);
            Assert.AreEqual(0, returns);

            menu.Activate(PauseMenuItem.ReturnToMainMenu);
            menu.CancelConfirmation();
            Assert.AreEqual(PauseScreen.Root, menu.Screen);

            menu.Activate(PauseMenuItem.ReturnToMainMenu);
            menu.Confirm();
            Assert.AreEqual(1, returns, "the owner's leave path runs exactly once");
            Assert.AreEqual(1, menu.ReturnRequests);
            Assert.AreEqual(0, world.Holds, "the world pause is released before the scene is torn down");
            Assert.IsFalse(menu.IsOpen);
            menu.Confirm();
            Assert.AreEqual(1, returns, "a stray second confirm is a no-op once closed");
        }

        [Test]
        public void ReturnToMainMenu_OutsideAnExpedition_DoesNotSpeakOfALoss()
        {
            using var menu = new PauseMenuViewModel(null, new CountingPause(), isCoop: false, expeditionActive: () => false);
            menu.Open();
            menu.Activate(PauseMenuItem.ReturnToMainMenu);
            StringAssert.DoesNotContain("failed", menu.ConfirmationText);
            StringAssert.Contains("saved", menu.ConfirmationText);
        }

        [Test]
        public void QuitGame_IsConfirmed_AndTheCoopTextNamesAbandoning()
        {
            var quits = 0;
            using var menu = new PauseMenuViewModel(null, new CountingPause(), isCoop: true, quit: () => quits++, expeditionActive: () => true);
            menu.Open();
            Assert.IsFalse(menu.IsWorldPaused, "co-op never freezes the authoritative simulation");
            menu.Activate(PauseMenuItem.QuitGame);
            Assert.AreEqual(PauseScreen.ConfirmQuit, menu.Screen);
            StringAssert.Contains("QUIT GAME", menu.ConfirmationTitle);
            StringAssert.Contains("abandons", menu.ConfirmationText);
            Assert.AreEqual(0, quits);
            menu.Confirm();
            Assert.AreEqual(1, quits);
            Assert.AreEqual(1, menu.QuitRequests);
        }

        [Test]
        public void ControllerNavigation_ReachesEveryItemByStepping_AndWraps()
        {
            using var menu = new PauseMenuViewModel(null, new CountingPause(), isCoop: false);
            menu.Open();
            var seen = new System.Collections.Generic.List<PauseMenuItem> { menu.Selected };
            for (var i = 0; i < PauseMenuViewModel.Items.Length - 1; i++) { menu.MoveSelection(+1); seen.Add(menu.Selected); }
            CollectionAssert.AreEqual(PauseMenuViewModel.Items, seen);
            menu.MoveSelection(+1);
            Assert.AreEqual(PauseMenuItem.Resume, menu.Selected, "wraps");
            menu.MoveSelection(-1);
            Assert.AreEqual(PauseMenuItem.QuitGame, menu.Selected);
        }
    }
}

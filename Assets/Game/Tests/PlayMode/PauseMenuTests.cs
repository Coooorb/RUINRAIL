using System.Linq;
using NUnit.Framework;
using RuinRail.Core.Input;
using RuinRail.UI.Inventory;
using RuinRail.UI.Pause;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 135 — Esc/Menu pause flow: solo pauses the world, co-op only opens a local menu; overlap with the inventory pause; Pause never reaches gameplay input.</summary>
    public class PauseMenuTests
    {
        [TearDown]
        public void TearDown() => Time.timeScale = 1f;

        [Test]
        public void Solo_PauseTogglesWorldPause_MenuNavigation_AndInventoryOverlapIsCounted()
        {
            var reader = new FakePlayerInputReader();
            var world = new TimeScalePause();
            using var menu = new PauseMenuViewModel(reader, world, isCoop: false);
            CollectionAssert.AreEqual(new[] { "RESUME", "SETTINGS", "RETURN TO MAIN MENU", "QUIT GAME" }, PauseMenuViewModel.Items.Select(PauseMenuViewModel.Label));
            Assert.IsFalse(menu.IsOpen);
            Assert.AreEqual(1f, Time.timeScale);

            reader.RaisePause();
            Assert.IsTrue(menu.IsOpen);
            Assert.AreEqual(PauseScreen.Root, menu.Screen);
            Assert.IsTrue(menu.IsWorldPaused);
            Assert.AreEqual(0f, Time.timeScale, "Solo: the world is paused.");
            Assert.AreEqual("PAUSED", menu.Title);

            // Inventory opened while paused, then the menu closes: the inventory's hold keeps the world paused.
            var inventory = new InventoryViewModel();
            inventory.ConfigurePause(world, isCoop: false);
            inventory.Open();
            Assert.AreEqual(2, world.Holds);
            reader.RaisePause();
            Assert.IsFalse(menu.IsOpen);
            Assert.IsFalse(menu.IsWorldPaused);
            Assert.AreEqual(0f, Time.timeScale, "The inventory still holds the pause.");
            inventory.Close();
            Assert.AreEqual(1f, Time.timeScale);
            Assert.AreEqual(0, world.Holds);
            world.Resume();
            Assert.AreEqual(0, world.Holds, "Never negative.");

            // Keyboard/controller navigation: RESUME is first; wrapping selection; RESUME closes.
            menu.Open();
            Assert.AreEqual(PauseMenuItem.Resume, menu.Selected);
            menu.MoveSelection(-1);
            Assert.AreEqual(PauseMenuItem.QuitGame, menu.Selected);
            menu.MoveSelection(+1);
            Assert.AreEqual(PauseMenuItem.Resume, menu.Selected);
            menu.Activate();
            Assert.IsFalse(menu.IsOpen);
            Assert.AreEqual(1f, Time.timeScale);

            var quits = 0;
            using var withQuit = new PauseMenuViewModel(reader, world, isCoop: false, quit: () => quits++);
            withQuit.Open();
            withQuit.Activate(PauseMenuItem.QuitGame);
            Assert.AreEqual(PauseScreen.ConfirmQuit, withQuit.Screen, "Quit is confirmed first.");
            Assert.AreEqual(0, quits);
            withQuit.Confirm();
            Assert.AreEqual(1, quits);
            Assert.AreEqual(1, withQuit.QuitRequests);
        }

        [Test]
        public void Coop_PauseOpensLocalMenuOnly_WorldKeepsRunning()
        {
            var reader = new FakePlayerInputReader();
            var world = new TimeScalePause();
            using var menu = new PauseMenuViewModel(reader, world, isCoop: true);
            reader.RaisePause();
            Assert.IsTrue(menu.IsOpen);
            Assert.IsFalse(menu.IsWorldPaused, "Co-op: local menu only.");
            Assert.AreEqual(1f, Time.timeScale);
            Assert.AreEqual(0, world.Holds);
            Assert.AreEqual("MENU — the expedition continues", menu.Title);
            reader.RaisePause();
            Assert.IsFalse(menu.IsOpen);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [Test]
        public void PauseContract_IsCarriedByEveryReader()
        {
            // Pause is a separate event on the reader contract; gameplay components never subscribe to it (only the pause flow does).
            var pauseEvent = typeof(IPlayerInputReader).GetEvent(nameof(IPlayerInputReader.PauseToggled));
            Assert.IsNotNull(pauseEvent);
            Assert.IsNotNull(typeof(NullPlayerInputReader).GetEvent(nameof(IPlayerInputReader.PauseToggled)));
            Assert.IsNotNull(typeof(RuinRail.Networking.RemoteIntentInputReader).GetEvent(nameof(IPlayerInputReader.PauseToggled)), "Remote replicas carry the contract but never raise it.");
        }
    }
}

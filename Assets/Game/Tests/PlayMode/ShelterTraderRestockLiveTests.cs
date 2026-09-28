using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.UI.Base;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// 72 through the shipping flow and file persistence: buy at the Shelter Trader → the offer is sold out for the rest
    /// of the cycle (reopening the counter changes nothing) → run → Return → the Shelter's Trader shows a fresh stock →
    /// a fresh boot from the same save shows that same stock, with no extra restock.
    /// </summary>
    public sealed class ShelterTraderRestockLiveTests
    {
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_trader_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
        }

        [TearDown]
        public void TearDown()
        {
            DestroyApp();
            Time.timeScale = 1f;
            try { Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private void DestroyApp()
        {
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            _app = null;
            foreach (var screen in Object.FindObjectsByType<MainMenuScreen>(FindObjectsSortMode.None)) Object.DestroyImmediate(screen.gameObject);
            foreach (var screen in Object.FindObjectsByType<BaseHubScreen>(FindObjectsSortMode.None)) Object.DestroyImmediate(screen.gameObject);
            foreach (var scene in Object.FindObjectsByType<ExpeditionScene>(FindObjectsSortMode.None)) Object.DestroyImmediate(scene.gameObject);
        }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 40f;
            while (_app.ComposedScene != scene)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed in time (last: '{_app.ComposedScene}').");
                yield return null;
            }
        }

        private IEnumerator BootToShelter()
        {
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
        }

        private static string Stock(BaseHubScreen hub) =>
            string.Join("|", hub.Hub.Trader.Offers.Select(o => $"{o.Index}:{o.Definition.Id}:{o.Item.Rarity}:{o.Price}:{(o.IsSold ? "sold" : "open")}"));

        [UnityTest]
        public IEnumerator Buy_SoldOut_Run_Return_FreshStock_AndReloadKeepsIt()
        {
            yield return BootToShelter();
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Trader Proof");
            hub.Onboarding.AcknowledgeStarterKit();
            var session = hub.Session;
            session.Banked.Credit(50000, "test");

            // Buy one offer at the counter: it reads SOLD, and reopening the counter changes nothing.
            hub.Hub.Open(BaseStation.Trader);
            yield return null;
            var offer = hub.Hub.Trader.Offers.First();
            var banked = session.Banked.Balance;
            Assert.IsTrue(hub.Hub.Trader.Buy(offer.Index), hub.Hub.Trader.Feedback.Text);
            Assert.AreEqual(banked - offer.Price, session.Banked.Balance, "charged once");
            hub.Hub.Close();
            hub.Hub.Open(BaseStation.Trader);
            yield return null;
            var soldStock = Stock(hub);
            var refreshes = session.Trader.State.RefreshCount;
            Assert.IsTrue(hub.Hub.Trader.Offers.First(o => o.Index == offer.Index).IsSold);
            Assert.IsFalse(hub.Hub.Trader.Buy(offer.Index), "sold out for the rest of the cycle");
            Assert.AreEqual(banked - offer.Price, session.Banked.Balance);
            UiScreenCapture.Capture("trader_restock_1_sold_out");

            // Run → Return: the Shelter's Trader carries a fresh stock.
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition(), hub.Hub.Transit.Feedback.Text);
            yield return WaitComposed(SceneNames.Dungeon);
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            for (var i = 0; i < 6; i++) yield return null;
            Assert.IsTrue(run.Expedition.Return().IsSuccess);
            yield return WaitComposed(SceneNames.Base);
            hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Hub.Open(BaseStation.Trader);
            yield return null;
            Assert.AreEqual(refreshes + 1, hub.Session.Trader.State.RefreshCount, "exactly one restock for the ended run");
            Assert.IsTrue(hub.Hub.Trader.Offers.All(o => !o.IsSold), "every offer is back in stock");
            var freshStock = Stock(hub);
            Assert.AreNotEqual(soldStock, freshStock);
            UiScreenCapture.Capture("trader_restock_2_fresh_after_return");
            for (var i = 0; i < 3; i++) { hub.Hub.Close(); hub.Hub.Open(BaseStation.Trader); yield return null; }
            Assert.AreEqual(freshStock, Stock(hub), "reopening the counter never rerolls");

            // A fresh boot from the same save: the same stock, no extra restock.
            _app.Menu.LeaveBase();
            DestroyApp();
            yield return null;
            yield return BootToShelter();
            hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Hub.Open(BaseStation.Trader);
            yield return null;
            Assert.AreEqual(refreshes + 1, hub.Session.Trader.State.RefreshCount, "a reload never restocks");
            Assert.AreEqual(freshStock, Stock(hub), "a reload never rerolls");
        }
    }
}

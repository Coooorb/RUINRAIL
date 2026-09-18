using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Core.Input;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using RuinRail.UI.Base;
using RuinRail.UI.Hud;
using RuinRail.UI.Inventory;
using RuinRail.UI.Merchant;
using RuinRail.UI.Navigation;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Live-run proof (real boot flow → Shelter → generated dungeon, seed with a merchant room) of: the run starting at
    /// the true effective max HP, the dash icon and its cooldown from the tuned dash, the graphical weapon and
    /// consumable HUD slots following the authoritative equipment, and the merchant prompt → trade screen → purchase
    /// → close flow through the run's own input stack. Captures go to <c>TestResults/StartHpDashIconHudProof</c>
    /// beside the built-player captures.
    /// </summary>
    public sealed class StartHpDashIconHudProofTests
    {
        private const string Folder = "TestResults/StartHpDashIconHudProof";
        private readonly StringBuilder _evidence = new();
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_hudproof_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            Directory.CreateDirectory(Folder);
            foreach (var stale in Directory.GetFiles(Folder, "live_*")) File.Delete(stale);
            CursorService.SetApplier(_ => true);
            GameplayInputGate.Reset();
            _evidence.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            File.WriteAllText(Path.Combine(Folder, "live_hud_merchant_evidence.txt"), _evidence.ToString());
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            foreach (var scene in Object.FindObjectsByType<ExpeditionScene>(FindObjectsSortMode.None)) Object.DestroyImmediate(scene.gameObject);
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == null || IsTestRunner(root)) continue;
                Object.DestroyImmediate(root);
            }

            Time.timeScale = 1f;
            NetworkPlayerObject.VisualComposer = null;
            RoomDoorLock.SkinResolver = null;
            WorldObjectArt.Resolver = null;
            CursorService.Reset();
            GameplayInputGate.Reset();
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
            try { Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private static bool IsTestRunner(GameObject root)
        {
            if (root.name.IndexOf("tests runner", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (var component in root.GetComponents<Component>())
                if (component != null && (component.GetType().Namespace ?? string.Empty).StartsWith("UnityEngine.TestTools")) return true;
            return false;
        }

        private void Note(string line)
        {
            _evidence.AppendLine(line);
            Debug.Log("[PROOF] " + line);
        }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 30f;
            while (_app.ComposedScene != scene)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed in time (last: '{_app.ComposedScene}').");
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator LiveRun_StartsFull_DashIcon_GraphicalHudSlots_AndMerchantTradeFlow()
        {
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(11); // OvergrownLabs: loot room + merchant on depth 1
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("HUD Proof");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 6; i++) yield return null;

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var hud = run.HudView;
            var vm = run.Hud;
            var player = run.Rig.Player;
            var health = player.GetComponent<HealthComponent>();
            var dash = player.GetComponent<PlayerDash>();
            var inventory = run.Expedition.State.Inventory;
            var ppu = run.Camera.Config.PixelsPerUnit;
            var camera = run.Camera.Camera;
            var menuInput = Object.FindFirstObjectByType<MenuInput>();
            var content = _app.Content;
            Sprite Icon(string id) => content.Items.First(i => i != null && i.Id == id).Icon;

            // ---- Run start: full effective HP ----
            var stats = run.Rig.StatsBinder.Stats;
            Assert.AreEqual(120, stats.MaxHealth, "base 100 + Scrap Vest 20");
            Assert.AreEqual(stats.MaxHealth, health.MaxHealth);
            Assert.AreEqual(health.MaxHealth, health.CurrentHealth, "the run starts at the TRUE effective maximum");
            Assert.AreEqual("120 / 120", hud.HpText);
            Note($"run start HP {health.CurrentHealth}/{health.MaxHealth} (PlayerStats.MaxHealth {stats.MaxHealth}); HUD '{hud.HpText}'");
            var shot = LiveDungeonCapture.Capture(Folder, "live_01_full_hp_at_run_start", camera, ppu, includeUi: true);

            // ---- Dash icon: ready → cooldown sweep from the tuned cooldown ----
            Assert.IsTrue(hud.DashIcon.HasIconSprite, "the skin binds the generated dash icon");
            Assert.AreEqual(HudDashState.Ready, hud.DashIcon.State);
            Assert.IsEmpty(hud.DashPanel.GetComponentsInChildren<UnityEngine.UI.Text>(true), "no DASH text");
            Assert.AreEqual(1.4706f, dash.CurrentDashCooldown, 0.001f);
            Assert.AreEqual(17f, dash.CurrentDashSpeed, 0.001f);
            LiveDungeonCapture.Capture(Folder, "live_02_dash_icon_ready", camera, ppu, includeUi: true);
            Assert.IsTrue(dash.TryStartDash(Vector2.right));
            for (var i = 0; i < 15; i++) yield return null;
            Assert.AreEqual(HudDashState.Cooldown, hud.DashIcon.State);
            Assert.AreEqual(dash.CooldownRemaining / dash.CurrentDashCooldown, hud.DashIcon.Cooldown01, 0.05f, "the sweep is the authoritative remaining fraction");
            Note($"dash cooldown {dash.CurrentDashCooldown:0.####} s, speed {dash.CurrentDashSpeed}; icon state {hud.DashIcon.State} fill {hud.DashIcon.Cooldown01:0.00}");
            LiveDungeonCapture.Capture(Folder, "live_03_dash_icon_cooldown", camera, ppu, includeUi: true);

            // ---- Weapon slots: P9 active with ammo, knife icon only ----
            Assert.IsTrue(hud.PrimarySlot.IsActive && hud.PrimarySlot.IconSprite == Icon(StarterKitService.PistolId) && hud.PrimarySlot.ResourceVisible);
            Assert.IsTrue(hud.SecondarySlot.IconSprite == Icon(StarterKitService.KnifeId) && !hud.SecondarySlot.ResourceVisible);
            LiveDungeonCapture.Capture(Folder, "live_04_weapon_slots_p9_active", camera, ppu, includeUi: true);

            // ---- Equip a Wasp-45 through the inventory window: the HUD follows live ----
            var wasp = new ItemInstance("weapon_wasp_45", 1, Rarity.Uncommon);
            var waspDefinition = (RangedWeaponDefinition)_app.Configs.Resolve(wasp.DefinitionId);
            inventory.Add(waspDefinition.AmmoType, 60);
            Assert.IsTrue(inventory.TryAddToBackpack(wasp));
            run.Inventory.Open();
            yield return null;
            var waspIndex = inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.InstanceId == wasp.InstanceId);
            Assert.AreEqual(InventoryActionResult.Done, run.Inventory.MoveTo(new InventorySlotRef(InventorySlotKind.Backpack, waspIndex), new InventorySlotRef(InventorySlotKind.Equipped, (int)EquippedSlot.PrimaryWeapon)));
            yield return null;
            Assert.AreEqual("weapon_wasp_45", vm.Snapshot.Primary.DefinitionId, "no stale P9 on the HUD");
            Assert.AreSame(Icon("weapon_wasp_45"), hud.PrimarySlot.IconSprite);
            var mounted = (RangedWeapon)run.Rig.Loadout.GetSlot(WeaponSlot.Primary);
            Assert.AreEqual($"{mounted.MagazineAmmo} / {inventory.Get(waspDefinition.AmmoType)}", hud.PrimaryText);
            LiveDungeonCapture.Capture(Folder, "live_09_inventory_equip_live_hud", camera, ppu, includeUi: true);
            run.Inventory.Close();
            yield return null;
            Note($"after equipping Wasp-45: HUD slot 1 = {vm.Snapshot.Primary.DefinitionId} '{hud.PrimarySlot.NameText}' {hud.PrimaryText}");
            LiveDungeonCapture.Capture(Folder, "live_05_weapon_slots_wasp45_after_equip", camera, ppu, includeUi: true);
            run.Rig.Loadout.SelectSlot(WeaponSlot.Secondary);
            yield return null;
            Assert.IsTrue(hud.SecondarySlot.IsActive && !hud.SecondarySlot.ResourceVisible);
            LiveDungeonCapture.Capture(Folder, "live_06_knife_active_no_fake_ammo", camera, ppu, includeUi: true);
            run.Rig.Loadout.SelectSlot(WeaponSlot.Primary);

            // ---- Consumable icon slot with the stack chip; decrement after a use ----
            var bandage = inventory.GetEquipped(EquippedSlot.ActiveConsumable);
            Assert.IsNotNull(bandage);
            bandage.SetQuantity(3); // a representative stack for the proof (the starter kit carries one): x3 → x2 after a use
            yield return null;
            Assert.AreEqual("x3", hud.ConsumableText, "the chip re-reads the stack every frame");
            Assert.AreSame(Icon(StarterKitService.BandageId), hud.ConsumableSlot.IconSprite);
            Assert.IsFalse(hud.GetComponentsInChildren<UnityEngine.UI.Text>(true).Any(t => t.text.Contains("Bandage")), "no 'Bandage xN' text line");
            LiveDungeonCapture.Capture(Folder, "live_07_bandage_icon_stack_chip", camera, ppu, includeUi: true);
            var quantity = bandage.Quantity;
            health.TryApplyDamage(new DamageRequest(30));
            yield return null;
            Assert.AreEqual($"{health.CurrentHealth} / {health.MaxHealth}", hud.HpText);
            Assert.IsTrue(player.GetComponent<PlayerConsumableUser>().TryUse());
            var deadline = Time.realtimeSinceStartup + 10f;
            while ((inventory.GetEquipped(EquippedSlot.ActiveConsumable)?.Quantity ?? 0) != quantity - 1 && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            Assert.AreEqual(quantity - 1 > 0 ? "x" + (quantity - 1) : string.Empty, hud.ConsumableText, "the chip follows the use");
            Note($"bandage x{quantity} → chip '{hud.ConsumableText}' after one use");
            LiveDungeonCapture.Capture(Folder, "live_08_after_consume_bandage", camera, ppu, includeUi: true);
            LiveDungeonCapture.Capture(Folder, "live_10_clean_640x360_frame", camera, ppu, includeUi: true);

            // ---- Merchant: prompt → E → trade screen → buy → close ----
            var binding = run.Rooms.Values.Select(r => r.GetComponent<RoomContentBinding>()).FirstOrDefault(b => b != null && b.Merchant != null);
            Assert.IsNotNull(binding, "seed 11 depth 1 carries a merchant room");
            var merchant = binding.Merchant;
            var body = player.GetComponent<Rigidbody2D>();
            var spot = (Vector2)merchant.transform.position + Vector2.down * 1.1f;
            player.transform.position = spot; body.position = spot; body.linearVelocity = Vector2.zero; Physics2D.SyncTransforms();
            for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            for (var i = 0; i < 4; i++) yield return null;
            StringAssert.Contains("TRADE WITH MERCHANT", run.CurrentInteractionPrompt);
            LiveDungeonCapture.Capture(Folder, "live_11_merchant_prompt", camera, ppu, includeUi: true);

            var interactor = player.GetComponent<PlayerInteractor>();
            Assert.IsTrue(interactor.TryInteract(), "E opens the merchant");
            yield return null;
            var trade = run.Merchant;
            var tradeView = run.MerchantView;
            Assert.IsTrue(trade.IsOpen && tradeView.IsVisible, "a real trade screen opened");
            Assert.AreEqual(1, merchant.OpenCount);
            Assert.AreSame(tradeView.FocusList, menuInput.Stack.Current, "the merchant focus list is on the run's input stack");
            Assert.IsTrue(GameplayInputGate.IsHeld);
            Assert.AreEqual(CursorKind.Pointer, CursorService.Current);
            Assert.AreEqual(string.Empty, run.CurrentInteractionPrompt, "the prompt hides under the window");
            Assert.IsTrue(tradeView.RowViews.Take(trade.Rows.Count).All(r => r.IconVisible && r.FrameSprite != null));
            Note($"merchant opened: {trade.Rows.Count} offers [{string.Join(", ", trade.Rows.Select(r => r.Name + " " + r.Price + "C"))}]");
            LiveDungeonCapture.Capture(Folder, "live_12_merchant_menu_open", camera, ppu, includeUi: true);

            run.Expedition.AddCarriedCoins(400);
            var cheapest = trade.Rows.Where(r => !r.IsSold).OrderBy(r => r.Price).First();
            var target = trade.Rows.ToList().IndexOf(cheapest);
            for (var guard = 0; guard < 12 && trade.Cursor != target; guard++) menuInput.Stack.Navigate(trade.Cursor < target ? Vector2Int.down : Vector2Int.up);
            yield return null;
            Assert.AreEqual(target, trade.Cursor);
            StringAssert.Contains(cheapest.Name, tradeView.DetailTitleText);
            LiveDungeonCapture.Capture(Folder, "live_13_merchant_item_selected", camera, ppu, includeUi: true);

            var coinsBefore = run.Expedition.State.CarriedCoins;
            var definitionId = cheapest.Item.DefinitionId;
            var delivered = cheapest.Item.Quantity;
            int Held() => inventory.BackpackSlots.Where(i => i != null && i.DefinitionId == definitionId).Sum(i => i.Quantity);
            var heldBefore = Held();
            Assert.IsTrue(menuInput.Stack.Activate(), "confirm buys the focused offer");
            yield return null;
            Assert.AreEqual(coinsBefore - cheapest.Price, run.Expedition.State.CarriedCoins, "exactly the price");
            Assert.AreEqual(heldBefore + delivered, Held(), "exactly the offer's quantity delivered, once");
            Assert.IsTrue(merchant.Merchant.Offers[target].IsSold);
            StringAssert.StartsWith("BOUGHT", tradeView.MessageText);
            Note($"bought {cheapest.Name} x{delivered} for {cheapest.Price}: coins {coinsBefore} → {run.Expedition.State.CarriedCoins}, held {heldBefore} → {Held()}");
            LiveDungeonCapture.Capture(Folder, "live_14_merchant_purchase_success", camera, ppu, includeUi: true);
            menuInput.Stack.Activate();
            Assert.AreEqual(coinsBefore - cheapest.Price, run.Expedition.State.CarriedCoins, "a repeated confirm changes nothing");
            Assert.AreEqual(heldBefore + delivered, Held(), "no duplicate delivery");

            // Esc / B closes through the run's Back handling; gameplay comes back.
            tradeView.Buttons[MerchantView.CloseFocusId].SimulateClick();
            yield return null;
            for (var i = 0; i < 3; i++) yield return null;
            Assert.IsFalse(trade.IsOpen || tradeView.IsVisible);
            Assert.IsFalse(GameplayInputGate.IsHeld);
            Assert.AreEqual(CursorKind.Aim, CursorService.Current);
            Assert.IsFalse(menuInput.Stack.Contains(tradeView.FocusList));
            StringAssert.Contains("TRADE WITH MERCHANT", run.CurrentInteractionPrompt, "the prompt returns");
            Note("closed: input released, aim cursor back, prompt back");
            LiveDungeonCapture.Capture(Folder, "live_15_merchant_closed", camera, ppu, includeUi: true);

            // Pause coherence: Esc with the trade screen open closes it instead of pausing.
            Assert.IsTrue(interactor.TryInteract());
            yield return null;
            Assert.IsTrue(trade.IsOpen);
            Assert.IsTrue(run.Pause.BeforePauseToggle(), "Esc closes the trade screen first");
            Assert.IsFalse(trade.IsOpen);
            Assert.IsFalse(run.Pause.IsOpen);
            Note($"captures: {string.Join(", ", Directory.GetFiles(Folder, "live_*.png").Select(Path.GetFileName))}");
        }
    }
}

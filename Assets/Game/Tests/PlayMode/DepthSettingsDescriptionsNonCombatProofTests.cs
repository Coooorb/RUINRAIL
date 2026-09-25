using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Core.Input;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using RuinRail.UI.Base;
using RuinRail.UI.Inventory;
using RuinRail.UI.Pause;
using RuinRail.UI.Settings;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Live-run proof of this pass through the real boot flow (Main Menu → Shelter → generated dungeon, seed 53: a
    /// Ruined Metro depth with a Loot room, a Broken Machine and a Cursed Chest, then a Rustworks depth): the damaged
    /// player before Descend and the same player at full effective HP on the next depth (exactly one heal; room
    /// entries, the Transit vote, the pause menu and equipment changes never heal); item descriptions in the
    /// inventory; the Settings category screen and its four pages from the pause menu; the enemy-remaining chip in a
    /// combat room, decremented, absent in the Boss arena and in a non-combat room; the Broken Machine prompt and
    /// outcome; and the other non-combat mechanics of the depths. Captures and evidence go to
    /// <c>TestResults/DepthSettingsDescriptionsNonCombatProof</c>.
    /// </summary>
    public sealed class DepthSettingsDescriptionsNonCombatProofTests
    {
        private const string Folder = NonCombatRoomMatrixTests.Folder;
        private readonly StringBuilder _evidence = new();
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_dsnc_" + System.Guid.NewGuid().ToString("N"));
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
            File.WriteAllText(Path.Combine(Folder, "live_depth_settings_descriptions_noncombat_evidence.txt"), _evidence.ToString());
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
            ProjectileVisualCatalog.Active = null;
            ItemDescriptions.WeaponCatalog = null;
            CursorService.Reset();
            GameplayInputGate.Reset();
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
            try { Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private sealed class Guard : IInvulnerabilityState { public bool IsInvulnerable => true; }

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
            Debug.Log("[DSNC] " + line);
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

        private static Vector2 RoomCentre(RoomRuntime room)
        {
            var marker = room.Root.GetMarkers(RoomMarkerRole.PlayerSpawn).FirstOrDefault();
            return marker != null ? (Vector2)room.Root.transform.TransformPoint(marker.WorldCenter) : room.InteriorWorldBounds.center;
        }

        [UnityTest]
        public IEnumerator LiveRun_DepthHeal_Descriptions_SettingsPages_EnemyCount_BrokenMachine_AndNonCombatRooms()
        {
            const int seed = 53; // depth 1 Ruined Metro: Loot room, Broken Machine, Cursed Chest (event_seed_scan.txt)
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(seed);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);

            // ---------------------------------------------------------------- 7-10. Settings from the Main Menu ----
            var mainMenu = Object.FindFirstObjectByType<MainMenuScreen>();
            var settings = _app.SettingsScreen;
            mainMenu.MenuList.Focus("menu.Settings");
            Assert.IsTrue(mainMenu.MenuList.ActivateFocused());
            yield return null;
            Assert.IsTrue(mainMenu.SettingsShowing && settings.IsOnCategories, "Main Menu → SETTINGS shows the category list");
            Assert.AreSame(mainMenu.SettingsList, mainMenu.Input.Stack.Current);
            var menuCamera = new GameObject("MenuCaptureCamera").AddComponent<Camera>();
            menuCamera.orthographic = true;
            LiveDungeonCapture.Capture(Folder, "live_07_settings_categories_main_menu", menuCamera, 32, includeUi: true);
            settings.OpenPage(SettingsTab.Video);
            yield return null;
            Assert.AreEqual(SettingsTab.Video, mainMenu.SettingsInstance.Driver.Page);
            LiveDungeonCapture.Capture(Folder, "live_08_settings_video_page", menuCamera, 32, includeUi: true);
            settings.BackFromPage();
            settings.OpenPage(SettingsTab.Audio);
            yield return null;
            var audio = mainMenu.SettingsInstance.Driver.RowViews.Where(v => v.gameObject.activeInHierarchy).ToList();
            Assert.AreEqual(4, audio.Count(v => v.ShowsBar), "Master / Music / SFX / Ambience sliders");
            audio.First(v => v.Control.Id == "settings.audio.sfx").SimulateFill(0.6f);
            mainMenu.Input.Stack.Current.Focus("settings.audio.ambience");
            mainMenu.Input.Stack.Adjust(-5);
            yield return null;
            Note($"AUDIO page: sfx set by pointer to {settings.Draft.Audio.SfxVolume:0.00}, ambience stepped by keyboard to {settings.Draft.Audio.AmbienceVolume:0.00}, master {settings.Draft.Audio.MasterVolume:0.00}, music {settings.Draft.Audio.MusicVolume:0.00}; AudioLevels preview sfx {RuinRail.Core.Rendering.AudioLevels.Sfx:0.00} ambience {RuinRail.Core.Rendering.AudioLevels.Ambience:0.00}");
            Assert.AreEqual(0.6f, settings.Draft.Audio.SfxVolume, 1e-3f);
            Assert.AreEqual(0.75f, settings.Draft.Audio.AmbienceVolume, 1e-3f);
            LiveDungeonCapture.Capture(Folder, "live_09_settings_audio_page", menuCamera, 32, includeUi: true);
            settings.BackFromPage();
            Assert.AreEqual(0.6f, _app.Settings.Current.Audio.SfxVolume, 1e-3f, "persisted on Back");
            settings.OpenPage(SettingsTab.Controls);
            yield return null;
            Assert.IsTrue(mainMenu.SettingsInstance.Driver.RowViews.Any(v => v.Control.Id == "settings.controls.scheme"));
            Assert.Greater(settings.RowsFor(SettingsTab.Controls).Count(r => r.Id.StartsWith("settings.rebind.")), 5, "the shipped app has a real rebinder behind CONTROLS");
            LiveDungeonCapture.Capture(Folder, "live_10_settings_controls_page", menuCamera, 32, includeUi: true);
            settings.BackFromPage();
            settings.OpenPage(SettingsTab.Gameplay);
            yield return null;
            LiveDungeonCapture.Capture(Folder, "live_10b_settings_gameplay_page", menuCamera, 32, includeUi: true);
            mainMenu.Input.Stack.Current.Focus(RuinRail.UI.Navigation.ScreenNavigation.SettingsBackId);
            mainMenu.Input.Stack.Activate(); // BACK row: page -> categories
            yield return null;
            Assert.IsTrue(settings.IsOnCategories && mainMenu.SettingsShowing);
            mainMenu.Input.Stack.Current.Focus(RuinRail.UI.Navigation.ScreenNavigation.SettingsBackId);
            mainMenu.Input.Stack.Activate(); // BACK row: categories -> main menu
            yield return null;
            Assert.IsFalse(mainMenu.SettingsShowing, "Main Menu → Settings → Back → Back returns to the Main Menu");
            Assert.AreSame(mainMenu.MenuList, mainMenu.Input.Stack.Current);
            settings.SetSfxVolume(1f); settings.SetAmbienceVolume(1f); settings.Apply();
            Object.DestroyImmediate(menuCamera.gameObject);

            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("DSNC Proof");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 6; i++) yield return null;

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var state = run.Expedition.State;
            var inventory = state.Inventory;
            var player = run.Rig.Player;
            var body = player.GetComponent<Rigidbody2D>();
            var health = player.GetComponent<HealthComponent>();
            var interactor = player.GetComponent<PlayerInteractor>();
            var camera = run.Camera.Camera;
            var ppu = run.Camera.Config.PixelsPerUnit;
            var hud = run.HudView;
            void Put(Vector2 p) { player.transform.position = p; body.position = p; body.linearVelocity = Vector2.zero; Physics2D.SyncTransforms(); }
            IEnumerator Settle() { for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate(); for (var i = 0; i < 3; i++) yield return null; }
            Note($"seed {seed}: depth 1 {state.Biome}, {run.Rooms.Count} rooms; run start HP {health.CurrentHealth}/{health.MaxHealth} (effective max {run.Rig.StatsBinder.Stats.MaxHealth})");
            var kinds = run.Rooms.Values.Select(r => r.GetComponent<RoomContentBinding>()).Where(b => b != null && b.EventInstance != null).Select(b => b.EventInstance.Kind).ToList();
            Note($"  event rooms: {string.Join(", ", kinds)}; special rooms: {string.Join(", ", run.Rooms.Values.Where(r => r.State.RoomType != RoomType.Combat).Select(r => r.State.RoomType + ":" + r.State.RoomId))}");
            CollectionAssert.Contains(kinds, DungeonEventKind.BrokenMachine, "seed 53 places a Broken Machine on depth 1");

            // ---------------------------------------------------------------- 3-6. item descriptions in the inventory ----
            var scope = new ItemInstance("accessory_field_scope", 1, Rarity.Uncommon);
            inventory.TryAddToBackpack(scope);
            run.Inventory.Open();
            yield return null;
            var width = InventoryView.DetailsPanel.Width - UiTheme.Pad * 2;
            IEnumerator Show(InventorySlotRef slot, string capture)
            {
                run.Inventory.SetCursor(slot);
                yield return null;
                var tooltip = run.Inventory.TooltipAt(slot);
                Assert.IsNotNull(tooltip);
                var lines = UiText.Wrap(tooltip.Description, width);
                Assert.AreEqual(lines[0], run.InventoryView.DetailRowTexts[0], "the description leads the details panel");
                Assert.IsTrue(run.InventoryView.DetailRowTexts.All(t => UiText.Width(t) <= width), "no row overflows the panel");
                Note($"  {tooltip.Name}: \"{tooltip.Description}\"{(string.IsNullOrEmpty(tooltip.LegendaryText) ? string.Empty : " / " + tooltip.LegendaryText)} | rows {run.InventoryView.DetailPager.Count}, pages {run.InventoryView.DetailPager.PageCount}");
                LiveDungeonCapture.Capture(Folder, capture, camera, ppu, includeUi: true);
            }

            yield return Show(new InventorySlotRef(InventorySlotKind.Equipped, (int)EquippedSlot.ActiveConsumable), "live_03_consumable_description_bandage");
            yield return Show(new InventorySlotRef(InventorySlotKind.Equipped, (int)EquippedSlot.PrimaryWeapon), "live_04_weapon_description_p9");
            yield return Show(new InventorySlotRef(InventorySlotKind.Equipped, (int)EquippedSlot.Armor), "live_05_armor_description_scrap_vest");
            var scopeIndex = inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.InstanceId == scope.InstanceId);
            yield return Show(new InventorySlotRef(InventorySlotKind.Backpack, scopeIndex), "live_06_accessory_description_field_scope");
            run.Inventory.Close();
            yield return null;
            inventory.RemoveFromBackpack(scopeIndex);

            // ---------------------------------------------------------------- 11-14. enemy-remaining chip ----
            var combat = run.Rooms.Values.First(r => r.State.RoomType == RoomType.Combat && !r.State.IsElite && r.HasEncounter && r.Lifecycle == RoomLifecycleState.Unentered);
            Put(RoomCentre(combat));
            yield return Settle();
            Assert.AreEqual(RoomLifecycleState.Active, combat.Lifecycle);
            var remaining = combat.EnemiesRemaining;
            Assert.Greater(remaining, 0);
            Assert.IsTrue(hud.EnemyCountVisible && hud.EnemiesText == "x" + remaining, "the chip shows the encounter's remaining count");
            Note($"combat room {combat.State.RoomId}: encounter total {combat.Plan.TotalCount}, living {combat.Encounter.LivingCount}, pending {combat.Encounter.PendingCount} -> chip '{hud.EnemiesText}'");
            LiveDungeonCapture.Capture(Folder, "live_11_combat_room_enemies_remaining", camera, ppu, includeUi: true);
            combat.Encounter.Living.First(e => e != null && e.IsAlive).GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(999999));
            yield return null; yield return null;
            Assert.AreEqual(remaining - 1, combat.EnemiesRemaining);
            Assert.AreEqual("x" + (remaining - 1), hud.EnemiesText, "one death decrements the chip at once");
            Note($"  one enemy killed -> chip '{hud.EnemiesText}'");
            LiveDungeonCapture.Capture(Folder, "live_12_combat_room_enemies_decremented", camera, ppu, includeUi: true);
            for (var guard = 0; guard < 60 && combat.Lifecycle == RoomLifecycleState.Active; guard++)
            {
                foreach (var e in combat.Encounter.Living.ToList()) if (e != null && e.IsAlive) e.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(999999));
                yield return null;
            }

            Assert.AreEqual(RoomLifecycleState.Cleared, combat.Lifecycle);
            Assert.IsFalse(hud.EnemyCountVisible, "cleared room: chip gone");

            // ---------------------------------------------------------------- 15-17. the non-combat rooms of the depth ----
            var brokenRoom = run.Rooms.Values.First(r => r.GetComponent<RoomContentBinding>()?.EventInstance is BrokenMachineEvent);
            var brokenBinding = brokenRoom.GetComponent<RoomContentBinding>();
            var machine = (BrokenMachineEvent)brokenBinding.EventInstance;
            var wallet = state.CarriedWallet;
            if (wallet.Balance > 0) wallet.Debit(wallet.Balance, "proof:strip");
            Put(RoomCentre(brokenRoom));
            yield return Settle();
            Assert.IsFalse(hud.EnemyCountVisible, "no enemy chip in an event room");
            LiveDungeonCapture.Capture(Folder, "live_14_noncombat_room_no_enemy_hud", camera, ppu, includeUi: true);
            Put((Vector2)brokenBinding.Event.transform.position + Vector2.down * 1.0f);
            yield return Settle();
            yield return null;
            Note($"Broken Machine ({brokenRoom.State.RoomId}): cost {machine.CostCoins}, carried 0 -> prompt '{run.CurrentInteractionPrompt}'");
            StringAssert.Contains("REPAIR BROKEN MACHINE", run.CurrentInteractionPrompt);
            StringAssert.Contains("NEED", run.CurrentInteractionPrompt);
            LiveDungeonCapture.Capture(Folder, "live_15a_broken_machine_prompt_unaffordable", camera, ppu, includeUi: true);
            Assert.IsFalse(interactor.TryInteract(), "the poor press is refused");
            StringAssert.Contains("NOT ENOUGH COINS", run.LastNotice);
            Assert.AreEqual(DungeonEventPhase.Available, machine.Phase);
            run.Expedition.AddCarriedCoins(machine.CostCoins + 40);
            yield return null; yield return null;
            Assert.AreEqual($"[E] REPAIR BROKEN MACHINE ({machine.CostCoins} COINS)", run.CurrentInteractionPrompt);
            Note($"  carried {wallet.Balance} -> prompt '{run.CurrentInteractionPrompt}'; seeded repair will {(machine.WillRepairSucceed() ? "succeed" : "fail")}");
            LiveDungeonCapture.Capture(Folder, "live_15_broken_machine_prompt", camera, ppu, includeUi: true);
            var coinsBefore = wallet.Balance;
            var pickupsBefore = Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Length;
            Assert.IsTrue(interactor.TryInteract(), "E reaches the machine");
            yield return null; yield return null;
            Assert.AreEqual(coinsBefore - machine.CostCoins, wallet.Balance, "the repair cost was paid exactly once");
            var pickupsAfter = Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Length;
            if (machine.WillRepairSucceed()) { Assert.AreEqual(DungeonEventPhase.Completed, machine.Phase); Assert.Greater(pickupsAfter, pickupsBefore, "the reward landed"); StringAssert.StartsWith("MACHINE REPAIRED", run.LastNotice); }
            else { Assert.AreEqual(DungeonEventPhase.Failed, machine.Phase); Assert.AreEqual(pickupsBefore, pickupsAfter); StringAssert.StartsWith("REPAIR FAILED", run.LastNotice); }
            Note($"  pressed E: coins {coinsBefore} -> {wallet.Balance}, phase {machine.Phase}, pickups {pickupsBefore} -> {pickupsAfter}, notice '{run.LastNotice}'");
            LiveDungeonCapture.Capture(Folder, "live_16_broken_machine_outcome", camera, ppu, includeUi: true);
            yield return null;
            Assert.AreEqual(string.Empty, ((IInteractionPrompt)brokenBinding.Event).PromptFor(player), "a used machine offers no prompt (a dropped reward may now be the nearest prompt)");
            Assert.IsFalse(brokenBinding.Event.CanInteract(player) || brokenBinding.Event.Interact(player), "and no second outcome");
            Note($"  used machine: prompt '{run.CurrentInteractionPrompt}' now belongs to the dropped reward, the machine itself offers none");
            Assert.AreEqual(coinsBefore - machine.CostCoins, wallet.Balance);
            Assert.AreEqual(WorldObjectVisual.ResolvedTint, brokenBinding.Event.GetComponentInChildren<WorldObjectVisual>().Renderer.color, "the machine reads as used");
            Assert.IsTrue(brokenRoom.State.IsResolved("event:BrokenMachine"));

            foreach (var room in run.Rooms.Values.Where(r => r != brokenRoom && r.State.RoomType != RoomType.Combat && r.State.RoomType != RoomType.Start && r.State.RoomType != RoomType.Boss))
            {
                var binding = room.GetComponent<RoomContentBinding>();
                if (binding == null || binding.Skipped.Count > 0) { Note($"  {room.State.RoomId}: skipped {string.Join("|", binding?.Skipped ?? new List<string>())}"); Assert.Fail(room.State.RoomId + " composed with skips"); }
                Put(RoomCentre(room));
                yield return Settle();
                if (binding.EventInstance is CursedChestEvent cursed)
                {
                    Put((Vector2)binding.Event.transform.position + Vector2.down * 1.0f);
                    yield return Settle(); yield return null;
                    Assert.AreEqual("[E] OPEN CURSED CHEST", run.CurrentInteractionPrompt);
                    Assert.IsTrue(interactor.TryInteract());
                    yield return null; yield return null;
                    Assert.IsTrue(room.DoorsLocked && cursed.Phase == DungeonEventPhase.InProgress, "the cursed wave runs behind locked doors");
                    var wave = Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Where(e => e != null && e.IsAlive && room.InteriorWorldBounds.Contains(e.transform.position)).ToList();
                    Note($"  cursed chest ({room.State.RoomId}): wave {wave.Count} enemies, doors locked, notice '{run.LastNotice}', chip visible {hud.EnemyCountVisible}");
                    Assert.IsFalse(hud.EnemyCountVisible, "an event wave never shows the standard-encounter chip");
                    LiveDungeonCapture.Capture(Folder, "live_17_cursed_chest_wave", camera, ppu, includeUi: true);
                    var pickups = Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Length;
                    for (var guard = 0; guard < 80 && cursed.Phase == DungeonEventPhase.InProgress; guard++)
                    {
                        foreach (var e in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (e != null && e.IsAlive && room.InteriorWorldBounds.Contains(e.transform.position)) e.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(999999));
                        yield return null;
                    }

                    Assert.AreEqual(DungeonEventPhase.Completed, cursed.Phase);
                    Assert.IsFalse(room.DoorsLocked);
                    Assert.Greater(Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Length + Object.FindObjectsByType<CoinPickup>(FindObjectsSortMode.None).Length, pickups, "the cursed reward landed");
                    Note($"  cursed chest cleared: notice '{run.LastNotice}', doors open");
                    LiveDungeonCapture.Capture(Folder, "live_17_cursed_chest_cleared_loot", camera, ppu, includeUi: true);
                }
                else if (binding.Chests.Count > 0)
                {
                    var chest = binding.Chests[0];
                    Put((Vector2)chest.transform.position + Vector2.down * 1.0f);
                    yield return Settle(); yield return null;
                    Assert.AreEqual("[E] OPEN CHEST", run.CurrentInteractionPrompt);
                    Assert.IsTrue(interactor.TryInteract() && chest.IsOpened, "E opens the chest");
                    Assert.IsFalse(chest.CanInteract(player) || chest.Interact(player), "an opened chest cannot open again (the dropped loot is now the nearest prompt)");
                    yield return null;
                    Note($"  {room.State.RoomType} room {room.State.RoomId}: chest opened once (art '{chest.GetComponentInChildren<WorldObjectVisual>().Key}')");
                    LiveDungeonCapture.Capture(Folder, $"live_17_{room.State.RoomType.ToString().ToLowerInvariant()}_chest_opened", camera, ppu, includeUi: true);
                }
            }

            // ---------------------------------------------------------------- 13. Boss arena: no chip; defeat opens the transit ----
            var bossRoom = run.Rooms.Values.First(r => r.State.RoomType == RoomType.Boss);
            var bossBinding = bossRoom.GetComponent<RoomContentBinding>();
            Put(RoomCentre(bossRoom));
            yield return Settle();
            Assert.AreEqual(RoomLifecycleState.Active, bossRoom.Lifecycle);
            Assert.IsTrue(hud.BossVisible && !hud.EnemyCountVisible, "the boss bar, never the enemy chip");
            Note($"boss arena {bossRoom.State.RoomId}: boss bar '{hud.BossNameText}', enemy chip visible {hud.EnemyCountVisible}");
            LiveDungeonCapture.Capture(Folder, "live_13_boss_room_no_enemy_hud", camera, ppu, includeUi: true);
            bossBinding.Boss.Boss.Health.TryApplyDamage(new DamageRequest(9999999));
            yield return null; yield return null;
            Assert.IsNotNull(run.Expedition.Transit);
            Assert.AreEqual(TransitDecisionState.Open, run.Expedition.Transit.State);
            Put((Vector2)bossBinding.Transit.transform.position + Vector2.down * 1.0f);
            yield return Settle(); yield return null;
            Assert.AreEqual("[E] BOARD TRANSIT", run.CurrentInteractionPrompt);
            Assert.IsTrue(interactor.TryInteract() && bossBinding.Transit.IsBoarded);
            yield return null;
            StringAssert.StartsWith("TRANSIT BOARDED", run.LastNotice);

            // ---------------------------------------------------------------- 1-2. the depth heal, exactly once ----
            var effectiveMax = run.Rig.StatsBinder.Stats.MaxHealth;
            health.TryApplyDamage(new DamageRequest(effectiveMax / 2));
            yield return null;
            var damaged = health.CurrentHealth;
            Assert.Less(damaged, effectiveMax);
            health.SetInvulnerabilityState(new Guard()); // from here on nothing may change HP except the one heal under test
            var healsBefore = 0;
            health.Healed += _ => healsBefore++;
            Note($"end of depth 1: HP {damaged}/{health.MaxHealth} (effective max {effectiveMax}); transit open, boarded");
            LiveDungeonCapture.Capture(Folder, "live_01_damaged_end_of_depth", camera, ppu, includeUi: true);
            // Not a heal: the pause menu, an equipment change, revisiting rooms, the open transit vote.
            run.Pause.Open(); yield return null; run.Pause.Close(); yield return null;
            var vest = inventory.Unequip(EquippedSlot.Armor);
            yield return null;
            Assert.IsTrue(inventory.TryEquip(vest, EquippedSlot.Armor));
            yield return null;
            Put(RoomCentre(combat)); yield return Settle();
            Put(RoomCentre(bossRoom)); yield return Settle();
            Assert.AreEqual(damaged, health.CurrentHealth, "menus, equipment changes and room revisits never heal");
            Assert.AreEqual(0, run.DepthArrivalHeals);
            Assert.AreEqual(0, healsBefore);

            Assert.IsNotNull(run.Vote, "the transit vote panel is up");
            Assert.IsTrue(run.Vote.Vote(TransitChoice.DescendDeeper), "DESCEND DEEPER through the vote panel (the solo policy resolves at once)");
            var deadline = Time.realtimeSinceStartup + 30f;
            while (run.DepthsBuilt < 2) { Assert.Less(Time.realtimeSinceStartup, deadline, "depth 2 built"); yield return null; }
            for (var i = 0; i < 5; i++) yield return null;
            state = run.Expedition.State;
            Assert.AreEqual(2, state.Depth);
            Assert.AreEqual(run.Rig.StatsBinder.Stats.MaxHealth, health.CurrentHealth, "depth 2 starts at the effective maximum");
            Assert.AreEqual(health.MaxHealth, health.CurrentHealth);
            Assert.AreEqual(1, run.DepthArrivalHeals, "the heal ran exactly once");
            Assert.AreEqual(1, run.LastDepthArrivalHeals.Count);
            Assert.AreEqual(1, healsBefore, "one Healed event carried the fill");
            Note($"Descend Deeper -> depth 2 {state.Biome}: HP {health.CurrentHealth}/{health.MaxHealth} (effective max {run.Rig.StatsBinder.Stats.MaxHealth}); DepthArrivalHeals {run.DepthArrivalHeals}, restored {run.LastDepthArrivalHeals[0].Restored}");
            LiveDungeonCapture.Capture(Folder, "live_02_next_depth_full_hp", camera, ppu, includeUi: true);

            // On the new depth: room entries, revisits and a second opened transit (boss defeat) never heal again.
            health.SetInvulnerabilityState(null);
            health.TryApplyDamage(new DamageRequest(30));
            health.SetInvulnerabilityState(new Guard());
            var hurt = health.CurrentHealth;
            var start2 = run.Rooms[run.Generation.Graph.StartId];
            // Depth 2 of seed 53 is a Rustworks depth of combat rooms and a boss: any non-boss room other than the start does for a revisit (the player is invulnerable here).
            var another = run.Rooms.Values.OrderBy(r => r.State.RoomType == RoomType.Combat ? 1 : 0).First(r => r.State.NodeId != start2.State.NodeId && r.State.RoomType != RoomType.Boss);
            Put(RoomCentre(another)); yield return Settle();
            Put(RoomCentre(start2)); yield return Settle();
            Assert.AreEqual(hurt, health.CurrentHealth, "room transitions on the new depth do not heal");
            var boss2 = run.Rooms.Values.First(r => r.State.RoomType == RoomType.Boss);
            Put(RoomCentre(boss2)); yield return Settle();
            boss2.GetComponent<RoomContentBinding>().Boss.Boss.Health.TryApplyDamage(new DamageRequest(9999999));
            yield return null; yield return null;
            Assert.AreEqual(TransitDecisionState.Open, run.Expedition.Transit.State, "the depth-2 transit reopened");
            Assert.AreEqual(hurt, health.CurrentHealth, "reopening the Transit never heals");
            Assert.AreEqual(1, run.DepthArrivalHeals, "still exactly one heal");
            Note($"depth 2 after damage: HP {hurt}; room entries, revisits and the reopened transit changed nothing; DepthArrivalHeals still {run.DepthArrivalHeals}");

            run.Vote.Vote(TransitChoice.ReturnToShelter);
            if (run.Vote.AwaitingReturnConfirmation) run.Vote.ConfirmReturn();
            yield return WaitComposed(SceneNames.Base);
            Note("returned to the Shelter through the transit; the depth heal path was not used by Return");
        }
    }
}

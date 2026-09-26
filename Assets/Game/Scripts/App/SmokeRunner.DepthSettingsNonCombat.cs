using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core.Input;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.UI.Inventory;
using RuinRail.UI.Pause;
using RuinRail.UI.Settings;
using RuinRail.UI.Theme;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// The depth-heal / item-description / Settings / enemy-count / non-combat-room stage of the built-player smoke:
    /// every catalog item resolves its description and the inventory shows it first; the category-based Settings
    /// pages open from the pause menu and their controls change and persist real settings; the enemy-remaining chip
    /// counts a real combat room and stays hidden in the Boss arena and non-combat rooms; every generated non-combat
    /// room of the depth is driven through its real interaction (prompt, press, outcome, used state); and the
    /// Descend Deeper transition starts the next depth at the effective maximum HP exactly once.
    /// </summary>
    public sealed partial class SmokeRunner
    {
        private IEnumerator DepthSettingsNonCombatChecks(ExpeditionScene run)
        {
            var passed = new List<string>();
            void Check(string what, bool ok) { if (ok) passed.Add(what); else if (string.IsNullOrEmpty(_result.Error)) Fail("depthsettings: " + what); }
            var player = run.Rig.Player;
            var body = player.GetComponent<Rigidbody2D>();
            var health = player.GetComponent<HealthComponent>();
            var interactor = player.GetComponent<PlayerInteractor>();
            var inventory = run.Expedition.State.Inventory;
            var hud = run.HudView;
            var content = _app.Content;
            void Put(Vector2 p) { player.transform.position = p; body.position = p; body.linearVelocity = Vector2.zero; Physics2D.SyncTransforms(); }
            IEnumerator Settle() { for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate(); for (var i = 0; i < 3; i++) yield return null; }
            Vector2 Centre(RoomRuntime room)
            {
                var marker = room.Root.GetMarkers(RoomMarkerRole.PlayerSpawn).FirstOrDefault();
                return marker != null ? (Vector2)room.Root.transform.TransformPoint(marker.WorldCenter) : room.InteriorWorldBounds.center;
            }

            // ---- 1. Item descriptions: every catalog item, and the inventory shows the description first ----
            var specials = _app.Specials;
            var incomplete = content.Items.Where(i => i != null).Select(i => (i.Id, d: ItemDescriptions.Build(i, specials))).Where(x => !x.d.IsComplete).Select(x => x.Id).ToList();
            Check($"every one of the {content.Items.Count(i => i != null)} catalog items resolves a complete data-derived description", incomplete.Count == 0 && content.Items.Count(i => i != null) >= 72);
            Check("no description carries an internal id or placeholder", content.Items.Where(i => i != null).All(i => { var t = ItemDescriptions.Build(i, specials).FullText; return !t.Contains(i.Id) && !t.Contains("TODO"); }));
            var scope = new ItemInstance("accessory_field_scope", 1, Rarity.Uncommon);
            inventory.TryAddToBackpack(scope);
            var legendary = new ItemInstance("weapon_quickfang", 1, Rarity.Legendary);
            inventory.TryAddToBackpack(legendary);
            run.Inventory.Open();
            yield return null;
            var view = run.InventoryView;
            var width = InventoryView.DetailsPanel.Width - UiTheme.Pad * 2;
            IEnumerator ShowSlot(InventorySlotRef slot, string what, string capture)
            {
                run.Inventory.SetCursor(slot);
                yield return null;
                var tooltip = run.Inventory.TooltipAt(slot);
                var first = tooltip != null ? UiText.Wrap(tooltip.Description, width).FirstOrDefault() ?? string.Empty : string.Empty;
                Check($"{what}: description shown first in the details ('{first}')", tooltip != null && first.Length > 0 && view.DetailRowTexts[0] == first);
                Check($"{what}: every detail row fits the panel", view.DetailRowTexts.All(t => UiText.Width(t) <= width));
                yield return CaptureHud(capture);
            }

            yield return ShowSlot(new InventorySlotRef(InventorySlotKind.Equipped, (int)EquippedSlot.ActiveConsumable), "consumable (Bandage)", "dsnc_03_consumable_description");
            var bandageTip = run.Inventory.TooltipAt(new InventorySlotRef(InventorySlotKind.Equipped, (int)EquippedSlot.ActiveConsumable));
            Check("Bandage says exactly what it does (25 HP, 1.5 s)", bandageTip != null && bandageTip.Description.Contains("Restore 25 HP") && bandageTip.Description.Contains("1.5 s") && bandageTip.BaseStats.Any(l => l.Label == "Heal" && l.Value == "+25 HP"));
            yield return ShowSlot(new InventorySlotRef(InventorySlotKind.Equipped, (int)EquippedSlot.PrimaryWeapon), "weapon (P9 Ranger)", "dsnc_04_weapon_description");
            yield return ShowSlot(new InventorySlotRef(InventorySlotKind.Equipped, (int)EquippedSlot.Armor), "armor (Scrap Vest)", "dsnc_05_armor_description");
            var scopeIndex = inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.InstanceId == scope.InstanceId);
            if (scopeIndex >= 0) yield return ShowSlot(new InventorySlotRef(InventorySlotKind.Backpack, scopeIndex), "accessory (Field Scope)", "dsnc_06_accessory_description");
            var legendaryIndex = inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.InstanceId == legendary.InstanceId);
            if (legendaryIndex >= 0)
            {
                run.Inventory.SetCursor(new InventorySlotRef(InventorySlotKind.Backpack, legendaryIndex));
                yield return null;
                Check("a Legendary weapon's special is described and every row stays reachable (paged, not cut)", view.DetailPager.Rows.Any(r => r.Key.Contains("Snapfire")) && (!view.DetailPager.Overflows || view.DetailsPageDown()));
                yield return CaptureHud("dsnc_04b_legendary_weapon_description_paged");
            }

            run.Inventory.Close();
            yield return null;
            foreach (var extra in new[] { scope, legendary })
            {
                var index = inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.InstanceId == extra.InstanceId);
                if (index >= 0) inventory.RemoveFromBackpack(index);
            }

            // ---- 2. Settings from the pause menu: categories, pages, real controls, persistence ----
            run.Pause.Open();
            yield return null;
            run.Pause.Activate(PauseMenuItem.Settings);
            yield return null;
            var screen = run.PauseScreen;
            var settings = _app.SettingsScreen;
            Check("Settings opens on its category list", screen.SettingsShowing && settings.IsOnCategories && screen.SettingsInstance.Driver.IsOnCategories);
            Check("the four real categories are controls", new[] { "Video", "Audio", "Controls", "Gameplay" }.All(c => screen.Controls.Any(x => x.Id == "settings.category." + c)));
            Check("gameplay input is held under Settings", GameplayInputGate.IsHeld);
            yield return CaptureHud("dsnc_07_settings_categories");
            var driver = screen.SettingsInstance.Driver;
            settings.OpenPage(SettingsTab.Video);
            yield return null;
            Check("VIDEO page shows display mode / resolution / VSync / frame-rate limit with their values", driver.Page == SettingsTab.Video && driver.RowViews.Count(v => v.gameObject.activeInHierarchy) >= 5 && driver.RowViews.Any(v => v.ValueText == settings.DisplayModeText));
            yield return CaptureHud("dsnc_08_settings_video");
            settings.BackFromPage();
            settings.OpenPage(SettingsTab.Audio);
            yield return null;
            var audioRows = driver.RowViews.Where(v => v.gameObject.activeInHierarchy).ToList();
            Check("AUDIO page: Master / Music / SFX / Ambience sliders and MUTE", audioRows.Count(v => v.ShowsBar) == 4 && audioRows.Any(v => v.Control.Id == "settings.audio.mute"));
            var musicRow = settings.RowsFor(SettingsTab.Audio).First(r => r.Id == "settings.audio.music");
            musicRow.Adjust(-6);
            yield return null;
            Check("music slider steps to 70% and previews at once (master untouched)", Mathf.Abs(settings.Draft.Audio.MusicVolume - 0.7f) < 1e-3f && Mathf.Abs(RuinRail.Core.Rendering.AudioLevels.Music - 0.7f) < 1e-3f && settings.Draft.Audio.MasterVolume == 1f);
            yield return CaptureHud("dsnc_09_settings_audio");
            settings.BackFromPage();
            Check("leaving the AUDIO page persisted the level", Mathf.Abs(_app.Settings.Current.Audio.MusicVolume - 0.7f) < 1e-3f);
            settings.OpenPage(SettingsTab.Controls);
            yield return null;
            Check("CONTROLS page: scheme selector plus real rebind rows", driver.RowViews.Any(v => v.Control.Id == "settings.controls.scheme") && settings.RowsFor(SettingsTab.Controls).Count(r => r.Id.StartsWith("settings.rebind.")) > 5);
            yield return CaptureHud("dsnc_10_settings_controls");
            settings.BackFromPage();
            settings.OpenPage(SettingsTab.Gameplay);
            yield return null;
            Check("GAMEPLAY page: shake / intensity / damage numbers / hit flash / tutorial prompts", settings.RowsFor(SettingsTab.Gameplay).Count >= 5);
            yield return CaptureHud("dsnc_10b_settings_gameplay");
            run.Pause.Back(); // page -> categories
            Check("Back from a page returns to the categories, still paused", run.Pause.Screen == PauseScreen.Settings && settings.IsOnCategories);
            run.Pause.Back(); // categories -> pause root
            Check("Back from the categories returns to the pause root", run.Pause.Screen == PauseScreen.Root && run.Pause.IsOpen);
            settings.SetMusicVolume(1f); settings.Apply(); // leave the smoke profile at defaults
            run.Pause.Close();
            yield return null;
            Check("gameplay input released after the pause menu closed", !GameplayInputGate.IsHeld);

            // ---- 3. Enemy-remaining chip: a normal combat room, then the Boss arena and a non-combat room ----
            var combat = run.Rooms.Values.FirstOrDefault(r => r.State.RoomType == RoomType.Combat && !r.State.IsElite && r.Lifecycle == RoomLifecycleState.Unentered && r.HasEncounter);
            if (combat == null) { Check("a fresh normal combat room exists on this depth", false); yield break; }
            Put(Centre(combat));
            yield return Settle();
            Check("entering the combat room activated it", run.CurrentRoom == combat && combat.Lifecycle == RoomLifecycleState.Active);
            var remaining = combat.EnemiesRemaining;
            Check($"enemy chip shows the encounter's remaining count (x{remaining})", remaining > 0 && hud.EnemyCountVisible && hud.EnemiesText == "x" + remaining && hud.EnemyCount.HasIconSprite);
            yield return CaptureHud("dsnc_11_combat_room_enemies_remaining");
            var first = combat.Encounter.Living.FirstOrDefault(e => e != null && e.IsAlive);
            if (first != null) first.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(99999));
            yield return null; yield return null;
            Check($"one death decrements the chip at once (x{combat.EnemiesRemaining})", combat.EnemiesRemaining == remaining - 1 && hud.EnemiesText == "x" + (remaining - 1));
            yield return CaptureHud("dsnc_12_combat_room_enemies_decremented");
            for (var guard = 0; guard < 40 && combat.Lifecycle == RoomLifecycleState.Active; guard++)
            {
                foreach (var e in combat.Encounter.Living.ToList()) if (e != null && e.IsAlive) e.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(99999));
                yield return null;
            }

            Check("chip gone once the room is cleared", combat.Lifecycle == RoomLifecycleState.Cleared && !hud.EnemyCountVisible);

            var nonCombat = run.Rooms.Values.FirstOrDefault(r => r.State.RoomType != RoomType.Combat && r.State.RoomType != RoomType.Boss && r.State.RoomType != RoomType.Start);
            if (nonCombat != null)
            {
                Put(Centre(nonCombat));
                yield return Settle();
                Check($"no enemy chip in a {nonCombat.State.RoomType} room", run.CurrentRoom == nonCombat && !hud.EnemyCountVisible);
                yield return CaptureHud("dsnc_14_noncombat_room_no_enemy_hud");
            }

            // ---- 4. Every generated non-combat room of the depth: prompt, press, outcome, used state ----
            yield return NonCombatRoomsOnDepth(run, passed, Put, Settle, Centre);
            if (!string.IsNullOrEmpty(_result.Error)) yield break;

            // ---- 5. Boss arena: no chip; defeat opens the transit ----
            var bossRoom = run.Rooms.Values.First(r => r.State.RoomType == RoomType.Boss);
            var bossBinding = bossRoom.GetComponent<RoomContentBinding>();
            Put(Centre(bossRoom));
            yield return Settle();
            yield return SkipBossIntro(); // skip the room introduction, as a player can
            Check("Boss arena active with the boss bar and NO enemy chip", run.CurrentRoom == bossRoom && bossRoom.Lifecycle == RoomLifecycleState.Active && hud.BossVisible && !hud.EnemyCountVisible);
            yield return CaptureHud("dsnc_13_boss_room_no_enemy_hud");
            bossBinding.Boss.Boss.Health.TryApplyDamage(new DamageRequest(999999));
            yield return null; yield return null;
            Check("boss defeated: transit decision open", run.Expedition.Transit != null && run.Expedition.Transit.State == TransitDecisionState.Open);

            // ---- 6. Descend Deeper: full effective HP on the new depth, exactly once ----
            var effectiveMax = run.Rig.StatsBinder.Stats.MaxHealth;
            health.TryApplyDamage(new DamageRequest(effectiveMax / 2));
            yield return null;
            var damaged = health.CurrentHealth;
            Check($"damaged at the end of depth 1 ({damaged}/{health.MaxHealth}, effective max {effectiveMax})", damaged < effectiveMax && damaged > 0);
            health.SetInvulnerabilityState(new SmokeGuard()); // from here only the one heal under test may move HP
            yield return CaptureHud("dsnc_01_damaged_end_of_depth");
            run.Pause.Open(); yield return null; run.Pause.Close(); yield return null; // a menu never heals
            Check("opening the Transit vote / pause menu did not heal", health.CurrentHealth == damaged && run.DepthArrivalHeals == 0);
            var depthBefore = run.Expedition.State.Depth;
            Check("descend chosen through the transit vote panel", run.Vote != null && run.Vote.Vote(TransitChoice.DescendDeeper));
            yield return WaitFor(() => run.DepthsBuilt >= 2 && run.Expedition.State.Depth == depthBefore + 1, "next depth built");
            for (var i = 0; i < 5; i++) yield return null;
            player = run.Rig.Player; health = player.GetComponent<HealthComponent>(); body = player.GetComponent<Rigidbody2D>();
            health.SetInvulnerabilityState(new SmokeGuard());
            Check($"depth {run.Expedition.State.Depth} starts at full effective HP ({health.CurrentHealth}/{run.Rig.StatsBinder.Stats.MaxHealth})", health.CurrentHealth == run.Rig.StatsBinder.Stats.MaxHealth && health.CurrentHealth == health.MaxHealth);
            Check("the depth heal ran exactly once", run.DepthArrivalHeals == 1 && run.LastDepthArrivalHeals.Count == 1 && run.LastDepthArrivalHeals[0].IsFull);
            yield return CaptureHud("dsnc_02_next_depth_full_hp");
            health.SetInvulnerabilityState(null);
            health.TryApplyDamage(new DamageRequest(25));
            health.SetInvulnerabilityState(new SmokeGuard());
            var hurt = health.CurrentHealth;
            var other = run.Rooms.Values.FirstOrDefault(r => r.State.RoomType != RoomType.Boss && r.State.NodeId != run.Generation.Graph.StartId && r.State.RoomType != RoomType.Combat) ?? run.Rooms.Values.First(r => r.State.NodeId != run.Generation.Graph.StartId && r.State.RoomType != RoomType.Boss);
            Put(Centre(other));
            yield return Settle();
            Put(Centre(run.Rooms[run.Generation.Graph.StartId]));
            yield return Settle();
            Check("room entries and revisits on the new depth never heal", health.CurrentHealth == hurt && run.DepthArrivalHeals == 1);
            health.SetInvulnerabilityState(null);
            health.Heal(999);

            _result.DepthSettingsNonCombatChecks = passed.ToArray();
            Debug.Log($"[SMOKE] depth/settings/descriptions/non-combat: {passed.Count} checks passed");
        }

        /// <summary>Drives every non-combat room the depth generated through its real interaction contract (the shipped composition, the real prompt path).</summary>
        private IEnumerator NonCombatRoomsOnDepth(ExpeditionScene run, List<string> passed, System.Action<Vector2> put, System.Func<IEnumerator> settle, System.Func<RoomRuntime, Vector2> centre)
        {
            void Check(string what, bool ok) { if (ok) passed.Add(what); else if (string.IsNullOrEmpty(_result.Error)) Fail("noncombat: " + what); }
            var player = run.Rig.Player;
            var interactor = player.GetComponent<PlayerInteractor>();
            var health = player.GetComponent<HealthComponent>();
            var wallet = run.Expedition.State.CarriedWallet;
            var driven = new List<string>();
            IEnumerator Approach(Component target)
            {
                put((Vector2)target.transform.position + Vector2.down * 1.0f);
                yield return settle();
                yield return null; // the prompt line refreshes in Update
            }

            foreach (var room in run.Rooms.Values.Where(r => r.State.RoomType != RoomType.Combat && r.State.RoomType != RoomType.Start && r.State.RoomType != RoomType.Boss).OrderBy(r => r.State.NodeId))
            {
                var binding = room.GetComponent<RoomContentBinding>();
                if (binding == null) continue;
                Check($"{room.State.RoomId}: composed without skips", binding.Skipped.Count == 0);
                put(centre(room));
                yield return settle();
                if (binding.Chests.Count > 0 && binding.Merchant == null && binding.Event == null)
                {
                    var chest = binding.Chests[0];
                    yield return Approach(chest);
                    Check($"{room.State.RoomId}: chest prompt '{run.CurrentInteractionPrompt}'", run.CurrentInteractionPrompt.EndsWith("OPEN CHEST"));
                    var before = CountPickups();
                    Check($"{room.State.RoomId}: chest opens once and drops loot", interactor.TryInteract() && chest.IsOpened && CountPickups() > before);
                    // The dropped loot is now the nearest prompt; the chest itself is spent and cannot open again.
                    Check($"{room.State.RoomId}: an opened chest is spent", !chest.CanInteract(player) && !chest.Interact(player) && Prompt(chest, player) == string.Empty);
                    yield return CaptureHud($"dsnc_17_{room.State.RoomType.ToString().ToLowerInvariant()}_chest_opened");
                    driven.Add(room.State.RoomType.ToString());
                }
                else if (binding.Merchant != null)
                {
                    yield return Approach(binding.Merchant);
                    Check($"{room.State.RoomId}: merchant prompt", run.CurrentInteractionPrompt.EndsWith("TRADE WITH MERCHANT"));
                    Check($"{room.State.RoomId}: merchant opens, closes, reopens", interactor.TryInteract() && run.Merchant.IsOpen);
                    run.Merchant.Close(); yield return null;
                    Check($"{room.State.RoomId}: merchant reopens", interactor.TryInteract() && run.Merchant.IsOpen);
                    yield return CaptureHud("dsnc_17_merchant_reopened");
                    run.Merchant.Close(); yield return null;
                    driven.Add("Merchant");
                }
                else if (binding.Event != null && binding.EventInstance != null)
                {
                    var instance = binding.EventInstance;
                    var kind = instance.Kind;
                    var handle = binding.Event;
                    var cost = instance.CostCoins;
                    var title = EventPromptBuilder.TitleOf(kind).ToUpperInvariant();
                    Check($"{room.State.RoomId}: {kind} object drawn with its final art", handle.GetComponentInChildren<WorldObjectVisual>() is { } v && v.IsVisible);
                    if (instance is WeaponCacheEvent { IsConsumed: true })
                    {
                        // The Weapon Cache stage earlier in this smoke already took from this very cache. Its selection
                        // flow is proven there; here the spent cache must hold its used state, exactly as below.
                        yield return Approach(handle);
                        var held = wallet.Balance;
                        Check($"{room.State.RoomId}: {kind} (taken by the Weapon Cache stage) used state — no prompt, no second outcome, dimmed", Prompt(handle, player) == string.Empty && !handle.CanInteract(player) && !handle.Interact(player) && wallet.Balance == held && handle.GetComponentInChildren<WorldObjectVisual>().Renderer.color == WorldObjectVisual.ResolvedTint && room.State.IsResolved("event:" + kind));
                        Check($"{room.State.RoomId}: room can be left", room.Lifecycle == RoomLifecycleState.Cleared && !room.DoorsLocked);
                        driven.Add(kind + " (used)");
                        continue;
                    }

                    if (kind == DungeonEventKind.MedicalStation) health.TryApplyDamage(new DamageRequest(30));
                    if (cost > 0)
                    {
                        // Poor first: the prompt names the shortfall and the press is refused without paying.
                        var coins = wallet.Balance;
                        if (coins >= cost) wallet.Debit(coins, "smoke:strip");
                        yield return Approach(handle);
                        Check($"{room.State.RoomId}: unaffordable {kind} prompt says why ('{run.CurrentInteractionPrompt}')", run.CurrentInteractionPrompt.Contains(title) && run.CurrentInteractionPrompt.Contains("MORE COINS"));
                        if (kind == DungeonEventKind.BrokenMachine) yield return CaptureHud("dsnc_15_broken_machine_prompt_unaffordable");
                        var noticesBefore = run.Notices;
                        Check($"{room.State.RoomId}: the poor press is refused and announced", !interactor.TryInteract() && run.Notices == noticesBefore + 1 && run.LastNotice.Contains("NOT ENOUGH COINS") && instance.Phase == DungeonEventPhase.Available);
                        run.Expedition.AddCarriedCoins(cost + 50);
                        yield return null;
                    }

                    yield return Approach(handle);
                    var expected = cost > 0 ? $"{EventPromptBuilder.ActionOf(kind).ToUpperInvariant()} {title} ({cost} COINS)" : $"{EventPromptBuilder.ActionOf(kind).ToUpperInvariant()} {title}";
                    Check($"{room.State.RoomId}: {kind} prompt '{run.CurrentInteractionPrompt}'", run.CurrentInteractionPrompt.EndsWith(expected));
                    if (kind == DungeonEventKind.BrokenMachine) yield return CaptureHud("dsnc_15_broken_machine_prompt");
                    var coinsBefore = wallet.Balance;
                    var lootBefore = CountPickups();
                    var notices = run.Notices;
                    var pressed = interactor.TryInteract();
                    yield return null; yield return null;
                    switch (kind)
                    {
                        case DungeonEventKind.BrokenMachine:
                        {
                            var machine = (BrokenMachineEvent)instance;
                            var ok = pressed && wallet.Balance == coinsBefore - cost && (machine.WillRepairSucceed() ? instance.Phase == DungeonEventPhase.Completed && CountPickups() > lootBefore : instance.Phase == DungeonEventPhase.Failed && CountPickups() == lootBefore);
                            Check($"{room.State.RoomId}: Broken Machine paid {cost} and resolved ({instance.Phase}, notice '{run.LastNotice}')", ok && run.Notices == notices + 1 && (run.LastNotice.StartsWith("MACHINE REPAIRED") || run.LastNotice.StartsWith("REPAIR FAILED")));
                            yield return CaptureHud("dsnc_16_broken_machine_outcome");
                            break;
                        }
                        case DungeonEventKind.LockedVault:
                            Check($"{room.State.RoomId}: vault paid {cost} and dropped loot", pressed && wallet.Balance == coinsBefore - cost && CountPickups() > lootBefore && run.LastNotice.StartsWith("VAULT UNLOCKED"));
                            yield return CaptureHud("dsnc_17_locked_vault_unlocked");
                            break;
                        case DungeonEventKind.MedicalStation:
                            Check($"{room.State.RoomId}: heal restored to full for {cost} coins", pressed && health.CurrentHealth == health.MaxHealth && wallet.Balance == coinsBefore - cost && run.LastNotice.StartsWith("HEALED"));
                            yield return CaptureHud("dsnc_17_medical_station_healed");
                            break;
                        case DungeonEventKind.CursedChest:
                        {
                            Check($"{room.State.RoomId}: cursed chest locked the doors and spawned its wave", pressed && room.DoorsLocked && run.LastNotice.StartsWith("CURSED CHEST OPENED"));
                            yield return CaptureHud("dsnc_17_cursed_chest_wave");
                            for (var guard = 0; guard < 60 && instance.Phase == DungeonEventPhase.InProgress; guard++)
                            {
                                foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (e != null && e.IsAlive && room.InteriorWorldBounds.Contains(e.transform.position)) e.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(99999));
                                yield return null;
                            }

                            Check($"{room.State.RoomId}: cursed chest cleared, doors open, loot dropped", instance.Phase == DungeonEventPhase.Completed && !room.DoorsLocked && CountPickups() > lootBefore && run.LastNotice.StartsWith("CURSED CHEST CLEARED"));
                            break;
                        }
                        case DungeonEventKind.SupplySignal:
                        {
                            var signal = (SupplySignalEvent)instance;
                            Check($"{room.State.RoomId}: supply signal started with a wave and a held countdown", pressed && signal.IsRunning && run.HudView.Notice.IsHeld && run.HudView.Notice.Text.Contains("SUPPLY SIGNAL"));
                            yield return CaptureHud("dsnc_17_supply_signal_running");
                            signal.Tick(signal.DurationSeconds + 1f);
                            yield return null; yield return null;
                            Check($"{room.State.RoomId}: surviving the signal delivered the drop", instance.Phase == DungeonEventPhase.Completed && CountPickups() > lootBefore && run.LastNotice.StartsWith("SUPPLY DROP DELIVERED") && !run.HudView.Notice.IsHeld);
                            foreach (var e in FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (e != null && e.IsAlive && room.InteriorWorldBounds.Contains(e.transform.position)) e.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(99999));
                            yield return null;
                            break;
                        }
                        case DungeonEventKind.WeaponCache:
                            Check($"{room.State.RoomId}: weapon cache opened its selection", pressed && run.WeaponCache.IsOpen);
                            yield return CaptureHud("dsnc_17_weapon_cache_selection");
                            Check($"{room.State.RoomId}: one weapon taken, cache consumed", run.WeaponCache.Take() == DungeonEventOutcome.Success && ((WeaponCacheEvent)instance).IsConsumed && run.LastNotice == "WEAPON TAKEN");
                            if (run.WeaponCache.IsOpen) run.WeaponCache.Close();
                            yield return null;
                            break;
                    }

                    if (kind != DungeonEventKind.MedicalStation)
                    {
                        yield return Approach(handle);
                        var coinsAfter = wallet.Balance;
                        // A dropped reward may now be the nearest prompt; the event object itself offers none and cannot fire again.
                        Check($"{room.State.RoomId}: {kind} used state — no prompt, no second outcome, dimmed", Prompt(handle, player) == string.Empty && !handle.CanInteract(player) && !handle.Interact(player) && wallet.Balance == coinsAfter && handle.GetComponentInChildren<WorldObjectVisual>().Renderer.color == WorldObjectVisual.ResolvedTint && room.State.IsResolved("event:" + kind));
                    }

                    Check($"{room.State.RoomId}: room can be left", room.Lifecycle == RoomLifecycleState.Cleared && !room.DoorsLocked);
                    driven.Add(kind.ToString());
                }
            }

            _result.NonCombatRoomsDriven = driven.ToArray();
            // dungeon/55: every non-combat room type ranges from 0, so a depth may legally have none. Every one that is
            // present must have been driven; the release manifest pins a seed (11) whose depth has them.
            var present = run.Rooms.Values.Count(r => r.State.RoomType != RoomType.Combat && r.State.RoomType != RoomType.Start && r.State.RoomType != RoomType.Boss && r.GetComponent<RoomContentBinding>() != null);
            if (present == 0) passed.Add("no non-combat room on this depth (legal: dungeon/55 ranges start at 0)");
            else Check($"every non-combat room on this depth was driven ({driven.Count}/{present}): {string.Join(", ", driven)}", driven.Count == present);
        }

        private static string Prompt(IInteractable target, GameObject player) => target is IInteractionPrompt p ? p.PromptFor(player) : string.Empty;

        private static int CountPickups() => FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Length + FindObjectsByType<CoinPickup>(FindObjectsSortMode.None).Length;
    }
}

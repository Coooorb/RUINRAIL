using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core.Input;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.UI.Hud;
using RuinRail.UI.Theme;
using RuinRail.UI.WeaponCache;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// The room / HUD / QoL stage of the built-player smoke: the Weapon Cache selection flow, the room-title reveal,
    /// the minimap and its discovery rule, the graphical coin readout, the low-HP vignette and the dash cooldown
    /// wipe — all inside the shipped executable, on the real composition, with captures written to the proof folder.
    /// </summary>
    public sealed partial class SmokeRunner
    {
        private IEnumerator RoomHudQolChecks(ExpeditionScene run)
        {
            var passed = new List<string>();
            void Check(string what, bool ok) { if (ok) passed.Add(what); else if (string.IsNullOrEmpty(_result.Error)) Fail("roomhud: " + what); }
            var player = run.Rig.Player;
            var body = player.GetComponent<Rigidbody2D>();
            var health = player.GetComponent<HealthComponent>();
            var dash = player.GetComponent<PlayerDash>();
            var interactor = player.GetComponent<PlayerInteractor>();
            var hud = run.HudView;
            var map = run.Minimap;
            if (hud == null || map == null || interactor == null) { Check("room/HUD screens composed", false); yield break; }

            void Put(Vector2 p) { player.transform.position = p; body.position = p; body.linearVelocity = Vector2.zero; Physics2D.SyncTransforms(); }

            // ---- Minimap + biome label ----
            Check("minimap panel is top-left with the biome label under it",
                hud.Minimap != null && hud.TopLeftPanel == hud.Minimap.Rect && hud.BiomeText == RoomDisplayNames.BiomeName(run.Expedition.State.Biome));
            Check($"minimap depth chip reads D{run.Expedition.State.Depth}", hud.DepthText == "D" + run.Expedition.State.Depth);
            Check("no permanent objective text block remains in the HUD",
                !hud.GetComponentsInChildren<UnityEngine.UI.Text>(true).Any(t => t.text.Contains("find and defeat") || t.text.StartsWith("DEPTH ")));
            Check("the start room is the current room on the map and is visited",
                map.CurrentNodeId.HasValue && map.Room(map.CurrentNodeId.Value) != null && map.Room(map.CurrentNodeId.Value).Visited);
            var startNode = map.CurrentNodeId.Value;
            var undiscovered = map.Rooms.Where(r => !r.Discovered).ToList();
            Check($"the generated dungeon is not revealed up front ({map.Rooms.Count(r => r.Discovered)} of {map.Rooms.Count} rooms drawn)",
                map.Rooms.Count <= 2 || undiscovered.Count > 0);
            Check("every drawn connection exists in the real room graph",
                map.DiscoveredLinks.All(l => run.Generation.Layout.Connections.Any(c => c.Joins(l.a, l.b))));
            Check("no room is drawn that the layout does not contain",
                map.Rooms.All(r => run.Generation.Layout.GetPlacement(r.NodeId) != null));
            yield return CaptureHud("qol_06_minimap_current_room");

            // ---- Room title reveal on a genuine entry ----
            var otherRooms = run.Rooms.Values.Where(r => r.State.NodeId != startNode).ToList();
            RoomRuntime entered = null;
            foreach (var room in otherRooms.OrderBy(r => r.State.RoomType == RoomType.Boss ? 1 : 0))
            {
                var marker = room.Root.GetMarkers(RoomMarkerRole.PlayerSpawn).FirstOrDefault();
                var centre = marker != null
                    ? (Vector2)room.Root.transform.TransformPoint(marker.WorldCenter)
                    : (Vector2)room.Root.transform.position + (Vector2)room.Root.Size * 0.5f;
                Put(centre);
                for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
                for (var i = 0; i < 3; i++) yield return null;
                if (run.CurrentRoom == room) { entered = room; break; }
            }

            if (entered == null)
            {
                passed.Add("no second room could be reached by teleport on this seed (the reveal is proven by the PlayMode suite)");
            }
            else
            {
                var expected = RoomDisplayNames.NameOf(entered.State.RoomId, entered.Root.Definition.RoomType).ToUpperInvariant();
                Check($"entering a new room reveals its name ('{hud.RoomTitle.NameText}')", hud.RoomTitle.IsShowing && hud.RoomTitle.NameText == expected);
                Check("the revealed name is a player-facing name, never the internal room id", !hud.RoomTitle.NameText.Contains("_"));
                var revealsAfterEntry = hud.RoomTitle.Reveals;
                Check("the map's current room followed the same entry event", map.CurrentNodeId == entered.State.NodeId && map.Room(entered.State.NodeId).Visited);
                yield return CaptureHud("qol_04_room_title_reveal");
                // Standing still inside the same room never repeats the reveal.
                for (var i = 0; i < 20; i++) yield return null;
                Check("staying in the same room does not repeat the reveal", hud.RoomTitle.Reveals == revealsAfterEntry);
                Check("neighbours of the visited room are discovered, the rest is not",
                    map.Neighbours(entered.State.NodeId).All(n => map.Room(n) == null || map.Room(n).Discovered));
                yield return CaptureHud("qol_07_minimap_after_discovery");
            }

            // ---- Special room: marker + role line ----
            var special = run.Rooms.Values.FirstOrDefault(r => r.Root.Definition != null &&
                (r.Root.Definition.RoomType == RoomType.Merchant || r.Root.Definition.RoomType == RoomType.Treasure ||
                 r.Root.Definition.RoomType == RoomType.Event || r.Root.Definition.RoomType == RoomType.Loot ||
                 r.Root.Definition.RoomType == RoomType.MedicalRecovery));
            if (special != null)
            {
                var beforeMarker = hud.Minimap.DrawnMarkerNodeIds.Contains(special.State.NodeId);
                Check("an unvisited special room shows no marker (no spoilers)", !beforeMarker || map.Room(special.State.NodeId).Visited);
                var marker = special.Root.GetMarkers(RoomMarkerRole.PlayerSpawn).FirstOrDefault();
                var centre = marker != null
                    ? (Vector2)special.Root.transform.TransformPoint(marker.WorldCenter)
                    : (Vector2)special.Root.transform.position + (Vector2)special.Root.Size * 0.5f;
                Put(centre);
                for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
                for (var i = 0; i < 4; i++) yield return null;
                if (run.CurrentRoom == special)
                {
                    Check($"special room title names its role ('{hud.RoomTitle.RoleText}')", hud.RoomTitle.RoleText.Length > 0);
                    yield return CaptureHud("qol_05_room_title_special_room");
                    hud.Minimap.Render();
                    Check("the special room's marker appears once it has been entered", hud.Minimap.DrawnMarkerNodeIds.Contains(special.State.NodeId));
                    yield return CaptureHud("qol_08_minimap_special_marker");
                }
            }

            yield return CaptureHud("qol_09_biome_label_beside_minimap");

            // ---- Graphical coins ----
            var coinsBefore = run.Expedition.State.CarriedCoins;
            Check("coin readout is a token plus the number, not the word COINS",
                hud.CoinView != null && hud.CoinView.HasIconSprite && hud.CoinsText == coinsBefore.ToString() &&
                !hud.GetComponentsInChildren<UnityEngine.UI.Text>(true).Any(t => t.text.Contains("COINS")));
            run.Expedition.AddCarriedCoins(137);
            yield return null;
            Check("coin amount updates immediately after a wallet change", hud.CoinsText == (coinsBefore + 137).ToString());
            yield return CaptureHud("qol_10_graphical_coin_display");

            // ---- Low-HP vignette ----
            var vignette = hud.Vignette;
            Check("low-HP vignette bound to the generated frame sprite", vignette != null && vignette.HasSprite);
            Check("vignette hidden at full health", !vignette.IsActive);
            var target = Mathf.CeilToInt(health.MaxHealth * (HudLowHealthVignetteView.DefaultThreshold - 0.05f));
            health.TryApplyDamage(new DamageRequest(Mathf.Max(1, health.CurrentHealth - target)));
            for (var i = 0; i < 30; i++) yield return null;
            Check($"vignette active below the threshold ({health.CurrentHealth}/{health.MaxHealth})", vignette.IsActive && vignette.IsVisible);
            Check("vignette uses effective max HP, not base HP", health.MaxHealth > 100 && health.CurrentHealth / (float)health.MaxHealth <= HudLowHealthVignetteView.DefaultThreshold);
            var alphaA = vignette.Alpha;
            for (var i = 0; i < 30; i++) yield return null;
            Check("vignette pulses rather than sitting at one opacity", Mathf.Abs(vignette.Alpha - alphaA) > 0.001f);
            yield return CaptureHud("qol_11_low_hp_vignette");
            health.Heal(health.MaxHealth);
            yield return null;
            Check("vignette stands down the moment the player is healed above the threshold", !vignette.IsActive);
            // It fades out over HudLowHealthVignetteView.FadeSeconds rather than cutting, so wait for the fade.
            yield return WaitFor(() => !vignette.IsVisible, "low-HP vignette faded out");
            Check("vignette is gone once the fade completes", !vignette.IsVisible);

            // ---- Dash cooldown wipe ----
            yield return WaitFor(() => dash.CanDash, "dash ready before the wipe proof");
            Check("dash icon clear while ready", hud.DashIcon.State == HudDashState.Ready && hud.DashIcon.CoveredPixels <= 0f);
            yield return CaptureHud("qol_15_dash_ready");
            Check("dash starts", dash.TryStartDash(Vector2.right));
            yield return null;
            Check("the cooldown overlay is a vertical wipe driven by the authoritative cooldown",
                hud.DashIcon.CoverIsVertical && hud.DashIcon.State == HudDashState.Cooldown && hud.DashIcon.Cooldown01 > 0.85f);
            yield return CaptureHud("qol_12_dash_cooldown_full");
            yield return WaitFor(() => hud.DashIcon.Cooldown01 <= 0.55f, "dash cooldown half done");
            Check($"the wipe is about half way at half the cooldown (cover {hud.DashIcon.Cooldown01:0.00})",
                hud.DashIcon.Cooldown01 is > 0.35f and < 0.6f &&
                Mathf.Abs(hud.DashIcon.Cooldown01 - dash.CooldownRemaining / dash.CurrentDashCooldown) < 0.06f);
            yield return CaptureHud("qol_13_dash_cooldown_half");
            yield return WaitFor(() => hud.DashIcon.Cooldown01 <= 0.2f && hud.DashIcon.Cooldown01 > 0f, "dash cooldown nearly done");
            Check($"only a sliver remains near ready (cover {hud.DashIcon.Cooldown01:0.00})", hud.DashIcon.Cooldown01 < 0.25f && hud.DashIcon.CoveredPixels > 0f);
            yield return CaptureHud("qol_14_dash_cooldown_near_ready");
            yield return WaitFor(() => dash.CanDash, "dash ready again");
            Check("the overlay is completely gone when the dash is ready", hud.DashIcon.State == HudDashState.Ready && hud.DashIcon.Cooldown01 == 0f && hud.DashIcon.CoveredPixels == 0f);

            // ---- Clean full HUD ----
            Check("HUD bands do not overlap at 640x360", HudBandsDisjoint(hud));
            yield return CaptureCleanReferenceFrame("qol_16_clean_hud_640x360");

            _result.RoomHudChecks = passed.ToArray();
            Debug.Log("[SMOKE] roomhud: " + string.Join("; ", passed));
        }

        /// <summary>
        /// The Weapon Cache flow in the shipped player: prompt in reach, Interact opens the selection screen exactly
        /// once, gameplay is gated while it is up, one choice grants exactly one weapon, and the cache is consumed.
        /// </summary>
        private IEnumerator WeaponCacheChecks(ExpeditionScene run)
        {
            var passed = new List<string>();
            void Check(string what, bool ok) { if (ok) passed.Add(what); else if (string.IsNullOrEmpty(_result.Error)) Fail("cache: " + what); }
            var player = run.Rig.Player;
            var body = player.GetComponent<Rigidbody2D>();
            var interactor = player.GetComponent<PlayerInteractor>();
            var inventory = run.Expedition.State.Inventory;
            var vm = run.WeaponCache;
            var view = run.WeaponCacheView;
            var menuInput = FindFirstObjectByType<MenuInput>();
            if (vm == null || view == null || interactor == null || menuInput == null) { Check("weapon cache screen composed", false); yield break; }

            var cacheRoom = run.Rooms.Values
                .Select(r => r.GetComponent<RoomContentBinding>())
                .FirstOrDefault(b => b != null && b.EventInstance is WeaponCacheEvent && b.Event != null);
            if (cacheRoom == null)
            {
                passed.Add("no Weapon Cache on this seed/depth (the selection flow is proven by the PlayMode suite)");
                _result.WeaponCacheChecks = passed.ToArray();
                yield break;
            }

            var cache = (WeaponCacheEvent)cacheRoom.EventInstance;
            var handle = cacheRoom.Event;
            void Put(Vector2 p) { player.transform.position = p; body.position = p; body.linearVelocity = Vector2.zero; Physics2D.SyncTransforms(); }
            Put((Vector2)handle.transform.position + Vector2.down * 1.1f);
            for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            for (var i = 0; i < 4; i++) yield return null;
            Check("cache prompt shows '[E] CHOOSE WEAPON CACHE' in reach",
                run.CurrentInteractionPrompt.Contains("CHOOSE WEAPON CACHE") && interactor.FindNearestInteractable() == (IInteractable)handle);
            yield return CaptureHud("qol_01_weapon_cache_prompt");

            var opensBefore = vm.Opens;
            Check("interact opens the weapon cache selection", interactor.TryInteract() && vm.Opens == opensBefore + 1 && vm.IsOpen && handle.ChoiceRequests == 1);
            yield return null;
            Check("selection window visible, bound to this cache, showing its rolled weapons",
                view.IsVisible && vm.Cache == cache && vm.Rows.Count == cache.Choices.Count && vm.Rows.Count > 0);
            Check("every choice shows a weapon icon, rarity frame and name",
                view.RowViews.Take(vm.Rows.Count).All(r => r.IconVisible && r.FrameSprite != null && r.NameText.Length > 0));
            Check("cache focus list on the menu input stack, first choice focused",
                menuInput.Stack.Current == view.FocusList && view.FocusList.Focused != null && view.FocusList.Focused.Id == WeaponCacheView.RowFocusPrefix + "0");
            Check("gameplay gated and the pointer cursor shown while choosing",
                GameplayInputGate.IsHeld && run.Rig.Reader.Move == Vector2.zero && PointerLayerOwnsCursor && run.CurrentInteractionPrompt.Length == 0);
            yield return CaptureHud("qol_02_weapon_cache_ui_open");

            // A second press while the screen is up must not open it again.
            interactor.TryInteract();
            yield return null;
            Check("a second interact while the screen is up opens nothing new", vm.Opens == opensBefore + 1);

            // Take exactly one weapon through the screen's own confirm.
            var chosen = vm.Rows[0];
            int Held(string id) => inventory.BackpackSlots.Where(i => i != null && i.DefinitionId == id).Sum(i => i.Quantity);
            var heldBefore = Held(chosen.Item.DefinitionId);
            Check("confirm activates the focused choice", menuInput.Stack.Activate());
            yield return null;
            Check($"exactly one {chosen.Item.DefinitionId} was granted and the cache is consumed",
                Held(chosen.Item.DefinitionId) == heldBefore + 1 && vm.Takes == 1 && cache.IsConsumed && cache.ChosenIndex == 0);
            Check("the screen closed itself and gameplay is restored",
                !vm.IsOpen && !view.IsVisible && !GameplayInputGate.IsHeld && CursorService.Current == CursorKind.Aim);
            yield return CaptureHud("qol_03_weapon_cache_reward_granted");

            // A consumed cache offers nothing: no prompt, no second reward.
            for (var i = 0; i < 4; i++) yield return null;
            Check("a consumed cache shows no prompt and cannot be reopened",
                !handle.CanInteract(player) && run.CurrentInteractionPrompt.Length == 0 && !interactor.TryInteract() && vm.Opens == opensBefore + 1);
            Check("no duplicate reward after the cache was emptied", Held(chosen.Item.DefinitionId) == heldBefore + 1);

            _result.WeaponCacheChecks = passed.ToArray();
            Debug.Log("[SMOKE] cache: " + string.Join("; ", passed));
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuinRail.Core.Input;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.UI.Hud;
using RuinRail.UI.Inventory;
using RuinRail.UI.Merchant;
using RuinRail.UI.Theme;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// Run-start HP, dash icon, graphical weapon/consumable HUD slots and the merchant trade flow, proven in the
    /// shipped player (`-hudproofdir dir`, default: the proof dir). The HUD stage runs first so the very first frames
    /// of the run are what the "full HP at start" capture shows; the merchant stage runs after the inventory stage so
    /// the trade happens with the run's real wallet and backpack.
    /// </summary>
    public sealed partial class SmokeRunner
    {
        public const string HudProofDirArgument = "-hudproofdir";

        private string HudProofDir
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                var index = Array.IndexOf(args, HudProofDirArgument);
                if (index < 0 || index + 1 >= args.Length) return ProofDir;
                var dir = Path.GetFullPath(args[index + 1]);
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private IEnumerator CaptureHud(string name)
        {
            var dir = HudProofDir;
            if (string.IsNullOrEmpty(dir)) yield break;
            var path = Path.Combine(dir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            for (var i = 0; i < 180 && !File.Exists(path); i++) yield return null;
            if (File.Exists(path)) _captures.Add(path);
            Debug.Log("[SMOKE] capture " + (File.Exists(path) ? "written → " + path : "NOT written: " + path));
        }

        /// <summary>A clean 640×360 reference frame: the back buffer point-sampled down by its integer scale (no filtering, no resampling of the pixel art).</summary>
        private IEnumerator CaptureCleanReferenceFrame(string name)
        {
            var dir = HudProofDir;
            if (string.IsNullOrEmpty(dir)) yield break;
            yield return new WaitForEndOfFrame();
            var full = ScreenCapture.CaptureScreenshotAsTexture();
            if (full == null || full.width < DungeonHudView.ReferenceWidth) yield break;
            var scale = Mathf.Max(1, full.width / DungeonHudView.ReferenceWidth);
            var w = full.width / scale;
            var h = full.height / scale;
            var small = new Texture2D(w, h, TextureFormat.RGB24, false);
            var source = full.GetPixels32();
            var target = new Color32[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                target[y * w + x] = source[(y * scale) * full.width + x * scale];
            small.SetPixels32(target);
            small.Apply();
            var path = Path.Combine(dir, name + ".png");
            File.WriteAllBytes(path, small.EncodeToPNG());
            Destroy(small);
            Destroy(full);
            if (File.Exists(path)) _captures.Add(path);
            Debug.Log($"[SMOKE] clean {w}x{h} frame written → {path}");
        }

        private IEnumerator StartHpDashIconHudChecks(ExpeditionScene run)
        {
            var passed = new List<string>();
            void Check(string what, bool ok) { if (ok) passed.Add(what); else if (string.IsNullOrEmpty(_result.Error)) Fail("hud: " + what); }
            var player = run.Rig.Player;
            var health = player.GetComponent<HealthComponent>();
            var dash = player.GetComponent<PlayerDash>();
            var inventory = run.Expedition.State.Inventory;
            var hud = run.HudView;
            var vm = run.Hud;
            var content = _app.Content;
            if (hud == null || vm == null || health == null || dash == null) { Check("HUD view composed", false); yield break; }
            Sprite Icon(string id) => content.Items.First(i => i != null && i.Id == id).Icon;

            // 1. The run starts at the true effective max HP (base 100 + Scrap Vest 20 = 120), never at base/effective.
            var stats = run.Rig.StatsBinder.Stats;
            Check($"run starts at effective max HP ({health.CurrentHealth}/{health.MaxHealth}, PlayerStats {stats.MaxHealth})", health.CurrentHealth == health.MaxHealth && health.MaxHealth == stats.MaxHealth && stats.MaxHealth > 100);
            Check("HUD HP readout equals CurrentHP / EffectiveMaxHP", hud.HpText == $"{health.CurrentHealth} / {health.MaxHealth}" && vm.Snapshot.MaxHp == stats.MaxHealth);
            yield return CaptureHud("hud_01_full_hp_at_run_start");

            // 2. Dash: graphical icon, ready state, then the cooldown sweep driven by the authoritative cooldown.
            Check("dash indicator is an icon slot with the bound dash sprite (no DASH READY text)", hud.DashIcon != null && hud.DashIcon.HasIconSprite && UiSkin.Load()?.DashIcon != null && !hud.GetComponentsInChildren<UnityEngine.UI.Text>(true).Any(t => t.text.Contains("DASH")));
            Check("dash icon shows READY while the dash is available", dash.CanDash && hud.DashIcon.State == HudDashState.Ready);
            Check($"dash tuning applied (cooldown {dash.CurrentDashCooldown:0.####} s = 1.25 / 0.85, speed {dash.CurrentDashSpeed} = 20 x 0.85)", Mathf.Abs(dash.CurrentDashCooldown - 1.4706f) < 0.001f && Mathf.Abs(dash.CurrentDashSpeed - 17f) < 0.001f);
            yield return CaptureHud("hud_02_dash_icon_ready");
            Check("dash starts", dash.TryStartDash(Vector2.right));
            for (var i = 0; i < 20; i++) yield return null;
            Check("dash icon shows the cooldown sweep from the live cooldown", !dash.CanDash && hud.DashIcon.State == HudDashState.Cooldown && hud.DashIcon.Cooldown01 > 0.05f && Mathf.Abs(hud.DashIcon.Cooldown01 - dash.CooldownRemaining / dash.CurrentDashCooldown) < 0.05f);
            yield return CaptureHud("hud_03_dash_icon_cooldown");
            yield return WaitFor(() => dash.CanDash, "dash cooldown");
            Check("dash icon returns to READY when the cooldown ends", hud.DashIcon.State == HudDashState.Ready);

            // 3. Weapon slots: P9 in slot 1 (active, ammo), knife in slot 2 (icon only, no fake ammo).
            var p9 = hud.PrimarySlot;
            var knife = hud.SecondarySlot;
            Check("slot 1 shows the P9 Ranger icon, number, rarity frame and the active brackets", p9.IsActive && p9.IconSprite == Icon(StarterKitService.PistolId) && p9.SlotNumber == "1" && p9.FrameSprite != null && p9.BracketsVisible);
            Check("slot 1 ammo reads 'magazine / reserve'", p9.ResourceVisible && System.Text.RegularExpressions.Regex.IsMatch(p9.ResourceText, @"^\d+ / \d+$"));
            Check("slot 2 shows the Field Knife icon with no ammo line", knife.IconSprite == Icon(StarterKitService.KnifeId) && knife.SlotNumber == "2" && !knife.ResourceVisible && !knife.IsActive);
            Check("no text-only weapon line remains", !hud.GetComponentsInChildren<UnityEngine.UI.Text>(true).Any(t => t.text.StartsWith(">1 ") || t.text.StartsWith(" 2 ")));
            yield return CaptureHud("hud_04_weapon_slots_p9_active");

            // 4. Equip a Wasp-45 through the inventory: the HUD follows the authoritative equipment (no stale P9).
            var wasp = new ItemInstance("weapon_wasp_45", 1, Rarity.Uncommon);
            var waspDefinition = _app.Configs.Resolve(wasp.DefinitionId) as RangedWeaponDefinition;
            Check("Wasp-45 definition present", waspDefinition != null);
            if (waspDefinition != null) inventory.Add(waspDefinition.AmmoType, 60);
            Check("Wasp-45 added to the backpack", inventory.TryAddToBackpack(wasp));
            var invVm = run.Inventory;
            invVm.Open();
            yield return null;
            var waspIndex = inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.InstanceId == wasp.InstanceId);
            var moved = invVm.MoveTo(new InventorySlotRef(InventorySlotKind.Backpack, waspIndex), new InventorySlotRef(InventorySlotKind.Equipped, (int)EquippedSlot.PrimaryWeapon));
            yield return null;
            var mounted = run.Rig.Loadout.GetSlot(WeaponSlot.Primary) as RangedWeapon;
            Check("inventory equip swapped the Wasp-45 into PRIMARY and remounted the weapon", moved == InventoryActionResult.Done && inventory.GetEquipped(EquippedSlot.PrimaryWeapon)?.InstanceId == wasp.InstanceId && mounted != null && mounted.Definition == waspDefinition);
            Check("HUD slot 1 shows the Wasp-45 live (icon, name, its own ammo) while the inventory is still open", vm.Snapshot.Primary.DefinitionId == "weapon_wasp_45" && p9.IconSprite == Icon("weapon_wasp_45") && p9.NameText.Contains("Wasp") && p9.ResourceText == $"{mounted.MagazineAmmo} / {inventory.Get(waspDefinition.AmmoType)}");
            yield return CaptureHud("hud_09_inventory_equip_live_hud");
            invVm.Close();
            yield return null;
            Check("HUD keeps the Wasp-45 after the inventory closes", p9.IconSprite == Icon("weapon_wasp_45") && p9.IsActive && p9.FrameSprite == UiSkin.Load().RarityFrame((int)Rarity.Uncommon));
            yield return CaptureHud("hud_05_weapon_slots_wasp45_after_equip");

            // 5. Slot 2 (knife) active: highlight moves, still no ammo text.
            run.Rig.Loadout.SelectSlot(WeaponSlot.Secondary);
            yield return null;
            Check("switching to slot 2 moves the active highlight to the knife with no fake ammo", knife.IsActive && knife.BracketsVisible && !p9.IsActive && !knife.ResourceVisible && knife.IconVisible);
            yield return CaptureHud("hud_06_knife_active_no_fake_ammo");
            run.Rig.Loadout.SelectSlot(WeaponSlot.Primary);

            // 6. Consumable: icon slot with the stack chip; consuming one decrements the chip (no permanent text).
            var bandage = inventory.GetEquipped(EquippedSlot.ActiveConsumable);
            bandage?.SetQuantity(3); // a representative stack for the proof (the starter kit carries one): x3 → x2 after a use
            yield return null;
            var slot = hud.ConsumableSlot;
            Check("consumable slot shows the Bandage icon with an 'xN' stack chip", bandage != null && slot.IconSprite == Icon(StarterKitService.BandageId) && slot.CountText == "x" + bandage.Quantity && !hud.GetComponentsInChildren<UnityEngine.UI.Text>(true).Any(t => t.text.Contains("Bandage")));
            yield return CaptureHud("hud_07_bandage_icon_stack_chip");
            var quantityBefore = bandage?.Quantity ?? 0;
            health.TryApplyDamage(new DamageRequest(30));
            yield return null;
            Check("HP readout follows damage", hud.HpText == $"{health.CurrentHealth} / {health.MaxHealth}");
            var user = player.GetComponent<PlayerConsumableUser>();
            Check("bandage use starts", user != null && user.TryUse());
            yield return WaitFor(() => (inventory.GetEquipped(EquippedSlot.ActiveConsumable)?.Quantity ?? 0) == quantityBefore - 1, "bandage consumed");
            yield return null;
            Check("stack chip decrements after the use", slot.CountText == "x" + (quantityBefore - 1));
            yield return CaptureHud("hud_08_after_consume_bandage");

            // 7. Clean 640x360 frame of the whole HUD (no window over it).
            Check("HUD bands do not overlap at 640x360", HudBandsDisjoint(hud));
            yield return CaptureCleanReferenceFrame("hud_10_clean_640x360_frame");

            // Leave the loadout as the later stages expect it (P9 in slot 1, Wasp-45 parked in the backpack).
            invVm.Open();
            var p9Index = inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.DefinitionId == StarterKitService.PistolId);
            invVm.MoveTo(new InventorySlotRef(InventorySlotKind.Backpack, p9Index), new InventorySlotRef(InventorySlotKind.Equipped, (int)EquippedSlot.PrimaryWeapon));
            invVm.Close();
            yield return null;
            Check("P9 restored to slot 1 for the rest of the smoke; HUD follows", inventory.GetEquipped(EquippedSlot.PrimaryWeapon)?.DefinitionId == StarterKitService.PistolId && p9.IconSprite == Icon(StarterKitService.PistolId));

            _result.HudChecks = passed.ToArray();
            Debug.Log("[SMOKE] hud: " + string.Join("; ", passed));
        }

        private static bool HudBandsDisjoint(DungeonHudView hud)
        {
            var rects = new[] { hud.DashPanel, hud.HpPanel, hud.WeaponsPanel, hud.ConsumablePanel, hud.TopLeftPanel, hud.TopRightPanel }
                .Select(r => new Rect(Corner(r), r.sizeDelta)).ToList();
            for (var i = 0; i < rects.Count; i++)
            for (var j = i + 1; j < rects.Count; j++)
                if (rects[i].Overlaps(rects[j])) return false;
            return rects.All(r => r.xMin >= 0f && r.yMin >= 0f && r.xMax <= DungeonHudView.ReferenceWidth && r.yMax <= DungeonHudView.ReferenceHeight);

            static Vector2 Corner(RectTransform r) => Vector2.Scale(r.anchorMin, new Vector2(DungeonHudView.ReferenceWidth, DungeonHudView.ReferenceHeight)) + r.anchoredPosition - Vector2.Scale(r.pivot, r.sizeDelta);
        }

        private IEnumerator MerchantChecks(ExpeditionScene run)
        {
            var passed = new List<string>();
            void Check(string what, bool ok) { if (ok) passed.Add(what); else if (string.IsNullOrEmpty(_result.Error)) Fail("merchant: " + what); }
            var player = run.Rig.Player;
            var body = player.GetComponent<Rigidbody2D>();
            var inventory = run.Expedition.State.Inventory;
            var vm = run.Merchant;
            var view = run.MerchantView;
            var interactor = player.GetComponent<PlayerInteractor>();
            var menuInput = FindFirstObjectByType<MenuInput>();
            if (vm == null || view == null || interactor == null || menuInput == null) { Check("merchant screen composed", false); yield break; }
            var bindings = run.Rooms.Values.Select(r => r.GetComponent<RoomContentBinding>()).Where(b => b != null && b.Merchant != null).ToList();
            if (bindings.Count == 0)
            {
                passed.Add("no merchant room on this seed/depth (trade flow proven by the PlayMode suite)");
                _result.MerchantChecks = passed.ToArray();
                yield break;
            }

            var merchant = bindings[0].Merchant;
            void Put(Vector2 p) { player.transform.position = p; body.position = p; body.linearVelocity = Vector2.zero; Physics2D.SyncTransforms(); }
            Put((Vector2)merchant.transform.position + Vector2.down * 1.1f);
            for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            for (var i = 0; i < 4; i++) yield return null;
            Check("merchant prompt shows '[E] TRADE WITH MERCHANT' in reach", run.CurrentInteractionPrompt.Contains("TRADE WITH MERCHANT") && interactor.FindNearestInteractable() == (IInteractable)merchant);
            yield return CaptureHud("merchant_11_prompt_in_reach");

            // E (the interactor's one-press path) opens the trade screen exactly once.
            var opensBefore = vm.Opens;
            Check("interact opens the merchant", interactor.TryInteract() && merchant.OpenCount == 1 && vm.Opens == opensBefore + 1 && vm.IsOpen);
            yield return null;
            Check("merchant window visible over the run, bound to this merchant's stock", view.IsVisible && vm.Merchant == merchant.Merchant && vm.Rows.Count == merchant.Merchant.Offers.Count && vm.Rows.Count > 0);
            Check("merchant focus list on the menu input stack, first offer focused", menuInput.Stack.Current == view.FocusList && view.FocusList.Focused != null && view.FocusList.Focused.Id == "merchant.row.0");
            Check("gameplay gated and the pointer cursor shown while trading", GameplayInputGate.IsHeld && run.Rig.Reader.Move == Vector2.zero && !run.Rig.Reader.InteractHeld && PointerLayerOwnsCursor && run.CurrentInteractionPrompt.Length == 0);
            Check("offer rows show icon, rarity frame, name and price", view.RowViews.Take(vm.Rows.Count).All(r => r.IconVisible && r.FrameSprite != null && r.NameText.Length > 0 && r.PriceText.EndsWith(" C")));
            Check("coins and free backpack slots shown", view.CoinsText.Contains("COINS") && view.BackpackText.Contains("SLOTS FREE"));
            yield return CaptureHud("merchant_12_menu_open");

            // Pick the cheapest unsold offer with the keyboard; the details panel follows.
            run.Expedition.AddCarriedCoins(400);
            var cheapest = vm.Rows.Where(r => !r.IsSold).OrderBy(r => r.Price).First();
            var target = vm.Rows.ToList().IndexOf(cheapest);
            for (var guard = 0; guard < 12 && vm.Cursor != target; guard++) if (!menuInput.Stack.Navigate(vm.Cursor < target ? Vector2Int.down : Vector2Int.up)) break;
            yield return null;
            Check("keyboard navigation selects the offer and the details show it", vm.Cursor == target && view.DetailTitleText.Contains(cheapest.Name) && view.DetailSubtitleText.Contains($"PRICE {cheapest.Price} C") && vm.CanAct);
            yield return CaptureHud("merchant_13_item_selected");

            // Confirm buys through the service: exact coin change, item in the backpack, offer marked sold, exactly once.
            var coinsBefore = run.Expedition.State.CarriedCoins;
            var definitionId = cheapest.Item.DefinitionId;
            var delivered = cheapest.Item.Quantity; // a stackable is absorbed into the backpack stacks
            int Held() => inventory.BackpackSlots.Where(i => i != null && i.DefinitionId == definitionId).Sum(i => i.Quantity);
            var heldBefore = Held();
            Check("confirm activates the focused offer", menuInput.Stack.Activate());
            yield return null;
            Check($"purchase debited exactly the price ({cheapest.Price}) and delivered exactly {delivered} x {definitionId}", run.Expedition.State.CarriedCoins == coinsBefore - cheapest.Price && Held() == heldBefore + delivered && vm.Purchases == 1 && merchant.Merchant.Offers[target].IsSold);
            Check("purchase feedback shown and the row reads SOLD", view.MessageText.StartsWith("BOUGHT") && view.RowViews[target].SubtitleText == "SOLD");
            yield return CaptureHud("merchant_14_purchase_success");
            var coinsAfter = run.Expedition.State.CarriedCoins;
            menuInput.Stack.Activate();
            yield return null;
            Check("a repeated confirm on the sold offer changes nothing (exactly-once)", run.Expedition.State.CarriedCoins == coinsAfter && vm.Purchases == 1 && Held() == heldBefore + delivered);

            // SELL tab lists the dungeon-held backpack items with quotes; starter gear is not on offer to sell.
            while (view.FocusList.Focused != null && view.FocusList.Focused.Id.StartsWith("merchant.row.")) if (!menuInput.Stack.Navigate(Vector2Int.up)) break;
            menuInput.Stack.Navigate(Vector2Int.right);
            menuInput.Stack.Activate();
            yield return null;
            Check("SELL tab lists the backpack with sell quotes", vm.Tab == MerchantTab.Sell && vm.Rows.Count == inventory.BackpackSlots.Count(i => i != null) && vm.Rows.All(r => r.IsUnsellable || r.Price >= 0));

            // Mouse CLOSE restores gameplay: input released, aim cursor back, prompt back, focus list gone.
            view.Buttons[MerchantView.CloseFocusId].SimulateClick();
            yield return null;
            for (var i = 0; i < 3; i++) yield return null;
            Check("CLOSE restores gameplay (input released, aim cursor, prompt back, focus list popped)", !vm.IsOpen && !view.IsVisible && !GameplayInputGate.IsHeld && CursorService.Current == CursorKind.Aim && !menuInput.Stack.Contains(view.FocusList) && run.CurrentInteractionPrompt.Contains("TRADE WITH MERCHANT"));
            yield return CaptureHud("merchant_15_closed_gameplay_restored");

            // Reopen keeps the sold state and the same single window.
            Check("reopen shows the same window with the sold offer still sold", interactor.TryInteract() && vm.IsOpen && merchant.OpenCount == 2 && FindObjectsByType<MerchantView>(FindObjectsSortMode.None).Length == 1 && vm.Rows[target].IsSold);
            vm.Close();
            yield return null;

            _result.MerchantChecks = passed.ToArray();
            Debug.Log("[SMOKE] merchant: " + string.Join("; ", passed));
        }
    }
}

using System;
using System.Linq;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Progression;
using RuinRail.UI.Base;
using RuinRail.UI.Inventory;
using RuinRail.UI.Multiplayer;
using RuinRail.UI.Pause;
using RuinRail.UI.Settings;
using UnityEngine;

namespace RuinRail.UI.Navigation
{
    /// <summary>
    /// The controller/keyboard navigation map of every interactive screen (ui/90: all menus support mouse/keyboard
    /// and controller). Each builder turns a view model into a <see cref="FocusList"/> whose items call the view
    /// model's own actions — the same code paths the pointer uses — so no control is pointer-only.
    /// </summary>
    public static class ScreenNavigation
    {
        public static FocusList MainMenu(MainMenuViewModel menu)
        {
            var list = new FocusList("MainMenu");
            foreach (var entry in MainMenuViewModel.Entries) list.Add("menu." + entry, MainMenuViewModel.Label(entry), () => menu.Select(entry));
            return list;
        }

        public static FocusList PauseMenu(PauseMenuViewModel pause)
        {
            var list = new FocusList("Pause");
            foreach (var item in PauseMenuViewModel.Items) list.Add("pause." + item, PauseMenuViewModel.Label(item), () => pause.Activate(item), () => item != PauseMenuItem.Settings || pause.Settings != null);
            return list;
        }

        /// <summary>The Run Lost screen's two exits; RETURN TO SHELTER is the first (default focus) entry.</summary>
        public static FocusList RunFailed(RunEnd.RunFailedViewModel failed)
        {
            var list = new FocusList("RunFailed");
            list.Add("runfailed.shelter", RunEnd.RunFailedViewModel.ReturnToShelterLabel, failed.ReturnToShelter);
            list.Add("runfailed.menu", RunEnd.RunFailedViewModel.MainMenuLabel, failed.MainMenu);
            return list;
        }

        /// <summary>The confirmation under RETURN TO MAIN MENU / QUIT GAME: confirm or back out.</summary>
        public static FocusList PauseConfirmation(PauseMenuViewModel pause)
        {
            var list = new FocusList("PauseConfirm");
            list.Add("pause.confirm.cancel", "CANCEL", pause.CancelConfirmation);
            list.Add("pause.confirm.yes", "CONFIRM", pause.Confirm);
            return list;
        }

        /// <summary>The station bar of the Shelter hub; each station opens its own panel (pushed by the caller).</summary>
        public static FocusList BaseHub(BaseHubViewModel hub)
        {
            var list = new FocusList("BaseHub");
            foreach (var station in BaseHubViewModel.Stations) list.Add("station." + station, BaseHubViewModel.Label(station), () => hub.Open(station));
            list.Add("station.close", "LEAVE", hub.Close);
            return list;
        }

        public static FocusList Storage(StoragePanelViewModel storage, Func<string> selectedInstance)
        {
            var list = new FocusList("Storage");
            list.Add("storage.filter.all", "ALL", () => storage.Filter = null);
            foreach (ItemCategory category in Enum.GetValues(typeof(ItemCategory))) list.Add("storage.filter." + category, category.ToString().ToUpperInvariant(), () => storage.Filter = category);
            list.Add("storage.sort", "SORT BY RARITY", () => storage.SortByRarity = !storage.SortByRarity);
            list.Add("storage.deposit", "DEPOSIT", () => storage.Deposit(selectedInstance()), () => !string.IsNullOrEmpty(selectedInstance()));
            list.Add("storage.withdraw", "WITHDRAW", () => storage.Withdraw(selectedInstance()), () => !string.IsNullOrEmpty(selectedInstance()));
            return list;
        }

        /// <summary>Loadout / inventory: 5 equipment slots + 8 backpack slots as a 4-column grid region, then the actions on the cursor.</summary>
        public static FocusList Inventory(InventoryViewModel inventory)
        {
            var list = new FocusList("Inventory");
            foreach (var slot in inventory.EquipmentSlots) list.Add("slot." + slot, slot.ToString(), () => { inventory.SetCursor(new InventorySlotRef(InventorySlotKind.Equipped, (int)slot)); inventory.Activate(); });
            for (var i = 0; i < InventoryViewModel.BackpackSlots; i++)
            {
                var index = i;
                list.Add("backpack." + index, $"Backpack {index + 1}", () => { inventory.SetCursor(new InventorySlotRef(InventorySlotKind.Backpack, index)); inventory.Activate(); });
            }

            list.Add("inventory.drop", "DROP", () => inventory.Drop(inventory.Cursor), () => inventory.ItemAt(inventory.Cursor) != null);
            list.Add("inventory.consumable", "SET ACTIVE CONSUMABLE", () => inventory.SetActiveConsumable(inventory.Cursor), () => inventory.ItemAt(inventory.Cursor) != null);
            list.Add("inventory.close", "CLOSE", inventory.Close);
            return list;
        }

        public static FocusList Trader(TraderPanelViewModel trader, Func<string> selectedInstance)
        {
            var list = new FocusList("Trader");
            for (var i = 0; i < trader.Offers.Count; i++)
            {
                var index = i;
                list.Add("trader.buy." + index, "BUY " + (trader.Offers[index].Definition != null ? trader.Offers[index].Definition.DisplayName : "offer"), () => trader.Buy(index));
            }

            list.Add("trader.sell", "SELL", () => trader.Sell(selectedInstance()), () => !string.IsNullOrEmpty(selectedInstance()));
            return list;
        }

        /// <summary>
        /// The Character Station's controls: one live-captioned row per attribute (name + rank / cap, MAX at the cap)
        /// and the respec at its real price. A row is enabled only while the purchase would actually succeed — at the
        /// Shelter, below the cap, with the Skill Point in hand — so a rejected purchase is never offered as available
        /// and nothing can be spent by pressing it.
        /// </summary>
        public static FocusList Character(CharacterPanelViewModel character)
        {
            var list = new FocusList("Character");
            foreach (var skill in CharacterPanelViewModel.Attributes)
            {
                var attribute = skill;
                list.Add("character.allocate." + attribute, character.NameOf(attribute),
                    () => character.Allocate(attribute), () => character.CanAllocate(attribute));
            }

            list.Add("character.respec", character.RespecLabel(), () => character.Respec(), () => character.CanRespec());
            return list;
        }

        public static FocusList Workshop(WorkshopPanelViewModel workshop)
        {
            var list = new FocusList("Workshop");
            list.Add("workshop.storage", "UPGRADE STORAGE", () => workshop.BuyStorageUpgrade());
            list.Add("workshop.trader", "UPGRADE TRADER", () => workshop.BuyTraderUpgrade());
            return list;
        }

        public static FocusList Multiplayer(TerminalViewModel terminal)
        {
            var list = new FocusList("Multiplayer");
            foreach (var action in TerminalViewModel.Actions) list.Add("terminal." + action, TerminalViewModel.Label(action), () => { _ = terminal.ActivateAsync(action); }, () => terminal.IsEnabled(action));
            return list;
        }

        public static FocusList Transit(TransitPanelViewModel transit, MultiplayerPanelViewModel multiplayer)
        {
            var list = new FocusList("Transit");
            list.Add("transit.ready", "READY", () => multiplayer.SetReady(true));
            list.Add("transit.start", "START EXPEDITION", () => transit.StartExpedition());
            return list;
        }

        public static FocusList TransitVote(TransitVoteViewModel vote)
        {
            var list = new FocusList("TransitVote");
            list.Add("vote.return", "RETURN TO SHELTER", () => vote.Vote(RuinRail.Gameplay.Expedition.TransitChoice.ReturnToShelter), () => vote.HasVoteControls && !vote.IsResolved);
            list.Add("vote.descend", "DESCEND DEEPER", () => vote.Vote(RuinRail.Gameplay.Expedition.TransitChoice.DescendDeeper), () => vote.HasVoteControls && !vote.IsResolved);
            list.Add("vote.confirm", "CONFIRM RETURN", () => vote.ConfirmReturn(), () => vote.AwaitingReturnConfirmation);
            list.Add("vote.cancel", "CANCEL", vote.CancelReturn, () => vote.AwaitingReturnConfirmation);
            return list;
        }

        public const string SettingsBackId = "settings.back";
        public const string SettingsDefaultsId = "settings.defaults";

        /// <summary>
        /// The Settings root (ui/90): one control per real category, then RESET TO DEFAULTS and BACK. Selecting a
        /// category opens its page (<see cref="SettingsPage"/>); nothing here is a value.
        /// </summary>
        public static FocusList Settings(SettingsViewModel settings)
        {
            var list = new FocusList("Settings");
            foreach (var tab in SettingsViewModel.Tabs)
            {
                var t = tab;
                if (t == SettingsTab.Gameplay && !settings.HasGameplayPreferences) continue;
                list.Add("settings.category." + t, SettingsViewModel.CategoryLabel(t), () => settings.OpenPage(t));
            }

            list.Add(SettingsDefaultsId, "RESET TO DEFAULTS", () => settings.ResetToDefaults());
            list.Add(SettingsBackId, "BACK", () => settings.RequestClose());
            return list;
        }

        /// <summary>
        /// One category page: every row is a real control — Enter/click toggles, cycles, starts a rebind or runs the
        /// action; left/right (arrows, D-pad, stick) step sliders and selectors — and BACK applies and returns to the
        /// categories. Row ids are the view model's.
        /// </summary>
        public static FocusList SettingsPage(SettingsViewModel settings, SettingsTab tab)
        {
            var list = new FocusList("Settings." + tab);
            foreach (var row in settings.RowsFor(tab))
            {
                var r = row;
                if (r.Adjust != null) list.AddAdjustable(r.Id, r.Label, r.Activate, r.Adjust, () => r.IsEnabled);
                else list.Add(r.Id, r.Label, r.Activate, () => r.IsEnabled);
            }

            list.Add(SettingsBackId, "BACK", () => settings.BackFromPage());
            return list;
        }
    }
}

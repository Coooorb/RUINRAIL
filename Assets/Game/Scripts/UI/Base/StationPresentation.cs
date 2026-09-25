using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Progression;
using RuinRail.UI.Inventory;
using RuinRail.UI.Multiplayer;
using RuinRail.UI.Navigation;

namespace RuinRail.UI.Base
{
    /// <summary>One line of a station's data panel. A value-less row is a heading inside the list.</summary>
    public readonly struct StationRow
    {
        public StationRow(string key, string value = null, bool isHeading = false, bool isText = false)
        {
            Key = key ?? string.Empty;
            Value = value ?? string.Empty;
            IsHeading = isHeading;
            IsText = isText;
        }

        public string Key { get; }
        public string Value { get; }
        public bool IsHeading { get; }

        /// <summary>A sentence that needs the whole column rather than the key/value split (an attribute's effect line).</summary>
        public bool IsText { get; }

        public static StationRow Heading(string text) => new(text, null, true);

        /// <summary>One full-width line of text inside the list; no value, no rule, no extra spacing.</summary>
        public static StationRow Text(string text) => new(text, null, false, true);
    }

    /// <summary>What one Shelter station presents: its identity, what it is for, and the state it currently holds.</summary>
    public sealed class StationView
    {
        public string Title = string.Empty;
        /// <summary>One line saying what this station does, in terms of the approved rules — never invented lore.</summary>
        public string Description = string.Empty;
        public IReadOnlyList<StationRow> Rows = System.Array.Empty<StationRow>();
        /// <summary>Shown instead of the rows when the station genuinely holds nothing yet.</summary>
        public string EmptyText = string.Empty;

        public bool IsEmpty => Rows == null || Rows.Count == 0;
    }

    /// <summary>
    /// Turns each Shelter station into the content its panel shows.
    ///
    /// This exists so the hub screen stops being a list of buttons beside a paragraph of concatenated text. Every row
    /// below is read from a view model that already owns that number; nothing is computed a second time here and
    /// nothing is fabricated to fill the panel. Where a station really has little to show — an empty Storage, a solo
    /// party — it says so in a sentence rather than padding itself out, which is what section A4 of the polish pass
    /// asks for.
    ///
    /// It is deliberately free of Unity types so the content can be asserted in EditMode without building a screen.
    /// </summary>
    public static class StationPresentation
    {
        public static StationView For(BaseStation station, BaseHubViewModel hub, TerminalViewModel terminal)
        {
            return station switch
            {
                BaseStation.Storage => Storage(hub),
                BaseStation.Loadout => Loadout(hub),
                BaseStation.Trader => Trader(hub),
                BaseStation.Character => Character(hub),
                BaseStation.Workshop => Workshop(hub),
                BaseStation.Multiplayer => Multiplayer(terminal),
                _ => Transit(hub)
            };
        }

        /// <summary>The station's own one-line purpose, used in the panel header and in the tab tooltip line.</summary>
        public static string DescriptionOf(BaseStation station) => station switch
        {
            BaseStation.Storage => "Items kept here survive a failed run.",
            BaseStation.Loadout => "Everything you carry in is at risk.",
            BaseStation.Trader => "Buy from the counter, sell what you spare.",
            BaseStation.Character => "Level, XP and the Skill Points you earned.",
            BaseStation.Workshop => "Spend Banked Coins on Shelter upgrades.",
            BaseStation.Multiplayer => "Play solo, or host a party of up to three.",
            BaseStation.Transit => "The Transit Car departs when all are Ready.",
            _ => string.Empty
        };

        private static StationView Storage(BaseHubViewModel hub)
        {
            var storage = hub.Storage;
            var rows = new List<StationRow>
            {
                new("CAPACITY", $"{storage.Count} / {storage.Capacity}"),
                new("FILTER", storage.Filter?.ToString().ToUpperInvariant() ?? "ALL"),
                new("SORT", storage.SortByRarity ? "RARITY" : "NAME")
            };

            var items = storage.Items;
            if (items.Count > 0)
            {
                rows.Add(StationRow.Heading("STORED"));
                foreach (var item in items.Take(14))
                    rows.Add(new StationRow(NameOf(hub, item), RarityStyle.For(item.Rarity).Label));
            }

            return new StationView
            {
                Title = "STORAGE",
                Description = DescriptionOf(BaseStation.Storage),
                Rows = rows,
                EmptyText = "Storage is empty. Extract with loot and deposit it here."
            };
        }

        private static StationView Loadout(BaseHubViewModel hub)
        {
            var inventory = hub.Loadout.Inventory;
            var rows = new List<StationRow> { StationRow.Heading("EQUIPPED") };

            foreach (var slot in inventory.EquipmentSlots)
            {
                var item = inventory.ItemAt(new InventorySlotRef(InventorySlotKind.Equipped, (int)slot));
                rows.Add(new StationRow(SlotLabel(slot), item == null ? "—" : inventory.DisplayNameOf(item)));
            }

            var carried = 0;
            var backpack = new List<StationRow>();
            for (var i = 0; i < InventoryViewModel.BackpackSlots; i++)
            {
                var item = inventory.ItemAt(new InventorySlotRef(InventorySlotKind.Backpack, i));
                if (item == null) continue;
                carried++;
                backpack.Add(new StationRow($"BAG {i + 1}", inventory.DisplayNameOf(item)));
            }

            rows.Add(StationRow.Heading($"BACKPACK  {carried} / {InventoryViewModel.BackpackSlots}"));
            if (backpack.Count > 0) rows.AddRange(backpack);
            else rows.Add(new StationRow("Empty", "—"));

            return new StationView
            {
                Title = "LOADOUT",
                Description = DescriptionOf(BaseStation.Loadout),
                Rows = rows
            };
        }

        private static StationView Trader(BaseHubViewModel hub)
        {
            var trader = hub.Trader;
            var rows = new List<StationRow>
            {
                new("BANKED", hub.Session.Banked.Balance + " C")
            };

            var offers = trader.Offers;
            if (offers.Count > 0)
            {
                rows.Add(StationRow.Heading("ON THE COUNTER"));
                foreach (var offer in offers.Take(12))
                    rows.Add(new StationRow(offer.Definition != null ? offer.Definition.DisplayName : "offer", offer.Price + " C"));
            }

            return new StationView
            {
                Title = "TRADER",
                Description = DescriptionOf(BaseStation.Trader),
                Rows = rows,
                EmptyText = "The counter is bare. Stock rotates as the Trader is upgraded."
            };
        }

        private static StationView Character(BaseHubViewModel hub)
        {
            var sheet = hub.Character.Sheet;
            var character = hub.Character;
            var rows = new List<StationRow>
            {
                new("LEVEL", sheet.Level.ToString()),
                new("XP", sheet.IsMaxLevel ? $"{sheet.TotalXp} (max)" : $"{sheet.XpIntoLevel} / {sheet.XpToNextLevel}"),
                new("SKILL POINTS", sheet.UnspentPoints.ToString()),
                new("RANK COST", SkillCatalog.PointCostText),
                // The respec price lives on its own control's caption; the rows above are the purchase economy.
                StationRow.Heading("ATTRIBUTES")
            };

            // One block per attribute: rank / cap, what it does, and — per affected stat — what it is worth now and
            // what one more point buys, or MAX at the cap. Every number is formatted from SkillRules through
            // SkillCatalog, so this panel can never advertise an effect the run does not produce.
            foreach (var skill in CharacterPanelViewModel.Attributes)
            {
                rows.Add(new StationRow(character.NameOf(skill),
                    character.IsMaxed(skill) ? character.RankTextOf(skill) + " " + SkillCatalog.MaxedText : character.RankTextOf(skill)));
                rows.Add(StationRow.Text(character.DescriptionOf(skill)));
                foreach (var line in character.EffectRows(skill)) rows.Add(StationRow.Text(line));
            }

            return new StationView
            {
                Title = "CHARACTER",
                Description = character.StatusLine(),
                Rows = rows
            };
        }

        private static StationView Workshop(BaseHubViewModel hub)
        {
            var workshop = hub.Workshop;
            var rows = new List<StationRow>
            {
                new("BANKED", hub.Session.Banked.Balance + " C"),
                StationRow.Heading("STORAGE"),
                new("TIER", workshop.StorageTier.ToString()),
                new("SLOTS", workshop.StorageCapacity.ToString()),
                new("NEXT", workshop.NextStorageCapacity > workshop.StorageCapacity ? $"{workshop.NextStorageCapacity} for {workshop.NextStorageUpgradeCost} C" : "max tier"),
                StationRow.Heading("TRADER"),
                new("LEVEL", workshop.TraderLevel.ToString()),
                new("NEXT", workshop.NextTraderUpgradeCost > 0 ? workshop.NextTraderUpgradeCost + " C" : "max level")
            };

            return new StationView
            {
                Title = "WORKSHOP",
                Description = DescriptionOf(BaseStation.Workshop),
                Rows = rows
            };
        }

        private static StationView Multiplayer(TerminalViewModel terminal)
        {
            var rows = new List<StationRow>();
            if (terminal == null)
                return new StationView
                {
                    Title = "MULTIPLAYER",
                    Description = DescriptionOf(BaseStation.Multiplayer),
                    EmptyText = "The terminal is offline."
                };

            rows.Add(new StationRow("STATUS", terminal.StatusText));
            rows.Add(new StationRow("PARTY", $"{terminal.Roster.Count} / {terminal.MaxPartySize}"));
            // The non-blocking notice (Starter Loadout equipped on READY) gets a full-width line, never a truncated value cell.
            if (!string.IsNullOrEmpty(terminal.Notice)) rows.Add(StationRow.Heading(terminal.Notice));
            if (!string.IsNullOrEmpty(terminal.JoinCodeToShare)) rows.Add(new StationRow("JOIN CODE", terminal.JoinCodeToShare));
            if (!string.IsNullOrEmpty(terminal.ErrorText)) rows.Add(new StationRow("ERROR", terminal.ErrorText));

            rows.Add(StationRow.Heading("SURVIVORS"));
            foreach (var line in terminal.Roster)
                rows.Add(new StationRow(line.Name + (line.IsHost ? " (host)" : string.Empty), line.StatusText));

            return new StationView
            {
                Title = "MULTIPLAYER TERMINAL",
                Description = DescriptionOf(BaseStation.Multiplayer),
                Rows = rows
            };
        }

        private static StationView Transit(BaseHubViewModel hub)
        {
            var lobby = hub.Multiplayer;
            var ready = lobby.Members.Count(m => m.IsReady);
            var rows = new List<StationRow>
            {
                new("PARTY", lobby.Members.Count.ToString()),
                new("READY", $"{ready} / {lobby.Members.Count}"),
                new("YOU", lobby.LocalReady ? "READY" : "NOT READY"),
                new("DEPARTURE", hub.Transit.CanStart ? "CLEARED" : "HELD"),
                StationRow.Heading("SURVIVORS")
            };

            foreach (var member in lobby.Members)
                rows.Add(new StationRow(member.ParticipantId + (member.IsHost ? " (host)" : string.Empty),
                    member.IsReady ? "READY" : member.HasValidLoadout ? "NOT READY" : "LOADOUT INVALID"));

            return new StationView
            {
                Title = "EXPEDITION TRANSIT",
                Description = DescriptionOf(BaseStation.Transit),
                Rows = rows
            };
        }

        /// <summary>
        /// Short key for an equipment slot.
        ///
        /// The enum names are what the focus list uses and must stay as they are, but PRIMARYWEAPON and
        /// ACTIVECONSUMABLE do not fit the key half of a stat row and were truncating to "PRIMARYWEAP…". The slot is
        /// already unambiguous without repeating the category.
        /// </summary>
        private static string SlotLabel(EquippedSlot slot) => slot switch
        {
            EquippedSlot.PrimaryWeapon => "PRIMARY",
            EquippedSlot.SecondaryWeapon => "SECONDARY",
            EquippedSlot.ActiveConsumable => "CONSUMABLE",
            _ => slot.ToString().ToUpperInvariant()
        };

        private static string NameOf(BaseHubViewModel hub, ItemInstance item) =>
            hub.Session.Configs.Resolve(item.DefinitionId)?.DisplayName ?? item.DefinitionId;
    }
}

using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Progression;
using RuinRail.UI.Inventory;
using RuinRail.UI.Multiplayer;
using RuinRail.UI.Navigation;

namespace RuinRail.UI.Base
{
    /// <summary>Semantic colour of a visual row: the screen maps it onto the palette.</summary>
    public enum RowTone { Neutral, Good, Warn, Bad, Muted }

    /// <summary>How a row is drawn. Pair/Heading/Text are the original text rows; the rest carry their meaning visually.</summary>
    public enum RowKind { Pair, Heading, Text, Bar, Item, Badge, Pips }

    /// <summary>One line of a station's data panel. A value-less row is a heading inside the list.</summary>
    public readonly struct StationRow
    {
        public StationRow(string key, string value = null, bool isHeading = false, bool isText = false)
            : this(isHeading ? RowKind.Heading : isText ? RowKind.Text : RowKind.Pair, key, value)
        {
        }

        private StationRow(RowKind kind, string key, string value, RowTone tone = RowTone.Neutral, float fraction = 0f,
            string itemId = null, Rarity rarity = Rarity.Common, int filled = 0, int total = 0)
        {
            Kind = kind;
            Key = key ?? string.Empty;
            Value = value ?? string.Empty;
            Tone = tone;
            Fraction = fraction < 0f ? 0f : fraction > 1f ? 1f : fraction;
            ItemId = itemId ?? string.Empty;
            Rarity = rarity;
            Filled = filled;
            Total = total;
        }

        public RowKind Kind { get; }
        public string Key { get; }
        public string Value { get; }
        public RowTone Tone { get; }
        /// <summary>Bar fill, 0..1.</summary>
        public float Fraction { get; }
        /// <summary>Item rows: the definition id whose icon is drawn.</summary>
        public string ItemId { get; }
        public Rarity Rarity { get; }
        /// <summary>Pips rows: filled of total slots.</summary>
        public int Filled { get; }
        public int Total { get; }

        public bool IsHeading => Kind == RowKind.Heading;

        /// <summary>A sentence that needs the whole column rather than the key/value split (an attribute's effect line).</summary>
        public bool IsText => Kind == RowKind.Text;

        public static StationRow Heading(string text) => new(text, null, true);

        /// <summary>One full-width line of text inside the list; no value, no rule, no extra spacing.</summary>
        public static StationRow Text(string text) => new(text, null, false, true);

        /// <summary>Key and value over a progress bar (capacity, rank, tier, XP).</summary>
        public static StationRow Bar(string key, string value, float fraction, RowTone tone = RowTone.Warn) =>
            new(RowKind.Bar, key, value, tone, fraction);

        /// <summary>A slot/key, then the item's icon and its name in its rarity colour; an empty slot passes a null id.</summary>
        public static StationRow Item(string key, string itemId, string name, Rarity rarity) =>
            new(RowKind.Item, key, name, itemId == null ? RowTone.Muted : RowTone.Neutral, 0f, itemId, rarity);

        /// <summary>A full-width status plate (READY / HELD, a terminal status).</summary>
        public static StationRow Badge(string text, RowTone tone) => new(RowKind.Badge, text, null, tone);

        /// <summary>Key, then one square per slot, <paramref name="filled"/> of them lit (party size, Ready count).</summary>
        public static StationRow Pips(string key, int filled, int total, string value = null, RowTone tone = RowTone.Good) =>
            new(RowKind.Pips, key, value, tone, 0f, null, Rarity.Common, filled, total);
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

        private static float Share(int part, int whole) => whole <= 0 ? 0f : part / (float)whole;

        private static StationView Storage(BaseHubViewModel hub)
        {
            var storage = hub.Storage;
            var full = storage.Count >= storage.Capacity;
            var rows = new List<StationRow>
            {
                StationRow.Bar("CAPACITY", $"{storage.Count} / {storage.Capacity}", Share(storage.Count, storage.Capacity), full ? RowTone.Bad : RowTone.Warn),
                new("SHOWING", (storage.Filter?.ToString().ToUpperInvariant() ?? "ALL") + (storage.SortByRarity ? " · RARITY" : " · NAME"))
            };

            var items = storage.Items;
            if (items.Count > 0)
            {
                rows.Add(StationRow.Heading("STORED"));
                foreach (var item in items.Take(12))
                    rows.Add(StationRow.Item(string.Empty, item.DefinitionId, NameOf(hub, item), item.Rarity));
            }
            else rows.Add(StationRow.Text("Nothing stored yet."));

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
                rows.Add(item == null
                    ? StationRow.Item(SlotLabel(slot), null, "empty", Rarity.Common)
                    : StationRow.Item(SlotLabel(slot), item.DefinitionId, inventory.DisplayNameOf(item), item.Rarity));
            }

            var carried = 0;
            var backpack = new List<StationRow>();
            for (var i = 0; i < InventoryViewModel.BackpackSlots; i++)
            {
                var item = inventory.ItemAt(new InventorySlotRef(InventorySlotKind.Backpack, i));
                if (item == null) continue;
                carried++;
                backpack.Add(StationRow.Item($"BAG {i + 1}", item.DefinitionId, inventory.DisplayNameOf(item), item.Rarity));
            }

            rows.Add(StationRow.Pips("BACKPACK", carried, InventoryViewModel.BackpackSlots, $"{carried} / {InventoryViewModel.BackpackSlots}", RowTone.Warn));
            rows.AddRange(backpack);

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
                StationRow.Bar("LEVEL " + sheet.Level, sheet.IsMaxLevel ? "MAX" : $"{sheet.XpIntoLevel} / {sheet.XpToNextLevel} XP",
                    sheet.IsMaxLevel ? 1f : Share(sheet.XpIntoLevel, sheet.XpToNextLevel), RowTone.Warn),
                new("SKILL POINTS", sheet.UnspentPoints.ToString()),
                new("RANK COST", SkillCatalog.PointCostText),
                StationRow.Heading("ATTRIBUTES")
            };

            // One compact block per attribute: its rank as a bar, then what it is worth now and what one more point
            // buys (or MAX). The attribute's description is contextual: it is shown for the focused attribute only.
            // Every number is formatted from SkillRules through SkillCatalog, so this panel can never advertise an
            // effect the run does not produce.
            foreach (var skill in CharacterPanelViewModel.Attributes)
            {
                var maxed = character.IsMaxed(skill);
                rows.Add(StationRow.Bar(character.NameOf(skill),
                    maxed ? character.RankTextOf(skill) + " " + SkillCatalog.MaxedText : character.RankTextOf(skill),
                    Share(character.RankOf(skill), character.MaxRank), maxed ? RowTone.Good : RowTone.Warn));
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
            var storageMaxed = workshop.StorageTier >= workshop.MaxStorageTier;
            var traderMaxed = workshop.NextTraderUpgradeCost <= 0;
            var rows = new List<StationRow>
            {
                new("BANKED", workshop.Banked + " C"),
                StationRow.Heading("STORAGE"),
                StationRow.Bar($"TIER {workshop.StorageTier} / {workshop.MaxStorageTier}", workshop.StorageCapacity + " SLOTS",
                    Share(workshop.StorageTier, workshop.MaxStorageTier), storageMaxed ? RowTone.Good : RowTone.Warn),
                storageMaxed
                    ? StationRow.Badge("MAX TIER", RowTone.Good)
                    : new StationRow($"TO {workshop.NextStorageCapacity} SLOTS", workshop.NextStorageUpgradeCost + " C"),
                StationRow.Heading("TRADER"),
                StationRow.Bar($"LEVEL {workshop.TraderLevel} / {workshop.TraderMaxLevel}", string.Empty,
                    Share(workshop.TraderLevel, workshop.TraderMaxLevel), traderMaxed ? RowTone.Good : RowTone.Warn),
                traderMaxed
                    ? StationRow.Badge("MAX LEVEL", RowTone.Good)
                    : new StationRow($"TO LEVEL {workshop.TraderLevel + 1}", workshop.NextTraderUpgradeCost + " C")
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

            rows.Add(StationRow.Badge(terminal.StatusText, string.IsNullOrEmpty(terminal.ErrorText) ? (terminal.IsInSession ? RowTone.Good : RowTone.Neutral) : RowTone.Bad));
            rows.Add(StationRow.Pips("PARTY", terminal.Roster.Count, terminal.MaxPartySize, $"{terminal.Roster.Count} / {terminal.MaxPartySize}"));
            // The non-blocking notice (Starter Loadout equipped on READY) gets a full-width line, never a truncated value cell.
            if (!string.IsNullOrEmpty(terminal.Notice)) rows.Add(StationRow.Heading(terminal.Notice));
            if (!string.IsNullOrEmpty(terminal.JoinCodeToShare)) rows.Add(new StationRow("JOIN CODE", terminal.JoinCodeToShare));
            if (!string.IsNullOrEmpty(terminal.ErrorText)) rows.Add(new StationRow("ERROR", terminal.ErrorText));

            rows.Add(StationRow.Heading("SURVIVORS"));
            foreach (var line in terminal.Roster)
                rows.Add(StationRow.Bar(line.Name + (line.IsHost ? " (host)" : string.Empty), line.StatusText, line.IsReady ? 1f : 0f,
                    line.IsReady ? RowTone.Good : line.HasValidLoadout ? RowTone.Warn : RowTone.Bad));

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
            var canStart = hub.Transit.CanStart;
            var rows = new List<StationRow>
            {
                StationRow.Badge(canStart ? "CLEARED TO DEPART" : "DEPARTURE HELD", canStart ? RowTone.Good : RowTone.Warn),
                StationRow.Pips("READY", ready, lobby.Members.Count, $"{ready} / {lobby.Members.Count}"),
                new("YOU", lobby.LocalReady ? "READY" : "NOT READY"),
                // 77: taken coins become Carried Coins — at risk, banked again only by a successful return.
                StationRow.Heading("COINS FOR THE RUN"),
                new("BANKED NOW", hub.Transit.Banked + " C"),
                // Two bars that together are the bank: what leaves with the run (at risk) and what stays safe.
                StationRow.Bar("TAKING", hub.Transit.CoinsToCarry + " C", Share(hub.Transit.CoinsToCarry, hub.Transit.Banked), RowTone.Warn),
                StationRow.Bar("STAYS BANKED", hub.Transit.BankedAfterDeparture + " C", Share(hub.Transit.BankedAfterDeparture, hub.Transit.Banked), RowTone.Good),
                StationRow.Heading("SURVIVORS")
            };

            foreach (var member in lobby.Members)
                rows.Add(StationRow.Bar(member.ParticipantId + (member.IsHost ? " (host)" : string.Empty),
                    member.IsReady ? "READY" : member.HasValidLoadout ? "NOT READY" : "LOADOUT INVALID", member.IsReady ? 1f : 0f,
                    member.IsReady ? RowTone.Good : member.HasValidLoadout ? RowTone.Warn : RowTone.Bad));

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

using System;
using System.Linq;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;

namespace RuinRail.Gameplay.Events
{
    /// <summary>
    /// The one line the HUD shows for what an event just did (57): the reward that landed, the failure that cost
    /// coins, the wave that started, the heal that was bought — or why a press was refused. Pure text over the event's
    /// own result; the services never see it and never wait for it.
    /// </summary>
    public static class EventOutcomeText
    {
        /// <summary>Text for an activation/resolution result; empty when nothing worth announcing happened.</summary>
        public static string For(IDungeonEvent dungeonEvent, DungeonEventResult result, Func<string, ItemDefinition> resolve)
        {
            if (dungeonEvent == null || result == null) return string.Empty;
            var title = EventPromptBuilder.TitleOf(dungeonEvent.Kind).ToUpperInvariant();
            var loot = LootText(result.Loot, resolve);
            switch (result.Outcome)
            {
                case DungeonEventOutcome.InsufficientFunds:
                    return $"{title}: NOT ENOUGH COINS ({dungeonEvent.CostCoins} NEEDED)";
                case DungeonEventOutcome.Unavailable:
                    return result.Detail switch
                    {
                        "already_full" => $"{title}: HP ALREADY FULL",
                        "heal_uses_spent" => $"{title}: ALREADY USED",
                        "target_not_dead" => $"{title}: NO ONE TO REVIVE",
                        "revive_uses_spent" => $"{title}: NO REVIVES LEFT",
                        DungeonEventDetails.ChoiceRequired => string.Empty,
                        _ => $"{title}: UNAVAILABLE"
                    };
                case DungeonEventOutcome.Started:
                    return dungeonEvent switch
                    {
                        SupplySignalEvent signal => $"SUPPLY SIGNAL SENT: SURVIVE {signal.DurationSeconds:0} S",
                        CursedChestEvent => "CURSED CHEST OPENED: DEFEAT ITS GUARDIANS",
                        _ => $"{title} STARTED"
                    };
                case DungeonEventOutcome.Success:
                    return dungeonEvent.Kind switch
                    {
                        DungeonEventKind.BrokenMachine => "MACHINE REPAIRED: " + (loot.Length > 0 ? loot : "NOTHING INSIDE"),
                        DungeonEventKind.LockedVault => "VAULT UNLOCKED: " + (loot.Length > 0 ? loot : "EMPTY"),
                        DungeonEventKind.SupplySignal => "SUPPLY DROP DELIVERED: " + (loot.Length > 0 ? loot : "SUPPLY CHEST"),
                        DungeonEventKind.CursedChest => "CURSED CHEST CLEARED: " + (loot.Length > 0 ? loot : "LOOT DROPPED"),
                        DungeonEventKind.MedicalStation => HealText(result),
                        DungeonEventKind.WeaponCache => "WEAPON TAKEN",
                        _ => $"{title}: DONE"
                    };
                case DungeonEventOutcome.Failed:
                    return dungeonEvent.Kind switch
                    {
                        DungeonEventKind.BrokenMachine => $"REPAIR FAILED: THE MACHINE IS DEAD ({result.CoinsSpent} COINS SPENT)",
                        DungeonEventKind.SupplySignal => "SUPPLY SIGNAL LOST",
                        DungeonEventKind.CursedChest => "CURSED CHEST LOST",
                        _ => $"{title}: FAILED"
                    };
                default:
                    return string.Empty;
            }
        }

        private static string HealText(DungeonEventResult result)
        {
            var detail = result.Detail ?? string.Empty;
            var healed = detail.StartsWith("healed:") && int.TryParse(detail.Substring("healed:".Length), out var n) ? n : 0;
            return healed > 0 ? $"HEALED +{healed} HP ({result.CoinsSpent} COINS)" : $"MEDICAL STATION USED ({result.CoinsSpent} COINS)";
        }

        /// <summary>"MEDKIT x1, LIGHT AMMO x24" from a loot result; empty for none.</summary>
        public static string LootText(LootResult loot, Func<string, ItemDefinition> resolve)
        {
            if (loot == null || loot.IsEmpty) return string.Empty;
            var parts = loot.Items.Where(i => i != null).Select(i =>
            {
                var definition = resolve?.Invoke(i.DefinitionId);
                var name = definition != null && !string.IsNullOrEmpty(definition.DisplayName) ? definition.DisplayName : i.DefinitionId;
                return (name + (i.Quantity > 1 || (definition != null && definition.IsStackable) ? $" x{i.Quantity}" : string.Empty)).ToUpperInvariant();
            }).ToList();
            if (loot.Coins > 0) parts.Add($"{loot.Coins} COINS");
            return string.Join(", ", parts);
        }
    }
}

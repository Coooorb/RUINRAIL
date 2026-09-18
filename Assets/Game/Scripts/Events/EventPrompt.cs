namespace RuinRail.Gameplay.Events
{
    /// <summary>Presentation data for an event's interaction prompt. Built from an event, never owned by it.</summary>
    public readonly struct EventPrompt
    {
        public EventPrompt(DungeonEventKind kind, string title, string actionLabel, int costCoins, bool isAffordable, bool isAvailable, DungeonEventPhase phase)
        {
            Kind = kind;
            Title = title;
            ActionLabel = actionLabel;
            CostCoins = costCoins;
            IsAffordable = isAffordable;
            IsAvailable = isAvailable;
            Phase = phase;
        }

        public DungeonEventKind Kind { get; }
        public string Title { get; }
        public string ActionLabel { get; }
        public int CostCoins { get; }
        public bool IsAffordable { get; }
        public bool IsAvailable { get; }
        public DungeonEventPhase Phase { get; }
        public bool HasCost => CostCoins > 0;
    }

    /// <summary>Maps event gameplay state to prompt data (57 names); the UI reads this, the services never see it.</summary>
    public static class EventPromptBuilder
    {
        public static string TitleOf(DungeonEventKind kind) => kind switch
        {
            DungeonEventKind.CursedChest => "Cursed Chest",
            DungeonEventKind.LockedVault => "Locked Vault",
            DungeonEventKind.BrokenMachine => "Broken Machine",
            DungeonEventKind.SupplySignal => "Supply Signal",
            DungeonEventKind.MedicalStation => "Medical Station",
            DungeonEventKind.WeaponCache => "Weapon Cache",
            _ => kind.ToString()
        };

        public static string ActionOf(DungeonEventKind kind) => kind switch
        {
            DungeonEventKind.CursedChest => "Open",
            DungeonEventKind.LockedVault => "Unlock",
            DungeonEventKind.BrokenMachine => "Repair",
            DungeonEventKind.SupplySignal => "Activate",
            DungeonEventKind.MedicalStation => "Use",
            DungeonEventKind.WeaponCache => "Choose",
            _ => "Use"
        };

        public static EventPrompt Build(IDungeonEvent dungeonEvent, int carriedCoins)
        {
            var cost = dungeonEvent.CostCoins;
            var available = dungeonEvent.Phase == DungeonEventPhase.Available;
            return new EventPrompt(dungeonEvent.Kind, TitleOf(dungeonEvent.Kind), ActionOf(dungeonEvent.Kind), cost, cost <= carriedCoins, available, dungeonEvent.Phase);
        }
    }
}

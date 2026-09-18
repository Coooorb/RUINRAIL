using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;

namespace RuinRail.Persistence
{
    /// <summary>First-launch bookkeeping that belongs to the gameplay save (not to settings).</summary>
    [Serializable]
    public sealed class FirstLaunchFlags
    {
        /// <summary>The player confirmed a display name on first launch (player/10: requested on first launch).</summary>
        public bool DisplayNameConfirmed;

        /// <summary>ui/95 first-launch steps (name, starter kit, equip, first expedition start) completed once for this profile.</summary>
        public bool ShelterOnboardingComplete;

        /// <summary>Contextual tutorial prompt ids already completed/dismissed on this profile (ui/95: stored as seen; reset from Settings).</summary>
        public List<string> TutorialPromptsSeen = new();
    }

    /// <summary>An item that could not be resolved on load: kept verbatim, never replaced, never silently dropped.</summary>
    [Serializable]
    public sealed class QuarantinedItem
    {
        /// <summary>"SafeLoadout" or "Storage".</summary>
        public string Source;
        public int Slot;
        public string Reason;
        public ItemInstanceSnapshot Item;
    }

    /// <summary>
    /// Persisted record that a solo expedition transaction is open. Written at expedition start, cleared by the Return or
    /// Fail commit. Found on boot, it can only resolve one way: failure (113 anti-exploit rule; no mid-run resume).
    /// </summary>
    [Serializable]
    public sealed class ExpeditionMarker
    {
        public string TransactionId = "";
        public int RunSeed;
        public long StartedUtcTicks;

        /// <summary>Instance ids that left the safe loadout at start (for the boot-time loss summary only; the items are gone).</summary>
        public string[] AtRiskInstanceIds = Array.Empty<string>();

        public bool IsOpen => !string.IsNullOrEmpty(TransactionId);
    }

    /// <summary>
    /// The one V1 gameplay save slot (technical/113_SAVE_PERSISTENCE, technical/111_DATA_ARCHITECTURE): permanent
    /// profile (display name, XP, skills, banked coins, base upgrades, safe loadout), Shelter storage and first-launch
    /// flags. Plain serializable data only — stable definition ids and instance ids, never Unity object references.
    /// Settings are persisted separately and are not part of this document. No expedition state is ever stored here:
    /// V1 has no mid-expedition resume.
    /// </summary>
    [Serializable]
    public sealed class SaveSlot
    {
        /// <summary>Schema version of this envelope. Version 1 is the legacy bare PlayerProfile document (pre-envelope).</summary>
        public const int CurrentVersion = 2;

        public int SaveVersion = CurrentVersion;
        public PlayerProfile Profile = new();
        public StorageSnapshot Storage = new() { Capacity = Gameplay.Base.Storage.BaseCapacity, Slots = Array.Empty<InventorySnapshot.Entry>() };
        public FirstLaunchFlags FirstLaunch = new();

        /// <summary>Open expedition transaction, if any (never a resumable run: only a pending failure).</summary>
        public ExpeditionMarker ActiveExpedition = new();

        /// <summary>Items whose definition could not be resolved at load; preserved so a later build/fix can restore them.</summary>
        public List<QuarantinedItem> Quarantine = new();
    }
}

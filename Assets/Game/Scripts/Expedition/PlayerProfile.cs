using System;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Progression;

namespace RuinRail.Gameplay.Expedition
{
    /// <summary>
    /// Permanent (safe) player domain per technical/113_SAVE_PERSISTENCE and player/10_PLAYER_PROFILE: display name,
    /// lifetime XP (level is derived from it), unspent skill points, six invested attributes, banked coins and the safe
    /// loadout. Expedition state never writes here except through the explicit transactions in ExpeditionService.
    /// </summary>
    [Serializable]
    public sealed class PlayerProfile
    {
        public const int CurrentVersion = 1;

        /// <summary>Name a profile carries until the player chooses one (also the fallback for a blank saved name).</summary>
        public const string DefaultDisplayName = "Runner";

        public int Version = CurrentVersion;
        public string DisplayName = DefaultDisplayName;

        /// <summary>Stable per-profile seed for deterministic base services (trader refreshes).</summary>
        public int ProfileSeed = Environment.TickCount;
        public int TotalXp;
        public int BankedCoins;

        /// <summary>Skill points earned by levels but not yet allocated (spent only at the Shelter).</summary>
        public int UnspentSkillPoints;
        public SkillAllocation Skills = new();
        public TraderState Trader = new();
        public WorkshopState Workshop = new();

        /// <summary>First-profile Starter Kit already granted (never re-granted on boot; only softlock rescue re-issues it).</summary>
        public bool StarterKitGranted;

        /// <summary>Count of ended expeditions (extraction or failure); the trader refresh keys on it.</summary>
        public int ExpeditionsEnded;

        /// <summary>
        /// Deepest depth the player has actually ARRIVED at, across every expedition. Monotonic: it only ever rises,
        /// and neither death, extraction nor a later shallow run lowers it. 0 means "no depth entered yet", which is
        /// also what an old save deserializes to — JsonUtility leaves a missing field at its default, so no migration
        /// step is needed and no historical best is invented.
        ///
        /// Written only by <see cref="ExpeditionService.RecordDepthArrival"/>, which the scene calls once a depth has
        /// been generated and composed successfully. A failed descend therefore cannot record a depth never entered.
        /// </summary>
        public int DeepestDepthReached;

        /// <summary>Loadout stored safely between expeditions (equipped + backpack). Null/empty when everything is at risk.</summary>
        public InventorySnapshot SafeLoadout;

        public int Level => LevelCurve.LevelForTotalXp(TotalXp);
    }
}

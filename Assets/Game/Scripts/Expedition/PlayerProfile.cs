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

        public int Version = CurrentVersion;
        public string DisplayName = "Runner";

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

        /// <summary>Loadout stored safely between expeditions (equipped + backpack). Null/empty when everything is at risk.</summary>
        public InventorySnapshot SafeLoadout;

        public int Level => LevelCurve.LevelForTotalXp(TotalXp);
    }
}

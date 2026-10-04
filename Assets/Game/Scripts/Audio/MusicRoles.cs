using System;
using RuinRail.Core;

namespace RuinRail.Audio
{
    /// <summary>art/105: the full music tracks (menu, Shelter, exploration/combat/boss per biome). Both Bosses of a biome share that biome's Boss track. Values are stored as ints in the catalog: append only.</summary>
    public enum MusicRole
    {
        MainMenu,
        Shelter,
        RuinedMetroExploration,
        RuinedMetroCombat,
        RuinedMetroBoss,
        RustworksExploration,
        RustworksCombat,
        RustworksBoss,
        OvergrownLabsExploration,
        OvergrownLabsCombat,
        OvergrownLabsBoss,
        CryoVaultsExploration,
        CryoVaultsCombat,
        CryoVaultsBoss
    }

    /// <summary>art/105: exactly the six required stingers (not counted as tracks).</summary>
    public enum StingerRole
    {
        LegendaryDrop,
        EliteEncounter,
        BossDefeated,
        ExtractionSuccess,
        ExpeditionFailed,
        LevelUp,
        /// <summary>A Combat / Elite room's encounter was won (not bosses: Boss Defeated is theirs).</summary>
        RoomCleared
    }

    /// <summary>Where the player is, as far as music is concerned.</summary>
    public enum MusicScreen
    {
        MainMenu,
        Shelter,
        Expedition
    }

    public enum CombatIntensity
    {
        Exploration,
        Combat,
        Boss
    }

    /// <summary>Deterministic role selection (art/105): screen → menu/shelter; expedition → biome × exploration/combat/boss.</summary>
    public static class MusicStateResolver
    {
        public const int TrackCount = 14;
        public const int StingerCount = 7;

        public static MusicRole Resolve(MusicScreen screen, Biome biome, CombatIntensity intensity)
        {
            switch (screen)
            {
                case MusicScreen.MainMenu: return MusicRole.MainMenu;
                case MusicScreen.Shelter: return MusicRole.Shelter;
            }

            return (biome, intensity) switch
            {
                (Biome.RuinedMetro, CombatIntensity.Exploration) => MusicRole.RuinedMetroExploration,
                (Biome.RuinedMetro, CombatIntensity.Combat) => MusicRole.RuinedMetroCombat,
                (Biome.RuinedMetro, CombatIntensity.Boss) => MusicRole.RuinedMetroBoss,
                (Biome.Rustworks, CombatIntensity.Exploration) => MusicRole.RustworksExploration,
                (Biome.Rustworks, CombatIntensity.Combat) => MusicRole.RustworksCombat,
                (Biome.Rustworks, CombatIntensity.Boss) => MusicRole.RustworksBoss,
                (Biome.OvergrownLabs, CombatIntensity.Exploration) => MusicRole.OvergrownLabsExploration,
                (Biome.OvergrownLabs, CombatIntensity.Combat) => MusicRole.OvergrownLabsCombat,
                (Biome.OvergrownLabs, CombatIntensity.Boss) => MusicRole.OvergrownLabsBoss,
                (Biome.CryoVaults, CombatIntensity.Exploration) => MusicRole.CryoVaultsExploration,
                (Biome.CryoVaults, CombatIntensity.Combat) => MusicRole.CryoVaultsCombat,
                (Biome.CryoVaults, CombatIntensity.Boss) => MusicRole.CryoVaultsBoss,
                _ => throw new ArgumentOutOfRangeException(nameof(biome))
            };
        }

        /// <summary>The Boss track of a biome — the same role for both of its bosses.</summary>
        public static MusicRole BossRoleFor(Biome biome) => Resolve(MusicScreen.Expedition, biome, CombatIntensity.Boss);

        public static string DisplayName(MusicRole role) => role switch
        {
            MusicRole.MainMenu => "Main Menu",
            MusicRole.Shelter => "The Shelter",
            MusicRole.RuinedMetroExploration => "Ruined Metro — Exploration",
            MusicRole.RuinedMetroCombat => "Ruined Metro — Combat",
            MusicRole.RuinedMetroBoss => "Ruined Metro — Boss",
            MusicRole.RustworksExploration => "Rustworks — Exploration",
            MusicRole.RustworksCombat => "Rustworks — Combat",
            MusicRole.RustworksBoss => "Rustworks — Boss",
            MusicRole.OvergrownLabsExploration => "Overgrown Labs — Exploration",
            MusicRole.OvergrownLabsCombat => "Overgrown Labs — Combat",
            MusicRole.OvergrownLabsBoss => "Overgrown Labs — Boss",
            MusicRole.CryoVaultsExploration => "Cryo Vaults — Exploration",
            MusicRole.CryoVaultsCombat => "Cryo Vaults — Combat",
            MusicRole.CryoVaultsBoss => "Cryo Vaults — Boss",
            _ => role.ToString()
        };

        /// <summary>art/105 ambience character per biome (data for the sound designer; the loop clip is external).</summary>
        public static string AmbienceDescription(Biome biome) => biome switch
        {
            Biome.RuinedMetro => "electricity, tunnels, distant metal, old transit infrastructure",
            Biome.Rustworks => "machinery, steam, industrial movement",
            Biome.OvergrownLabs => "electronics, organic/bio ambience, damaged laboratory equipment",
            Biome.CryoVaults => "low ventilation, refrigeration machinery, pressure releases, condensation, distant metal and ice",
            _ => string.Empty
        };
    }
}

using System.Collections.Generic;
using RuinRail.Core;

namespace RuinRail.Dungeon.Rooms
{
    /// <summary>
    /// Player-facing names for the authored rooms (ui/91 room-title reveal). The room definitions keep their stable
    /// ids as identity (coding rule 9); this is a presentation mapping from that id to a short, atmospheric,
    /// biome-appropriate name, plus the room's role line ("MERCHANT", "BOSS", "WEAPON CACHE").
    ///
    /// A room id that is not listed falls back to the biome-neutral name of its <see cref="RoomType"/>, so a room
    /// added later is still announced rather than showing an internal id.
    /// </summary>
    public static class RoomDisplayNames
    {
        private static readonly Dictionary<string, string> Names = new(System.StringComparer.Ordinal)
        {
            // ---- Ruined Metro: a flooded, collapsed transit system ----
            ["metro_start_01"] = "Platform Landing",
            ["metro_start_02"] = "Turnstile Hall",
            ["metro_combat_small_01"] = "Service Junction",
            ["metro_combat_small_02"] = "Signal Closet",
            ["metro_combat_small_03"] = "Ticket Mezzanine",
            ["metro_combat_small_04"] = "Cable Run",
            ["metro_combat_small_05"] = "Drainage Shaft",
            ["metro_combat_medium_01"] = "Collapsed Platform",
            ["metro_combat_medium_02"] = "Maintenance Hall",
            ["metro_combat_medium_03"] = "Track Crossing",
            ["metro_combat_medium_04"] = "Ventilation Gallery",
            ["metro_combat_large_01"] = "Interchange Concourse",
            ["metro_combat_large_02"] = "Derailment Yard",
            ["metro_loot_01"] = "Lost Property Store",
            ["metro_treasure_01"] = "Sealed Strongroom",
            ["metro_merchant_01"] = "Scavenger's Stall",
            ["metro_medical_01"] = "First Aid Post",
            ["metro_event_01"] = "Abandoned Siding",
            ["metro_event_02"] = "Emergency Bay",
            ["metro_boss_01"] = "Terminus Hall",
            ["metro_boss_02"] = "Deep Terminus",

            // ---- Rustworks: a dead foundry ----
            ["rust_start_01"] = "Loading Dock",
            ["rust_start_02"] = "Slag Gate",
            ["rust_combat_small_01"] = "Boiler Junction",
            ["rust_combat_small_02"] = "Coolant Nook",
            ["rust_combat_small_03"] = "Pipe Gallery",
            ["rust_combat_small_04"] = "Scrap Alcove",
            ["rust_combat_small_05"] = "Ash Chute",
            ["rust_combat_medium_01"] = "Smelter Floor",
            ["rust_combat_medium_02"] = "Machine Bay",
            ["rust_combat_medium_03"] = "Casting Line",
            ["rust_combat_medium_04"] = "Furnace Walk",
            ["rust_combat_large_01"] = "Foundry Hall",
            ["rust_combat_large_02"] = "Rolling Mill",
            ["rust_loot_01"] = "Parts Store",
            ["rust_treasure_01"] = "Foreman's Vault",
            ["rust_merchant_01"] = "Salvage Exchange",
            ["rust_medical_01"] = "Works Infirmary",
            ["rust_event_01"] = "Control Cabin",
            ["rust_event_02"] = "Pump House",
            ["rust_boss_01"] = "Great Furnace",
            ["rust_boss_02"] = "Titan Pit",

            // ---- Overgrown Labs: a research complex the growth took back ----
            ["labs_start_01"] = "Reception Wing",
            ["labs_start_02"] = "Airlock Foyer",
            ["labs_combat_small_01"] = "Sample Closet",
            ["labs_combat_small_02"] = "Culture Nook",
            ["labs_combat_small_03"] = "Sterile Corridor",
            ["labs_combat_small_04"] = "Vent Cloister",
            ["labs_combat_small_05"] = "Seed Vault Annex",
            ["labs_combat_medium_01"] = "Specimen Wing",
            ["labs_combat_medium_02"] = "Hydroponics Bay",
            ["labs_combat_medium_03"] = "Containment Hall",
            ["labs_combat_medium_04"] = "Growth Chamber",
            ["labs_combat_large_01"] = "Atrium Canopy",
            ["labs_combat_large_02"] = "Biodome Floor",
            ["labs_loot_01"] = "Supply Cold Store",
            ["labs_treasure_01"] = "Secure Archive",
            ["labs_merchant_01"] = "Quartermaster Bay",
            ["labs_medical_01"] = "Triage Ward",
            ["labs_event_01"] = "Experiment Bay",
            ["labs_event_02"] = "Incubation Room",
            ["labs_boss_01"] = "Prime Greenhouse",
            ["labs_boss_02"] = "Root Nexus",

            // ---- Test fixture room (never reachable in normal play) ----
            ["test_grid_small_01"] = "Test Chamber"
        };

        /// <summary>Every authored room id that carries a hand-written name (for the coverage test).</summary>
        public static IReadOnlyCollection<string> KnownIds => Names.Keys;

        /// <summary>The player-facing name of a room definition; never an internal id.</summary>
        public static string NameOf(string roomId, RoomType type) =>
            !string.IsNullOrEmpty(roomId) && Names.TryGetValue(roomId, out var name) ? name : FallbackName(type);

        public static string NameOf(RoomDefinition definition) =>
            definition == null ? FallbackName(RoomType.Combat) : NameOf(definition.Id, definition.RoomType);

        /// <summary>Biome-neutral name for a room whose id carries no authored name.</summary>
        public static string FallbackName(RoomType type) => type switch
        {
            RoomType.Start => "Entry Point",
            RoomType.Loot => "Supply Room",
            RoomType.Treasure => "Treasure Vault",
            RoomType.Merchant => "Merchant Post",
            RoomType.Event => "Anomaly",
            RoomType.MedicalRecovery => "Medical Station",
            RoomType.Boss => "Boss Chamber",
            _ => "Combat Zone"
        };

        /// <summary>
        /// The room's role line under the name, upper case. Special rooms read as what they are; an ordinary Combat
        /// room has no role line at all, so the reveal stays a single line during a fight.
        /// </summary>
        public static string RoleOf(RoomType type) => type switch
        {
            RoomType.Start => "ENTRY",
            RoomType.Loot => "SUPPLY CACHE",
            RoomType.Treasure => "TREASURE",
            RoomType.Merchant => "MERCHANT",
            RoomType.Event => "ANOMALY",
            RoomType.MedicalRecovery => "MEDICAL",
            RoomType.Boss => "BOSS",
            _ => string.Empty
        };

        /// <summary>Upper-case biome name used beside the minimap and in the reveal.</summary>
        public static string BiomeName(Biome biome) => biome switch
        {
            Biome.RuinedMetro => "RUINED METRO",
            Biome.Rustworks => "RUSTWORKS",
            Biome.OvergrownLabs => "OVERGROWN LABS",
            _ => biome.ToString().ToUpperInvariant()
        };
    }
}

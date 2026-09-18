using System.Collections.Generic;
using System.Linq;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.Rooms
{
    /// <summary>
    /// The handmade Rustworks room set (56: factory / scrapyard / industrial facility — presses, pipe runs, furnaces,
    /// machinery blocks, scrap heaps). Identity comes from geometry and hazards only: machinery ('O') forms long
    /// press lines and pipe runs, furnace grates ('~', Hazard_FurnaceGrate) sit in the open floor, and layouts favour
    /// wide halls with heavy central obstacles instead of the Metro's cramped tunnels. No biome-exclusive player
    /// mechanic exists. Same ASCII legend and baker as the Metro set; content counts follow 126 (21 per biome) and
    /// this file grows subset by subset (TASKS 109–112).
    /// </summary>
    public static class RustworksRoomSet
    {
        public const string PrefabFolder = "Assets/Game/Prefabs/Rooms/Rustworks";
        public const string DefinitionFolder = "Assets/Game/ScriptableObjects/Rooms/Rustworks";
        public const string HazardPath = "Assets/Game/ScriptableObjects/Combat/Hazard_FurnaceGrate.asset";

        private const Biome Rust = Biome.Rustworks;

        // ---- Start rooms (2): safe, no enemy spawns, no hazards ----

        public static readonly RoomLayoutSpec Start01 = new("rust_start_01", Rust, RoomType.Start, RoomSizeClass.Small, new[]
        {
            "#######DD#######",
            "#..............#",
            "#.OOO......OOO.#",
            "#..............#",
            "#......P.......D",
            "#......,.......D",
            "D..............#",
            "D..............#",
            "#..,........,..#",
            "#.OOO......OOO.#",
            "#..............#",
            "######DD########"
        }, difficulty: 0, weight: 1f, tags: new[] { "rustworks", "start", "loading_dock" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec Start02 = new("rust_start_02", Rust, RoomType.Start, RoomSizeClass.Small, new[]
        {
            "###DD###########",
            "#..............#",
            "#..............#",
            "#....OOOOOO....#",
            "#..............#",
            "D......P.......D",
            "D......,.......D",
            "#..............#",
            "#....OOOOOO....#",
            "#..............#",
            "#..............#",
            "###########DD###"
        }, difficulty: 0, weight: 1f, tags: new[] { "rustworks", "start", "conveyor_hall" }) { HazardDefinitionPath = HazardPath };

        // ---- Small Combat rooms (5) ----

        public static readonly RoomLayoutSpec CombatSmall01 = new("rust_combat_small_01", Rust, RoomType.Combat, RoomSizeClass.Small, new[]
        {
            "######DD########",
            "#..............#",
            "#.E..........E.#",
            "#....OO..OO....#",
            "#....OO..OO....#",
            "D......~~......#",
            "D......~~......D",
            "#....OO..OO....D",
            "#.E..OO..OO..E.#",
            "#..............#",
            "#..............#",
            "########DD######"
        }, difficulty: 1, weight: 1f, tags: new[] { "rustworks", "combat", "small", "press_line" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec CombatSmall02 = new("rust_combat_small_02", Rust, RoomType.Combat, RoomSizeClass.Small, new[]
        {
            "###DD###########",
            "#..............#",
            "#.E...OOOO...E.#",
            "#..............#",
            "D..,........,..#",
            "D....~~..~~....D",
            "#..............D",
            "#..,........,..#",
            "#.E...OOOO...E.#",
            "#..............#",
            "#..............#",
            "###########DD###"
        }, difficulty: 1, weight: 1f, tags: new[] { "rustworks", "combat", "small", "furnace_row" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec CombatSmall03 = new("rust_combat_small_03", Rust, RoomType.Combat, RoomSizeClass.Small, new[]
        {
            "#####DD#########",
            "#..............#",
            "#.E..........E.#",
            "#.O..........O.#",
            "D.O...OOOO...O.#",
            "D.O..........O.D",
            "#.O..........O.D",
            "#.O...OOOO...O.#",
            "#.E..........E.#",
            "#......,.......#",
            "#..............#",
            "################"
        }, difficulty: 2, weight: 1f, tags: new[] { "rustworks", "combat", "small", "pipe_gallery" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec CombatSmall04 = new("rust_combat_small_04", Rust, RoomType.Combat, RoomSizeClass.Small, new[]
        {
            "################",
            "#..............#",
            "#..E........E..D",
            "#..............D",
            "#...OOOOOOOO...#",
            "D...OOOOOOOO...#",
            "D..............#",
            "#....~~..~~....#",
            "#..E........E..#",
            "#......,.......#",
            "#..............#",
            "######DD########"
        }, difficulty: 1, weight: 1f, tags: new[] { "rustworks", "combat", "small", "scrap_press" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec CombatSmall05 = new("rust_combat_small_05", Rust, RoomType.Combat, RoomSizeClass.Small, new[]
        {
            "########DD######",
            "#..............#",
            "#.E....OO....E.#",
            "#......OO......#",
            "#.OO........OO.#",
            "D....~~~~~~....#",
            "D..............D",
            "#.OO........OO.D",
            "#......OO......#",
            "#.E....OO....E.#",
            "#..............#",
            "###DD###########"
        }, difficulty: 2, weight: 1f, tags: new[] { "rustworks", "combat", "small", "furnace_pit" }) { HazardDefinitionPath = HazardPath };

        // ---- Medium Combat rooms (4): 24x16 ----

        public static readonly RoomLayoutSpec CombatMedium01 = new("rust_combat_medium_01", Rust, RoomType.Combat, RoomSizeClass.Medium, new[]
        {
            "##########DD############",
            "#......................#",
            "#..E................E..#",
            "#......................#",
            "#...OOOOO....OOOOO.....#",
            "#...OOOOO....OOOOO.....#",
            "#......................#",
            "D........~~~~..........D",
            "D........~~~~..........D",
            "#......................#",
            "#.....OOOOO....OOOOO...#",
            "#.....OOOOO....OOOOO...#",
            "#......................#",
            "#..E................E..#",
            "#......................#",
            "############DD##########"
        }, difficulty: 2, weight: 1f, supportsElite: true, tags: new[] { "rustworks", "combat", "medium", "press_hall" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec CombatMedium02 = new("rust_combat_medium_02", Rust, RoomType.Combat, RoomSizeClass.Medium, new[]
        {
            "#####DD#################",
            "#......................#",
            "#..E.......O.......E...#",
            "#..........O...........#",
            "#..........O...........#",
            "#....~~....O....~~.....#",
            "#..........O...........D",
            "#......................D",
            "#......................#",
            "D..........O...........#",
            "D....~~....O....~~.....#",
            "#..........O...........#",
            "#..E.......O.......E...#",
            "#......................#",
            "#......................#",
            "#################DD#####"
        }, difficulty: 2, weight: 1f, tags: new[] { "rustworks", "combat", "medium", "pipe_spine" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec CombatMedium03 = new("rust_combat_medium_03", Rust, RoomType.Combat, RoomSizeClass.Medium, new[]
        {
            "########DD##############",
            "#......................#",
            "#..E..............E....#",
            "#......OOO.............#",
            "#......OOO....OOOOOO...#",
            "#.............OOOOOO...#",
            "#......................D",
            "#..OO.......,..........D",
            "D..OO..........~~~~....#",
            "D..............~~~~....#",
            "#.........OOO..........#",
            "#.........OOO....OO....#",
            "#..E.............OO.E..#",
            "#......................#",
            "#......................#",
            "##############DD########"
        }, difficulty: 2, weight: 1f, tags: new[] { "rustworks", "combat", "medium", "scrap_yard" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec CombatMedium04 = new("rust_combat_medium_04", Rust, RoomType.Combat, RoomSizeClass.Medium, new[]
        {
            "###DD###################",
            "#......................#",
            "#..E................E..#",
            "#......................D",
            "#.....OO......OO.......D",
            "#.....OO......OO.......#",
            "#..........~~..........#",
            "#.........~~~~.........#",
            "#.........~~~~.........#",
            "#..........~~..........#",
            "#.....OO......OO.......#",
            "D.....OO......OO.......#",
            "D......................#",
            "#..E................E..#",
            "#......................#",
            "###################DD###"
        }, difficulty: 3, weight: 1f, supportsElite: true, tags: new[] { "rustworks", "combat", "medium", "furnace_core" }) { HazardDefinitionPath = HazardPath };

        // ---- Large Combat rooms (2): 32x20 ----

        public static readonly RoomLayoutSpec CombatLarge01 = new("rust_combat_large_01", Rust, RoomType.Combat, RoomSizeClass.Large, new[]
        {
            "##############DD################",
            "#..............................#",
            "#..E........................E..#",
            "#......OOOO..........OOOO......#",
            "#......OOOO..........OOOO......#",
            "#..............................#",
            "#..........,...........,.......#",
            "#...............E..............#",
            "#....~~~~~~........~~~~~~~.....#",
            "D..............................D",
            "D..............................D",
            "#....~~~~~~........~~~~~~~.....#",
            "#...............E..............#",
            "#..............................#",
            "#......OOOO..........OOOO......#",
            "#......OOOO..........OOOO......#",
            "#..............................#",
            "#..E........................E..#",
            "#..............................#",
            "################DD##############"
        }, difficulty: 3, weight: 1f, supportsElite: true, tags: new[] { "rustworks", "combat", "large", "assembly_floor" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec CombatLarge02 = new("rust_combat_large_02", Rust, RoomType.Combat, RoomSizeClass.Large, new[]
        {
            "######DD########################",
            "#..............................#",
            "#..E..........................E#",
            "#..............................#",
            "#....OOOOOOOOOOOOOOOOOOO.......D",
            "#..............................D",
            "#..........~~~~~.....~~~~~.....#",
            "#..............................#",
            "#..............................#",
            "#..............E...............#",
            "D......OOOOOOOOOOOOOOOOOOO.....#",
            "D..............................#",
            "#..............................#",
            "#....~~~~~.........~~~~~.......#",
            "#..............................#",
            "#......OOOOOOOOOOOOOOOOO.......#",
            "#..............E...............#",
            "#..E..........................E#",
            "#..............................#",
            "################################"
        }, difficulty: 3, weight: 1f, supportsElite: true, tags: new[] { "rustworks", "combat", "large", "conveyor_lines" }) { HazardDefinitionPath = HazardPath };

        // ---- Utility rooms: Merchant (1), Event (2), Loot (1), Treasure (1), Medical/Recovery (1) ----

        public static readonly RoomLayoutSpec Merchant01 = new("rust_merchant_01", Rust, RoomType.Merchant, RoomSizeClass.Small, new[]
        {
            "########DD######",
            "#..............#",
            "D..OO......OO..#",
            "D..............#",
            "#......M.......#",
            "#......,.......#",
            "#..............#",
            "#..,........,..#",
            "#..OO......OO..D",
            "#..............D",
            "#..............#",
            "####DD##########"
        }, difficulty: 0, weight: 1f, tags: new[] { "rustworks", "merchant", "tool_crib" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec Event01 = new("rust_event_01", Rust, RoomType.Event, RoomSizeClass.Small, new[]
        {
            "####DD##########",
            "#..............#",
            "#.E..........E.#",
            "#....OOO.......#",
            "#..............#",
            "D......V.......#",
            "D..............D",
            "#.......OOO....D",
            "#..............#",
            "#.E..........E.#",
            "#..............#",
            "################"
        }, difficulty: 1, weight: 1f, tags: new[] { "rustworks", "event", "control_booth" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec Event02 = new("rust_event_02", Rust, RoomType.Event, RoomSizeClass.Small, new[]
        {
            "#########DD#####",
            "#..............#",
            "D.E....OO....E.#",
            "D......OO......#",
            "#..,........,..#",
            "#......V.......#",
            "#..............#",
            "#..,........,..#",
            "#......OO......D",
            "#.E....OO....E.D",
            "#..............#",
            "#####DD#########"
        }, difficulty: 1, weight: 1f, tags: new[] { "rustworks", "event", "generator_bay" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec Loot01 = new("rust_loot_01", Rust, RoomType.Loot, RoomSizeClass.Small, new[]
        {
            "#####DD#########",
            "#..............#",
            "#..OO......OO..D",
            "#......C.......D",
            "#..............#",
            "#..............#",
            "#..............#",
            "#..............#",
            "D......C.......#",
            "D..OO......OO..#",
            "#..............#",
            "#########DD#####"
        }, difficulty: 0, weight: 1f, tags: new[] { "rustworks", "loot", "parts_store" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec Treasure01 = new("rust_treasure_01", Rust, RoomType.Treasure, RoomSizeClass.Small, new[]
        {
            "##########DD####",
            "#..............#",
            "#.OOO......OOO.D",
            "#..............D",
            "#..............#",
            "#....~~~~~~....#",
            "#..............#",
            "#......C.......#",
            "#..............#",
            "#.OOO......OOO.#",
            "#..............#",
            "####DD##########"
        }, difficulty: 0, weight: 0.5f, tags: new[] { "rustworks", "treasure", "foreman_safe" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec Medical01 = new("rust_medical_01", Rust, RoomType.MedicalRecovery, RoomSizeClass.Small, new[]
        {
            "##########DD####",
            "#..............#",
            "#.OO........OO.#",
            "D......,.......#",
            "D......V.......#",
            "#..............#",
            "#..............#",
            "#......,.......D",
            "#.OO........OO.D",
            "#..............#",
            "#..............#",
            "####DD##########"
        }, difficulty: 0, weight: 1f, tags: new[] { "rustworks", "medical", "infirmary_container" }) { HazardDefinitionPath = HazardPath };

        // ---- Boss arenas (2): 36x24, one BossAnchor, Boss Cache chest, Transit Car interactable ----

        public static readonly RoomLayoutSpec Boss01 = new("rust_boss_01", Rust, RoomType.Boss, RoomSizeClass.Boss, new[]
        {
            "####################################",
            "#..................................#",
            "#..................................#",
            "#...OOO........................OOO.#",
            "#...OOO........................OOO.#",
            "#..................................#",
            "#..................................#",
            "#........~~~~~........~~~~~........#",
            "#..................................#",
            "#..................................#",
            "#.................B................#",
            "#..................................#",
            "#..................................#",
            "#........~~~~~........~~~~~........#",
            "#..................................#",
            "#..................................#",
            "#...OOO........................OOO.#",
            "#...OOO........................OOO.#",
            "#..................................#",
            "#......C....................I......#",
            "#..................................#",
            "#..................................#",
            "#..................................#",
            "#################DD#################"
        }, difficulty: 4, weight: 1f, tags: new[] { "rustworks", "boss", "foundry_floor", "boss:the_foundry_titan" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec Boss02 = new("rust_boss_02", Rust, RoomType.Boss, RoomSizeClass.Boss, new[]
        {
            "####################################",
            "#..................................#",
            "#..................................#",
            "#.....OOOO....................OOOO.#",
            "#..................................#",
            "#..................................#",
            "#..........OO..........OO..........#",
            "#..........OO..........OO..........#",
            "#..................................#",
            "#..................................#",
            "#..OO............B.............OO..#",
            "#..OO..........................OO..#",
            "#..................................#",
            "#..................................#",
            "#..........OO..........OO..........#",
            "#..........OO..........OO..........#",
            "#..................................#",
            "#..................................#",
            "#.....OOOO....................OOOO.#",
            "#.......C.................I........#",
            "#..................................#",
            "D..................................#",
            "D..................................#",
            "####################################"
        }, difficulty: 4, weight: 1f, tags: new[] { "rustworks", "boss", "scrap_arena", "boss:scrap_king" }) { HazardDefinitionPath = HazardPath };

        /// <summary>Every authored Rustworks layout in stable order.</summary>
        public static IReadOnlyList<RoomLayoutSpec> All => new[]
        {
            Start01, Start02,
            CombatSmall01, CombatSmall02, CombatSmall03, CombatSmall04, CombatSmall05,
            CombatMedium01, CombatMedium02, CombatMedium03, CombatMedium04,
            CombatLarge01, CombatLarge02,
            Merchant01, Event01, Event02, Loot01, Treasure01, Medical01,
            Boss01, Boss02
        };

        public static IEnumerable<RoomLayoutSpec> OfType(RoomType type, RoomSizeClass? size = null) => All.Where(s => s.RoomType == type && (size == null || s.SizeClass == size));

        [MenuItem("RuinRail/Rooms/Rebuild Rustworks Rooms")]
        public static void Rebuild()
        {
            var results = RoomPrefabBaker.BakeAll(All, PrefabFolder, DefinitionFolder);
            Debug.Log($"Baked {results.Count} Rustworks rooms into {PrefabFolder}.");
        }
    }
}

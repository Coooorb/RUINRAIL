using System.Collections.Generic;
using System.Linq;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.Rooms
{
    /// <summary>
    /// The handmade Overgrown Labs room set (56: abandoned research complex — glass partitions, terminals, bio tanks,
    /// overgrowth, roots and vines, failed experiments). Identity comes from geometry and hazards only: obstacles ('O')
    /// are irregular organic clusters (root masses, tank clusters) and short glass/terminal partitions, acid pools
    /// ('~', Hazard_AcidPool) are small irregular puddles rather than the Metro's straight rails or the Rustworks
    /// lines. No biome-exclusive player mechanic exists. Same ASCII legend and baker as the other sets; content
    /// counts follow 126 (21 per biome) and this file grows subset by subset (TASKS 119–122).
    /// </summary>
    public static class OvergrownLabsRoomSet
    {
        public const string PrefabFolder = "Assets/Game/Prefabs/Rooms/OvergrownLabs";
        public const string DefinitionFolder = "Assets/Game/ScriptableObjects/Rooms/OvergrownLabs";
        public const string HazardPath = "Assets/Game/ScriptableObjects/Combat/Hazard_AcidPool.asset";

        private const Biome Labs = Biome.OvergrownLabs;

        // ---- Start rooms (2): safe, no enemy spawns, no hazards ----

        public static readonly RoomLayoutSpec Start01 = new("labs_start_01", Labs, RoomType.Start, RoomSizeClass.Small, new[]
        {
            "######DD########",
            "#..............#",
            "#.O..........O.#",
            "#..............#",
            "#......P.......D",
            "#......,.......D",
            "D..............#",
            "D..,........,..#",
            "#..............#",
            "#.OO........OO.#",
            "#..............#",
            "#########DD#####"
        }, difficulty: 0, weight: 1f, tags: new[] { "labs", "start", "decon_lobby" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec Start02 = new("labs_start_02", Labs, RoomType.Start, RoomSizeClass.Small, new[]
        {
            "##########DD####",
            "#..............#",
            "#....O....O....#",
            "#..............#",
            "D......P.......#",
            "D......,.......#",
            "#..............D",
            "#..............D",
            "#....O....O....#",
            "#..,........,..#",
            "#..............#",
            "####DD##########"
        }, difficulty: 0, weight: 1f, tags: new[] { "labs", "start", "reception" }) { HazardDefinitionPath = HazardPath };

        // ---- Small Combat rooms (5) ----

        public static readonly RoomLayoutSpec CombatSmall01 = new("labs_combat_small_01", Labs, RoomType.Combat, RoomSizeClass.Small, new[]
        {
            "#####DD#########",
            "#..............#",
            "#.E...OO.....E.#",
            "#.....OO.......#",
            "#..............#",
            "D...~~....OO...#",
            "D...~~....OO...D",
            "#..............D",
            "#.E..........E.#",
            "#......,.......#",
            "#..............#",
            "#########DD#####"
        }, difficulty: 1, weight: 1f, tags: new[] { "labs", "combat", "small", "tank_room" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec CombatSmall02 = new("labs_combat_small_02", Labs, RoomType.Combat, RoomSizeClass.Small, new[]
        {
            "########DD######",
            "#..............#",
            "#.E..........E.#",
            "#...O......O...#",
            "#..OOO....OOO..#",
            "D...O......O...#",
            "D......~.......D",
            "#.....~~~......D",
            "#.E....~.....E.#",
            "#..............#",
            "#..............#",
            "###DD###########"
        }, difficulty: 1, weight: 1f, tags: new[] { "labs", "combat", "small", "root_cluster" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec CombatSmall03 = new("labs_combat_small_03", Labs, RoomType.Combat, RoomSizeClass.Small, new[]
        {
            "###DD###########",
            "#..............#",
            "#.E....O.....E.#",
            "#......O.......#",
            "D......O.......#",
            "D..............D",
            "#..............D",
            "#......O.......#",
            "#.E....O.....E.#",
            "#......O.......#",
            "#..............#",
            "################"
        }, difficulty: 2, weight: 1f, tags: new[] { "labs", "combat", "small", "glass_partition" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec CombatSmall04 = new("labs_combat_small_04", Labs, RoomType.Combat, RoomSizeClass.Small, new[]
        {
            "################",
            "#..............#",
            "#..E........E..D",
            "#..............D",
            "#.....OOOO.....#",
            "D....~....~....#",
            "D....~....~....#",
            "#.....OOOO.....#",
            "#..E........E..#",
            "#......,.......#",
            "#..............#",
            "#######DD#######"
        }, difficulty: 1, weight: 1f, tags: new[] { "labs", "combat", "small", "specimen_bay" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec CombatSmall05 = new("labs_combat_small_05", Labs, RoomType.Combat, RoomSizeClass.Small, new[]
        {
            "##########DD####",
            "#..............#",
            "#.E..........E.#",
            "#..OO..........#",
            "#..OO....~~....#",
            "D........~~....#",
            "D..............D",
            "#....~~........D",
            "#.E..~~....OO.E#",
            "#..........OO..#",
            "#..............#",
            "####DD##########"
        }, difficulty: 2, weight: 1f, tags: new[] { "labs", "combat", "small", "leak_corridor" }) { HazardDefinitionPath = HazardPath };

        // ---- Medium Combat rooms (4): 24x16 ----

        public static readonly RoomLayoutSpec CombatMedium01 = new("labs_combat_medium_01", Labs, RoomType.Combat, RoomSizeClass.Medium, new[]
        {
            "###########DD###########",
            "#......................#",
            "#..E................E..#",
            "#......OO......OO......#",
            "#.....OOO......OOO.....#",
            "#......................#",
            "#..........~~..........#",
            "D.........~~~~.........D",
            "D.........~~~~.........D",
            "#..........~~..........#",
            "#......................#",
            "#.....OOO......OOO.....#",
            "#......OO......OO......#",
            "#..E................E..#",
            "#......................#",
            "###########DD###########"
        }, difficulty: 2, weight: 1f, supportsElite: true, tags: new[] { "labs", "combat", "medium", "bio_tank_hall" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec CombatMedium02 = new("labs_combat_medium_02", Labs, RoomType.Combat, RoomSizeClass.Medium, new[]
        {
            "######DD################",
            "#......................#",
            "#..E......O.......E....#",
            "#.........O............#",
            "#....O....O.....O......#",
            "#....O..........O......#",
            "#....O..~~......O......D",
            "#.......~~.............D",
            "#......................#",
            "D......O....O..........#",
            "D......O....O....~~....#",
            "#......O....O....~~....#",
            "#..E..............E....#",
            "#......................#",
            "#......................#",
            "################DD######"
        }, difficulty: 2, weight: 1f, tags: new[] { "labs", "combat", "medium", "terminal_maze" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec CombatMedium03 = new("labs_combat_medium_03", Labs, RoomType.Combat, RoomSizeClass.Medium, new[]
        {
            "#########DD#############",
            "#......................#",
            "#..E...............E...#",
            "#......OO..............#",
            "#.....OOOO....OOO......#",
            "#......OO....OOOOO.....#",
            "#.............OOO......D",
            "#..OO........,.........D",
            "D.OOOO.........~~......#",
            "D..OO.........~~~~.....#",
            "#..............~~......#",
            "#........OO......OO....#",
            "#..E....OOOO....OOOO.E.#",
            "#........OO......OO....#",
            "#......................#",
            "#############DD#########"
        }, difficulty: 2, weight: 1f, tags: new[] { "labs", "combat", "medium", "overgrown_atrium" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec CombatMedium04 = new("labs_combat_medium_04", Labs, RoomType.Combat, RoomSizeClass.Medium, new[]
        {
            "####DD##################",
            "#......................#",
            "#..E................E..#",
            "#......................D",
            "#......O........O......D",
            "#......O........O......#",
            "#......O..~~~...O......#",
            "#.........~~~..........#",
            "#.........~~~..........#",
            "#......O..~~~...O......#",
            "#......O........O......#",
            "D......O........O......#",
            "D......................#",
            "#..E................E..#",
            "#......................#",
            "##################DD####"
        }, difficulty: 3, weight: 1f, supportsElite: true, tags: new[] { "labs", "combat", "medium", "containment_ward" }) { HazardDefinitionPath = HazardPath };

        // ---- Large Combat rooms (2): 32x20 ----

        public static readonly RoomLayoutSpec CombatLarge01 = new("labs_combat_large_01", Labs, RoomType.Combat, RoomSizeClass.Large, new[]
        {
            "###############DD###############",
            "#..............................#",
            "#..E........................E..#",
            "#......OO..............OO......#",
            "#.....OOOO............OOOO.....#",
            "#......OO..............OO......#",
            "#..........,...........,.......#",
            "#...............E..............#",
            "#....~~~..........~~~..........#",
            "D...~~~~~........~~~~~.........D",
            "D....~~~..........~~~..........D",
            "#..............................#",
            "#...............E..............#",
            "#..............................#",
            "#......OO..............OO......#",
            "#.....OOOO............OOOO.....#",
            "#......OO..............OO......#",
            "#..E........................E..#",
            "#..............................#",
            "###############DD###############"
        }, difficulty: 3, weight: 1f, supportsElite: true, tags: new[] { "labs", "combat", "large", "greenhouse_dome" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec CombatLarge02 = new("labs_combat_large_02", Labs, RoomType.Combat, RoomSizeClass.Large, new[]
        {
            "#####DD#########################",
            "#..............................#",
            "#..E..........................E#",
            "#..............................#",
            "#....OOOOO.......OOOOO.........D",
            "#....O...O.......O...O.........D",
            "#....O.~.O.......O.~.O.........#",
            "#....O...O.......O...O.........#",
            "#....OO.OO.......OO.OO.........#",
            "#..............E...............#",
            "D..............................#",
            "D....OO.OO.......OO.OO.........#",
            "#....O...O.......O...O.........#",
            "#....O.~.O.......O.~.O.........#",
            "#....O...O.......O...O.........#",
            "#....OOOOO.......OOOOO.........#",
            "#..............E...............#",
            "#..E..........................E#",
            "#..............................#",
            "################################"
        }, difficulty: 3, weight: 1f, supportsElite: true, tags: new[] { "labs", "combat", "large", "containment_cells" }) { HazardDefinitionPath = HazardPath };

        // ---- Utility rooms: Merchant (1), Event (2), Loot (1), Treasure (1), Medical/Recovery (1) ----

        public static readonly RoomLayoutSpec Merchant01 = new("labs_merchant_01", Labs, RoomType.Merchant, RoomSizeClass.Small, new[]
        {
            "#######DD#######",
            "#..............#",
            "D..O........O..#",
            "D..............#",
            "#......M.......#",
            "#......,.......#",
            "#..............#",
            "#..,........,..#",
            "#..O........O..D",
            "#..............D",
            "#..............#",
            "#####DD#########"
        }, difficulty: 0, weight: 1f, tags: new[] { "labs", "merchant", "supply_office" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec Event01 = new("labs_event_01", Labs, RoomType.Event, RoomSizeClass.Small, new[]
        {
            "#########DD#####",
            "#..............#",
            "#.E..........E.#",
            "#....OO........#",
            "#..............#",
            "D......V.......#",
            "D..............D",
            "#........OO....D",
            "#..............#",
            "#.E..........E.#",
            "#..............#",
            "################"
        }, difficulty: 1, weight: 1f, tags: new[] { "labs", "event", "server_room" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec Event02 = new("labs_event_02", Labs, RoomType.Event, RoomSizeClass.Small, new[]
        {
            "####DD##########",
            "#..............#",
            "D.E....O.....E.#",
            "D.....OOO......#",
            "#..,...O....,..#",
            "#......V.......#",
            "#..............#",
            "#..,........,..#",
            "#......O.......D",
            "#.E...OOO....E.D",
            "#..............#",
            "##########DD####"
        }, difficulty: 1, weight: 1f, tags: new[] { "labs", "event", "growth_chamber" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec Loot01 = new("labs_loot_01", Labs, RoomType.Loot, RoomSizeClass.Small, new[]
        {
            "##########DD####",
            "#..............#",
            "#..O........O..D",
            "#......C.......D",
            "#..............#",
            "#..............#",
            "#..............#",
            "#..............#",
            "D......C.......#",
            "D..O........O..#",
            "#..............#",
            "####DD##########"
        }, difficulty: 0, weight: 1f, tags: new[] { "labs", "loot", "sample_archive" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec Treasure01 = new("labs_treasure_01", Labs, RoomType.Treasure, RoomSizeClass.Small, new[]
        {
            "#####DD#########",
            "#..............#",
            "#..OO......OO..D",
            "#..............D",
            "#....~~..~~....#",
            "#..............#",
            "#..............#",
            "#......C.......#",
            "#..OO......OO..#",
            "#..............#",
            "#..............#",
            "#########DD#####"
        }, difficulty: 0, weight: 0.5f, tags: new[] { "labs", "treasure", "sealed_vault" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec Medical01 = new("labs_medical_01", Labs, RoomType.MedicalRecovery, RoomSizeClass.Small, new[]
        {
            "###########DD###",
            "#..............#",
            "#.O..........O.#",
            "D......,.......#",
            "D......V.......#",
            "#..............#",
            "#..............#",
            "#......,.......D",
            "#.O..........O.D",
            "#..............#",
            "#..............#",
            "###DD###########"
        }, difficulty: 0, weight: 1f, tags: new[] { "labs", "medical", "medbay" }) { HazardDefinitionPath = HazardPath };

        // ---- Boss arenas (2): 36x24, one BossAnchor, Boss Cache chest, Transit Car interactable ----

        public static readonly RoomLayoutSpec Boss01 = new("labs_boss_01", Labs, RoomType.Boss, RoomSizeClass.Boss, new[]
        {
            "####################################",
            "#..................................#",
            "#..................................#",
            "#....OO......................OO....#",
            "#...OOOO....................OOOO...#",
            "#....OO......................OO....#",
            "#..................................#",
            "#..........~~~........~~~..........#",
            "#.........~~~~~......~~~~~.........#",
            "#..........~~~........~~~..........#",
            "#.................B................#",
            "#..................................#",
            "#..................................#",
            "#..........~~~........~~~..........#",
            "#.........~~~~~......~~~~~.........#",
            "#..........~~~........~~~..........#",
            "#....OO......................OO....#",
            "#...OOOO....................OOOO...#",
            "#....OO......................OO....#",
            "#......C....................I......#",
            "#..................................#",
            "#..................................#",
            "#..................................#",
            "#################DD#################"
        }, difficulty: 4, weight: 1f, tags: new[] { "labs", "boss", "biomass_pit", "boss:subject_omega" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec Boss02 = new("labs_boss_02", Labs, RoomType.Boss, RoomSizeClass.Boss, new[]
        {
            "####################################",
            "#..................................#",
            "#..................................#",
            "#.....OOO....................OOO...#",
            "#..................................#",
            "#..................................#",
            "#..........O............O..........#",
            "#..........O............O..........#",
            "#..................................#",
            "#..................................#",
            "#..O.............B.............O...#",
            "#..O...........................O...#",
            "#..................................#",
            "#..................................#",
            "#..........O............O..........#",
            "#..........O............O..........#",
            "#..................................#",
            "#..................................#",
            "#.....OOO....................OOO...#",
            "#.......C.................I........#",
            "#..................................#",
            "D..................................#",
            "D..................................#",
            "####################################"
        }, difficulty: 4, weight: 1f, tags: new[] { "labs", "boss", "security_core", "boss:aegis_core" }) { HazardDefinitionPath = HazardPath };

        /// <summary>Every authored Overgrown Labs layout in stable order.</summary>
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

        [MenuItem("RuinRail/Rooms/Rebuild Overgrown Labs Rooms")]
        public static void Rebuild()
        {
            var results = RoomPrefabBaker.BakeAll(All, PrefabFolder, DefinitionFolder);
            Debug.Log($"Baked {results.Count} Overgrown Labs rooms into {PrefabFolder}.");
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.Rooms
{
    /// <summary>
    /// The handmade Ruined Metro room set (56: platforms, tunnels, stalled cars, pillars, electrified rails). Each
    /// layout is authored here as ASCII (see RoomLayoutSpec legend) and baked to a static prefab + definition.
    /// Content counts follow 126 (21 per biome); this file grows subset by subset (TASKS 083–086).
    /// </summary>
    public static class RuinedMetroRoomSet
    {
        public const string PrefabFolder = "Assets/Game/Prefabs/Rooms/RuinedMetro";
        public const string DefinitionFolder = "Assets/Game/ScriptableObjects/Rooms/RuinedMetro";

        private const Biome Metro = Biome.RuinedMetro;

        // ---- Start rooms (2): safe, no enemy spawns ----

        public static readonly RoomLayoutSpec Start01 = new("metro_start_01", Metro, RoomType.Start, RoomSizeClass.Small, new[]
        {
            "####DD##########",
            "#..............#",
            "#..OO.....,....#",
            "#..OO.....P....D",
            "#.........,....D",
            "#..............#",
            "#......,.......#",
            "D..............#",
            "D........OO....#",
            "#........OO....#",
            "#..............#",
            "######DD########"
        }, difficulty: 0, weight: 1f, tags: new[] { "metro", "start", "platform" });

        public static readonly RoomLayoutSpec Start02 = new("metro_start_02", Metro, RoomType.Start, RoomSizeClass.Small, new[]
        {
            "#########DD#####",
            "#..............#",
            "#.O..........O.#",
            "#......,.......#",
            "#......P.......#",
            "D..............D",
            "D......,.......D",
            "#..............#",
            "#.O..........O.#",
            "#..............#",
            "#..............#",
            "######DD########"
        }, difficulty: 0, weight: 1f, tags: new[] { "metro", "start", "ticket_hall" });

        // ---- Small Combat rooms (5) ----

        public static readonly RoomLayoutSpec CombatSmall01 = new("metro_combat_small_01", Metro, RoomType.Combat, RoomSizeClass.Small, new[]
        {
            "######DD########",
            "#..............#",
            "#.E..........E.#",
            "#....OOO.......#",
            "#.....,........#",
            "D......~~~.....#",
            "D..............#",
            "#.......OOO....#",
            "#.E....,.....E.#",
            "#..............#",
            "#..............#",
            "########DD######"
        }, difficulty: 1, weight: 1f, tags: new[] { "metro", "combat", "small", "platform_edge" });

        public static readonly RoomLayoutSpec CombatSmall02 = new("metro_combat_small_02", Metro, RoomType.Combat, RoomSizeClass.Small, new[]
        {
            "###DD###########",
            "#..............#",
            "#..O..O..O..O..#",
            "#.E...........E#",
            "D......,.......#",
            "D..............D",
            "#......,.......D",
            "#..O..O..O..O..#",
            "#.E...........E#",
            "#..............#",
            "#..............#",
            "###########DD###"
        }, difficulty: 1, weight: 1f, tags: new[] { "metro", "combat", "small", "turnstiles" });

        public static readonly RoomLayoutSpec CombatSmall03 = new("metro_combat_small_03", Metro, RoomType.Combat, RoomSizeClass.Small, new[]
        {
            "#####DD#########",
            "#..............#",
            "#.E...........E#",
            "#..............#",
            "D...~~~~~~~~...#",
            "D..............D",
            "#..............D",
            "#...OO....OO...#",
            "#.E...,......E.#",
            "#..............#",
            "#..............#",
            "################"
        }, difficulty: 2, weight: 1f, tags: new[] { "metro", "combat", "small", "rail_cut" });

        public static readonly RoomLayoutSpec CombatSmall04 = new("metro_combat_small_04", Metro, RoomType.Combat, RoomSizeClass.Small, new[]
        {
            "################",
            "#..............#",
            "#..E........E..D",
            "#..............D",
            "#....OOOOOO....#",
            "D....OOOOOO....#",
            "D..............#",
            "#......,.......#",
            "#..E....~~..E..#",
            "#..............#",
            "#..............#",
            "######DD########"
        }, difficulty: 1, weight: 1f, tags: new[] { "metro", "combat", "small", "stalled_car" });

        public static readonly RoomLayoutSpec CombatSmall05 = new("metro_combat_small_05", Metro, RoomType.Combat, RoomSizeClass.Small, new[]
        {
            "###DD###########",
            "#..............#",
            "D..E........E..#",
            "D....O....O....#",
            "#..............#",
            "#......~~......#",
            "#......~~......#",
            "#..............#",
            "#....O....O....D",
            "#..E........E..D",
            "#..............#",
            "###########DD###"
        }, difficulty: 2, weight: 1f, tags: new[] { "metro", "combat", "small", "service_pillars" });

        // ---- Medium Combat rooms (4) ----

        public static readonly RoomLayoutSpec CombatMedium01 = new("metro_combat_medium_01", Metro, RoomType.Combat, RoomSizeClass.Medium, new[]
        {
            "###########DD###########",
            "#......................#",
            "#..E................E..#",
            "#....,.................#",
            "#..OOO..........OOO....#",
            "#......................#",
            "#......................#",
            "D....~~~~~~..~~~~~~....D",
            "D....~~~~~~..~~~~~~....D",
            "#......................#",
            "#......................#",
            "#....OOO..........OOO..#",
            "#......................#",
            "#..E................E..#",
            "#......................#",
            "###########DD###########"
        }, difficulty: 2, weight: 1f, supportsElite: true, tags: new[] { "metro", "combat", "medium", "twin_platforms" });

        public static readonly RoomLayoutSpec CombatMedium02 = new("metro_combat_medium_02", Metro, RoomType.Combat, RoomSizeClass.Medium, new[]
        {
            "######DD################",
            "#......................#",
            "#..E......O.......E....#",
            "#......................#",
            "#....O..........O......#",
            "#..........,...........#",
            "#......................D",
            "#.....O....~~....O.....D",
            "#......................#",
            "D......................#",
            "D....O..........O......#",
            "#..........,...........#",
            "#..E......O.......E....#",
            "#......................#",
            "#......................#",
            "################DD######"
        }, difficulty: 2, weight: 1f, tags: new[] { "metro", "combat", "medium", "maintenance_bay" });

        public static readonly RoomLayoutSpec CombatMedium03 = new("metro_combat_medium_03", Metro, RoomType.Combat, RoomSizeClass.Medium, new[]
        {
            "#########DD#############",
            "#......................#",
            "#..E...............E...#",
            "#......OO..............#",
            "#......OO.....OOOO.....#",
            "#.............OOOO.....#",
            "#......................D",
            "#..OOO.......,.........D",
            "D..OOO.........~~~.....#",
            "D..............~~~.....#",
            "#........OO............#",
            "#........OO......OO....#",
            "#..E.............OO.E..#",
            "#......................#",
            "#......................#",
            "#############DD#########"
        }, difficulty: 2, weight: 1f, tags: new[] { "metro", "combat", "medium", "collapsed_concourse" });

        public static readonly RoomLayoutSpec CombatMedium04 = new("metro_combat_medium_04", Metro, RoomType.Combat, RoomSizeClass.Medium, new[]
        {
            "####DD##################",
            "#......................#",
            "#..E................E..#",
            "#......................D",
            "#......O........O......D",
            "#......................#",
            "#..........~~..........#",
            "#..........~~..........#",
            "#..........~~..........#",
            "#..........~~..........#",
            "#......................#",
            "D......O........O......#",
            "D......................#",
            "#..E................E..#",
            "#......................#",
            "##################DD####"
        }, difficulty: 3, weight: 1f, supportsElite: true, tags: new[] { "metro", "combat", "medium", "tunnel_junction" });

        // ---- Large Combat rooms (2) ----

        public static readonly RoomLayoutSpec CombatLarge01 = new("metro_combat_large_01", Metro, RoomType.Combat, RoomSizeClass.Large, new[]
        {
            "###############DD###############",
            "#..............................#",
            "#..E........................E..#",
            "#......OOO............OOO......#",
            "#......OOO............OOO......#",
            "#..............................#",
            "#..........,...........,.......#",
            "#...............E..............#",
            "#....~~~~~~~........~~~~~~~....#",
            "D..............................D",
            "D..............................D",
            "#....~~~~~~~........~~~~~~~....#",
            "#...............E..............#",
            "#..............................#",
            "#......OOO............OOO......#",
            "#......OOO............OOO......#",
            "#..............................#",
            "#..E........................E..#",
            "#..............................#",
            "###############DD###############"
        }, difficulty: 3, weight: 1f, supportsElite: true, tags: new[] { "metro", "combat", "large", "central_station" });

        public static readonly RoomLayoutSpec CombatLarge02 = new("metro_combat_large_02", Metro, RoomType.Combat, RoomSizeClass.Large, new[]
        {
            "#####DD#########################",
            "#..............................#",
            "#..E..........................E#",
            "#..............................#",
            "#....~~~~~~~~~~~~~~~~~~~~......D",
            "#..............................D",
            "#........OOOOOO.......OOOO.....#",
            "#........OOOOOO.......OOOO.....#",
            "#..............................#",
            "#..............E...............#",
            "D....~~~~~~~~~~~~~~~~~~~~......#",
            "D..............................#",
            "#..............................#",
            "#....OOOO.........OOOOOO.......#",
            "#....OOOO.........OOOOOO.......#",
            "#..............................#",
            "#..............E...............#",
            "#..E..........................E#",
            "#..............................#",
            "################################"
        }, difficulty: 3, weight: 1f, supportsElite: true, tags: new[] { "metro", "combat", "large", "rail_yard" });

        // ---- Utility rooms: Merchant (1), Event (2), Loot (1), Treasure (1), Medical/Recovery (1) ----

        public static readonly RoomLayoutSpec Merchant01 = new("metro_merchant_01", Metro, RoomType.Merchant, RoomSizeClass.Small, new[]
        {
            "#########DD#####",
            "#..............#",
            "D..O........O..#",
            "D......,.......#",
            "#......M.......#",
            "#..............#",
            "#..............#",
            "#......,.......#",
            "#..O........O..D",
            "#..............D",
            "#..............#",
            "###DD###########"
        }, difficulty: 0, weight: 1f, tags: new[] { "metro", "merchant", "kiosk" });

        public static readonly RoomLayoutSpec Event01 = new("metro_event_01", Metro, RoomType.Event, RoomSizeClass.Small, new[]
        {
            "#####DD#########",
            "#..............#",
            "#.E..........E.#",
            "#....OO........#",
            "#..............#",
            "D......V.......#",
            "D..............D",
            "#..........OO..D",
            "#..............#",
            "#.E..........E.#",
            "#..............#",
            "################"
        }, difficulty: 1, weight: 1f, tags: new[] { "metro", "event", "signal_room" });

        public static readonly RoomLayoutSpec Event02 = new("metro_event_02", Metro, RoomType.Event, RoomSizeClass.Small, new[]
        {
            "######DD########",
            "#..............#",
            "D.E....OO....E.#",
            "D..............#",
            "#..,........,..#",
            "#......V.......#",
            "#..............#",
            "#..,........,..#",
            "#..............D",
            "#.E....OO....E.D",
            "#..............#",
            "###########DD###"
        }, difficulty: 1, weight: 1f, tags: new[] { "metro", "event", "control_room" });

        public static readonly RoomLayoutSpec Loot01 = new("metro_loot_01", Metro, RoomType.Loot, RoomSizeClass.Small, new[]
        {
            "###DD###########",
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
            "###########DD###"
        }, difficulty: 0, weight: 1f, tags: new[] { "metro", "loot", "storage_cage" });

        public static readonly RoomLayoutSpec Treasure01 = new("metro_treasure_01", Metro, RoomType.Treasure, RoomSizeClass.Small, new[]
        {
            "###########DD###",
            "#..............#",
            "#..OO......OO..D",
            "#..OO......OO..D",
            "#..............#",
            "#.....~~~~.....#",
            "#..............#",
            "#......C.......#",
            "#..OO......OO..#",
            "#..OO......OO..#",
            "#..............#",
            "###DD###########"
        }, difficulty: 0, weight: 0.5f, tags: new[] { "metro", "treasure", "vault_annex" });

        public static readonly RoomLayoutSpec Medical01 = new("metro_medical_01", Metro, RoomType.MedicalRecovery, RoomSizeClass.Small, new[]
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
        }, difficulty: 0, weight: 1f, tags: new[] { "metro", "medical", "first_aid_post" });

        // ---- Boss arenas (2): one BossAnchor, Boss Cache chest, Transit Car interactable ----

        public static readonly RoomLayoutSpec Boss01 = new("metro_boss_01", Metro, RoomType.Boss, RoomSizeClass.Boss, new[]
        {
            "####################################",
            "#..................................#",
            "#....I.............................#",
            "#..................................#",
            "#......OOO..................OOO....#",
            "#......OOO..................OOO....#",
            "#..................................#",
            "#....~~~~~~~~~..........~~~~~~~~~..#",
            "#..................................#",
            "#..................................#",
            "#.................B................#",
            "#..................................#",
            "#..................................#",
            "#....~~~~~~~~~..........~~~~~~~~~..#",
            "#..................................#",
            "#......OOO..................OOO....#",
            "#......OOO..................OOO....#",
            "#..................................#",
            "#..............C...................#",
            "#..................................#",
            "#..................................#",
            "#..................................#",
            "#..................................#",
            "#################DD#################"
        }, difficulty: 4, weight: 1f, tags: new[] { "metro", "boss", "terminal_hall", "boss:the_conductor" });

        public static readonly RoomLayoutSpec Boss02 = new("metro_boss_02", Metro, RoomType.Boss, RoomSizeClass.Boss, new[]
        {
            "####################################",
            "#..................................#",
            "#..................................#",
            "#....OO......................OO....#",
            "#....OO......................OO....#",
            "#..................................#",
            "#..........~~~~~~~~~~~~~~..........#",
            "#..................................#",
            "#..................................#",
            "#..OO..........................OO..#",
            "#..OO............B.............OO..#",
            "#..OO..........................OO..#",
            "#..................................#",
            "#..................................#",
            "#..........~~~~~~~~~~~~~~..........#",
            "#..................................#",
            "#....OO......................OO....#",
            "#....OO......................OO....#",
            "#..................................#",
            "#.....C....................I.......#",
            "#..................................#",
            "D..................................#",
            "D..................................#",
            "####################################"
        }, difficulty: 4, weight: 1f, tags: new[] { "metro", "boss", "tunnel_nest", "boss:tunnel_maw" });

        /// <summary>Every authored Ruined Metro layout in stable order.</summary>
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

        [MenuItem("RuinRail/Rooms/Rebuild Ruined Metro Rooms")]
        public static void Rebuild()
        {
            var results = RoomPrefabBaker.BakeAll(All, PrefabFolder, DefinitionFolder);
            Debug.Log($"Baked {results.Count} Ruined Metro rooms into {PrefabFolder}.");
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.Rooms
{
    /// <summary>
    /// The handmade Cryo Vaults room set (56: an abandoned underground preservation and cold-storage facility —
    /// refrigeration machinery, cryo containers, storage racks, frozen pipes). Identity comes from geometry and hazards
    /// only: obstacles ('O') are container aisles, pod rows and rack blocks laid out on a storage grid, coolant leaks
    /// ('~', Hazard_CryoCoolantLeak) are short straight runs along the service channels between them. No
    /// biome-exclusive player mechanic exists (no freeze, slow or temperature). Every room keeps the proven shell
    /// (doors, size, marker positions) of its Overgrown Labs counterpart, so sockets and marker counts match the other
    /// biomes exactly; same ASCII legend and baker as the other sets, 21 rooms per biome (126).
    /// </summary>
    public static class CryoVaultsRoomSet
    {
        public const string PrefabFolder = "Assets/Game/Prefabs/Rooms/CryoVaults";
        public const string DefinitionFolder = "Assets/Game/ScriptableObjects/Rooms/CryoVaults";
        public const string HazardPath = "Assets/Game/ScriptableObjects/Combat/Hazard_CryoCoolantLeak.asset";

        private const Biome Cryo = Biome.CryoVaults;

        // ---- Start rooms (2): safe, no enemy spawns, no hazards ----

        public static readonly RoomLayoutSpec Start01 = new("cryo_start_01", Cryo, RoomType.Start, RoomSizeClass.Small, new[]
        {
            "######DD########",
            "#..............#",
            "#.OO........OO.#",
            "#..............#",
            "#......P.......D",
            "#......,.......D",
            "D..............#",
            "D..............#",
            "#..,........,..#",
            "#.OO........OO.#",
            "#..............#",
            "#########DD#####"
        }, difficulty: 0, weight: 1f, tags: new[] { "cryovaults", "start", "intake_lock" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec Start02 = new("cryo_start_02", Cryo, RoomType.Start, RoomSizeClass.Small, new[]
        {
            "##########DD####",
            "#..............#",
            "#..............#",
            "#.OOOO....OOOO.#",
            "D......P.......#",
            "D..............#",
            "#..............D",
            "#..............D",
            "#.OOOO....OOOO.#",
            "#......,.......#",
            "#..............#",
            "####DD##########"
        }, difficulty: 0, weight: 1f, tags: new[] { "cryovaults", "start", "decon_gate" }) { HazardDefinitionPath = HazardPath };

        // ---- Small combat rooms (5) ----

        public static readonly RoomLayoutSpec CombatSmall01 = new("cryo_combat_small_01", Cryo, RoomType.Combat, RoomSizeClass.Small, new[]
        {
            "#####DD#########",
            "#..............#",
            "#.E..........E.#",
            "#...OO....OO...#",
            "#...OO....OO...#",
            "D......~~......#",
            "D......~~......D",
            "#...OO....OO...D",
            "#.E.OO....OO.E.#",
            "#..............#",
            "#..............#",
            "#########DD#####"
        }, difficulty: 1, weight: 1f, tags: new[] { "cryovaults", "combat", "small", "coolant_junction" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec CombatSmall02 = new("cryo_combat_small_02", Cryo, RoomType.Combat, RoomSizeClass.Small, new[]
        {
            "########DD######",
            "#..............#",
            "#.E..........E.#",
            "#....O....O....#",
            "#....O....O....#",
            "D.....,..,.....#",
            "D....~~~~~~....D",
            "#..............D",
            "#.E..OOOOOO..E.#",
            "#..............#",
            "#..............#",
            "###DD###########"
        }, difficulty: 1, weight: 1f, tags: new[] { "cryovaults", "combat", "small", "valve_closet" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec CombatSmall03 = new("cryo_combat_small_03", Cryo, RoomType.Combat, RoomSizeClass.Small, new[]
        {
            "###DD###########",
            "#..............#",
            "#.E..........E.#",
            "#.....OOOO.....#",
            "D..............#",
            "D..~~......~~..D",
            "#..............D",
            "#.....OOOO.....#",
            "#.E..........E.#",
            "#....,....,....#",
            "#..............#",
            "################"
        }, difficulty: 2, weight: 1f, tags: new[] { "cryovaults", "combat", "small", "frost_corridor" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec CombatSmall04 = new("cryo_combat_small_04", Cryo, RoomType.Combat, RoomSizeClass.Small, new[]
        {
            "################",
            "#..............#",
            "#..E........E..D",
            "#..............D",
            "#..OOOO..OOOO..#",
            "D..............#",
            "D....~~~~~~....#",
            "#..............#",
            "#..E........E..#",
            "#..OOOO..OOOO..#",
            "#..............#",
            "#######DD#######"
        }, difficulty: 1, weight: 1f, tags: new[] { "cryovaults", "combat", "small", "rack_aisle" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec CombatSmall05 = new("cryo_combat_small_05", Cryo, RoomType.Combat, RoomSizeClass.Small, new[]
        {
            "##########DD####",
            "#..............#",
            "#.E..........E.#",
            "#..O..,...,.O..#",
            "#..O........O..#",
            "D.....~~~~.....#",
            "D.....~~~~.....D",
            "#..O........O..D",
            "#.EO........O.E#",
            "#..............#",
            "#..............#",
            "####DD##########"
        }, difficulty: 2, weight: 1f, tags: new[] { "cryovaults", "combat", "small", "drain_sump" }) { HazardDefinitionPath = HazardPath };

        // ---- Medium combat rooms (4): medium_01 and medium_04 can host an Elite; medium_04 from depth 10 ----

        public static readonly RoomLayoutSpec CombatMedium01 = new("cryo_combat_medium_01", Cryo, RoomType.Combat, RoomSizeClass.Medium, new[]
        {
            "###########DD###########",
            "#......................#",
            "#..E................E..#",
            "#......................#",
            "#...OOOO........OOOO...#",
            "#......................#",
            "#...OOOO...~~...OOOO...#",
            "D..........~~..........D",
            "D..........~~..........D",
            "#...OOOO...~~...OOOO...#",
            "#......................#",
            "#...OOOO........OOOO...#",
            "#......................#",
            "#..E................E..#",
            "#......................#",
            "###########DD###########"
        }, difficulty: 2, weight: 1f, supportsElite: true, tags: new[] { "cryovaults", "combat", "medium", "cold_storage_hall" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec CombatMedium02 = new("cryo_combat_medium_02", Cryo, RoomType.Combat, RoomSizeClass.Medium, new[]
        {
            "######DD################",
            "#......................#",
            "#..E..............E....#",
            "#......................#",
            "#.....OOO....OOO.......#",
            "#.....OOO....OOO.......#",
            "#..........~~~.........D",
            "#......................D",
            "#.....OOO....OOO.......#",
            "D.....OOO....OOO.......#",
            "D.........~~~..........#",
            "#......................#",
            "#..E..............E....#",
            "#......................#",
            "#......................#",
            "################DD######"
        }, difficulty: 2, weight: 1f, tags: new[] { "cryovaults", "combat", "medium", "compressor_bay" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec CombatMedium03 = new("cryo_combat_medium_03", Cryo, RoomType.Combat, RoomSizeClass.Medium, new[]
        {
            "#########DD#############",
            "#......................#",
            "#..E...............E...#",
            "#......................#",
            "#..OOOOOO....OOOOOO....#",
            "#......................#",
            "#......,.......,.......D",
            "#.......~~~~~~.........D",
            "D......................#",
            "D......................#",
            "#..OOOOOO....OOOOOO....#",
            "#......................#",
            "#..E.................E.#",
            "#......................#",
            "#......................#",
            "#############DD#########"
        }, difficulty: 2, weight: 1f, tags: new[] { "cryovaults", "combat", "medium", "sorting_floor" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec CombatMedium04 = new("cryo_combat_medium_04", Cryo, RoomType.Combat, RoomSizeClass.Medium, new[]
        {
            "####DD##################",
            "#......................#",
            "#..E................E..#",
            "#......................D",
            "#......O.......O.......D",
            "#......O..~~~..O.......#",
            "#......O.......O.......#",
            "#.........,............#",
            "#.........,............#",
            "#......O.......O.......#",
            "#......O..~~~..O.......#",
            "D......O.......O.......#",
            "D......................#",
            "#..E................E..#",
            "#......................#",
            "##################DD####"
        }, difficulty: 3, weight: 1f, supportsElite: true, minDepth: 10, tags: new[] { "cryovaults", "combat", "medium", "condenser_walk" }) { HazardDefinitionPath = HazardPath };

        // ---- Large combat rooms (2): both can host an Elite; large_02 from depth 5 ----

        public static readonly RoomLayoutSpec CombatLarge01 = new("cryo_combat_large_01", Cryo, RoomType.Combat, RoomSizeClass.Large, new[]
        {
            "###############DD###############",
            "#..............................#",
            "#..E........................E..#",
            "#..............................#",
            "#....OOOOOO..........OOOOOO....#",
            "#....OOOOOO..........OOOOOO....#",
            "#..............................#",
            "#...............E..............#",
            "#.....~~~~............~~~~.....#",
            "D..............................D",
            "D..............................D",
            "#.....~~~~............~~~~.....#",
            "#...............E..............#",
            "#..............................#",
            "#....OOOOOO..........OOOOOO....#",
            "#....OOOOOO..........OOOOOO....#",
            "#..............................#",
            "#..E........................E..#",
            "#..............................#",
            "###############DD###############"
        }, difficulty: 3, weight: 1f, supportsElite: true, tags: new[] { "cryovaults", "combat", "large", "deep_freeze_hall" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec CombatLarge02 = new("cryo_combat_large_02", Cryo, RoomType.Combat, RoomSizeClass.Large, new[]
        {
            "#####DD#########################",
            "#..............................#",
            "#..E..........................E#",
            "#..............................#",
            "#.......OO....OO....OO.........D",
            "#.......OO....OO....OO.........D",
            "#..............................#",
            "#..~~~.................~~~.....#",
            "#..............................#",
            "#..............E...............#",
            "D.......OO....OO....OO.........#",
            "D.......OO....OO....OO.........#",
            "#..............................#",
            "#..~~~.................~~~.....#",
            "#..............................#",
            "#..............................#",
            "#..............E...............#",
            "#..E..........................E#",
            "#..............................#",
            "################################"
        }, difficulty: 3, weight: 1f, supportsElite: true, minDepth: 5, tags: new[] { "cryovaults", "combat", "large", "pod_array" }) { HazardDefinitionPath = HazardPath };

        // ---- Non-combat rooms: merchant, events (event_02 from depth 20), loot, treasure, medical — no hazards ----

        public static readonly RoomLayoutSpec Merchant01 = new("cryo_merchant_01", Cryo, RoomType.Merchant, RoomSizeClass.Small, new[]
        {
            "#######DD#######",
            "#..............#",
            "D..OO......OO..#",
            "D..............#",
            "#......M.......#",
            "#.....,.,......#",
            "#..............#",
            "#..,........,..#",
            "#..OO......OO..D",
            "#..............D",
            "#..............#",
            "#####DD#########"
        }, difficulty: 0, weight: 1f, tags: new[] { "cryovaults", "merchant", "quartermaster_hatch" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec Event01 = new("cryo_event_01", Cryo, RoomType.Event, RoomSizeClass.Small, new[]
        {
            "#########DD#####",
            "#..............#",
            "#.E..........E.#",
            "#...OO...OO....#",
            "#..............#",
            "D......V.......#",
            "D..............D",
            "#..............D",
            "#...OO...OO....#",
            "#.E..........E.#",
            "#..............#",
            "################"
        }, difficulty: 1, weight: 1f, tags: new[] { "cryovaults", "event", "monitoring_station" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec Event02 = new("cryo_event_02", Cryo, RoomType.Event, RoomSizeClass.Small, new[]
        {
            "####DD##########",
            "#..............#",
            "D.E..........E.#",
            "D.......O......#",
            "#..,....O...,..#",
            "#......V.......#",
            "#..............#",
            "#..,.OOO.......#",
            "#..............D",
            "#.E..........E.D",
            "#..............#",
            "##########DD####"
        }, difficulty: 1, weight: 1f, minDepth: 20, tags: new[] { "cryovaults", "event", "pressure_control" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec Loot01 = new("cryo_loot_01", Cryo, RoomType.Loot, RoomSizeClass.Small, new[]
        {
            "##########DD####",
            "#..............#",
            "#.OOO....OOO...D",
            "#......C.......D",
            "#..............#",
            "#..OO......OO..#",
            "#..............#",
            "#..............#",
            "D......C.......#",
            "D.OOO....OOO...#",
            "#..............#",
            "####DD##########"
        }, difficulty: 0, weight: 1f, tags: new[] { "cryovaults", "loot", "sample_lockers" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec Treasure01 = new("cryo_treasure_01", Cryo, RoomType.Treasure, RoomSizeClass.Small, new[]
        {
            "#####DD#########",
            "#..............#",
            "#..............D",
            "#..OO......OO..D",
            "#..OO......OO..#",
            "#..............#",
            "#....,.....,...#",
            "#......C.......#",
            "#..............#",
            "#..OO......OO..#",
            "#..OO......OO..#",
            "#########DD#####"
        }, difficulty: 0, weight: 0.5f, tags: new[] { "cryovaults", "treasure", "sealed_cryo_vault" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec Medical01 = new("cryo_medical_01", Cryo, RoomType.MedicalRecovery, RoomSizeClass.Small, new[]
        {
            "###########DD###",
            "#..............#",
            "#..OO..........#",
            "D..............#",
            "D......V.......#",
            "#..............#",
            "#...,....,.....#",
            "#..............D",
            "#..........OO..D",
            "#..............#",
            "#..............#",
            "###DD###########"
        }, difficulty: 0, weight: 1f, tags: new[] { "cryovaults", "medical", "thaw_clinic" }) { HazardDefinitionPath = HazardPath };

        // ---- Boss rooms (2): The Warden, Subject Zero ----

        public static readonly RoomLayoutSpec Boss01 = new("cryo_boss_01", Cryo, RoomType.Boss, RoomSizeClass.Boss, new[]
        {
            "####################################",
            "#..................................#",
            "#..................................#",
            "#...OOOO....................OOOO...#",
            "#...OOOO....................OOOO...#",
            "#..................................#",
            "#.......~~~~~..........~~~~~.......#",
            "#..................................#",
            "#.....,......................,.....#",
            "#.........OO............OO.........#",
            "#.........OO......B.....OO.........#",
            "#.........OO............OO.........#",
            "#.,..............................,.#",
            "#.......~~~~~..........~~~~~.......#",
            "#..................................#",
            "#...OOOO....................OOOO...#",
            "#...OOOO....................OOOO...#",
            "#..................................#",
            "#.............,......,.............#",
            "#......C....................I......#",
            "#..................................#",
            "#..................................#",
            "#..................................#",
            "#################DD#################"
        }, difficulty: 4, weight: 1f, tags: new[] { "cryovaults", "boss", "wardens_core", "boss:the_warden" }) { HazardDefinitionPath = HazardPath };

        public static readonly RoomLayoutSpec Boss02 = new("cryo_boss_02", Cryo, RoomType.Boss, RoomSizeClass.Boss, new[]
        {
            "####################################",
            "#..................................#",
            "#..................................#",
            "#...............O.O................#",
            "#.........OO....O.O.....OO.........#",
            "#..,......OO............OO......,..#",
            "#...........~~~......~~~...........#",
            "#..................................#",
            "#..................................#",
            "#.....OO....................OO.....#",
            "#.....OO.........B..........OO.....#",
            "#.............,.....,..............#",
            "#..................................#",
            "#..................................#",
            "#...........~~~......~~~...........#",
            "#.........OO............OO.........#",
            "#.........OO............OO.........#",
            "#..,............................,..#",
            "#..................................#",
            "#.......C........,........I........#",
            "#..................................#",
            "D..................................#",
            "D..................................#",
            "####################################"
        }, difficulty: 4, weight: 1f, tags: new[] { "cryovaults", "boss", "containment_zero", "boss:subject_zero" }) { HazardDefinitionPath = HazardPath };

        /// <summary>Every authored Cryo Vaults layout in stable order.</summary>
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

        [MenuItem("RuinRail/Rooms/Rebuild Cryo Vaults Rooms")]
        public static void Rebuild()
        {
            var results = RoomPrefabBaker.BakeAll(All, PrefabFolder, DefinitionFolder);
            Debug.Log($"Baked {results.Count} Cryo Vaults rooms into {PrefabFolder}.");
        }

        /// <summary>The biome's two Elites and two Bosses (their definitions live with the other biomes' actors).</summary>
        public static readonly string[] ActorIds = { "elite_cryo_enforcer", "elite_vault_stalker", "boss_the_warden", "boss_subject_zero" };

        /// <summary>
        /// Batch entry that builds the whole biome's environment in pipeline order: bake the rooms (placeholder tiles),
        /// generate and bind the Cryo tiles, animated Coolant Leak and dressing, repaint only the Cryo prefabs, author the
        /// lighting profile, write the door skins, generate the Elite/Boss character art and frost projectiles, and rebuild the content catalog. The other biomes' rooms are neither
        /// rebaked nor repainted — rebaking them would reset their asset-side depth gates.
        /// </summary>
        public static void BuildBiomeBatch()
        {
            try
            {
                Rebuild();
                RuinRail.EditorTools.ArtGen.ArtIntegration.GenerateBiomeEnvironment(RuinRail.EditorTools.ArtGen.TileFactory.Biome.CryoVaults);
                RuinRail.EditorTools.ArtGen.DoorFactory.Generate();
                RuinRail.EditorTools.ArtGen.ArtIntegration.GenerateActors(ActorIds, new[] { "proj_enemy_frost", "proj_boss_frost" });
                var problems = RuinRail.EditorTools.Production.GameContentCatalogBuilder.Build().Problems().ToList();
                Debug.Log("Cryo Vaults built. Catalog problems: " + (problems.Count == 0 ? "none" : string.Join(" | ", problems)));
                EditorApplication.Exit(0);
            }
            catch (System.Exception e) { Debug.LogError(e); EditorApplication.Exit(1); }
        }
    }
}

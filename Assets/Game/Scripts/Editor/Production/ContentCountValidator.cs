using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using RuinRail.EditorTools.Items;
using RuinRail.EditorTools.Rooms;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using RuinRail.Gameplay.Items.Validation;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.Production
{
    /// <summary>One counted line of the content report: what, expected, actual, PASS/FAIL.</summary>
    public sealed class ContentCountLine
    {
        public ContentCountLine(string category, string scope, int expected, int actual)
        {
            Category = category;
            Scope = scope;
            Expected = expected;
            Actual = actual;
        }

        public string Category { get; }
        public string Scope { get; }
        public int Expected { get; }
        public int Actual { get; }
        public bool Pass => Expected == Actual;
    }

    public sealed class ContentCountReport
    {
        public ContentCountReport(IReadOnlyList<ContentCountLine> lines, IReadOnlyList<string> problems)
        {
            Lines = lines;
            Problems = problems;
        }

        public IReadOnlyList<ContentCountLine> Lines { get; }
        public IReadOnlyList<string> Problems { get; }
        public bool Pass => Lines.All(l => l.Pass) && Problems.Count == 0;

        /// <summary>Deterministic markdown (stable ordering, no timestamps) for the final gate / CI.</summary>
        public string ToMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# RUINRAIL V1 content-count validation (production/126)");
            sb.AppendLine();
            sb.AppendLine($"Result: **{(Pass ? "PASS" : "FAIL")}** — {Lines.Count(l => l.Pass)}/{Lines.Count} counts exact, {Problems.Count} problems.");
            sb.AppendLine();
            sb.AppendLine("| Category | Scope | Expected | Actual | Result |");
            sb.AppendLine("|---|---|---:|---:|---|");
            foreach (var line in Lines)
            {
                sb.AppendLine($"| {line.Category} | {line.Scope} | {line.Expected} | {line.Actual} | {(line.Pass ? "PASS" : "FAIL")} |");
            }

            if (Problems.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Problems");
                sb.AppendLine();
                foreach (var problem in Problems) sb.AppendLine($"- {problem}");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Production validator for the fixed V1 content (production/126_FINAL_MVP_CONTENT_COUNTS): exact counts per
    /// category/biome/class, unique stable ids, resolvable references and biome registrations. Missing, extra or
    /// duplicated content fails. Runs from the menu or the test suite and writes a deterministic report.
    /// </summary>
    public static class ContentCountValidator
    {
        public const string ReportPath = "TestResults/content_counts.md";

        public const int Biomes = 3;
        public const int RoomsPerBiome = 21;
        public const int RoomsTotal = 63;
        public const int Weapons = 33;
        public const int WeaponClasses = 11;
        public const int RegularWeapons = 22;
        public const int LegendaryWeapons = 11;
        public const int ArmorFamilies = 9;
        public const int AccessoryFamilies = 16;
        public const int Consumables = 10;
        public const int NormalEnemies = 9;
        public const int Elites = 6;
        public const int Bosses = 6;
        public const int ElitesPerBiome = 2;
        public const int BossesPerBiome = 2;
        public const int DungeonEvents = 6;

        public static readonly (RoomType type, RoomSizeClass? size, int count)[] RoomDistribution =
        {
            (RoomType.Start, null, 2),
            (RoomType.Combat, RoomSizeClass.Small, 5),
            (RoomType.Combat, RoomSizeClass.Medium, 4),
            (RoomType.Combat, RoomSizeClass.Large, 2),
            (RoomType.Merchant, null, 1),
            (RoomType.Event, null, 2),
            (RoomType.Loot, null, 1),
            (RoomType.Treasure, null, 1),
            (RoomType.MedicalRecovery, null, 1),
            (RoomType.Boss, null, 2)
        };

        private static List<T> LoadAll<T>() where T : UnityEngine.Object =>
            AssetDatabase.FindAssets($"t:{typeof(T).Name}").Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g))).Where(a => a != null).OrderBy(a => AssetDatabase.GetAssetPath(a), StringComparer.Ordinal).ToList();

        [MenuItem("RuinRail/Production/Validate Content Counts")]
        public static void ValidateMenu()
        {
            var report = ValidateProject();
            var path = WriteReport(report);
            Debug.Log($"Content counts: {(report.Pass ? "PASS" : "FAIL")} — report written to {path}.");
        }

        public static string WriteReport(ContentCountReport report, string path = ReportPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, report.ToMarkdown(), new UTF8Encoding(false));
            return path;
        }

        public static ContentCountReport ValidateProject()
        {
            var lines = new List<ContentCountLine>();
            var problems = new List<string>();

            // ---- Biomes ----
            lines.Add(new ContentCountLine("Biomes", "all", Biomes, Enum.GetValues(typeof(Biome)).Length));

            // ---- Rooms: 63 prefabs, 21 per biome, exact distribution, unique ids, validator-ready ----
            var rooms = LoadAll<RoomDefinition>().Where(r => !AssetDatabase.GetAssetPath(r).Contains("/_Test/") && !r.Id.StartsWith("test_")).ToList();
            lines.Add(new ContentCountLine("Room prefabs", "all biomes", RoomsTotal, rooms.Count));
            foreach (var biome in (Biome[])Enum.GetValues(typeof(Biome)))
            {
                var ofBiome = rooms.Where(r => r.Biome == biome).ToList();
                lines.Add(new ContentCountLine("Room prefabs", biome.ToString(), RoomsPerBiome, ofBiome.Count));
                foreach (var (type, size, count) in RoomDistribution)
                {
                    var actual = ofBiome.Count(r => r.RoomType == type && (size == null || r.SizeClass == size));
                    lines.Add(new ContentCountLine($"Rooms: {type}{(size != null ? " " + size : string.Empty)}", biome.ToString(), count, actual));
                }
            }

            AddDuplicates(problems, "room id", rooms.Select(r => r.Id));
            foreach (var room in rooms)
            {
                if (room.Prefab == null) problems.Add($"room '{room.Id}': no prefab.");
                else if (room.Prefab.GetComponent<RoomRoot>() == null || room.Prefab.GetComponent<RoomRoot>().Definition != room) problems.Add($"room '{room.Id}': prefab does not point back to its definition.");
            }

            foreach (var report in RoomValidator.ValidateAll(rooms).Where(r => !r.IsGeneratorReady))
            {
                problems.Add($"room '{report.RoomId}' is not generator-ready: {string.Join("; ", report.Problems)}");
            }

            // ---- Weapons: 33 = 11 classes x (2 regular + 1 Legendary), full catalog audit ----
            var weapons = WeaponCatalogValidationTools.LoadAllWeaponDefinitions();
            var weaponReport = WeaponCatalogValidator.Validate(weapons, AssetDatabase.LoadAssetAtPath<Gameplay.Economy.EconomyConfig>(WeaponCatalogValidationTools.EconomyConfigPath), WeaponCatalogValidationTools.LoadSpecialRegistry());
            lines.Add(new ContentCountLine("Weapons", "all", Weapons, weapons.Count));
            lines.Add(new ContentCountLine("Weapon classes", "with authored weapons", WeaponClasses, weapons.Select(w => w.WeaponClass).Distinct().Count()));
            lines.Add(new ContentCountLine("Weapons", "regular", RegularWeapons, weaponReport.PerClass.Values.Sum(v => v.normal)));
            lines.Add(new ContentCountLine("Weapons", "Legendary-only", LegendaryWeapons, weaponReport.PerClass.Values.Sum(v => v.legendary)));
            foreach (var problem in weaponReport.Problems) problems.Add($"weapon catalog: {problem}");

            // ---- Armor / accessories / consumables ----
            var armors = LoadAll<ArmorDefinition>();
            var accessories = LoadAll<AccessoryDefinition>();
            var consumables = LoadAll<ConsumableDefinition>();
            lines.Add(new ContentCountLine("Armor families", "all", ArmorFamilies, armors.Count));
            lines.Add(new ContentCountLine("Accessory families", "all", AccessoryFamilies, accessories.Count));
            lines.Add(new ContentCountLine("Consumables", "all", Consumables, consumables.Count));

            // ---- Item ids: unique across every item definition ----
            var items = LoadAll<ItemDefinition>();
            AddDuplicates(problems, "item id", items.Select(i => i.Id));
            foreach (var item in items.Where(i => string.IsNullOrWhiteSpace(i.Id))) problems.Add($"item asset '{AssetDatabase.GetAssetPath(item)}' has no id.");

            // ---- Enemies: 9 normal archetypes, 6 Elites (2 per biome), 6 Bosses (2 per biome) ----
            var enemies = LoadAll<EnemyDefinition>();
            var elites = LoadAll<EliteDefinition>();
            var bosses = LoadAll<BossDefinition>();
            lines.Add(new ContentCountLine("Normal enemy archetypes", "all", NormalEnemies, enemies.Count));
            lines.Add(new ContentCountLine("Elites", "all", Elites, elites.Count));
            lines.Add(new ContentCountLine("Bosses", "all", Bosses, bosses.Count));
            foreach (var biome in (Biome[])Enum.GetValues(typeof(Biome)))
            {
                lines.Add(new ContentCountLine("Elites", biome.ToString(), ElitesPerBiome, elites.Count(e => e.Biome == biome)));
                lines.Add(new ContentCountLine("Bosses", biome.ToString(), BossesPerBiome, bosses.Count(b => b.Biome == biome)));
            }

            AddDuplicates(problems, "enemy id", enemies.Select(e => e.Id));
            AddDuplicates(problems, "elite id", elites.Select(e => e.Id));
            AddDuplicates(problems, "boss id", bosses.Select(b => b.Id));
            AddDuplicates(problems, "actor id (enemy/elite/boss)", enemies.Select(e => e.Id).Concat(elites.Select(e => e.Id)).Concat(bosses.Select(b => b.Id)));
            foreach (var elite in elites)
            {
                if (elite.Moveset.Count == 0 || elite.Moveset.Any(a => a == null)) problems.Add($"elite '{elite.Id}': moveset has a missing attack reference.");
            }

            foreach (var boss in bosses)
            {
                if (boss.Moveset.Count == 0 || boss.Moveset.Any(a => a == null)) problems.Add($"boss '{boss.Id}': moveset has a missing attack reference.");
                if (boss.PhaseTwoArenaHazards.Any(a => a == null)) problems.Add($"boss '{boss.Id}': phase-two hazard reference missing.");
                if (boss.CanSummon && boss.SummonDefinition == null) problems.Add($"boss '{boss.Id}': summon definition missing.");
            }

            // ---- Boss arenas resolve to a boss of their own biome ----
            foreach (var arena in rooms.Where(r => r.RoomType == RoomType.Boss))
            {
                var pins = arena.Tags.Where(t => t.StartsWith(BossSelection.TagPrefix, StringComparison.Ordinal)).Select(t => t.Substring(BossSelection.TagPrefix.Length)).ToList();
                foreach (var pin in pins)
                {
                    var match = bosses.FirstOrDefault(b => b.Id == pin || b.Id == "boss_" + pin);
                    if (match == null) problems.Add($"arena '{arena.Id}' pins unknown boss '{pin}'.");
                    else if (match.Biome != arena.Biome) problems.Add($"arena '{arena.Id}' ({arena.Biome}) pins '{match.Id}' of {match.Biome}.");
                }
            }

            // ---- Dungeon events: exactly the six approved kinds ----
            lines.Add(new ContentCountLine("Dungeon event kinds", "all", DungeonEvents, Enum.GetValues(typeof(DungeonEventKind)).Length));

            return new ContentCountReport(lines, problems);
        }

        private static void AddDuplicates(List<string> problems, string what, IEnumerable<string> ids)
        {
            foreach (var group in ids.Where(id => !string.IsNullOrEmpty(id)).GroupBy(id => id).Where(g => g.Count() > 1).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                problems.Add($"duplicate {what} '{group.Key}' ({group.Count()} definitions).");
            }
        }
    }
}

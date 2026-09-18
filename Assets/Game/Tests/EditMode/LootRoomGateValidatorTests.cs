using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.EditorTools.Items;
using RuinRail.EditorTools.Rooms;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>
    /// TASK 082 gate: runs every production validator over the real project data and writes a machine-readable
    /// report (TestResults/gate_082_validators.json) the gate document quotes. A validator with nothing to validate
    /// is a failure, never a pass.
    /// </summary>
    public class LootRoomGateValidatorTests
    {
        private const string ReportPath = "TestResults/gate_082_validators.json";

        private sealed class Check
        {
            public string Name;
            public string Status;
            public int Count;
            public List<string> Problems = new();
        }

        [Test]
        public void ProductionValidators_AllPass_AndTheGateReportIsWritten()
        {
            var checks = new List<Check>();

            // Rooms (schema + validator over every authored RoomDefinition).
            var rooms = RoomValidationTools.ValidateProject();
            checks.Add(new Check { Name = "rooms", Status = rooms.Count > 0 && rooms.All(r => r.IsGeneratorReady) ? "PASS" : "FAIL", Count = rooms.Count, Problems = rooms.SelectMany(r => r.Problems.Select(p => $"{r.RoomId}: {p}")).ToList() });

            // Weapon catalog (33 weapons / 11 classes / economy prices / Legendary specials).
            var weapons = WeaponCatalogValidationTools.ValidateProject();
            checks.Add(new Check { Name = "weapon_catalog", Status = weapons.DefinitionCount > 0 && weapons.IsValid ? "PASS" : "FAIL", Count = weapons.DefinitionCount, Problems = weapons.Problems.ToList() });

            // Item definition registry (ids, uniqueness, categories) over every ItemDefinition asset.
            var items = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            var registry = ItemDefinitionRegistry.Build(items);
            checks.Add(new Check { Name = "item_registry", Status = items.Count > 0 && registry.Problems.Count == 0 ? "PASS" : "FAIL", Count = items.Count, Problems = registry.Problems.ToList() });

            // Loot sources: all four approved kinds configured with resolvable tables and rarity tables per quality.
            var loot = AssetDatabase.LoadAssetAtPath<LootSourceCatalog>("Assets/Game/ScriptableObjects/Loot/LootSourceCatalog.asset");
            var lootProblems = new List<string>();
            foreach (LootSourceKind kind in Enum.GetValues(typeof(LootSourceKind)))
            {
                if (loot == null || !loot.TryGet(kind, out var source) || source.Table == null) lootProblems.Add($"{kind}: no table");
            }

            foreach (LootQuality quality in Enum.GetValues(typeof(LootQuality)))
            {
                if (loot == null || loot.RarityTableFor(quality) == null || loot.RarityTableFor(quality).Quality != quality) lootProblems.Add($"{quality}: no rarity table");
            }

            var tableEntries = loot == null ? 0 : loot.Sources.Where(s => s.Table != null).Sum(s => s.Table.Rolls.Sum(r => r.Entries?.Length ?? 0));
            foreach (var source in loot != null ? loot.Sources : Array.Empty<LootSourceCatalog.Source>())
            {
                if (source.Table == null) continue;
                foreach (var roll in source.Table.Rolls)
                foreach (var entry in roll.Entries ?? Array.Empty<LootTableDefinition.Entry>())
                {
                    if (entry.Item != null && !registry.TryGet(entry.Item.Id, out _)) lootProblems.Add($"{source.Kind}/{roll.Label}: unresolvable {entry.Item.Id}");
                }
            }

            checks.Add(new Check { Name = "loot_sources", Status = tableEntries > 0 && lootProblems.Count == 0 ? "PASS" : "FAIL", Count = tableEntries, Problems = lootProblems });

            // Enemy archetypes: the nine approved ids with positive threat costs.
            var enemies = AssetDatabase.FindAssets("t:EnemyDefinition", new[] { "Assets/Game/ScriptableObjects/Enemies" }).Select(g => AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            var expectedEnemies = new[] { "grunt", "shooter", "swarm", "charger", "brute", "bomber", "shield_enemy", "sniper_enemy", "summoner" };
            var enemyProblems = expectedEnemies.Where(id => enemies.All(e => e.Id != id)).Select(id => $"missing archetype {id}").ToList();
            enemyProblems.AddRange(enemies.Where(e => e.ThreatCost <= 0).Select(e => $"{e.Id}: threat cost must be positive"));
            checks.Add(new Check { Name = "enemy_archetypes", Status = enemies.Count >= 9 && enemyProblems.Count == 0 ? "PASS" : "FAIL", Count = enemies.Count, Problems = enemyProblems });

            // Event config: every approved kind has its data (prices come from EconomyConfig, checked via PriceService elsewhere).
            var events = AssetDatabase.LoadAssetAtPath<DungeonEventConfig>("Assets/Game/ScriptableObjects/Balance/DungeonEventConfig.asset");
            var eventProblems = new List<string>();
            if (events == null) eventProblems.Add("DungeonEventConfig missing");
            else
            {
                if (events.BrokenMachineTable == null) eventProblems.Add("Broken Machine table unassigned");
                if (events.SupplySignalWaveSeconds <= 0) eventProblems.Add("Supply Signal duration");
                if (events.WeaponCacheChoices != 3) eventProblems.Add("Weapon Cache must present exactly three weapons (57)");
            }

            checks.Add(new Check { Name = "event_config", Status = eventProblems.Count == 0 ? "PASS" : "FAIL", Count = Enum.GetValues(typeof(DungeonEventKind)).Length, Problems = eventProblems });

            WriteReport(checks);

            foreach (var check in checks)
            {
                Assert.AreEqual("PASS", check.Status, $"{check.Name}: {string.Join("; ", check.Problems)}");
                Assert.Greater(check.Count, 0, $"{check.Name}: a validator with nothing to validate is not a pass.");
            }
        }

        private static void WriteReport(List<Check> checks)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"gate\": \"TASK_082\",\n  \"generatedUtc\": \"").Append(DateTime.UtcNow.ToString("O")).Append("\",\n  \"checks\": [\n");
            for (var i = 0; i < checks.Count; i++)
            {
                var c = checks[i];
                sb.Append("    {\"name\": \"").Append(c.Name).Append("\", \"status\": \"").Append(c.Status).Append("\", \"count\": ").Append(c.Count).Append(", \"problems\": [");
                sb.Append(string.Join(", ", c.Problems.Select(p => "\"" + p.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"")));
                sb.Append("]}").Append(i < checks.Count - 1 ? ",\n" : "\n");
            }

            sb.Append("  ]\n}\n");
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath) ?? ".");
            File.WriteAllText(ReportPath, sb.ToString());
            Debug.Log($"Gate 082 validator report written to {ReportPath}");
        }
    }
}

using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using RuinRail.EditorTools.Rooms;
using RuinRail.Gameplay.Combat.Hazards;
using UnityEditor;
using UnityEngine.Tilemaps;

namespace RuinRail.Tests
{
    /// <summary>TASK 109+: the handmade Rustworks room set — counts per subset, stable ids, metadata, static geometry, validator.</summary>
    public class RustworksRoomSetTests
    {
        private static RoomDefinition[] RustDefinitions() => RoomValidationTools.LoadAllRoomDefinitions().Where(d => d.Biome == Biome.Rustworks && AssetDatabase.GetAssetPath(d).StartsWith(RustworksRoomSet.DefinitionFolder)).ToArray();

        [Test]
        public void Subsets_HaveTheApprovedCounts_WithUniqueStableIds()
        {
            var specs = RustworksRoomSet.All;
            Assert.AreEqual(2, RustworksRoomSet.OfType(RoomType.Start).Count(), "TASK 109: 2 Start");
            Assert.AreEqual(5, RustworksRoomSet.OfType(RoomType.Combat, RoomSizeClass.Small).Count(), "TASK 109: 5 Small Combat");
            Assert.AreEqual(4, RustworksRoomSet.OfType(RoomType.Combat, RoomSizeClass.Medium).Count(), "TASK 110: 4 Medium Combat");
            Assert.AreEqual(2, RustworksRoomSet.OfType(RoomType.Combat, RoomSizeClass.Large).Count(), "TASK 110: 2 Large Combat");
            Assert.AreEqual(1, RustworksRoomSet.OfType(RoomType.Merchant).Count(), "TASK 111: 1 Merchant");
            Assert.AreEqual(2, RustworksRoomSet.OfType(RoomType.Event).Count(), "TASK 111: 2 Event");
            Assert.AreEqual(1, RustworksRoomSet.OfType(RoomType.Loot).Count(), "TASK 111: 1 Loot");
            Assert.AreEqual(1, RustworksRoomSet.OfType(RoomType.Treasure).Count(), "TASK 111: 1 Treasure");
            Assert.AreEqual(1, RustworksRoomSet.OfType(RoomType.MedicalRecovery).Count(), "TASK 111: 1 Medical/Recovery");
            Assert.AreEqual(2, RustworksRoomSet.OfType(RoomType.Boss).Count(), "TASK 112: 2 Boss arenas");
            Assert.AreEqual(21, specs.Count, "126: exactly 21 authored rooms per biome.");
            Assert.AreEqual(specs.Count, specs.Select(s => s.Id).Distinct().Count(), "Stable ids are unique.");
            Assert.IsTrue(specs.All(s => s.Biome == Biome.Rustworks));
            Assert.IsTrue(specs.All(s => s.Id.StartsWith("rust_")));
            Assert.IsTrue(specs.All(s => s.Tags.Contains("rustworks")));
            Assert.IsFalse(RuinedMetroRoomSet.All.Select(s => s.Id).Intersect(specs.Select(s => s.Id)).Any(), "Ids never collide across biomes.");

            var definitions = RustDefinitions();
            Assert.AreEqual(specs.Count, definitions.Length, "Every spec is baked exactly once.");
            CollectionAssert.AreEquivalent(specs.Select(s => s.Id), definitions.Select(d => d.Id));
            Assert.AreEqual(2, definitions.Count(d => d.RoomType == RoomType.Start));
            Assert.AreEqual(5, definitions.Count(d => d.RoomType == RoomType.Combat && d.SizeClass == RoomSizeClass.Small));
            Assert.AreEqual(4, definitions.Count(d => d.RoomType == RoomType.Combat && d.SizeClass == RoomSizeClass.Medium));
            Assert.AreEqual(2, definitions.Count(d => d.RoomType == RoomType.Combat && d.SizeClass == RoomSizeClass.Large));
            Assert.IsTrue(definitions.Where(d => d.SupportsElite).All(d => d.RoomType == RoomType.Combat && d.SizeClass != RoomSizeClass.Small), "Elite-capable rooms are medium/large combat rooms.");
            Assert.GreaterOrEqual(definitions.Count(d => d.SupportsElite), 2, "Elite encounters need compatible combat rooms.");
            Assert.IsTrue(definitions.All(d => d.Biome == Biome.Rustworks), "Every prefab is tagged Rustworks.");
        }

        [Test]
        public void EveryBakedRoom_MatchesItsSpecMetadata_AndUsesItsSizeClassTarget()
        {
            foreach (var spec in RustworksRoomSet.All)
            {
                var definition = RustDefinitions().Single(d => d.Id == spec.Id);
                Assert.AreEqual(spec.RoomType, definition.RoomType, spec.Id);
                Assert.AreEqual(spec.SizeClass, definition.SizeClass, spec.Id);
                Assert.AreEqual(RoomSizeClasses.DimensionsOf(spec.SizeClass), definition.Dimensions, spec.Id);
                Assert.AreEqual(spec.Difficulty, definition.Difficulty, spec.Id);
                Assert.AreEqual(spec.SupportsElite, definition.SupportsElite, spec.Id);
                CollectionAssert.AreEquivalent(spec.SupportedDoors().ToList(), definition.SupportedDoors.ToList(), spec.Id);
                CollectionAssert.AreEquivalent(spec.Tags, definition.Tags, spec.Id);
                Assert.IsTrue(definition.HasTag("rustworks"), spec.Id);
                Assert.IsNotNull(definition.Prefab, spec.Id);
                Assert.AreSame(definition, definition.Prefab.GetComponent<RoomRoot>().Definition, $"{spec.Id}: prefab points back to its definition.");
            }
        }

        [Test]
        public void BakedPrefabs_CarryStaticTilemapGeometry_Sockets_Markers_AndRustworksHazards()
        {
            var furnace = AssetDatabase.LoadAssetAtPath<HazardDefinition>(RustworksRoomSet.HazardPath);
            Assert.IsNotNull(furnace, "Rustworks hazard definition exists.");
            Assert.AreEqual("hazard_furnace_grate", furnace.Id);
            var hazardRooms = 0;
            foreach (var definition in RustDefinitions())
            {
                var root = definition.Prefab.GetComponent<RoomRoot>();
                foreach (var layer in RoomTilemapLayers.All)
                {
                    Assert.IsNotNull(RoomGridBuilder.FindLayer(root.Grid, layer), $"{definition.Id}: layer {layer} present.");
                }

                var floor = RoomGridBuilder.FindLayer(root.Grid, RoomTilemapLayer.Floor);
                var walls = RoomGridBuilder.FindLayer(root.Grid, RoomTilemapLayer.Walls);
                Assert.Greater(CountTiles(floor), definition.Dimensions.x * definition.Dimensions.y / 2, $"{definition.Id}: handmade floor is painted.");
                Assert.GreaterOrEqual(CountTiles(walls), 2 * (definition.Dimensions.x + definition.Dimensions.y) - 4 - 8, $"{definition.Id}: wall ring painted (minus door gaps).");
                Assert.GreaterOrEqual(root.GetSockets().Count, definition.RoomType == RoomType.Combat ? 2 : 1, definition.Id);
                Assert.IsTrue(root.GetSockets().All(s => s.Width == DoorSocket.DefaultWidth));

                var markers = root.GetMarkers();
                if (definition.RoomType == RoomType.Start)
                {
                    Assert.IsTrue(markers.Any(m => m.Role == RoomMarkerRole.PlayerSpawn), definition.Id);
                    Assert.IsFalse(markers.Any(m => m.Role == RoomMarkerRole.EnemySpawn), $"{definition.Id}: Start rooms are safe.");
                    Assert.IsFalse(markers.Any(m => m.Role == RoomMarkerRole.Hazard), $"{definition.Id}: Start rooms carry no hazards.");
                }
                else if (definition.RoomType == RoomType.Combat)
                {
                    Assert.GreaterOrEqual(markers.Count(m => m.Role == RoomMarkerRole.EnemySpawn), 4, $"{definition.Id}: enough spawn markers.");
                }
                else if (definition.RoomType == RoomType.Event)
                {
                    Assert.AreEqual(1, markers.Count(m => m.Role == RoomMarkerRole.EventAnchor), $"{definition.Id}: exactly one event anchor.");
                    Assert.GreaterOrEqual(markers.Count(m => m.Role == RoomMarkerRole.EnemySpawn), 4, $"{definition.Id}: encounter events need spawn markers.");
                }
                else if (definition.RoomType == RoomType.Loot || definition.RoomType == RoomType.Treasure)
                {
                    Assert.GreaterOrEqual(markers.Count(m => m.Role == RoomMarkerRole.ChestSpawn), 1, definition.Id);
                    Assert.IsFalse(markers.Any(m => m.Role == RoomMarkerRole.EnemySpawn), $"{definition.Id}: little or no combat.");
                }
                else if (definition.RoomType == RoomType.Merchant)
                {
                    Assert.AreEqual(1, markers.Count(m => m.Role == RoomMarkerRole.MerchantAnchor), definition.Id);
                    Assert.IsFalse(markers.Any(m => m.Role == RoomMarkerRole.EnemySpawn || m.Role == RoomMarkerRole.Hazard), $"{definition.Id}: safe merchant room.");
                }
                else if (definition.RoomType == RoomType.Boss)
                {
                    Assert.AreEqual(RoomSizeClass.Boss, definition.SizeClass, definition.Id);
                    Assert.AreEqual(1, markers.Count(m => m.Role == RoomMarkerRole.BossAnchor), definition.Id);
                    Assert.AreEqual(1, markers.Count(m => m.Role == RoomMarkerRole.ChestSpawn), $"{definition.Id}: Boss Cache chest marker.");
                    Assert.AreEqual(1, markers.Count(m => m.Role == RoomMarkerRole.InteractableSpawn), $"{definition.Id}: Transit Car marker.");
                    Assert.IsFalse(markers.Any(m => m.Role == RoomMarkerRole.EnemySpawn), $"{definition.Id}: boss arenas run the boss, not a director encounter.");
                    Assert.IsTrue(definition.Tags.Any(t => t == "boss:the_foundry_titan" || t == "boss:scrap_king"), $"{definition.Id}: arena pins one of the two Rustworks bosses (46).");
                }
                else if (definition.RoomType == RoomType.MedicalRecovery)
                {
                    Assert.AreEqual(1, markers.Count(m => m.Role == RoomMarkerRole.EventAnchor), definition.Id);
                    Assert.IsFalse(markers.Any(m => m.Role == RoomMarkerRole.EnemySpawn), definition.Id);
                }

                foreach (var hazard in markers.Where(m => m.Role == RoomMarkerRole.Hazard))
                {
                    hazardRooms++;
                    Assert.AreSame(furnace, hazard.GetComponent<RoomHazard>()?.Definition, $"{definition.Id}: Rustworks hazards are furnace grates, never Metro rails.");
                }
            }

            Assert.Greater(hazardRooms, 0, "The biome identity includes furnace hazards in combat rooms.");
        }

        [Test]
        public void RoomValidator_PassesEveryRustworksRoom()
        {
            var reports = RoomValidator.ValidateAll(RustDefinitions());
            Assert.AreEqual(RustworksRoomSet.All.Count, reports.Count);
            foreach (var report in reports)
            {
                Assert.IsTrue(report.IsGeneratorReady, $"{report.RoomId}:\n" + string.Join("\n", report.Problems));
            }
        }

        [Test]
        public void Layouts_EncodeTwoWideCardinalSockets_AndOnlyLegalLegendCharacters()
        {
            foreach (var spec in RustworksRoomSet.All)
            {
                foreach (var (direction, cell, width) in spec.Sockets())
                {
                    Assert.AreEqual(2, width, $"{spec.Id} {direction}");
                    Assert.IsTrue(spec.HasFloor(cell.x, cell.y), $"{spec.Id} {direction} socket at {cell} is open floor.");
                }

                foreach (var row in spec.Rows)
                {
                    Assert.IsTrue(row.All(c => "#.,O~DPECLMVBIT".Contains(c)), $"{spec.Id}: illegal legend character in '{row}'.");
                }
            }
        }

        private static int CountTiles(Tilemap map)
        {
            var count = 0;
            foreach (var position in map.cellBounds.allPositionsWithin)
            {
                if (map.HasTile(position)) count++;
            }

            return count;
        }
    }
}

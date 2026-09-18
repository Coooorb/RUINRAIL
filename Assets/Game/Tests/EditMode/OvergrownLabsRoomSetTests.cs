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
    /// <summary>TASK 119+: the handmade Overgrown Labs room set — counts per subset, stable ids, metadata, static geometry, validator.</summary>
    public class OvergrownLabsRoomSetTests
    {
        private static RoomDefinition[] LabsDefinitions() => RoomValidationTools.LoadAllRoomDefinitions().Where(d => d.Biome == Biome.OvergrownLabs && AssetDatabase.GetAssetPath(d).StartsWith(OvergrownLabsRoomSet.DefinitionFolder)).ToArray();

        [Test]
        public void Subsets_HaveTheApprovedCounts_WithUniqueStableIds()
        {
            var specs = OvergrownLabsRoomSet.All;
            Assert.AreEqual(2, OvergrownLabsRoomSet.OfType(RoomType.Start).Count(), "TASK 119: 2 Start");
            Assert.AreEqual(5, OvergrownLabsRoomSet.OfType(RoomType.Combat, RoomSizeClass.Small).Count(), "TASK 119: 5 Small Combat");
            Assert.AreEqual(4, OvergrownLabsRoomSet.OfType(RoomType.Combat, RoomSizeClass.Medium).Count(), "TASK 120: 4 Medium Combat");
            Assert.AreEqual(2, OvergrownLabsRoomSet.OfType(RoomType.Combat, RoomSizeClass.Large).Count(), "TASK 120: 2 Large Combat");
            Assert.AreEqual(1, OvergrownLabsRoomSet.OfType(RoomType.Merchant).Count(), "TASK 121: 1 Merchant");
            Assert.AreEqual(2, OvergrownLabsRoomSet.OfType(RoomType.Event).Count(), "TASK 121: 2 Event");
            Assert.AreEqual(1, OvergrownLabsRoomSet.OfType(RoomType.Loot).Count(), "TASK 121: 1 Loot");
            Assert.AreEqual(1, OvergrownLabsRoomSet.OfType(RoomType.Treasure).Count(), "TASK 121: 1 Treasure");
            Assert.AreEqual(1, OvergrownLabsRoomSet.OfType(RoomType.MedicalRecovery).Count(), "TASK 121: 1 Medical/Recovery");
            Assert.AreEqual(2, OvergrownLabsRoomSet.OfType(RoomType.Boss).Count(), "TASK 122: 2 Boss arenas");
            Assert.AreEqual(21, specs.Count, "126: exactly 21 authored rooms per biome.");
            Assert.AreEqual(specs.Count, specs.Select(s => s.Id).Distinct().Count(), "Stable ids are unique.");
            Assert.IsTrue(specs.All(s => s.Biome == Biome.OvergrownLabs));
            Assert.IsTrue(specs.All(s => s.Id.StartsWith("labs_")));
            Assert.IsTrue(specs.All(s => s.Tags.Contains("labs")));
            Assert.IsFalse(RuinedMetroRoomSet.All.Concat(RustworksRoomSet.All).Select(s => s.Id).Intersect(specs.Select(s => s.Id)).Any(), "Ids never collide across biomes.");

            var definitions = LabsDefinitions();
            Assert.AreEqual(specs.Count, definitions.Length, "Every spec is baked exactly once.");
            CollectionAssert.AreEquivalent(specs.Select(s => s.Id), definitions.Select(d => d.Id));
            Assert.AreEqual(2, definitions.Count(d => d.RoomType == RoomType.Start));
            Assert.AreEqual(5, definitions.Count(d => d.RoomType == RoomType.Combat && d.SizeClass == RoomSizeClass.Small));
            Assert.AreEqual(4, definitions.Count(d => d.RoomType == RoomType.Combat && d.SizeClass == RoomSizeClass.Medium));
            Assert.AreEqual(2, definitions.Count(d => d.RoomType == RoomType.Combat && d.SizeClass == RoomSizeClass.Large));
            Assert.IsTrue(definitions.Where(d => d.SupportsElite).All(d => d.RoomType == RoomType.Combat && d.SizeClass != RoomSizeClass.Small), "Elite-capable rooms are medium/large combat rooms.");
            Assert.GreaterOrEqual(definitions.Count(d => d.SupportsElite), 2, "Elite encounters need compatible combat rooms.");
            Assert.IsTrue(definitions.All(d => d.Biome == Biome.OvergrownLabs), "Every prefab is tagged Overgrown Labs.");
        }

        [Test]
        public void EveryBakedRoom_MatchesItsSpecMetadata_AndUsesItsSizeClassTarget()
        {
            foreach (var spec in OvergrownLabsRoomSet.All)
            {
                var definition = LabsDefinitions().Single(d => d.Id == spec.Id);
                Assert.AreEqual(spec.RoomType, definition.RoomType, spec.Id);
                Assert.AreEqual(spec.SizeClass, definition.SizeClass, spec.Id);
                Assert.AreEqual(RoomSizeClasses.DimensionsOf(spec.SizeClass), definition.Dimensions, spec.Id);
                Assert.AreEqual(spec.Difficulty, definition.Difficulty, spec.Id);
                Assert.AreEqual(spec.SupportsElite, definition.SupportsElite, spec.Id);
                CollectionAssert.AreEquivalent(spec.SupportedDoors().ToList(), definition.SupportedDoors.ToList(), spec.Id);
                CollectionAssert.AreEquivalent(spec.Tags, definition.Tags, spec.Id);
                Assert.IsTrue(definition.HasTag("labs"), spec.Id);
                Assert.IsNotNull(definition.Prefab, spec.Id);
                Assert.AreSame(definition, definition.Prefab.GetComponent<RoomRoot>().Definition, $"{spec.Id}: prefab points back to its definition.");
            }
        }

        [Test]
        public void BakedPrefabs_CarryStaticTilemapGeometry_Sockets_Markers_AndLabsHazards()
        {
            var furnace = AssetDatabase.LoadAssetAtPath<HazardDefinition>(OvergrownLabsRoomSet.HazardPath);
            Assert.IsNotNull(furnace, "Labs hazard definition exists.");
            Assert.AreEqual("hazard_acid_pool", furnace.Id);
            var hazardRooms = 0;
            foreach (var definition in LabsDefinitions())
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
                    Assert.IsTrue(definition.Tags.Any(t => t == "boss:subject_omega" || t == "boss:aegis_core"), $"{definition.Id}: arena pins one of the two Labs bosses (46).");
                }
                else if (definition.RoomType == RoomType.MedicalRecovery)
                {
                    Assert.AreEqual(1, markers.Count(m => m.Role == RoomMarkerRole.EventAnchor), definition.Id);
                    Assert.IsFalse(markers.Any(m => m.Role == RoomMarkerRole.EnemySpawn), definition.Id);
                }

                foreach (var hazard in markers.Where(m => m.Role == RoomMarkerRole.Hazard))
                {
                    hazardRooms++;
                    Assert.AreSame(furnace, hazard.GetComponent<RoomHazard>()?.Definition, $"{definition.Id}: Labs hazards are acid pools, never Metro rails.");
                }
            }

            Assert.Greater(hazardRooms, 0, "The biome identity includes acid hazards in combat rooms.");
        }

        [Test]
        public void RoomValidator_PassesEveryLabsRoom()
        {
            var reports = RoomValidator.ValidateAll(LabsDefinitions());
            Assert.AreEqual(OvergrownLabsRoomSet.All.Count, reports.Count);
            foreach (var report in reports)
            {
                Assert.IsTrue(report.IsGeneratorReady, $"{report.RoomId}:\n" + string.Join("\n", report.Problems));
            }
        }

        [Test]
        public void Layouts_EncodeTwoWideCardinalSockets_AndOnlyLegalLegendCharacters()
        {
            foreach (var spec in OvergrownLabsRoomSet.All)
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

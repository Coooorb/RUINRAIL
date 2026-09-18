using System.Linq;
using NUnit.Framework;
using RuinRail.Dungeon.Grid;
using RuinRail.Gameplay.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuinRail.Tests
{
    public class GridFoundationTests
    {
        private const string FixturePrefabPath = "Assets/Game/Prefabs/Rooms/_Test/GridTestRoom.prefab";
        private const string PlaceholderFolder = "Assets/Game/Art/Tiles/_Placeholder";

        [Test]
        public void GridConstants_MatchApprovedConventions()
        {
            Assert.AreEqual(32, GridConstants.TileSizePixels);
            Assert.AreEqual(32, GridConstants.PixelsPerUnit);
            Assert.AreEqual(1f, GridConstants.TileWorldSize, 0.0001f, "One 32px tile at PPU 32 is exactly one world unit.");
            Assert.AreEqual(640, GridConstants.ReferenceResolutionWidth);
            Assert.AreEqual(360, GridConstants.ReferenceResolutionHeight);
        }

        [Test]
        public void CellToWorldCenter_AndBack_IsReversible_AndFloorsCorrectly()
        {
            for (var x = -3; x <= 40; x++)
            {
                for (var y = -3; y <= 30; y++)
                {
                    var cell = new Vector2Int(x, y);
                    var center = GridCoordinates.CellToWorldCenter(cell);
                    Assert.AreEqual(x + 0.5f, center.x, 0.0001f);
                    Assert.AreEqual(y + 0.5f, center.y, 0.0001f);
                    Assert.AreEqual(cell, GridCoordinates.WorldToCell(center));
                    Assert.AreEqual(cell, GridCoordinates.WorldToCell(GridCoordinates.CellToWorldMin(cell)), "Min corner belongs to the cell.");
                    Assert.AreEqual(cell, GridCoordinates.WorldToCell(center + new Vector2(0.499f, -0.499f)), "Anywhere inside the tile maps back.");
                    Assert.AreEqual(GridCoordinates.ToTilemapCell(cell), new Vector3Int(x, y, 0));
                    Assert.AreEqual(cell, GridCoordinates.FromTilemapCell(GridCoordinates.ToTilemapCell(cell)));
                }
            }

            Assert.AreEqual(new Vector2Int(-1, -1), GridCoordinates.WorldToCell(new Vector2(-0.001f, -0.001f)));
            Assert.AreEqual(new Vector2(1f, 1.5f), GridCoordinates.PixelsToWorld(new Vector2Int(32, 48)));
        }

        [Test]
        public void IsInsideBounds_UsesRoomSizeClasses()
        {
            var small = new Vector2Int(16, 12);
            Assert.IsTrue(GridCoordinates.IsInsideBounds(new Vector2Int(0, 0), small));
            Assert.IsTrue(GridCoordinates.IsInsideBounds(new Vector2Int(15, 11), small));
            Assert.IsFalse(GridCoordinates.IsInsideBounds(new Vector2Int(16, 11), small));
            Assert.IsFalse(GridCoordinates.IsInsideBounds(new Vector2Int(-1, 0), small));
        }

        [Test]
        public void RoomLayers_AreExactlyTheSevenApprovedLayers_WithStableNames()
        {
            CollectionAssert.AreEqual(
                new[] { "Floor", "FloorDetail", "Walls", "Obstacles", "Hazards", "AbovePlayer", "Logic" },
                RoomTilemapLayers.All.Select(RoomTilemapLayers.NameOf));
            Assert.IsTrue(RoomTilemapLayers.TryParse("Walls", out var walls) && walls == RoomTilemapLayer.Walls);
            Assert.IsFalse(RoomTilemapLayers.TryParse("Wall", out _));
            Assert.IsTrue(RoomTilemapLayers.BlocksMovement(RoomTilemapLayer.Walls));
            Assert.IsTrue(RoomTilemapLayers.BlocksMovement(RoomTilemapLayer.Obstacles));
            Assert.IsFalse(RoomTilemapLayers.BlocksMovement(RoomTilemapLayer.Floor));
            Assert.IsTrue(RoomTilemapLayers.IsTrigger(RoomTilemapLayer.Hazards));
            Assert.IsFalse(RoomTilemapLayers.IsRendered(RoomTilemapLayer.Logic));
            // art/102 (TASK 137): layers render on the approved sorting layers; rank compares across them.
            Assert.Less(RoomTilemapLayers.GlobalRank(RoomTilemapLayer.Floor), RoomTilemapLayers.GlobalRank(RoomTilemapLayer.Walls));
            Assert.Greater(RoomTilemapLayers.GlobalRank(RoomTilemapLayer.AbovePlayer), RoomTilemapLayers.GlobalRank(RoomTilemapLayer.Hazards));
            Assert.AreEqual("Ground", RoomTilemapLayers.SortingLayerNameOf(RoomTilemapLayer.Floor));
            Assert.AreEqual("AboveCharacters", RoomTilemapLayers.SortingLayerNameOf(RoomTilemapLayer.AbovePlayer));
        }

        [Test]
        public void RoomGridBuilder_ProducesAValidRoomGrid_AndValidatorReportsNoProblems()
        {
            var grid = RoomGridBuilder.CreateRoomGrid("BuiltRoom");
            try
            {
                Assert.IsEmpty(RoomTilemapValidator.Validate(grid));
                Assert.AreEqual(Vector3.one - Vector3.forward, grid.cellSize);

                var walls = grid.transform.Find("Walls");
                Assert.IsNotNull(walls.GetComponent<TilemapCollider2D>());
                Assert.IsFalse(walls.GetComponent<TilemapCollider2D>().isTrigger);
                Assert.IsNotNull(walls.GetComponent<CompositeCollider2D>());
                Assert.IsNotNull(walls.GetComponent<EnvironmentObstacle>());
                Assert.IsNotNull(grid.transform.Find("Obstacles").GetComponent<EnvironmentObstacle>());
                Assert.IsTrue(grid.transform.Find("Hazards").GetComponent<TilemapCollider2D>().isTrigger);
                Assert.IsNull(grid.transform.Find("Floor").GetComponent<TilemapCollider2D>());
                Assert.IsFalse(grid.transform.Find("Logic").GetComponent<TilemapRenderer>().enabled);
            }
            finally
            {
                Object.DestroyImmediate(grid.gameObject);
            }
        }

        [Test]
        public void Validator_ReportsMissingLayer_WrongCellSize_AndMissingCollision()
        {
            var grid = RoomGridBuilder.CreateRoomGrid("BrokenRoom");
            try
            {
                Object.DestroyImmediate(grid.transform.Find("Hazards").gameObject);
                grid.cellSize = new Vector3(2f, 2f, 0f);
                Object.DestroyImmediate(grid.transform.Find("Obstacles").GetComponent<EnvironmentObstacle>());
                grid.transform.Find("Floor").gameObject.AddComponent<TilemapCollider2D>();

                var problems = RoomTilemapValidator.Validate(grid);

                Assert.IsTrue(problems.Any(p => p.Contains("Missing Tilemap layer 'Hazards'")), string.Join("\n", problems));
                Assert.IsTrue(problems.Any(p => p.Contains("cell size")), string.Join("\n", problems));
                Assert.IsTrue(problems.Any(p => p.Contains("'Obstacles' needs EnvironmentObstacle")), string.Join("\n", problems));
                Assert.IsTrue(problems.Any(p => p.Contains("'Floor' must not carry solid collision")), string.Join("\n", problems));
            }
            finally
            {
                Object.DestroyImmediate(grid.gameObject);
            }
        }

        [Test]
        public void GridTestRoomFixture_IsValid_SmallClass_WithWallRingObstacleAndHazard()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FixturePrefabPath);
            Assert.IsNotNull(prefab, $"Expected fixture prefab at {FixturePrefabPath}.");
            var grid = prefab.GetComponent<Grid>();
            Assert.IsEmpty(RoomTilemapValidator.Validate(grid));

            var floor = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Floor);
            var walls = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Walls);
            var obstacles = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Obstacles);
            var hazards = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Hazards);

            var floorCount = 0;
            var wallCount = 0;
            for (var x = 0; x < 16; x++)
            {
                for (var y = 0; y < 12; y++)
                {
                    var cell = new Vector3Int(x, y, 0);
                    if (floor.HasTile(cell)) floorCount++;
                    var isBorder = x == 0 || y == 0 || x == 15 || y == 11;
                    var isDoorGap = (y == 11 && (x == 7 || x == 8)) || (x == 15 && (y == 5 || y == 6));
                    Assert.AreEqual(isBorder && !isDoorGap, walls.HasTile(cell), $"Wall ring mismatch at {cell}.");
                    if (walls.HasTile(cell)) wallCount++;
                }
            }

            Assert.AreEqual(16 * 12, floorCount, "Small room class is 16x12 tiles, fully floored.");
            Assert.AreEqual(2 * 16 + 2 * 10 - 4, wallCount, "Wall ring minus the two 2-tile door gaps.");
            Assert.IsFalse(floor.HasTile(new Vector3Int(16, 0, 0)), "Nothing outside the declared bounds.");
            Assert.IsTrue(obstacles.HasTile(new Vector3Int(6, 5, 0)) && obstacles.HasTile(new Vector3Int(7, 6, 0)));
            Assert.IsTrue(hazards.HasTile(new Vector3Int(10, 3, 0)) && hazards.HasTile(new Vector3Int(12, 3, 0)));
            Assert.IsFalse(obstacles.HasTile(new Vector3Int(10, 3, 0)), "Hazard cells are not obstacles.");
            Assert.AreEqual(Vector3.one, prefab.transform.localScale);
        }

        [Test]
        public void PlaceholderTiles_ImportAt32PixelsPerUnit_PointFiltered()
        {
            foreach (var name in new[] { "Placeholder_Floor", "Placeholder_Wall", "Placeholder_Obstacle", "Placeholder_Hazard" })
            {
                var importer = AssetImporter.GetAtPath($"{PlaceholderFolder}/{name}.png") as TextureImporter;
                Assert.IsNotNull(importer, name);
                Assert.AreEqual(GridConstants.PixelsPerUnit, importer.spritePixelsPerUnit, 0.001f, name);
                Assert.AreEqual(FilterMode.Point, importer.filterMode, name);
                Assert.AreEqual(TextureImporterType.Sprite, importer.textureType, name);

                var tile = AssetDatabase.LoadAssetAtPath<Tile>($"{PlaceholderFolder}/{name}.asset");
                Assert.IsNotNull(tile, name);
                Assert.IsNotNull(tile.sprite, name);
                Assert.AreEqual(32f, tile.sprite.rect.width, name);
            }

            Assert.AreEqual(Tile.ColliderType.None, AssetDatabase.LoadAssetAtPath<Tile>($"{PlaceholderFolder}/Placeholder_Floor.asset").colliderType);
            Assert.AreEqual(Tile.ColliderType.Grid, AssetDatabase.LoadAssetAtPath<Tile>($"{PlaceholderFolder}/Placeholder_Wall.asset").colliderType);
        }
    }
}

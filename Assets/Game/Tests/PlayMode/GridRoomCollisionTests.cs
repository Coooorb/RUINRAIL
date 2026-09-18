using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RuinRail.Dungeon.Grid;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Player;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace RuinRail.Tests
{
    /// <summary>
    /// Proves the room layer contract yields the intended gameplay behaviour on a runtime-built grid:
    /// Walls/Obstacles block the player and terminate projectiles, Hazards are pass-through triggers.
    /// </summary>
    public class GridRoomCollisionTests
    {
        private readonly List<Object> _created = new();
        private Grid _grid;
        private Tile _solidTile;
        private Tile _floorTile;

        [SetUp]
        public void SetUp()
        {
            _solidTile = CreateTile(Tile.ColliderType.Grid);
            _floorTile = CreateTile(Tile.ColliderType.None);
            _grid = RoomGridBuilder.CreateRoomGrid("RuntimeRoom");
            _created.Add(_grid.gameObject);

            var floor = RoomGridBuilder.FindLayer(_grid, RoomTilemapLayer.Floor);
            for (var x = 0; x < 12; x++)
            {
                for (var y = 0; y < 8; y++)
                {
                    floor.SetTile(new Vector3Int(x, y, 0), _floorTile);
                }
            }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private Tile CreateTile(Tile.ColliderType colliderType)
        {
            var texture = new Texture2D(GridConstants.TileSizePixels, GridConstants.TileSizePixels);
            _created.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), GridConstants.PixelsPerUnit);
            _created.Add(sprite);
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            tile.colliderType = colliderType;
            _created.Add(tile);
            return tile;
        }

        private void Paint(RoomTilemapLayer layer, Tile tile, params Vector2Int[] cells)
        {
            var map = RoomGridBuilder.FindLayer(_grid, layer);
            foreach (var cell in cells)
            {
                map.SetTile(GridCoordinates.ToTilemapCell(cell), tile);
            }
        }

        private (GameObject go, Rigidbody2D rb, FakePlayerInputReader input) CreatePlayer(Vector2Int cell)
        {
            var go = new GameObject("Player");
            _created.Add(go);
            go.transform.position = GridCoordinates.CellToWorldCenter(cell);
            var rb = go.AddComponent<Rigidbody2D>();
            var collider = go.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(0.8f, 0.8f);
            var movement = go.AddComponent<PlayerMovement>();
            var input = new FakePlayerInputReader();
            var config = ScriptableObject.CreateInstance<PlayerBalanceConfig>();
            _created.Add(config);
            movement.SetInputReader(input);
            movement.SetBalanceConfig(config);
            return (go, rb, input);
        }

        [UnityTest]
        public IEnumerator WallCells_BlockPlayerMovement()
        {
            Paint(RoomTilemapLayer.Walls, _solidTile, new Vector2Int(5, 2), new Vector2Int(5, 3), new Vector2Int(5, 4));
            yield return new WaitForFixedUpdate();

            var (player, _, input) = CreatePlayer(new Vector2Int(2, 3));
            input.Move = Vector2.right;
            yield return new WaitForSeconds(1.5f);

            var x = player.transform.position.x;
            Assert.Less(x, 5f, "Player must not enter the wall column (cell x=5 spans world 5..6).");
            Assert.Greater(x, 3.5f, "Player should reach the wall face, not stop early.");
        }

        [UnityTest]
        public IEnumerator ObstacleCells_BlockPlayerMovement_TooAndHazardsDoNot()
        {
            Paint(RoomTilemapLayer.Obstacles, _solidTile, new Vector2Int(6, 5));
            Paint(RoomTilemapLayer.Hazards, _solidTile, new Vector2Int(3, 5), new Vector2Int(4, 5));
            yield return new WaitForFixedUpdate();

            var (player, _, input) = CreatePlayer(new Vector2Int(1, 5));
            input.Move = Vector2.right;
            yield return new WaitForSeconds(1.6f);

            var x = player.transform.position.x;
            Assert.Greater(x, 5.0f, "Hazard cells (3,5)-(4,5) must be walkable.");
            Assert.Less(x, 6f, "Obstacle cell (6,5) must block.");
        }

        [UnityTest]
        public IEnumerator Projectile_TerminatesOnWallCell_WithoutReachingTargetBehind()
        {
            Paint(RoomTilemapLayer.Walls, _solidTile, new Vector2Int(6, 4));
            yield return new WaitForFixedUpdate();

            var target = new GameObject("Target");
            _created.Add(target);
            target.transform.position = GridCoordinates.CellToWorldCenter(new Vector2Int(9, 4));
            target.AddComponent<BoxCollider2D>().size = Vector2.one;
            var damageable = target.AddComponent<TestDamageableTarget>();

            var poolObject = new GameObject("Pool");
            _created.Add(poolObject);
            var pool = poolObject.AddComponent<ProjectilePool>();
            var projectile = pool.Spawn(
                GridCoordinates.CellToWorldCenter(new Vector2Int(2, 4)),
                new ProjectileSpawnData(5, 10f, 20f, 0f, 0f, Vector2.right, poolObject));
            yield return new WaitForSeconds(1.2f);

            Assert.IsTrue(projectile.IsResolved || !projectile.gameObject.activeSelf, "Projectile terminated on the wall tile.");
            Assert.AreEqual(0, damageable.HitCount, "Target behind the wall is never hit.");
        }

        [UnityTest]
        public IEnumerator Projectile_PassesThroughHazardCells_AndHitsTarget()
        {
            Paint(RoomTilemapLayer.Hazards, _solidTile, new Vector2Int(5, 4), new Vector2Int(6, 4));
            yield return new WaitForFixedUpdate();

            var target = new GameObject("Target");
            _created.Add(target);
            target.transform.position = GridCoordinates.CellToWorldCenter(new Vector2Int(9, 4));
            target.AddComponent<BoxCollider2D>().size = Vector2.one;
            var damageable = target.AddComponent<TestDamageableTarget>();

            var poolObject = new GameObject("Pool");
            _created.Add(poolObject);
            var pool = poolObject.AddComponent<ProjectilePool>();
            pool.Spawn(
                GridCoordinates.CellToWorldCenter(new Vector2Int(2, 4)),
                new ProjectileSpawnData(5, 10f, 20f, 0f, 0f, Vector2.right, poolObject));
            yield return new WaitForSeconds(1.2f);

            Assert.AreEqual(1, damageable.HitCount, "Hazard tiles never stop projectiles.");
        }

        [Test]
        public void RuntimeGrid_PassesValidator_AndTileCellsLandOnWorldUnits()
        {
            Assert.IsEmpty(RoomTilemapValidator.Validate(_grid));
            var walls = RoomGridBuilder.FindLayer(_grid, RoomTilemapLayer.Walls);
            var worldCenter = walls.GetCellCenterWorld(new Vector3Int(4, 2, 0));
            Assert.AreEqual(GridCoordinates.CellToWorldCenter(new Vector2Int(4, 2)), (Vector2)worldCenter);
            Assert.AreEqual(new Vector3Int(4, 2, 0), walls.WorldToCell(GridCoordinates.CellToWorldCenter(new Vector2Int(4, 2))));
        }
    }
}

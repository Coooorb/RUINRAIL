using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>
    /// The room activation volume sits inside the room proper: past the wall ring and every door cell, with an explicit
    /// interior margin, and never overlapping a neighbouring room — for all 63 shipped rooms in every orientation and
    /// for assembled dungeons across the three biomes.
    /// </summary>
    public sealed class RoomEntryGeometryTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void InteriorVolume_IsInsetByTheMargin_AndExcludesEveryDoorCell_ForAll63Rooms()
        {
            var catalog = GameContentCatalog.Load();
            Assert.AreEqual(63, catalog.Rooms.Count);
            foreach (var room in catalog.Rooms)
            {
                var root = room.Prefab.GetComponent<RoomRoot>();
                var volume = RoomEntryTrigger.InteriorVolume(root.Size);
                var bounds = new Rect(0f, 0f, root.Size.x * GridConstants.TileWorldSize, root.Size.y * GridConstants.TileWorldSize);
                Assert.GreaterOrEqual(volume.xMin, bounds.xMin + RoomEntryTrigger.InteriorMarginTiles - 1e-4f, room.Id);
                Assert.GreaterOrEqual(volume.yMin, bounds.yMin + RoomEntryTrigger.InteriorMarginTiles - 1e-4f, room.Id);
                Assert.LessOrEqual(volume.xMax, bounds.xMax - RoomEntryTrigger.InteriorMarginTiles + 1e-4f, room.Id);
                Assert.LessOrEqual(volume.yMax, bounds.yMax - RoomEntryTrigger.InteriorMarginTiles + 1e-4f, room.Id);
                Assert.Greater(volume.width, 4f, $"{room.Id}: a usable interior remains");
                Assert.Greater(volume.height, 4f, $"{room.Id}: a usable interior remains");

                foreach (var socket in root.GetSockets())
                foreach (var cell in socket.Cells())
                {
                    var cellRect = new Rect(cell.x, cell.y, 1f, 1f);
                    Assert.IsFalse(volume.Overlaps(cellRect), $"{room.Id}: {socket.Direction} door cell {cell} lies outside the activation volume");
                    // A player standing on the door cell (collider radius 0.4) does not reach the volume either.
                    var centre = GridCoordinates.CellToWorldCenter(cell);
                    var reach = new Rect(centre.x - 0.4f, centre.y - 0.4f, 0.8f, 0.8f);
                    Assert.IsFalse(volume.Overlaps(reach), $"{room.Id}: a player in the {socket.Direction} doorway does not touch the volume");
                }
            }
        }

        [Test]
        public void ActivationVolumes_NeverOverlapANeighbouringRoom_AcrossBiomesAndSeeds()
        {
            var catalog = GameContentCatalog.Load();
            var pools = BiomeRoomPools.Build(catalog.Rooms);
            var rules = DungeonGraphRules.CreateDefault();
            _created.Add(rules);
            var generator = new DungeonGraphGenerator(rules);
            var checkedRooms = 0;
            foreach (var biome in new[] { Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs })
            foreach (var seed in Enumerable.Range(1, 8))
            {
                var generation = DungeonGenerationPipeline.Generate(generator, pools.PoolFor(biome), seed, 1 + seed % 6);
                Assert.IsTrue(generation.Success, generation.Error);
                var placements = generation.Layout.Placements.ToList();
                foreach (var placement in placements)
                {
                    var local = RoomEntryTrigger.InteriorVolume(placement.Definition.Dimensions);
                    var world = new Rect(local.position + (Vector2)placement.Offset * GridConstants.TileWorldSize, local.size);
                    foreach (var other in placements)
                    {
                        if (other == placement) continue;
                        var otherBounds = new Rect((Vector2)other.Bounds.min * GridConstants.TileWorldSize, (Vector2)other.Bounds.size * GridConstants.TileWorldSize);
                        Assert.IsFalse(world.Overlaps(otherBounds), $"{biome} seed {seed}: room {placement.NodeId}'s activation volume reaches into room {other.NodeId}");
                    }

                    checkedRooms++;
                }
            }

            Assert.Greater(checkedRooms, 100);
        }
    }
}

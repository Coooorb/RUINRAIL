using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.UI.Hud;
using RuinRail.UI.Theme;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>
    /// The room-title reveal source data and the minimap's discovery/placement rules, as deterministic logic: the
    /// names every shipped room announces, the reveal envelope, what a player is allowed to see on the map and where
    /// the cells land. The live behaviour (one reveal per entry, the map following the same entry event) is proven in
    /// the PlayMode suite against a real run.
    /// </summary>
    public sealed class RoomTitleMinimapTests
    {
        private static List<RoomDefinition> ShippedRooms() =>
            AssetDatabase.FindAssets("t:RoomDefinition")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !p.Contains("/_Test/"))
                .Select(AssetDatabase.LoadAssetAtPath<RoomDefinition>)
                .Where(d => d != null)
                .ToList();

        // ---- Room names ----

        [Test]
        public void EveryShippedRoom_HasAPlayerFacingName_ThatIsNeverAnInternalId()
        {
            var rooms = ShippedRooms();
            Assert.GreaterOrEqual(rooms.Count, 63, "every authored room of the three biomes is covered");
            foreach (var room in rooms)
            {
                var name = RoomDisplayNames.NameOf(room);
                Assert.IsNotEmpty(name, room.Id);
                Assert.IsFalse(name.Contains("_"), $"{room.Id}: '{name}' reads as an internal id");
                Assert.AreNotEqual(room.Id, name, room.Id);
                Assert.AreNotEqual(RoomDisplayNames.FallbackName(room.RoomType), name, $"{room.Id} falls back instead of carrying its own name");
                Assert.LessOrEqual(name.Length, 24, $"{room.Id}: '{name}' is short enough for the reveal band");
                CollectionAssert.Contains(RoomDisplayNames.KnownIds, room.Id);
            }

            var duplicates = rooms.Select(RoomDisplayNames.NameOf).GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            CollectionAssert.IsEmpty(duplicates, "every room reads as its own place");
        }

        [Test]
        public void SpecialRooms_AnnounceTheirRole_AndOrdinaryCombatRoomsDoNot()
        {
            Assert.AreEqual("MERCHANT", RoomDisplayNames.RoleOf(RoomType.Merchant));
            Assert.AreEqual("TREASURE", RoomDisplayNames.RoleOf(RoomType.Treasure));
            Assert.AreEqual("BOSS", RoomDisplayNames.RoleOf(RoomType.Boss));
            Assert.AreEqual("MEDICAL", RoomDisplayNames.RoleOf(RoomType.MedicalRecovery));
            Assert.AreEqual("SUPPLY CACHE", RoomDisplayNames.RoleOf(RoomType.Loot));
            Assert.AreEqual(string.Empty, RoomDisplayNames.RoleOf(RoomType.Combat), "an ordinary fight never gets a second banner line");
            // Names follow the biome the room belongs to.
            Assert.AreEqual("Collapsed Platform", RoomDisplayNames.NameOf("metro_combat_medium_01", RoomType.Combat));
            Assert.AreEqual("Smelter Floor", RoomDisplayNames.NameOf("rust_combat_medium_01", RoomType.Combat));
            Assert.AreEqual("Specimen Wing", RoomDisplayNames.NameOf("labs_combat_medium_01", RoomType.Combat));
            // An unknown id still announces something readable.
            Assert.AreEqual("Combat Zone", RoomDisplayNames.NameOf("not_a_room", RoomType.Combat));
        }

        [Test]
        public void TheRevealEnvelope_FadesInHoldsAndFadesOut_InsideTheApprovedWindow()
        {
            Assert.GreaterOrEqual(HudRoomTitleView.TotalSeconds, 1.5f);
            Assert.LessOrEqual(HudRoomTitleView.TotalSeconds, 2.5f);
            Assert.AreEqual(0f, HudRoomTitleView.EnvelopeAt(0f), 0.001f, "starts invisible");
            Assert.AreEqual(1f, HudRoomTitleView.EnvelopeAt(HudRoomTitleView.FadeInSeconds), 0.001f, "fully in after the fade-in");
            Assert.AreEqual(1f, HudRoomTitleView.EnvelopeAt(HudRoomTitleView.FadeInSeconds + HudRoomTitleView.HoldSeconds * 0.5f), 0.001f, "held");
            Assert.AreEqual(0f, HudRoomTitleView.EnvelopeAt(HudRoomTitleView.TotalSeconds), 0.001f, "gone at the end");
            Assert.AreEqual(0f, HudRoomTitleView.EnvelopeAt(HudRoomTitleView.TotalSeconds + 5f), 0.001f, "and stays gone");
            var mid = HudRoomTitleView.EnvelopeAt(HudRoomTitleView.FadeInSeconds + HudRoomTitleView.HoldSeconds + HudRoomTitleView.FadeOutSeconds * 0.5f);
            Assert.That(mid, Is.GreaterThan(0.2f).And.LessThan(0.8f), "fades rather than cutting");
        }

        // ---- Minimap discovery ----

        private static MinimapModel Chain(int rooms, out List<int> ids)
        {
            var model = new MinimapModel();
            model.BeginDepth(1, "RUINED METRO");
            ids = new List<int>();
            for (var i = 0; i < rooms; i++)
            {
                model.AddRoom(i, new Vector2(i * 20f, 0f), i == 0 ? MinimapRoomKind.Start : i == rooms - 1 ? MinimapRoomKind.Boss : MinimapRoomKind.Normal);
                ids.Add(i);
            }

            for (var i = 1; i < rooms; i++) model.AddLink(i - 1, i);
            return model;
        }

        [Test]
        public void ANewDepth_RevealsNothing_UntilTheFirstRoomIsEntered()
        {
            var model = Chain(6, out _);
            CollectionAssert.IsEmpty(model.DiscoveredRooms, "the generated dungeon is never shown up front");
            Assert.IsNull(model.CurrentNodeId);
            Assert.IsTrue(model.MarkEntered(0));
            Assert.AreEqual(0, model.CurrentNodeId);
            Assert.IsTrue(model.Room(0).Visited);
            Assert.IsTrue(model.Room(1).Discovered, "a direct neighbour becomes a discovered outline");
            Assert.IsFalse(model.Room(1).Visited);
            Assert.IsFalse(model.Room(2).Discovered, "two steps away stays hidden");
            Assert.AreEqual(2, model.DiscoveredRooms.Count());
        }

        [Test]
        public void EnteringRooms_MovesTheCurrentMarker_AndKeepsWhatWasAlreadySeen()
        {
            var model = Chain(6, out _);
            model.MarkEntered(0);
            model.MarkEntered(1);
            Assert.AreEqual(1, model.CurrentNodeId);
            Assert.IsTrue(model.Room(0).Visited, "the room left behind stays on the map");
            Assert.IsTrue(model.Room(2).Discovered);
            Assert.IsFalse(model.Room(3).Discovered);
            // Returning to an already visited room only moves the marker.
            var before = model.DiscoveredRooms.Count();
            model.MarkEntered(0);
            Assert.AreEqual(0, model.CurrentNodeId);
            Assert.AreEqual(before, model.DiscoveredRooms.Count(), "a revisit reveals nothing new");
            Assert.AreEqual(2, model.Entries, "two distinct rooms have been visited");
        }

        [Test]
        public void OnlyLinksBetweenDiscoveredRooms_AreDrawn_AndTheyAreRealDoors()
        {
            var model = Chain(4, out _);
            model.MarkEntered(0);
            var links = model.DiscoveredLinks.ToList();
            CollectionAssert.AreEquivalent(new[] { (0, 1) }, links, "only the door the player can see");
            model.MarkEntered(1);
            Assert.AreEqual(2, model.DiscoveredLinks.Count());
            Assert.IsTrue(model.DiscoveredLinks.All(l => model.Room(l.a).Discovered && model.Room(l.b).Discovered));
            model.AddLink(0, 99); // a link to a room that does not exist is refused outright
            Assert.AreEqual(3, model.Links.Count);
        }

        [Test]
        public void ASpecialRoomsSymbol_AppearsOnlyAfterThePlayerHasBeenInside()
        {
            var model = new MinimapModel();
            model.BeginDepth(2, "RUSTWORKS");
            model.AddRoom(0, Vector2.zero, MinimapRoomKind.Start);
            model.AddRoom(1, new Vector2(24f, 0f), MinimapRoomKind.Merchant);
            model.AddLink(0, 1);
            model.MarkEntered(0);
            Assert.IsTrue(model.Room(1).Discovered, "the room outline is visible");
            Assert.IsFalse(model.Room(1).ShowsMarker, "but what it holds is not spoiled");
            model.MarkEntered(1);
            Assert.IsTrue(model.Room(1).ShowsMarker);
            // An Event room reveals which event it actually was once entered.
            model.AddRoom(2, new Vector2(48f, 0f), MinimapRoomKind.Event);
            model.AddLink(1, 2);
            model.MarkEntered(2);
            model.SetKind(2, MinimapRoomKind.WeaponCache);
            Assert.AreEqual(MinimapRoomKind.WeaponCache, model.Room(2).Kind);
            Assert.IsTrue(model.Room(2).ShowsMarker);
            // An ordinary room never carries a symbol.
            model.AddRoom(3, new Vector2(72f, 0f), MinimapRoomKind.Normal);
            model.AddLink(2, 3);
            model.MarkEntered(3);
            Assert.IsFalse(model.Room(3).ShowsMarker);
        }

        [Test]
        public void EveryMarkerKind_HasItsOwnCompactSymbol()
        {
            var kinds = new[]
            {
                MinimapRoomKind.Start, MinimapRoomKind.Loot, MinimapRoomKind.Treasure, MinimapRoomKind.Merchant,
                MinimapRoomKind.Event, MinimapRoomKind.WeaponCache, MinimapRoomKind.Medical, MinimapRoomKind.Boss,
                MinimapRoomKind.Transit
            };
            var shapes = new List<string>();
            foreach (var kind in kinds)
            {
                var parts = MinimapSymbols.For(kind);
                Assert.IsNotEmpty(parts, kind.ToString());
                Assert.LessOrEqual(parts.Count, 3, kind + ": the panel draws at most three parts per cell");
                foreach (var (rect, _) in parts)
                {
                    Assert.GreaterOrEqual(rect.X, 0);
                    Assert.GreaterOrEqual(rect.Y, 0);
                    Assert.LessOrEqual(rect.Right, 5, kind + ": inside the 5x5 symbol box");
                    Assert.LessOrEqual(rect.Bottom, 5, kind + ": inside the 5x5 symbol box");
                }

                shapes.Add(string.Join(";", parts.Select(p => $"{p.rect.X},{p.rect.Y},{p.rect.Width},{p.rect.Height}")));
            }

            Assert.AreEqual(shapes.Count, shapes.Distinct().Count(), "every symbol differs in shape, not only in colour (18.4)");
            CollectionAssert.IsEmpty(MinimapSymbols.For(MinimapRoomKind.Normal), "an ordinary room has no symbol");
            CollectionAssert.IsEmpty(MinimapSymbols.For(MinimapRoomKind.Combat));
        }

        // ---- Minimap placement ----

        [Test]
        public void CellsFitThePanel_AreCentredOnTheDiscoveredGraph_AndKeepLayoutOrientation()
        {
            var panel = new Vector2(HudMinimapView.Width - HudMinimapView.Inset * 2, HudMinimapView.Height - HudMinimapView.Inset * 2);
            var model = new MinimapModel();
            model.BeginDepth(1, "RUINED METRO");
            model.AddRoom(0, new Vector2(0f, 0f), MinimapRoomKind.Start);
            model.AddRoom(1, new Vector2(24f, 0f), MinimapRoomKind.Normal);   // east of the start
            model.AddRoom(2, new Vector2(24f, 18f), MinimapRoomKind.Boss);    // north of that
            model.AddLink(0, 1);
            model.AddLink(1, 2);
            model.MarkEntered(0);
            model.MarkEntered(1);
            model.MarkEntered(2);

            var rooms = MinimapLayout.VisibleRooms(model, panel);
            Assert.AreEqual(3, rooms.Count);
            var cells = MinimapLayout.Place(rooms, panel);
            Assert.AreEqual(3, cells.Count);
            foreach (var cell in cells)
            {
                Assert.GreaterOrEqual(cell.Center.x - cell.Size.x / 2f, -0.01f, "inside the panel");
                Assert.GreaterOrEqual(cell.Center.y - cell.Size.y / 2f, -0.01f);
                Assert.LessOrEqual(cell.Center.x + cell.Size.x / 2f, panel.x + 0.01f);
                Assert.LessOrEqual(cell.Center.y + cell.Size.y / 2f, panel.y + 0.01f);
                Assert.AreEqual(Mathf.Round(cell.Center.x), cell.Center.x, "cells land on whole pixels");
                Assert.AreEqual(Mathf.Round(cell.Center.y), cell.Center.y);
            }

            var start = cells.Single(c => c.NodeId == 0);
            var east = cells.Single(c => c.NodeId == 1);
            var north = cells.Single(c => c.NodeId == 2);
            Assert.Greater(east.Center.x, start.Center.x, "east in the layout is right on the map");
            Assert.Less(north.Center.y, east.Center.y, "north in the layout is up on the map (panel y grows downward)");
        }

        [Test]
        public void ASprawlingGraph_FallsBackToTheNeighbourhood_RatherThanToUnreadableCells()
        {
            var panel = new Vector2(HudMinimapView.Width - HudMinimapView.Inset * 2, HudMinimapView.Height - HudMinimapView.Inset * 2);
            var model = new MinimapModel();
            model.BeginDepth(3, "OVERGROWN LABS");
            const int count = 12;
            for (var i = 0; i < count; i++) model.AddRoom(i, new Vector2(i * 160f, 0f), MinimapRoomKind.Normal);
            for (var i = 1; i < count; i++) model.AddLink(i - 1, i);
            for (var i = 0; i < count; i++) model.MarkEntered(i);
            model.MarkEntered(6);

            var all = model.DiscoveredRooms.ToList();
            Assert.Less(MinimapLayout.ScaleFor(all, panel), MinimapLayout.MinScale, "the whole graph would be unreadable");
            var visible = MinimapLayout.VisibleRooms(model, panel);
            Assert.Less(visible.Count, all.Count, "the panel shows the local neighbourhood instead");
            CollectionAssert.Contains(visible.Select(r => r.NodeId).ToList(), 6, "the current room is always on the map");
            Assert.LessOrEqual(visible.Count, 1 + 2 * MinimapLayout.LocalRadius);
            var cells = MinimapLayout.Place(visible, panel);
            foreach (var cell in cells)
            {
                Assert.LessOrEqual(cell.Center.x + cell.Size.x / 2f, panel.x + 0.01f);
                Assert.GreaterOrEqual(cell.Center.x - cell.Size.x / 2f, -0.01f);
            }
        }

        [Test]
        public void ASeededDungeonOfEveryBiome_MapsOntoTheMinimapExactly()
        {
            var pools = BiomeRoomPools.Build(ShippedRooms());
            var rules = DungeonGraphRules.CreateDefault();
            var generator = new DungeonGraphGenerator(rules);
            var panel = new Vector2(HudMinimapView.Width - HudMinimapView.Inset * 2, HudMinimapView.Height - HudMinimapView.Inset * 2);
            try
            {
                foreach (Biome biome in System.Enum.GetValues(typeof(Biome)))
                foreach (var seed in new[] { 11, 12, 77 })
                for (var depth = 1; depth <= 3; depth++)
                {
                    var generation = DungeonGenerationPipeline.Generate(generator, pools.PoolFor(biome), seed, depth);
                    Assert.IsTrue(generation.Success, $"{biome} seed {seed} depth {depth}: {generation.Error}");
                    var model = new MinimapModel();
                    model.BeginDepth(depth, RoomDisplayNames.BiomeName(biome));
                    foreach (var placement in generation.Layout.Placements)
                    {
                        var bounds = placement.Bounds;
                        model.AddRoom(placement.NodeId, new Vector2(bounds.xMin + bounds.width * 0.5f, bounds.yMin + bounds.height * 0.5f), MinimapRoomKind.Normal);
                    }

                    foreach (var connection in generation.Layout.Connections) model.AddLink(connection.NodeA, connection.NodeB);

                    Assert.AreEqual(generation.Layout.Placements.Count, model.Rooms.Count, "one map cell per generated room, no more");
                    Assert.AreEqual(generation.Layout.Connections.Count, model.Links.Count, "one map link per realised door");
                    // Walking the whole depth reveals everything and nothing that does not exist.
                    foreach (var placement in generation.Layout.Placements) model.MarkEntered(placement.NodeId);
                    Assert.AreEqual(model.Rooms.Count, model.VisitedRooms.Count());
                    Assert.IsTrue(model.DiscoveredLinks.All(l => generation.Layout.Connections.Any(c => c.Joins(l.a, l.b))));
                    // Whatever the shape, the panel always produces cells that fit it.
                    var cells = MinimapLayout.Place(MinimapLayout.VisibleRooms(model, panel), panel);
                    Assert.IsNotEmpty(cells);
                    foreach (var cell in cells)
                    {
                        Assert.GreaterOrEqual(cell.Center.x - cell.Size.x / 2f, -0.01f, $"{biome} seed {seed} depth {depth}");
                        Assert.LessOrEqual(cell.Center.x + cell.Size.x / 2f, panel.x + 0.01f, $"{biome} seed {seed} depth {depth}");
                        Assert.GreaterOrEqual(cell.Center.y - cell.Size.y / 2f, -0.01f, $"{biome} seed {seed} depth {depth}");
                        Assert.LessOrEqual(cell.Center.y + cell.Size.y / 2f, panel.y + 0.01f, $"{biome} seed {seed} depth {depth}");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(rules);
            }
        }

        [Test]
        public void TheHudSourceNeverDetectsRoomEntryItself()
        {
            // The room title and the minimap must be fed by the one authoritative room-entry event, never by a
            // second detector living in the HUD.
            foreach (var path in Directory.GetFiles("Assets/Game/Scripts/UI/Hud", "*.cs", SearchOption.AllDirectories))
            {
                var source = File.ReadAllText(path);
                StringAssert.DoesNotContain("OnTriggerEnter", source, path);
                StringAssert.DoesNotContain("Physics2D.", source, path);
                StringAssert.DoesNotContain("RoomRuntime", source, path);
            }
        }
    }
}

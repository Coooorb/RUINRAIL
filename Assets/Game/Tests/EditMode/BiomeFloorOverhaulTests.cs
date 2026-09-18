using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.EditorTools.ArtGen;
using UnityEngine;

namespace RuinRail.Tests.EditMode
{
    /// <summary>
    /// UI-and-biome polish pass, part B — the floors must stop reading as wallpaper, and that has to be measurable
    /// rather than asserted in a report.
    ///
    /// Two numbers carry most of the weight here. <em>Flatness</em> is the share of a tile whose four neighbours are
    /// the same colour: dense micro-noise drives it toward zero, broad material regions keep it high, so it is a
    /// direct measure of the "calm base first, detail second" rule. <em>Clustering</em> is the share of a family's
    /// cells that touch another cell of the same family: an even sprinkle scores low, authored zones score high.
    ///
    /// Neither number says the art is good. They say the art obeys the rules that made the old floors repetitive.
    /// </summary>
    public sealed class BiomeFloorOverhaulTests
    {
        private static readonly TileFactory.Biome[] Biomes = (TileFactory.Biome[])Enum.GetValues(typeof(TileFactory.Biome));

        // ---------------- pixel measurements ----------------

        /// <summary>Share of pixels whose four neighbours all carry the same colour: the opposite of speckle.</summary>
        private static float Flatness(PixelCanvas c)
        {
            var flat = 0;
            var total = 0;
            for (var y = 1; y < c.Height - 1; y++)
            for (var x = 1; x < c.Width - 1; x++)
            {
                total++;
                var here = c.Get(x, y);
                if (Same(here, c.Get(x - 1, y)) && Same(here, c.Get(x + 1, y)) &&
                    Same(here, c.Get(x, y - 1)) && Same(here, c.Get(x, y + 1))) flat++;
            }

            return total == 0 ? 0f : flat / (float)total;
        }

        /// <summary>Share of pixels that differ from the pixel to their left — how busy the surface is horizontally.</summary>
        private static float EdgeDensity(PixelCanvas c)
        {
            var edges = 0;
            var total = 0;
            for (var y = 0; y < c.Height; y++)
            for (var x = 1; x < c.Width; x++)
            {
                total++;
                if (!Same(c.Get(x, y), c.Get(x - 1, y))) edges++;
            }

            return total == 0 ? 0f : edges / (float)total;
        }

        /// <summary>Share of pixels two tiles differ in — used to prove family members are actually different art.</summary>
        private static float Difference(PixelCanvas a, PixelCanvas b)
        {
            var differing = 0;
            for (var y = 0; y < a.Height; y++)
            for (var x = 0; x < a.Width; x++)
                if (!Same(a.Get(x, y), b.Get(x, y))) differing++;
            return differing / (float)(a.Width * a.Height);
        }

        private static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

        /// <summary>
        /// Standard deviation of luminance across the tile's interior, ignoring the authored seam on its edges.
        ///
        /// This is the number that catches the failure flatness misses. A tile can be 65% flat and still be a single
        /// large high-contrast blob on a field — and a large blob repeated every 32 px is *more* obviously a stamped
        /// grid than fine noise is. Keeping the interior spread low means the mottling gives the surface material
        /// character without giving the eye a shape to recognise, which is what stops a big room reading as wallpaper.
        /// </summary>
        private static float InteriorLuminanceSpread(PixelCanvas c)
        {
            const int margin = 3;
            var values = new List<float>();
            for (var y = margin; y < c.Height - margin; y++)
            for (var x = margin; x < c.Width - margin; x++)
            {
                var p = c.Get(x, y);
                values.Add((0.2126f * p.r + 0.7152f * p.g + 0.0722f * p.b) / 255f);
            }

            if (values.Count == 0) return 0f;
            var mean = values.Average();
            return Mathf.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / values.Count);
        }

        /// <summary>Share of a tile occupied by colours that are not one of the three most common ones.</summary>
        private static float MinorityMass(PixelCanvas c)
        {
            var counts = new Dictionary<int, int>();
            for (var y = 0; y < c.Height; y++)
            for (var x = 0; x < c.Width; x++)
            {
                var p = c.Get(x, y);
                var key = (p.r << 24) | (p.g << 16) | (p.b << 8) | p.a;
                counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
            }

            var dominant = counts.Values.OrderByDescending(v => v).Take(3).Sum();
            return 1f - dominant / (float)(c.Width * c.Height);
        }

        // ---------------- the base tile is calm ----------------

        [Test]
        public void EveryBiome_BaseFloorIsMostlyCalmStructuralMaterial()
        {
            foreach (var biome in Biomes)
            {
                var tile = TileFactory.Build(biome, TileRole.Floor, (int)TileFactory.FloorFamily.Base);

                Assert.GreaterOrEqual(Flatness(tile), 0.60f,
                    $"{biome} base floor: only {Flatness(tile):P0} of it is flat material. " +
                    "The spec asks for 60-75% calm mass; a busier tile becomes wallpaper when it is repeated.");

                Assert.LessOrEqual(EdgeDensity(tile), 0.22f,
                    $"{biome} base floor has {EdgeDensity(tile):P0} horizontal edge density — that is speckle, not a surface.");

                Assert.LessOrEqual(MinorityMass(tile), 0.16f,
                    $"{biome} base floor puts {MinorityMass(tile):P0} of its area into incidental colours; the base member should be near-monolithic.");

                Assert.LessOrEqual(InteriorLuminanceSpread(tile), 0.018f,
                    $"{biome} base floor has an interior luminance spread of {InteriorLuminanceSpread(tile):F3}. " +
                    "A tile can be flat and still be one big high-contrast blob, and a blob repeated every 32 px is " +
                    "a stamped grid — the mottling has to stay below the threshold where the eye reads it as a shape.");
            }
        }

        /// <summary>
        /// How much visual noise one family member contributes to a whole room: its own contrast times the share of
        /// the room it covers.
        ///
        /// A flat per-tile contrast limit would be the wrong rule. The Accent member is *supposed* to be the loudest
        /// thing on the floor — that is what makes it an accent — and it is affordable precisely because it covers 4%
        /// of a room. The base member covers two thirds, so the same contrast there would be a disaster. What has to
        /// stay bounded is the product, and holding every member to one budget is what keeps a room calm without
        /// flattening the members that are meant to have character.
        /// </summary>
        [Test]
        public void NoFloorMember_ContributesMoreNoiseToARoomThanItsShareCanAfford()
        {
            const float budget = 0.016f;

            foreach (var biome in Biomes)
            foreach (var family in TileFactory.FloorFamilies)
            {
                var tile = TileFactory.Build(biome, TileRole.Floor, (int)family);
                var spread = InteriorLuminanceSpread(tile);
                var share = family == TileFactory.FloorFamily.Base
                    ? 1f - BiomeFloorPlan.FloorQuota.Where(kv => kv.Key != TileFactory.FloorFamily.Base).Sum(kv => kv.Value)
                    : BiomeFloorPlan.FloorQuota[family];

                Assert.LessOrEqual(spread * share, budget,
                    $"{biome} {family}: luminance spread {spread:F3} over {share:P0} of a room contributes " +
                    $"{spread * share:F4}, past the {budget:F3} budget. Either calm the tile or make it rarer.");
            }
        }

        [Test]
        public void FloorFamilyMembers_SitInTheSameMaterialFamily_SoZonesAreNotHarshPatches()
        {
            foreach (var biome in Biomes)
            {
                var baseLuminance = MeanLuminance(TileFactory.Build(biome, TileRole.Floor, (int)TileFactory.FloorFamily.Base));

                foreach (var family in new[] { TileFactory.FloorFamily.Worn, TileFactory.FloorFamily.Cracked })
                {
                    var luminance = MeanLuminance(TileFactory.Build(biome, TileRole.Floor, (int)family));
                    Assert.LessOrEqual(Mathf.Abs(luminance - baseLuminance), 0.07f,
                        $"{biome} {family} is {Mathf.Abs(luminance - baseLuminance):F3} away from the base floor in mean luminance. " +
                        "Wear and damage are the same material in a worse state; a large value jump turns a zone into a blotch.");
                }
            }
        }

        [Test]
        public void EveryBiome_ShipsTheWholeFloorFamilyAndEveryMemberIsDistinct()
        {
            foreach (var biome in Biomes)
            {
                Assert.AreEqual(5, TileFactory.VariantCount(TileRole.Floor), "Five floor family members per biome (spec B1.4).");

                var tiles = TileFactory.FloorFamilies.ToDictionary(f => f, f => TileFactory.Build(biome, TileRole.Floor, (int)f));
                var baseTile = tiles[TileFactory.FloorFamily.Base];

                foreach (var family in TileFactory.FloorFamilies.Where(f => f != TileFactory.FloorFamily.Base))
                    Assert.Greater(Difference(baseTile, tiles[family]), 0.10f,
                        $"{biome} {family} differs from the base tile in only {Difference(baseTile, tiles[family]):P0} of its pixels — " +
                        "that is a reseed, not a variant.");

                // The louder members are allowed to be busy; they are the minority of a room.
                foreach (var family in new[] { TileFactory.FloorFamily.Worn, TileFactory.FloorFamily.Cracked })
                    Assert.GreaterOrEqual(Flatness(tiles[family]), 0.40f,
                        $"{biome} {family} is too noisy even for a detail member.");
            }
        }

        [Test]
        public void EveryBiome_FloorTilesAreFullyOpaqueAndTheDetailLayerIsNot()
        {
            foreach (var biome in Biomes)
            {
                foreach (var family in TileFactory.FloorFamilies)
                {
                    var tile = TileFactory.Build(biome, TileRole.Floor, (int)family);
                    Assert.AreEqual(TileFactory.Size * TileFactory.Size, tile.OpaqueCount(),
                        $"{biome} {family} floor must cover its cell completely or the ground shows through.");
                }

                foreach (var kind in TileFactory.DetailKinds)
                {
                    var detail = TileFactory.Build(biome, TileRole.FloorDetail, (int)kind);
                    var coverage = detail.OpaqueCount() / (float)(TileFactory.Size * TileFactory.Size);
                    Assert.Less(coverage, 0.85f,
                        $"{biome} {kind} detail covers {coverage:P0} of its cell; a full-coverage detail layer is just a second floor (spec B6).");
                    Assert.Greater(coverage, 0.02f, $"{biome} {kind} detail is effectively empty.");
                }
            }
        }

        [Test]
        public void BaseFloor_TilesSeamlessly_SoALargeRoomHasNoGridOfHardEdges()
        {
            foreach (var biome in Biomes)
            {
                var tile = TileFactory.Build(biome, TileRole.Floor, (int)TileFactory.FloorFamily.Base);

                // The material field wraps, so the column that meets the tile's own left edge must not be a wall of
                // differences. The authored slab/plate seam is allowed to sit on the edge — it is one line, so the
                // comparison is against the column just inside it.
                var mismatches = 0;
                for (var y = 0; y < TileFactory.Size; y++)
                    if (!Same(tile.Get(TileFactory.Size - 1, y), tile.Get(2, y))) mismatches++;

                Assert.Less(mismatches, TileFactory.Size,
                    $"{biome} base floor: every row differs across the wrap, which draws a hard grid line every 32 px.");
            }
        }

        // ---------------- biome identity ----------------

        [Test]
        public void TheThreeBiomes_AreDistinguishableFromTheirFloorsAlone()
        {
            var floors = Biomes.ToDictionary(b => b, b => TileFactory.Build(b, TileRole.Floor, (int)TileFactory.FloorFamily.Base));

            foreach (var a in Biomes)
            foreach (var b in Biomes.Where(x => x > a))
                Assert.Greater(Difference(floors[a], floors[b]), 0.80f,
                    $"{a} and {b} base floors share too much; the biome identification test (spec B8) needs them to differ on more than a tint.");

            // Rustworks must be the darkest of the three and the Labs the palest, which is what the spec's palettes say.
            var brightness = Biomes.ToDictionary(b => b, b => MeanLuminance(floors[b]));
            Assert.Less(brightness[TileFactory.Biome.Rustworks], brightness[TileFactory.Biome.RuinedMetro],
                "Rustworks is blackened steel and soot; it cannot be lighter than Metro concrete.");
            Assert.Greater(brightness[TileFactory.Biome.OvergrownLabs], brightness[TileFactory.Biome.RuinedMetro],
                "Overgrown Labs is pale lab surfacing; it cannot be darker than Metro concrete.");
        }

        [Test]
        public void Rustworks_DoesNotSprayOrangeAcrossItsOrdinaryFloor()
        {
            // The old floor put rust pixels on every tile, which turned whole rooms orange. Heat and rust now belong
            // to the Cracked member and to the detail layer, which together are a small share of a room.
            var calmMembers = new[] { TileFactory.FloorFamily.Base, TileFactory.FloorFamily.Worn, TileFactory.FloorFamily.Utility };

            foreach (var family in calmMembers)
            {
                var tile = TileFactory.Build(TileFactory.Biome.Rustworks, TileRole.Floor, (int)family);
                Assert.Less(WarmFraction(tile), 0.04f,
                    $"Rustworks {family} floor is {WarmFraction(tile):P0} warm/orange. Neutral darks must dominate (spec 14).");
            }
        }

        [Test]
        public void OvergrownLabs_DoesNotScatterGreenOverItsOrdinaryFloor()
        {
            var calmMembers = new[] { TileFactory.FloorFamily.Base, TileFactory.FloorFamily.Worn, TileFactory.FloorFamily.Utility };

            foreach (var family in calmMembers)
            {
                var tile = TileFactory.Build(TileFactory.Biome.OvergrownLabs, TileRole.Floor, (int)family);
                Assert.Less(VegetationFraction(tile), 0.03f,
                    $"Overgrown Labs {family} floor carries {VegetationFraction(tile):P0} vegetation. Green belongs in the Accent member, in clusters (spec B4).");
            }

            // And the accent member must actually be a colony rather than a sprinkle.
            var accent = TileFactory.Build(TileFactory.Biome.OvergrownLabs, TileRole.Floor, (int)TileFactory.FloorFamily.Accent);
            Assert.Greater(VegetationFraction(accent), 0.12f, "The vegetation member should carry a visible colony.");
            Assert.GreaterOrEqual(Flatness(accent), 0.35f, "Even the vegetation member is a mass, not a spray of single pixels.");
        }

        private static float MeanLuminance(PixelCanvas c)
        {
            var sum = 0f;
            for (var y = 0; y < c.Height; y++)
            for (var x = 0; x < c.Width; x++)
            {
                var p = c.Get(x, y);
                sum += (0.2126f * p.r + 0.7152f * p.g + 0.0722f * p.b) / 255f;
            }

            return sum / (c.Width * c.Height);
        }

        /// <summary>Share of pixels that are clearly warm: red well above blue, and not merely a warm grey.</summary>
        private static float WarmFraction(PixelCanvas c)
        {
            var warm = 0;
            for (var y = 0; y < c.Height; y++)
            for (var x = 0; x < c.Width; x++)
            {
                var p = c.Get(x, y);
                if (p.r > p.b + 26 && p.r > p.g + 12) warm++;
            }

            return warm / (float)(c.Width * c.Height);
        }

        /// <summary>
        /// Share of pixels that are clearly vegetation: green dominant over both red and blue.
        ///
        /// The margin is 10, not something larger, because RUINRAIL's survival greens are deliberately desaturated
        /// (spec 3.3: Olive is #425640, Deep Olive #26352A). A stricter test would classify the project's own
        /// vegetation palette as grey and pass a floor covered in moss. The lab panel greys stay well under this
        /// margin, so the two families remain separable.
        /// </summary>
        private static float VegetationFraction(PixelCanvas c)
        {
            var green = 0;
            for (var y = 0; y < c.Height; y++)
            for (var x = 0; x < c.Width; x++)
            {
                var p = c.Get(x, y);
                if (p.g > p.r + 10 && p.g > p.b + 10) green++;
            }

            return green / (float)(c.Width * c.Height);
        }

        // ---------------- room-level distribution ----------------

        private static IReadOnlyList<Vector3Int> Room(int width, int height)
        {
            var cells = new List<Vector3Int>();
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                cells.Add(new Vector3Int(x, y, 0));
            return cells;
        }

        [Test]
        public void RoomPlan_HitsTheScreenBalanceTargetInEveryRoomSize()
        {
            foreach (var (w, h) in new[] { (20, 14), (12, 10), (30, 20), (8, 6) })
            foreach (var seed in new[] { 1, 991, 40503 })
            {
                var plan = BiomeFloorPlan.PlanFloor(Room(w, h), seed);
                var balance = BiomeFloorPlan.Measure(plan);

                Assert.GreaterOrEqual(balance[TileFactory.FloorFamily.Base], 0.60f,
                    $"{w}x{h} seed {seed}: only {balance[TileFactory.FloorFamily.Base]:P0} calm floor; spec B7 asks for 60-75%.");
                Assert.LessOrEqual(balance[TileFactory.FloorFamily.Base], 0.80f,
                    $"{w}x{h} seed {seed}: {balance[TileFactory.FloorFamily.Base]:P0} calm floor leaves the room featureless.");
                Assert.LessOrEqual(balance[TileFactory.FloorFamily.Accent], 0.08f,
                    "The loudest floor member must stay a focal note.");
                Assert.AreEqual(w * h, plan.Count, "Every floor cell gets a family.");
            }
        }

        [Test]
        public void RoomPlan_PutsDetailInZonesRatherThanSprinklingItEvenly()
        {
            var cells = Room(24, 16);
            var plan = BiomeFloorPlan.PlanFloor(cells, 12345);

            foreach (var family in new[] { TileFactory.FloorFamily.Worn, TileFactory.FloorFamily.Cracked })
            {
                var clustering = BiomeFloorPlan.Clustering(plan, family);
                Assert.GreaterOrEqual(clustering, 0.55f,
                    $"{family} clustering is {clustering:P0}; a room needs quiet ground and busy ground, not an even sprinkle (spec B1.5).");
            }

            // The control: the same quota handed out at random scores far lower, which is what makes the number mean
            // something rather than being true of any assignment.
            var rng = new System.Random(7);
            var shuffled = cells.OrderBy(_ => rng.Next()).ToList();
            var random = new Dictionary<Vector3Int, TileFactory.FloorFamily>();
            var wornQuota = plan.Values.Count(v => v == TileFactory.FloorFamily.Worn);
            for (var i = 0; i < shuffled.Count; i++)
                random[shuffled[i]] = i < wornQuota ? TileFactory.FloorFamily.Worn : TileFactory.FloorFamily.Base;

            Assert.Greater(BiomeFloorPlan.Clustering(plan, TileFactory.FloorFamily.Worn),
                BiomeFloorPlan.Clustering(random, TileFactory.FloorFamily.Worn) + 0.2f,
                "The plan must be visibly more clustered than the same quota scattered at random.");
        }

        [Test]
        public void RoomPlan_IsDeterministic_SoABakeIsReproducible()
        {
            var cells = Room(18, 12);
            var first = BiomeFloorPlan.PlanFloor(cells, 2026);
            var second = BiomeFloorPlan.PlanFloor(cells.Reverse().ToList(), 2026);

            CollectionAssert.AreEquivalent(first, second,
                "The same room and seed must plan identically regardless of the order the cells arrive in.");

            var different = BiomeFloorPlan.PlanFloor(cells, 2027);
            Assert.AreNotEqual(
                string.Join(",", first.OrderBy(kv => kv.Key.x).ThenBy(kv => kv.Key.y).Select(kv => kv.Value)),
                string.Join(",", different.OrderBy(kv => kv.Key.x).ThenBy(kv => kv.Key.y).Select(kv => kv.Value)),
                "Different rooms must not all receive the same floor plan.");
        }

        [Test]
        public void DetailPlan_KeepsTheQuietestKindDominant()
        {
            var plan = BiomeFloorPlan.PlanDetail(Room(14, 10), 555);
            var grime = plan.Values.Count(v => v == TileFactory.DetailKind.Grime) / (float)plan.Count;

            Assert.GreaterOrEqual(grime, 0.45f, "Broad grime is the quietest detail and should dominate the layer.");
            Assert.AreEqual(4, TileFactory.VariantCount(TileRole.FloorDetail));
            CollectionAssert.AreEquivalent(TileFactory.DetailKinds, plan.Values.Distinct().ToList(),
                "Every detail kind is reachable in a room of this size.");
        }

        // ---------------- room repainting contract ----------------

        [Test]
        public void Repainter_RecognisesBothPlaceholdersAndItsOwnPreviousOutput()
        {
            Assert.AreEqual(TileRole.Floor, RoomTileRepainter.RoleOf("Placeholder_Floor", TileFactory.Biome.RuinedMetro));
            Assert.AreEqual(TileRole.FloorDetail, RoomTileRepainter.RoleOf("Placeholder_FloorDetail", TileFactory.Biome.RuinedMetro));

            // Its own output, so a second pass re-plans rather than doing nothing.
            Assert.AreEqual(TileRole.Floor, RoomTileRepainter.RoleOf("ruinedmetro_floor", TileFactory.Biome.RuinedMetro));
            Assert.AreEqual(TileRole.Floor, RoomTileRepainter.RoleOf("ruinedmetro_floor_cracked", TileFactory.Biome.RuinedMetro));
            Assert.AreEqual(TileRole.FloorDetail, RoomTileRepainter.RoleOf("ruinedmetro_floor_detail_marking", TileFactory.Biome.RuinedMetro),
                "floor_detail must not be mistaken for floor.");
            Assert.AreEqual(TileRole.Wall, RoomTileRepainter.RoleOf("ruinedmetro_wall", TileFactory.Biome.RuinedMetro));
            Assert.AreEqual(TileRole.Hazard, RoomTileRepainter.RoleOf("ruinedmetro_hazard", TileFactory.Biome.RuinedMetro));

            // Another biome's tile is not this biome's tile.
            Assert.IsNull(RoomTileRepainter.RoleOf("rustworks_floor", TileFactory.Biome.RuinedMetro));
            Assert.IsNull(RoomTileRepainter.RoleOf("SomeOtherTile", TileFactory.Biome.RuinedMetro));
        }

        [Test]
        public void Repainter_SeedsRoomsStablyAndDifferently()
        {
            var a = RoomTileRepainter.SeedFor("Assets/Game/Prefabs/Rooms/RuinedMetro/Room_Metro_01.prefab");
            var b = RoomTileRepainter.SeedFor("Assets/Game/Prefabs/Rooms/RuinedMetro/Room_Metro_02.prefab");

            Assert.AreEqual(a, RoomTileRepainter.SeedFor("Assets/Game/Prefabs/Rooms/RuinedMetro/Room_Metro_01.prefab"),
                "The same room always bakes the same way.");
            Assert.AreNotEqual(a, b, "Two rooms must not receive the same floor plan.");
            Assert.GreaterOrEqual(a, 0);
        }
    }
}

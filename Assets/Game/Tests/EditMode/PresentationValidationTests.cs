using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Core.Rendering;
using RuinRail.Dungeon.Grid;
using RuinRail.EditorTools.Production;
using RuinRail.Presentation;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests.EditMode
{
    /// <summary>TASK 137 — approved sorting layers, 640×360 @ PPU 32 camera config, pixel-grid framing math, biome lighting readability, production validator.</summary>
    public sealed class PresentationValidationTests
    {
        [Test]
        public void SortingLayers_ExistInTheApprovedOrder_AndRolesMapOntoThem()
        {
            CollectionAssert.AreEqual(
                new[] { "Ground", "GroundDetails", "LowProps", "Characters", "Weapons", "WorldProps", "AboveCharacters", "Projectiles", "WorldVFX", "Loot", "UIWorld", "ScreenUI" },
                SortingLayers.Ordered, "art/102 recommended layers, bottom to top.");
            var project = SortingLayer.layers.Select(l => l.name).Where(SortingLayers.Ordered.Contains).ToList();
            CollectionAssert.AreEqual(SortingLayers.Ordered, project, "TagManager defines them in that order.");

            int Rank(string layer) => SortingLayers.Ordered.ToList().IndexOf(layer);
            Assert.Less(Rank(SortingConvention.LayerOf(SortingRole.Floor)), Rank(SortingConvention.LayerOf(SortingRole.LowWall)));
            Assert.Less(Rank(SortingConvention.LayerOf(SortingRole.LowWall)), Rank(SortingConvention.LayerOf(SortingRole.Character)), "Lower wall geometry sits below characters.");
            Assert.Less(Rank(SortingConvention.LayerOf(SortingRole.Character)), Rank(SortingConvention.LayerOf(SortingRole.Weapon)), "360° aimed weapons render over the body.");
            Assert.Less(Rank(SortingConvention.LayerOf(SortingRole.Character)), Rank(SortingConvention.LayerOf(SortingRole.AboveCharacters)), "Characters move behind tall walls.");
            Assert.Less(Rank(SortingConvention.LayerOf(SortingRole.AboveCharacters)), Rank(SortingConvention.LayerOf(SortingRole.Projectile)), "Projectiles are never hidden under occluders.");
            Assert.Less(Rank(SortingConvention.LayerOf(SortingRole.Projectile)), Rank(SortingConvention.LayerOf(SortingRole.WorldVfx)));
            Assert.Less(Rank(SortingConvention.LayerOf(SortingRole.Loot)), Rank(SortingConvention.LayerOf(SortingRole.ScreenUi)));
            Assert.AreEqual(10, SortingConvention.BaseOrderOf(SortingRole.Hazard), "Hazards above floor details on the same layer.");

            // Y-sorting: lower on screen draws on top, quantised to whole pixels.
            Assert.IsTrue(SortingConvention.IsYSorted(SortingRole.Character));
            Assert.IsFalse(SortingConvention.IsYSorted(SortingRole.Projectile));
            Assert.Greater(SortingConvention.OrderOf(SortingRole.Character, 1f), SortingConvention.OrderOf(SortingRole.Character, 2f));
            Assert.AreEqual(SortingConvention.OrderOf(SortingRole.Character, 1.001f), SortingConvention.OrderOf(SortingRole.Character, 1.01f), "Sub-pixel y differences never change the order.");
            Assert.AreEqual(-32, SortingConvention.OrderOf(SortingRole.Character, 1f));

            // Tilemap layers of every room render on the approved layers.
            Assert.AreEqual("Ground", RoomTilemapLayers.SortingLayerNameOf(RoomTilemapLayer.Floor));
            Assert.AreEqual("GroundDetails", RoomTilemapLayers.SortingLayerNameOf(RoomTilemapLayer.Hazards));
            Assert.AreEqual("LowProps", RoomTilemapLayers.SortingLayerNameOf(RoomTilemapLayer.Walls));
            Assert.AreEqual("AboveCharacters", RoomTilemapLayers.SortingLayerNameOf(RoomTilemapLayer.AbovePlayer));
            Assert.Less(RoomTilemapLayers.GlobalRank(RoomTilemapLayer.Hazards), Rank("Characters") * 1000, "Hazards render under characters.");
        }

        [Test]
        public void CameraConfig_IsApproved_AndFramingKeepsThePixelGridAtEverySupportedScale()
        {
            var config = AssetDatabase.LoadAssetAtPath<CameraRigConfig>(PresentationValidator.CameraConfigPath);
            Assert.IsNotNull(config);
            Assert.IsTrue(config.IsApproved);
            Assert.AreEqual(640, config.ReferenceWidth);
            Assert.AreEqual(360, config.ReferenceHeight);
            Assert.AreEqual(32, config.PixelsPerUnit);
            Assert.AreEqual(5.625f, config.OrthographicSize, 1e-5f, "360 px / 32 PPU / 2.");
            Assert.LessOrEqual(config.AimOffsetMaxTiles, 1f, "Subtle aim offset.");

            Assert.AreEqual(1, CameraFraming.IntegerScaleFor(640, 360, 640, 360));
            Assert.AreEqual(2, CameraFraming.IntegerScaleFor(1280, 720, 640, 360));
            Assert.AreEqual(3, CameraFraming.IntegerScaleFor(1920, 1080, 640, 360), "1920×1080 is a clean 3× (art/101).");
            Assert.AreEqual(4, CameraFraming.IntegerScaleFor(2560, 1440, 640, 360));
            Assert.AreEqual(6, CameraFraming.IntegerScaleFor(3840, 2160, 640, 360));

            // Moving and aiming for 300 steps at 60 Hz: every camera position lands exactly on the 1/32 grid, the aim offset stays subtle, and the follow converges.
            var camera = Vector2.zero;
            var target = Vector2.zero;
            for (var i = 0; i < 300; i++)
            {
                target += new Vector2(0.037f, 0.011f);
                var aim = new Vector2(7.3f, -2.1f);
                camera = CameraFraming.Step(camera, target, aim, null, new Vector2(10f, 5.625f), config, 1f / 60f);
                Assert.IsTrue(CameraFraming.IsOnPixelGrid(camera, 32), $"Step {i}: {camera} is off the pixel grid.");
            }

            var offset = CameraFraming.AimOffset(new Vector2(7.3f, -2.1f), config.AimOffsetFraction, config.AimOffsetMaxTiles);
            Assert.LessOrEqual(offset.magnitude, config.AimOffsetMaxTiles + 1e-5f);
            Assert.LessOrEqual((camera - (target + offset)).magnitude, 0.5f, "Soft follow without excessive lag.");
            Assert.IsTrue(CameraFraming.IsOnPixelGrid(CameraFraming.SnapToPixelGrid(new Vector2(1.2345f, -9.8765f), 32), 32));

            // Bounds: the view never leaves the visible world; a world smaller than the view is centred.
            var half = new Vector2(10f, 5.625f);
            var bounds = new Rect(0f, 0f, 40f, 30f);
            Assert.AreEqual(new Vector2(10f, 5.625f), CameraFraming.ClampToBounds(new Vector2(-50f, -50f), half, bounds));
            Assert.AreEqual(new Vector2(30f, 24.375f), CameraFraming.ClampToBounds(new Vector2(500f, 500f), half, bounds));
            Assert.AreEqual(new Vector2(4f, 3f), CameraFraming.ClampToBounds(new Vector2(100f, 100f), half, new Rect(0f, 0f, 8f, 6f)));
            Assert.AreEqual(new Vector2(3f, 4f), CameraFraming.ClampToBounds(new Vector2(3f, 4f), half, null));
        }

        [Test]
        public void BiomeLighting_HasAReadableProfilePerBiome_AndTheValidatorPasses()
        {
            foreach (Biome biome in System.Enum.GetValues(typeof(Biome)))
            {
                var profile = AssetDatabase.LoadAssetAtPath<BiomeLightingProfile>($"{PresentationValidator.LightingFolder}/Lighting_{biome}.asset");
                Assert.IsNotNull(profile, $"Lighting profile for {biome}.");
                Assert.AreEqual(biome, profile.Biome);
                Assert.IsTrue(profile.IsReadable, "Visibility never depends on lighting; no post effects.");
                Assert.GreaterOrEqual(profile.GlobalLightIntensity, 1f);
                Assert.IsFalse(profile.PostProcessing);
            }

            var report = PresentationValidator.WriteReport();
            Assert.IsTrue(report.Pass, string.Join("\n", report.Problems));
            Assert.IsTrue(report.Passed.Any(p => p.Contains("room prefabs render on the approved sorting layers")));
            Assert.IsTrue(report.Passed.Any(p => p.Contains("640×360 @ PPU 32")));
            Assert.AreEqual(report.ToMarkdown(), PresentationValidator.ValidateProject().ToMarkdown(), "Deterministic.");
            Assert.IsTrue(System.IO.File.Exists(PresentationValidator.ReportPath));
        }
    }
}

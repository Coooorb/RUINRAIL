using System.Collections.Generic;
using NUnit.Framework;
using RuinRail.UI.Theme;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>The custom pixel cursors: bound, imported as crisp hardware cursors with correct hotspots, and owned by one state rule.</summary>
    public sealed class CursorTests
    {
        [TearDown]
        public void TearDown() => CursorService.Reset();

        [Test]
        public void UiSkin_BindsThreeCursorTextures_ImportedAsPointFilteredReadableCursors_WithHotspotsInside()
        {
            UiSkin.InvalidateCache();
            var skin = UiSkin.Load();
            Assert.IsNotNull(skin);
            Assert.IsTrue(skin.HasCursors, "pointer, hover and aim cursors are bound");

            foreach (var (name, texture, hotspot) in new[]
            {
                ("pointer", skin.CursorPointer, skin.CursorPointerHotspot),
                ("hover", skin.CursorHover, skin.CursorHoverHotspot),
                ("aim", skin.CursorAim, skin.CursorAimHotspot)
            })
            {
                Assert.IsNotNull(texture, name);
                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
                Assert.IsNotNull(importer, $"{name}: project asset");
                Assert.AreEqual(TextureImporterType.Cursor, importer.textureType, $"{name}: imported as a hardware cursor");
                Assert.IsTrue(importer.isReadable, $"{name}: readable (required for Cursor.SetCursor)");
                Assert.AreEqual(FilterMode.Point, importer.filterMode, $"{name}: nearest filtering");
                Assert.AreEqual(TextureImporterCompression.Uncompressed, importer.textureCompression, $"{name}: no compression artefacts");
                Assert.IsFalse(importer.mipmapEnabled, $"{name}: no mipmaps");
                Assert.LessOrEqual(texture.width, 64, $"{name}: desktop-sized");
                Assert.LessOrEqual(texture.height, 64, $"{name}: desktop-sized");
                Assert.IsTrue(hotspot.x >= 0f && hotspot.y >= 0f && hotspot.x < texture.width && hotspot.y < texture.height, $"{name}: hotspot {hotspot} inside {texture.width}x{texture.height}");

                // No anti-aliased fringe: every pixel is either fully opaque or fully clear.
                var pixels = texture.GetPixels32();
                var opaque = 0;
                foreach (var p in pixels)
                {
                    Assert.IsTrue(p.a == 0 || p.a == 255, $"{name}: semi-transparent pixel (alpha {p.a})");
                    if (p.a == 255) opaque++;
                }

                Assert.Greater(opaque, 20, $"{name}: draws something");
            }

            // The aim hotspot is the exact centre of the crosshair; the pointer's is its tip (top-left corner region).
            Assert.AreEqual(skin.CursorAim.width * 0.5f, skin.CursorAimHotspot.x, 1f, "aim hotspot centred horizontally");
            Assert.AreEqual(skin.CursorAim.height * 0.5f, skin.CursorAimHotspot.y, 1f, "aim hotspot centred vertically");
            Assert.Less(skin.CursorPointerHotspot.x, 4f, "pointer hotspot at the tip");
            Assert.Less(skin.CursorPointerHotspot.y, 4f, "pointer hotspot at the tip");
            Assert.AreNotEqual(skin.CursorPointer, skin.CursorHover, "hover is a distinct state");

            // The hover variant carries the amber accent; the default pointer does not.
            Assert.IsTrue(HasAmber(skin.CursorHover), "hover cursor uses the amber accent");
            Assert.IsFalse(HasAmber(skin.CursorPointer), "default pointer stays neutral");
        }

        private static bool HasAmber(Texture2D texture)
        {
            foreach (var p in texture.GetPixels32())
            {
                if (p.a == 255 && p.r > 200 && p.g > 130 && p.g < 190 && p.b < 110) return true;
            }

            return false;
        }

        [Test]
        public void CursorRule_MenusPointer_GameplayAim_OverlaysTakeThePointer_HoverOnlyDecoratesThePointer()
        {
            Assert.AreEqual(CursorKind.Pointer, CursorService.Resolve(CursorKind.Pointer, 0, false));
            Assert.AreEqual(CursorKind.Hover, CursorService.Resolve(CursorKind.Pointer, 0, true));
            Assert.AreEqual(CursorKind.Aim, CursorService.Resolve(CursorKind.Aim, 0, false));
            Assert.AreEqual(CursorKind.Aim, CursorService.Resolve(CursorKind.Aim, 0, true), "hover never shows over gameplay");
            Assert.AreEqual(CursorKind.Pointer, CursorService.Resolve(CursorKind.Aim, 1, false), "a pause/inventory overlay takes the pointer");
            Assert.AreEqual(CursorKind.Hover, CursorService.Resolve(CursorKind.Aim, 1, true));
            Assert.AreEqual(CursorKind.System, CursorService.Resolve(CursorKind.System, 3, true), "no skin: the OS cursor");
        }

        [Test]
        public void CursorOwnership_SceneBase_OverlayPushPop_HoverAndFocusRegain_ApplyExactlyTheExpectedCursor()
        {
            var applied = new List<CursorKind>();
            CursorService.SetApplier(kind => { applied.Add(kind); return true; });

            CursorService.SetBase(CursorKind.Pointer);   // main menu / shelter
            CursorService.SetHover(true);
            CursorService.SetHover(false);
            CursorService.SetBase(CursorKind.Aim);       // dungeon composed
            CursorService.PushOverlay();                 // pause opened
            CursorService.PushOverlay();                 // inventory over it
            CursorService.PopOverlay();
            CursorService.SetHover(true);                // pointer over a pause button
            CursorService.PopOverlay();                  // pause closed: aim again, hover cleared
            CursorService.Reapply();                     // alt-tab back in

            CollectionAssert.AreEqual(new[]
            {
                CursorKind.Pointer, CursorKind.Hover, CursorKind.Pointer,
                CursorKind.Aim, CursorKind.Pointer, CursorKind.Pointer, CursorKind.Pointer, CursorKind.Hover,
                CursorKind.Aim, CursorKind.Aim
            }, applied);
            Assert.AreEqual(CursorKind.Aim, CursorService.Current);
            Assert.AreEqual(0, CursorService.Overlays);
            Assert.IsFalse(CursorService.Hovering, "hover does not survive the overlay that owned it");

            CursorService.PopOverlay();
            Assert.AreEqual(0, CursorService.Overlays, "never negative");
        }

        [Test]
        public void CursorTextures_ResolveFromTheSkinForEveryKind_AndNullForTheOsCursor()
        {
            UiSkin.InvalidateCache();
            var skin = UiSkin.Load();
            Assert.AreSame(skin.CursorPointer, CursorService.TextureFor(CursorKind.Pointer, skin).texture);
            Assert.AreSame(skin.CursorHover, CursorService.TextureFor(CursorKind.Hover, skin).texture);
            Assert.AreSame(skin.CursorAim, CursorService.TextureFor(CursorKind.Aim, skin).texture);
            Assert.IsNull(CursorService.TextureFor(CursorKind.System, skin).texture);
            Assert.IsNull(CursorService.TextureFor(CursorKind.Aim, null).texture, "no skin: OS cursor, never an invisible one");
        }
    }
}

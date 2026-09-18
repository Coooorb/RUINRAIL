using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// Renders generated art to inspectable PNGs outside the Assets tree.
    ///
    /// FINAL_ART_PRODUCTION_SPEC section 25 requires representative captures to be looked at rather than assumed
    /// correct. These previews exist so generated art can be reviewed before it is promoted anywhere near the
    /// release path, at 1x for honest pixel assessment and at an integer zoom for legibility.
    /// </summary>
    public static class ArtPreviewTool
    {
        public const string PreviewFolder = "TestResults/ArtPreview";

        [MenuItem("RuinRail/Art/Preview Character Sheet")]
        public static void PreviewPlayer() => WritePreview(CharacterCatalog.Player(), "player");

        /// <summary>Single family, every facing at Idle plus the S-facing states, large enough to judge construction.</summary>
        [MenuItem("RuinRail/Art/Preview Focus Family")]
        public static void PreviewFocus()
        {
            Directory.CreateDirectory(PreviewFolder);
            foreach (var id in new[] { "player", "grunt", "sniper_enemy", "brute", "boss_the_conductor" })
            {
                var p = CharacterCatalog.ById(id);
                if (p == null) continue;

                var facings = new List<PixelCanvas>();
                foreach (Facing8 f in System.Enum.GetValues(typeof(Facing8)))
                    facings.Add(CharacterSpriteFactory.Build(p, f, VisualState.Idle, 0));
                var states = new List<PixelCanvas>();
                foreach (VisualState s in System.Enum.GetValues(typeof(VisualState)))
                    states.Add(CharacterSpriteFactory.Build(p, Facing8.S, s, CharacterSpriteFactory.FrameCount(s) / 2));

                var sheet = PixelCanvas.Grid(new List<IReadOnlyList<PixelCanvas>> { facings, states });
                File.WriteAllBytes($"{PreviewFolder}/focus_{id}.png", sheet.ToPng());
                File.WriteAllBytes($"{PreviewFolder}/focus_{id}_x8.png", Zoom(sheet, 8).ToPng());
            }
            Debug.Log("focus previews written");
        }

        /// <summary>Writes a facing x state contact sheet, plus a zoomed copy, for one family.</summary>
        public static string WritePreview(CharacterProfile profile, string name, int zoom = 4)
        {
            Directory.CreateDirectory(PreviewFolder);

            var rows = new List<IReadOnlyList<PixelCanvas>>();
            foreach (Facing8 facing in System.Enum.GetValues(typeof(Facing8)))
            {
                var row = new List<PixelCanvas>();
                foreach (VisualState state in System.Enum.GetValues(typeof(VisualState)))
                {
                    var frames = CharacterSpriteFactory.FrameCount(state);
                    for (var f = 0; f < frames; f++)
                        row.Add(CharacterSpriteFactory.Build(profile, facing, state, f));
                }
                rows.Add(row);
            }

            var sheet = PixelCanvas.Grid(rows);
            var path = $"{PreviewFolder}/{name}_sheet.png";
            File.WriteAllBytes(path, sheet.ToPng());
            File.WriteAllBytes($"{PreviewFolder}/{name}_sheet_x{zoom}.png", Zoom(sheet, zoom).ToPng());
            return path;
        }

        /// <summary>Writes a single frame large, for judging construction detail.</summary>
        public static void WriteFrame(PixelCanvas canvas, string name, int zoom = 8)
        {
            Directory.CreateDirectory(PreviewFolder);
            File.WriteAllBytes($"{PreviewFolder}/{name}.png", canvas.ToPng());
            File.WriteAllBytes($"{PreviewFolder}/{name}_x{zoom}.png", Zoom(canvas, zoom).ToPng());
        }

        /// <summary>Nearest-neighbour integer zoom. Never used for shipped assets — preview legibility only.</summary>
        public static PixelCanvas Zoom(PixelCanvas source, int factor)
        {
            var big = new PixelCanvas(source.Width * factor, source.Height * factor);
            for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
            {
                if (!source.IsOpaque(x, y)) continue;
                var c = source.Get(x, y);
                for (var dy = 0; dy < factor; dy++)
                for (var dx = 0; dx < factor; dx++)
                    big.Set(x * factor + dx, y * factor + dy, c);
            }
            return big;
        }

        /// <summary>Lays out several canvases side by side on a flat backdrop, for comparing silhouettes.</summary>
        public static PixelCanvas Contact(IReadOnlyList<PixelCanvas> items, Color32 backdrop, int gap = 2)
        {
            var w = 0; var h = 0;
            foreach (var i in items) { w += i.Width + gap; h = Mathf.Max(h, i.Height); }
            var sheet = new PixelCanvas(w + gap, h + gap * 2);
            sheet.Rect(0, 0, sheet.Width, sheet.Height, backdrop);
            var x = gap;
            foreach (var i in items) { sheet.Blit(i, x, gap); x += i.Width + gap; }
            return sheet;
        }

        /// <summary>
        /// Every biome tile, each shown 2x2 so seams and repetition are visible, with the whole floor family in a row.
        ///
        /// The floor row is the point of this sheet after the floor overhaul: five members side by side, each tiled
        /// against itself, is the fastest way to see whether the base member is calm enough to repeat and whether the
        /// louder members are distinguishable from it.
        /// </summary>
        [MenuItem("RuinRail/Art/Preview Biome Tiles")]
        public static void PreviewTiles()
        {
            Directory.CreateDirectory(PreviewFolder);
            foreach (TileFactory.Biome biome in System.Enum.GetValues(typeof(TileFactory.Biome)))
            {
                var blocks = new List<PixelCanvas>();
                foreach (TileRole role in System.Enum.GetValues(typeof(TileRole)))
                for (var variant = 0; variant < TileFactory.VariantCount(role); variant++)
                {
                    // 2x2 of the same tile, to expose seams and obvious repetition.
                    var block = new PixelCanvas(TileFactory.Size * 2, TileFactory.Size * 2);
                    var tile = TileFactory.Build(biome, role, variant);
                    for (var ty = 0; ty < 2; ty++)
                    for (var tx = 0; tx < 2; tx++)
                        block.Blit(tile, tx * TileFactory.Size, ty * TileFactory.Size);
                    blocks.Add(block);
                }

                var sheet = Contact(blocks, RuinPalette.NearBlack, 3);
                File.WriteAllBytes($"{PreviewFolder}/tiles_{biome}.png", sheet.ToPng());
                File.WriteAllBytes($"{PreviewFolder}/tiles_{biome}_x3.png", Zoom(sheet, 3).ToPng());

                // A large patch of the base member alone: the honest repetition test, since that is the tile the
                // player spends nearly every frame looking at.
                var baseTile = TileFactory.Build(biome, TileRole.Floor);
                var field = new PixelCanvas(TileFactory.Size * 8, TileFactory.Size * 5);
                for (var ty = 0; ty < 5; ty++)
                for (var tx = 0; tx < 8; tx++)
                    field.Blit(baseTile, tx * TileFactory.Size, ty * TileFactory.Size);
                File.WriteAllBytes($"{PreviewFolder}/floorfield_{biome}.png", field.ToPng());
            }
            Debug.Log("tile previews written");
        }

        /// <summary>All 33 weapons on one sheet, grouped so class families can be compared for palette-swap drift.</summary>
        [MenuItem("RuinRail/Art/Preview Weapons")]
        public static void PreviewWeapons()
        {
            Directory.CreateDirectory(PreviewFolder);
            var designs = WeaponFactory.All();
            var rows = new List<IReadOnlyList<PixelCanvas>>();
            var row = new List<PixelCanvas>();
            var maxW = 0; var maxH = 0;
            foreach (var d in designs) { maxW = Mathf.Max(maxW, d.Width); maxH = Mathf.Max(maxH, d.Height); }

            foreach (var d in designs)
            {
                // Pad every weapon onto a common cell so the sheet grid stays aligned.
                var cell = new PixelCanvas(maxW + 2, maxH + 2);
                cell.Blit(WeaponFactory.Build(d), 1, (maxH - d.Height) / 2 + 1);
                row.Add(cell);
                if (row.Count == 3) { rows.Add(row); row = new List<PixelCanvas>(); }
            }
            if (row.Count > 0) rows.Add(row);

            var sheet = PixelCanvas.Grid(rows);
            var backdrop = new PixelCanvas(sheet.Width, sheet.Height);
            backdrop.Rect(0, 0, sheet.Width, sheet.Height, RuinPalette.Concrete);
            backdrop.Blit(sheet, 0, 0);
            File.WriteAllBytes($"{PreviewFolder}/weapons.png", backdrop.ToPng());
            File.WriteAllBytes($"{PreviewFolder}/weapons_x4.png", Zoom(backdrop, 4).ToPng());
            Debug.Log($"{designs.Count} weapon previews written");
        }

        /// <summary>Icons, VFX frames and a font specimen on one sheet.</summary>
        [MenuItem("RuinRail/Art/Preview Support Art")]
        public static void PreviewSupportArt()
        {
            Directory.CreateDirectory(PreviewFolder);

            // --- VFX: every role's frames, one role per row ---
            var vfxRows = new List<IReadOnlyList<PixelCanvas>>();
            var cell = 32;
            foreach (var role in VfxFactory.Roles)
            {
                var row = new List<PixelCanvas>();
                for (var f = 0; f < VfxFactory.FrameCount(role); f++)
                {
                    var pad = new PixelCanvas(cell, cell);
                    var art = VfxFactory.Build(role, f);
                    pad.Blit(art, (cell - art.Width) / 2, (cell - art.Height) / 2);
                    row.Add(pad);
                }
                vfxRows.Add(row);
            }
            var vfx = PixelCanvas.Grid(vfxRows);
            var vfxBg = new PixelCanvas(vfx.Width, vfx.Height);
            vfxBg.Rect(0, 0, vfx.Width, vfx.Height, RuinPalette.Charcoal);
            vfxBg.Blit(vfx, 0, 0);
            File.WriteAllBytes($"{PreviewFolder}/vfx.png", vfxBg.ToPng());
            File.WriteAllBytes($"{PreviewFolder}/vfx_x3.png", Zoom(vfxBg, 3).ToPng());

            // --- Font specimen, including the characters spec 18.7 calls out ---
            var specimenLines = new[]
            {
                "RUINRAIL SHELTER", "Depth 12 - Rustworks",
                "0O 1Il 5S 8B 2Z", "HP 84/120  AMMO 24/90", "Legendary Drop!"
            };
            var font = new PixelCanvas(220, specimenLines.Length * PixelFontFactory.LineHeight + 6);
            font.Rect(0, 0, font.Width, font.Height, RuinPalette.Charcoal);
            for (var i = 0; i < specimenLines.Length; i++)
            {
                var y = font.Height - 4 - i * PixelFontFactory.LineHeight;
                var x = 4;
                foreach (var ch in specimenLines[i])
                {
                    PixelFontFactory.DrawGlyph(font, ch, x, y, RuinPalette.Hex("#D8DCD4"));
                    x += PixelFontFactory.Advance;
                }
            }
            File.WriteAllBytes($"{PreviewFolder}/font_specimen.png", font.ToPng());
            File.WriteAllBytes($"{PreviewFolder}/font_specimen_x4.png", Zoom(font, 4).ToPng());

            Debug.Log("support art previews written");
        }

        [MenuItem("RuinRail/Art/Preview All Character Silhouettes")]
        public static void PreviewAllSilhouettes()
        {
            Directory.CreateDirectory(PreviewFolder);
            var idles = new List<PixelCanvas>();
            foreach (var profile in CharacterCatalog.All())
                idles.Add(CharacterSpriteFactory.Build(profile, Facing8.S, VisualState.Idle, 0));

            // Against a Ruined Metro floor value, which is where readability matters most.
            var contact = Contact(idles, RuinPalette.Concrete);
            File.WriteAllBytes($"{PreviewFolder}/all_families.png", contact.ToPng());
            File.WriteAllBytes($"{PreviewFolder}/all_families_x4.png", Zoom(contact, 4).ToPng());
            Debug.Log($"Wrote {idles.Count} family silhouettes to {PreviewFolder}");
        }
    }
}

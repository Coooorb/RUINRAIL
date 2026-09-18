using System.IO;
using RuinRail.UI.Theme;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// Original RUINRAIL hardware cursors in the project's pixel language (spec 18: charcoal plate, thin amber accent,
    /// 1 px outline, no anti-aliasing): a compact pointer, its hover/select variant, and the gameplay crosshair.
    ///
    /// Each cursor is drawn at 1:1 into a small canvas, written to the UI art folder, imported as a Cursor texture
    /// (point filtered, uncompressed, readable, no mipmaps — the only import that gives a crisp hardware cursor) and
    /// bound into <see cref="UiSkin"/> with its hotspot: the pointer's tip, the crosshair's exact centre.
    /// </summary>
    public static class CursorFactory
    {
        public const string Folder = "Assets/Game/Art/UI";
        public const string PointerPath = Folder + "/cursor_pointer.png";
        public const string HoverPath = Folder + "/cursor_pointer_hover.png";
        public const string AimPath = Folder + "/cursor_aim.png";

        /// <summary>The desktop cursor size: 2× the authored 12/17 px art so it reads on a 1080p desktop without blur.</summary>
        public const int Scale = 2;
        public const int PointerWidth = 12;
        public const int PointerHeight = 17;
        public const int AimSize = 17;

        private static readonly Color32 Outline = RuinPalette.NearBlack;
        private static readonly Color32 Plate = RuinPalette.Hex("#DCE0D8");
        private static readonly Color32 PlateShade = RuinPalette.Hex("#9CA49F");
        private static readonly Color32 Amber = RuinPalette.Hex("#E7A74A");
        private static readonly Color32 AmberDeep = RuinPalette.Hex("#8A6329");

        [MenuItem("RuinRail/Art/Generate Cursors")]
        public static void GenerateMenu() => GenerateAndBind();

        public static void GenerateAndBind()
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(PointerPath, Upscale(Pointer(hover: false)).ToPng());
            File.WriteAllBytes(HoverPath, Upscale(Pointer(hover: true)).ToPng());
            File.WriteAllBytes(AimPath, Upscale(Crosshair()).ToPng());
            AssetDatabase.Refresh();
            foreach (var path in new[] { PointerPath, HoverPath, AimPath }) ConfigureImport(path);
            AssetDatabase.Refresh();

            var skin = AssetDatabase.LoadAssetAtPath<UiSkin>(ArtIntegration.UiSkinPath);
            if (skin == null)
            {
                skin = ScriptableObject.CreateInstance<UiSkin>();
                AssetDatabase.CreateAsset(skin, ArtIntegration.UiSkinPath);
            }

            // Hotspots are in texture pixels from the top-left: the pointer's tip pixel, the crosshair's centre pixel.
            skin.EditorSetCursors(
                AssetDatabase.LoadAssetAtPath<Texture2D>(PointerPath), new Vector2(1f * Scale, 0f),
                AssetDatabase.LoadAssetAtPath<Texture2D>(HoverPath), new Vector2(1f * Scale, 0f),
                AssetDatabase.LoadAssetAtPath<Texture2D>(AimPath), new Vector2(AimSize / 2 * Scale + Scale * 0.5f, AimSize / 2 * Scale + Scale * 0.5f));
            EditorUtility.SetDirty(skin);
            AssetDatabase.SaveAssets();
            UiSkin.InvalidateCache();
            Debug.Log("Cursors generated and bound: pointer, hover, aim.");
        }

        /// <summary>The classic arrow silhouette, drawn as a plate with a 1 px outline; the hover variant carries the amber edge.</summary>
        public static PixelCanvas Pointer(bool hover)
        {
            var c = new PixelCanvas(PointerWidth, PointerHeight);
            // Row-by-row silhouette (top = row 0). '#' outline, 'o' plate, 's' shade, '.' empty.
            var rows = new[]
            {
                ".#..........",
                ".##.........",
                ".#o#........",
                ".#oo#.......",
                ".#ooo#......",
                ".#oooo#.....",
                ".#ooooo#....",
                ".#oooooo#...",
                ".#ooooooo#..",
                ".#oooooooo#.",
                ".#oooo#####.",
                ".#oo#os#....",
                ".#o#.#os#...",
                ".##..#os#...",
                ".#....#os#..",
                "......#ss#..",
                ".......##..."
            };
            Paint(c, rows, hover ? Amber : Plate, hover ? AmberDeep : PlateShade, Outline);
            return c;
        }

        /// <summary>A gap-centred crosshair: four ticks with a clear centre so the hotspot pixel never covers the target.</summary>
        public static PixelCanvas Crosshair()
        {
            var c = new PixelCanvas(AimSize, AimSize);
            var mid = AimSize / 2;
            const int gap = 3;
            const int tick = 5;
            for (var i = gap; i < gap + tick; i++)
            {
                // Outline first, amber core on top, in both directions from the centre.
                foreach (var (x, y) in new[] { (mid + i, mid), (mid - i, mid), (mid, mid + i), (mid, mid - i) })
                {
                    c.Set(x, y, Amber);
                    if (Mathf.Abs(x - mid) > 0) { c.Set(x, y - 1, Outline); c.Set(x, y + 1, Outline); }
                    else { c.Set(x - 1, y, Outline); c.Set(x + 1, y, Outline); }
                }
            }

            // Cap the four ticks so they read as a reticle, not four floating dashes.
            foreach (var (x, y) in new[] { (mid + gap + tick, mid), (mid - gap - tick, mid), (mid, mid + gap + tick), (mid, mid - gap - tick) })
                c.Set(x, y, Outline);

            // A single centre dot in the deeper amber: visible, but the hotspot stays a one-pixel mark.
            c.Set(mid, mid, AmberDeep);
            return c;
        }

        private static void Paint(PixelCanvas c, string[] rows, Color32 plate, Color32 shade, Color32 outline)
        {
            for (var y = 0; y < rows.Length; y++)
            for (var x = 0; x < rows[y].Length; x++)
            {
                // rows[0] is the top row; the canvas stores y upward (texture convention).
                var ty = rows.Length - 1 - y;
                switch (rows[y][x])
                {
                    case '#': c.Set(x, ty, outline); break;
                    case 'o': c.Set(x, ty, plate); break;
                    case 's': c.Set(x, ty, shade); break;
                }
            }
        }

        /// <summary>Nearest-neighbour integer upscale: the hardware cursor is shown 1:1, so the scale is baked into the texture.</summary>
        public static PixelCanvas Upscale(PixelCanvas source)
        {
            var scaled = new PixelCanvas(source.Width * Scale, source.Height * Scale);
            for (var y = 0; y < scaled.Height; y++)
            for (var x = 0; x < scaled.Width; x++)
            {
                if (source.IsOpaque(x / Scale, y / Scale)) scaled.Set(x, y, source.Get(x / Scale, y / Scale));
            }

            return scaled;
        }

        private static void ConfigureImport(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
            {
                Debug.LogError($"No importer for {path}.");
                return;
            }

            importer.textureType = TextureImporterType.Cursor;
            importer.isReadable = true;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
        }
    }
}

using System.IO;
using RuinRail.Core;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// The combat-door presentation per biome (spec 13/14/15 "Doors"): one 2-tile × 1-tile sprite for the open state
    /// (housing and posts, the doorway itself clear) and one for the locked state (a physical shutter across the
    /// doorway with a lock indicator). Authored horizontally for North/South sockets; East/West sockets rotate it.
    ///
    /// Ruined Metro: a transit service shutter in steel and concrete with a small amber/red lock lamp. Rustworks: a
    /// heavier blackened-steel barrier with a restrained hot-orange warning stripe. Overgrown Labs: a damaged pale
    /// security door whose system light is cyan/green when open and amber/red when locked. Same gameplay object in
    /// every biome; only the skin differs.
    /// </summary>
    public static class DoorFactory
    {
        public const string Folder = "Assets/Game/Art/World/Doors";
        public const int Width = 64;
        public const int Height = 32;

        public static string PathFor(Biome biome, bool locked) => $"{Folder}/door_{Stem(biome)}_{(locked ? "locked" : "open")}.png";

        public static string Stem(Biome biome) => biome switch
        {
            Biome.RuinedMetro => "ruinedmetro",
            Biome.Rustworks => "rustworks",
            Biome.OvergrownLabs => "overgrownlabs",
            _ => biome.ToString().ToLowerInvariant()
        };

        [MenuItem("RuinRail/Art/Generate Doors")]
        public static void GenerateMenu() => Generate();

        public static void Generate()
        {
            Directory.CreateDirectory(Folder);
            foreach (Biome biome in System.Enum.GetValues(typeof(Biome)))
            {
                File.WriteAllBytes(PathFor(biome, false), Build(biome, locked: false).ToPng());
                File.WriteAllBytes(PathFor(biome, true), Build(biome, locked: true).ToPng());
            }

            AssetDatabase.Refresh();
            foreach (Biome biome in System.Enum.GetValues(typeof(Biome)))
            {
                ConfigureImport(PathFor(biome, false));
                ConfigureImport(PathFor(biome, true));
            }

            AssetDatabase.Refresh();
            Debug.Log("Door sprites generated: 3 biomes × open/locked.");
        }

        private sealed class Skin
        {
            public Color32 Post, PostLight, Housing, Slat, SlatLight, SlatDark, Seam, OpenLamp, LockLamp, Accent, Outline;
            public int SlatPitch = 4;
            public bool Chevrons;
            public bool Panels;
        }

        private static Skin SkinOf(Biome biome) => biome switch
        {
            Biome.Rustworks => new Skin
            {
                Post = RuinPalette.Hex("#1E2224"), PostLight = RuinPalette.Hex("#3A4043"), Housing = RuinPalette.Hex("#2B3235"),
                Slat = RuinPalette.Hex("#2A2D2F"), SlatLight = RuinPalette.Hex("#3D4245"), SlatDark = RuinPalette.Hex("#17191B"),
                Seam = RuinPalette.Hex("#0F1112"), OpenLamp = RuinPalette.Hex("#8A6329"), LockLamp = RuinPalette.OxideOrange,
                Accent = RuinPalette.OxideOrange, Outline = RuinPalette.OutlineRustBlack, SlatPitch = 6, Chevrons = true
            },
            Biome.OvergrownLabs => new Skin
            {
                Post = RuinPalette.Hex("#3E4A4C"), PostLight = RuinPalette.Hex("#6E7C7C"), Housing = RuinPalette.Hex("#556364"),
                Slat = RuinPalette.Hex("#8D9A98"), SlatLight = RuinPalette.Hex("#A9B5B2"), SlatDark = RuinPalette.Hex("#66716F"),
                Seam = RuinPalette.Hex("#2F3A3B"), OpenLamp = RuinPalette.ElectricCyan, LockLamp = RuinPalette.EmergencyRed,
                Accent = RuinPalette.TerminalGreen, Outline = RuinPalette.OutlineGreenBlack, SlatPitch = 8, Panels = true
            },
            _ => new Skin
            {
                Post = RuinPalette.Hex("#3A3C3E"), PostLight = RuinPalette.Hex("#5C5F61"), Housing = RuinPalette.Concrete,
                Slat = RuinPalette.MidSteel, SlatLight = RuinPalette.PaleSteel, SlatDark = RuinPalette.DarkSteel,
                Seam = RuinPalette.Hex("#1A1D1F"), OpenLamp = RuinPalette.Hex("#8A6329"), LockLamp = RuinPalette.EmergencyRed,
                Accent = RuinPalette.AmberActive, Outline = RuinPalette.OutlineCharcoal, SlatPitch = 4
            }
        };

        public static PixelCanvas Build(Biome biome, bool locked)
        {
            var s = SkinOf(biome);
            var c = new PixelCanvas(Width, Height);
            const int post = 4;

            // Housing rail along the top (the wall side) and the two posts: the frame that is always there.
            c.Rect(0, Height - 3, Width, 3, s.Housing);
            c.Rect(0, Height - 1, Width, 1, s.PostLight);
            c.Rect(0, 0, post, Height, s.Post);
            c.Rect(Width - post, 0, post, Height, s.Post);
            c.Rect(1, 0, 1, Height, s.PostLight);
            c.Rect(Width - 2, 0, 1, Height, s.PostLight);

            if (!locked)
            {
                // Open: the doorway is clear. Only a floor-level track and the system lamps in the "clear" colour.
                c.Rect(post, 0, Width - post * 2, 1, s.Seam);
                Lamp(c, s.OpenLamp, s.Outline);
                return c;
            }

            // Locked: a shutter fills the doorway between the posts.
            var inner = Width - post * 2;
            c.Rect(post, 0, inner, Height - 3, s.Slat);
            if (s.Panels)
            {
                // Lab security door: two pale panels meeting at a centre seam, with a bent panel edge (damage).
                c.Rect(post, 0, inner, Height - 3, s.Slat);
                c.Rect(Width / 2 - 1, 0, 2, Height - 3, s.Seam);
                for (var y = 2; y < Height - 5; y += s.SlatPitch) c.Rect(post + 2, y, inner - 4, 1, s.SlatDark);
                c.Rect(post + 2, Height - 6, inner / 2 - 4, 1, s.SlatLight);
                c.Rect(Width / 2 + 3, 6, 5, 1, s.SlatDark); // dent
                c.Rect(Width / 2 + 4, 7, 3, 1, s.SlatDark);
            }
            else
            {
                // Shutter slats: each slat has a light top edge and a dark underside.
                for (var y = 0; y < Height - 3; y += s.SlatPitch)
                {
                    c.Rect(post, y, inner, 1, s.SlatDark);
                    c.Rect(post, Mathf.Min(Height - 4, y + s.SlatPitch - 1), inner, 1, s.SlatLight);
                }

                c.Rect(Width / 2, 0, 1, Height - 3, s.Seam);
            }

            if (s.Chevrons)
            {
                // Rustworks: two restrained warning chevrons low on the barrier.
                for (var i = 0; i < 6; i++)
                {
                    c.Set(post + 6 + i, 4 + i, s.Accent);
                    c.Set(post + 6 + i, 10 - i + 4, s.Accent);
                    c.Set(Width - post - 7 - i, 4 + i, s.Accent);
                    c.Set(Width - post - 7 - i, 10 - i + 4, s.Accent);
                }
            }
            else if (!s.Panels)
            {
                // Metro: a stencilled bar in the accent, restrained.
                c.Rect(Width / 2 - 8, 3, 16, 2, s.Accent);
            }

            Lamp(c, s.LockLamp, s.Outline);
            c.SelectiveOutline(s.Outline);
            return c;
        }

        /// <summary>The lock indicator: a 4×3 lamp in the housing rail, with a dark socket.</summary>
        private static void Lamp(PixelCanvas c, Color32 colour, Color32 outline)
        {
            c.Rect(Width / 2 - 3, Height - 4, 6, 4, outline);
            c.Rect(Width / 2 - 2, Height - 3, 4, 2, colour);
        }

        private static void ConfigureImport(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
            {
                Debug.LogError($"No importer for {path}.");
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 32;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }
    }
}

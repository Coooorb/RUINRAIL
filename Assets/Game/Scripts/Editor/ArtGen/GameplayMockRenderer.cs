using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// Composites the integrated art into 640x360 gameplay views for the visual review
    /// FINAL_ART_PRODUCTION_SPEC section 25 asks for.
    ///
    /// These are compositions of the *shipped* assets at the *reference resolution*, drawn in the same layer order the
    /// game uses: floor, floor detail, hazards, props, actors with their weapons, VFX, then HUD. That makes them a
    /// genuine readability check — if the player cannot be picked out of a Rustworks floor here, they cannot be picked
    /// out in the game either.
    ///
    /// It is not a substitute for playing the game. It cannot show motion, timing or feel.
    /// </summary>
    public static class GameplayMockRenderer
    {
        public const int Width = 640;
        public const int Height = 360;
        public const string Folder = "TestResults/ArtPreview/Gameplay";

        /// <summary>Where the UI-and-biome polish pass collects its evidence.</summary>
        public const string PolishFolder = "TestResults/PolishPreview";

        /// <summary>
        /// The six biome views the polish pass has to be judged on: an ordinary combat scene and a
        /// telegraph-heavy one per biome, both at the reference resolution.
        /// </summary>
        [MenuItem("RuinRail/Art/Render Polish Previews")]
        public static void RenderPolishPreviews()
        {
            Directory.CreateDirectory(PolishFolder);

            foreach (TileFactory.Biome biome in Enum.GetValues(typeof(TileFactory.Biome)))
            {
                var combat = RenderScene(biome);
                File.WriteAllBytes($"{PolishFolder}/biome_{biome}_combat.png", combat.ToPng());
                File.WriteAllBytes($"{PolishFolder}/biome_{biome}_combat_x2.png", ArtPreviewTool.Zoom(combat, 2).ToPng());

                var telegraphs = RenderTelegraphScene(biome);
                File.WriteAllBytes($"{PolishFolder}/biome_{biome}_telegraphs.png", telegraphs.ToPng());
                File.WriteAllBytes($"{PolishFolder}/biome_{biome}_telegraphs_x2.png", ArtPreviewTool.Zoom(telegraphs, 2).ToPng());
            }

            Debug.Log($"Polish biome previews written to {PolishFolder}");
        }

        [MenuItem("RuinRail/Art/Render Gameplay Mocks")]
        public static void RenderAll()
        {
            Directory.CreateDirectory(Folder);

            foreach (TileFactory.Biome biome in Enum.GetValues(typeof(TileFactory.Biome)))
            {
                var scene = RenderScene(biome);
                File.WriteAllBytes($"{Folder}/scene_{biome}.png", scene.ToPng());
                File.WriteAllBytes($"{Folder}/scene_{biome}_x2.png", ArtPreviewTool.Zoom(scene, 2).ToPng());
            }

            var cast = RenderCast();
            File.WriteAllBytes($"{Folder}/cast_on_metro_floor.png", cast.ToPng());
            File.WriteAllBytes($"{Folder}/cast_on_metro_floor_x2.png", ArtPreviewTool.Zoom(cast, 2).ToPng());

            Debug.Log($"Gameplay mocks written to {Folder}");
        }

        /// <summary>
        /// The environment bed of a preview scene: floor family, detail layer and the wall bands.
        ///
        /// The floor is laid out by the same <see cref="BiomeFloorPlan"/> the room repainter uses rather than by
        /// picking a tile per cell at random. That matters: the preview exists to judge whether a room still reads as
        /// wallpaper, and it can only answer that if it distributes tiles the way a baked room does — same quotas,
        /// same zone clustering.
        /// </summary>
        public static PixelCanvas RenderFloorAndWalls(TileFactory.Biome biome, int seed)
        {
            var c = new PixelCanvas(Width, Height);

            var columns = Mathf.CeilToInt(Width / 32f);
            var rows = Mathf.CeilToInt(Height / 32f);
            var cells = new List<Vector3Int>();
            for (var y = 0; y < rows; y++)
            for (var x = 0; x < columns; x++)
                cells.Add(new Vector3Int(x, y, 0));

            var floorPlan = BiomeFloorPlan.PlanFloor(cells, seed);
            var floors = TileFactory.FloorFamilies.ToDictionary(f => f, f => TileFactory.Build(biome, TileRole.Floor, (int)f));
            foreach (var cell in cells)
                c.Blit(floors[floorPlan[cell]], cell.x * 32, cell.y * 32);

            // The detail layer covers a minority of cells and sits on top of the floor, as it does in a room.
            var detailCells = cells.Where(cell => BiomeFloorPlan.Hash01(cell.x, cell.y, seed ^ 0x77) > 0.74f).ToList();
            var detailPlan = BiomeFloorPlan.PlanDetail(detailCells, seed ^ 0x13579BDF);
            var details = TileFactory.DetailKinds.ToDictionary(d => d, d => TileFactory.Build(biome, TileRole.FloorDetail, (int)d));
            foreach (var cell in detailCells)
                c.Blit(details[detailPlan[cell]], cell.x * 32, cell.y * 32);

            // Wall band top and bottom.
            var wall = TileFactory.Build(biome, TileRole.Wall);
            for (var x = 0; x < Width; x += 32)
            {
                c.Blit(wall, x, Height - 32);
                c.Blit(wall, x, 0);
            }

            return c;
        }

        /// <summary>One biome room with actors, a telegraph, a weapon, loot and the HUD.</summary>
        public static PixelCanvas RenderScene(TileFactory.Biome biome)
        {
            var c = RenderFloorAndWalls(biome, 9041 + (int)biome * 37);
            var rng = new System.Random(4242 + (int)biome);

            // --- hazard patch and obstacles ---
            var hazard = TileFactory.Build(biome, TileRole.Hazard);
            c.Blit(hazard, 96, 96);
            c.Blit(hazard, 128, 96);
            var obstacle = TileFactory.Build(biome, TileRole.Obstacle);
            c.Blit(obstacle, 384, 160);
            c.Blit(obstacle, 416, 160);

            // --- dressing props ---
            for (var i = 0; i < 5; i++)
                c.Blit(BiomeDressingFactory.BuildVariant(biome, "props", i), 40 + i * 110, 210);

            // --- a door on the far wall ---
            c.Blit(BiomeDressingFactory.BuildVariant(biome, "doors", 0), 300, Height - 62);

            // --- loot and pickups ---
            Place(c, "world_item_pickup", 210, 120);
            Place(c, "world_coin_pickup", 250, 112);
            Place(c, "world_supply_chest", 520, 120);

            // --- the cast for this biome ---
            var (elite, boss) = BiomeActors(biome);
            DrawActor(c, "player", Facing8.E, VisualState.Idle, 0, 160, 150, "weapon_p9_ranger");
            DrawActor(c, "grunt", Facing8.W, VisualState.Attack, 2, 300, 150);
            DrawActor(c, "shooter", Facing8.SW, VisualState.Idle, 0, 380, 96, "weapon_ar_17");
            DrawActor(c, elite, Facing8.S, VisualState.Special, 1, 470, 176);
            DrawActor(c, boss, Facing8.S, VisualState.Idle, 0, 560, 190);

            // --- a live telegraph in front of the elite, and combat VFX ---
            Blend(c, VfxFactory.Build("telegraph_zone", 1), 440, 120);
            Blend(c, VfxFactory.Build("muzzle", 0), 196, 158);
            Blend(c, VfxFactory.Build("impact", 1), 288, 156);
            Blend(c, VfxFactory.Build("loot_glow", 2), 200, 110);

            DrawHud(c, biome);
            return c;
        }

        /// <summary>
        /// The worst case for floor readability: several live telegraphs, hazards and combat effects at once.
        ///
        /// This is the view that decides whether the floor overhaul went too far the other way. A calmer floor is only
        /// an improvement if the danger markings still win the frame on top of it, so the scene stacks every telegraph
        /// family the game can show at the same time and puts them over hazard tiles and props rather than over clean
        /// ground.
        /// </summary>
        public static PixelCanvas RenderTelegraphScene(TileFactory.Biome biome)
        {
            var c = RenderFloorAndWalls(biome, 7717 + (int)biome);

            // Hazards under the action, which is where a telegraph is hardest to read.
            var hazard = TileFactory.Build(biome, TileRole.Hazard);
            foreach (var (hx, hy) in new[] { (64, 128), (96, 128), (64, 160), (352, 96), (384, 96) })
                c.Blit(hazard, hx, hy);

            var obstacle = TileFactory.Build(biome, TileRole.Obstacle);
            c.Blit(obstacle, 256, 224);
            c.Blit(obstacle, 480, 192);

            var (elite, boss) = BiomeActors(biome);
            DrawActor(c, "player", Facing8.NE, VisualState.Move, 1, 150, 140, "weapon_ar_17");
            DrawActor(c, "charger", Facing8.W, VisualState.Special, 1, 300, 190);
            DrawActor(c, "bomber", Facing8.S, VisualState.Special, 0, 220, 96);
            DrawActor(c, elite, Facing8.S, VisualState.Special, 1, 430, 170);
            DrawActor(c, boss, Facing8.S, VisualState.Special, 1, 545, 150);

            // Every telegraph family at once, at its most visible frame.
            Blend(c, VfxFactory.Build("telegraph_zone", 2), 96, 112);
            Blend(c, VfxFactory.Build("telegraph_zone", 2), 400, 128);
            Blend(c, VfxFactory.Build("telegraph_dash", 1), 250, 180);
            Blend(c, VfxFactory.Build("telegraph_projectile", 1), 352, 208);
            Blend(c, VfxFactory.Build("telegraph_slam", 2), 512, 128);
            Blend(c, VfxFactory.Build("telegraph_stationary", 1), 176, 208);
            Blend(c, VfxFactory.Build("explosion", 3), 208, 120);
            Blend(c, VfxFactory.Build("muzzle", 1), 186, 148);
            Blend(c, VfxFactory.Build("impact", 2), 288, 196);
            Blend(c, VfxFactory.Build("status", 1), 436, 200);

            DrawHud(c, biome);
            return c;
        }

        /// <summary>Every character family lined up on a Ruined Metro floor, for silhouette comparison.</summary>
        public static PixelCanvas RenderCast()
        {
            var c = new PixelCanvas(Width, 200);
            var floor = TileFactory.Build(TileFactory.Biome.RuinedMetro, TileRole.Floor);
            for (var y = 0; y < 200; y += 32)
            for (var x = 0; x < Width; x += 32)
                c.Blit(floor, x, y);

            var x0 = 4;
            foreach (var profile in CharacterCatalog.All())
            {
                var art = CharacterSpriteFactory.Build(profile, Facing8.S, VisualState.Idle, 0);
                c.Blit(art, x0, 8);
                x0 += art.Width + 2;
                if (x0 > Width - 40) break;
            }

            return c;
        }

        private static (string elite, string boss) BiomeActors(TileFactory.Biome biome) => biome switch
        {
            TileFactory.Biome.RuinedMetro => ("elite_railguard", "boss_the_conductor"),
            TileFactory.Biome.Rustworks => ("elite_crusher_unit", "boss_scrap_king"),
            _ => ("elite_prototype_x7", "boss_aegis_core")
        };

        private static void DrawActor(PixelCanvas c, string actorId, Facing8 facing, VisualState state, int frame,
            int x, int y, string weaponId = null)
        {
            var profile = CharacterCatalog.ById(actorId);
            if (profile == null) return;

            var art = CharacterSpriteFactory.Build(profile, facing, state, frame);
            // Grounded contact shadow, as the presentation layer provides.
            c.Ellipse(x + profile.CanvasWidth / 2f, y + 4, profile.CanvasWidth / 3.2f, 3f, RuinPalette.Hex("#0D1112"));
            c.Blit(art, x, y);

            if (weaponId == null) return;
            var design = WeaponFactory.All().FirstOrDefault(d => d.Id == weaponId);
            if (design == null) return;

            // Weapon sits on the pivot at roughly chest height, pointing the way the body faces.
            var weapon = WeaponFactory.Build(design);
            c.Blit(weapon, x + profile.CanvasWidth - 6, y + profile.CanvasHeight / 2 - 2);
        }

        private static void Place(PixelCanvas c, string worldId, int x, int y)
        {
            var design = WorldObjectFactory.All().FirstOrDefault(d => d.Id == worldId);
            if (design == null) return;
            c.Blit(WorldObjectFactory.Build(design), x, y);
        }

        private static void Blend(PixelCanvas c, PixelCanvas fx, int x, int y) => c.Blit(fx, x, y);

        /// <summary>The HUD band: health, ammo and depth, drawn with the generated UI art and the pixel font.</summary>
        private static void DrawHud(PixelCanvas c, TileFactory.Biome biome)
        {
            // Health panel, bottom left.
            NineSlice(c, UiFactory.Panel(24), 6, 6, 150, 40);
            var hp = UiFactory.Bar(120, 8, RuinPalette.Hex("#B4483E"), 0.68f);
            c.Blit(hp, 16, 28);
            Text(c, "HP 82/120", 16, 22);
            c.Blit(UiFactory.DashIcon(), 136, 10);   // the dash indicator is an icon slot, not a text line

            // Weapon panel, bottom right.
            NineSlice(c, UiFactory.Panel(24), Width - 156, 6, 150, 40);
            Text(c, "P9 RANGER", Width - 146, 34);
            Text(c, "12 / 90", Width - 146, 22);
            var heat = UiFactory.Bar(120, 6, RuinPalette.OxideOrange, 0.35f);
            c.Blit(heat, Width - 146, 12);

            // Top context strip.
            NineSlice(c, UiFactory.Panel(24), Width / 2 - 80, Height - 30, 160, 24);
            Text(c, "DEPTH 12", Width / 2 - 70, Height - 12);
            Text(c, biome.ToString().ToUpperInvariant(), Width / 2 - 14, Height - 12);

            // An inventory slot with a rarity frame and a real item icon.
            var slot = UiFactory.InventorySlot(20, RuinPalette.Rarity["Legendary"], true);
            c.Blit(slot, 170, 10);
            var icon = IconFactory.BuildWeaponIcon(WeaponFactory.All().First(d => d.Id == "weapon_quickfang"));
            for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++)
                if (icon.IsOpaque(x * 2, y * 2)) c.Set(172 + x, 12 + y, icon.Get(x * 2, y * 2));
        }

        private static void Text(PixelCanvas c, string text, int x, int yBaseline)
        {
            foreach (var ch in text)
            {
                PixelFontFactory.DrawGlyph(c, ch, x, yBaseline + PixelFontFactory.GlyphHeight - 1, RuinPalette.Hex("#DCE0D8"));
                x += PixelFontFactory.Advance;
            }
        }

        /// <summary>Stretches a bordered panel sprite to an arbitrary size, as the UI system does at runtime.</summary>
        private static void NineSlice(PixelCanvas c, PixelCanvas source, int x, int y, int w, int h)
        {
            const int b = 4;
            for (var yy = 0; yy < h; yy++)
            for (var xx = 0; xx < w; xx++)
            {
                var sx = xx < b ? xx : xx >= w - b ? source.Width - (w - xx) : b + (xx - b) % Mathf.Max(1, source.Width - b * 2);
                var sy = yy < b ? yy : yy >= h - b ? source.Height - (h - yy) : b + (yy - b) % Mathf.Max(1, source.Height - b * 2);
                if (source.IsOpaque(sx, sy)) c.Set(x + xx, y + yy, source.Get(sx, sy));
            }
        }
    }
}

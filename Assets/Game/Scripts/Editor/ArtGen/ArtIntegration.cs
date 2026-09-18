using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuinRail.Gameplay.Items;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// Writes every generated asset into the project at its convention path, with the import settings
    /// FINAL_ART_PRODUCTION_SPEC section 21 requires, and binds it to the runtime seam that already expects it.
    ///
    /// Nothing here invents a new lookup path. Tiles land beside a Tile asset the room baker already reads, item
    /// icons bind to the ItemDefinition icon field, and character sheets are sliced into the CharacterAnimationSet
    /// the SpriteAnimator resolves by ActorId — the same seams TASK 151 documented.
    /// </summary>
    public static class ArtIntegration
    {
        public const string ArtRoot = "Assets/Game/Art";
        public const string GeneratedNote = "generated";

        // ---------- import ----------

        /// <summary>
        /// One texture's import contract, recorded while the PNG is written and applied after a single refresh.
        ///
        /// Import settings cannot be applied during generation: writing a PNG does not create an importer until the
        /// asset database has seen the file, so the whole run writes bytes first, refreshes once, then configures.
        /// That is also an order of magnitude faster than importing several hundred textures one at a time.
        /// </summary>
        private sealed class PendingImport
        {
            public string Path = string.Empty;
            public int Ppu = 32;
            public Vector2? Pivot;
            public Vector4? Border;
            public bool Sliced;
            public int CellWidth;
            public int CellHeight;
            public int SheetWidth;
            public int SheetHeight;
        }

        private static readonly List<PendingImport> Pending = new();

        /// <summary>Writes a single-sprite PNG and queues its import settings.</summary>
        public static void WriteSprite(PixelCanvas canvas, string assetPath, int pixelsPerUnit = 32,
            Vector2? pivot = null, Vector4? border = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath) ?? ArtRoot);
            File.WriteAllBytes(assetPath, canvas.ToPng());
            Pending.Add(new PendingImport { Path = assetPath, Ppu = pixelsPerUnit, Pivot = pivot, Border = border });
        }

        /// <summary>Writes a multi-frame sheet and queues a deterministic grid slice (spec 21 slicing rule).</summary>
        public static void WriteSheet(PixelCanvas sheet, string assetPath, int cellWidth, int cellHeight,
            int pixelsPerUnit = 32, Vector2? pivot = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath) ?? ArtRoot);
            File.WriteAllBytes(assetPath, sheet.ToPng());
            Pending.Add(new PendingImport
            {
                Path = assetPath, Ppu = pixelsPerUnit,
                Pivot = pivot ?? new Vector2(0.5f, 0.15f),   // feet anchor, matching the animation driver
                Sliced = true, CellWidth = cellWidth, CellHeight = cellHeight,
                SheetWidth = sheet.Width, SheetHeight = sheet.Height
            });
        }

        /// <summary>Applies every queued import contract. Called once, after the asset database has seen the files.</summary>
        private static void ApplyPendingImports()
        {
            foreach (var p in Pending)
            {
                if (AssetImporter.GetAtPath(p.Path) is not TextureImporter importer)
                {
                    Debug.LogError($"No importer for {p.Path}; the asset database has not seen it.");
                    continue;
                }

                ConfigureBase(importer, p.Ppu);

                if (p.Sliced)
                {
                    importer.spriteImportMode = SpriteImportMode.Multiple;
                    var cols = p.SheetWidth / p.CellWidth;
                    var rows = p.SheetHeight / p.CellHeight;
                    var metas = new List<SpriteMetaData>();
                    var name = Path.GetFileNameWithoutExtension(p.Path);

                    // Row 0 is the sheet's top row, so index rows downward for stable, predictable frame ids.
                    for (var r = 0; r < rows; r++)
                    for (var col = 0; col < cols; col++)
                        metas.Add(new SpriteMetaData
                        {
                            name = $"{name}_{r}_{col}",
                            rect = new Rect(col * p.CellWidth, p.SheetHeight - (r + 1) * p.CellHeight, p.CellWidth, p.CellHeight),
                            alignment = (int)SpriteAlignment.Custom,
                            pivot = p.Pivot ?? new Vector2(0.5f, 0.15f)
                        });

#pragma warning disable CS0618 // spritesheet is the supported deterministic grid-slicing path in this Unity version
                    importer.spritesheet = metas.ToArray();
#pragma warning restore CS0618
                }
                else
                {
                    importer.spriteImportMode = SpriteImportMode.Single;
                    if (p.Pivot.HasValue)
                    {
                        var settings = new TextureImporterSettings();
                        importer.ReadTextureSettings(settings);
                        settings.spriteAlignment = (int)SpriteAlignment.Custom;
                        settings.spritePivot = p.Pivot.Value;
                        importer.SetTextureSettings(settings);
                    }
                    if (p.Border.HasValue) importer.spriteBorder = p.Border.Value;
                }

                importer.SaveAndReimport();
            }

            Debug.Log($"Applied import settings to {Pending.Count} textures.");
            Pending.Clear();
        }

        private static void ConfigureBase(TextureImporter importer, int ppu)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = ppu;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 8192;
        }

        // ---------- generation passes ----------

        /// <summary>Everything, in dependency order. This is the one entry point the runner calls.</summary>
        [MenuItem("RuinRail/Art/Generate All Final Art")]
        public static void GenerateAll()
        {
            Pending.Clear();

            // Phase 1: write every PNG to disk. No asset database calls, so nothing imports yet.
            GenerateTiles();
            GenerateWeapons();
            GenerateIcons();
            GenerateVfx();
            GenerateWorldObjects();
            GenerateBiomeDressing();
            GenerateUiSprites();
            GenerateCharacters();

            // Phase 2: one refresh, then configure every texture's import contract.
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ApplyPendingImports();
            AssetDatabase.SaveAssets();

            // Phase 3: build the font and bind sprites to the runtime seams that expect them.
            PixelFontAssetBuilder.Build();
            AnimationSetBuilder.BuildAll();
            AuthorBiomeLighting();
            BindEverything();
            ProvenanceRecorder.Record();
            Debug.Log("Final art generation complete.");
        }

        public static void GenerateTiles()
        {
            foreach (TileFactory.Biome biome in Enum.GetValues(typeof(TileFactory.Biome)))
            foreach (TileRole role in Enum.GetValues(typeof(TileRole)))
            for (var variant = 0; variant < TileFactory.VariantCount(role); variant++)
            {
                var canvas = TileFactory.Build(biome, role, variant);
                WriteSprite(canvas, $"{ArtRoot}/Tiles/{biome}/{TileStem(biome, role, variant)}.png");
            }
        }

        private static string RoleStem(TileRole role) => role switch
        {
            TileRole.FloorDetail => "floor_detail",
            _ => role.ToString().ToLowerInvariant()
        };

        /// <summary>
        /// The file stem of one tile.
        ///
        /// Variant 0 keeps the bare role name — <c>ruinedmetro_floor</c> — because that is the path the asset naming
        /// convention, the completion manifest and the existing room prefabs already resolve. The rest of the family
        /// is suffixed with its own name, which reads better in the project window than a numeric index and matches
        /// the lowercase, underscore-separated convention (spec section 22).
        /// </summary>
        public static string TileStem(TileFactory.Biome biome, TileRole role, int variant)
        {
            var stem = $"{biome.ToString().ToLowerInvariant()}_{RoleStem(role)}";
            if (variant <= 0) return stem;

            var suffix = role switch
            {
                TileRole.Floor => ((TileFactory.FloorFamily)variant).ToString().ToLowerInvariant(),
                TileRole.FloorDetail => ((TileFactory.DetailKind)variant).ToString().ToLowerInvariant(),
                _ => variant.ToString()
            };
            return $"{stem}_{suffix}";
        }

        public static void GenerateWeapons()
        {
            foreach (var design in WeaponFactory.All())
            {
                var canvas = WeaponFactory.Build(design);
                var anchor = WeaponFactory.GripAnchor(design);
                // Pivot at the grip so WeaponPivot rotates about the hand, not the sprite centre.
                var pivot = new Vector2((anchor.x + 0.5f) / design.Width, (anchor.y + 0.5f) / design.Height);
                WriteSprite(canvas, $"{ArtRoot}/Weapons/{design.Id}.png", 32, pivot);
            }
        }

        public static void GenerateIcons()
        {
            foreach (var design in WeaponFactory.All())
                WriteSprite(IconFactory.BuildWeaponIcon(design), $"{ArtRoot}/Icons/Items/{design.Id}.png", 32);

            foreach (var design in ItemIconCatalog.NonWeaponIcons())
                WriteSprite(IconFactory.Build(design), $"{ArtRoot}/Icons/Items/{design.Id}.png", 32);
        }

        public static void GenerateVfx()
        {
            foreach (var role in VfxFactory.Roles)
            {
                var frames = new List<PixelCanvas>();
                for (var f = 0; f < VfxFactory.FrameCount(role); f++) frames.Add(VfxFactory.Build(role, f));
                var size = VfxFactory.SizeOf(role);
                var sheet = PixelCanvas.Row(frames);
                WriteSheet(sheet, $"{ArtRoot}/Vfx/vfx_{role}.png", size, size, 32, new Vector2(0.5f, 0.5f));
            }
        }

        public static void GenerateWorldObjects()
        {
            foreach (var design in WorldObjectFactory.All())
                WriteSprite(WorldObjectFactory.Build(design), $"{ArtRoot}/World/{design.Id}.png", 32,
                    new Vector2(0.5f, 0.12f));
        }

        /// <summary>
        /// Writes only the world-object sprites that do not exist yet (a new state frame such as the opened chest) and
        /// applies their import contract, leaving every accepted sprite untouched. Batch entry for the runner.
        /// </summary>
        [MenuItem("RuinRail/Art/Generate Missing World Objects")]
        public static void GenerateMissingWorldObjects()
        {
            Pending.Clear();
            var written = 0;
            foreach (var design in WorldObjectFactory.All())
            {
                var path = $"{ArtRoot}/World/{design.Id}.png";
                if (File.Exists(path)) continue;
                WriteSprite(WorldObjectFactory.Build(design), path, 32, new Vector2(0.5f, 0.12f));
                written++;
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ApplyPendingImports();
            AssetDatabase.SaveAssets();
            Debug.Log($"Generated {written} missing world-object sprite(s).");
        }

        public static void GenerateMissingWorldObjectsBatch()
        {
            try { GenerateMissingWorldObjects(); EditorApplication.Exit(0); }
            catch (System.Exception e) { Debug.LogError(e); EditorApplication.Exit(1); }
        }

        /// <summary>Per-biome prop, door, rail, decal, foreground and environment-VFX packages.</summary>
        public static void GenerateBiomeDressing()
        {
            foreach (TileFactory.Biome biome in Enum.GetValues(typeof(TileFactory.Biome)))
            foreach (var category in BiomeDressingFactory.Categories)
            {
                var sheet = BiomeDressingFactory.BuildPackage(biome, category);
                WriteSheet(sheet, $"{ArtRoot}/Props/{biome}/{category}/{biome.ToString().ToLowerInvariant()}_{category}.png",
                    BiomeDressingFactory.Cell, BiomeDressingFactory.Cell, 32, new Vector2(0.5f, 0.15f));
            }
        }

        /// <summary>
        /// Authors the three biome lighting looks, replacing the neutral white placeholders.
        ///
        /// This is what closes the one BLOCKED_REVIEW prototype marker TASK 179 could not disposition: the tints were
        /// undecidable while the biome art did not exist, and now it does.
        /// </summary>
        public static void AuthorBiomeLighting()
        {
            foreach (RuinRail.Core.Biome biome in Enum.GetValues(typeof(RuinRail.Core.Biome)))
            {
                var path = $"Assets/Game/ScriptableObjects/Presentation/Lighting_{biome}.asset";
                var profile = AssetDatabase.LoadAssetAtPath<RuinRail.Presentation.BiomeLightingProfile>(path);
                if (profile == null)
                {
                    Debug.LogError($"Missing lighting profile at {path}.");
                    continue;
                }

                var (ambient, intensity) = BiomeDressingFactory.LightingFor(RoomTileRepainter.FromRuntime(biome));
                profile.EditorSetLighting(ambient, intensity);
            }
        }

        public static void GenerateUiSprites()
        {
            const int ppu = 1;   // UI sprites are authored at screen pixels

            // Panels and frames, 9-sliced with integer borders.
            WriteSprite(UiFactory.Panel(24), $"{ArtRoot}/UI/ui_panel_frame.png", ppu, null, new Vector4(4, 4, 4, 4));
            WriteSprite(UiFactory.Panel(24, true), $"{ArtRoot}/UI/ui_panel_emphasis.png", ppu, null, new Vector4(5, 5, 5, 5));

            foreach (UiFactory.ButtonState state in Enum.GetValues(typeof(UiFactory.ButtonState)))
                WriteSprite(UiFactory.Button(24, 16, state),
                    $"{ArtRoot}/UI/ui_button_{state.ToString().ToLowerInvariant()}.png", ppu, null, new Vector4(5, 5, 5, 5));

            WriteSprite(UiFactory.InventorySlot(), $"{ArtRoot}/UI/ui_inventory_slot.png", ppu, null, new Vector4(4, 4, 4, 4));
            foreach (var kv in RuinPalette.Rarity)
                WriteSprite(UiFactory.InventorySlot(20, kv.Value),
                    $"{ArtRoot}/UI/ui_rarity_frame_{kv.Key.ToLowerInvariant()}.png", ppu, null, new Vector4(4, 4, 4, 4));

            WriteSprite(UiFactory.DashIcon(), $"{ArtRoot}/UI/ui_dash_icon.png", ppu);
            WriteSprite(UiFactory.CoinIcon(), $"{ArtRoot}/UI/ui_coin_icon.png", ppu);
            WriteSprite(UiFactory.LowHealthVignette(), $"{ArtRoot}/UI/ui_vignette_low_hp.png", ppu);
            WriteSprite(UiFactory.Bar(32, 8, RuinPalette.Hex("#B4483E")), $"{ArtRoot}/UI/ui_bar_hp.png", ppu, null, new Vector4(3, 3, 3, 3));
            WriteSprite(UiFactory.Bar(32, 6, RuinPalette.TerminalGreen), $"{ArtRoot}/UI/ui_bar_xp.png", ppu, null, new Vector4(3, 3, 3, 3));
            WriteSprite(UiFactory.Bar(32, 6, RuinPalette.OxideOrange), $"{ArtRoot}/UI/ui_bar_heat.png", ppu, null, new Vector4(3, 3, 3, 3));

            WriteSprite(UiFactory.MenuBackground(320, 180), $"{ArtRoot}/UI/ui_mainmenu_background.png", ppu);

            // The two full-screen front-end backdrops, authored at the 640x360 reference. These are what turn the
            // Shelter screen from a panel over black into a room the player is standing in (spec section 16).
            WriteSprite(ShelterSceneFactory.Shelter(), $"{ArtRoot}/UI/ui_shelter_backdrop.png", ppu);
            WriteSprite(ShelterSceneFactory.MainMenu(), $"{ArtRoot}/UI/ui_menu_backdrop.png", ppu);

            // The canonical button role, so the manifest's `ui.button` resolves rather than only its four states.
            WriteSprite(UiFactory.Button(24, 16), $"{ArtRoot}/UI/ui_button.png", ppu, null, new Vector4(5, 5, 5, 5));

            foreach (var action in UiFactory.GlyphActions)
            foreach (UiFactory.Device device in Enum.GetValues(typeof(UiFactory.Device)))
                WriteSprite(UiFactory.Glyph(action, device),
                    $"{ArtRoot}/UI/Glyphs/glyph_{device.ToString().ToLowerInvariant()}_{action.ToLowerInvariant()}.png", ppu);

            // One sheet per device holding every action glyph, which is the role the manifest tracks.
            foreach (UiFactory.Device device in Enum.GetValues(typeof(UiFactory.Device)))
            {
                var glyphs = UiFactory.GlyphActions.Select(a => UiFactory.Glyph(a, device)).ToList();
                var widest = glyphs.Max(g => g.Width);
                var cells = glyphs.Select(g =>
                {
                    var cell = new PixelCanvas(widest, g.Height);
                    cell.Blit(g, (widest - g.Width) / 2, 0);
                    return (PixelCanvas)cell;
                }).ToList();
                WriteSheet(PixelCanvas.Row(cells),
                    $"{ArtRoot}/UI/ui_glyphs_{device.ToString().ToLowerInvariant()}.png",
                    widest, glyphs[0].Height, ppu, new Vector2(0.5f, 0.5f));
            }

            // Shelter station icons as one sheet, for the hub's station bar.
            var stationIds = new[]
            {
                "base_storage", "base_loadout", "base_trader", "base_character_station",
                "base_workshop", "base_multiplayer_terminal", "base_expedition_transit"
            };
            var stations = stationIds
                .Select(id => WorldObjectFactory.All().First(d => d.Id == id))
                .Select(d =>
                {
                    // Normalise each station into a common 32x32 icon cell so the sheet slices evenly.
                    var art = WorldObjectFactory.Build(d);
                    var cell = new PixelCanvas(32, 32);
                    var scale = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(art.Width, art.Height) / 30f));
                    for (var y = 0; y < art.Height; y += scale)
                    for (var x = 0; x < art.Width; x += scale)
                        if (art.IsOpaque(x, y))
                            cell.Set(1 + x / scale, 1 + y / scale, art.Get(x, y));
                    return cell;
                })
                .ToList();
            WriteSheet(PixelCanvas.Row(stations), $"{ArtRoot}/UI/ui_shelter_stations.png", 32, 32, ppu, new Vector2(0.5f, 0.5f));


        }

        public static void GenerateCharacters()
        {
            foreach (var profile in CharacterCatalog.All())
            {
                var facings = (Facing8[])Enum.GetValues(typeof(Facing8));
                var states = (VisualState[])Enum.GetValues(typeof(VisualState));

                // One row per facing; states run left to right within the row. Deterministic, so the animation set
                // can address any clip by (facing, state, frame) without storing per-frame metadata.
                var rows = new List<IReadOnlyList<PixelCanvas>>();
                var columns = states.Sum(s => CharacterSpriteFactory.FrameCount(s));

                foreach (var facing in facings)
                {
                    var row = new List<PixelCanvas>();
                    foreach (var state in states)
                    for (var f = 0; f < CharacterSpriteFactory.FrameCount(state); f++)
                        row.Add(CharacterSpriteFactory.Build(profile, facing, state, f));
                    rows.Add(row);
                }

                var sheet = PixelCanvas.Grid(rows);
                var dir = $"{ArtRoot}/Characters/{profile.Id}";
                WriteSheet(sheet, $"{dir}/{profile.Id}_sheet.png", profile.CanvasWidth, profile.CanvasHeight);
            }
        }

        // ---------- binding ----------

        /// <summary>Wires generated sprites into the runtime seams that already expect them.</summary>
        public static void BindEverything()
        {
            BindTiles();
            BindItemIcons();
            BindUiSkin();
            AssetDatabase.SaveAssets();
        }

        /// <summary>Where the runtime finds the UI art. The sprites stay at their convention path; this is the pointer.</summary>
        public const string UiSkinPath = "Assets/Game/Resources/" + RuinRail.UI.Theme.UiSkin.ResourcePath + ".asset";

        /// <summary>
        /// Points the runtime UI skin at the generated backdrops and station icons.
        ///
        /// A player build can only load by path from a Resources folder, and UI art lives under Assets/Game/Art/UI
        /// where the naming convention and import validators expect it. So the asset here holds references rather
        /// than copies — the same split GameContentCatalog already uses for gameplay definitions.
        /// </summary>
        public static void BindUiSkin()
        {
            var skin = AssetDatabase.LoadAssetAtPath<RuinRail.UI.Theme.UiSkin>(UiSkinPath);
            if (skin == null)
            {
                Directory.CreateDirectory("Assets/Game/Resources");
                skin = ScriptableObject.CreateInstance<RuinRail.UI.Theme.UiSkin>();
                AssetDatabase.CreateAsset(skin, UiSkinPath);
            }

            var shelter = AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtRoot}/UI/ui_shelter_backdrop.png");
            var menu = AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtRoot}/UI/ui_menu_backdrop.png");

            // The station sheet slices left to right in BaseStation order; the sub-sprite suffix is the column index.
            var stations = AssetDatabase.LoadAllAssetsAtPath($"{ArtRoot}/UI/ui_shelter_stations.png")
                .OfType<Sprite>()
                .OrderBy(s => s.name, StringComparer.Ordinal)
                .ToArray();

            skin.EditorSet(shelter, menu, stations);
            BindInventoryFrames(skin);
            BindDashIcon(skin);
            BindHudSprites(skin);
            EditorUtility.SetDirty(skin);
            RuinRail.UI.Theme.UiSkin.InvalidateCache();

            if (shelter == null || menu == null)
                Debug.LogError("UI skin bound with a missing backdrop; the front-end will fall back to a flat background.");
            else
                Debug.Log($"UI skin bound: shelter + menu backdrops and {stations.Length} station icons.");
        }

        /// <summary>
        /// Points the skin at the generated 9-sliced panel, slot and rarity frames (spec 18) the graphical inventory
        /// draws with. Sprites stay at their convention path; the skin only references them.
        /// </summary>
        public static void BindInventoryFrames(RuinRail.UI.Theme.UiSkin skin)
        {
            Sprite Load(string stem) => AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtRoot}/UI/{stem}.png");
            var rarity = new[] { "common", "uncommon", "rare", "epic", "legendary" }.Select(r => Load("ui_rarity_frame_" + r)).ToArray();
            skin.EditorSetFrames(Load("ui_panel_frame"), Load("ui_panel_emphasis"), Load("ui_inventory_slot"), rarity);
            if (!skin.HasInventoryFrames) Debug.LogError("UI skin: inventory frames are incomplete; run RuinRail/Art/Generate All Final Art.");
        }

        /// <summary>Points the skin at the generated HUD dash icon (91 Player State); the sprite stays at its convention path.</summary>
        public static void BindDashIcon(RuinRail.UI.Theme.UiSkin skin)
        {
            var icon = AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtRoot}/UI/ui_dash_icon.png");
            skin.EditorSetDashIcon(icon);
            if (icon == null) Debug.LogError("UI skin: no dash icon at Art/UI/ui_dash_icon.png; run RuinRail/Art/Generate Missing UI Sprites.");
        }

        /// <summary>Points the skin at the generated coin token and low-HP vignette (91 Top Information / Player State).</summary>
        public static void BindHudSprites(RuinRail.UI.Theme.UiSkin skin)
        {
            var coin = AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtRoot}/UI/ui_coin_icon.png");
            var vignette = AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtRoot}/UI/ui_vignette_low_hp.png");
            skin.EditorSetHudSprites(coin, vignette);
            if (coin == null || vignette == null)
                Debug.LogError("UI skin: coin icon / low-HP vignette missing under Art/UI; run RuinRail/Art/Generate Missing UI Sprites.");
        }

        /// <summary>
        /// Writes only the UI sprites that do not exist yet (the HUD dash icon), applies their import contract, binds
        /// them into the skin and records their provenance, leaving every accepted sprite untouched.
        /// </summary>
        [MenuItem("RuinRail/Art/Generate Missing UI Sprites")]
        public static void GenerateMissingUiSprites()
        {
            Pending.Clear();
            var written = 0;
            void WriteIfMissing(string stem, Func<PixelCanvas> draw)
            {
                var path = $"{ArtRoot}/UI/{stem}.png";
                if (File.Exists(path)) return;
                WriteSprite(draw(), path, 1);
                written++;
            }

            WriteIfMissing("ui_dash_icon", () => UiFactory.DashIcon());
            WriteIfMissing("ui_coin_icon", () => UiFactory.CoinIcon());
            WriteIfMissing("ui_vignette_low_hp", () => UiFactory.LowHealthVignette());

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ApplyPendingImports();
            var skin = AssetDatabase.LoadAssetAtPath<RuinRail.UI.Theme.UiSkin>(UiSkinPath);
            if (skin == null) throw new InvalidOperationException("No UI skin at " + UiSkinPath);
            BindDashIcon(skin);
            BindHudSprites(skin);
            EditorUtility.SetDirty(skin);
            AssetDatabase.SaveAssets();
            RuinRail.UI.Theme.UiSkin.InvalidateCache();
            ProvenanceRecorder.Record();
            Debug.Log($"Generated {written} missing UI sprite(s); dash icon bound = {skin.DashIcon != null}, coin = {skin.CoinIcon != null}, vignette = {skin.LowHealthVignette != null}.");
        }

        public static void GenerateMissingUiSpritesBatch()
        {
            try
            {
                GenerateMissingUiSprites();
                var skin = AssetDatabase.LoadAssetAtPath<RuinRail.UI.Theme.UiSkin>(UiSkinPath);
                var ok = skin != null && skin.DashIcon != null && skin.CoinIcon != null && skin.LowHealthVignette != null;
                EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (Exception e) { Debug.LogError(e); EditorApplication.Exit(1); }
        }

        /// <summary>Batch entry: binds only the inventory frames into the existing skin (backdrops, stations and cursors untouched).</summary>
        public static void BindInventoryFramesBatch()
        {
            try
            {
                var skin = AssetDatabase.LoadAssetAtPath<RuinRail.UI.Theme.UiSkin>(UiSkinPath);
                if (skin == null) throw new InvalidOperationException("No UI skin at " + UiSkinPath);
                BindInventoryFrames(skin);
                EditorUtility.SetDirty(skin);
                AssetDatabase.SaveAssets();
                RuinRail.UI.Theme.UiSkin.InvalidateCache();
                EditorApplication.Exit(skin.HasInventoryFrames ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Creates a Tile asset per biome tile sprite and repaints every room prefab's tilemaps from the placeholder
        /// tiles onto the biome's final set. Room logic, layout and collider types are untouched: only the Tile the
        /// cell points at changes.
        /// </summary>
        public static void BindTiles()
        {
            var tiles = new Dictionary<(TileFactory.Biome, TileRole, int), Tile>();

            foreach (TileFactory.Biome biome in Enum.GetValues(typeof(TileFactory.Biome)))
            foreach (TileRole role in Enum.GetValues(typeof(TileRole)))
            for (var variant = 0; variant < TileFactory.VariantCount(role); variant++)
            {
                var stem = TileStem(biome, role, variant);
                var spritePath = $"{ArtRoot}/Tiles/{biome}/{stem}.png";
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
                if (sprite == null) continue;

                var tilePath = $"{ArtRoot}/Tiles/{biome}/{stem}.asset";
                var tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
                if (tile == null)
                {
                    tile = ScriptableObject.CreateInstance<Tile>();
                    tile.name = stem;
                    AssetDatabase.CreateAsset(tile, tilePath);
                }

                tile.sprite = sprite;
                tile.color = Color.white;
                // Collider type must match what the placeholder provided, or room collision changes. Every member of
                // a floor family is floor, so they all keep ColliderType.None regardless of how they look.
                tile.colliderType = role is TileRole.Wall or TileRole.Obstacle ? Tile.ColliderType.Grid : Tile.ColliderType.None;
                EditorUtility.SetDirty(tile);
                tiles[(biome, role, variant)] = tile;
            }

            AssetDatabase.SaveAssets();
            RoomTileRepainter.RepaintAll(tiles);
        }

        /// <summary>Binds each generated icon to its ItemDefinition through the TASK-185-C icon field.</summary>
        public static void BindItemIcons()
        {
            var bound = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:ItemDefinition"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (item == null || string.IsNullOrEmpty(item.Id)) continue;

                var iconPath = $"{ArtRoot}/Icons/Items/{item.Id}.png";
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
                if (sprite == null) continue;

                item.EditorSetIcon(sprite);
                bound++;
            }

            Debug.Log($"Bound {bound} item icons.");
        }
    }
}

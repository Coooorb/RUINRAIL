using System.IO;
using RuinRail.Dungeon.Grid;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuinRail.EditorTools.Rooms
{
    /// <summary>
    /// Generates flat-colour 32x32 placeholder tiles (PPU 32) for grid fixtures and room authoring until final
    /// pixel art arrives. Placeholders are dev art only and are replaced asset-for-asset later.
    /// </summary>
    public static class PlaceholderTileAuthoring
    {
        public const string TileFolder = "Assets/Game/Art/Tiles/_Placeholder";

        public static Tile GetOrCreateTile(string name, Color color, Tile.ColliderType colliderType)
        {
            Directory.CreateDirectory(TileFolder);
            var texturePath = $"{TileFolder}/{name}.png";
            var tilePath = $"{TileFolder}/{name}.asset";

            if (!File.Exists(texturePath))
            {
                var texture = new Texture2D(GridConstants.TileSizePixels, GridConstants.TileSizePixels, TextureFormat.RGBA32, false);
                var pixels = new Color[GridConstants.TileSizePixels * GridConstants.TileSizePixels];
                for (var i = 0; i < pixels.Length; i++)
                {
                    var x = i % GridConstants.TileSizePixels;
                    var y = i / GridConstants.TileSizePixels;
                    var border = x == 0 || y == 0 || x == GridConstants.TileSizePixels - 1 || y == GridConstants.TileSizePixels - 1;
                    pixels[i] = border ? color * 0.8f : color;
                    pixels[i].a = 1f;
                }

                texture.SetPixels(pixels);
                texture.Apply();
                File.WriteAllBytes(texturePath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            if (importer.textureType != TextureImporterType.Sprite || importer.spritePixelsPerUnit != GridConstants.PixelsPerUnit || importer.filterMode != FilterMode.Point)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = GridConstants.PixelsPerUnit;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(texturePath);
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                tile.name = name;
                AssetDatabase.CreateAsset(tile, tilePath);
            }

            tile.sprite = sprite;
            tile.color = Color.white;
            tile.colliderType = colliderType;
            EditorUtility.SetDirty(tile);
            AssetDatabase.SaveAssets();
            return tile;
        }
    }
}

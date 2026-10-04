using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RuinRail.Dungeon.Runtime
{
    /// <summary>
    /// The pixels of a 32x32 floor-tile sprite, for the environment layer's seam repair. Tile textures are imported
    /// non-readable, so the texture is copied once through a temporary sRGB render target and read back; the result
    /// is cached per sprite for the rest of the session. Without a graphics device there is nothing to read (and
    /// nothing to see), so <see cref="Available"/> is false and the caller skips the repair.
    /// </summary>
    internal static class RoomEnvironmentTileReader
    {
        private static readonly Dictionary<int, Color32[]> Textures = new();
        private static readonly Dictionary<int, Color32[]> Sprites = new();

        public static bool Available => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        /// <summary>The sprite's 32x32 pixels, bottom row first, or null when they cannot be read.</summary>
        public static Color32[] Read(Sprite sprite)
        {
            if (sprite == null || !Available) return null;
            if (Sprites.TryGetValue(sprite.GetInstanceID(), out var cached)) return cached;
            var rect = sprite.textureRect;
            var size = RoomEnvironmentMap.Tile;
            Color32[] result = null;
            if (Mathf.RoundToInt(rect.width) == size && Mathf.RoundToInt(rect.height) == size)
            {
                var texture = sprite.texture;
                var full = ReadTexture(texture);
                if (full != null)
                {
                    result = new Color32[size * size];
                    int ox = Mathf.RoundToInt(rect.x), oy = Mathf.RoundToInt(rect.y);
                    for (var y = 0; y < size; y++)
                    for (var x = 0; x < size; x++)
                        result[y * size + x] = full[(oy + y) * texture.width + ox + x];
                }
            }

            Sprites[sprite.GetInstanceID()] = result;
            return result;
        }

        private static Color32[] ReadTexture(Texture2D texture)
        {
            if (texture == null) return null;
            if (Textures.TryGetValue(texture.GetInstanceID(), out var cached)) return cached;
            Color32[] pixels;
            if (texture.isReadable)
            {
                pixels = texture.GetPixels32();
            }
            else
            {
                var target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var previous = RenderTexture.active;
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, false);
                copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0, false);
                copy.Apply(false);
                pixels = copy.GetPixels32();
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                if (Application.isPlaying) Object.Destroy(copy);
                else Object.DestroyImmediate(copy);
            }

            Textures[texture.GetInstanceID()] = pixels;
            return pixels;
        }
    }
}

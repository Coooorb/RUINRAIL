using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// Assembles the generated glyphs into a real Unity <see cref="Font"/> asset.
    ///
    /// This is what removes the release dependency on Unity's builtin `LegacyRuntime.ttf` that
    /// FINAL_ART_PRODUCTION_SPEC D7 and 29.12 require gone. A custom bitmap Font is the right construction here: the
    /// typeface is original project work with no licence question, and a bitmap face renders at exact pixel sizes
    /// without the hinting blur a scaled TTF would introduce at 640x360.
    /// </summary>
    public static class PixelFontAssetBuilder
    {
        // The font lives under Resources so UiKit can load it at runtime without a scene reference.
        public const string FontFolder = "Assets/Game/Resources/Fonts";
        public const string AtlasPath = FontFolder + "/ruinrail_pixel_atlas.png";
        public const string MaterialPath = FontFolder + "/ruinrail_pixel.mat";
        public const string FontPath = FontFolder + "/ruinrail_pixel.fontsettings";

        /// <summary>The size the face is authored at. Text renders 1:1 at this size and integer multiples of it.</summary>
        public const int FontSize = PixelFontFactory.GlyphHeight;

        public static Font Build()
        {
            Directory.CreateDirectory(FontFolder);

            var atlas = PixelFontFactory.BuildAtlas(out var rects);
            File.WriteAllBytes(AtlasPath, atlas.ToPng());
            AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceSynchronousImport);

            // The atlas is a font texture, not a sprite: no compression, point filtering, alpha preserved.
            var importer = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
            importer.textureType = TextureImporterType.Default;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("GUI/Text Shader"));
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.mainTexture = texture;
            EditorUtility.SetDirty(material);

            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null)
            {
                font = new Font("RUINRAIL Pixel");
                AssetDatabase.CreateAsset(font, FontPath);
            }

            font.material = material;

            var characters = new List<CharacterInfo>();
            foreach (var ch in PixelFontFactory.Charset)
            {
                if (!rects.TryGetValue(ch, out var r)) continue;

                // UVs are the glyph's rect in atlas space. Unity's CharacterInfo uses a bottom-left origin, and the
                // vertical rect is flipped so the glyph is not drawn upside down.
                // The quad spans the glyph plus its gutter column, and the UV stops half a texel short of the cell
                // boundary. Without that inset the right edge of a quad can sample the first column of the *next*
                // glyph: invisible between letters, because the following quad abuts and paints over it, but plainly
                // visible as a stray vertical tick after the last letter of every line.
                const float inset = 0.5f;
                var uv = new Rect(
                    r.x / (float)texture.width,
                    r.y / (float)texture.height,
                    (r.width + PixelFontFactory.AtlasPadding - inset) / texture.width,
                    r.height / (float)texture.height);

                characters.Add(new CharacterInfo
                {
                    index = ch,
                    advance = PixelFontFactory.Advance,
                    // Glyph quad: origin at the baseline, extending up by the glyph height.
                    //
                    // The quad is one pixel wider than the glyph art, taking in the transparent gutter column of its
                    // atlas cell. That makes the quad exactly one advance wide, so consecutive quads abut instead of
                    // overlapping, and a sampling error at the quad's right edge lands on the gutter rather than on
                    // the first column of the next letter.
                    minX = 0,
                    maxX = PixelFontFactory.GlyphWidth + PixelFontFactory.AtlasPadding,
                    minY = -1,
                    maxY = PixelFontFactory.GlyphHeight - 1,
                    uvBottomLeft = new Vector2(uv.xMin, uv.yMin),
                    uvBottomRight = new Vector2(uv.xMax, uv.yMin),
                    uvTopLeft = new Vector2(uv.xMin, uv.yMax),
                    uvTopRight = new Vector2(uv.xMax, uv.yMax)
                });
            }

            font.characterInfo = characters.ToArray();

            ApplyFaceMetrics(font);

            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();

            Debug.Log($"Built RUINRAIL pixel font: {characters.Count} glyphs, atlas {texture.width}x{texture.height}, " +
                      $"font size {FontSize}, line spacing {PixelFontFactory.LineHeight}.");
            return font;
        }

        public static Font Load() => AssetDatabase.LoadAssetAtPath<Font>(FontPath);

        /// <summary>
        /// Writes the face-wide metrics a bitmap font needs and that the public <see cref="Font"/> API does not expose.
        ///
        /// <c>Font.lineHeight</c> and <c>Font.fontSize</c> are read-only, so a font built purely through the public
        /// API ships with <c>m_LineSpacing = 0.1</c> and <c>m_FontSize = 0</c>: every line of a multi-line label then
        /// draws a tenth of a pixel below the one above it, i.e. all of them on top of each other. That is what made
        /// the Shelter profile block render three lines as one smear of overlapping text. The values are the ones the
        /// glyphs were actually authored at, so a line box in the UI is the same height the layout measures.
        /// </summary>
        private static void ApplyFaceMetrics(Font font)
        {
            var serialized = new SerializedObject(font);
            serialized.FindProperty("m_LineSpacing").floatValue = PixelFontFactory.LineHeight;
            serialized.FindProperty("m_FontSize").floatValue = FontSize;

            // The ascent is what Unity measures the first baseline down from. Left at zero — which is what a Font
            // built purely through the public API ships with — the baseline lands on the top edge of the text
            // rectangle and the whole line is drawn above the box the layout assigned it. Writing the authored cap
            // height here puts the glyphs inside their rectangle, so the measured layout and the rendered one agree.
            var ascent = serialized.FindProperty("m_Ascent");
            if (ascent != null) ascent.floatValue = PixelFontFactory.GlyphHeight;
            else Debug.LogWarning("Font has no m_Ascent property in this Unity version; UiKit compensates at layout time instead.");
            // The glyph rects already carry one pixel of separation in the atlas; padding it again would double the
            // gap between characters and break the measured advance the text budgets are built on.
            serialized.FindProperty("m_CharacterPadding").intValue = 0;
            serialized.FindProperty("m_CharacterSpacing").intValue = 0;
            serialized.FindProperty("m_Tracking").floatValue = 1f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}

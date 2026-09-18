using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace RuinRail.Tests
{
    /// <summary>
    /// Captures a live expedition frame — the real dungeon world through the real camera rig's framing, with the
    /// real HUD canvases over it — to a PNG at the 640x360 reference resolution.
    ///
    /// A capture camera is placed exactly where the rig put the gameplay camera and renders the world into a
    /// 640x360 render texture at the rig's orthographic size, so one captured pixel is one reference pixel. The
    /// screen-space canvases are switched to that camera for the duration (overlay canvases bypass cameras and
    /// cannot be captured otherwise) and restored afterwards, the same way <see cref="UiScreenCapture"/> does for the
    /// menus. Nothing about the scene is mocked: what lands in the PNG is what the player would see.
    /// </summary>
    public static class LiveDungeonCapture
    {
        public const int Width = 640;
        public const int Height = 360;
        public const string Folder = "TestResults/RegressionProof";

        public sealed class Result
        {
            public string Path = string.Empty;
            public Color32[] Pixels = System.Array.Empty<Color32>();
            /// <summary>Camera world position at capture; world→pixel conversions use it.</summary>
            public Vector2 CameraPosition;
            public float OrthographicSize;
            public int PixelsPerUnit;

            public Color32 At(int x, int y) => Pixels[Mathf.Clamp(y, 0, Height - 1) * Width + Mathf.Clamp(x, 0, Width - 1)];

            /// <summary>World position → capture pixel (origin bottom-left, matching <see cref="Texture2D.GetPixels32"/>).</summary>
            public Vector2Int WorldToPixel(Vector2 world)
            {
                var pixelsPerUnit = Height / (2f * OrthographicSize);
                var offset = (world - CameraPosition) * pixelsPerUnit;
                return new Vector2Int(Mathf.RoundToInt(Width * 0.5f + offset.x), Mathf.RoundToInt(Height * 0.5f + offset.y));
            }

            /// <summary>Count of pixels inside a rect (pixel space, bottom-left origin) that are not near-black.</summary>
            public int CountLit(RectInt rect, byte threshold = 24)
            {
                var count = 0;
                for (var y = rect.yMin; y < rect.yMax; y++)
                for (var x = rect.xMin; x < rect.xMax; x++)
                {
                    if (x < 0 || y < 0 || x >= Width || y >= Height) continue;
                    var p = At(x, y);
                    if (p.r > threshold || p.g > threshold || p.b > threshold) count++;
                }

                return count;
            }

            /// <summary>Share of pixels inside a rect that are near-black (the void).</summary>
            public float BlackShare(RectInt rect, byte threshold = 24)
            {
                var total = Mathf.Max(1, rect.width * rect.height);
                return 1f - CountLit(rect, threshold) / (float)total;
            }
        }

        /// <summary>The frame the gameplay camera is showing: its position, the reference orthographic size, the HUD over it.</summary>
        public static Result Capture(string name, Camera worldCamera, int pixelsPerUnit, bool includeUi = true) =>
            Capture(Folder, name, worldCamera, pixelsPerUnit, includeUi);

        /// <summary>Same, into a caller-chosen folder.</summary>
        public static Result Capture(string folder, string name, Camera worldCamera, int pixelsPerUnit, bool includeUi = true)
        {
            var position = worldCamera != null ? (Vector2)worldCamera.transform.position : Vector2.zero;
            return Capture(folder, name, worldCamera, position, Height / (2f * pixelsPerUnit), pixelsPerUnit, includeUi);
        }

        /// <summary>An explicitly framed view of the world (e.g. the whole dungeon zoomed out, or a close-up of one doorway).</summary>
        public static Result Capture(string name, Camera worldCamera, Vector2 position, float orthographicSize, int pixelsPerUnit, bool includeUi) =>
            Capture(Folder, name, worldCamera, position, orthographicSize, pixelsPerUnit, includeUi);

        public static Result Capture(string folder, string name, Camera worldCamera, Vector2 position, float orthographicSize, int pixelsPerUnit, bool includeUi)
        {
            Directory.CreateDirectory(folder);

            var canvases = includeUi ? Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None) : new Canvas[0];
            System.Array.Sort(canvases, (a, b) => a.sortingOrder.CompareTo(b.sortingOrder));

            var cameraGo = new GameObject("LiveCaptureCamera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = orthographicSize;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = worldCamera != null ? worldCamera.backgroundColor : Color.black;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 100f;
            camera.cullingMask = worldCamera != null ? worldCamera.cullingMask : -1;
            cameraGo.transform.position = new Vector3(position.x, position.y, -10f);

            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            camera.targetTexture = target;

            var restore = new (Canvas canvas, RenderMode mode, Camera cam, float distance, int layer, CanvasScaler scaler, CanvasScaler.ScaleMode scaleMode, float factor)[canvases.Length];
            for (var i = 0; i < canvases.Length; i++)
            {
                var scaler = canvases[i].GetComponent<CanvasScaler>();
                restore[i] = (canvases[i], canvases[i].renderMode, canvases[i].worldCamera, canvases[i].planeDistance, canvases[i].sortingLayerID,
                    scaler, scaler != null ? scaler.uiScaleMode : default, scaler != null ? scaler.scaleFactor : 1f);
                canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
                canvases[i].worldCamera = camera;
                canvases[i].planeDistance = 1f + (canvases.Length - 1 - i) * 0.01f;
                // An overlay canvas drops its sorting layer (Unity ignores the field in that mode), so in camera mode it
                // would land on Default — underneath every world layer. Screen UI is the topmost approved layer.
                canvases[i].sortingLayerName = RuinRail.Core.Rendering.SortingLayers.ScreenUI;
                if (scaler != null)
                {
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                    scaler.scaleFactor = 1f;
                }
            }

            Canvas.ForceUpdateCanvases();

            var result = new Result
            {
                Path = Path.Combine(folder, name + ".png"),
                CameraPosition = position,
                OrthographicSize = camera.orthographicSize,
                PixelsPerUnit = pixelsPerUnit
            };
            try
            {
                camera.Render();

                var previous = RenderTexture.active;
                RenderTexture.active = target;
                var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                texture.Apply();
                RenderTexture.active = previous;

                File.WriteAllBytes(result.Path, texture.EncodeToPNG());
                result.Pixels = texture.GetPixels32();

                var zoomed = Zoom(texture, 2);
                File.WriteAllBytes(Path.Combine(folder, name + "_x2.png"), zoomed.EncodeToPNG());
                Object.DestroyImmediate(zoomed);
                Object.DestroyImmediate(texture);
            }
            finally
            {
                foreach (var (canvas, mode, cam, distance, layer, scaler, scaleMode, factor) in restore)
                {
                    if (canvas == null) continue;
                    canvas.renderMode = mode;
                    canvas.worldCamera = cam;
                    canvas.planeDistance = distance;
                    canvas.sortingLayerID = layer;
                    if (scaler == null) continue;
                    scaler.uiScaleMode = scaleMode;
                    scaler.scaleFactor = factor;
                }

                camera.targetTexture = null;
                Object.DestroyImmediate(cameraGo);
                target.Release();
                Object.DestroyImmediate(target);
            }

            return result;
        }

        private static Texture2D Zoom(Texture2D source, int factor)
        {
            var zoomed = new Texture2D(source.width * factor, source.height * factor, TextureFormat.RGBA32, false);
            var pixels = source.GetPixels32();
            var output = new Color32[zoomed.width * zoomed.height];
            for (var y = 0; y < zoomed.height; y++)
            for (var x = 0; x < zoomed.width; x++)
                output[y * zoomed.width + x] = pixels[y / factor * source.width + x / factor];
            zoomed.SetPixels32(output);
            zoomed.Apply();
            return zoomed;
        }
    }
}

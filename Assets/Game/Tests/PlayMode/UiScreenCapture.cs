using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace RuinRail.Tests
{
    /// <summary>
    /// Captures a live menu canvas to a PNG at the 640x360 reference resolution.
    ///
    /// It renders the real screen — the one the screens' own builders produced from the real view models — rather
    /// than a mock of it. That is the whole point: the polish pass is judged on screenshots, and a screenshot of a
    /// re-implementation of the UI would prove nothing about the UI.
    ///
    /// The canvas is temporarily switched from Screen Space Overlay to Screen Space Camera and pointed at an
    /// orthographic camera with a 640x360 render texture. Overlay canvases bypass every camera and cannot be captured
    /// any other way; the camera path also fixes the output at the reference resolution instead of at whatever size
    /// the batch-mode window happens to be, which is what makes the captures comparable to the layout arithmetic.
    /// The canvas is put back exactly as it was afterwards.
    /// </summary>
    public static class UiScreenCapture
    {
        public const int Width = 640;
        public const int Height = 360;
        public const string Folder = "TestResults/PolishPreview";

        /// <summary>
        /// A capture plus the pixels behind it, so a test can check the image rather than trust that a file exists.
        /// </summary>
        public sealed class Result
        {
            public string Path = string.Empty;
            public Color32[] Pixels = System.Array.Empty<Color32>();

            /// <summary>Share of the frame taken by the single most common colour. A blank screen scores near 1.</summary>
            public float DominantColourShare()
            {
                if (Pixels.Length == 0) return 1f;
                var counts = new Dictionary<int, int>();
                foreach (var p in Pixels)
                {
                    var key = (p.r << 16) | (p.g << 8) | p.b;
                    counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
                }

                var top = 0;
                foreach (var n in counts.Values) if (n > top) top = n;
                return top / (float)Pixels.Length;
            }

            /// <summary>How many distinct colours the frame carries — a blank or broken render carries very few.</summary>
            public int DistinctColours()
            {
                var seen = new HashSet<int>();
                foreach (var p in Pixels) seen.Add((p.r << 16) | (p.g << 8) | p.b);
                return seen.Count;
            }

            /// <summary>
            /// The largest axis-aligned block of one flat colour, as a share of the frame.
            ///
            /// This is the "giant blank black rectangle" check from section A4: a screen that leaves a content area
            /// empty shows up here as one enormous uniform region, however busy the rest of the frame is.
            /// </summary>
            public float LargestFlatBlockShare(int blockSize = 16)
            {
                if (Pixels.Length == 0) return 1f;
                var columns = Width / blockSize;
                var rows = Height / blockSize;
                var flat = new bool[columns * rows];

                for (var by = 0; by < rows; by++)
                for (var bx = 0; bx < columns; bx++)
                {
                    var first = Pixels[by * blockSize * Width + bx * blockSize];
                    var uniform = true;
                    for (var y = 0; y < blockSize && uniform; y++)
                    for (var x = 0; x < blockSize && uniform; x++)
                    {
                        var p = Pixels[(by * blockSize + y) * Width + bx * blockSize + x];
                        if (p.r != first.r || p.g != first.g || p.b != first.b) uniform = false;
                    }

                    flat[by * columns + bx] = uniform;
                }

                // Largest connected run of uniform blocks, by flood fill.
                var seen = new bool[flat.Length];
                var best = 0;
                var stack = new Stack<int>();
                for (var i = 0; i < flat.Length; i++)
                {
                    if (!flat[i] || seen[i]) continue;
                    var size = 0;
                    stack.Push(i);
                    seen[i] = true;
                    while (stack.Count > 0)
                    {
                        var at = stack.Pop();
                        size++;
                        var cx = at % columns;
                        var cy = at / columns;
                        foreach (var (nx, ny) in new[] { (cx - 1, cy), (cx + 1, cy), (cx, cy - 1), (cx, cy + 1) })
                        {
                            if (nx < 0 || ny < 0 || nx >= columns || ny >= rows) continue;
                            var n = ny * columns + nx;
                            if (!flat[n] || seen[n]) continue;
                            seen[n] = true;
                            stack.Push(n);
                        }
                    }

                    if (size > best) best = size;
                }

                return best / (float)flat.Length;
            }
        }

        /// <summary>Renders every canvas in the scene, back to front, into one PNG.</summary>
        public static Result Capture(string name)
        {
            Directory.CreateDirectory(Folder);

            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            System.Array.Sort(canvases, (a, b) => a.sortingOrder.CompareTo(b.sortingOrder));

            var cameraGo = new GameObject("UiCaptureCamera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 100f;
            camera.transform.position = new Vector3(0f, 0f, -10f);

            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            camera.targetTexture = target;

            var restore = new (Canvas canvas, RenderMode mode, Camera cam, float distance, CanvasScaler scaler, CanvasScaler.ScaleMode scaleMode, float factor)[canvases.Length];
            for (var i = 0; i < canvases.Length; i++)
            {
                var scaler = canvases[i].GetComponent<CanvasScaler>();
                restore[i] = (canvases[i], canvases[i].renderMode, canvases[i].worldCamera, canvases[i].planeDistance,
                    scaler, scaler != null ? scaler.uiScaleMode : default, scaler != null ? scaler.scaleFactor : 1f);

                canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
                canvases[i].worldCamera = camera;
                // The array is sorted by ascending sorting order, and a larger plane distance is farther from the
                // camera — so the highest sorting order has to take the *smallest* distance. Getting this backwards
                // put the topmost canvas behind everything else, which is not what the player sees.
                canvases[i].planeDistance = 1f + (canvases.Length - 1 - i) * 0.01f;

                // Pin the scaler to 1:1 for the duration of the capture. Scale-with-screen-size would otherwise key
                // off whatever the batch-mode window happens to be, and the whole point of these captures is that
                // one captured pixel is one reference pixel — so they can be compared against the layout arithmetic.
                if (scaler != null)
                {
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                    scaler.scaleFactor = 1f;
                }
            }

            Canvas.ForceUpdateCanvases();

            var result = new Result { Path = Path.Combine(Folder, name + ".png") };
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

                // A doubled copy, because a 640x360 PNG of a pixel UI is hard to read at 100% on a modern display.
                var zoomed = Zoom(texture, 2);
                File.WriteAllBytes(Path.Combine(Folder, name + "_x2.png"), zoomed.EncodeToPNG());
                Object.DestroyImmediate(zoomed);
                Object.DestroyImmediate(texture);
            }
            finally
            {
                foreach (var (canvas, mode, cam, distance, scaler, scaleMode, factor) in restore)
                {
                    if (canvas == null) continue;
                    canvas.renderMode = mode;
                    canvas.worldCamera = cam;
                    canvas.planeDistance = distance;
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

        /// <summary>Nearest-neighbour integer zoom; anything smoother would misrepresent a pixel UI.</summary>
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

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RuinRail.App
{
    /// <summary>
    /// Quiet life over a front-end backdrop, at the exact spots the baked scene has them (<see cref="FrontEndScenes"/>):
    ///   Main Menu — one unreliable emergency lamp that stutters now and then, the Shelter door's indicator breathing,
    ///     dust drifting in the lamp light, and a ceiling leak dripping into a puddle.
    ///   Shelter — the practical lamps breathing (one flickers), the door indicator pulsing, the terminal scrolling with
    ///     a blinking cursor, vapour rising from the generator's exhaust, dust in the lamp cones.
    /// Every element is a few pixels to a small soft glow, at low alpha, never over the UI's own areas' contrast: the
    /// overlays are children of the backdrop, so every panel, button and label still draws on top of them. Driven by
    /// unscaled time (menus may pause the clock); positions snap to whole pixels.
    /// </summary>
    public sealed class FrontEndAmbience : MonoBehaviour
    {
        public enum Place { MainMenu, Shelter }

        private static Sprite _pixel;
        private static Sprite _glow;

        private sealed class Mote
        {
            public Image Image;
            public Vector2 Origin;
            public Vector2 Drift;
            public float Phase;
            public float Period;
            public float MaxAlpha;
        }

        private readonly List<Mote> _motes = new();
        private readonly List<(Image Image, Vector2 Origin, float Phase)> _puffs = new();
        private Place _place;
        private Image _flicker;
        private Image _dim;
        private readonly List<Image> _breathers = new();
        private Image _indicator;
        private Image _drip;
        private Image _splash;
        private Vector2 _dripTop;
        private Vector2 _dripBottom;
        private readonly List<Image> _screenLines = new();
        private Image _cursor;
        private Rect _screen;

        /// <summary>Elements this ambience animates (tests check it is alive and restrained).</summary>
        public int Elements { get; private set; }

        public static FrontEndAmbience Attach(Image backdrop, Place place)
        {
            if (backdrop == null || backdrop.sprite == null) return null;
            var ambience = backdrop.gameObject.GetComponent<FrontEndAmbience>() ?? backdrop.gameObject.AddComponent<FrontEndAmbience>();
            ambience.Build(place);
            return ambience;
        }

        private static Sprite Pixel()
        {
            if (_pixel != null) return _pixel;
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
            t.SetPixel(0, 0, Color.white);
            t.Apply(false, true);
            _pixel = Sprite.Create(t, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            _pixel.hideFlags = HideFlags.DontSave;
            return _pixel;
        }

        /// <summary>A soft radial glow in three flat bands (pixel-art light, not an airbrush).</summary>
        private static Sprite Glow()
        {
            if (_glow != null) return _glow;
            const int s = 32;
            var t = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
            for (var y = 0; y < s; y++)
            for (var x = 0; x < s; x++)
            {
                var d = Mathf.Sqrt((x - 15.5f) * (x - 15.5f) + (y - 15.5f) * (y - 15.5f)) / 16f;
                var a = d >= 1f ? 0f : Mathf.Floor((1f - d) * 3f + 0.4f) / 3f;
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }

            t.Apply(false, true);
            _glow = Sprite.Create(t, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 1f);
            _glow.hideFlags = HideFlags.DontSave;
            return _glow;
        }

        private Image Add(string name, Sprite sprite, Vector2 at, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = Snap(at);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            Elements++;
            return image;
        }

        private static Vector2 Snap(Vector2 v) => new(Mathf.Round(v.x), Mathf.Round(v.y));

        private void Build(Place place)
        {
            _place = place;
            for (var i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
            Elements = 0;
            var amber = new Color(1f, 0.72f, 0.35f, 0f);
            if (place == Place.MainMenu)
            {
                var view = FrontEndScenes.MenuView;
                var lamp = view.Project(FrontEndScenes.MenuLamps[FrontEndScenes.MenuFlickerLamp]);
                _flicker = Add("LampSurge", Glow(), lamp, new Vector2(30, 30), amber);
                _dim = Add("LampDrop", Glow(), lamp, new Vector2(26, 26), new Color(0.04f, 0.05f, 0.06f, 0f));
                _indicator = Add("DoorIndicator", Glow(), view.Project(FrontEndScenes.MenuDoorLamp), new Vector2(24, 14), amber);
                _dripTop = view.Project(FrontEndScenes.MenuDripTop);
                _dripBottom = view.Project(FrontEndScenes.MenuDripPuddle);
                _drip = Add("Drip", Pixel(), _dripTop, new Vector2(1, 2), new Color(0.62f, 0.7f, 0.72f, 0f));
                _splash = Add("Splash", Pixel(), _dripBottom, new Vector2(5, 1), new Color(0.62f, 0.7f, 0.72f, 0f));
                var door = view.Project(new Vector3(0f, 0.2f, FrontEndScenes.MenuEnd - 4f));
                Motes(16, door, new Vector2(60, 40), new Color(1f, 0.8f, 0.5f, 0f), 0.5f);
                foreach (var l in FrontEndScenes.MenuLamps)
                    if (l.z < 20f) Motes(3, view.Project(l + new Vector3(0.6f, -0.4f, 0f)), new Vector2(14, 14), new Color(1f, 0.8f, 0.5f, 0f), 0.45f);
            }
            else
            {
                var view = FrontEndScenes.ShelterView;
                foreach (var l in FrontEndScenes.ShelterLamps) _breathers.Add(Add("LampBreath", Glow(), view.Project(l), new Vector2(26, 26), amber));
                _flicker = _breathers[FrontEndScenes.ShelterFlickerLamp];
                _dim = Add("LampDrop", Glow(), view.Project(FrontEndScenes.ShelterLamps[FrontEndScenes.ShelterFlickerLamp]), new Vector2(22, 22), new Color(0.05f, 0.05f, 0.05f, 0f));
                _indicator = Add("DoorIndicator", Glow(), view.Project(FrontEndScenes.ShelterDoorLamp), new Vector2(30, 14), amber);
                var a = view.Project(FrontEndScenes.ShelterScreenMin);
                var b = view.Project(FrontEndScenes.ShelterScreenMax);
                _screen = Rect.MinMaxRect(Mathf.Min(a.x, b.x) + 2, Mathf.Min(a.y, b.y) + 2, Mathf.Max(a.x, b.x) - 2, Mathf.Max(a.y, b.y) - 2);
                for (var i = 0; i < 3; i++)
                    _screenLines.Add(Add("TerminalLine", Pixel(), new Vector2(_screen.xMin, _screen.yMax - 2 - i * 3), new Vector2(4, 1), new Color(0.55f, 0.95f, 0.6f, 0.55f)));
                _cursor = Add("TerminalCursor", Pixel(), new Vector2(_screen.xMin + 2, _screen.yMin + 2), new Vector2(2, 2), new Color(0.7f, 1f, 0.75f, 0f));
                var stack = view.Project(FrontEndScenes.ShelterExhaust);
                for (var i = 0; i < 6; i++)
                    _puffs.Add((Add("Vapour", Glow(), stack, new Vector2(6, 6), new Color(0.75f, 0.76f, 0.74f, 0f)), stack, i / 6f));
                foreach (var l in FrontEndScenes.ShelterLamps)
                    Motes(6, view.Project(l + new Vector3(0f, -1.1f, 0f)), new Vector2(24, 34), new Color(1f, 0.82f, 0.55f, 0f), 0.45f);
            }
        }

        private void Motes(int count, Vector2 centre, Vector2 spread, Color color, float maxAlpha)
        {
            for (var i = 0; i < count; i++)
            {
                var h = Hash(i, (int)centre.x, (int)centre.y);
                var origin = centre + new Vector2((h - 0.5f) * 2f * spread.x, (Hash(i, 7, (int)centre.x) - 0.5f) * 2f * spread.y);
                _motes.Add(new Mote
                {
                    Image = Add("Mote", Pixel(), origin, Vector2.one, color),
                    Origin = origin,
                    Drift = new Vector2((Hash(i, 3, 5) - 0.5f) * 10f, 4f + Hash(i, 4, 6) * 8f),
                    Phase = Hash(i, 9, 2),
                    Period = 5f + Hash(i, 1, 1) * 5f,
                    MaxAlpha = maxAlpha * (0.5f + 0.5f * Hash(i, 2, 3))
                });
            }
        }

        private static float Hash(int a, int b, int c)
        {
            unchecked
            {
                var h = (uint)(a * 73856093 ^ b * 19349663 ^ c * 83492791);
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return (h & 0xFFFF) / 65535f;
            }
        }

        private static void Alpha(Image image, float a)
        {
            if (image == null) return;
            var c = image.color;
            c.a = Mathf.Clamp01(a);
            image.color = c;
        }

        /// <summary>Stutter: mostly steady, a short burst of drops and surges every few seconds.</summary>
        private static float Stutter(float t, out bool dropped)
        {
            var cycle = t % 7.3f;
            dropped = false;
            if (cycle > 0.9f) return 0f;
            var step = Mathf.FloorToInt(cycle * 14f);
            dropped = step % 3 != 1;
            return dropped ? 0.55f : 0.3f;
        }

        private void Update()
        {
            var t = Time.unscaledTime;

            // The unreliable lamp.
            var flick = Stutter(t, out var dropped);
            if (_place == Place.MainMenu) Alpha(_flicker, dropped ? 0f : flick);
            Alpha(_dim, dropped ? flick : 0f);

            Alpha(_indicator, 0.12f + 0.1f * (0.5f + 0.5f * Mathf.Sin(t * 1.7f)));

            for (var i = 0; i < _breathers.Count; i++)
            {
                if (_breathers[i] == _flicker && dropped) { Alpha(_breathers[i], 0f); continue; }
                Alpha(_breathers[i], 0.08f + 0.04f * Mathf.Sin(t * (0.9f + i * 0.23f) + i * 1.7f));
            }

            foreach (var m in _motes)
            {
                var k = (t / m.Period + m.Phase) % 1f;
                var pos = m.Origin + m.Drift * k + new Vector2(Mathf.Sin((t + m.Phase * 10f) * 0.8f) * 2f, 0f);
                ((RectTransform)m.Image.transform).anchoredPosition = Snap(pos);
                Alpha(m.Image, m.MaxAlpha * Mathf.Sin(k * Mathf.PI));
            }

            if (_drip != null)
            {
                var cycle = t % 3.6f;
                if (cycle < 0.55f)
                {
                    var fall = cycle / 0.55f;
                    ((RectTransform)_drip.transform).anchoredPosition = Snap(Vector2.Lerp(_dripTop, _dripBottom, fall * fall));
                    Alpha(_drip, 0.7f);
                    Alpha(_splash, 0f);
                }
                else
                {
                    Alpha(_drip, 0f);
                    Alpha(_splash, cycle < 0.75f ? 0.55f : 0f);
                }
            }

            foreach (var (image, origin, phase) in _puffs)
            {
                var k = (t / 4.2f + phase) % 1f;
                var rt = (RectTransform)image.transform;
                rt.anchoredPosition = Snap(origin + new Vector2(Mathf.Sin((k + phase) * 6f) * 2f + k * 6f, 2f + k * 26f));
                var size = Mathf.Round(4f + k * 8f);
                rt.sizeDelta = new Vector2(size, size);
                Alpha(image, 0.22f * Mathf.Sin(k * Mathf.PI));
            }

            if (_screenLines.Count > 0)
            {
                var tick = Mathf.FloorToInt(t * 2.5f);
                for (var i = 0; i < _screenLines.Count; i++)
                {
                    var w = 2 + Mathf.FloorToInt(Hash(tick + i, i, 11) * Mathf.Max(2f, _screen.width - 4f));
                    var rt = (RectTransform)_screenLines[i].transform;
                    rt.sizeDelta = new Vector2(w, 1);
                    rt.anchoredPosition = Snap(new Vector2(_screen.xMin + w * 0.5f, _screen.yMax - 1 - ((i + tick) % 4) * 3));
                }

                Alpha(_cursor, Mathf.FloorToInt(t * 2f) % 2 == 0 ? 0.8f : 0f);
            }
        }
    }
}

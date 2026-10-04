using System.Collections.Generic;
using RuinRail.Core;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Loot;
using UnityEngine;
using static RuinRail.Dungeon.Runtime.RoomEnvironmentCanvas;

namespace RuinRail.Dungeon.Runtime
{
    /// <summary>
    /// The non-combat room interactables, drawn at runtime per biome and state in the same three-quarter, outlined,
    /// grounded construction as the chests and the dungeon props: the dungeon merchant's stall, and each event object —
    /// Medical Station, Weapon Cache, Cursed Chest, Locked Vault, Broken Machine, Supply Signal, Secure Relay. Each has
    /// an idle (usable) look with its light on, and where the event has them an active look (Cursed Chest prised open
    /// with its glow pouring out, Supply Signal beacon transmitting) and a used look (lights off, emptied, opened,
    /// repaired or burnt out); the Secure Relay keeps its secured variant. Biome sets the body material.
    ///
    /// Presentation only: it swaps sprites through <see cref="WorldObjectVisual.SetSkin"/> under the same art keys plus
    /// a state suffix; prompts, colliders, rules and rewards are untouched. Pivot (0.5, 0.12) and PPU 32 match the art it
    /// replaces, so every object stands exactly where it stood. Sprites are pure functions of (kind, biome, state),
    /// identical on every peer, painted once per session.
    /// </summary>
    public static class InteractableArt
    {
        public const string Active = "_active";
        public const string Used = "_used";
        public const string Failed = "_failed";
        public const string Secured = "_secured";
        private const float PivotY = 0.12f;

        private static readonly Dictionary<(string, Biome), Sprite> Cache = new();

        /// <summary>Draws a merchant or event object with the new art from here on, in every state.</summary>
        public static void Apply(WorldObjectVisual visual, Biome biome)
        {
            if (visual == null) return;
            if (visual.Renderer != null) visual.Renderer.color = Color.white;
            visual.SetSkin(key => For(key, biome));
        }

        /// <summary>The sprite for a merchant/event art key (with an optional state suffix); null for any other key.</summary>
        public static Sprite For(string key, Biome biome)
        {
            if (string.IsNullOrEmpty(key)) return null;
            var baseKey = key;
            var state = string.Empty;
            foreach (var suffix in new[] { Active, Used, Failed, Secured })
                if (key.EndsWith(suffix)) { baseKey = key.Substring(0, key.Length - suffix.Length); state = suffix; break; }
            if (baseKey != WorldObjectArt.DungeonMerchant && !baseKey.StartsWith(WorldObjectArt.EventPrefix)) return null;
            if (Cache.TryGetValue((key, biome), out var cached) && cached != null) return cached;
            var kind = baseKey == WorldObjectArt.DungeonMerchant ? "Merchant" : baseKey.Substring(WorldObjectArt.EventPrefix.Length);
            var (w, h) = SizeOf(kind);
            var canvas = new RoomEnvironmentCanvas(w, h);
            var painter = new InteractablePainter(canvas, biome, Mathf.RoundToInt(h * PivotY));
            if (!painter.Paint(kind, state)) return null;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
            {
                name = $"Interactable_{kind}{state}_{biome}", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
            };
            texture.SetPixels32(canvas.Pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, PivotY), 32f, 0, SpriteMeshType.FullRect);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.DontSave;
            return Cache[(key, biome)] = sprite;
        }

        private static (int W, int H) SizeOf(string kind) => kind switch
        {
            "Merchant" => (48, 52),
            "MedicalStation" => (36, 46),
            "WeaponCache" => (44, 36),
            "CursedChest" => (38, 38),
            "LockedVault" => (44, 46),
            "BrokenMachine" => (44, 40),
            "SupplySignal" => (34, 52),
            "SecureRelay" => (34, 52),
            _ => (40, 40)
        };

        /// <summary>The state suffix an event's phase is drawn with.</summary>
        public static string StateFor(IDungeonEvent instance)
        {
            if (instance == null) return string.Empty;
            return instance.Phase switch
            {
                DungeonEventPhase.InProgress => Active,
                DungeonEventPhase.Completed => Used,
                DungeonEventPhase.Failed => Failed,
                _ => string.Empty
            };
        }
    }

    /// <summary>
    /// Keeps an event object's sprite on its state (available / active / used / failed) from the event's own phase, so
    /// a revisit, a co-op mirror or a restore all read right without another hook. The Secure Relay is per-member and
    /// keeps its own secured swap.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractableStatePresenter : MonoBehaviour
    {
        private IDungeonEvent _event;
        private WorldObjectVisual _visual;
        private string _baseKey;

        public string State { get; private set; } = string.Empty;

        public void Bind(IDungeonEvent instance, WorldObjectVisual visual, string baseKey)
        {
            _event = instance;
            _visual = visual;
            _baseKey = baseKey;
            Sync();
        }

        private void LateUpdate() => Sync();

        private void Sync()
        {
            if (_event == null || _visual == null) return;
            var state = InteractableArt.StateFor(_event);
            if (state == State && _visual.Key == _baseKey + state) return;
            State = state;
            _visual.Show(_baseKey + state);
        }
    }

    internal sealed class InteractablePainter
    {
        private readonly RoomEnvironmentCanvas _c;
        private readonly PropPainter _q;
        private readonly Biome _biome;
        private readonly int _base;
        private readonly Color32 _dark, _mid, _light, _front;
        private static readonly Color32 SteelDark = Hex("#2E3438"), Steel = Hex("#5E676B"), SteelLight = Hex("#949DA1");
        private static readonly Color32 Amber = Hex("#FFB347"), Green = Hex("#7CE08A"), Cyan = Hex("#7FE6F0"), Violet = Hex("#B87CFF");
        private static readonly Color32 Off = Hex("#2A2F31");

        public InteractablePainter(RoomEnvironmentCanvas c, Biome biome, int baseline)
        {
            _c = c;
            _biome = biome;
            _base = baseline;
            _q = new PropPainter(c, Hex("#101315"), Hex("#050607"), (uint)((int)biome * 97 + 13));
            (_dark, _mid, _light, _front) = biome switch
            {
                Biome.Rustworks => (Hex("#45241A"), Hex("#7A4229"), Hex("#9E5D38"), Hex("#5A301E")),
                Biome.OvergrownLabs => (Hex("#7C8078"), Hex("#B7BBB1"), Hex("#D8DBD2"), Hex("#9A9E94")),
                Biome.CryoVaults => (Hex("#202A33"), Hex("#34424D"), Hex("#4C5E6C"), Hex("#28343E")),
                _ => (Hex("#2E3840"), Hex("#4A5864"), Hex("#6A7B88"), Hex("#3A4650"))
            };
        }

        private void Lamp(int x, int y, Color32 c, bool on, float glow = 0.25f, int size = 2)
        {
            _q.Rect(x, y, size, size, on ? c : Off);
            if (on && glow > 0f) _q.Glow(x + size / 2, y + size / 2, 6f + size * 2f, c, glow);
        }

        private void BiomeDetail(int x0, int y0, int w, int h)
        {
            switch (_biome)
            {
                case Biome.CryoVaults: _q.Frost(x0, y0 + h / 2, w, h / 2, Hex("#8FB0BE"), Hex("#E2F3F8")); break;
                case Biome.OvergrownLabs: _q.Vines(x0, y0, w / 2, h / 2, Hex("#2C4524"), Hex("#5F8A3A")); break;
                case Biome.Rustworks:
                    for (var x = x0 + 2; x < x0 + w - 2; x += 5) for (var k = 0; k < 3 + (x % 4); k++) _q.Px(x, y0 + h - 3 - k, Hex("#4A2A1A"));
                    break;
            }
        }

        public bool Paint(string kind, string state)
        {
            var b = _base;
            var w = _c.Width;
            switch (kind)
            {
                case "Merchant":
                {
                    // A trader's stall: counter with goods, canvas awning on two poles, a lantern.
                    _q.Rect(5, b, 2, 34, SteelDark);
                    _q.Rect(w - 7, b, 2, 34, SteelDark);
                    _q.Block(4, b, w - 8, 18, 10, _dark, _mid, _light, _front);
                    _q.Rect(6, b + 3, w - 12, 2, Hex("#8A6A2A"));
                    // Goods on the counter: crates, an ammo tin, bottles.
                    _q.Block(8, b + 12, 9, 8, 3, Hex("#4E3E2C"), Hex("#6E5A40"), Hex("#8C7350"), Hex("#5A4834"));
                    _q.Block(19, b + 13, 7, 5, 2, Hex("#2E3A2E"), Hex("#4A5A3E"), Hex("#6A7A52"), Hex("#3A4630"));
                    for (var i = 0; i < 3; i++) _q.Rect(29 + i * 3, b + 13, 2, 5, i == 1 ? Hex("#4E7A8A") : Hex("#6E5A30"));
                    _q.Block(w - 15, b + 12, 7, 7, 3, Hex("#4E3E2C"), Hex("#6E5A40"), Hex("#8C7350"), Hex("#5A4834"));
                    // The awning: striped canvas with a scalloped edge.
                    var stripeA = _biome == Biome.CryoVaults ? Hex("#3E5A6A") : _biome == Biome.OvergrownLabs ? Hex("#4E6A40") : Hex("#7A3A2A");
                    for (var y = b + 34; y < b + 42; y++)
                    for (var x = 2; x < w - 2; x++)
                    {
                        var c = ((x / 5) & 1) == 0 ? stripeA : Hex("#B8A888");
                        if (y == b + 34 && (x % 5 == 2)) continue;
                        _q.Px(x, y, y == b + 41 ? Shade(c, 1.15f) : c);
                    }

                    _q.Line(w / 2 + 8, b + 34, w / 2 + 8, b + 30, SteelDark);
                    Lamp(w / 2 + 7, b + 28, Amber, true, 0f, 2);
                    _q.Px(w / 2 + 7, b + 29, Hex("#FFF2B0"));
                    BiomeDetail(4, b, w - 8, 10);
                    break;
                }
                case "MedicalStation":
                {
                    // A wall med-unit on a stand: lit cross panel, supply drawer, an IV line.
                    var on = state != InteractableArt.Used;
                    _q.Block(6, b, w - 12, 34, 8, _dark, _mid, _light, _front);
                    _q.Rect(9, b + 13, w - 18, 16, Hex("#1A2422"));
                    var cross = on ? Hex("#5ED07A") : Hex("#3A4A40");
                    _q.Rect(w / 2 - 2, b + 15, 4, 12, cross);
                    _q.Rect(w / 2 - 6, b + 19, 12, 4, cross);
                    if (on) _q.Glow(w / 2, b + 21, 12f, Green, 0.2f);
                    _q.Rect(9, b + 3, w - 18, 4, Hex("#D8DCD4"));
                    _q.Rect(w / 2 - 2, b + 4, 4, 2, SteelDark);
                    // IV bag on a hook.
                    _q.Line(w - 5, b + 2, w - 5, b + 38, SteelDark);
                    _q.Rect(w - 7, b + 30, 4, 6, on ? Hex("#A8D8E0") : Hex("#6A7A80"));
                    _q.Line(w - 5, b + 30, w - 8, b + 18, Hex("#8AB8C0"));
                    Lamp(9, b + 30, on ? Green : Off, on, 0.15f);
                    BiomeDetail(6, b, w - 12, 12);
                    break;
                }
                case "WeaponCache":
                {
                    // An armoury case on a pallet: lid open with three weapons in foam (closed and dark once taken).
                    var used = state == InteractableArt.Used;
                    _q.Block(3, b, w - 6, 6, 3, Hex("#3A3024"), Hex("#4E4232"), Hex("#6A5A44"), Hex("#2E2618"));
                    _q.Block(5, b + 5, w - 10, 13, 7, SteelDark, Steel, SteelLight, Hex("#3E4548"));
                    if (!used)
                    {
                        _q.Rect(7, b + 13, w - 14, 4, Hex("#1A1E20"));
                        for (var i = 0; i < 3; i++)
                        {
                            var x = 9 + i * ((w - 18) / 3);
                            _q.Rect(x, b + 14, 9, 2, Hex("#8A9296"));
                            _q.Rect(x + 2, b + 13, 2, 1, Hex("#4A5054"));
                        }

                        // The open lid standing behind.
                        _q.Block(5, b + 18, w - 10, 12, 2, SteelDark, Hex("#4E575B"), Steel, SteelDark);
                        Lamp(w - 11, b + 7, Cyan, true, 0.2f);
                    }
                    else Lamp(w - 11, b + 7, Off, false);

                    _q.Rect(7, b + 8, 10, 2, Hex("#C9A227"));
                    BiomeDetail(5, b + 5, w - 10, 10);
                    break;
                }
                case "CursedChest":
                {
                    // A black iron chest bound in chains, violet light leaking from its seams; prised open while its
                    // guardians are up; open and dead once cleared.
                    var open = state != string.Empty;
                    var lit = state != InteractableArt.Used && state != InteractableArt.Failed;
                    _q.Block(6, b, w - 12, 14, 8, Hex("#141018"), Hex("#2A2232"), Hex("#3E3448"), Hex("#1E1824"));
                    if (!open)
                    {
                        _q.Block(5, b + 14, w - 10, 9, 4, Hex("#141018"), Hex("#2E2638"), Hex("#463A52"), Hex("#221A2A"));
                        for (var x = 7; x < w - 7; x++) _q.Px(x, b + 13, lit ? Violet : Off);
                        _q.Line(8, b + 2, w - 9, b + 20, Hex("#5A5E62"));
                        _q.Line(w - 9, b + 2, 8, b + 20, Hex("#5A5E62"));
                        _q.Rect(w / 2 - 2, b + 9, 4, 4, Hex("#8A7A3A"));
                        if (lit) _q.Glow(w / 2, b + 13, 14f, Violet, 0.22f);
                    }
                    else
                    {
                        _q.Rect(8, b + 10, w - 16, 3, Hex("#0A080C"));
                        _q.Block(5, b + 14, w - 10, 12, 2, Hex("#141018"), Hex("#241C2C"), Hex("#3A2E44"), Hex("#141018"));
                        if (lit)
                        {
                            for (var x = 9; x < w - 9; x++) _q.Px(x, b + 11, Violet);
                            _q.Glow(w / 2, b + 16, 18f, Violet, 0.35f);
                        }
                    }

                    _q.Rect(6, b + 1, 3, 3, Hex("#5A5E62"));
                    _q.Rect(w - 9, b + 1, 3, 3, Hex("#5A5E62"));
                    break;
                }
                case "LockedVault":
                {
                    // A vault door set in a heavy frame: round door, spoked handle, keypad with its status light; swung
                    // open on a dark interior once unlocked.
                    var open = state == InteractableArt.Used;
                    _q.Block(3, b, w - 6, 36, 6, SteelDark, Steel, SteelLight, Hex("#3A4044"));
                    var cx = w / 2 - 3;
                    var cy = b + 20;
                    if (!open)
                    {
                        _q.Disc(cx, cy, 13, _dark, _mid, _light);
                        _q.Ring(cx, cy, 13, Hex("#20262A"));
                        _q.Ring(cx, cy, 9, Hex("#2A3034"));
                        for (var k = 0; k < 6; k++)
                        {
                            var a = k * Mathf.PI / 3f + 0.4f;
                            _q.Line(cx, cy, cx + Mathf.RoundToInt(Mathf.Cos(a) * 8f), cy + Mathf.RoundToInt(Mathf.Sin(a) * 8f), SteelLight);
                        }

                        _q.Rect(cx - 1, cy - 1, 3, 3, Hex("#C9A227"));
                    }
                    else
                    {
                        _q.Disc(cx, cy, 12, Hex("#0C0E10"), Hex("#121518"), Hex("#181C20"));
                        _q.Rect(cx - 15, cy - 12, 4, 24, _mid);
                        _q.Rect(cx - 15, cy - 12, 1, 24, _light);
                    }

                    _q.Rect(w - 10, b + 18, 6, 9, Hex("#1A1E20"));
                    for (var i = 0; i < 3; i++) _q.Rect(w - 9 + (i % 2) * 2, b + 20 + i * 2, 1, 1, Hex("#6A7276"));
                    Lamp(w - 9, b + 25, open ? Green : Amber, true, 0.18f);
                    BiomeDetail(3, b, w - 6, 14);
                    break;
                }
                case "BrokenMachine":
                {
                    // A broken generator: panel hanging open, wires out, sparks and smoke; repaired = panel shut, lamp
                    // green; failed = burnt black and dead.
                    var repaired = state == InteractableArt.Used;
                    var dead = state == InteractableArt.Failed;
                    _q.Block(4, b, w - 8, 24, 9, _dark, _mid, _light, _front);
                    for (var x = 7; x < w - 7; x++) if (((x / 3) & 1) == 0) _q.Px(x, b + 2, Hex("#C9A227"));
                    _q.Rect(8, b + 13, 12, 8, Hex("#1A1E20"));
                    for (var i = 0; i < 4; i++) _q.Rect(10 + i * 2, b + 15, 1, 4, Hex("#3A4044"));
                    _q.Disc(w - 13, b + 18, 5, SteelDark, Steel, SteelLight);
                    if (repaired)
                    {
                        _q.Rect(7, b + 12, 14, 10, _mid);
                        _q.Rect(7, b + 21, 14, 1, _light);
                        Lamp(w - 9, b + 6, Green, true, 0.2f);
                    }
                    else if (dead)
                    {
                        _q.Rect(7, b + 12, 14, 10, Hex("#16181A"));
                        for (var k = 0; k < 18; k++) _q.Px(6 + (k * 7) % (w - 12), b + 4 + (k * 5) % 18, Hex("#0A0A0A"));
                        Lamp(w - 9, b + 6, Off, false);
                    }
                    else
                    {
                        // The panel hangs off its hinge; loose wires; sparks.
                        _q.Line(6, b + 13, 2, b + 6, _light);
                        _q.Line(7, b + 13, 3, b + 6, _mid);
                        _q.Line(12, b + 13, 9, b + 9, Hex("#B04A2A"));
                        _q.Line(15, b + 13, 16, b + 8, Hex("#C9A227"));
                        foreach (var (sx, sy) in new[] { (9, b + 9), (17, b + 7), (11, b + 6), (13, b + 4), (18, b + 10), (8, b + 5) }) _q.Px(sx, sy, Hex("#FFF2B0"));
                        foreach (var (sx, sy) in new[] { (10, b + 8), (16, b + 6), (12, b + 5) }) _q.Px(sx, sy, Amber);
                        _q.Glow(13, b + 8, 8f, Amber, 0.4f);
                        // A smoke wisp off the top.
                        foreach (var (sx, sy) in new[] { (w - 14, b + 26), (w - 13, b + 28), (w - 15, b + 30), (w - 14, b + 32), (w - 12, b + 31) }) _q.Rect(sx, sy, 2, 2, Hex("#6A6E70"));
                        _q.Puddle(w / 2, b + 30, 6, 3, Hex("#3A3C3E"));
                        Lamp(w - 9, b + 6, Amber, true, 0.15f);
                    }

                    BiomeDetail(4, b, w - 8, 12);
                    break;
                }
                case "SupplySignal":
                {
                    // A field beacon: battery crate, mast with a dish and a beacon lamp; transmitting while active,
                    // dark once the drop has landed.
                    var active = state == InteractableArt.Active;
                    var used = state == InteractableArt.Used || state == InteractableArt.Failed;
                    _q.Block(6, b, w - 12, 9, 5, Hex("#2E3A2E"), Hex("#4A5A3E"), Hex("#6A7A52"), Hex("#3A4630"));
                    _q.Rect(9, b + 3, 6, 2, Hex("#C9A227"));
                    _q.Line(w / 2, b + 9, w / 2, b + 40, SteelDark);
                    _q.Line(w / 2 + 1, b + 9, w / 2 + 1, b + 40, Steel);
                    _q.Line(w / 2, b + 12, 6, b + 2, SteelDark);
                    _q.Line(w / 2, b + 12, w - 6, b + 2, SteelDark);
                    // Dish.
                    for (var y = 0; y < 6; y++) _q.Rect(w / 2 - 8 + y, b + 30 + y, 8 - y, 1, y == 0 ? SteelLight : Steel);
                    var beacon = used ? Off : active ? Hex("#FF9A3C") : Amber;
                    _q.Rect(w / 2 - 1, b + 41, 4, 3, beacon);
                    if (!used) _q.Glow(w / 2 + 1, b + 42, active ? 12f : 7f, beacon, active ? 0.4f : 0.2f);
                    if (active)
                        for (var r = 6; r <= 10; r += 4)
                        for (var k = -2; k <= 2; k++)
                            _q.Px(w / 2 + 1 + k * 2, b + 42 + r - Mathf.Abs(k), Hex("#FFC872"));
                    BiomeDetail(6, b, w - 12, 8);
                    break;
                }
                case "SecureRelay":
                {
                    // The relay terminal: a vault-like cabinet with a dish; its screen shows the upload slot, then a
                    // green lock once this member has secured an item.
                    var secured = state == InteractableArt.Secured;
                    _q.Block(5, b, w - 10, 30, 8, _dark, _mid, _light, _front);
                    _q.Rect(8, b + 14, w - 16, 12, Hex("#0E1416"));
                    if (secured)
                    {
                        _q.Rect(w / 2 - 3, b + 17, 6, 5, Hex("#5ED07A"));
                        _q.Rect(w / 2 - 2, b + 22, 4, 2, Hex("#5ED07A"));
                        _q.Glow(w / 2, b + 20, 10f, Green, 0.2f);
                    }
                    else
                    {
                        for (var i = 0; i < 3; i++) _q.Rect(10, b + 23 - i * 3, 6 + i * 3, 1, Hex("#3E9AA8"));
                        _q.Rect(w - 14, b + 16, 4, 4, Cyan);
                        _q.Glow(w / 2, b + 20, 10f, Cyan, 0.16f);
                    }

                    for (var x = 8; x < w - 8; x++) if (((x / 2) & 1) == 0) _q.Px(x, b + 10, Hex("#C9A227"));
                    _q.Rect(w / 2 - 3, b + 3, 6, 5, Hex("#1A1E20"));
                    _q.Line(w / 2 + 4, b + 30, w / 2 + 4, b + 38, SteelDark);
                    for (var y = 0; y < 5; y++) _q.Rect(w / 2 + 1 + y, b + 38 + y, 7 - y, 1, y == 0 ? SteelLight : Steel);
                    BiomeDetail(5, b, w - 10, 10);
                    break;
                }
                default:
                    return false;
            }

            _q.Finish();
            return true;
        }
    }
}

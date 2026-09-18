using System;
using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// The 72 item icons from FINAL_ART_PRODUCTION_SPEC section 12.
    ///
    /// Icons are 32x32 with 3 px padding, transparent background, and the object occupying roughly 70-85% of the
    /// usable area. Rarity is deliberately never painted into the object — the spec puts rarity in the UI frame and
    /// glow, so the same icon reads correctly whatever the roll produced (12, 24.4).
    ///
    /// Weapon icons reuse the world sprite scaled to the slot and rotated to a consistent diagonal, which keeps the
    /// inventory and the world showing the same object rather than two drawings that can drift apart.
    /// </summary>
    public static class IconFactory
    {
        public const int Size = 32;
        private const int Pad = 3;

        public enum ItemKind { Weapon, Armor, Accessory, Consumable, Ammo }

        public sealed class IconDesign
        {
            public string Id = string.Empty;
            public ItemKind Kind;
            /// <summary>Which visual metaphor to draw; meaning depends on Kind.</summary>
            public int Motif;
            public Color32 Primary = RuinPalette.MidSteel;
            public Color32 Secondary = RuinPalette.DarkSteel;
            public Color32 Accent = RuinPalette.AmberActive;
            public int Seed;
        }

        public static PixelCanvas Build(IconDesign d)
        {
            var c = new PixelCanvas(Size, Size);
            switch (d.Kind)
            {
                case ItemKind.Armor: Armor(c, d); break;
                case ItemKind.Accessory: Accessory(c, d); break;
                case ItemKind.Consumable: Consumable(c, d); break;
                case ItemKind.Ammo: Ammo(c, d); break;
            }
            c.SelectiveOutline(RuinPalette.OutlineCharcoal);
            return c;
        }

        /// <summary>Weapon icon: the world sprite, centred and rotated 30 degrees for a consistent inventory read.</summary>
        public static PixelCanvas BuildWeaponIcon(WeaponFactory.WeaponDesign weapon)
        {
            var source = WeaponFactory.Build(weapon);
            var c = new PixelCanvas(Size, Size);

            // Nearest-neighbour rotation about the sprite centre. Kept to a fixed angle so every weapon icon sits the
            // same way in the grid, and integer-sampled so no anti-alias fringe appears (spec 2.2).
            const float angle = -30f * Mathf.Deg2Rad;
            var cos = Mathf.Cos(angle);
            var sin = Mathf.Sin(angle);
            var sxc = source.Width / 2f;
            var syc = source.Height / 2f;

            // Scale down only if the weapon is longer than the icon can hold.
            var longest = Mathf.Max(source.Width, source.Height);
            var usable = Size - Pad * 2;
            var scale = longest > usable ? (float)usable / longest : 1f;

            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var dx = (x - Size / 2f) / scale;
                var dy = (y - Size / 2f) / scale;
                var sx = Mathf.RoundToInt(sxc + dx * cos - dy * sin);
                var sy = Mathf.RoundToInt(syc + dx * sin + dy * cos);
                if (source.IsOpaque(sx, sy)) c.Set(x, y, source.Get(sx, sy));
            }

            return c;
        }

        // ---- 12.2 armor: emphasise the armour silhouette, not a whole character ----
        private static void Armor(PixelCanvas c, IconDesign d)
        {
            var ramp = RuinPalette.RampOf(d.Primary);
            var mid = Size / 2;

            // Chest/vest form: shoulders, torso taper, neck opening.
            c.Taper(mid, Size - Pad - 2, Pad + 4, 9, 7, ramp.Base);
            c.Rect(mid - 11, Size - Pad - 8, 5, 7, ramp.Base);   // left shoulder pad
            c.Rect(mid + 7, Size - Pad - 8, 5, 7, ramp.Base);    // right shoulder pad
            c.ShadeForm(Pad, Pad, Size - Pad * 2, Size - Pad * 2, ramp.Base, ramp);

            // Neck opening, cut out so the vest reads as worn rather than solid.
            for (var y = Size - Pad - 3; y < Size - Pad; y++)
            for (var x = mid - 3; x <= mid + 2; x++)
                c.Erase(x, y);

            switch (d.Motif)
            {
                case 0: // plated
                    c.Rect(mid - 7, mid - 2, 14, 3, RuinPalette.Darken(d.Secondary, 0.1f));
                    c.Rect(mid - 7, mid + 3, 14, 3, RuinPalette.Darken(d.Secondary, 0.1f));
                    break;
                case 1: // padded / canvas
                    for (var y = Pad + 6; y < Size - Pad - 6; y += 4)
                        c.Line(mid - 7, y, mid + 6, y, RuinPalette.Darken(d.Primary, 0.3f));
                    break;
                case 2: // reinforced with a central plate
                    c.Rect(mid - 4, mid - 4, 9, 10, d.Secondary);
                    c.ShadeForm(mid - 4, mid - 4, 9, 10, d.Secondary, RuinPalette.RampOf(d.Secondary));
                    break;
                default: // powered: a small emissive module
                    c.Rect(mid - 3, mid - 1, 6, 5, RuinPalette.Darken(d.Accent, 0.55f));
                    c.Rect(mid - 2, mid, 4, 3, d.Accent);
                    break;
            }

            // Straps, common to every armour family.
            c.Line(mid - 8, Size - Pad - 9, mid + 7, Size - Pad - 9, RuinPalette.Darken(d.Primary, 0.42f));
        }

        // ---- 12.3 accessories: each must use a distinct object metaphor ----
        private static void Accessory(PixelCanvas c, IconDesign d)
        {
            var ramp = RuinPalette.RampOf(d.Primary);
            var mid = Size / 2;

            switch (d.Motif % 8)
            {
                case 0: // module / circuit block
                    c.Rect(mid - 8, mid - 7, 17, 15, ramp.Base);
                    c.ShadeForm(mid - 8, mid - 7, 17, 15, ramp.Base, ramp);
                    for (var y = mid - 4; y <= mid + 4; y += 4) c.Line(mid - 6, y, mid + 6, y, RuinPalette.Darken(d.Primary, 0.35f));
                    c.Rect(mid - 2, mid - 2, 5, 5, d.Accent);
                    break;
                case 1: // lens / optic
                    c.Ellipse(mid, mid, 10, 10, ramp.Base);
                    c.ShadeForm(mid - 10, mid - 10, 21, 21, ramp.Base, ramp);
                    c.Ellipse(mid, mid, 6, 6, RuinPalette.Darken(d.Secondary, 0.3f));
                    c.Ellipse(mid, mid, 4, 4, d.Accent);
                    c.Set(mid - 2, mid + 2, RuinPalette.Lighten(d.Accent, 0.6f));
                    break;
                case 2: // battery / cell
                    c.Rect(mid - 6, mid - 9, 13, 17, ramp.Base);
                    c.ShadeForm(mid - 6, mid - 9, 13, 17, ramp.Base, ramp);
                    c.Rect(mid - 3, mid + 8, 7, 3, RuinPalette.Darken(d.Secondary, 0.2f));
                    c.Rect(mid - 4, mid - 6, 9, 5, d.Accent);
                    break;
                case 3: // injector / vial
                    c.Rect(mid - 4, mid - 9, 9, 14, ramp.Base);
                    c.ShadeForm(mid - 4, mid - 9, 9, 14, ramp.Base, ramp);
                    c.Rect(mid - 3, mid - 7, 7, 8, d.Accent);
                    c.Rect(mid - 1, mid + 5, 3, 6, RuinPalette.PaleSteel);
                    break;
                case 4: // charm / token on a cord
                    c.Ellipse(mid, mid - 2, 7, 7, ramp.Base);
                    c.ShadeForm(mid - 7, mid - 9, 15, 15, ramp.Base, ramp);
                    c.Ellipse(mid, mid - 2, 3, 3, d.Accent);
                    c.Line(mid - 5, mid + 5, mid, mid + 10, RuinPalette.Darken(d.Secondary, 0.2f));
                    c.Line(mid, mid + 10, mid + 5, mid + 5, RuinPalette.Darken(d.Secondary, 0.2f));
                    break;
                case 5: // sensor array
                    c.Rect(mid - 7, mid - 5, 15, 9, ramp.Base);
                    c.ShadeForm(mid - 7, mid - 5, 15, 9, ramp.Base, ramp);
                    for (var x = mid - 5; x <= mid + 5; x += 3) c.Line(x, mid + 4, x, mid + 9, RuinPalette.Darken(d.Secondary, 0.1f));
                    c.Rect(mid - 5, mid - 3, 11, 3, d.Accent);
                    break;
                case 6: // reinforced component / bracket
                    c.Rect(mid - 9, mid - 4, 19, 9, ramp.Base);
                    c.ShadeForm(mid - 9, mid - 4, 19, 9, ramp.Base, ramp);
                    c.Rect(mid - 9, mid - 8, 5, 17, RuinPalette.Darken(d.Primary, 0.2f));
                    c.Rect(mid + 5, mid - 8, 5, 17, RuinPalette.Darken(d.Primary, 0.2f));
                    c.Set(mid - 7, mid, d.Accent);
                    c.Set(mid + 7, mid, d.Accent);
                    break;
                default: // coil / emitter
                    c.Ellipse(mid, mid, 9, 9, RuinPalette.Darken(d.Secondary, 0.25f));
                    for (var r = 8; r > 1; r -= 3) c.Ellipse(mid, mid, r, r, r % 2 == 0 ? ramp.Base : d.Accent);
                    c.ShadeForm(mid - 9, mid - 9, 19, 19, ramp.Base, ramp);
                    break;
            }
        }

        // ---- 12.4 consumables: clear container silhouettes ----
        private static void Consumable(PixelCanvas c, IconDesign d)
        {
            var ramp = RuinPalette.RampOf(d.Primary);
            var mid = Size / 2;

            switch (d.Motif % 5)
            {
                case 0: // medkit
                    c.Rect(mid - 9, mid - 7, 19, 15, ramp.Base);
                    c.ShadeForm(mid - 9, mid - 7, 19, 15, ramp.Base, ramp);
                    c.Rect(mid - 2, mid - 4, 5, 11, d.Accent);   // original cross, not a trademarked one
                    c.Rect(mid - 6, mid - 1, 13, 5, d.Accent);
                    c.Rect(mid - 3, mid + 8, 7, 2, RuinPalette.Darken(d.Secondary, 0.2f));
                    break;
                case 1: // injector / stim
                    c.Rect(mid - 3, mid - 8, 7, 13, ramp.Base);
                    c.ShadeForm(mid - 3, mid - 8, 7, 13, ramp.Base, ramp);
                    c.Rect(mid - 2, mid - 6, 5, 8, d.Accent);
                    c.Rect(mid - 1, mid + 5, 3, 5, RuinPalette.PaleSteel);
                    c.Rect(mid - 5, mid + 4, 11, 2, RuinPalette.Darken(d.Secondary, 0.2f));
                    break;
                case 2: // canister
                    c.Rect(mid - 6, mid - 9, 13, 16, ramp.Base);
                    c.Ellipse(mid, mid + 7, 6, 3, ramp.Base);
                    c.Ellipse(mid, mid - 9, 6, 3, RuinPalette.Darken(d.Primary, 0.3f));
                    c.ShadeForm(mid - 6, mid - 12, 13, 22, ramp.Base, ramp);
                    c.Rect(mid - 5, mid - 2, 11, 4, d.Accent);
                    break;
                case 3: // grenade
                    c.Ellipse(mid, mid - 1, 8, 8, ramp.Base);
                    c.ShadeForm(mid - 8, mid - 9, 17, 17, ramp.Base, ramp);
                    c.Rect(mid - 3, mid + 7, 7, 4, RuinPalette.Darken(d.Secondary, 0.15f));
                    c.Rect(mid - 5, mid + 9, 4, 2, RuinPalette.Darken(d.Secondary, 0.3f));
                    c.Rect(mid - 7, mid - 3, 15, 2, d.Accent);
                    break;
                default: // field device
                    c.Rect(mid - 8, mid - 5, 17, 11, ramp.Base);
                    c.ShadeForm(mid - 8, mid - 5, 17, 11, ramp.Base, ramp);
                    c.Rect(mid - 6, mid - 3, 8, 6, RuinPalette.Darken(d.Secondary, 0.35f));
                    c.Rect(mid - 5, mid - 2, 6, 4, d.Accent);
                    c.Line(mid + 4, mid + 6, mid + 4, mid + 10, RuinPalette.PaleSteel);
                    break;
            }
        }

        // ---- 12.5 ammo: four cartridge geometries, distinguishable at slot size ----
        private static void Ammo(PixelCanvas c, IconDesign d)
        {
            var ramp = RuinPalette.RampOf(d.Primary);
            var mid = Size / 2;

            void Round(int x, int bottom, int w, int h, Color32 caseColor, Color32 tip, bool shell)
            {
                var caseRamp = RuinPalette.RampOf(caseColor);
                c.Rect(x, bottom, w, h, caseColor);
                c.ShadeForm(x, bottom, w, h, caseColor, caseRamp);
                if (shell)
                {
                    // Shotgun shell: brass base, plastic hull, flat crimp.
                    c.Rect(x, bottom, w, 4, RuinPalette.Darken(RuinPalette.WarningOchre, 0.15f));
                    c.Rect(x, bottom + h - 2, w, 2, RuinPalette.Darken(caseColor, 0.4f));
                }
                else
                {
                    // Cartridge: tapered bullet tip.
                    for (var i = 0; i < 4; i++)
                    {
                        var inset = i;
                        for (var xx = x + inset; xx < x + w - inset; xx++) c.Set(xx, bottom + h + i, tip);
                    }
                    c.Rect(x, bottom, w, 2, RuinPalette.Darken(caseColor, 0.35f));
                }
            }

            switch (d.Motif % 4)
            {
                case 0: // Light: three small slim rounds
                    Round(mid - 9, mid - 8, 5, 11, RuinPalette.WarningOchre, RuinPalette.PaleSteel, false);
                    Round(mid - 2, mid - 8, 5, 11, RuinPalette.WarningOchre, RuinPalette.PaleSteel, false);
                    Round(mid + 5, mid - 8, 5, 11, RuinPalette.WarningOchre, RuinPalette.PaleSteel, false);
                    break;
                case 1: // Medium: two mid rounds
                    Round(mid - 8, mid - 9, 7, 13, RuinPalette.DirtyYellow, RuinPalette.Hex("#9A5A38"), false);
                    Round(mid + 1, mid - 9, 7, 13, RuinPalette.DirtyYellow, RuinPalette.Hex("#9A5A38"), false);
                    break;
                case 2: // Heavy: one large round
                    Round(mid - 5, mid - 11, 11, 16, RuinPalette.Hex("#A87C3A"), RuinPalette.Hex("#8A8F90"), false);
                    break;
                default: // Shells: two stubby shotgun shells
                    Round(mid - 9, mid - 7, 8, 13, RuinPalette.EmergencyRed, RuinPalette.WarningOchre, true);
                    Round(mid + 2, mid - 7, 8, 13, RuinPalette.EmergencyRed, RuinPalette.WarningOchre, true);
                    break;
            }
        }
    }
}

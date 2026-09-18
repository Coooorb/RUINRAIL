using System.Collections.Generic;
using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// The 33 weapon world sprites from FINAL_ART_PRODUCTION_SPEC sections 10-11.
    ///
    /// Everything is authored pointing +X (east) with the grip at the canvas's left-of-centre anchor, because the
    /// runtime WeaponPivot rotates one sprite through 360 degrees rather than storing per-angle art (spec 10, 21).
    /// Class silhouette is built from the receiver/barrel proportions the spec names for each family; the Legendary
    /// of each class shares that family language but adds the distinguishing feature the profile calls for.
    /// </summary>
    public static class WeaponFactory
    {
        public enum Family { Pistol, Smg, Rifle, BattleRifle, Shotgun, Sniper, Rocket, Bow, Blaster, Knife, Spear }

        public sealed class WeaponDesign
        {
            public string Id = string.Empty;
            public string DisplayName = string.Empty;
            public Family Family;
            public int Width = 24;
            public int Height = 14;
            /// <summary>Barrel length in px from the receiver front.</summary>
            public int Barrel = 8;
            /// <summary>Receiver block length.</summary>
            public int Receiver = 8;
            public int ReceiverHeight = 5;
            public bool Magazine = true;
            public int MagazineLength = 4;
            public bool Stock;
            public bool Optic;
            public bool Legendary;
            /// <summary>Emissive/charged detail: heat ports, energy strips, charged spine.</summary>
            public bool Powered;
            public Color32 Metal = RuinPalette.MidSteel;
            public Color32 Grip = RuinPalette.Hex("#4A3A2C");
            public Color32 Accent = RuinPalette.WarningOchre;
            public Color32 Energy = RuinPalette.ElectricCyan;
            public int Seed;
        }

        /// <summary>The grip anchor: where the hand holds it, which is what WeaponPivot rotates around.</summary>
        public static Vector2Int GripAnchor(WeaponDesign d) => new(6, d.Height / 2 - 1);

        public static PixelCanvas Build(WeaponDesign d)
        {
            var c = new PixelCanvas(d.Width, d.Height);
            var midY = d.Height / 2;
            var metal = RuinPalette.RampOf(d.Metal);
            var grip = RuinPalette.RampOf(d.Grip);

            switch (d.Family)
            {
                case Family.Bow: BuildBow(c, d, metal); break;
                case Family.Knife: BuildBlade(c, d, metal, grip, midY, false); break;
                case Family.Spear: BuildBlade(c, d, metal, grip, midY, true); break;
                default: BuildFirearm(c, d, metal, grip, midY); break;
            }

            var emissive = d.Powered ? c.MarkEmissive(0, 0, d.Width, d.Height) : null;
            c.SelectiveOutline(RuinPalette.OutlineCharcoal, emissive);
            return c;
        }

        private static void BuildFirearm(PixelCanvas c, WeaponDesign d, RuinPalette.Ramp metal, RuinPalette.Ramp grip, int midY)
        {
            var anchor = GripAnchor(d);
            var recX = anchor.x;
            var recY = midY - d.ReceiverHeight / 2;

            // Grip, angled back and down from the receiver.
            c.Taper(anchor.x + 1, recY, Mathf.Max(0, recY - 5), 2, 1, grip.Base);
            c.ShadeForm(anchor.x - 2, Mathf.Max(0, recY - 5), 6, 7, grip.Base, grip);

            // Receiver block.
            c.Rect(recX, recY, d.Receiver, d.ReceiverHeight, metal.Base);
            c.ShadeForm(recX, recY, d.Receiver, d.ReceiverHeight, metal.Base, metal);

            // Barrel: thinner than the receiver so the class silhouette reads.
            var barrelH = d.Family switch
            {
                Family.Shotgun => 4,
                Family.Rocket => 6,
                Family.Sniper => 2,
                Family.Blaster => 4,
                _ => 3
            };
            var barrelY = midY - barrelH / 2;
            c.Rect(recX + d.Receiver, barrelY, d.Barrel, barrelH, metal.Base);
            c.ShadeForm(recX + d.Receiver, barrelY, d.Barrel, barrelH, metal.Base, metal);

            // Muzzle: a visibly distinct end so the firing point is obvious (spec 10).
            var muzzleX = recX + d.Receiver + d.Barrel;
            var muzzleH = d.Family is Family.Shotgun or Family.Rocket ? barrelH + 2 : barrelH + 1;
            c.Rect(muzzleX - 2, midY - muzzleH / 2, 3, muzzleH, RuinPalette.Darken(d.Metal, 0.35f));
            c.Set(muzzleX - 1, midY, RuinPalette.NearBlack);

            if (d.Magazine)
            {
                var magX = recX + d.Receiver / 2 - 1;
                c.Rect(magX, Mathf.Max(0, recY - d.MagazineLength), 3, d.MagazineLength + 1, RuinPalette.Darken(d.Metal, 0.2f));
                c.ShadeForm(magX, Mathf.Max(0, recY - d.MagazineLength), 3, d.MagazineLength + 1,
                    RuinPalette.Darken(d.Metal, 0.2f), RuinPalette.RampOf(RuinPalette.Darken(d.Metal, 0.2f)));
            }

            if (d.Stock)
            {
                c.Taper(recX - 3, recY + 1, recY + d.ReceiverHeight - 1, 2, 2, RuinPalette.Darken(d.Grip, 0.1f));
                c.Rect(Mathf.Max(0, recX - 5), recY + 1, 3, d.ReceiverHeight - 2, RuinPalette.Darken(d.Grip, 0.1f));
            }

            if (d.Optic)
            {
                var oy = recY + d.ReceiverHeight;
                c.Rect(recX + 2, oy, d.Family == Family.Sniper ? 7 : 4, 2, RuinPalette.Darken(d.Metal, 0.4f));
                c.Set(recX + 2, oy + 1, RuinPalette.Darken(d.Energy, 0.2f));
            }

            if (d.Powered)
            {
                // Energy strip / heat ports along the top of the receiver.
                for (var x = recX + 1; x < recX + d.Receiver - 1; x += 2)
                    c.Set(x, recY + d.ReceiverHeight - 1, d.Energy);
                c.Rect(recX + d.Receiver, midY - 1, Mathf.Max(2, d.Barrel - 2), 1, RuinPalette.Lighten(d.Energy, 0.3f));
            }

            if (d.Legendary)
            {
                // Legendary marker: a warning band plus a brighter accent at the receiver front (spec 11).
                c.Rect(recX + d.Receiver - 2, recY, 2, d.ReceiverHeight, d.Accent);
                c.Set(recX + d.Receiver - 1, recY + d.ReceiverHeight - 1, RuinPalette.Lighten(d.Accent, 0.4f));
            }
        }

        private static void BuildBow(PixelCanvas c, WeaponDesign d, RuinPalette.Ramp metal)
        {
            var midY = d.Height / 2;
            var limb = RuinPalette.RampOf(d.Grip);
            var gx = GripAnchor(d).x;

            // A bow aiming +X reads as limbs bellying forward with the string drawn back behind them. The limb is a
            // parabola from the riser out to each tip: deepest at the grip, returning to the string line at the tips.
            var half = d.Height / 2 - 2;
            var belly = Mathf.Max(6, d.Width - gx - 8);
            var tipX = gx + 2;

            for (var y = midY - half; y <= midY + half; y++)
            {
                var t = (float)(y - midY) / half;                 // -1 at top tip, 0 at grip, +1 at bottom tip
                var x = tipX + Mathf.RoundToInt(belly * (1f - t * t));
                c.Dot(x, y, limb.Base, 2);
                // Recurve: the outer third kicks slightly back for a composite-limb read.
                if (Mathf.Abs(t) > 0.72f) c.Dot(x - 1, y, limb.Base, 2);
            }

            c.ShadeForm(0, 0, d.Width, d.Height, limb.Base, limb);

            // String: a straight line between the two tips, behind the limbs.
            var stringColor = d.Legendary || d.Powered ? d.Energy : RuinPalette.PaleSteel;
            c.Line(tipX, midY - half, tipX, midY + half, stringColor);

            // Riser and grip wrap at the belly's deepest point, where the hand goes.
            var riserX = tipX + belly - 1;
            c.Rect(riserX - 1, midY - 4, 3, 9, RuinPalette.Darken(d.Grip, 0.4f));
            c.ShadeForm(riserX - 1, midY - 4, 3, 9, RuinPalette.Darken(d.Grip, 0.4f),
                RuinPalette.RampOf(RuinPalette.Darken(d.Grip, 0.4f)));

            if (d.Powered)
            {
                // Emitters at the limb tips (Compound cams, Stormstring energy).
                c.Dot(tipX + 1, midY - half, d.Energy, 2);
                c.Dot(tipX + 1, midY + half, d.Energy, 2);
            }
        }

        private static void BuildBlade(PixelCanvas c, WeaponDesign d, RuinPalette.Ramp metal, RuinPalette.Ramp grip, int midY, bool spear)
        {
            var gx = GripAnchor(d).x;

            if (spear)
            {
                // Long shaft with a real leaf head. A 2 px shaft and a 3 px tip read as a flagpole, not a weapon.
                var headLen = 9;
                var shaftEnd = d.Width - headLen;

                c.Rect(0, midY - 1, shaftEnd, 3, grip.Base);
                c.ShadeForm(0, midY - 1, shaftEnd, 3, grip.Base, grip);

                // Binding collar where the head meets the shaft.
                c.Rect(shaftEnd - 3, midY - 2, 3, 5, RuinPalette.Darken(d.Metal, 0.35f));

                // Leaf blade: widens from the collar then tapers to the point.
                for (var i = 0; i < headLen; i++)
                {
                    var t = (float)i / (headLen - 1);
                    var halfH = Mathf.RoundToInt(Mathf.Lerp(3f, 0f, Mathf.Pow(t, 0.75f)));
                    for (var y = midY - halfH; y <= midY + halfH; y++) c.Set(shaftEnd + i, y, metal.Base);
                }
                c.ShadeForm(shaftEnd, midY - 4, headLen, 9, metal.Base, metal);
                // Central fuller, the value break that stops the blade reading as a flat wedge.
                c.Line(shaftEnd + 1, midY, d.Width - 3, midY, RuinPalette.Lighten(d.Metal, 0.3f));

                if (d.Powered) c.Line(3, midY + 1, shaftEnd - 4, midY + 1, d.Energy);
            }
            else
            {
                // Short blade: handle, guard, tapered edge.
                c.Rect(0, midY - 1, 7, 3, grip.Base);
                c.ShadeForm(0, midY - 1, 7, 3, grip.Base, grip);
                c.Rect(7, midY - 2, 2, 5, RuinPalette.Darken(d.Metal, 0.25f));
                c.Taper(d.Width - 6, midY - 2, midY + 2, 0, 2, metal.Base);
                c.Rect(9, midY - 1, d.Width - 12, 3, metal.Base);
                c.ShadeForm(9, midY - 2, d.Width - 9, 5, metal.Base, metal);
                if (d.Legendary) c.Line(10, midY + 1, d.Width - 4, midY + 1, d.Energy);
            }
        }

        /// <summary>All 33 designs, named and ordered exactly as FINAL_ART_PRODUCTION_SPEC section 11 lists them.</summary>
        public static IReadOnlyList<WeaponDesign> All() => new List<WeaponDesign>
        {
            // Pistols
            new() { Id = "weapon_p9_ranger", DisplayName = "P9 Ranger", Family = Family.Pistol, Width = 24, Height = 14, Receiver = 7, Barrel = 5, ReceiverHeight = 5, MagazineLength = 4, Grip = RuinPalette.Hex("#4A3A2C"), Metal = RuinPalette.Hex("#414A4C"), Optic = true, Accent = RuinPalette.AmberActive, Seed = 1 },
            new() { Id = "weapon_kestrel_12", DisplayName = "Kestrel-12", Family = Family.Pistol, Width = 26, Height = 14, Receiver = 6, Barrel = 8, ReceiverHeight = 4, MagazineLength = 4, Metal = RuinPalette.Hex("#5A6365"), Seed = 2 },
            new() { Id = "weapon_quickfang", DisplayName = "Quickfang", Family = Family.Pistol, Width = 26, Height = 14, Receiver = 7, Barrel = 6, ReceiverHeight = 5, MagazineLength = 5, Legendary = true, Powered = true, Accent = RuinPalette.OxideOrange, Energy = RuinPalette.OxideOrange, Seed = 3 },

            // SMGs
            new() { Id = "weapon_rattler_9", DisplayName = "Rattler-9", Family = Family.Smg, Width = 28, Height = 14, Receiver = 9, Barrel = 6, ReceiverHeight = 5, MagazineLength = 6, Grip = RuinPalette.Hex("#3E3630"), Seed = 4 },
            new() { Id = "weapon_wasp_45", DisplayName = "Wasp-45", Family = Family.Smg, Width = 30, Height = 14, Receiver = 10, Barrel = 7, ReceiverHeight = 6, MagazineLength = 6, Accent = RuinPalette.DirtyYellow, Legendary = false, Seed = 5 },
            new() { Id = "weapon_buzzsaw", DisplayName = "Buzzsaw", Family = Family.Smg, Width = 32, Height = 16, Receiver = 10, Barrel = 8, ReceiverHeight = 6, MagazineLength = 8, Legendary = true, Powered = true, Energy = RuinPalette.OxideOrange, Accent = RuinPalette.OxideOrange, Seed = 6 },

            // Assault rifles
            new() { Id = "weapon_ar_17", DisplayName = "AR-17", Family = Family.Rifle, Width = 34, Height = 14, Receiver = 11, Barrel = 11, ReceiverHeight = 5, MagazineLength = 6, Stock = true, Optic = true, Seed = 7 },
            new() { Id = "weapon_marauder_a2", DisplayName = "Marauder A2", Family = Family.Rifle, Width = 36, Height = 14, Receiver = 12, Barrel = 11, ReceiverHeight = 6, MagazineLength = 6, Stock = true, Metal = RuinPalette.Hex("#4E4740"), Seed = 8 },
            new() { Id = "weapon_vanguard", DisplayName = "Vanguard", Family = Family.Rifle, Width = 36, Height = 14, Receiver = 12, Barrel = 11, ReceiverHeight = 5, MagazineLength = 6, Stock = true, Optic = true, Legendary = true, Powered = true, Metal = RuinPalette.Hex("#5C6466"), Seed = 9 },

            // Battle rifles
            new() { Id = "weapon_sentinel_br", DisplayName = "Sentinel BR", Family = Family.BattleRifle, Width = 38, Height = 14, Receiver = 13, Barrel = 13, ReceiverHeight = 5, MagazineLength = 5, Stock = true, Optic = true, Seed = 10 },
            new() { Id = "weapon_hound_br", DisplayName = "Hound BR", Family = Family.BattleRifle, Width = 34, Height = 14, Receiver = 12, Barrel = 8, ReceiverHeight = 6, MagazineLength = 5, Stock = true, Grip = RuinPalette.Hex("#503D2C"), Seed = 11 },
            new() { Id = "weapon_judicator", DisplayName = "Judicator", Family = Family.BattleRifle, Width = 40, Height = 14, Receiver = 13, Barrel = 14, ReceiverHeight = 5, MagazineLength = 5, Stock = true, Optic = true, Legendary = true, Powered = true, Energy = RuinPalette.AmberActive, Seed = 12 },

            // Shotguns
            new() { Id = "weapon_breacher_12", DisplayName = "Breacher-12", Family = Family.Shotgun, Width = 32, Height = 14, Receiver = 11, Barrel = 10, ReceiverHeight = 6, Magazine = false, Stock = true, Seed = 13 },
            new() { Id = "weapon_scatter_8", DisplayName = "Scatter-8", Family = Family.Shotgun, Width = 34, Height = 14, Receiver = 12, Barrel = 10, ReceiverHeight = 6, MagazineLength = 5, Stock = true, Seed = 14 },
            new() { Id = "weapon_crowdbreaker", DisplayName = "Crowdbreaker", Family = Family.Shotgun, Width = 36, Height = 16, Receiver = 12, Barrel = 10, ReceiverHeight = 7, Magazine = false, Stock = true, Legendary = true, Accent = RuinPalette.WarningOchre, Seed = 15 },

            // Snipers
            new() { Id = "weapon_longshot_s1", DisplayName = "Longshot S1", Family = Family.Sniper, Width = 44, Height = 14, Receiver = 12, Barrel = 20, ReceiverHeight = 4, MagazineLength = 4, Stock = true, Optic = true, Seed = 16 },
            new() { Id = "weapon_needle_m7", DisplayName = "Needle M7", Family = Family.Sniper, Width = 42, Height = 12, Receiver = 10, Barrel = 20, ReceiverHeight = 4, MagazineLength = 3, Stock = true, Optic = true, Metal = RuinPalette.Hex("#6B7476"), Energy = RuinPalette.ElectricCyan, Seed = 17 },
            new() { Id = "weapon_farline", DisplayName = "Farline", Family = Family.Sniper, Width = 46, Height = 14, Receiver = 12, Barrel = 22, ReceiverHeight = 4, MagazineLength = 3, Stock = true, Optic = true, Legendary = true, Powered = true, Energy = RuinPalette.TerminalGreen, Seed = 18 },

            // Rockets
            new() { Id = "weapon_pipe_launcher", DisplayName = "Pipe Launcher", Family = Family.Rocket, Width = 36, Height = 16, Receiver = 10, Barrel = 16, ReceiverHeight = 7, Magazine = false, Metal = RuinPalette.Hex("#6A5A4A"), Seed = 19 },
            new() { Id = "weapon_twin_tube", DisplayName = "Twin-Tube", Family = Family.Rocket, Width = 38, Height = 18, Receiver = 11, Barrel = 16, ReceiverHeight = 9, Magazine = false, Seed = 20 },
            new() { Id = "weapon_sunbreaker", DisplayName = "Sunbreaker", Family = Family.Rocket, Width = 42, Height = 18, Receiver = 13, Barrel = 17, ReceiverHeight = 9, Magazine = false, Legendary = true, Powered = true, Energy = RuinPalette.AmberActive, Accent = RuinPalette.OxideOrange, Seed = 21 },

            // Bows
            new() { Id = "weapon_recurve_bow", DisplayName = "Recurve Bow", Family = Family.Bow, Width = 30, Height = 30, Grip = RuinPalette.Hex("#6A5334"), Seed = 22 },
            new() { Id = "weapon_compound_bow", DisplayName = "Compound Bow", Family = Family.Bow, Width = 32, Height = 32, Grip = RuinPalette.Hex("#4C4A44"), Powered = true, Energy = RuinPalette.PaleSteel, Seed = 23 },
            new() { Id = "weapon_stormstring", DisplayName = "Stormstring", Family = Family.Bow, Width = 34, Height = 34, Grip = RuinPalette.Hex("#3E4A46"), Legendary = true, Powered = true, Energy = RuinPalette.TerminalGreen, Seed = 24 },

            // Blasters
            new() { Id = "weapon_pulse_carbine_b1", DisplayName = "Pulse Carbine B1", Family = Family.Blaster, Width = 30, Height = 16, Receiver = 11, Barrel = 8, ReceiverHeight = 7, Magazine = false, Powered = true, Metal = RuinPalette.Hex("#4B5457"), Energy = RuinPalette.ElectricCyan, Seed = 25 },
            new() { Id = "weapon_arc_blaster_b4", DisplayName = "Arc B4", Family = Family.Blaster, Width = 34, Height = 18, Receiver = 13, Barrel = 9, ReceiverHeight = 9, Magazine = false, Powered = true, Energy = RuinPalette.TerminalGreen, Seed = 26 },
            new() { Id = "weapon_redline", DisplayName = "Redline", Family = Family.Blaster, Width = 36, Height = 18, Receiver = 13, Barrel = 10, ReceiverHeight = 9, Magazine = false, Legendary = true, Powered = true, Energy = RuinPalette.EmergencyRed, Accent = RuinPalette.OxideOrange, Seed = 27 },

            // Knives
            new() { Id = "weapon_field_knife", DisplayName = "Field Knife", Family = Family.Knife, Width = 22, Height = 12, Grip = RuinPalette.Hex("#332C26"), Seed = 28 },
            new() { Id = "weapon_ripper_knife", DisplayName = "Ripper Knife", Family = Family.Knife, Width = 24, Height = 12, Grip = RuinPalette.Hex("#40342C"), Metal = RuinPalette.Hex("#707A7C"), Seed = 29 },
            new() { Id = "weapon_ghostedge", DisplayName = "Ghostedge", Family = Family.Knife, Width = 24, Height = 12, Grip = RuinPalette.Charcoal, Metal = RuinPalette.Hex("#2E3639"), Legendary = true, Powered = true, Energy = RuinPalette.ElectricCyan, Seed = 30 },

            // Spears
            new() { Id = "weapon_scrap_spear", DisplayName = "Scrap Spear", Family = Family.Spear, Width = 44, Height = 12, Grip = RuinPalette.Hex("#5A5148"), Metal = RuinPalette.Hex("#6E6A62"), Seed = 31 },
            new() { Id = "weapon_guard_lance", DisplayName = "Guard Lance", Family = Family.Spear, Width = 48, Height = 12, Grip = RuinPalette.Hex("#4A4E50"), Metal = RuinPalette.PaleSteel, Seed = 32 },
            new() { Id = "weapon_railspike", DisplayName = "Railspike", Family = Family.Spear, Width = 50, Height = 14, Grip = RuinPalette.Hex("#3A342E"), Metal = RuinPalette.Hex("#8A8478"), Legendary = true, Powered = true, Energy = RuinPalette.AmberActive, Seed = 33 }
        };
    }
}

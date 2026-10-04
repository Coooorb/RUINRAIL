using RuinRail.App;
using UnityEngine;
using static RuinRail.EditorTools.ArtGen.FrontEndSceneRenderer;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// The two full-screen front-end backdrops: the Shelter interior the hub is staged in, and the transit tunnel the
    /// Main Menu looks down to the Shelter door.
    ///
    /// FINAL_ART_PRODUCTION_SPEC section 16 describes the Shelter as the safest place in RUINRAIL and still an
    /// improvised, repaired, resource-limited one — warmer and more human than the dungeons without ever becoming
    /// comfortable. Both places are rendered as real spaces from one perspective camera each
    /// (<see cref="FrontEndScenes"/>, <see cref="FrontEndSceneRenderer"/>): structure with depth, materials that read
    /// (tiles, concrete, bulkhead plate, wood, canvas), practical lamps that actually light what is near them and
    /// throw shadows, and fog that pushes the far end back. The light is banded the way the dungeon art is, so the
    /// front-end speaks the same pixel language as the runs.
    ///
    /// They are authored at screen pixels (PPU 1) at the 640x360 reference and composed for the screens on top:
    /// the focal door sits behind the centre, the outer thirds are darker and quieter because the menu column and the
    /// hub's side columns cover them, and a vignette keeps thin 1 px chrome readable. The ambient life (flicker, dust,
    /// steam, the terminal) is runtime animation over these exact spots (<see cref="FrontEndAmbience"/>).
    /// </summary>
    public static class ShelterSceneFactory
    {
        public const int Width = 640;
        public const int Height = 360;

        private static readonly Color Dark = Hex("#12171A");
        private static readonly Color Amber = Hex("#FFB347");
        private static readonly Color AmberLight = new(1f, 0.70f, 0.38f);

        private static float Frac(float v) => v - Mathf.Floor(v);

        // =====================================================================
        //  Shelter interior — the hub backdrop
        // =====================================================================

        /// <summary>The Shelter as a lit room: bulkhead end wall and transit door, storage side, work side, generator.</summary>
        public static PixelCanvas Shelter()
        {
            const float hw = FrontEndScenes.ShelterHalfWidth;
            const float floor = FrontEndScenes.ShelterFloor;
            const float ceil = FrontEndScenes.ShelterCeiling;
            const float back = FrontEndScenes.ShelterBack;
            var r = new FrontEndSceneRenderer
            {
                View = FrontEndScenes.ShelterView,
                XMin = -hw, XMax = hw, YMin = floor, YMax = ceil, ZEnd = back,
                Ambient = new Color(0.17f, 0.16f, 0.15f),
                Fog = Hex("#101417"),
                FogDensity = 0.018f,
                Floor = ShelterFloor,
                Ceiling = (p, n) => Grime(Frac(p.z / 1.6f) < 0.05f ? Hex("#2A2E2E") : Hex("#3A3E3C"), p.x, p.z, 0.8f, 3),
                Left = ShelterWall,
                Right = ShelterRightWall,
                End = ShelterEndWall
            };

            Surface steel = (p, n) => Grime(Hex("#5A605C"), p.x + p.z, p.y, 0.6f, 11);
            Surface darkSteel = (p, n) => Grime(Hex("#3E4442"), p.x + p.z, p.y, 0.5f, 12);
            Surface wood = (p, n) => Frac((p.x + p.z) / 0.22f) < 0.12f ? Hex("#4E3E2C") : Hex("#6E5A40");
            Surface beige = (p, n) => Crate(p, n, Hex("#8C7A5C"));
            Surface canvas = (p, n) => Crate(p, n, Hex("#5E6A48"));
            Surface drum = (p, n) => Frac(p.y / 0.35f) < 0.1f ? Hex("#2E3A2E") : Hex("#45563E");
            Surface machine = (p, n) => Frac(p.y / 0.3f) < 0.08f ? Hex("#2A2E2C") : Grime(Hex("#4E5450"), p.x + p.z, p.y, 0.6f, 21);

            // Ceiling beams across the room and a conduit along it.
            foreach (var z in new[] { 5.5f, 8.5f, 11.5f })
                r.Boxes.Add(new Box { Min = new Vector3(-hw, ceil - 0.32f, z), Max = new Vector3(hw, ceil, z + 0.3f), Mat = steel });
            r.Boxes.Add(new Box { Min = new Vector3(-1.4f, ceil - 0.5f, 3f), Max = new Vector3(-1.25f, ceil - 0.35f, back), Mat = darkSteel });

            // Storage side: a shelving run along the left wall, loaded with crates.
            for (var i = 0; i < 3; i++)
            {
                var z0 = 5.6f + i * 2.1f;
                foreach (var zu in new[] { z0, z0 + 1.9f })
                    r.Boxes.Add(new Box { Min = new Vector3(-hw, floor, zu), Max = new Vector3(-hw + 0.85f, floor + 3.2f, zu + 0.08f), Mat = darkSteel });
                foreach (var y in new[] { -1.55f, -0.45f, 0.65f })
                    r.Boxes.Add(new Box { Min = new Vector3(-hw, y, z0), Max = new Vector3(-hw + 0.85f, y + 0.08f, z0 + 1.98f), Mat = steel });
                for (var s = 0; s < 3; s++)
                {
                    var y = new[] { -1.47f, -0.37f, 0.73f }[s];
                    var h = 0.45f + 0.25f * Hash(i, s, 5);
                    var zz = z0 + 0.15f + 0.6f * Hash(i, s, 6);
                    r.Boxes.Add(new Box { Min = new Vector3(-hw + 0.05f, y, zz), Max = new Vector3(-hw + 0.75f, y + h, zz + 0.7f + 0.4f * Hash(i, s, 7)), Mat = (i + s) % 2 == 0 ? beige : canvas });
                }
            }

            // Floor stacks in front of the shelves.
            r.Boxes.Add(new Box { Min = new Vector3(-4.9f, floor, 4.2f), Max = new Vector3(-3.9f, floor + 0.8f, 5.1f), Mat = canvas });
            r.Boxes.Add(new Box { Min = new Vector3(-4.75f, floor + 0.8f, 4.35f), Max = new Vector3(-4.05f, floor + 1.3f, 4.95f), Mat = beige });

            // Water drums by the door.
            r.Boxes.Add(new Box { Min = new Vector3(-2.9f, floor, 12.5f), Max = new Vector3(-2.2f, floor + 1.1f, 13.2f), Mat = drum });
            r.Boxes.Add(new Box { Min = new Vector3(-2.1f, floor, 12.9f), Max = new Vector3(-1.5f, floor + 1.0f, 13.5f), Mat = drum });

            // Work side: bench, terminal, generator and its exhaust stack.
            r.Boxes.Add(new Box { Min = new Vector3(4.3f, floor + 1.0f, 6.4f), Max = new Vector3(hw, floor + 1.15f, 10.6f), Mat = wood });
            r.Boxes.Add(new Box { Min = new Vector3(5.6f, floor, 6.5f), Max = new Vector3(5.75f, floor + 1.0f, 6.65f), Mat = darkSteel });
            r.Boxes.Add(new Box { Min = new Vector3(4.45f, floor, 6.5f), Max = new Vector3(4.6f, floor + 1.0f, 6.65f), Mat = darkSteel });
            r.Boxes.Add(new Box { Min = new Vector3(4.45f, floor, 10.3f), Max = new Vector3(4.6f, floor + 1.0f, 10.45f), Mat = darkSteel });
            r.Boxes.Add(new Box { Min = new Vector3(4.6f, floor + 1.15f, 9.5f), Max = new Vector3(5.4f, floor + 1.55f, 10.3f), Mat = canvas });
            var sMin = FrontEndScenes.ShelterScreenMin;
            var sMax = FrontEndScenes.ShelterScreenMax;
            r.Boxes.Add(new Box { Min = new Vector3(sMin.x + 0.02f, sMin.y - 0.1f, sMax.z - 0.1f), Max = new Vector3(5.1f, sMax.y + 0.1f, sMin.z + 0.1f), Mat = (p, n) => Hex("#2A3230") });
            r.Boxes.Add(new Box { Min = new Vector3(sMin.x, sMin.y, sMax.z), Max = new Vector3(sMin.x + 0.02f, sMax.y, sMin.z), Mat = (p, n) => Frac(p.y / 0.12f) < 0.4f ? Hex("#2E6A3E") : Hex("#1C3A24"), Emissive = true, CastsShadow = false });
            r.Boxes.Add(new Box { Min = new Vector3(3.5f, floor, 3.8f), Max = new Vector3(5.5f, floor + 1.25f, 5.4f), Mat = machine });
            var ex = FrontEndScenes.ShelterExhaust;
            r.Boxes.Add(new Box { Min = new Vector3(ex.x - 0.15f, floor + 1.25f, ex.z - 0.15f), Max = new Vector3(ex.x + 0.15f, ex.y, ex.z + 0.15f), Mat = darkSteel });

            // The door's indicator, and the practical lamps (shade + bulb).
            var dl = FrontEndScenes.ShelterDoorLamp;
            r.Boxes.Add(new Box { Min = new Vector3(dl.x - 0.35f, dl.y - 0.06f, dl.z - 0.04f), Max = new Vector3(dl.x + 0.35f, dl.y + 0.06f, dl.z + 0.02f), Mat = (p, n) => Amber, Emissive = true, CastsShadow = false });
            foreach (var lamp in FrontEndScenes.ShelterLamps)
            {
                r.Boxes.Add(new Box { Min = lamp + new Vector3(-0.32f, 0.08f, -0.32f), Max = lamp + new Vector3(0.32f, 0.3f, 0.32f), Mat = darkSteel, CastsShadow = false });
                r.Boxes.Add(new Box { Min = lamp + new Vector3(-0.1f, -0.06f, -0.1f), Max = lamp + new Vector3(0.1f, 0.08f, 0.1f), Mat = (p, n) => Hex("#FFE2A8"), Emissive = true, CastsShadow = false });
                r.Lamps.Add(new Lamp(lamp + new Vector3(0f, -0.15f, 0f), AmberLight, 1.55f, 3.6f));
            }

            r.Lamps.Add(new Lamp(new Vector3(0f, 1.2f, back - 0.4f), AmberLight, 0.8f, 2.2f));
            r.Lamps.Add(new Lamp(new Vector3(4.0f, -0.6f, 8.7f), new Color(0.3f, 0.75f, 0.4f), 0.35f, 1.4f));

            var c = r.Render(Width, Height);

            // Lamp cords and the patched cable runs strung under the ceiling.
            foreach (var lamp in FrontEndScenes.ShelterLamps)
            {
                var a = r.Project(lamp + new Vector3(0f, 0.3f, 0f));
                var b = r.Project(new Vector3(lamp.x, ceil, lamp.z));
                c.Line(Mathf.RoundToInt(a.x), Mathf.RoundToInt(a.y), Mathf.RoundToInt(b.x), Mathf.RoundToInt(b.y), Hex("#1A1E1E"));
            }

            Catenary(c, r, new Vector3(-hw, ceil - 0.5f, 10f), new Vector3(hw, ceil - 0.6f, 10f), 0.4f, Hex("#3A2420"));
            Catenary(c, r, new Vector3(-hw, ceil - 0.3f, 13f), new Vector3(hw, ceil - 0.3f, 13f), 0.3f, Hex("#2A2E2C"));

            Vignette(c, 0.42f);
            return c;
        }

        private static Color Crate(Vector3 p, Vector3 n, Color tone)
        {
            var u = Mathf.Abs(n.x) > 0.5f ? p.z : p.x;
            var v = Mathf.Abs(n.y) > 0.5f ? p.z : p.y;
            if (Frac(u / 0.45f) < 0.08f || Frac(v / 0.3f) < 0.07f) return tone * 0.62f;
            return n.y > 0.5f ? tone * 1.12f : tone;
        }

        private static Color ShelterFloor(Vector3 p, Vector3 n)
        {
            var c = Hex("#4A4A42");
            // A worn lane straight to the door, and an old canvas rug in the middle of the room.
            if (Mathf.Abs(p.x) < 1.25f) c = Hex("#55534A");
            if (Mathf.Abs(p.x) < 1.6f && p.z > 6f && p.z < 9.5f)
            {
                c = Frac(p.x / 0.25f) < 0.5f ? Hex("#4E5A40") : Hex("#56623F");
                if (Mathf.Abs(p.x) > 1.45f || p.z < 6.15f || p.z > 9.35f) c = Hex("#7A6A48");
            }

            if (Frac(p.x / 1.5f) < 0.025f || Frac(p.z / 1.5f) < 0.03f) c *= 0.72f;
            if (Mathf.Abs(Mathf.Abs(p.x) - 1.45f) < 0.06f && Frac(p.z / 1.2f) < 0.45f && (p.z < 6f || p.z > 9.5f)) c = Hex("#6E5E2A");
            return Grime(c, p.x, p.z, 0.7f, 41);
        }

        private static Color ShelterWall(Vector3 p, Vector3 n)
        {
            var c = Hex("#3E4848");
            if (Frac(p.z / 1.6f) < 0.03f || Frac((p.y + 3f) / 1.3f) < 0.04f) c = Hex("#2C3434");
            if (p.y < -1.8f) c = Hex("#30383A");
            if (p.y > 1.6f && p.y < 1.85f) c = Hex("#525C58"); // structural rail
            return Grime(c, p.z, p.y, 0.8f, 51);
        }

        private static Color ShelterRightWall(Vector3 p, Vector3 n)
        {
            // The tool board behind the bench: hung tools as dark silhouettes.
            if (p.z > 6.8f && p.z < 10.2f && p.y > -0.4f && p.y < 1.05f)
            {
                var c = Hex("#5E4E38");
                var slot = Mathf.FloorToInt(p.z / 0.42f);
                var len = 0.45f + 0.35f * Hash(slot, 1, 61);
                if (Frac(p.z / 0.42f) < 0.22f && p.y > 0.95f - len && p.y < 0.9f) c = Hex("#2E2C28");
                if (p.y > 0.98f || p.y < -0.33f || p.z < 6.88f || p.z > 10.12f) c = Hex("#3A3024");
                return c;
            }

            return ShelterWall(p, n);
        }

        private static Color ShelterEndWall(Vector3 p, Vector3 n)
        {
            var ax = Mathf.Abs(p.x);
            // The transit door: recessed frame, two ribbed leaves, warning chevrons on the jambs.
            if (ax < 1.75f && p.y < 1.35f)
            {
                if (ax > 1.55f || p.y > 1.2f) return Hex("#262E30");
                if (ax > 1.35f) return Frac((p.y + p.x) / 0.3f) < 0.5f ? Hex("#7A6420") : Hex("#202426");
                if (ax < 0.04f) return Hex("#15191A");
                var rib = Frac((p.y + 2.3f) / 0.42f);
                if (rib < 0.16f) return Hex("#55605E");
                if (rib < 0.22f) return Hex("#1E2526");
                return Grime(Hex("#38424A"), p.x, p.y, 0.6f, 71);
            }

            var c = Hex("#3E4848");
            if (Frac((p.x + 6f) / 1.5f) < 0.025f || Frac((p.y + 3f) / 1.2f) < 0.035f) c = Hex("#2A3232");
            var cell = new Vector2Int(Mathf.FloorToInt((p.x + 6f) / 1.5f), Mathf.FloorToInt((p.y + 3f) / 1.2f));
            var patch = Hash(cell.x, cell.y, 73);
            if (patch < 0.12f) c = Hex("#5E5442");
            else if (patch > 0.9f) c = Hex("#5A3A2C");
            if (p.y > 1.9f && p.y < 2.15f) c = Hex("#525C58");
            return Grime(c, p.x, p.y, 0.8f, 74);
        }

        // =====================================================================
        //  Main menu — the transit tunnel to the Shelter
        // =====================================================================

        /// <summary>The Main Menu backdrop: the old transit tunnel that runs to the Shelter door.</summary>
        public static PixelCanvas MainMenu()
        {
            const float hw = FrontEndScenes.MenuHalfWidth;
            const float floor = FrontEndScenes.MenuFloor;
            const float ceil = FrontEndScenes.MenuCeiling;
            const float end = FrontEndScenes.MenuEnd;
            var r = new FrontEndSceneRenderer
            {
                View = FrontEndScenes.MenuView,
                XMin = -hw, XMax = hw, YMin = floor, YMax = ceil, ZEnd = end,
                Ambient = new Color(0.09f, 0.10f, 0.11f),
                Fog = Hex("#0B0F11"),
                FogDensity = 0.034f,
                Floor = TunnelFloor,
                Ceiling = (p, n) => Grime(Frac(p.z / 1.5f) < 0.05f ? Hex("#262A2A") : Mathf.Abs(p.x + 1f) < 0.12f ? Hex("#4A4440") : Hex("#3A3E3C"), p.x, p.z, 0.8f, 81),
                Left = (p, n) => TunnelWall(p, Hex("#3E6A66")),
                Right = (p, n) => TunnelWall(p, Hex("#6E3A2E")),
                End = TunnelEnd
            };

            Surface rib = (p, n) => Grime(Frac((p.y + p.x) / 0.5f) < 0.06f ? Hex("#3A3E3C") : Hex("#5A5E58"), p.x + p.y, p.z, 0.7f, 82);
            for (var z = FrontEndScenes.MenuRibStart; z < end - 1f; z += FrontEndScenes.MenuRibStep)
            {
                r.Boxes.Add(new Box { Min = new Vector3(-hw, floor, z), Max = new Vector3(-hw + 0.22f, ceil, z + 0.45f), Mat = rib });
                r.Boxes.Add(new Box { Min = new Vector3(hw - 0.22f, floor, z), Max = new Vector3(hw, ceil, z + 0.45f), Mat = rib });
                r.Boxes.Add(new Box { Min = new Vector3(-hw, ceil - 0.24f, z), Max = new Vector3(hw, ceil, z + 0.45f), Mat = rib });
            }

            // Emergency lamps on the left wall; most still work.
            for (var i = 0; i < FrontEndScenes.MenuLamps.Length; i++)
            {
                var l = FrontEndScenes.MenuLamps[i];
                r.Boxes.Add(new Box { Min = l + new Vector3(-0.08f, -0.09f, -0.14f), Max = l + new Vector3(0.08f, 0.09f, 0.14f), Mat = (p, n) => Hex("#FFD08A"), Emissive = true, CastsShadow = false });
                r.Lamps.Add(new Lamp(l + new Vector3(0.35f, -0.1f, 0f), AmberLight, i == FrontEndScenes.MenuFlickerLamp ? 0.9f : 1.35f, 3.4f));
            }

            // Dead lamps on the right wall, one per rib.
            for (var z = FrontEndScenes.MenuRibStart + 0.7f; z < end - 2f; z += FrontEndScenes.MenuRibStep)
                r.Boxes.Add(new Box { Min = new Vector3(hw - 0.12f, 1.16f, z - 0.14f), Max = new Vector3(hw - 0.02f, 1.34f, z + 0.14f), Mat = (p, n) => Hex("#3A3530") });

            // The Shelter door's light at the far end, and its indicator.
            var dl = FrontEndScenes.MenuDoorLamp;
            r.Boxes.Add(new Box { Min = new Vector3(dl.x - 0.45f, dl.y - 0.07f, dl.z - 0.04f), Max = new Vector3(dl.x + 0.45f, dl.y + 0.07f, dl.z + 0.03f), Mat = (p, n) => Amber, Emissive = true, CastsShadow = false });
            r.Lamps.Add(new Lamp(new Vector3(0f, 0.6f, end - 1.2f), AmberLight, 3.6f, 5.5f));
            r.Lamps.Add(new Lamp(new Vector3(0f, 0.9f, end - 0.3f), AmberLight, 1.4f, 1.6f));

            // What the tunnel has collected: crates on the walkway, drums, a derailed handcar.
            Surface crateWood = (p, n) => Crate(p, n, Hex("#6E5A40"));
            Surface crateCanvas = (p, n) => Crate(p, n, Hex("#4E5A40"));
            Surface drum = (p, n) => Frac(p.y / 0.3f) < 0.1f ? Hex("#2E2A22") : Hex("#4A3E2A");
            Surface cart = (p, n) => Grime(Frac(p.x / 0.4f) < 0.1f ? Hex("#2A2826") : Hex("#4E4640"), p.x, p.y + p.z, 0.8f, 83);
            r.Boxes.Add(new Box { Min = new Vector3(1.9f, floor, 8.4f), Max = new Vector3(2.75f, floor + 0.75f, 9.2f), Mat = crateWood });
            r.Boxes.Add(new Box { Min = new Vector3(2.0f, floor + 0.75f, 8.55f), Max = new Vector3(2.6f, floor + 1.2f, 9.05f), Mat = crateCanvas });
            r.Boxes.Add(new Box { Min = new Vector3(2.15f, floor, 9.6f), Max = new Vector3(2.75f, floor + 0.55f, 10.2f), Mat = crateCanvas });
            r.Boxes.Add(new Box { Min = new Vector3(-2.75f, floor, 13.2f), Max = new Vector3(-2.2f, floor + 0.9f, 13.75f), Mat = drum });
            r.Boxes.Add(new Box { Min = new Vector3(-2.15f, floor, 13.6f), Max = new Vector3(-1.65f, floor + 0.85f, 14.1f), Mat = drum });
            r.Boxes.Add(new Box { Min = new Vector3(-0.5f, floor + 0.1f, 19f), Max = new Vector3(1.3f, floor + 0.65f, 20.4f), Mat = cart });
            r.Boxes.Add(new Box { Min = new Vector3(-0.3f, floor + 0.65f, 19.4f), Max = new Vector3(0.9f, floor + 0.8f, 20f), Mat = cart });

            var c = r.Render(Width, Height);

            // Cable runs sagging along both walls between the ribs.
            for (var z = FrontEndScenes.MenuRibStart; z < end - FrontEndScenes.MenuRibStep; z += FrontEndScenes.MenuRibStep)
            {
                Catenary(c, r, new Vector3(-hw + 0.25f, 1.75f, z + 0.45f), new Vector3(-hw + 0.25f, 1.75f, z + FrontEndScenes.MenuRibStep), 0.22f, Hex("#15181A"));
                Catenary(c, r, new Vector3(hw - 0.25f, 1.6f, z + 0.45f), new Vector3(hw - 0.25f, 1.6f, z + FrontEndScenes.MenuRibStep), 0.3f, Hex("#15181A"));
            }

            Vignette(c, 0.62f);
            return c;
        }

        private static Color TunnelFloor(Vector3 p, Vector3 n)
        {
            var ax = Mathf.Abs(p.x);
            Color c;
            if (ax < 1.3f)
            {
                c = Hash(Mathf.FloorToInt(p.x * 9f), Mathf.FloorToInt(p.z * 5f), 91) < 0.3f ? Hex("#3A3630") : Hex("#2C2A26");
                if (Frac(p.z / 0.7f) < 0.3f && ax < 1.15f) c = Hex("#4A3A2C");
                if (Mathf.Abs(ax - 0.72f) < 0.05f) c = p.x < 0 == ax < 0.72f ? Hex("#9AA0A0") : Hex("#6A7070");
                if (Mathf.Abs(ax - 0.79f) < 0.02f) c = Hex("#1A1C1C");
            }
            else if (ax < 1.48f) c = Hex("#161818");
            else
            {
                c = Hex("#4E504C");
                if (Frac(p.z / 1.2f) < 0.04f || Mathf.Abs(ax - 2.35f) < 0.025f) c = Hex("#353836");
                if (ax < 1.62f && Noise(p.x, p.z, 0.6f, 92) > 0.3f) c = Hex("#8A7A2A");
                // Standing water on the walkway catches the lamps.
                if (Noise(p.x, p.z, 1.3f, 93) > 0.66f) c = Hex("#262E30");
            }

            return Grime(c, p.x, p.z, 0.7f, 94);
        }

        private static Color TunnelWall(Vector3 p, Color band)
        {
            Color c;
            if (p.y < -1.25f) c = Hex("#2E302E");
            else if (p.y < 0.92f)
            {
                var u = Frac(p.z / 0.34f);
                var v = Frac((p.y + 2f) / 0.26f);
                var tile = Hash(Mathf.FloorToInt(p.z / 0.34f), Mathf.FloorToInt((p.y + 2f) / 0.26f), 95);
                c = u < 0.09f || v < 0.11f ? Hex("#44463F") : tile < 0.14f ? Hex("#34342F") : tile > 0.82f ? Hex("#5E6058") : Hex("#6E7068");
                // Water has run down from the cable runs and stained whole columns of tiles.
                if (Noise(p.z, 0f, 0.9f, 98) > 0.62f && Noise(p.z, p.y, 0.3f, 99) > 0.35f) c *= 0.78f;
            }
            else if (p.y < 1.1f) c = band;
            else c = Hex("#4E504C");
            var low = Mathf.Clamp01((0.9f - p.y) / 2.6f);
            return Grime(c, p.z, p.y, 0.8f + low * 0.2f, 96);
        }

        private static Color TunnelEnd(Vector3 p, Vector3 n)
        {
            var ax = Mathf.Abs(p.x);
            if (ax < 1.1f && p.y < 0.85f)
            {
                if (ax > 0.95f || p.y > 0.76f) return Hex("#262E30");
                if (ax > 0.82f) return Frac((p.y + p.x) / 0.25f) < 0.5f ? Hex("#8A7024") : Hex("#202426");
                if (ax < 0.03f) return Hex("#15191A");
                return Frac((p.y + 2f) / 0.32f) < 0.18f ? Hex("#5E6866") : Hex("#3A444A");
            }

            return Grime(Frac(p.x / 0.8f) < 0.04f ? Hex("#2E3230") : Hex("#3E423E"), p.x, p.y, 0.8f, 97);
        }

        // ---------------- shared light and shade ----------------

        /// <summary>A sagging cable between two world points, drawn in screen space after the render.</summary>
        private static void Catenary(PixelCanvas c, FrontEndSceneRenderer r, Vector3 a, Vector3 b, float sag, Color tone)
        {
            const int steps = 80;
            Vector2? last = null;
            for (var i = 0; i <= steps; i++)
            {
                var t = i / (float)steps;
                var p = Vector3.Lerp(a, b, t) + Vector3.down * (Mathf.Sin(t * Mathf.PI) * sag);
                if (p.z <= 0.5f) continue;
                var s = r.Project(p);
                if (last.HasValue) c.Line(Mathf.RoundToInt(last.Value.x), Mathf.RoundToInt(last.Value.y), Mathf.RoundToInt(s.x), Mathf.RoundToInt(s.y), tone);
                last = s;
            }
        }

        /// <summary>
        /// Darkens toward the frame edges.
        ///
        /// This is here for the UI as much as for the mood: the header, tab bar, footer and side columns all sit on
        /// the outer edges, and a quiet backdrop underneath them is what lets thin 1 px chrome stay readable.
        /// </summary>
        private static void Vignette(PixelCanvas c, float strength)
        {
            var cx = Width / 2f;
            var cy = Height / 2f;
            var max = Mathf.Sqrt(cx * cx + cy * cy);
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / max;
                var a = Mathf.Clamp01((d - 0.35f) / 0.65f) * strength;
                if (a <= 0f) continue;
                c.Set(x, y, Color32.Lerp(c.Get(x, y), (Color32)Dark, a));
            }
        }
    }
}

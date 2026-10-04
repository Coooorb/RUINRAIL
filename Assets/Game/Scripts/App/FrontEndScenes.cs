using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// The two front-end places — the transit tunnel the Main Menu looks down, and the Shelter room the hub is staged
    /// in — as shared scene facts: the camera each backdrop is rendered from and the world positions of everything
    /// that lives in it (lamps, the door indicator, the terminal, the generator exhaust, a leak). The editor art
    /// generator renders the backdrops from these, and <see cref="FrontEndAmbience"/> animates the same spots at
    /// runtime, so a flickering lamp always flickers exactly where the baked lamp hangs.
    /// Coordinates: x right, y up, z into the screen; the camera sits at the origin. Screen pixels are y-up from the
    /// bottom-left of the 640x360 reference.
    /// </summary>
    public static class FrontEndScenes
    {
        public readonly struct View
        {
            public View(float focal, float cx, float cy) { Focal = focal; Cx = cx; Cy = cy; }
            public float Focal { get; }
            public float Cx { get; }
            public float Cy { get; }

            /// <summary>Screen pixel (y-up) of a world point in front of the camera.</summary>
            public Vector2 Project(Vector3 p) => new(Cx + Focal * p.x / p.z, Cy + Focal * p.y / p.z);
        }

        // ---------------- Main Menu: the transit tunnel to the Shelter door ----------------

        public static readonly View MenuView = new(250f, 340f, 196f);
        public const float MenuHalfWidth = 3.2f;
        public const float MenuFloor = -1.7f;
        public const float MenuCeiling = 2.3f;
        public const float MenuEnd = 34f;
        /// <summary>Structural ribs along the tunnel, every <see cref="MenuRibStep"/> from <see cref="MenuRibStart"/>.</summary>
        public const float MenuRibStart = 4f;
        public const float MenuRibStep = 6f;

        /// <summary>Emergency lamps on the left wall, one per rib; the second one is the unreliable one.</summary>
        public static readonly Vector3[] MenuLamps =
        {
            new(-3.0f, 1.25f, 4.7f), new(-3.0f, 1.25f, 10.7f), new(-3.0f, 1.25f, 16.7f), new(-3.0f, 1.25f, 22.7f), new(-3.0f, 1.25f, 28.7f)
        };

        public const int MenuFlickerLamp = 1;
        /// <summary>The powered indicator above the Shelter door at the tunnel's end.</summary>
        public static readonly Vector3 MenuDoorLamp = new(0f, 0.95f, MenuEnd - 0.05f);
        /// <summary>A ceiling leak dripping into a puddle on the right walkway.</summary>
        public static readonly Vector3 MenuDripTop = new(2.3f, MenuCeiling, 7.5f);
        public static readonly Vector3 MenuDripPuddle = new(2.3f, MenuFloor, 7.5f);

        // ---------------- Shelter: the room ----------------

        public static readonly View ShelterView = new(300f, 320f, 196f);
        public const float ShelterHalfWidth = 6f;
        public const float ShelterFloor = -2.3f;
        public const float ShelterCeiling = 3.1f;
        public const float ShelterBack = 14f;

        /// <summary>The hanging practical lamps.</summary>
        public static readonly Vector3[] ShelterLamps = { new(-3.4f, 2.05f, 8.5f), new(0f, 2.35f, 11f), new(3.4f, 2.05f, 8.5f) };
        public const int ShelterFlickerLamp = 2;
        /// <summary>The transit door's status indicator, centre top of the door.</summary>
        public static readonly Vector3 ShelterDoorLamp = new(0f, 1.45f, ShelterBack - 0.02f);
        /// <summary>The terminal on the work bench: its screen corners (bottom-left, top-right).</summary>
        public static readonly Vector3 ShelterScreenMin = new(4.25f, -0.95f, 9.2f);
        public static readonly Vector3 ShelterScreenMax = new(4.25f, -0.25f, 8.2f);
        /// <summary>The top of the generator's exhaust stack, where its vapour rises.</summary>
        public static readonly Vector3 ShelterExhaust = new(4.6f, 1.6f, 4.6f);
    }
}

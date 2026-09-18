using UnityEngine;

namespace RuinRail.UI.Theme
{
    /// <summary>
    /// The visual state a control is in. Focus and Active are separate because both can be true at once: the tab you
    /// are standing on stays selected while the keyboard focus moves elsewhere and comes back.
    /// </summary>
    public enum ControlState
    {
        Normal,
        Hover,
        Focused,
        Active,
        Pressed,
        Disabled
    }

    /// <summary>What kind of control a style describes; each role carries its own weight in the screen hierarchy.</summary>
    public enum ControlRole
    {
        /// <summary>A content tab in the primary navigation bar.</summary>
        Tab,
        /// <summary>The strongest call to action on a screen (PLAY, START EXPEDITION, TRANSIT).</summary>
        Primary,
        /// <summary>An ordinary button inside a content panel.</summary>
        Button,
        /// <summary>A compact row in a list.</summary>
        Row,
        /// <summary>A screen-leaving action, deliberately styled apart from the content tabs.</summary>
        Exit
    }

    /// <summary>
    /// FINAL_ART_PRODUCTION_SPEC section 18 expressed as runtime colours and metrics: post-apocalyptic industrial
    /// terminal chrome on a 4 px spacing grid, dark charcoal plates with thin warm accents and restrained terminal
    /// green.
    ///
    /// The values come from the same palette families the generated art uses (spec section 3), so code-built chrome
    /// and generated sprites sit in one colour world. They live in the runtime assembly because the editor art
    /// generator cannot be referenced from a player build.
    /// </summary>
    public static class UiTheme
    {
        // ---- 18.2: the 4 px spacing grid ----
        public const int Unit = 4;
        public const int PadSmall = 4;
        public const int Pad = 8;
        public const int PadLarge = 12;

        // ---- reference layout bands at 640x360 (art/101) ----
        public const int ScreenWidth = 640;
        public const int ScreenHeight = 360;
        public const int HeaderHeight = 30;
        public const int TabBarHeight = 22;
        public const int FooterHeight = 18;
        public const int ScreenMargin = 6;

        /// <summary>Top of the content band, measured downward from the top of the screen.</summary>
        public const int ContentTop = HeaderHeight + TabBarHeight;
        public const int ContentBottom = ScreenHeight - FooterHeight;
        public const int ContentHeight = ContentBottom - ContentTop;

        // ---- neutral chrome (spec 3.1) ----
        public static readonly Color NearBlack = Hex("#111518");
        public static readonly Color Charcoal = Hex("#1B2023");
        public static readonly Color PanelEdge = Hex("#394346");
        public static readonly Color PanelEdgeSoft = Hex("#2A3234");
        public static readonly Color Ink = Hex("#DCE0D8");
        public static readonly Color InkMuted = Hex("#8E9894");
        public static readonly Color InkFaint = Hex("#68736F");
        public static readonly Color InkDisabled = Hex("#4A5354");

        // ---- warm and tech accents (spec 3.2, 3.4) ----
        public static readonly Color Amber = Hex("#E7A74A");
        public static readonly Color AmberDim = Hex("#8A6329");
        public static readonly Color Rust = Hex("#8E4327");
        public static readonly Color Terminal = Hex("#6FBF8C");
        public static readonly Color Cyan = Hex("#52A9A4");
        public static readonly Color Danger = Hex("#C84B42");

        /// <summary>A content plate over the shelter backdrop: dark enough to read on, open enough to show the room.</summary>
        public static readonly Color Plate = new(0.086f, 0.106f, 0.114f, 0.88f);
        /// <summary>Header, tab bar and footer chrome, which sit solidly in front of the scene.</summary>
        public static readonly Color ChromeBar = new(0.055f, 0.070f, 0.078f, 0.96f);

        public static Color Hex(string hex)
        {
            var h = hex.TrimStart('#');
            var r = System.Convert.ToInt32(h.Substring(0, 2), 16) / 255f;
            var g = System.Convert.ToInt32(h.Substring(2, 2), 16) / 255f;
            var b = System.Convert.ToInt32(h.Substring(4, 2), 16) / 255f;
            var a = h.Length >= 8 ? System.Convert.ToInt32(h.Substring(6, 2), 16) / 255f : 1f;
            return new Color(r, g, b, a);
        }

        /// <summary>How one control is drawn in one state.</summary>
        public readonly struct ControlVisual
        {
            public ControlVisual(Color fill, Color edge, Color label, Color marker, bool showMarker, bool showBrackets, int inset)
            {
                Fill = fill; Edge = edge; Label = label; Marker = marker;
                ShowMarker = showMarker; ShowBrackets = showBrackets; Inset = inset;
            }

            public Color Fill { get; }
            public Color Edge { get; }
            public Color Label { get; }
            /// <summary>Colour of the persistent selected-state side notch.</summary>
            public Color Marker { get; }
            /// <summary>Whether the selected marker is drawn. It survives the pointer leaving the control.</summary>
            public bool ShowMarker { get; }
            /// <summary>Whether focus corner brackets are drawn. Focus is a shape change, never colour alone (18.4).</summary>
            public bool ShowBrackets { get; }
            /// <summary>Pixels the label is pushed down and right, so a pressed control reads as depressed.</summary>
            public int Inset { get; }
        }

        /// <summary>
        /// The state styling table. Every state differs from Normal in fill <em>and</em> in at least one non-colour
        /// cue (bracket, marker or inset), so the states stay distinguishable without colour perception (spec 18.4).
        /// </summary>
        public static ControlVisual Visual(ControlRole role, ControlState state)
        {
            var accent = role == ControlRole.Exit ? Hex("#D2724A") : Amber;

            switch (state)
            {
                case ControlState.Disabled:
                    return new ControlVisual(Hex("#161B1D"), Hex("#272D2F"), InkDisabled, InkDisabled, false, false, 0);

                case ControlState.Pressed:
                    // Visibly depressed: darker plate, accent edge, label pushed 1 px down and right (18.4).
                    return new ControlVisual(Darken(BaseFill(role), 0.35f), accent, Ink, accent, true, false, 1);

                case ControlState.Active:
                    // The persistent selected state. Strongest plate of the set plus the side notch.
                    return new ControlVisual(
                        role == ControlRole.Primary ? Hex("#5A4016") : Hex("#2E383B"),
                        accent,
                        role == ControlRole.Primary ? Hex("#FFE9C4") : Hex("#F0F2EA"),
                        accent, true, false, 0);

                case ControlState.Focused:
                    // Focus owns the corner brackets; the resolver merges it with Active when both hold.
                    return new ControlVisual(Lighten(BaseFill(role), 0.12f), accent, Ink, accent, false, true, 0);

                case ControlState.Hover:
                    return new ControlVisual(Lighten(BaseFill(role), 0.09f), Hex("#63706F"), Ink, accent, false, false, 0);

                default:
                    return new ControlVisual(
                        BaseFill(role),
                        role == ControlRole.Primary ? AmberDim : PanelEdgeSoft,
                        role == ControlRole.Row ? Hex("#C2C9C1") : Ink,
                        accent, false, false, 0);
            }
        }

        private static Color BaseFill(ControlRole role) => role switch
        {
            ControlRole.Primary => Hex("#3E2C12"),
            ControlRole.Exit => Hex("#2A1D18"),
            ControlRole.Row => Hex("#1C2224"),
            _ => Hex("#232A2C")
        };

        public static Color Darken(Color c, float t) => Color.Lerp(c, NearBlack, Mathf.Clamp01(t));
        public static Color Lighten(Color c, float t) => Color.Lerp(c, new Color(0.98f, 0.96f, 0.93f), Mathf.Clamp01(t));

        public static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);
    }
}

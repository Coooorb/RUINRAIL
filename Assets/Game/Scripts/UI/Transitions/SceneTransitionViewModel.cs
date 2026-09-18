using System;
using RuinRail.Core;

namespace RuinRail.UI.Transitions
{
    /// <summary>Which transition is running, so the overlay can label it truthfully instead of saying "Loading…" for everything.</summary>
    public enum TransitionKind
    {
        None,
        BootToMainMenu,
        MainMenuToShelter,
        ShelterToExpedition,
        DepthTransit,
        ExpeditionToShelter,
        ShelterToMainMenu
    }

    public enum TransitionPhase
    {
        Idle,
        /// <summary>Covering the screen before the load starts, so no raw frame is ever presented.</summary>
        CoveringIn,
        Loading,
        /// <summary>Uncovering once the destination has composed.</summary>
        Revealing
    }

    /// <summary>
    /// TASK 178 — the state behind the loading/transition overlay.
    ///
    /// Two things make this worth a type rather than a coroutine. First, the screen must be covered *before* the load
    /// begins and uncovered only after the destination has composed, so a transition never shows a raw or blank frame.
    /// Second, input has to be dead for that entire window: the double-submit bug this prevents is a player pressing
    /// START twice and launching two expeditions, which save/transaction code should never have to defend against.
    ///
    /// It deliberately has no progress percentage. `SceneManager.LoadScene` is synchronous and exposes no real metric,
    /// and a fake bar that jumps 0 to 100 is a lie told to the player (requirement 4). The overlay shows an
    /// indeterminate treatment instead.
    /// </summary>
    public sealed class SceneTransitionViewModel
    {
        /// <summary>Seconds the cover takes. Short enough not to feel like a stall, long enough to read as deliberate.</summary>
        public const float CoverSeconds = 0.18f;
        public const float RevealSeconds = 0.22f;

        private float _elapsed;

        public TransitionPhase Phase { get; private set; } = TransitionPhase.Idle;
        public TransitionKind Kind { get; private set; } = TransitionKind.None;
        public string Destination { get; private set; } = string.Empty;

        /// <summary>True while the overlay is on screen at all, so the renderer knows to draw it.</summary>
        public bool IsTransitioning => Phase != TransitionPhase.Idle;

        /// <summary>
        /// The window where input must be dead: from the moment the cover starts until the destination has composed.
        /// That is where a double-submit does damage — a second START before the load would launch two expeditions.
        ///
        /// Once the destination has composed the scene is live and interactive, and the reveal is only a fade. Gating
        /// input through the reveal as well would drop a legitimate next navigation on the floor.
        /// </summary>
        public bool BlocksInput => Phase == TransitionPhase.CoveringIn || Phase == TransitionPhase.Loading;

        /// <summary>The overlay's opacity: fully opaque across the load, so no raw frame is ever visible.</summary>
        public float CoverAlpha => Phase switch
        {
            TransitionPhase.CoveringIn => CoverSeconds <= 0f ? 1f : Clamp01(_elapsed / CoverSeconds),
            TransitionPhase.Loading => 1f,
            TransitionPhase.Revealing => RevealSeconds <= 0f ? 0f : 1f - Clamp01(_elapsed / RevealSeconds),
            _ => 0f
        };

        /// <summary>No fake percentage: the overlay is indeterminate because no real progress metric exists.</summary>
        public bool ShowsIndeterminateProgress => Phase == TransitionPhase.Loading;

        /// <summary>Raised when the cover is complete and the scene load should actually happen.</summary>
        public event Action<string> LoadRequested;

        public event Action<TransitionKind> Completed;

        public string Label => Kind switch
        {
            TransitionKind.BootToMainMenu => "STARTING",
            TransitionKind.MainMenuToShelter => "ENTERING THE SHELTER",
            TransitionKind.ShelterToExpedition => "DEPARTING",
            TransitionKind.DepthTransit => "DESCENDING",
            TransitionKind.ExpeditionToShelter => "RETURNING",
            TransitionKind.ShelterToMainMenu => "LEAVING",
            _ => string.Empty
        };

        /// <summary>
        /// Starts a transition. A second call while the screen is covering or loading is ignored rather than queued —
        /// that is the double-submit guard, and it returns false so the caller can tell it did nothing. A call during
        /// the reveal is honoured: the destination is already live, so that is a real new navigation, not a stray
        /// second press, and it simply re-covers.
        /// </summary>
        public bool Begin(TransitionKind kind, string destinationScene)
        {
            if (BlocksInput || kind == TransitionKind.None) return false;
            if (string.IsNullOrEmpty(destinationScene)) throw new ArgumentException("A transition needs a destination scene.", nameof(destinationScene));

            Kind = kind;
            Destination = destinationScene;
            Phase = TransitionPhase.CoveringIn;
            _elapsed = 0f;
            return true;
        }

        public void Tick(float deltaTime)
        {
            if (!IsTransitioning) return;
            _elapsed += Math.Max(0f, deltaTime);

            switch (Phase)
            {
                case TransitionPhase.CoveringIn when _elapsed >= CoverSeconds:
                    // Fully covered: only now is it safe to load, so the swap is never visible.
                    Phase = TransitionPhase.Loading;
                    _elapsed = 0f;
                    LoadRequested?.Invoke(Destination);
                    break;

                case TransitionPhase.Revealing when _elapsed >= RevealSeconds:
                    Phase = TransitionPhase.Idle;
                    _elapsed = 0f;
                    var finished = Kind;
                    Kind = TransitionKind.None;
                    Destination = string.Empty;
                    Completed?.Invoke(finished);
                    break;
            }
        }

        /// <summary>Called once the destination scene has composed. Until this arrives the cover stays opaque.</summary>
        public void DestinationComposed()
        {
            if (Phase != TransitionPhase.Loading) return;
            Phase = TransitionPhase.Revealing;
            _elapsed = 0f;
        }

        /// <summary>Drops the overlay immediately. Used when an error takes over the screen.</summary>
        public void Abort()
        {
            Phase = TransitionPhase.Idle;
            Kind = TransitionKind.None;
            Destination = string.Empty;
            _elapsed = 0f;
        }

        public static TransitionKind KindFor(string fromScene, string toScene)
        {
            if (toScene == SceneNames.MainMenu) return fromScene == SceneNames.Base ? TransitionKind.ShelterToMainMenu : TransitionKind.BootToMainMenu;
            if (toScene == SceneNames.Base) return fromScene == SceneNames.Dungeon ? TransitionKind.ExpeditionToShelter : TransitionKind.MainMenuToShelter;
            if (toScene == SceneNames.Dungeon) return fromScene == SceneNames.Dungeon ? TransitionKind.DepthTransit : TransitionKind.ShelterToExpedition;
            return TransitionKind.None;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}

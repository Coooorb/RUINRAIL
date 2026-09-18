using System;
using System.Collections.Generic;

namespace RuinRail.UI.Transitions
{
    /// <summary>What went wrong, which decides what the player can safely do next.</summary>
    public enum RecoverableErrorKind
    {
        /// <summary>Sessions/Relay connect, host or join failure.</summary>
        ServiceConnection,
        /// <summary>A join code was rejected or the session was full/gone.</summary>
        SessionJoin,
        /// <summary>A save could not be committed. The previous save is intact.</summary>
        SaveFailed,
        /// <summary>A save could not be read back.</summary>
        LoadFailed,
        /// <summary>A scene failed to compose.</summary>
        SceneLoadFailed
    }

    /// <summary>One thing the player can do about it. The first is the default focus.</summary>
    public enum ErrorAction
    {
        Retry,
        BackToMainMenu,
        BackToShelter,
        ContinueOffline,
        Dismiss
    }

    /// <summary>
    /// TASK 178 — the recoverable-error screen.
    ///
    /// Three rules shape this type. It never shows a raw exception: the player gets a sentence in their own language
    /// about what happened and what is safe to do. It always offers at least one action that leads somewhere safe, so
    /// an error is never a dead end. And it never swallows the underlying failure — `Technical` keeps the original
    /// detail for the diagnostics the save and networking layers already write (requirement 6).
    /// </summary>
    public sealed class RecoverableErrorViewModel
    {
        public bool IsShowing { get; private set; }
        public RecoverableErrorKind Kind { get; private set; }
        public string Title { get; private set; } = string.Empty;
        public string Message { get; private set; } = string.Empty;
        /// <summary>The original failure detail, preserved for diagnostics. Never rendered as the player-facing message.</summary>
        public string Technical { get; private set; } = string.Empty;
        public IReadOnlyList<ErrorAction> Actions { get; private set; } = Array.Empty<ErrorAction>();

        /// <summary>The action focused by default: always a safe one, never a destructive or repeating one.</summary>
        public ErrorAction DefaultAction => Actions.Count > 0 ? Actions[0] : ErrorAction.Dismiss;

        public event Action<ErrorAction> ActionChosen;

        public void Show(RecoverableErrorKind kind, string technical = "")
        {
            Kind = kind;
            Technical = technical ?? string.Empty;
            IsShowing = true;

            switch (kind)
            {
                case RecoverableErrorKind.ServiceConnection:
                    Title = "CONNECTION FAILED";
                    Message = "Could not reach the online service. You can try again, or keep playing on your own.";
                    Actions = new[] { ErrorAction.Retry, ErrorAction.ContinueOffline, ErrorAction.BackToMainMenu };
                    break;

                case RecoverableErrorKind.SessionJoin:
                    Title = "COULD NOT JOIN";
                    Message = "That session could not be joined. The code may be wrong, or the expedition may already be full.";
                    Actions = new[] { ErrorAction.Retry, ErrorAction.BackToMainMenu };
                    break;

                case RecoverableErrorKind.SaveFailed:
                    Title = "SAVE FAILED";
                    Message = "Your progress could not be written. Your previous save is intact and has not been damaged.";
                    Actions = new[] { ErrorAction.Retry, ErrorAction.Dismiss };
                    break;

                case RecoverableErrorKind.LoadFailed:
                    Title = "SAVE COULD NOT BE READ";
                    Message = "This save slot could not be loaded. Nothing has been overwritten.";
                    Actions = new[] { ErrorAction.Retry, ErrorAction.BackToMainMenu };
                    break;

                case RecoverableErrorKind.SceneLoadFailed:
                    Title = "SOMETHING WENT WRONG";
                    Message = "That area could not be opened. You can return to the Shelter and try again.";
                    Actions = new[] { ErrorAction.BackToShelter, ErrorAction.BackToMainMenu };
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public void Choose(ErrorAction action)
        {
            if (!IsShowing) return;
            if (!Contains(action)) throw new InvalidOperationException($"{action} is not offered for {Kind}.");
            IsShowing = false;
            ActionChosen?.Invoke(action);
        }

        public void Dismiss()
        {
            if (!IsShowing) return;
            IsShowing = false;
        }

        public static string Label(ErrorAction action) => action switch
        {
            ErrorAction.Retry => "TRY AGAIN",
            ErrorAction.BackToMainMenu => "MAIN MENU",
            ErrorAction.BackToShelter => "RETURN TO SHELTER",
            ErrorAction.ContinueOffline => "PLAY SOLO",
            ErrorAction.Dismiss => "CONTINUE",
            _ => action.ToString().ToUpperInvariant()
        };

        private bool Contains(ErrorAction action)
        {
            for (var i = 0; i < Actions.Count; i++) if (Actions[i] == action) return true;
            return false;
        }
    }
}

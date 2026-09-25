using System;
using RuinRail.Core.Input;
using RuinRail.UI.Inventory;
using RuinRail.UI.Settings;

namespace RuinRail.UI.Pause
{
    public enum PauseMenuItem
    {
        Resume,
        Settings,
        Help,
        ReturnToMainMenu,
        QuitGame
    }

    public enum PauseScreen
    {
        Closed,
        Root,
        Settings,
        /// <summary>The Help / Codex page over the pause root.</summary>
        Help,
        /// <summary>The destructive-leave confirmation for RETURN TO MAIN MENU.</summary>
        ConfirmReturn,
        /// <summary>The confirmation for QUIT GAME.</summary>
        ConfirmQuit
    }

    /// <summary>
    /// Esc / Menu flow (116 Pause action). Solo: opening pauses the world through <see cref="IWorldPause"/> (time
    /// scale) — the same counted pause the inventory uses, so the two overlap safely. Co-op: the menu is local UI only;
    /// the authoritative simulation and the other players continue and nothing here reaches the network. Pause while
    /// on the Settings screen goes back (applying settings); on a confirmation it cancels; while a rebind is listening,
    /// Esc belongs to the rebind operation and is ignored here.
    ///
    /// RETURN TO MAIN MENU and QUIT GAME are both confirmed first. During an active expedition they follow the
    /// approved loss rules (85 Solo Quit: the run is not resumable, carried loot and coins are at risk and lost): the
    /// owner passes the existing expedition-fail/leave path as <c>returnToMenu</c> — this view model never resolves an
    /// expedition itself and never invents a second loss implementation. Outside an expedition (the Shelter) leaving
    /// is a plain save-and-leave with nothing to fail.
    /// </summary>
    public sealed class PauseMenuViewModel : IDisposable
    {
        public static readonly PauseMenuItem[] Items = { PauseMenuItem.Resume, PauseMenuItem.Settings, PauseMenuItem.Help, PauseMenuItem.ReturnToMainMenu, PauseMenuItem.QuitGame };

        private readonly IPlayerInputReader _reader;
        private readonly IWorldPause _worldPause;
        private readonly SettingsViewModel _settings;
        private readonly Action _quit;
        private readonly Action _returnToMenu;
        private readonly Func<bool> _expeditionActive;
        private int _selected;

        public PauseMenuViewModel(IPlayerInputReader reader, IWorldPause worldPause, bool isCoop, SettingsViewModel settings = null, Action quit = null,
            Action returnToMenu = null, Func<bool> expeditionActive = null)
        {
            _reader = reader;
            _worldPause = worldPause;
            IsCoop = isCoop;
            _settings = settings;
            _quit = quit;
            _returnToMenu = returnToMenu;
            _expeditionActive = expeditionActive;
            if (_reader != null) _reader.PauseToggled += OnPauseToggled;
        }

        public bool IsCoop { get; }
        public PauseScreen Screen { get; private set; } = PauseScreen.Closed;
        public bool IsOpen => Screen != PauseScreen.Closed;
        public bool IsConfirming => Screen == PauseScreen.ConfirmReturn || Screen == PauseScreen.ConfirmQuit;
        /// <summary>True only while the solo world is held by this menu; never in co-op.</summary>
        public bool IsWorldPaused { get; private set; }
        public PauseMenuItem Selected => Items[_selected];
        public SettingsViewModel Settings => _settings;
        public int Opens { get; private set; }
        public int QuitRequests { get; private set; }
        public int ReturnRequests { get; private set; }
        /// <summary>True when leaving now ends an active expedition under the loss rules.</summary>
        public bool ExpeditionActive => _expeditionActive != null && _expeditionActive();
        public string Title => !IsOpen ? string.Empty : IsCoop ? "MENU — the expedition continues" : "PAUSED";

        public event Action Changed;

        public static string Label(PauseMenuItem item) => item switch
        {
            PauseMenuItem.Resume => "RESUME",
            PauseMenuItem.Settings => "SETTINGS",
            PauseMenuItem.Help => "HELP",
            PauseMenuItem.ReturnToMainMenu => "RETURN TO MAIN MENU",
            PauseMenuItem.QuitGame => "QUIT GAME",
            _ => item.ToString()
        };

        /// <summary>The confirmation text for the open confirmation screen (empty otherwise).</summary>
        public string ConfirmationTitle => Screen switch
        {
            PauseScreen.ConfirmReturn => "RETURN TO MAIN MENU?",
            PauseScreen.ConfirmQuit => "QUIT GAME?",
            _ => string.Empty
        };

        public string ConfirmationText
        {
            get
            {
                if (!IsConfirming) return string.Empty;
                if (ExpeditionActive)
                {
                    return IsCoop
                        ? "Leaving now abandons the expedition for you. It counts as failed: everything you are carrying — items and Coins — is lost. XP, banked Coins and Storage are safe."
                        : "Leaving now ends the expedition. It counts as failed: everything you are carrying — items and Coins — is lost and the run cannot be resumed. XP, banked Coins and Storage are safe.";
                }

                return Screen == PauseScreen.ConfirmQuit ? "Your profile is saved. Quit the game?" : "Your profile is saved. Return to the main menu?";
            }
        }

        public void Open()
        {
            if (IsOpen) return;
            Screen = PauseScreen.Root;
            _selected = 0;
            Opens++;
            RuinRail.Core.Input.GameplayInputGate.Hold(); // a click on RESUME is never also a shot
            RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Confirm);
            if (!IsCoop && _worldPause != null)
            {
                _worldPause.Pause();
                IsWorldPaused = true;
            }

            Raise();
        }

        public void Close()
        {
            if (!IsOpen) return;
            if (Screen == PauseScreen.Settings) LeaveSettings(apply: true);
            if (Screen == PauseScreen.Help) LeaveHelp();
            Screen = PauseScreen.Closed;
            RuinRail.Core.Input.GameplayInputGate.Release();
            RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Cancel);
            if (IsWorldPaused)
            {
                _worldPause?.Resume();
                IsWorldPaused = false;
            }

            Raise();
        }

        public void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }

        public void MoveSelection(int delta)
        {
            if (Screen != PauseScreen.Root) return;
            _selected = ((_selected + delta) % Items.Length + Items.Length) % Items.Length;
            RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Navigate);
            Raise();
        }

        public void Activate() => Activate(Selected);

        public void Activate(PauseMenuItem item)
        {
            if (Screen != PauseScreen.Root) return;
            switch (item)
            {
                case PauseMenuItem.Resume:
                    Close();
                    break;
                case PauseMenuItem.Settings:
                    if (_settings == null) return;
                    _settings.ResetToCategories(); // Settings always opens on its category list
                    _settings.CloseRequested = () => { if (Screen == PauseScreen.Settings) LeaveSettings(apply: true); };
                    Screen = PauseScreen.Settings;
                    Raise();
                    break;
                case PauseMenuItem.Help:
                    Screen = PauseScreen.Help;
                    Raise();
                    break;
                case PauseMenuItem.ReturnToMainMenu:
                    Screen = PauseScreen.ConfirmReturn;
                    Raise();
                    break;
                case PauseMenuItem.QuitGame:
                    Screen = PauseScreen.ConfirmQuit;
                    Raise();
                    break;
            }
        }

        /// <summary>Confirms the open confirmation: runs the owner's leave/quit path exactly once per confirmation.</summary>
        public void Confirm()
        {
            switch (Screen)
            {
                case PauseScreen.ConfirmReturn:
                    ReturnRequests++;
                    ReleaseWorld();
                    Screen = PauseScreen.Closed;
                    Raise();
                    _returnToMenu?.Invoke();
                    break;
                case PauseScreen.ConfirmQuit:
                    QuitRequests++;
                    ReleaseWorld();
                    Raise();
                    _quit?.Invoke();
                    break;
            }
        }

        /// <summary>Back out of a confirmation to the root menu.</summary>
        public void CancelConfirmation()
        {
            if (!IsConfirming) return;
            Screen = PauseScreen.Root;
            RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Cancel);
            Raise();
        }

        /// <summary>Back from Settings to the root; applies (persists) by default so a rebind is never lost by leaving.</summary>
        public void LeaveSettings(bool apply)
        {
            if (Screen != PauseScreen.Settings) return;
            if (apply) { _settings?.BackFromPage(); _settings?.Apply(); } else _settings?.Discard();
            _settings?.ResetToCategories();
            Screen = PauseScreen.Root;
            Raise();
        }

        /// <summary>Back from the Help page to the pause root. It holds no state, so leaving it costs nothing.</summary>
        public void LeaveHelp()
        {
            if (Screen != PauseScreen.Help) return;
            Screen = PauseScreen.Root;
            Raise();
        }

        /// <summary>Back (Esc / B): a nested panel owns it first — a settings page returns to the categories, the categories return here; on the root it resumes.</summary>
        public void Back()
        {
            if (IsConfirming) { CancelConfirmation(); return; }
            if (Screen == PauseScreen.Settings)
            {
                if (_settings != null && _settings.BackFromPage()) return; // page -> categories, still in Settings
                LeaveSettings(apply: true);
                return;
            }

            if (Screen == PauseScreen.Help) { LeaveHelp(); return; }
            if (Screen == PauseScreen.Root) Close();
        }

        private void ReleaseWorld()
        {
            // The scene is about to be torn down: never leave the time scale held by a menu that no longer exists.
            if (!IsWorldPaused) return;
            _worldPause?.Resume();
            IsWorldPaused = false;
        }

        /// <summary>
        /// Another layer over gameplay (the inventory) may claim the Pause press: when it returns true the press is
        /// consumed there (the inventory closes) and the pause menu stays as it is.
        /// </summary>
        public Func<bool> BeforePauseToggle { get; set; }

        private void OnPauseToggled() => HandlePauseInput();

        /// <summary>The Pause action (Esc / Menu) exactly as the input reader delivers it — the owner's veto first, then settings/confirmation/toggle.</summary>
        public void HandlePauseInput()
        {
            if (!IsOpen && BeforePauseToggle != null && BeforePauseToggle()) return;
            if (_settings != null && _settings.IsListening) return;
            if (IsConfirming) { CancelConfirmation(); return; }
            if (Screen == PauseScreen.Settings || Screen == PauseScreen.Help) { Back(); return; }
            Toggle();
        }

        public void Dispose()
        {
            if (_reader != null) _reader.PauseToggled -= OnPauseToggled;
            if (IsOpen) RuinRail.Core.Input.GameplayInputGate.Release();
            if (IsWorldPaused) { _worldPause?.Resume(); IsWorldPaused = false; }
        }

        private void Raise() => Changed?.Invoke();
    }
}

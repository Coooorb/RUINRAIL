using System;
using RuinRail.Persistence;
using RuinRail.UI.Settings;

namespace RuinRail.UI.Base
{
    public enum MainMenuEntry
    {
        Play,
        Settings,
        Help,
        Quit
    }

    public enum MainMenuState
    {
        Menu,
        Base,
        Settings,
        Help,
        SaveError,
        Quitting
    }

    public enum PlayOutcome
    {
        NewProfile,
        Continued,
        Recovered,
        Failed
    }

    /// <summary>
    /// Main menu (94): PLAY / SETTINGS / QUIT. PLAY loads the single save slot and enters the Base — a missing save
    /// starts a new profile, a recovered/migrated one continues with a notice, an unreadable one is surfaced with an
    /// explicit reset offer (never silently overwritten). Multiplayer organization happens inside the Base.
    /// </summary>
    public sealed class MainMenuViewModel
    {
        private readonly SaveSlotService _saves;
        private readonly BaseConfigs _configs;

        public MainMenuViewModel(SaveSlotService saves, BaseConfigs configs)
        {
            _saves = saves ?? throw new ArgumentNullException(nameof(saves));
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
        }

        public static readonly MainMenuEntry[] Entries = { MainMenuEntry.Play, MainMenuEntry.Settings, MainMenuEntry.Help, MainMenuEntry.Quit };
        public MainMenuState State { get; private set; } = MainMenuState.Menu;
        public BaseSession Session { get; private set; }
        public PlayOutcome? LastOutcome { get; private set; }
        public string Message { get; private set; } = string.Empty;
        public bool HasSave => _saves.HasSave;
        public AbandonedExpeditionReport AbandonedExpedition { get; private set; }
        public SaveDiagnostics LastDiagnostics { get; private set; }
        /// <summary>The Settings page behind SETTINGS (TASK 135); optional so menu tests without input assets still work.</summary>
        public SettingsViewModel Settings { get; private set; }

        public event Action<MainMenuViewModel> Changed;

        public static string Label(MainMenuEntry entry) => entry switch
        {
            MainMenuEntry.Play => "PLAY",
            MainMenuEntry.Settings => "SETTINGS",
            MainMenuEntry.Help => "HELP",
            MainMenuEntry.Quit => "QUIT",
            _ => entry.ToString().ToUpperInvariant()
        };

        public void Select(MainMenuEntry entry)
        {
            switch (entry)
            {
                case MainMenuEntry.Play: Play(); break;
                case MainMenuEntry.Settings: State = MainMenuState.Settings; Raise(); break;
                case MainMenuEntry.Help: State = MainMenuState.Help; Raise(); break;
                case MainMenuEntry.Quit: State = MainMenuState.Quitting; Raise(); break;
            }
        }

        public void SetSettings(SettingsViewModel settings)
        {
            Settings = settings;
        }

        public void BackToMenu()
        {
            if (State == MainMenuState.Base) return;
            if (State == MainMenuState.Settings) Settings?.Apply();
            State = MainMenuState.Menu;
            Raise();
        }

        /// <summary>PLAY: continue the existing profile, or start a new one when no save exists.</summary>
        public PlayOutcome Play()
        {
            if (Session != null) return LastOutcome ?? PlayOutcome.Continued;
            var load = _saves.Load();
            LastDiagnostics = load.Diagnostics;
            SaveSlot slot;
            PlayOutcome outcome;
            if (load.Success)
            {
                slot = load.Slot;
                outcome = load.WasRecovered || load.WasMigrated ? PlayOutcome.Recovered : PlayOutcome.Continued;
                AbandonedExpedition = AbandonedExpeditionResolver.Resolve(slot, load.Diagnostics);
                Message = AbandonedExpedition != null ? "Your last expedition was still open when the game closed: it counts as failed. XP, banked Coins and Storage are safe." : load.WasRecovered ? "The previous save was recovered from a backup." : string.Empty;
            }
            else if (load.Error == SaveError.NoSave)
            {
                slot = SaveSlotService.CreateNew();
                outcome = PlayOutcome.NewProfile;
                Message = "New profile created.";
            }
            else
            {
                State = MainMenuState.SaveError;
                LastOutcome = PlayOutcome.Failed;
                Message = $"The save could not be read ({load.Error}). Reset the profile to start over, or exit and restore a backup.";
                Raise();
                return PlayOutcome.Failed;
            }

            Session = BaseSession.Open(slot, _saves, _configs);
            State = MainMenuState.Base;
            LastOutcome = outcome;
            Raise();
            return outcome;
        }

        /// <summary>Explicit reset after an unreadable save: a fresh profile replaces it (only from the error state).</summary>
        public bool ResetSave(bool confirmed)
        {
            if (State != MainMenuState.SaveError || !confirmed) return false;
            Session = BaseSession.Open(SaveSlotService.CreateNew(), _saves, _configs);
            State = MainMenuState.Base;
            LastOutcome = PlayOutcome.NewProfile;
            Message = "Profile reset. New profile created.";
            Raise();
            return true;
        }

        public void LeaveBase()
        {
            if (Session == null) return;
            Session.SaveNow("leave_base");
            Session.Dispose();
            Session = null;
            State = MainMenuState.Menu;
            Raise();
        }

        private void Raise() => Changed?.Invoke(this);
    }
}

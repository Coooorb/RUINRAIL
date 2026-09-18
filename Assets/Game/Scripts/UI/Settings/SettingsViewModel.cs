using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core.Input;
using RuinRail.Core.Rendering;
using RuinRail.Persistence;
using UnityEngine;

namespace RuinRail.UI.Settings
{
    /// <summary>Engine-side application of the persisted settings (video/audio/accessibility). Tests substitute a recorder.</summary>
    public interface ISettingsApplier
    {
        IReadOnlyList<Vector2Int> AvailableResolutions { get; }
        void Apply(SettingsData settings);
    }

    /// <summary>Unity application: fullscreen/resolution/vsync via Screen and QualitySettings; audio levels reach the sources through AudioLevels (PublishAudio), never through AudioListener.volume.</summary>
    public sealed class UnitySettingsApplier : ISettingsApplier
    {
        private List<Vector2Int> _resolutions;

        public IReadOnlyList<Vector2Int> AvailableResolutions
        {
            get
            {
                if (_resolutions == null)
                {
                    _resolutions = Screen.resolutions.Select(r => new Vector2Int(r.width, r.height)).Distinct().OrderBy(r => r.x).ThenBy(r => r.y).ToList();
                }

                return _resolutions;
            }
        }

        public void Apply(SettingsData settings)
        {
            if (settings == null) return;
            QualitySettings.vSyncCount = settings.Video.VSync ? 1 : 0;
            var width = settings.Video.ResolutionWidth > 0 ? settings.Video.ResolutionWidth : Screen.currentResolution.width;
            var height = settings.Video.ResolutionHeight > 0 ? settings.Video.ResolutionHeight : Screen.currentResolution.height;
            if (Application.isPlaying) Screen.SetResolution(width, height, settings.Video.Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            // Master is applied once, by AudioLevels on every source gain (PublishAudio); the listener stays at unity so the
            // master setting is not applied twice (0.5 would otherwise play at 0.25).
            AudioListener.volume = 1f;
        }
    }

    public enum SettingsTab
    {
        Audio,
        Video,
        Controls,
        Accessibility
    }

    /// <summary>
    /// The Settings page (Base SETTINGS entry and the pause menu): audio, video, accessibility and control settings over
    /// <see cref="UserSettingsService"/> (technical/113: its own document, never the gameplay save) plus rebinding over
    /// <see cref="InputRebinder"/>. Edits accumulate in a draft; APPLY persists and publishes the binding overrides so
    /// every input reader (now and after restart) uses them; DISCARD returns to the persisted state; RESET restores
    /// the approved defaults for settings and bindings. Nothing here touches gameplay state.
    /// </summary>
    public sealed class SettingsViewModel : IDisposable
    {
        public static readonly SettingsTab[] Tabs = { SettingsTab.Audio, SettingsTab.Video, SettingsTab.Controls, SettingsTab.Accessibility };
        public static readonly string[] Schemes = { InputRebinder.KeyboardMouseScheme, InputRebinder.GamepadScheme };

        private readonly UserSettingsService _settings;
        private readonly InputRebinder _rebinder;
        private readonly ISettingsApplier _applier;
        private RuinRail.UI.Onboarding.ITutorialProgress _tutorialProgress;

        public SettingsViewModel(UserSettingsService settings, InputRebinder rebinder, ISettingsApplier applier)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _rebinder = rebinder;
            _applier = applier;
            if (_rebinder != null) _rebinder.Changed += OnBindingsChanged;
            Draft = Clone(_settings.Current);
        }

        /// <summary>Boot: load the persisted document, publish its binding overrides and apply engine settings.</summary>
        public static SettingsData Bootstrap(UserSettingsService settings, ISettingsApplier applier)
        {
            var data = settings.Load();
            ActiveBindingOverrides.Set(data.Controls.BindingOverridesJson);
            PublishFeedback(data);
            PublishAudio(data);
            applier?.Apply(data);
            return data;
        }

        public SettingsData Draft { get; private set; }
        public SettingsData Persisted => _settings.Current;
        public SettingsTab Tab { get; private set; } = SettingsTab.Audio;
        public string Scheme { get; private set; } = InputRebinder.KeyboardMouseScheme;
        public string Message { get; private set; } = string.Empty;
        public bool IsDirty { get; private set; }
        public bool IsListening => _rebinder != null && _rebinder.IsListening;
        public RebindEntry ListeningEntry => _rebinder?.ListeningEntry;
        public SaveError LastSaveError => _settings.LastError;
        public int Applies { get; private set; }
        public IReadOnlyList<Vector2Int> ResolutionOptions => _applier?.AvailableResolutions ?? Array.Empty<Vector2Int>();

        public event Action Changed;

        public void SelectTab(SettingsTab tab) { Tab = tab; Message = string.Empty; Raise(); }
        public void SelectScheme(string scheme)
        {
            if (Array.IndexOf(Schemes, scheme) < 0) return;
            _rebinder?.CancelListening();
            Scheme = scheme;
            Raise();
        }

        // ---- Audio ----

        public void SetMasterVolume(float value) => Edit(() => Draft.Audio.MasterVolume = Mathf.Clamp01(value));
        public void SetMusicVolume(float value) => Edit(() => Draft.Audio.MusicVolume = Mathf.Clamp01(value));
        public void SetSfxVolume(float value) => Edit(() => Draft.Audio.SfxVolume = Mathf.Clamp01(value));
        public void SetMute(bool value) => Edit(() => Draft.Audio.Mute = value);

        /// <summary>Publishes the audio levels to the audio layer.</summary>
        public static void PublishAudio(SettingsData data) => AudioLevels.Set(data.Audio.MasterVolume, data.Audio.MusicVolume, data.Audio.SfxVolume, data.Audio.Mute);

        // ---- Video ----

        public void SetFullscreen(bool value) => Edit(() => Draft.Video.Fullscreen = value);
        public void SetVSync(bool value) => Edit(() => Draft.Video.VSync = value);

        /// <summary>0×0 = the display's current resolution; anything else must be an offered option.</summary>
        public bool SetResolution(int width, int height)
        {
            if (width != 0 || height != 0)
            {
                if (width <= 0 || height <= 0 || (ResolutionOptions.Count > 0 && !ResolutionOptions.Contains(new Vector2Int(width, height))))
                {
                    Message = "That resolution is not available on this display.";
                    Raise();
                    return false;
                }
            }

            Edit(() => { Draft.Video.ResolutionWidth = width; Draft.Video.ResolutionHeight = height; });
            return true;
        }

        public string ResolutionText => Draft.Video.ResolutionWidth > 0 ? $"{Draft.Video.ResolutionWidth}×{Draft.Video.ResolutionHeight}" : "Native";

        // ---- Accessibility ----

        public void SetScreenShake(bool value) => Edit(() => Draft.Accessibility.ScreenShake = value);
        public void SetScreenShakeIntensity(float value) => Edit(() => Draft.Accessibility.ScreenShakeIntensity = Mathf.Clamp01(value));
        public void SetDamageNumbers(bool value) => Edit(() => Draft.Accessibility.DamageNumbers = value);
        public void SetHitFlash(bool value) => Edit(() => Draft.Accessibility.HitFlash = value);

        /// <summary>Publishes the feedback choices to the presentation layer (never to gameplay).</summary>
        public static void PublishFeedback(SettingsData data) =>
            FeedbackPreferences.Set(data.Accessibility.ScreenShake ? data.Accessibility.ScreenShakeIntensity : 0f, data.Accessibility.DamageNumbers, data.Accessibility.HitFlash);

        // ---- Tutorial (ui/95: prompts can be reset / re-enabled in Settings) ----

        public void SetTutorialPrompts(bool value) => Edit(() => Draft.Tutorial.ShowPrompts = value);

        /// <summary>The active profile's tutorial progress (set while a Base session is open) so RESET TUTORIALS can clear it.</summary>
        public void SetTutorialProgress(RuinRail.UI.Onboarding.ITutorialProgress progress) => _tutorialProgress = progress;

        public bool CanResetTutorials => _tutorialProgress != null;

        public bool ResetTutorials()
        {
            if (_tutorialProgress == null) { Message = "Open a profile to reset its tutorials."; Raise(); return false; }
            _tutorialProgress.ResetSeen();
            Draft.Tutorial.ShowPrompts = true;
            IsDirty = true;
            Message = "Tutorial prompts will show again.";
            Raise();
            return true;
        }

        // ---- Controls ----

        public IReadOnlyList<RebindEntry> ControlEntries => _rebinder == null ? Array.Empty<RebindEntry>() : _rebinder.EntriesFor(Scheme).ToList();
        public bool HasBindingOverrides => _rebinder != null && _rebinder.HasOverrides;

        public string BindingLine(RebindEntry entry)
        {
            if (entry == null) return string.Empty;
            var text = $"{entry.Label}: {entry.DisplayText}";
            if (!entry.IsRebindable) text += " (fixed)";
            else if (entry.IsOverridden) text += " *";
            if (ListeningEntry == entry) text = $"{entry.Label}: press a control… (Esc cancels)";
            return text;
        }

        public bool BeginRebind(RebindEntry entry, bool swapOnConflict = false)
        {
            if (_rebinder == null || entry == null) return false;
            if (!entry.IsRebindable)
            {
                Message = $"{entry.Label} cannot be rebound.";
                Raise();
                return false;
            }

            var started = _rebinder.BeginListening(entry, swapOnConflict);
            Message = started ? $"Press a control for {entry.Label}. Esc cancels." : Message;
            Raise();
            return started;
        }

        public void CancelRebind()
        {
            if (_rebinder == null || !_rebinder.IsListening) return;
            _rebinder.CancelListening();
            Message = "Rebind cancelled.";
            Raise();
        }

        /// <summary>Direct assignment (also the path the interactive listener ends in). Never binds reserved/impossible controls.</summary>
        public RebindResult TryBind(RebindEntry entry, string controlPath, bool swapOnConflict = false)
        {
            if (_rebinder == null) return new RebindResult(RebindOutcome.NotRebindable);
            var result = _rebinder.TryBind(entry, controlPath, swapOnConflict);
            Message = MessageFor(entry, result);
            Raise();
            return result;
        }

        public static string MessageFor(RebindEntry entry, RebindResult result)
        {
            var label = entry?.Label ?? "That action";
            return result.Outcome switch
            {
                RebindOutcome.Applied => $"{label} rebound to {entry?.DisplayText}.",
                RebindOutcome.Swapped => $"{label} rebound to {entry?.DisplayText}; {result.ConflictingAction} took its previous control.",
                RebindOutcome.Conflict => $"{result.ConflictingAction} already uses that control. Choose SWAP to exchange them or pick another control.",
                RebindOutcome.Reserved => "That control is reserved and cannot be assigned.",
                RebindOutcome.Impossible => $"That control cannot be used for {label}.",
                RebindOutcome.WrongDevice => "That control belongs to a different control scheme.",
                RebindOutcome.NotRebindable => $"{label} cannot be rebound.",
                RebindOutcome.Cancelled => "Rebind cancelled.",
                _ => string.Empty
            };
        }

        public void ResetBinding(RebindEntry entry)
        {
            if (_rebinder == null || entry == null) return;
            _rebinder.ResetEntry(entry);
            Message = $"{entry.Label} restored to default.";
            Raise();
        }

        public void ResetAllBindings()
        {
            if (_rebinder == null) return;
            _rebinder.ResetAll();
            Message = "All bindings restored to the approved defaults.";
            Raise();
        }

        // ---- Apply / discard / reset ----

        /// <summary>Persists the draft and current binding overrides to the settings document and applies them to the engine and every input reader.</summary>
        public SaveError Apply()
        {
            _rebinder?.CancelListening();
            var overrides = _rebinder?.OverridesJson ?? Persisted.Controls.BindingOverridesJson;
            var target = _settings.Current;
            Copy(Draft, target);
            target.Controls.BindingOverridesJson = overrides ?? string.Empty;
            var error = _settings.Save();
            ActiveBindingOverrides.Set(target.Controls.BindingOverridesJson);
            PublishFeedback(target);
            PublishAudio(target);
            _applier?.Apply(target);
            Draft = Clone(target);
            IsDirty = false;
            Applies++;
            Message = error == SaveError.None ? "Settings applied." : "Settings applied for this session, but could not be saved.";
            Raise();
            return error;
        }

        /// <summary>Drops unapplied edits, including rebinds made since the last APPLY.</summary>
        public void Discard()
        {
            _rebinder?.CancelListening();
            Draft = Clone(Persisted);
            _rebinder?.LoadOverrides(Persisted.Controls.BindingOverridesJson);
            IsDirty = false;
            Message = "Changes discarded.";
            Raise();
        }

        /// <summary>Approved defaults for settings and bindings; the gameplay save is untouched (113).</summary>
        public SaveError ResetToDefaults()
        {
            _rebinder?.ResetAll();
            var error = _settings.ResetToDefaults();
            ActiveBindingOverrides.Clear();
            PublishFeedback(Persisted);
            PublishAudio(Persisted);
            _applier?.Apply(Persisted);
            Draft = Clone(Persisted);
            IsDirty = false;
            Message = "Settings restored to defaults.";
            Raise();
            return error;
        }

        public void Dispose()
        {
            if (_rebinder != null) _rebinder.Changed -= OnBindingsChanged;
        }

        private void OnBindingsChanged()
        {
            IsDirty = true;
            Raise();
        }

        private void Edit(Action edit)
        {
            edit();
            IsDirty = true;
            Raise();
        }

        private void Raise() => Changed?.Invoke();

        private static SettingsData Clone(SettingsData source) => JsonUtility.FromJson<SettingsData>(JsonUtility.ToJson(source ?? SettingsData.Defaults())) ?? SettingsData.Defaults();

        private static void Copy(SettingsData from, SettingsData to)
        {
            to.Audio.MasterVolume = from.Audio.MasterVolume;
            to.Audio.MusicVolume = from.Audio.MusicVolume;
            to.Audio.SfxVolume = from.Audio.SfxVolume;
            to.Audio.Mute = from.Audio.Mute;
            to.Video.Fullscreen = from.Video.Fullscreen;
            to.Video.VSync = from.Video.VSync;
            to.Video.ResolutionWidth = from.Video.ResolutionWidth;
            to.Video.ResolutionHeight = from.Video.ResolutionHeight;
            to.Accessibility.ScreenShake = from.Accessibility.ScreenShake;
            to.Accessibility.ScreenShakeIntensity = from.Accessibility.ScreenShakeIntensity;
            to.Accessibility.DamageNumbers = from.Accessibility.DamageNumbers;
            to.Accessibility.HitFlash = from.Accessibility.HitFlash;
            to.Tutorial.ShowPrompts = from.Tutorial.ShowPrompts;
        }
    }
}

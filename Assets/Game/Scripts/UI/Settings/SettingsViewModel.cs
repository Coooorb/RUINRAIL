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
            // 0 = unlimited (-1 to Unity): under VSync the display's refresh bounds it anyway.
            Application.targetFrameRate = settings.Video.FrameRateLimit > 0 ? settings.Video.FrameRateLimit : -1;
            var width = settings.Video.ResolutionWidth > 0 ? settings.Video.ResolutionWidth : Screen.currentResolution.width;
            var height = settings.Video.ResolutionHeight > 0 ? settings.Video.ResolutionHeight : Screen.currentResolution.height;
            if (Application.isPlaying) Screen.SetResolution(width, height, settings.Video.Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            // Master is applied once, by AudioLevels on every source gain (PublishAudio); the listener stays at unity so the
            // master setting is not applied twice (0.5 would otherwise play at 0.25).
            AudioListener.volume = 1f;
        }
    }

    /// <summary>The Settings categories (ui/90): each is its own page; the root of Settings lists them.</summary>
    public enum SettingsTab
    {
        Video,
        Audio,
        Controls,
        Gameplay
    }

    /// <summary>How a settings row is adjusted: a two-state toggle, a stepped selector, a continuous slider, or a plain action.</summary>
    public enum SettingsControlKind
    {
        Toggle,
        Selector,
        Slider,
        Action
    }

    /// <summary>
    /// One adjustable row of a settings page as the panel renders it: its label, the current value as text, the fill
    /// (0..1) for sliders, and the step/activate actions. The view model owns every value; this only describes it.
    /// </summary>
    public sealed class SettingsRow
    {
        public SettingsRow(string id, string label, SettingsControlKind kind, Func<string> value, Action<int> adjust, Action activate, Func<float> fill = null, Func<bool> isEnabled = null)
        {
            Id = id;
            Label = label;
            Kind = kind;
            ValueText = value ?? (() => string.Empty);
            Adjust = adjust;
            Activate = activate;
            Fill = fill;
            IsEnabledCheck = isEnabled;
        }

        public string Id { get; }
        public string Label { get; }
        public SettingsControlKind Kind { get; }
        public Func<string> ValueText { get; }
        /// <summary>Left/right step (keyboard arrows, controller D-pad/stick): −1 / +1.</summary>
        public Action<int> Adjust { get; }
        /// <summary>Confirm / click: toggles, cycles or runs the row.</summary>
        public Action Activate { get; }
        public Func<float> Fill { get; }
        public Func<bool> IsEnabledCheck { get; }
        public bool IsEnabled => IsEnabledCheck?.Invoke() ?? true;
        /// <summary>Sliders and selectors can be set by pointer position along the row (0..1); toggles/actions cannot.</summary>
        public bool AcceptsFill => Kind == SettingsControlKind.Slider;
        public Action<float> SetFill { get; set; }
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
        public static readonly SettingsTab[] Tabs = { SettingsTab.Video, SettingsTab.Audio, SettingsTab.Controls, SettingsTab.Gameplay };
        /// <summary>Frame-rate limit options (0 = unlimited / VSync-bound).</summary>
        public static readonly int[] FrameRateOptions = { 0, 30, 60, 120, 144, 240 };
        public const float VolumeStep = 0.05f;
        public const float ShakeStep = 0.1f;
        /// <summary>Seconds a display-mode / resolution change stays before it reverts unless KEEP is chosen.</summary>
        public const float VideoConfirmSeconds = 12f;
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
        /// <summary>The open category page, or null while the category list (the Settings root) is showing.</summary>
        public SettingsTab? Page { get; private set; }
        /// <summary>The last selected category (the page when one is open).</summary>
        public SettingsTab Tab { get; private set; } = SettingsTab.Video;
        public bool IsOnCategories => !Page.HasValue;
        public int PageOpens { get; private set; }
        public string Scheme { get; private set; } = InputRebinder.KeyboardMouseScheme;
        public string Message { get; private set; } = string.Empty;
        public bool IsDirty { get; private set; }
        public bool IsListening => _rebinder != null && _rebinder.IsListening;
        public RebindEntry ListeningEntry => _rebinder?.ListeningEntry;
        public SaveError LastSaveError => _settings.LastError;
        public int Applies { get; private set; }
        public IReadOnlyList<Vector2Int> ResolutionOptions => _applier?.AvailableResolutions ?? Array.Empty<Vector2Int>();

        public event Action Changed;

        /// <summary>Opens a category page (the tab). Identical to <see cref="OpenPage"/>; kept for callers that select by tab.</summary>
        public void SelectTab(SettingsTab tab) => OpenPage(tab);

        /// <summary>Opens the category's page over the category list.</summary>
        public void OpenPage(SettingsTab tab)
        {
            _rebinder?.CancelListening();
            Tab = tab;
            Page = tab;
            PageOpens++;
            Message = string.Empty;
            Raise();
        }

        /// <summary>
        /// Back from a category page to the category list: the page's edits are applied (persisted) so nothing set
        /// with a slider or a rebind is lost by leaving, and a video change still waiting for KEEP is reverted first.
        /// Returns false when already on the category list — the owner then closes Settings.
        /// </summary>
        public bool BackFromPage()
        {
            if (!Page.HasValue) return false;
            if (VideoConfirmPending) RevertVideo();
            DropUnappliedDisplayChanges();
            Apply();
            Page = null;
            Raise();
            return true;
        }

        /// <summary>
        /// A display-mode / resolution edit only reaches the engine through APPLY DISPLAY SETTINGS + KEEP; leaving the
        /// page with such an edit still in the draft drops it (VSync / frame-rate edits are safe and persist).
        /// </summary>
        private void DropUnappliedDisplayChanges()
        {
            Draft.Video.Fullscreen = Persisted.Video.Fullscreen;
            Draft.Video.ResolutionWidth = Persisted.Video.ResolutionWidth;
            Draft.Video.ResolutionHeight = Persisted.Video.ResolutionHeight;
        }

        /// <summary>The owner (Main Menu / Pause) closes Settings when the root's BACK control is used; null = Esc/B only.</summary>
        public Action CloseRequested { get; set; }
        public int CloseRequests { get; private set; }

        /// <summary>The root BACK control: asks the owner to close Settings (the same path as Esc / B on the root).</summary>
        public void RequestClose()
        {
            CloseRequests++;
            CloseRequested?.Invoke();
        }

        /// <summary>Returns to the category list without leaving Settings (owner-level close resets it too).</summary>
        public void ResetToCategories()
        {
            _rebinder?.CancelListening();
            if (VideoConfirmPending) RevertVideo();
            Page = null;
            Raise();
        }

        public static string CategoryLabel(SettingsTab tab) => tab switch
        {
            SettingsTab.Video => "VIDEO",
            SettingsTab.Audio => "AUDIO",
            SettingsTab.Controls => "CONTROLS",
            SettingsTab.Gameplay => "GAMEPLAY",
            _ => tab.ToString().ToUpperInvariant()
        };

        public static string CategoryHint(SettingsTab tab) => tab switch
        {
            SettingsTab.Video => "Display mode, resolution, VSync, frame-rate limit",
            SettingsTab.Audio => "Master, music, SFX and ambience levels, mute",
            SettingsTab.Controls => "Keyboard / mouse and controller bindings",
            SettingsTab.Gameplay => "Aim assist, screen shake, damage numbers, hit flash, tutorial prompts",
            _ => string.Empty
        };
        public void SelectScheme(string scheme)
        {
            if (Array.IndexOf(Schemes, scheme) < 0) return;
            _rebinder?.CancelListening();
            Scheme = scheme;
            Raise();
        }

        // ---- Audio ----

        public void SetMasterVolume(float value) => EditAudio(() => Draft.Audio.MasterVolume = Mathf.Clamp01(value));
        public void SetMusicVolume(float value) => EditAudio(() => Draft.Audio.MusicVolume = Mathf.Clamp01(value));
        public void SetSfxVolume(float value) => EditAudio(() => Draft.Audio.SfxVolume = Mathf.Clamp01(value));
        public void SetAmbienceVolume(float value) => EditAudio(() => Draft.Audio.AmbienceVolume = Mathf.Clamp01(value));
        public void SetMute(bool value) => EditAudio(() => Draft.Audio.Mute = value);

        /// <summary>Steps a volume by <see cref="VolumeStep"/> (keyboard/controller left-right); an explicit 0 stays a valid mute.</summary>
        public void AdjustMasterVolume(int steps) => SetMasterVolume(Step(Draft.Audio.MasterVolume, steps, VolumeStep));
        public void AdjustMusicVolume(int steps) => SetMusicVolume(Step(Draft.Audio.MusicVolume, steps, VolumeStep));
        public void AdjustSfxVolume(int steps) => SetSfxVolume(Step(Draft.Audio.SfxVolume, steps, VolumeStep));
        public void AdjustAmbienceVolume(int steps) => SetAmbienceVolume(Step(Draft.Audio.AmbienceVolume, steps, VolumeStep));

        private static float Step(float value, int steps, float step) => Mathf.Clamp01(Mathf.Round((value + steps * step) / step) * step);

        public static string PercentText(float value) => Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%";

        /// <summary>An audio edit previews at once (the AUDIO page is heard while it is adjusted); DISCARD/Back restore the persisted mix.</summary>
        private void EditAudio(Action edit)
        {
            edit();
            IsDirty = true;
            PublishAudio(Draft);
            Raise();
        }

        /// <summary>Publishes the audio levels to the audio layer.</summary>
        public static void PublishAudio(SettingsData data) => AudioLevels.Set(data.Audio.MasterVolume, data.Audio.MusicVolume, data.Audio.SfxVolume, data.Audio.AmbienceVolume, data.Audio.Mute);

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

        public string ResolutionText => Draft.Video.ResolutionWidth > 0 ? $"{Draft.Video.ResolutionWidth} x {Draft.Video.ResolutionHeight}" : "Native";
        public string DisplayModeText => Draft.Video.Fullscreen ? "Fullscreen" : "Windowed";
        public string FrameRateText => Draft.Video.FrameRateLimit > 0 ? Draft.Video.FrameRateLimit + " fps" : "Unlimited";

        /// <summary>Native plus every offered resolution, in the order the selector steps through them.</summary>
        public IReadOnlyList<Vector2Int> ResolutionChoices
        {
            get
            {
                var list = new List<Vector2Int> { Vector2Int.zero };
                list.AddRange(ResolutionOptions);
                return list;
            }
        }

        /// <summary>Steps the resolution selector (wrapping) through Native and the display's options.</summary>
        public void AdjustResolution(int steps)
        {
            var choices = ResolutionChoices;
            var current = new Vector2Int(Draft.Video.ResolutionWidth, Draft.Video.ResolutionHeight);
            var index = Math.Max(0, choices.ToList().FindIndex(c => c == current));
            var next = choices[((index + steps) % choices.Count + choices.Count) % choices.Count];
            SetResolution(next.x, next.y);
        }

        public void SetFrameRateLimit(int fps)
        {
            var value = Array.IndexOf(FrameRateOptions, fps) >= 0 ? fps : 0;
            Edit(() => Draft.Video.FrameRateLimit = value);
        }

        public void AdjustFrameRateLimit(int steps)
        {
            var index = Math.Max(0, Array.IndexOf(FrameRateOptions, Draft.Video.FrameRateLimit));
            SetFrameRateLimit(FrameRateOptions[((index + steps) % FrameRateOptions.Length + FrameRateOptions.Length) % FrameRateOptions.Length]);
        }

        // ---- Video apply / confirm / revert ----

        private SettingsData _videoBeforeApply;

        /// <summary>True while an applied display-mode / resolution change waits for KEEP; it reverts when the timer ends.</summary>
        public bool VideoConfirmPending { get; private set; }
        public float VideoConfirmRemaining { get; private set; }
        public int VideoApplies { get; private set; }
        public int VideoReverts { get; private set; }

        /// <summary>True when the draft's video values differ from what the engine currently runs (the persisted values).</summary>
        public bool VideoDirty => Draft.Video.Fullscreen != Persisted.Video.Fullscreen || Draft.Video.VSync != Persisted.Video.VSync
                                  || Draft.Video.ResolutionWidth != Persisted.Video.ResolutionWidth || Draft.Video.ResolutionHeight != Persisted.Video.ResolutionHeight
                                  || Draft.Video.FrameRateLimit != Persisted.Video.FrameRateLimit;

        /// <summary>
        /// Applies the draft video settings to the engine now. A display-mode or resolution change is the one edit
        /// that can leave the player unable to see the menu, so it is applied provisionally: KEEP within
        /// <see cref="VideoConfirmSeconds"/> persists it, anything else (timeout, Back, REVERT) restores the previous
        /// values. VSync and the frame-rate limit are safe and persist immediately.
        /// </summary>
        public void ApplyVideo()
        {
            if (VideoConfirmPending) { ConfirmVideo(); return; }
            var risky = Draft.Video.Fullscreen != Persisted.Video.Fullscreen || Draft.Video.ResolutionWidth != Persisted.Video.ResolutionWidth || Draft.Video.ResolutionHeight != Persisted.Video.ResolutionHeight;
            VideoApplies++;
            if (!risky)
            {
                Apply();
                return;
            }

            _videoBeforeApply = Clone(Persisted);
            _applier?.Apply(Draft);
            VideoConfirmPending = true;
            VideoConfirmRemaining = VideoConfirmSeconds;
            Message = $"Keep these display settings? Reverting in {Mathf.CeilToInt(VideoConfirmRemaining)} s.";
            Raise();
        }

        /// <summary>KEEP: the provisional display change becomes the persisted one.</summary>
        public void ConfirmVideo()
        {
            if (!VideoConfirmPending) return;
            VideoConfirmPending = false;
            _videoBeforeApply = null;
            Apply();
            Message = "Display settings kept.";
            Raise();
        }

        /// <summary>REVERT (or timeout / Back): the engine and the draft return to the values before the provisional apply.</summary>
        public void RevertVideo()
        {
            if (!VideoConfirmPending) return;
            VideoConfirmPending = false;
            VideoReverts++;
            var previous = _videoBeforeApply ?? Persisted;
            Draft.Video.Fullscreen = previous.Video.Fullscreen;
            Draft.Video.ResolutionWidth = previous.Video.ResolutionWidth;
            Draft.Video.ResolutionHeight = previous.Video.ResolutionHeight;
            _applier?.Apply(Draft);
            _videoBeforeApply = null;
            Message = "Display settings reverted.";
            Raise();
        }

        /// <summary>Advances the KEEP timer (unscaled seconds); the panel drives it while the VIDEO page is up.</summary>
        public void Tick(float unscaledDeltaTime)
        {
            if (!VideoConfirmPending || unscaledDeltaTime <= 0f) return;
            VideoConfirmRemaining -= unscaledDeltaTime;
            var remaining = Mathf.CeilToInt(Mathf.Max(0f, VideoConfirmRemaining));
            var text = $"Keep these display settings? Reverting in {remaining} s.";
            if (VideoConfirmRemaining <= 0f) { RevertVideo(); return; }
            if (text != Message) { Message = text; Raise(); }
        }

        // ---- Accessibility ----

        public void SetScreenShake(bool value) => Edit(() => Draft.Accessibility.ScreenShake = value);
        public void SetScreenShakeIntensity(float value) => Edit(() => Draft.Accessibility.ScreenShakeIntensity = Mathf.Clamp01(value));
        public void AdjustScreenShakeIntensity(int steps) => SetScreenShakeIntensity(Step(Draft.Accessibility.ScreenShakeIntensity, steps, ShakeStep));
        /// <summary>True when the GAMEPLAY category has anything to show (it always has: shake, damage numbers, hit flash, tutorial prompts exist).</summary>
        public bool HasGameplayPreferences => true;
        public void SetDamageNumbers(bool value) => Edit(() => Draft.Accessibility.DamageNumbers = value);
        public void SetHitFlash(bool value) => Edit(() => Draft.Accessibility.HitFlash = value);

        /// <summary>
        /// Aim assist ON/OFF. It previews live, like the audio rows, because the point of the toggle is to feel the
        /// difference: the runtime reads <see cref="AssistPreferences"/> on the next shot. DISCARD and Back restore the
        /// persisted value through the same publish, so a previewed OFF never outlives the page.
        /// </summary>
        public void SetAimAssist(bool value)
        {
            Draft.Accessibility.AimAssist = value;
            IsDirty = true;
            AssistPreferences.Set(value);
            Raise();
        }

        /// <summary>Publishes the feedback choices to the presentation layer, and the assist choices to gameplay.</summary>
        public static void PublishFeedback(SettingsData data)
        {
            FeedbackPreferences.Set(data.Accessibility.ScreenShake ? data.Accessibility.ScreenShakeIntensity : 0f, data.Accessibility.DamageNumbers, data.Accessibility.HitFlash);
            // Published on the same path as the other gameplay-affecting preferences, so Bootstrap, APPLY, DISCARD and
            // RESET all reach the runtime through one call and the setting can never drift from the document.
            AssistPreferences.Set(data.Accessibility.AimAssist);
        }

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
        public bool HasRebinder => _rebinder != null;
        public static string SchemeLabel(string scheme) => scheme == InputRebinder.GamepadScheme ? "Controller" : "Keyboard & Mouse";

        /// <summary>Steps the scheme selector (Keyboard & Mouse / Controller).</summary>
        public void AdjustScheme(int steps)
        {
            var index = Math.Max(0, Array.IndexOf(Schemes, Scheme));
            SelectScheme(Schemes[((index + steps) % Schemes.Length + Schemes.Length) % Schemes.Length]);
        }
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
            if (VideoConfirmPending) { VideoConfirmPending = false; _videoBeforeApply = null; }
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
            if (VideoConfirmPending) RevertVideo();
            Draft = Clone(Persisted);
            _rebinder?.LoadOverrides(Persisted.Controls.BindingOverridesJson);
            PublishAudio(Persisted); // the previewed mix returns to what is saved
            AssistPreferences.Set(Persisted.Accessibility.AimAssist); // and so does a previewed aim-assist toggle
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

        // ---- Page rows (what the panel renders; every value read live from the draft) ----

        /// <summary>The adjustable rows of a category page, in order. Ids are stable for tests and focus restoration.</summary>
        public List<SettingsRow> RowsFor(SettingsTab tab)
        {
            var rows = new List<SettingsRow>();
            switch (tab)
            {
                case SettingsTab.Video:
                    rows.Add(new SettingsRow("settings.video.display_mode", "DISPLAY MODE", SettingsControlKind.Selector, () => DisplayModeText, _ => SetFullscreen(!Draft.Video.Fullscreen), () => SetFullscreen(!Draft.Video.Fullscreen)));
                    rows.Add(new SettingsRow("settings.video.resolution", "RESOLUTION", SettingsControlKind.Selector, () => ResolutionText, AdjustResolution, () => AdjustResolution(+1)));
                    rows.Add(new SettingsRow("settings.video.vsync", "VSYNC", SettingsControlKind.Toggle, () => OnOff(Draft.Video.VSync), _ => SetVSync(!Draft.Video.VSync), () => SetVSync(!Draft.Video.VSync)));
                    rows.Add(new SettingsRow("settings.video.framerate", "FRAME-RATE LIMIT", SettingsControlKind.Selector, () => FrameRateText, AdjustFrameRateLimit, () => AdjustFrameRateLimit(+1)));
                    rows.Add(new SettingsRow("settings.video.apply", "APPLY DISPLAY SETTINGS", SettingsControlKind.Action, () => VideoConfirmPending ? "KEEP" : VideoDirty ? "changed" : "applied", null, ApplyVideo, null, () => VideoDirty || VideoConfirmPending));
                    rows.Add(new SettingsRow("settings.video.revert", "REVERT DISPLAY CHANGE", SettingsControlKind.Action, () => VideoConfirmPending ? $"{Mathf.CeilToInt(Mathf.Max(0f, VideoConfirmRemaining))} s" : string.Empty, null, RevertVideo, null, () => VideoConfirmPending));
                    break;
                case SettingsTab.Audio:
                    rows.Add(Slider("settings.audio.master", "MASTER VOLUME", () => Draft.Audio.MasterVolume, AdjustMasterVolume, SetMasterVolume));
                    rows.Add(Slider("settings.audio.music", "MUSIC VOLUME", () => Draft.Audio.MusicVolume, AdjustMusicVolume, SetMusicVolume));
                    rows.Add(Slider("settings.audio.sfx", "SFX VOLUME", () => Draft.Audio.SfxVolume, AdjustSfxVolume, SetSfxVolume));
                    rows.Add(Slider("settings.audio.ambience", "AMBIENCE VOLUME", () => Draft.Audio.AmbienceVolume, AdjustAmbienceVolume, SetAmbienceVolume));
                    rows.Add(new SettingsRow("settings.audio.mute", "MUTE ALL", SettingsControlKind.Toggle, () => OnOff(Draft.Audio.Mute), _ => SetMute(!Draft.Audio.Mute), () => SetMute(!Draft.Audio.Mute)));
                    break;
                case SettingsTab.Controls:
                    rows.Add(new SettingsRow("settings.controls.scheme", "BINDINGS FOR", SettingsControlKind.Selector, () => SchemeLabel(Scheme), AdjustScheme, () => AdjustScheme(+1)));
                    foreach (var entry in ControlEntries)
                    {
                        var e = entry;
                        rows.Add(new SettingsRow("settings.rebind." + e.Scheme + "." + e.ActionName + "." + e.Binding.name, e.Label.ToUpperInvariant(), SettingsControlKind.Action,
                            () => ListeningEntry == e ? "press a control..." : e.DisplayText + (e.IsOverridden ? " *" : string.Empty) + (e.IsRebindable ? string.Empty : " (fixed)"),
                            null, () => BeginRebind(e), null, () => e.IsRebindable));
                    }

                    rows.Add(new SettingsRow("settings.reset_bindings", "RESET BINDINGS", SettingsControlKind.Action, () => HasBindingOverrides ? "custom" : "defaults", null, ResetAllBindings, null, () => HasRebinder));
                    break;
                case SettingsTab.Gameplay:
                    rows.Add(new SettingsRow("settings.aim_assist", "AIM ASSIST", SettingsControlKind.Toggle, () => OnOff(Draft.Accessibility.AimAssist), _ => SetAimAssist(!Draft.Accessibility.AimAssist), () => SetAimAssist(!Draft.Accessibility.AimAssist)));
                    rows.Add(new SettingsRow("settings.shake", "SCREEN SHAKE", SettingsControlKind.Toggle, () => OnOff(Draft.Accessibility.ScreenShake), _ => SetScreenShake(!Draft.Accessibility.ScreenShake), () => SetScreenShake(!Draft.Accessibility.ScreenShake)));
                    rows.Add(Slider("settings.shake.intensity", "SHAKE INTENSITY", () => Draft.Accessibility.ScreenShakeIntensity, AdjustScreenShakeIntensity, SetScreenShakeIntensity, () => Draft.Accessibility.ScreenShake));
                    rows.Add(new SettingsRow("settings.damage_numbers", "DAMAGE NUMBERS", SettingsControlKind.Toggle, () => OnOff(Draft.Accessibility.DamageNumbers), _ => SetDamageNumbers(!Draft.Accessibility.DamageNumbers), () => SetDamageNumbers(!Draft.Accessibility.DamageNumbers)));
                    rows.Add(new SettingsRow("settings.hit_flash", "HIT FLASH", SettingsControlKind.Toggle, () => OnOff(Draft.Accessibility.HitFlash), _ => SetHitFlash(!Draft.Accessibility.HitFlash), () => SetHitFlash(!Draft.Accessibility.HitFlash)));
                    rows.Add(new SettingsRow("settings.tutorials", "TUTORIAL PROMPTS", SettingsControlKind.Toggle, () => OnOff(Draft.Tutorial.ShowPrompts), _ => SetTutorialPrompts(!Draft.Tutorial.ShowPrompts), () => SetTutorialPrompts(!Draft.Tutorial.ShowPrompts)));
                    rows.Add(new SettingsRow("settings.tutorials.reset", "RESET TUTORIALS", SettingsControlKind.Action, () => string.Empty, null, () => ResetTutorials(), null, () => CanResetTutorials));
                    break;
            }

            return rows;
        }

        private static SettingsRow Slider(string id, string label, Func<float> value, Action<int> adjust, Action<float> set, Func<bool> enabled = null)
        {
            // A slider has no Enter/click action: left/right (or the pointer along the bar) set it, so a click on the
            // bar never also bumps the value by a step.
            var row = new SettingsRow(id, label, SettingsControlKind.Slider, () => PercentText(value()), adjust, null, value, enabled) { SetFill = set };
            return row;
        }

        private static string OnOff(bool value) => value ? "ON" : "OFF";

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
            to.Audio.AmbienceVolume = from.Audio.AmbienceVolume;
            to.Audio.Mute = from.Audio.Mute;
            to.Video.Fullscreen = from.Video.Fullscreen;
            to.Video.VSync = from.Video.VSync;
            to.Video.ResolutionWidth = from.Video.ResolutionWidth;
            to.Video.ResolutionHeight = from.Video.ResolutionHeight;
            to.Video.FrameRateLimit = from.Video.FrameRateLimit;
            to.Accessibility.ScreenShake = from.Accessibility.ScreenShake;
            to.Accessibility.ScreenShakeIntensity = from.Accessibility.ScreenShakeIntensity;
            to.Accessibility.DamageNumbers = from.Accessibility.DamageNumbers;
            to.Accessibility.HitFlash = from.Accessibility.HitFlash;
            to.Accessibility.AimAssist = from.Accessibility.AimAssist;
            to.Tutorial.ShowPrompts = from.Tutorial.ShowPrompts;
        }
    }
}

using System;
using UnityEngine;

namespace RuinRail.Persistence
{
    /// <summary>
    /// Load/save/reset of the settings document, in its own store next to (never inside) the gameplay slot. Uses the
    /// same atomic-write/recovery store as the gameplay save. A missing or unreadable document yields defaults without
    /// touching the gameplay save; a reset writes defaults to the settings store only.
    /// </summary>
    public sealed class UserSettingsService
    {
        public const string SettingsFileName = "ruinrail_settings.json";

        private readonly ISaveStore _store;

        public UserSettingsService(ISaveStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public static string DefaultPath => System.IO.Path.Combine(Application.persistentDataPath, SettingsFileName);

        public SettingsData Current { get; private set; } = SettingsData.Defaults();
        public SaveError LastError { get; private set; }
        public SaveDiagnostics LastDiagnostics { get; private set; } = new();

        /// <summary>Loads the first readable candidate; falls back to defaults (reported, not silent) when none loads.</summary>
        public SettingsData Load()
        {
            var diagnostics = new SaveDiagnostics();
            foreach (var candidate in _store.ReadCandidates())
            {
                if (string.IsNullOrWhiteSpace(candidate.Document)) continue;
                try
                {
                    var data = JsonUtility.FromJson<SettingsData>(candidate.Document);
                    if (data == null || data.SettingsVersion <= 0 || data.SettingsVersion > SettingsData.CurrentVersion)
                    {
                        diagnostics.Error("settings.version", $"[{candidate.Name}] Unsupported settings version {data?.SettingsVersion ?? 0}.");
                        continue;
                    }

                    Clamp(data);
                    MigrateAudio(candidate.Document, data, diagnostics);
                    if (candidate.Name != SaveCandidateNames.Current) diagnostics.Warning("settings.recovered", $"Loaded settings from '{candidate.Name}'.");
                    Current = data;
                    LastError = SaveError.None;
                    LastDiagnostics = diagnostics;
                    return Current;
                }
                catch (Exception e)
                {
                    diagnostics.Error("settings.parse", $"[{candidate.Name}] {e.Message}");
                }
            }

            Current = SettingsData.Defaults();
            LastError = _store.Exists ? SaveError.Corrupt : SaveError.NoSave;
            if (LastError == SaveError.Corrupt) diagnostics.Warning("settings.defaults", "No settings document could be loaded; defaults are in use and nothing was overwritten.");
            LastDiagnostics = diagnostics;
            return Current;
        }

        public SaveError Save()
        {
            var diagnostics = new SaveDiagnostics();
            Current.SettingsVersion = SettingsData.CurrentVersion;
            Clamp(Current);
            try
            {
                _store.Write(JsonUtility.ToJson(Current));
                LastError = SaveError.None;
            }
            catch (Exception e)
            {
                diagnostics.Error("settings.write", $"Settings could not be written: {e.Message}");
                LastError = SaveError.WriteFailed;
            }

            LastDiagnostics = diagnostics;
            return LastError;
        }

        /// <summary>Settings only: progression, storage and the gameplay slot are untouched.</summary>
        public SaveError ResetToDefaults()
        {
            Current = SettingsData.Defaults();
            return Save();
        }

        /// <summary>
        /// A settings document written before an audio key existed — or one whose audio block was truncated — leaves
        /// that float at 0 after deserialization, which is indistinguishable from "the player turned it all the way
        /// down" and makes the game boot silent. A key that is genuinely absent from the document is restored to the
        /// approved default; a key that is present and set to 0 is the player's own choice and is never overridden.
        /// </summary>
        internal static void MigrateAudio(string document, SettingsData data, SaveDiagnostics diagnostics = null)
        {
            if (data?.Audio == null) return;
            var text = document ?? string.Empty;
            var defaults = new AudioPreferences();
            var restored = new System.Collections.Generic.List<string>();
            if (!text.Contains("\"MasterVolume\"")) { data.Audio.MasterVolume = defaults.MasterVolume; restored.Add("MasterVolume"); }
            if (!text.Contains("\"MusicVolume\"")) { data.Audio.MusicVolume = defaults.MusicVolume; restored.Add("MusicVolume"); }
            if (!text.Contains("\"SfxVolume\"")) { data.Audio.SfxVolume = defaults.SfxVolume; restored.Add("SfxVolume"); }
            if (restored.Count > 0)
                diagnostics?.Warning("settings.audio.migrated", "Restored missing audio volume keys to their defaults: " + string.Join(", ", restored) + ".");
        }

        private static void Clamp(SettingsData data)
        {
            data.Audio ??= new AudioPreferences();
            data.Video ??= new VideoPreferences();
            data.Controls ??= new ControlPreferences();
            data.Accessibility ??= new AccessibilityPreferences();
            data.Tutorial ??= new TutorialPreferences();
            data.Audio.MasterVolume = Mathf.Clamp01(data.Audio.MasterVolume);
            data.Audio.MusicVolume = Mathf.Clamp01(data.Audio.MusicVolume);
            data.Audio.SfxVolume = Mathf.Clamp01(data.Audio.SfxVolume);
            data.Controls.BindingOverridesJson ??= "";
            data.Accessibility.ScreenShakeIntensity = Mathf.Clamp01(data.Accessibility.ScreenShakeIntensity);
        }
    }
}

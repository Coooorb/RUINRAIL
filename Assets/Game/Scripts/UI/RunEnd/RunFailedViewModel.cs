using System;
using System.Collections.Generic;
using RuinRail.Core;
using RuinRail.Gameplay.Expedition;
using RuinRail.UI.Hud;
using RuinRail.UI.Inventory;

namespace RuinRail.UI.RunEnd
{
    public enum RunFailedChoice
    {
        None,
        ReturnToShelter,
        MainMenu
    }

    /// <summary>One labelled figure on the Run Lost screen (label / value, both already player-facing text).</summary>
    public readonly struct RunFailedLine
    {
        public RunFailedLine(string label, string value, bool emphasis = false)
        {
            Label = label;
            Value = value;
            Emphasis = emphasis;
        }

        public string Label { get; }
        public string Value { get; }
        /// <summary>Drawn in the danger accent (the loss lines).</summary>
        public bool Emphasis { get; }
    }

    /// <summary>
    /// The Death / Run Failed screen. It shows the <see cref="ExpeditionSummary"/> the expedition's own failure
    /// transaction produced — depth, biome, rooms, enemies, boss, what was lost, what was kept — and offers exactly two
    /// ways out: RETURN TO SHELTER and MAIN MENU. It never decides the loss: the owner shows it from the
    /// expedition-ended callback after <c>Fail()</c> closed the run, only for a conclusive failure (solo death, co-op
    /// wipe), never while a co-op player is merely downed and revivable. Shown at most once per run; each choice
    /// resolves at most once. While it is up the gameplay input gate is held and (solo) the world stays paused, so no
    /// click or key reaches the dead player's weapons.
    /// </summary>
    public sealed class RunFailedViewModel : IDisposable
    {
        public const string TitleText = "RUN LOST";
        public const string ReturnToShelterLabel = "RETURN TO SHELTER";
        public const string MainMenuLabel = "MAIN MENU";
        /// <summary>The most rows <see cref="BuildLines"/> ever produces (the screen reserves this height).</summary>
        public const int MaxLines = 10;

        private readonly Action _returnToShelter;
        private readonly Action _mainMenu;
        private IWorldPause _pause;
        private bool _isCoop;
        private bool _holdingInput;
        private bool _disposed;

        public RunFailedViewModel(Action returnToShelter, Action mainMenu)
        {
            _returnToShelter = returnToShelter;
            _mainMenu = mainMenu;
        }

        public bool IsOpen { get; private set; }
        public int Shows { get; private set; }
        public ExpeditionSummary Summary { get; private set; }
        public RunFailedChoice Choice { get; private set; } = RunFailedChoice.None;
        public bool IsResolved => Choice != RunFailedChoice.None;
        public string Title => TitleText;
        public string Subtitle { get; private set; } = string.Empty;
        public IReadOnlyList<RunFailedLine> Lines { get; private set; } = Array.Empty<RunFailedLine>();

        public event Action Changed;

        /// <summary>Solo pauses through the world pause; co-op never pauses (the others are still playing).</summary>
        public void ConfigurePause(IWorldPause pause, bool isCoop)
        {
            _pause = pause;
            _isCoop = isCoop;
        }

        /// <summary>Shows the screen for a closed, failed expedition; a second call for the same run (or a success) changes nothing.</summary>
        public bool Show(ExpeditionSummary summary)
        {
            if (_disposed || IsOpen || IsResolved || summary == null || summary.IsSuccess) return false;
            Summary = summary;
            Lines = BuildLines(summary);
            Subtitle = BuildSubtitle(summary);
            IsOpen = true;
            Shows++;
            if (!_isCoop) _pause?.Pause();
            Core.Input.GameplayInputGate.Hold();
            _holdingInput = true;
            Core.Rendering.UiSoundBus.Raise(Core.Rendering.UiSound.Failure);
            Changed?.Invoke();
            return true;
        }

        public void ReturnToShelter() => Resolve(RunFailedChoice.ReturnToShelter, _returnToShelter);

        public void MainMenu() => Resolve(RunFailedChoice.MainMenu, _mainMenu);

        private void Resolve(RunFailedChoice choice, Action action)
        {
            if (!IsOpen || IsResolved) return;
            Choice = choice;
            Core.Rendering.UiSoundBus.Raise(Core.Rendering.UiSound.Confirm);
            Changed?.Invoke();
            action?.Invoke();
        }

        /// <summary>The figures, in display order, from tracked stats only — nothing is invented.</summary>
        public static List<RunFailedLine> BuildLines(ExpeditionSummary summary)
        {
            var biome = summary.Biomes != null && summary.Biomes.Count > 0 ? summary.Biomes[summary.Biomes.Count - 1] : (Biome?)null;
            var lines = new List<RunFailedLine>
            {
                new("DEPTH REACHED", summary.DepthReached.ToString()),
                new("BIOME", biome.HasValue ? HudSnapshot.BiomeName(biome.Value) : "—"),
                new("ROOMS CLEARED", summary.RoomsCleared.ToString()),
                new("ENEMIES DEFEATED", summary.EnemiesDefeated.ToString()),
                new("ELITES DEFEATED", summary.ElitesDefeated.ToString()),
                new("BOSS DEFEATED", summary.BossesDefeated > 0 ? "YES" : "NO"),
                new("CARRIED COINS LOST", summary.CoinsLost.ToString(), emphasis: true),
                new("ITEMS LOST", (summary.LostItems?.Count ?? 0).ToString(), emphasis: true),
                new("XP EARNED (KEPT)", summary.XpEarned.ToString()),
                // The record survives a failed run: the depth was still reached, and saying so is the one piece of
                // progress a wipe leaves intact. Emphasised only when it was actually beaten.
                summary.IsNewPersonalBest
                    ? new RunFailedLine("DEEPEST DEPTH", $"{summary.DeepestDepthReached} — NEW PERSONAL BEST", emphasis: true)
                    : new RunFailedLine("DEEPEST DEPTH", summary.DeepestDepthReached.ToString())
            };
            if (summary.LevelAfter != summary.LevelBefore) lines.Add(new RunFailedLine("LEVEL", $"{summary.LevelBefore} → {summary.LevelAfter}"));
            return lines;
        }

        private static string BuildSubtitle(ExpeditionSummary summary) => "Everything carried is gone. XP, banked Coins and Storage are safe.";

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (IsOpen && !_isCoop) _pause?.Resume();
            if (_holdingInput) { Core.Input.GameplayInputGate.Release(); _holdingInput = false; }
            IsOpen = false;
        }
    }
}

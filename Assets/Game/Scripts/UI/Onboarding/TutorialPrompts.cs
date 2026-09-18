using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core.Input;

namespace RuinRail.UI.Onboarding
{
    /// <summary>The contextual first-time prompts of ui/95 (taught only when relevant).</summary>
    public enum TutorialPromptId
    {
        MoveAim,
        Fire,
        Dash,
        PickupInventory,
        Reload,
        WeaponSwap,
        Consumable,
        Transit
    }

    public enum InputScheme
    {
        KeyboardMouse,
        Gamepad
    }

    /// <summary>Where "seen" lives (the gameplay save through the Base session; memory in tests) and whether prompts are enabled (settings).</summary>
    public interface ITutorialProgress
    {
        bool PromptsEnabled { get; }
        bool IsSeen(TutorialPromptId id);
        void MarkSeen(TutorialPromptId id);
        void ResetSeen();
    }

    public sealed class InMemoryTutorialProgress : ITutorialProgress
    {
        private readonly HashSet<TutorialPromptId> _seen = new();
        public bool PromptsEnabled { get; set; } = true;
        public IReadOnlyCollection<TutorialPromptId> Seen => _seen;
        public bool IsSeen(TutorialPromptId id) => _seen.Contains(id);
        public void MarkSeen(TutorialPromptId id) => _seen.Add(id);
        public void ResetSeen() => _seen.Clear();
    }

    /// <summary>Control text for an action name ("Space", "B", "LMB", "RT") for the current device; rebinds are reflected when built over a live rebinder.</summary>
    public interface IInputGlyphs
    {
        InputScheme Scheme { get; }
        string For(string actionName, string part = null);
    }

    /// <summary>Glyph text from an <see cref="InputRebinder"/> (effective, possibly rebound paths) or from the approved 116 defaults when none is given.</summary>
    public sealed class SchemeGlyphs : IInputGlyphs
    {
        private static readonly Dictionary<string, string> KeyboardDefaults = new()
        {
            { "Move", "WASD" }, { "Aim", "Mouse" }, { "Fire", "LMB" }, { "Special", "RMB" }, { "Dash", "Space" }, { "Interact", "E" }, { "Reload", "R" },
            { "Weapon1", "1" }, { "Weapon2", "2" }, { "WeaponSwap", "Mouse Wheel" }, { "Consumable", "G" }, { "Inventory", "Tab" }, { "Pause", "Esc" }
        };

        private static readonly Dictionary<string, string> GamepadDefaults = new()
        {
            { "Move", "Left Stick" }, { "Aim", "Right Stick" }, { "Fire", "RT" }, { "Special", "LT" }, { "Dash", "B" }, { "Interact", "A" }, { "Reload", "X" },
            { "Weapon1", "D-Pad Left" }, { "Weapon2", "D-Pad Right" }, { "WeaponSwap", "Y" }, { "Consumable", "RB" }, { "Inventory", "View" }, { "Pause", "Menu" }
        };

        private readonly InputRebinder _rebinder;

        public SchemeGlyphs(InputScheme scheme, InputRebinder rebinder = null)
        {
            Scheme = scheme;
            _rebinder = rebinder;
        }

        public InputScheme Scheme { get; private set; }

        public void SetScheme(InputScheme scheme) => Scheme = scheme;

        public string For(string actionName, string part = null)
        {
            var schemeName = Scheme == InputScheme.Gamepad ? InputRebinder.GamepadScheme : InputRebinder.KeyboardMouseScheme;
            if (_rebinder != null)
            {
                if (actionName == "Move" && Scheme == InputScheme.KeyboardMouse && part == null)
                {
                    var parts = new[] { "up", "left", "down", "right" }.Select(p => _rebinder.Find("Move", schemeName, p)).Where(e => e != null).ToList();
                    if (parts.Count == 4) return string.Concat(parts.Select(e => e.DisplayText));
                }

                var entry = _rebinder.Find(actionName, schemeName, part);
                if (entry != null && entry.IsRebindable) return entry.DisplayText;
            }

            var table = Scheme == InputScheme.Gamepad ? GamepadDefaults : KeyboardDefaults;
            return table.TryGetValue(actionName, out var text) ? text : actionName;
        }
    }

    /// <summary>Prompt copy (ui/95) with control tokens resolved for the current device.</summary>
    public static class TutorialPromptText
    {
        public static string Build(TutorialPromptId id, IInputGlyphs glyphs)
        {
            string G(string action) => glyphs?.For(action) ?? action;
            return id switch
            {
                TutorialPromptId.MoveAim => $"Move with {G("Move")}. Aim in any direction with {G("Aim")}.",
                TutorialPromptId.Fire => $"{G("Fire")}: fire your weapon.",
                TutorialPromptId.Dash => $"Enemy attack incoming — {G("Dash")}: Dash through it.",
                TutorialPromptId.PickupInventory => $"Loot dropped. {G("Interact")}: pick it up. {G("Inventory")}: open your inventory.",
                TutorialPromptId.Reload => $"Magazine empty — {G("Reload")}: reload.",
                TutorialPromptId.WeaponSwap => $"Two weapons equipped — {G("WeaponSwap")}: swap weapons.",
                TutorialPromptId.Consumable => $"Low health — {G("Consumable")}: use your active consumable.",
                TutorialPromptId.Transit => "Boss defeated. At the Transit: Return to the Shelter to secure your loot, or Descend deeper for more.",
                _ => id.ToString()
            };
        }
    }

    /// <summary>
    /// One prompt at a time, shown only when its context arises, never again once completed or dismissed (persisted
    /// through <see cref="ITutorialProgress"/>), silent when prompts are disabled in Settings. Purely local: in co-op a
    /// new player's prompts never touch other players or the authoritative simulation.
    /// </summary>
    public sealed class TutorialPromptService
    {
        private readonly ITutorialProgress _progress;
        private readonly IInputGlyphs _glyphs;
        private readonly List<TutorialPromptId> _queue = new();

        public TutorialPromptService(ITutorialProgress progress, IInputGlyphs glyphs)
        {
            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
            _glyphs = glyphs;
        }

        public TutorialPromptId? Active { get; private set; }
        public string ActiveText => Active.HasValue ? TutorialPromptText.Build(Active.Value, _glyphs) : string.Empty;
        public IReadOnlyList<TutorialPromptId> Queued => _queue;
        public int Shown { get; private set; }
        public bool IsEnabled => _progress.PromptsEnabled;

        public event Action Changed;

        public bool IsSeen(TutorialPromptId id) => _progress.IsSeen(id);

        /// <summary>The context for <paramref name="id"/> arose. Shown now, queued behind the active prompt, or ignored when seen/disabled.</summary>
        public bool Trigger(TutorialPromptId id)
        {
            if (!_progress.PromptsEnabled || _progress.IsSeen(id)) return false;
            if (Active == id || _queue.Contains(id)) return false;
            if (Active == null)
            {
                Active = id;
                Shown++;
            }
            else
            {
                _queue.Add(id);
            }

            Changed?.Invoke();
            return true;
        }

        /// <summary>The player performed the taught action: the prompt is done for good.</summary>
        public void Complete(TutorialPromptId id) => Finish(id);

        /// <summary>Closed without doing it (still counts as seen; experienced players are never nagged).</summary>
        public void Dismiss(TutorialPromptId id) => Finish(id);

        public void DismissActive()
        {
            if (Active.HasValue) Finish(Active.Value);
        }

        private void Finish(TutorialPromptId id)
        {
            var wasRelevant = Active == id || _queue.Remove(id);
            if (!_progress.IsSeen(id)) _progress.MarkSeen(id);
            if (Active == id)
            {
                Active = null;
                while (_queue.Count > 0 && Active == null)
                {
                    var next = _queue[0];
                    _queue.RemoveAt(0);
                    if (_progress.IsSeen(next) || !_progress.PromptsEnabled) continue;
                    Active = next;
                    Shown++;
                }
            }

            if (wasRelevant) Changed?.Invoke();
        }
    }
}

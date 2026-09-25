using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.InputSystem;

namespace RuinRail.Core.Input
{
    public enum RebindOutcome
    {
        Applied,
        /// <summary>The control belonged to another action of the same scheme; the two controls were exchanged.</summary>
        Swapped,
        /// <summary>The control belongs to another action of the same scheme and no swap was requested. Nothing changed.</summary>
        Conflict,
        /// <summary>Escape / Menu and OS keys are never assignable (116: Pause stays reachable).</summary>
        Reserved,
        /// <summary>Not a button-like control (pointer position, sticks, unknown path). Nothing changed.</summary>
        Impossible,
        /// <summary>The control is not on a device of the entry's control scheme. Nothing changed.</summary>
        WrongDevice,
        NotRebindable,
        Cancelled,
        /// <summary>The same control it already had. Nothing changed.</summary>
        Unchanged
    }

    public readonly struct RebindResult
    {
        public RebindResult(RebindOutcome outcome, string conflictingAction = null)
        {
            Outcome = outcome;
            ConflictingAction = conflictingAction ?? string.Empty;
        }

        public RebindOutcome Outcome { get; }
        public string ConflictingAction { get; }
        public bool Changed => Outcome == RebindOutcome.Applied || Outcome == RebindOutcome.Swapped;
    }

    /// <summary>One binding slot of one action in one control scheme (Move has four keyboard parts).</summary>
    public sealed class RebindEntry
    {
        internal RebindEntry(InputAction action, int bindingIndex, string scheme, string label, bool isRebindable)
        {
            Action = action;
            BindingIndex = bindingIndex;
            Scheme = scheme;
            Label = label;
            IsRebindable = isRebindable;
        }

        public InputAction Action { get; }
        public string ActionName => Action.name;
        /// <summary>Index into <see cref="InputAction.bindings"/>.</summary>
        public int BindingIndex { get; }
        public string Scheme { get; }
        /// <summary>UI label, e.g. "Dash" or "Move Up".</summary>
        public string Label { get; }
        public bool IsRebindable { get; }
        public InputBinding Binding => Action.bindings[BindingIndex];
        public string EffectivePath => Binding.effectivePath ?? string.Empty;
        public string DefaultPath => Binding.path ?? string.Empty;
        public bool IsOverridden => Binding.overridePath != null;
        public string DisplayText => InputRebinder.HumanReadable(EffectivePath);
    }

    /// <summary>
    /// Rebinding over the Player action map (116): keyboard/mouse and gamepad entries, per-scheme conflict detection
    /// with an explicit swap, reserved and impossible controls refused (never bound silently), per-entry and full reset
    /// to the approved defaults, and the override document for settings persistence. Interactive listening wraps the
    /// Input System operation and routes its result through the same validation as <see cref="TryBind"/>.
    /// </summary>
    public sealed class InputRebinder : IDisposable
    {
        public const string KeyboardMouseScheme = "Keyboard&Mouse";
        public const string GamepadScheme = "Gamepad";
        public const string PauseActionName = "Pause";
        public const string CancelControlPath = "<Keyboard>/escape";

        private static readonly string[] ReservedPaths =
        {
            "<Keyboard>/escape", "<Gamepad>/start", "<Keyboard>/leftMeta", "<Keyboard>/rightMeta", "<Keyboard>/printScreen", "<Keyboard>/anyKey"
        };

        private static readonly Dictionary<string, string> ActionLabels = new()
        {
            { "Move", "Move" }, { "Aim", "Aim" }, { "Fire", "Primary Attack" }, { "Special", "Legendary Special" }, { "Dash", "Dash" },
            { "Reload", "Reload" }, { "Interact", "Interact" }, { "Weapon1", "Select Primary Weapon" }, { "Weapon2", "Select Secondary Weapon" },
            { "WeaponSwap", "Swap Weapon" }, { "Consumable", "Use Active Consumable" }, { "QuickGrenade", "Quick Grenade" },
            { "Inventory", "Inventory" }, { PauseActionName, "Pause" }
        };

        private readonly InputActionAsset _asset;
        private readonly InputActionMap _map;
        private readonly List<RebindEntry> _entries = new();
        private InputActionRebindingExtensions.RebindingOperation _operation;
        private string _preListenOverride;

        public InputRebinder(InputActionAsset asset)
        {
            _asset = asset ?? throw new ArgumentNullException(nameof(asset));
            _map = asset.FindActionMap("Player", throwIfNotFound: true);
            foreach (var action in _map.actions)
            {
                var bindings = action.bindings;
                for (var i = 0; i < bindings.Count; i++)
                {
                    var binding = bindings[i];
                    if (binding.isComposite) continue;
                    var scheme = binding.groups ?? string.Empty;
                    var label = ActionLabels.TryGetValue(action.name, out var l) ? l : action.name;
                    if (binding.isPartOfComposite) label += " " + Capitalize(binding.name);
                    var rebindable = action.name != PauseActionName && (binding.isPartOfComposite || action.expectedControlType == "Button");
                    _entries.Add(new RebindEntry(action, i, scheme, label, rebindable));
                }
            }
        }

        public IReadOnlyList<RebindEntry> Entries => _entries;
        public IEnumerable<RebindEntry> EntriesFor(string scheme) => _entries.Where(e => e.Scheme == scheme);
        public RebindEntry Find(string actionName, string scheme, string partName = null) =>
            _entries.FirstOrDefault(e => e.ActionName == actionName && e.Scheme == scheme && (partName == null || string.Equals(e.Binding.name, partName, StringComparison.OrdinalIgnoreCase)));

        public bool HasOverrides => _entries.Any(e => e.IsOverridden);
        public string OverridesJson => _asset.SaveBindingOverridesAsJson();
        public bool IsListening => _operation != null;
        public RebindEntry ListeningEntry { get; private set; }
        public RebindResult LastResult { get; private set; } = new(RebindOutcome.Unchanged);

        public event Action Changed;

        // ---- Validation (pure; no devices needed) ----

        public static bool IsReserved(string controlPath) => ReservedPaths.Any(p => string.Equals(p, controlPath, StringComparison.OrdinalIgnoreCase));

        public static bool IsButtonLike(string controlPath)
        {
            if (string.IsNullOrWhiteSpace(controlPath)) return false;
            var layout = InputControlPath.TryGetControlLayout(controlPath);
            if (string.IsNullOrEmpty(layout)) return false;
            return InputSystem.IsFirstLayoutBasedOnSecond(layout, "Button");
        }

        public static bool BelongsToScheme(string controlPath, string scheme)
        {
            var device = InputControlPath.TryGetDeviceLayout(controlPath);
            if (string.IsNullOrEmpty(device)) return false;
            return scheme switch
            {
                KeyboardMouseScheme => InputSystem.IsFirstLayoutBasedOnSecond(device, "Keyboard") || InputSystem.IsFirstLayoutBasedOnSecond(device, "Mouse"),
                GamepadScheme => InputSystem.IsFirstLayoutBasedOnSecond(device, "Gamepad"),
                _ => false
            };
        }

        /// <summary>Rewrites a concrete device path ("&lt;XInputController&gt;/buttonSouth") to its scheme's generic layout so overrides survive a controller change.</summary>
        public static string Generalize(string controlPath)
        {
            var device = InputControlPath.TryGetDeviceLayout(controlPath);
            if (string.IsNullOrEmpty(device)) return controlPath;
            var generic = InputSystem.IsFirstLayoutBasedOnSecond(device, "Gamepad") ? "Gamepad"
                : InputSystem.IsFirstLayoutBasedOnSecond(device, "Keyboard") ? "Keyboard"
                : InputSystem.IsFirstLayoutBasedOnSecond(device, "Mouse") ? "Mouse" : device;
            var slash = controlPath.IndexOf('/');
            return slash < 0 ? controlPath : "<" + generic + ">" + controlPath.Substring(slash);
        }

        public static string HumanReadable(string controlPath) =>
            string.IsNullOrEmpty(controlPath) ? "—" : InputControlPath.ToHumanReadableString(controlPath, InputControlPath.HumanReadableStringOptions.OmitDevice | InputControlPath.HumanReadableStringOptions.UseShortNames);

        /// <summary>Classifies a candidate without changing anything.</summary>
        public RebindResult Validate(RebindEntry entry, string controlPath)
        {
            if (entry == null || !entry.IsRebindable) return new RebindResult(RebindOutcome.NotRebindable);
            if (string.IsNullOrWhiteSpace(controlPath)) return new RebindResult(RebindOutcome.Cancelled);
            controlPath = Generalize(controlPath);
            if (IsReserved(controlPath)) return new RebindResult(RebindOutcome.Reserved);
            if (!BelongsToScheme(controlPath, entry.Scheme)) return new RebindResult(RebindOutcome.WrongDevice);
            if (!IsButtonLike(controlPath)) return new RebindResult(RebindOutcome.Impossible);
            if (PathsEqual(entry.EffectivePath, controlPath)) return new RebindResult(RebindOutcome.Unchanged);
            var conflict = ConflictOf(entry, controlPath);
            return conflict != null ? new RebindResult(RebindOutcome.Conflict, conflict.Label) : new RebindResult(RebindOutcome.Applied);
        }

        public RebindEntry ConflictOf(RebindEntry entry, string controlPath) =>
            _entries.FirstOrDefault(e => e != entry && e.Scheme == entry.Scheme && PathsEqual(e.EffectivePath, controlPath));

        // ---- Mutation ----

        /// <summary>Binds <paramref name="controlPath"/> to the entry; a conflict is refused unless <paramref name="swapOnConflict"/>, in which case the other action takes this entry's previous control so nothing is left unbound.</summary>
        public RebindResult TryBind(RebindEntry entry, string controlPath, bool swapOnConflict = false)
        {
            var verdict = Validate(entry, controlPath);
            if (verdict.Outcome != RebindOutcome.Applied && verdict.Outcome != RebindOutcome.Conflict) return Record(verdict);
            controlPath = Generalize(controlPath);
            if (verdict.Outcome == RebindOutcome.Conflict)
            {
                if (!swapOnConflict) return Record(verdict);
                var other = ConflictOf(entry, controlPath);
                var previous = entry.EffectivePath;
                Override(other, previous);
                Override(entry, controlPath);
                return Record(new RebindResult(RebindOutcome.Swapped, other.Label));
            }

            Override(entry, controlPath);
            return Record(new RebindResult(RebindOutcome.Applied));
        }

        public void ResetEntry(RebindEntry entry)
        {
            if (entry == null || !entry.IsOverridden) return;
            entry.Action.RemoveBindingOverride(entry.BindingIndex);
            Changed?.Invoke();
        }

        /// <summary>Restores every approved default (116) for both schemes.</summary>
        public void ResetAll()
        {
            CancelListening();
            _asset.RemoveAllBindingOverrides();
            Changed?.Invoke();
        }

        /// <summary>Replaces all overrides with a persisted document (empty = defaults).</summary>
        public void LoadOverrides(string json)
        {
            CancelListening();
            _asset.RemoveAllBindingOverrides();
            if (!string.IsNullOrWhiteSpace(json)) _asset.LoadBindingOverridesFromJson(json);
            Changed?.Invoke();
        }

        // ---- Interactive listening (devices) ----

        /// <summary>Starts listening for a control on the entry's scheme devices. Escape cancels; the captured control goes through <see cref="TryBind"/>.</summary>
        public bool BeginListening(RebindEntry entry, bool swapOnConflict = false)
        {
            if (entry == null || !entry.IsRebindable || IsListening) return false;
            ListeningEntry = entry;
            _preListenOverride = entry.Binding.overridePath;
            var action = entry.Action;
            var wasEnabled = action.enabled;
            action.Disable();
            var op = action.PerformInteractiveRebinding(entry.BindingIndex)
                .WithCancelingThrough(CancelControlPath)
                .WithExpectedControlType("Button")
                .OnMatchWaitForAnother(0.1f);
            if (entry.Scheme == GamepadScheme) op.WithControlsHavingToMatchPath("<Gamepad>");
            else op.WithControlsHavingToMatchPath("<Keyboard>").WithControlsHavingToMatchPath("<Mouse>");
            op.OnCancel(_ => FinishListening(action, wasEnabled, null, swapOnConflict))
              .OnComplete(o => FinishListening(action, wasEnabled, o.selectedControl?.path, swapOnConflict));
            _operation = op;
            op.Start();
            return true;
        }

        public void CancelListening()
        {
            if (_operation == null) return;
            _operation.Cancel();
        }

        private void FinishListening(InputAction action, bool reenable, string capturedPath, bool swapOnConflict)
        {
            var entry = ListeningEntry;
            var op = _operation;
            _operation = null;
            ListeningEntry = null;
            // The operation writes the raw captured path itself; restore the pre-listen state and let validation decide.
            if (entry != null)
            {
                if (_preListenOverride != null) action.ApplyBindingOverride(entry.BindingIndex, _preListenOverride);
                else action.RemoveBindingOverride(entry.BindingIndex);
                if (capturedPath == null) Record(new RebindResult(RebindOutcome.Cancelled));
                else TryBind(entry, capturedPath, swapOnConflict);
            }

            op?.Dispose();
            if (reenable) action.Enable();
        }

        public void Dispose()
        {
            CancelListening();
        }

        private void Override(RebindEntry entry, string path)
        {
            if (PathsEqual(entry.DefaultPath, path)) entry.Action.RemoveBindingOverride(entry.BindingIndex);
            else entry.Action.ApplyBindingOverride(entry.BindingIndex, path);
        }

        private RebindResult Record(RebindResult result)
        {
            LastResult = result;
            if (result.Changed) Changed?.Invoke();
            return result;
        }

        private static bool PathsEqual(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}

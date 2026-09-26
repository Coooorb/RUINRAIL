using System;
using System.Linq;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.Persistence;
using RuinRail.UI.Base;

namespace RuinRail.UI.Onboarding
{
    /// <summary>Tutorial "seen" state in the gameplay save (ui/95), enabled flag from the settings document; every change is an autosave reason.</summary>
    public sealed class SaveSlotTutorialProgress : ITutorialProgress
    {
        private readonly SaveSlot _slot;
        private readonly AutosaveService _autosave;
        private readonly Func<bool> _enabled;

        public SaveSlotTutorialProgress(SaveSlot slot, AutosaveService autosave, Func<bool> promptsEnabled)
        {
            _slot = slot ?? throw new ArgumentNullException(nameof(slot));
            _autosave = autosave;
            _enabled = promptsEnabled;
            _slot.FirstLaunch ??= new FirstLaunchFlags();
            _slot.FirstLaunch.TutorialPromptsSeen ??= new System.Collections.Generic.List<string>();
        }

        public bool PromptsEnabled => _enabled?.Invoke() ?? true;
        public bool IsSeen(TutorialPromptId id) => _slot.FirstLaunch.TutorialPromptsSeen.Contains(id.ToString());

        public void MarkSeen(TutorialPromptId id)
        {
            if (IsSeen(id)) return;
            _slot.FirstLaunch.TutorialPromptsSeen.Add(id.ToString());
            _autosave?.MarkDirty("tutorial_seen");
        }

        /// <summary>Settings "reset tutorials": the seen list only; nothing else in the save changes.</summary>
        public void ResetSeen()
        {
            if (_slot.FirstLaunch.TutorialPromptsSeen.Count == 0) return;
            _slot.FirstLaunch.TutorialPromptsSeen.Clear();
            _autosave?.MarkDirty("tutorial_reset");
        }
    }

    public enum ShelterOnboardingStep
    {
        ChooseDisplayName,
        ReceiveStarterKit,
        EquipGear,
        StartFirstExpedition,
        Complete
    }

    /// <summary>
    /// ui/95 first launch: 1. choose display name, 2. receive the starter kit, 3. equip Primary Weapon and Armor,
    /// 4. start the first expedition. Runs over the existing DisplayNameService / StarterKitService / Base loadout /
    /// ExpeditionService — nothing is granted or validated here a second time, so every step stays idempotent.
    /// Existing profiles (kit granted before this session, or any expedition ended) are marked complete on open and
    /// only ever see the name step when a name was never confirmed.
    /// </summary>
    public sealed class ShelterOnboardingViewModel : IDisposable
    {
        private readonly BaseSession _session;
        private readonly DisplayNameService _names;
        private readonly DisplayNamePolicy _policy;
        private bool _kitAcknowledged;

        public ShelterOnboardingViewModel(BaseSession session, DisplayNamePolicy policy)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _policy = policy;
            _names = policy != null ? new DisplayNameService(session.Slot, policy) : null;
            var flags = session.Slot.FirstLaunch ??= new FirstLaunchFlags();
            var existingProfile = !session.GrantedFirstKit && session.Profile.StarterKitGranted || session.Profile.ExpeditionsEnded > 0;
            if (!flags.ShelterOnboardingComplete && existingProfile)
            {
                flags.ShelterOnboardingComplete = true;
                session.Autosave.MarkDirty("onboarding_existing_profile");
            }

            _session.Loadout.EquippedChanged += OnEquippedChanged;
            _session.Expedition.ExpeditionStarted += OnExpeditionStarted;
            Refresh();
        }

        public ShelterOnboardingStep Step { get; private set; }
        public bool IsComplete => Step == ShelterOnboardingStep.Complete;
        public string PromptText { get; private set; } = string.Empty;
        public string NameError { get; private set; } = string.Empty;
        public DisplayNameError LastNameError { get; private set; }
        public string DisplayName => _session.Profile.DisplayName;
        public int NameMinLength => _policy != null ? _policy.MinLength : 0;
        public int NameMaxLength => _policy != null ? _policy.MaxLength : 0;
        public bool NeedsDisplayName => _names != null && _names.NeedsDisplayName;
        public bool IsGearEquipped => _session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon) != null && _session.Loadout.GetEquipped(EquippedSlot.Armor) != null;

        public event Action Changed;

        /// <summary>Raised with the saved (normalised) name after every accepted first-launch name or rename.</summary>
        public event Action<string> DisplayNameChanged;

        /// <summary>Step 1: validated through the approved policy; invalid input changes nothing and reports the reason.</summary>
        public bool SubmitDisplayName(string raw)
        {
            if (_names == null) return false;
            var result = _names.TrySet(raw);
            LastNameError = result.Error;
            if (!result.IsValid)
            {
                NameError = result.Error.ToString();
                Changed?.Invoke();
                return false;
            }

            NameError = string.Empty;
            _session.Autosave.MarkDirty("display_name");
            _session.Autosave.Flush();
            Refresh();
            DisplayNameChanged?.Invoke(result.Normalized);
            return true;
        }

        /// <summary>Step 2: the kit was granted once by the session; this only closes the message.</summary>
        public void AcknowledgeStarterKit()
        {
            _kitAcknowledged = true;
            Refresh();
        }

        public void Refresh()
        {
            var flags = _session.Slot.FirstLaunch;
            if (NeedsDisplayName) Step = ShelterOnboardingStep.ChooseDisplayName;
            else if (flags.ShelterOnboardingComplete) Step = ShelterOnboardingStep.Complete;
            else if (_session.GrantedFirstKit && !_kitAcknowledged) Step = ShelterOnboardingStep.ReceiveStarterKit;
            else if (!IsGearEquipped) Step = ShelterOnboardingStep.EquipGear;
            else Step = ShelterOnboardingStep.StartFirstExpedition;

            PromptText = Step switch
            {
                ShelterOnboardingStep.ChooseDisplayName => "Choose your display name: open CHARACTER and pick CHANGE NAME.",
                ShelterOnboardingStep.ReceiveStarterKit => "Your free Starter Kit is in your loadout: " + KitSummary() + ".",
                ShelterOnboardingStep.EquipGear => "Equip a Primary Weapon and Armor at the Loadout station before you leave.",
                ShelterOnboardingStep.StartFirstExpedition => "Head to the Transit station and start your first expedition.",
                _ => string.Empty
            };
            Changed?.Invoke();
        }

        private string KitSummary()
        {
            var kit = StarterKitService.CreateKit().Select(k => _session.Configs.Resolve(k.item.DefinitionId)?.DisplayName ?? k.item.DefinitionId).Distinct();
            return string.Join(", ", kit);
        }

        private void OnEquippedChanged(EquippedSlot slot, ItemInstance item) => Refresh();

        private void OnExpeditionStarted(ExpeditionState state)
        {
            var flags = _session.Slot.FirstLaunch;
            if (!flags.ShelterOnboardingComplete)
            {
                flags.ShelterOnboardingComplete = true;
                _session.Autosave.MarkDirty("onboarding_complete");
            }

            Refresh();
        }

        public void Dispose()
        {
            _session.Loadout.EquippedChanged -= OnEquippedChanged;
            _session.Expedition.ExpeditionStarted -= OnExpeditionStarted;
        }
    }

    /// <summary>
    /// The display-name field (player/10: "changed later from profile/settings UI using the same validation"). Holds
    /// the text being edited and commits it through <see cref="ShelterOnboardingViewModel.SubmitDisplayName"/>, the one
    /// path that validates, stores and autosaves the name, so first launch and later renames cannot diverge.
    ///
    /// Typing is filtered to the allowed character set and capped at the policy maximum, so the field can never hold a
    /// string the HUD was not sized for. A controller edits the last character in place (Cycle) and appends/removes
    /// characters; a keyboard types. An invalid commit (empty, too short, blocked) keeps the saved name and says why.
    /// </summary>
    public sealed class DisplayNameEntry
    {
        /// <summary>The controller's character wheel: exactly the characters the validator accepts.</summary>
        public const string Charset = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 _-";

        private readonly ShelterOnboardingViewModel _onboarding;
        private readonly Func<string> _blockedReason;

        public DisplayNameEntry(ShelterOnboardingViewModel onboarding, Func<string> blockedReason = null)
        {
            _onboarding = onboarding ?? throw new ArgumentNullException(nameof(onboarding));
            _blockedReason = blockedReason;
        }

        public bool IsOpen { get; private set; }
        public string Text { get; private set; } = string.Empty;
        public string Error { get; private set; } = string.Empty;
        public int MaxLength => _onboarding.NameMaxLength;
        public int MinLength => _onboarding.NameMinLength;

        /// <summary>Why the name cannot be changed right now (a live co-op session fixed it at connect), or null.</summary>
        public string BlockedReason => _blockedReason?.Invoke();

        public event Action Changed;

        /// <summary>Opens the field on the saved name. Refused (false) while <see cref="BlockedReason"/> applies.</summary>
        public bool Open()
        {
            if (MaxLength <= 0 || !string.IsNullOrEmpty(BlockedReason)) return false;
            IsOpen = true;
            Text = Clamp(_onboarding.DisplayName);
            Error = string.Empty;
            Changed?.Invoke();
            return true;
        }

        public void Type(string typed)
        {
            if (!IsOpen || string.IsNullOrEmpty(typed)) return;
            var text = Text;
            foreach (var c in typed)
            {
                if (text.Length >= MaxLength) break;
                if (DisplayNameValidator.IsAllowedCharacter(c)) text += c;
            }

            Set(text);
        }

        public void Backspace()
        {
            if (!IsOpen || Text.Length == 0) return;
            Set(Text.Substring(0, Text.Length - 1));
        }

        /// <summary>Controller: adds a character (a copy of the last one, so neighbouring letters are quick to reach).</summary>
        public void AddCharacter()
        {
            if (!IsOpen || Text.Length >= MaxLength) return;
            var last = Text.Length > 0 ? Text[Text.Length - 1] : 'A';
            Set(Text + (last == ' ' ? 'A' : last));
        }

        /// <summary>Controller: steps the last character through <see cref="Charset"/> (an empty field starts one).</summary>
        public void Cycle(int delta)
        {
            if (!IsOpen || delta == 0) return;
            if (Text.Length == 0) { Set("A"); return; }
            var last = Text[Text.Length - 1];
            var index = Charset.IndexOf(last);
            var next = Charset[((index < 0 ? 0 : index) + delta % Charset.Length + Charset.Length) % Charset.Length];
            Set(Text.Substring(0, Text.Length - 1) + next);
        }

        /// <summary>Saves the name. Invalid input changes nothing, keeps the field open and reports the reason.</summary>
        public bool Submit()
        {
            if (!IsOpen) return false;
            var blocked = BlockedReason;
            if (!string.IsNullOrEmpty(blocked)) { Error = blocked; Changed?.Invoke(); return false; }
            if (!_onboarding.SubmitDisplayName(Text))
            {
                Error = Describe(_onboarding.LastNameError, MinLength, MaxLength);
                Changed?.Invoke();
                return false;
            }

            IsOpen = false;
            Error = string.Empty;
            Changed?.Invoke();
            return true;
        }

        public void Cancel()
        {
            if (!IsOpen) return;
            IsOpen = false;
            Error = string.Empty;
            Changed?.Invoke();
        }

        public static string Describe(DisplayNameError error, int min, int max) => error switch
        {
            DisplayNameError.None => string.Empty,
            DisplayNameError.Empty => "Enter a name.",
            DisplayNameError.TooShort => $"At least {min} characters.",
            DisplayNameError.TooLong => $"At most {max} characters.",
            DisplayNameError.Blocked => "That name is not allowed.",
            _ => "Letters, numbers, spaces, _ and - only."
        };

        private void Set(string text)
        {
            Text = text;
            Error = string.Empty;
            Changed?.Invoke();
        }

        private string Clamp(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var text = string.Empty;
            foreach (var c in name)
                if (DisplayNameValidator.IsAllowedCharacter(c) && text.Length < MaxLength) text += c;
            return text;
        }
    }
}
